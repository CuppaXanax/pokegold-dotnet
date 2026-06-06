namespace PokeGold.Game.Scenes

open System.Collections.Generic
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Audio
open PokeGold.Game.Battle
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Render
open PokeGold.Game.Save

/// The walk-around-the-map scene. Owns the mutable overworld state plus the
/// running-script bookkeeping that turns NPC/sign interactions and coord triggers
/// into real GSC scripts: it drives the pure [`Script`] VM, enacting each
/// [`ScriptEffect`] (text box, yes/no, battle, flags, items) and resuming the VM
/// with the result. Pure commands run inline within one frame; effects that need a
/// child scene push it and suspend until it pops.
type OverworldScene(content: Content, sound: ISoundBoard, initial: OverworldState) =
    let mutable state = initial
    /// The script flag/var/scene world — mutated as scripts run; persisted in M9.5.
    let mutable world = World.empty
    /// The map's active scene name (gates coord triggers). Scene *progression* is
    /// deeper than M9, so this stays at the map's default; rival coords stay off.
    let activeScene = MapEvents.defaultScene initial.Events
    /// Coord triggers already fired this visit (fire-once).
    let mutable firedCoords: Set<int * int> = Set.empty
    /// The player's full persistent state (party, bag, dex, money, etc.).
    let mutable player: PokeGold.Game.Player.PlayerState = PlayerStateOps.initial
    /// Staged battle data for the next `startbattle` call.
    let mutable stagedWild: (string * int) option = None
    let mutable stagedTrainer: (string * string) option = None
    let mutable stagedWinText: string = ""
    let mutable stagedLossText: string = ""
    /// A suspended script awaiting the child scene we pushed for an effect.
    let mutable pending: (ScriptVm * ScriptEffect) option = None
    /// A script suspended on an `applymovement`: the VM to resume, the moved object's
    /// index in `state.Npcs`, and the live movement run. Ticked each frame until done.
    let mutable runningMove: (ScriptVm * int * MovementRunner.Run) option = None
    /// The most recent yes/no choice, written by the YesNoScene callback.
    let mutable yesNoResult = 0
    let mutable prevA = false
    let mutable prevStart = false
    /// Wild encounter RNG for the overworld trigger hook.
    let encounterRng = System.Random()
    /// The outcome of the most recent battle (set by BattleScene callback).
    let mutable lastBattleOutcome: Outcome option = None
    /// Cache of NPC sprites by SPRITE_* constant (None = no art for it).
    let spriteCache = Dictionary<string, Sprite option>()

    /// Start this map's background music as soon as the scene exists.
    do
        match OverworldScene.musicFor initial.MapId with
        | Some path -> sound.PlayMusic path
        | None -> ()

    /// The repo-relative music file for a map id: its baked `Meta.Music` song id
    /// resolved through the generated `MUSIC_* -> file` table. `None` when the map
    /// or its song isn't in the tree (e.g. `MUSIC_NONE`, or a song not yet shipped),
    /// in which case the scene simply starts no track.
    static member private musicFor(mapId: string) : string option =
        MapsData.byName mapId
        |> Option.bind (fun m -> Map.tryFind m.Meta.Music MusicData.byId)

    /// Switch the background music to the given map's track. A no-op when the map
    /// has no shipped song, and (via the audio engine's same-path guard) when the
    /// track is already playing — so walking within a single-music region doesn't
    /// restart it, while crossing into a differently-scored map does change it.
    member private _.PlayMapMusic(mapId: string) =
        match OverworldScene.musicFor mapId with
        | Some path -> sound.PlayMusic path
        | None -> ()

    /// Resolve a text label to its M5 token string. Map-local text wins; std-script
    /// text (nurse prompts, bookshelves, signs) is the fallback; an unknown label
    /// shows the label itself.
    member private _.ResolveText(label: string) : string =
        match Map.tryFind label state.Text with
        | Some s -> s
        | None ->
            match Map.tryFind label StdScriptsData.text with
            | Some s -> s
            | None -> label + "<DONE>"

    /// Add `qty` of an item to the bag.
    member private _.AddItem (item: string) (qty: int) =
        player <- { player with Bag = Bag.add item qty player.Bag }

    /// Remove up to `qty` of an item from the bag.
    member private _.RemoveItem (item: string) (qty: int) =
        player <- { player with Bag = Bag.remove item qty player.Bag }

    member private _.SyncBattleParty (battle: BattleState) =
        lastBattleOutcome <- battle.Outcome
        let statusCode (status: StatusCondition) : string =
            match status with
            | Healthy -> ""
            | Sleep _ -> "SLP"
            | Poison -> "PSN"
            | BadPoison _ -> "PSN"
            | Burn -> "BRN"
            | Freeze -> "FRZ"
            | Paralysis -> "PAR"

        let syncPartyMon (partyMon: PartyMon) (battleMon: BattleMon) : PartyMon =
            { partyMon with
                Hp = max 0 battleMon.Hp
                MaxHp = battleMon.MaxHp
                Status = statusCode battleMon.Status
                Moves =
                    List.map2 (fun (moveId, _) pp -> (moveId, pp)) partyMon.Moves battleMon.Pp }

        let syncedParty =
            player.Party
            |> List.map (fun partyMon ->
                battle.PlayerTeam
                |> List.tryFind (fun b -> b.Species.Dex = partyMon.SpeciesId && b.Level = partyMon.Level)
                |> Option.map (fun battleMon -> syncPartyMon partyMon battleMon)
                |> Option.defaultValue partyMon)

        player <- { player with Party = syncedParty }

    member private this.BuildBattle() : BattleScene =
        let playerTeam =
            player.Party
            |> List.filter (fun m -> m.Hp > 0)
            |> List.map PartyMon.toBattleMon
            |> function
                | [] -> [ BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE" ] ]
                | t -> t

        let enemyTeam =
            match stagedTrainer with
            | Some(group, id) ->
                match Trainers.lookup group (int id) with
                | Some trainer ->
                    trainer.Party
                    |> List.map (fun tm ->
                        match Map.tryFind tm.Species Species.all with
                        | Some stats -> BattleMon.ofSpecies stats tm.Level [ Moves.byName "TACKLE" ]
                        | None -> BattleMon.ofSpecies (Species.byName "PIDGEY") tm.Level [ Moves.byName "TACKLE" ])
                | None -> [ BattleMon.ofSpecies (Species.byName "PIDGEY") 5 [ Moves.byName "TACKLE" ] ]
            | None ->
                match stagedWild with
                | Some(species, level) ->
                    match Map.tryFind species Species.all with
                    | Some stats -> [ BattleMon.ofSpecies stats level [ Moves.byName "TACKLE" ] ]
                    | None -> [ BattleMon.ofSpecies (Species.byName "PIDGEY") level [ Moves.byName "TACKLE" ] ]
                | None ->
                    [ BattleMon.ofSpecies (Species.byName "PIDGEY") 3 [ Moves.byName "TACKLE" ] ]

        BattleScene(content.Font, Battle.createTeam playerTeam enemyTeam 0x1234u, fun state -> this.SyncBattleParty state)

    /// Drive the VM from a run step: enact pure/immediate effects inline (resuming
    /// at once), and for effects that need a child scene, push it and suspend.
    member private this.Drive(step: ScriptStep) : Transition =
        world <- step.World

        match step.Outcome with
        | Completed -> Stay
        | Suspended(vm, effect) ->
            match effect with
            // ----- effects that push a child scene and suspend -----
            | ShowText(label, _faceFirst) ->
                pending <- Some(vm, effect)
                let speed = Options.textSpeedDelay player.Options.TextSpeed
                Push(TextBoxScene.Of(content, this.ResolveText label, speed) :> Scene)
            | HallOfFame ->
                world <- World.setEvent "EVENT_BEAT_ELITE_FOUR" world
                player <- { player with Party = Heal.healParty player.Party }
                pending <- Some(vm, effect)
                let speed = Options.textSpeedDelay player.Options.TextSpeed
                Push(TextBoxScene.Of(content, "Congratulations! You are the new Champion!<DONE>", speed) :> Scene)
            | AskYesNo ->
                pending <- Some(vm, effect)
                Push(YesNoScene(content.Font, fun r -> yesNoResult <- r) :> Scene)
            | StartBattle ->
                pending <- Some(vm, effect)
                sound.PlaySfx "Sfx_Menu"
                Push(this.BuildBattle() :> Scene)
            | GiveItem(item, qty, true) ->
                this.AddItem item qty
                pending <- Some(vm, effect)
                Push(TextBoxScene.Of(content, item.Replace("_", " ") + "<DONE>") :> Scene)
            // ----- immediate effects: enact, resume this frame -----
            | GiveItem(item, qty, false) ->
                this.AddItem item qty
                this.Drive(Script.resume (Some 1) world vm)
            | TakeItem(item, qty) ->
                this.RemoveItem item qty
                this.Drive(Script.resume (Some 1) world vm)
            | CheckItem item ->
                this.Drive(Script.resume (Some(if Bag.count item player.Bag > 0 then 1 else 0)) world vm)
            | LoadWild(species, level) ->
                stagedWild <- Some(species, level)
                stagedTrainer <- None
                this.Drive(Script.resume None world vm)
            | LoadTrainer(group, id) ->
                stagedTrainer <- Some(group, id)
                stagedWild <- None
                this.Drive(Script.resume None world vm)
            | WinLossText(win, loss) ->
                stagedWinText <- win
                stagedLossText <- loss
                this.Drive(Script.resume None world vm)
            | GivePoke(species, level, item) ->
                match Map.tryFind species PokeGold.Game.Data.Species.all with
                | Some stats ->
                    let mon = PokeGold.Game.Player.PartyMon.create stats.Dex level
                    let mon = match item with Some i -> { mon with HeldItem = Some i } | None -> mon
                    let mon = PokeGold.Game.Player.MoveLearn.seedStartingMoves mon
                    if player.Party.Length < 6 then
                        player <- { player with Party = player.Party @ [ mon ] }
                        this.Drive(Script.resume (Some 1) world vm)
                    else
                        this.Drive(Script.resume (Some 0) world vm)
                | None ->
                    this.Drive(Script.resume (Some 0) world vm)
            | CheckPoke species ->
                match Map.tryFind species PokeGold.Game.Data.Species.all with
                | Some stats ->
                    let has = player.Party |> List.exists (fun m -> m.SpeciesId = stats.Dex)
                    this.Drive(Script.resume (Some(if has then 1 else 0)) world vm)
                | None ->
                    this.Drive(Script.resume (Some 0) world vm)
            | SetVisible(obj, visible) ->
                match OverworldState.objectEventOf state.MapId obj with
                | Some o ->
                    match o.EventFlag with
                    | Some flag ->
                        world <- (if visible then World.clearEvent flag world else World.setEvent flag world)
                    | None -> ()
                | None -> ()

                this.Drive(Script.resume None world vm)
            | HealParty ->
                player <- { player with Party = Heal.healParty player.Party }
                // Play the heal jingle *once*, layered over the map music. MUSIC_HEAL
                // (audio/music/healpokemon.asm) is the real GSC heal track; playing it
                // as a looping background track left it cycling forever (we don't model
                // the `playmusic MUSIC_NONE` / HealMachineAnim sequence that silences it
                // in GSC), so it is played as a self-retiring fanfare instead.
                match Map.tryFind "MUSIC_HEAL" MusicData.byId with
                | Some path -> sound.PlayJingle path
                | None -> ()
                this.Drive(Script.resume None world vm)
            | OpenMart(_martType, items) ->
                pending <- Some(vm, effect)
                Push(MartScene(content, player, _martType, items, fun p -> player <- p) :> Scene)
            | OpenPc ->
                pending <- Some(vm, effect)
                Push(PcMenuScene(content, player, fun p -> player <- p) :> Scene)
            // ----- effects out of M9.4 scope: no-op, resume -----
            | SetLastTalked _
            | FacePlayer
            | FaceObject _
            | TurnObject _
            | ReloadMap
            | ChangeBlock(_, _, _) ->
                // TODO: Apply block overlay to map collision/visual data.
                // For now, just acknowledge and continue so scripts don't break.
                this.Drive(Script.resume None world vm)
            | PlayMusic _
            | PlaySound _
            | ScriptEffect.Cry _
            | WaitSfx -> this.Drive(Script.resume None world vm)
            // ----- applymovement: animate the actor over frames, then resume -----
            | ApplyMovement(objSym, label) ->
                match this.TryStartMovement(vm, objSym, label) with
                | true -> Stay
                | false -> this.Drive(Script.resume None world vm)
            // ----- warp: load the destination map, then continue the script -----
            | ScriptEffect.Warp(map, x, y, facing) ->
                match OverworldState.tryWarpExplicit content map x y facing state.Player.Facing with
                | Some ns ->
                    state <- ns
                    firedCoords <- Set.empty
                | None -> ()

                this.Drive(Script.resume None world vm)

    /// Begin an `applymovement`: resolve the symbolic actor to a live NPC and look up
    /// its baked movement script. Returns `true` if a run was started (the scene then
    /// suspends and ticks it each frame); `false` if either couldn't be resolved, so
    /// the caller resumes the VM immediately (a faithful no-op for unmodelled actors).
    member private _.TryStartMovement(vm: ScriptVm, objSym: string, label: string) : bool =
        let walkable = MapConnections.cellWalkable state.Map state.Collision state.Neighbors

        match OverworldState.objectIndexOf state.MapId objSym, OverworldState.movementScript state.MapId label with
        | Some i, Some cmds when i >= 0 && i < state.Npcs.Length ->
            let run = MovementRunner.start walkable cmds state.Npcs.[i]
            runningMove <- Some(vm, i, run)
            true
        | _ -> false

    /// Load the Azalea Town overworld scene through the shared asset cache.
    static member Load(content: Content, sound: ISoundBoard) : OverworldScene =
        OverworldScene(content, sound, OverworldState.loadAzalea content)

    /// Restore an overworld scene from a save (position, world flags, and player state).
    static member OfSave(content: Content, sound: ISoundBoard, save: SaveData) : OverworldScene =
        let scene = OverworldScene(content, sound, SaveData.apply content save)
        scene.Restore(SaveData.worldOf save, SaveData.playerOf save)
        scene

    /// Snapshot this scene's persistable state (position, world flags, player state).
    member _.Capture() : SaveData = SaveData.captureWith state world player

    /// Seed the script world and player state onto a freshly built scene (used by OfSave).
    member _.Restore(w: World, p: PlayerState) =
        world <- w
        player <- p

    // ---- Debug inspection / mutation surface (T1 debug pipe) ----------------
    // These give the debug channel a race-free window onto the scene's private
    // mutable state. They are only ever called on the game-update thread (the
    // DebugChannel marshals every command there), so reads and writes here are
    // consistent with the running frame.

    /// The live overworld state (map, player, camera, NPCs).
    member _.DebugState: OverworldState = state

    /// The live player state (party, bag, dex, money, etc.). Exposed for menus.
    member _.Player: PlayerState = player

    /// The live script world (event/engine flags, vars, scenes).
    member _.DebugWorld: World = world

    /// The live player state (party, bag, dex, money, etc.).
    member _.DebugPlayer: PlayerState = player

    /// The live bag (item constant → quantity) — for debug console.
    member _.DebugBag: Map<string, int> = Bag.toFlat player.Bag

    /// Whether an NPC object is currently present (event-flag gated).
    member _.DebugVisible(o: ObjectEvent) : bool = MapEvents.objectVisible world o

    /// Set or clear an `EVENT_*` story flag on the live world.
    member _.DebugSetEvent (flag: string) (value: bool) =
        world <- (if value then World.setEvent flag world else World.clearEvent flag world)

    /// Write a `VAR_*` game variable on the live world.
    member _.DebugSetVar (var: string) (value: int) = world <- World.setVar var value world

    /// Teleport the player to a cell on the current map (no warp/load), settling
    /// any in-progress step and re-centering the camera.
    member _.DebugTeleport (x: int) (y: int) =
        let p =
            { state.Player with
                CellX = x
                CellY = y
                SrcX = x
                SrcY = y
                Motion = Standing
                Progress = 0
                Bumped = false }

        let camX, camY = Camera.follow state.Map p
        state <- { state with Player = p; CamX = camX; CamY = camY }

    /// Warp to another map by id at an explicit cell/facing, loading its assets
    /// and neighbours and switching music. Throws if the map id is unknown or its
    /// assets aren't present (the channel turns that into an `error:` reply).
    member this.DebugWarp (mapId: string) (x: int) (y: int) (facing: Direction) =
        let ns = OverworldState.loadByIdAt content mapId x y facing
        state <- ns
        firedCoords <- Set.empty
        this.PlayMapMusic ns.MapId

    /// The NPC sprite for a SPRITE_* constant, best-effort (None if no PNG).
    member private _.SpriteFor(name: string) : Sprite option =
        match spriteCache.TryGetValue name with
        | true, v -> v
        | _ ->
            let file = name.Replace("SPRITE_", "").ToLowerInvariant()
            let v =
                try Some(Sprite.loadNamed file)
                with _ -> None

            spriteCache.[name] <- v
            v

    interface Scene with
        member this.Update(buttons: Buttons) : Transition =
            match runningMove with
            // An applymovement is animating: advance the actor a frame, write it back,
            // and resume the suspended script once the run reaches step_end.
            | Some(vm, i, run) ->
                let walkable = MapConnections.cellWalkable state.Map state.Collision state.Neighbors
                let run' = MovementRunner.step walkable run

                let npcs = Array.copy state.Npcs

                if i < npcs.Length then
                    npcs.[i] <- run'.Npc

                state <- { state with Npcs = npcs }

                if run'.Done then
                    runningMove <- None
                    this.Drive(Script.resume None world vm)
                else
                    runningMove <- Some(vm, i, run')
                    Stay
            | None ->

            match pending with
            // A pushed child scene popped — resume the suspended script with its result.
            | Some(vm, effect) ->
                pending <- None
                prevA <- buttons.A
                prevStart <- buttons.Start

                let value =
                    match effect with
                    | AskYesNo -> Some yesNoResult
                    | HallOfFame -> None
                    | StartBattle ->
                        let won = lastBattleOutcome = Some Win
                        let isTrainer = stagedTrainer.IsSome

                        if won then
                            match player.Party with
                            | lead :: rest when lead.Hp > 0 ->
                                let enemyBaseExp, enemyLevel =
                                    match stagedWild with
                                    | Some(species, level) ->
                                        let baseExp =
                                            Species.all
                                            |> Map.tryFind species
                                            |> Option.map (fun stats -> stats.BaseExp)
                                            |> Option.defaultValue 64
                                        (baseExp, level)
                                    | None ->
                                        match stagedTrainer with
                                        | Some(group, id) ->
                                            match Trainers.lookup group (int id) with
                                            | Some trainer ->
                                                let leadMon = trainer.Party |> List.tryHead
                                                let baseExp =
                                                    leadMon
                                                    |> Option.bind (fun mon -> Map.tryFind mon.Species Species.all)
                                                    |> Option.map (fun stats -> stats.BaseExp)
                                                    |> Option.defaultValue 64
                                                (baseExp, leadMon |> Option.map (fun mon -> mon.Level) |> Option.defaultValue 5)
                                            | None ->
                                                (64, match player.Party with h :: _ -> h.Level | [] -> 5)
                                        | None ->
                                            (64, match player.Party with h :: _ -> h.Level | [] -> 5)

                                let exp = Experience.expGained enemyBaseExp enemyLevel isTrainer
                                let growthRate =
                                    Species.all
                                    |> Map.tryPick (fun _ stats -> if stats.Dex = lead.SpeciesId then Some stats.GrowthRate else None)
                                    |> Option.defaultValue 0
                                let newLevel, newExp = Experience.levelAfterExp growthRate lead.Level lead.Exp exp
                                let newMaxHp = PartyMon.deriveMaxHp lead.SpeciesId newLevel
                                let hpGain = newMaxHp - lead.MaxHp
                                let updatedLead =
                                    { lead with
                                        Level = newLevel
                                        Exp = newExp
                                        MaxHp = newMaxHp
                                        Hp = lead.Hp + hpGain }

                                let evolvedLead =
                                    if newLevel > lead.Level then
                                        match Evolution.checkLevelEvolution updatedLead with
                                        | Some target ->
                                            Evolution.applyEvolution target updatedLead
                                        | None -> updatedLead
                                    else updatedLead

                                let learnedLead =
                                    if newLevel > lead.Level then
                                        MoveLearn.learnMovesForLevel evolvedLead
                                    else
                                        evolvedLead

                                player <- { player with Party = learnedLead :: rest }

                                if isTrainer then
                                    let reward =
                                        match stagedTrainer with
                                        | Some(group, id) ->
                                            match Trainers.lookup group (int id) with
                                            | Some trainer ->
                                                let lastMonLevel =
                                                    trainer.Party
                                                    |> List.tryLast
                                                    |> Option.map (fun mon -> mon.Level)
                                                    |> Option.defaultValue enemyLevel
                                                Experience.moneyEarned trainer.BaseReward lastMonLevel
                                            | None -> Experience.moneyEarned 25 enemyLevel
                                        | None -> 0

                                    player <- { player with Money = player.Money + reward }
                            | _ -> ()
                        else
                            // Lost: heal party, deduct half money
                            player <- { player with
                                            Party = Heal.healParty player.Party
                                            Money = player.Money / 2 }

                        stagedWild <- None
                        stagedTrainer <- None
                        stagedWinText <- ""
                        stagedLossText <- ""
                        lastBattleOutcome <- None
                        Some (if won then 1 else 0)
                    | _ -> None

                this.Drive(Script.resume value world vm)
            | None ->
                let aPressed = buttons.A && not prevA
                let startPressed = buttons.Start && not prevStart
                prevA <- buttons.A
                prevStart <- buttons.Start

                if startPressed && not state.Player.Moving then
                    // Open the start menu over the overworld. Each entry pushes a
                    // child scene; Exit (and B/Start within the menu) pops the menu.
                    Push(StartMenuScene(content, (fun entry ->
                        match entry with
                        | Pokedex -> Push(PokedexScene(content, player) :> Scene)
                        | Pokemon -> Push(PartyScene(content, player, fun p -> player <- p) :> Scene)
                        | Pack    ->
                            // TODO: once PackScene reports which rod was used, wire that
                            // result back into the overworld so fishing checks the
                            // facing water tile and stages fishEncounter here.
                            Push(PackScene(content, player, fun p -> player <- p) :> Scene)
                        | Pokegear -> Push(PokegearScene(content.Font, player) :> Scene)
                        | Save    -> Push(SaveMenuScene(content, player.Name, fun () -> SaveFile.write (this.Capture())) :> Scene)
                        | Option  -> Push(OptionsScene(content, player, fun p -> player <- p) :> Scene)
                        | Exit    -> Pop), buttons) :> Scene)
                elif aPressed && not state.Player.Moving then
                    let fx, fy = Triggers.facedCell state.Player.CellX state.Player.CellY state.Player.Facing
                    let collId = MapConnections.collisionId state.Map state.Collision state.Neighbors fx fy

                    if collId = FieldMoves.CollCutTree && FieldMoves.canUse "CUT" world player.Party then
                        printfn "HM tile detected: CUT at (%d, %d)" fx fy
                        Stay
                    elif collId = FieldMoves.CollSurf && FieldMoves.canUse "SURF" world player.Party then
                        printfn "HM tile detected: SURF at (%d, %d)" fx fy
                        Stay
                    else
                        // Talk to / read whatever the player faces. Objects are resolved
                        // over the *live* NPC set (a wandering NPC is talked to where it
                        // now stands), filtered to those currently present.
                        let objectScriptAt fx fy =
                            state.Npcs
                            |> Array.tryFind (fun n ->
                                MapEvents.objectVisible world n.Event && n.CellX = fx && n.CellY = fy)
                            |> Option.map (fun n -> n.Event.Script)

                        // A counter/desk tile in front of the player: GSC reaches one tile
                        // past it to the NPC behind (the Mart clerk, Center nurse, etc.).
                        let isCounter fx fy =
                            Collision.isCounterId (
                                MapConnections.collisionId state.Map state.Collision state.Neighbors fx fy)

                        match Triggers.actionScript objectScriptAt isCounter state.Events state.Player.CellX state.Player.CellY state.Player.Facing with
                        | Some label when state.Script.Labels.ContainsKey label ->
                            sound.PlaySfx "Sfx_Menu"
                            this.Drive(Script.start label world state.Script state.MapId)
                        | _ -> Stay
                else
                    let before = state.Player.CellX, state.Player.CellY
                    state <- OverworldState.tick (fun n -> MapEvents.objectVisible world n.Event) buttons state
                    let after = state.Player.CellX, state.Player.CellY

                    // Overworld locomotion SFX (GSC plays these as the action begins):
                    // a ledge hop on its first frame, a wall bump on its rising edge.
                    // Plain walking is silent.
                    if state.Player.Motion = Hopping && state.Player.Progress = 0 then
                        sound.PlaySfx "Sfx_JumpOverLedge"
                    elif state.Player.Bumped then
                        sound.PlaySfx "Sfx_Bump"

                    // Walking off the current map into a connected neighbour swaps the
                    // active map once the step settles (player rebased to the same world
                    // position, so the view is seamless).
                    match OverworldState.crossConnection content state with
                    | Some ns ->
                        state <- ns
                        this.PlayMapMusic ns.MapId
                        firedCoords <- Set.empty
                        Stay
                    | None ->

                    if after = before then
                        Stay
                    else
                        if player.RepelSteps > 0 then
                            player <- { player with RepelSteps = player.RepelSteps - 1 }

                        let mutable encounterTransition = Stay
                        let collId = MapConnections.collisionId state.Map state.Collision state.Neighbors (fst after) (snd after)

                        match WildEncounter.tryEncounter state.MapId collId encounterRng player with
                        | Some(species, level) ->
                            stagedWild <- Some(species, level)
                            stagedTrainer <- None
                            encounterTransition <- Push(this.BuildBattle() :> Scene)
                        | None -> ()

                        match encounterTransition with
                        | Push _ -> encounterTransition
                        | _ ->
                            // Stepping onto a warp tile sends the player to its paired
                            // warp on the destination map (a no-op until that map is
                            // wired up). Otherwise a coord trigger may fire.
                            match MapEvents.warpAt (fst after) (snd after) state.Events with
                            | Some w ->
                                match OverworldState.tryWarp content w.DestMap w.DestWarp with
                                | Some dest ->
                                    state <- dest
                                    this.PlayMapMusic dest.MapId
                                    firedCoords <- Set.empty
                                    Stay
                                | None -> Stay
                            | None ->
                                match Triggers.coordToFire activeScene firedCoords state.Events (fst after) (snd after) with
                                | Some c when state.Script.Labels.ContainsKey c.Script ->
                                    firedCoords <- Set.add after firedCoords
                                    this.Drive(Script.start c.Script world state.Script state.MapId)
                                | Some _ ->
                                    firedCoords <- Set.add after firedCoords
                                    Stay
                                | None -> Stay

        member this.Render(fb: Framebuffer) =
            OverworldRenderer.draw fb state

            // Draw visible NPC objects over the map (player already drawn above),
            // using each object's live, interpolated position and walk frame.
            for n in state.Npcs do
                if MapEvents.objectVisible world n.Event then
                    match this.SpriteFor n.Event.Sprite with
                    | Some spr ->
                        let frame, flip = NpcObject.frameAndFlip n
                        let px, py = NpcObject.worldPixel n
                        SpriteRenderer.draw fb state.SpritePalette spr frame (px - state.CamX) (py - state.CamY) flip
                    | None -> ()
