namespace PokeGold.Game.Scenes

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Audio
open PokeGold.Game.Battle
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Render
open PokeGold.Game.Save
open PokeGold.Game.Debug

module TrainerSight =
    let private isInSightCone (npc: NpcObject) (px: int) (py: int) : bool =
        let dx = px - npc.CellX
        let dy = py - npc.CellY
        let sight = npc.Event.Sight

        if sight <= 0 then
            false
        else
            match npc.Facing with
            | Down -> dx = 0 && dy > 0 && dy <= sight
            | Up -> dx = 0 && dy < 0 && -dy <= sight
            | Left -> dy = 0 && dx < 0 && -dx <= sight
            | Right -> dy = 0 && dx > 0 && dx <= sight

    let checkTrainerSight (npcs: NpcObject[]) (px: int) (py: int) (world: World) : int option =
        npcs
        |> Array.tryFindIndex (fun npc ->
            npc.Event.Type = "OBJECTTYPE_TRAINER"
            && MapEvents.objectVisible world npc.Event
            && isInSightCone npc px py)

    let checkTrainerSightPresent (npcs: NpcObject[]) (isPresent: int -> bool) (px: int) (py: int) : int option =
        npcs
        |> Array.mapi (fun i npc -> i, npc)
        |> Array.tryFind (fun (i, npc) ->
            isPresent i
            && npc.Event.Type = "OBJECTTYPE_TRAINER"
            && isInSightCone npc px py)
        |> Option.map fst

/// The walk-around-the-map scene. Owns the mutable overworld state plus the
/// running-script bookkeeping that turns NPC/sign interactions and coord triggers
/// into real GSC scripts: it drives the pure [`Script`] VM, enacting each
/// [`ScriptEffect`] (text box, yes/no, battle, flags, items) and resuming the VM
/// with the result. Pure commands run inline within one frame; effects that need a
/// child scene push it and suspend until it pops.
type OverworldScene(content: Content, sound: ISoundBoard, initial: OverworldState) =
    let mutable state = initial
    /// The script flag/var/scene world — mutated as scripts run; persisted in save.
    /// Starts empty; seeded by Load (debug) or Restore (save/new-game).
    let mutable world: PokeGold.Game.Overworld.Script.World = World.empty
    /// Coord triggers already fired this visit (fire-once).
    let mutable firedCoords: Set<int * int> = Set.empty
    /// The player's full persistent state (party, bag, dex, money, etc.).
    /// Starts at initial; seeded by Load (debug) or Restore (save/new-game).
    let mutable player: PokeGold.Game.Player.PlayerState = PlayerStateOps.initial
    /// Staged battle data for the next `startbattle` call.
    let mutable stagedWild: (string * int) option = None
    let mutable stagedTrainer: (string * string) option = None
    let mutable stagedWinText: string = ""
    let mutable stagedLossText: string = ""
    let mutable balanceOverlay: BalanceDisplay option = None
    /// A suspended script awaiting the child scene we pushed for an effect.
    let mutable pending: (ScriptVm * ScriptEffect) option = None
    /// A child scene transition produced while restoring/loading the overworld.
    let mutable restoreTransition: Transition option = None
    /// Script resumes waiting behind higher-priority map-entry scripts.
    let scriptQueue = Queue<ScriptVm * int option>()
    /// The live actor most recently selected by an A-press or `setlasttalked`.
    let mutable lastTalkedActor: ActorId option = None
    /// A script suspended on an `applymovement`: the VM to resume, the moved actor,
    /// and the live movement run. Ticked each frame until done.
    let mutable runningMove: (ScriptVm * ActorId * MovementRunner.Run) option = None
    /// The most recent yes/no choice, written by the YesNoScene callback.
    let mutable yesNoResult = 0
    /// A script suspended by `pause` / timed cosmetic effects.
    let mutable pauseFrames = 0
    let mutable pauseVm: ScriptVm option = None
    /// Active `follow follower, leader` relationship.
    let mutable followPair: (ActorId * ActorId) option = None
    let mutable lastText: (string * string) option = None
    let mutable hallOfFameCreditsShown = false
    let mutable askPhoneResult = 1
    let mutable menuResult = 0
    let mutable apricornResult: string option = None
    let mutable dayCareResult: int option = None
    let mutable haircutResult = 0
    let mutable billsGrandfatherResult = 0
    let mutable magikarpLengthResult = 1
    let mutable unownPuzzleResult = 0
    let mutable contestPartyBackup: PartyMon list option = None
    let mutable prevA = false
    let mutable prevStart = false
    /// Wild encounter RNG for the overworld trigger hook.
    let encounterRng = System.Random()
    let fundsResult current amount =
        if current > amount then 0
        elif current = amount then 1
        else 2

    let scriptMenuOptionCount mapId menu =
        if menu = ".Gold_MenuHeader"
           || menu = ".Silver_MenuHeader"
           || menu = "GoldenrodGameCornerTMVendorMenuHeader"
           || menu = "CeladonPrizeRoom_TMMenuHeader"
           || (mapId = "CeladonGameCornerPrizeRoom" && menu = ".MenuHeader") then
            4
        else
            3

    /// The outcome of the most recent battle (set by BattleScene callback).
    let mutable lastBattleOutcome: Outcome option = None
    /// Cache of NPC sprites by SPRITE_* constant (None = no art for it).
    let spriteCache = Dictionary<string, Sprite option>()

    let checkTimeMatches (time: string) =
        let tod = GameTimeState.timeOfDay player.GameTime
        match time.Trim().ToUpperInvariant() with
        | "MORN" -> tod = Morn
        | "DAY" -> tod = Day
        | "NITE" -> tod = Nite
        | "ANYTIME" -> true
        | _ -> false
    /// Live object presence for this map visit. Event flags seed this on map load;
    /// only `appear`/`disappear` update it live. Plain `setevent`/`clearevent`
    /// prepares future map-load state and must not pop already-loaded actors.
    let mutable objectPresent: bool[] = [||]

    let interpretHostEffect =
        function
        | HostEffect.PlayMusic path -> sound.PlayMusic path
        | HostEffect.StopMusic -> sound.StopMusic()
        | HostEffect.PlaySfx name -> sound.PlaySfx name
        | HostEffect.PlayJingle path -> sound.PlayJingle path

    let directionToward (fromX: int) (fromY: int) (toX: int) (toY: int) : Direction =
        let dx = toX - fromX
        let dy = toY - fromY

        if abs dx > abs dy then
            if dx > 0 then Right else Left
        else if dy > 0 then
            Down
        else
            Up

    let deltaOf (dir: Direction) : int * int =
        match dir with
        | Down -> 0, 1
        | Up -> 0, -1
        | Left -> -1, 0
        | Right -> 1, 0

    let directionFromButtons (buttons: Buttons) : Direction option =
        if buttons.Down then Some Down
        elif buttons.Up then Some Up
        elif buttons.Left then Some Left
        elif buttons.Right then Some Right
        else None

    let resetObjectPresence () =
        objectPresent <- state.Npcs |> Array.map (fun n -> MapEvents.objectVisible world n.Event)

    let syncFlaggedObjectPresenceFromWorld () =
        if objectPresent.Length = state.Npcs.Length then
            for i = 0 to state.Npcs.Length - 1 do
                if state.Npcs.[i].Event.EventFlag.IsSome then
                    objectPresent.[i] <- MapEvents.objectVisible world state.Npcs.[i].Event

    let isObjectPresent (idx: int) =
        idx >= 0 && idx < objectPresent.Length && objectPresent.[idx]

    let parseDirection (facing: string) : Direction option =
        match facing.Trim().ToUpperInvariant() with
        | "DOWN"
        | "OW_DOWN"
        | "0" -> Some Down
        | "UP"
        | "OW_UP"
        | "1"
        | "4" -> Some Up
        | "LEFT"
        | "OW_LEFT"
        | "2"
        | "8" -> Some Left
        | "RIGHT"
        | "OW_RIGHT"
        | "3"
        | "12" -> Some Right
        | _ -> None

    let npcIndexOfSymbol (objSym: string) : int option =
        Actor.resolveObjectIndex (OverworldState.objectIndexOf state.MapId) lastTalkedActor objSym

    let actorOfSymbol (objSym: string) : ActorId option =
        Actor.resolve (OverworldState.objectIndexOf state.MapId) lastTalkedActor objSym

    let setObjectVisible (objSym: string) (visible: bool) =
        let idxOpt = npcIndexOfSymbol objSym
        let eventFlag =
            match OverworldState.objectEventOf state.MapId objSym with
            | Some o -> o.EventFlag
            | None ->
                idxOpt
                |> Option.bind (fun idx ->
                    if idx >= 0 && idx < state.Npcs.Length then
                        state.Npcs.[idx].Event.EventFlag
                    else
                        None)

        match eventFlag with
        | Some flag ->
            world <- (if visible then World.clearEvent flag world else World.setEvent flag world)

            match idxOpt with
            | Some idx when idx >= 0 && idx < objectPresent.Length -> objectPresent.[idx] <- visible
            | _ -> ()
        | None ->
            match idxOpt with
            | Some idx when idx >= 0 && idx < objectPresent.Length -> objectPresent.[idx] <- visible
            | _ -> ()

    let tryActorCell (objSym: string) : (ActorId * int * int) option =
        actorOfSymbol objSym
        |> Option.bind (fun actor ->
            Actor.tryCell state.Player state.Npcs actor
            |> Option.map (fun (x, y) -> actor, x, y))

    let actorCell (actor: ActorId) : (int * int) option =
        Actor.tryCell state.Player state.Npcs actor

    let actorMoving (actor: ActorId) : bool =
        Actor.isMoving state.Player state.Npcs actor

    let setFollowerStep (actor: ActorId) (targetX: int) (targetY: int) =
        match actorCell actor with
        | Some(cx, cy) when not (actorMoving actor) && (cx <> targetX || cy <> targetY) ->
            let dir = directionToward cx cy targetX targetY
            let dx, dy = deltaOf dir
            let tx, ty =
                if abs (targetX - cx) + abs (targetY - cy) = 1 then
                    targetX, targetY
                else
                    cx + dx, cy + dy

            let player, npcs = Actor.beginStep actor dir tx ty state.Player state.Npcs
            let camX, camY = Camera.followExt state.Map state.Neighbors player
            state <-
                { state with
                    Player = player
                    CamX = camX
                    CamY = camY
                    Npcs = npcs }
        | _ -> ()

    let advanceFollowMotion (actor: ActorId) =
        match actor with
        | ActorId.Player when state.Player.Moving ->
            let walkable = MapConnections.cellWalkable state.Map state.Collision state.Neighbors
            let collId = MapConnections.collisionId state.Map state.Collision state.Neighbors
            let player = Movement.stepWith walkable collId Buttons.none state.Player
            let camX, camY = Camera.followExt state.Map state.Neighbors player
            state <- { state with Player = player; CamX = camX; CamY = camY }
        | ActorId.Object idx when idx >= 0 && idx < state.Npcs.Length && state.Npcs.[idx].Moving ->
            let walkable = MapConnections.cellWalkable state.Map state.Collision state.Neighbors
            let npcs = Array.copy state.Npcs
            npcs.[idx] <- ObjectStep.step walkable (fun _ _ -> false) npcs.[idx]
            state <- { state with Npcs = npcs }
        | _ -> ()

    let tryPushStrengthBoulder (buttons: Buttons) : bool =
        match directionFromButtons buttons with
        | None -> false
        | Some facing when state.Player.Motion <> Standing || World.getVar "__strength_active" world <> 1 -> false
        | Some facing ->
            let dx, dy = deltaOf facing
            let tx, ty = state.Player.CellX + dx, state.Player.CellY + dy
            let bx, by = tx + dx, ty + dy

            let target =
                state.Npcs
                |> Array.mapi (fun i n -> i, n)
                |> Array.tryFind (fun (i, n) ->
                    isObjectPresent i
                    && n.Motion = NpcStanding
                    && n.Event.Movement = "SPRITEMOVEDATA_STRENGTH_BOULDER"
                    && n.CellX = tx
                    && n.CellY = ty)

            match target with
            | None -> false
            | Some(idx, npc) ->
                let walkable = MapConnections.cellWalkable state.Map state.Collision state.Neighbors

                let occupiedByOtherObject =
                    state.Npcs
                    |> Array.mapi (fun i n -> i, n)
                    |> Array.exists (fun (i, n) ->
                        i <> idx
                        && isObjectPresent i
                        && ((n.CellX = bx && n.CellY = by)
                            || (n.Motion <> NpcStanding && n.SrcX = bx && n.SrcY = by)))

                if walkable bx by && not occupiedByOtherObject then
                    let player =
                        { state.Player with
                            Facing = facing
                            SrcX = state.Player.CellX
                            SrcY = state.Player.CellY
                            CellX = tx
                            CellY = ty
                            Motion = Walking
                            Progress = 0
                            Bumped = false }

                    let npcs = Array.copy state.Npcs
                    npcs.[idx] <-
                        { npc with
                            Facing = facing
                            SrcX = npc.CellX
                            SrcY = npc.CellY
                            CellX = bx
                            CellY = by
                            Motion = NpcWalking
                            Progress = 0 }

                    let camX, camY = Camera.followExt state.Map state.Neighbors player
                    state <- { state with Player = player; Npcs = npcs; CamX = camX; CamY = camY }
                    true
                else
                    false

    let strengthHoleFall (mapId: string) (eventFlag: string) (x: int) (y: int) : string option option =
        match mapId, eventFlag, x, y with
        | "IcePathB1F", "EVENT_BOULDER_IN_ICE_PATH_1", 11, 2 -> Some(Some "EVENT_BOULDER_IN_ICE_PATH_1A")
        | "IcePathB1F", "EVENT_BOULDER_IN_ICE_PATH_2", 4, 7 -> Some(Some "EVENT_BOULDER_IN_ICE_PATH_2A")
        | "IcePathB1F", "EVENT_BOULDER_IN_ICE_PATH_3", 5, 12 -> Some(Some "EVENT_BOULDER_IN_ICE_PATH_3A")
        | "IcePathB1F", "EVENT_BOULDER_IN_ICE_PATH_4", 12, 13 -> Some(Some "EVENT_BOULDER_IN_ICE_PATH_4A")
        | "BlackthornGym2F", "EVENT_BOULDER_IN_BLACKTHORN_GYM_1", 8, 3 -> Some None
        | "BlackthornGym2F", "EVENT_BOULDER_IN_BLACKTHORN_GYM_2", 2, 5 -> Some None
        | "BlackthornGym2F", "EVENT_BOULDER_IN_BLACKTHORN_GYM_3", 8, 7 -> Some None
        | _ -> None

    let applyStrengthBoulderHoleFalls () =
        for idx = 0 to state.Npcs.Length - 1 do
            let npc = state.Npcs.[idx]

            match npc.Event.EventFlag with
            | Some eventFlag when
                isObjectPresent idx
                && npc.Motion = NpcStanding
                && npc.Event.Movement = "SPRITEMOVEDATA_STRENGTH_BOULDER" ->
                match strengthHoleFall state.MapId eventFlag npc.CellX npc.CellY with
                | Some lowerFloorFlag ->
                    world <- World.setEvent eventFlag world
                    if idx < objectPresent.Length then
                        objectPresent.[idx] <- false

                    match lowerFloorFlag with
                    | Some flag -> world <- World.clearEvent flag world
                    | None -> ()
                | None -> ()
            | _ -> ()

    let setNpcFacing (idx: int) (facing: Direction) =
        let player, npcs = Actor.setFacing (ActorId.Object idx) facing state.Player state.Npcs
        state <- { state with Player = player; Npcs = npcs }

    let setActorFacing (actor: ActorId) (facing: Direction) =
        let player, npcs = Actor.setFacing actor facing state.Player state.Npcs
        state <- { state with Player = player; Npcs = npcs }

    /// Start this map's background music as soon as the scene exists.
    do
        match OverworldScene.musicFor initial.MapId with
        | Some path -> interpretHostEffect (HostEffect.PlayMusic path)
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
        | Some path -> interpretHostEffect (HostEffect.PlayMusic path)
        | None -> ()

    member private _.MoveObjectTo(objSym: string, x: int, y: int) =
        match actorOfSymbol objSym with
        | Some(ActorId.Object _ as actor) ->
            let player, npcs = Actor.place actor x y state.Player state.Npcs
            state <- { state with Player = player; Npcs = npcs }
        | _ -> ()

    member private _.ChangeBlockAt(x: int, y: int, blockId: int) =
        let blockX = x / 2
        let blockY = y / 2

        if blockX >= 0 && blockY >= 0 && blockX < state.Map.Width && blockY < state.Map.Height then
            let blocks = Array.copy state.Map.BlockIds
            blocks.[blockY * state.Map.Width + blockX] <- byte blockId
            state <- { state with Map = { state.Map with BlockIds = blocks } }

    member private _.ReanchorCamera() =
        let camX, camY = Camera.follow state.Map state.Player
        state <- { state with CamX = camX; CamY = camY }

    member private _.FacingCell() =
        let dx, dy =
            match state.Player.Facing with
            | Down -> 0, 1
            | Up -> 0, -1
            | Left -> -1, 0
            | Right -> 1, 0

        state.Player.CellX + dx, state.Player.CellY + dy

    member private _.PlayerTerrainWalkable (cx: int) (cy: int) =
        let footWalkable = MapConnections.cellWalkable state.Map state.Collision state.Neighbors cx cy
        if footWalkable then
            true
        elif World.getVar "__surfing" world = 1 then
            MapConnections.collisionId state.Map state.Collision state.Neighbors cx cy
            |> FieldMoves.isSurfWater
        else
            false

    member private this.UseFieldMove(moveName: string) : Transition =
        let x, y = this.FacingCell()
        let targetColl = MapConnections.collisionId state.Map state.Collision state.Neighbors x y

        match FieldMoves.tryUse moveName targetColl state.MapId world player.Party with
        | FieldMoves.NotUsable reason ->
            Push(TextBoxScene.Of(content, reason + "<DONE>") :> Scene)
        | FieldMoves.Used(move, message) ->
            world <-
                world
                |> World.setBuffer "__last_field_move" move
                |> World.setVar "__field_move_success" 1

            world <-
                match move with
                | "STRENGTH" -> World.setVar "__strength_active" 1 world
                | "SURF" -> World.setVar "__surfing" 1 world
                | "FLASH" -> World.setVar "__flash_active" 1 world
                | "FLY" -> World.setVar "__fly_requested" 1 world
                | "CUT" -> World.setVar "__cut_used" 1 world
                | "WHIRLPOOL" -> World.setVar "__whirlpool_used" 1 world
                | "WATERFALL" -> World.setVar "__waterfall_used" 1 world
                | _ -> world

            Push(TextBoxScene.Of(content, message + "<DONE>") :> Scene)

    /// The Pokégear with the radio dial the player can currently receive. The
    /// Poké Flute channel needs the EXPN CARD (it only broadcasts in Kanto,
    /// where the card is obtained); tuning persists to `__radio_station`,
    /// which `special SnorlaxAwake` checks.
    member private _.MakePokegear(tab: PokegearTab, mapId: string, radioChannel: int option) : Scene =
        let stations =
            [ "OAKS_POKEMON_TALK", "OAK'S TALK"
              "POKEMON_MUSIC", "POKeMON MUSIC"
              "LUCKY_CHANNEL", "LUCKY NUMBER"
              if World.hasFlag "ENGINE_EXPN_CARD" world then
                  "POKE_FLUTE", "POKe FLUTE" ]

        PokegearScene(
            content.Font,
            player,
            initialTab = tab,
            mapId = mapId,
            ?radioChannel = radioChannel,
            stations = stations,
            onTune = fun id -> world <- World.setBuffer "__radio_station" id world)
        :> Scene

    member private this.PlayScriptMusic(song: string) =
        match song with
        | "__MAP_DEFAULT__" -> this.PlayMapMusic state.MapId
        | "__STOP__" -> interpretHostEffect HostEffect.StopMusic
        | _ ->
            match Map.tryFind song MusicData.byId with
            | Some path -> interpretHostEffect (HostEffect.PlayMusic path)
            | None -> ()

    member private _.ReloadCurrentMap() =
        let p = state.Player
        state <- OverworldState.loadByIdAt content state.MapId p.CellX p.CellY p.Facing
        resetObjectPresence ()

    member private this.ContinueQueuedScripts() : Transition =
        if scriptQueue.Count > 0 then
            let vm, value = scriptQueue.Dequeue()
            this.Drive(Script.resume value world vm)
        else
            Stay

    member private this.RunSceneScript(mapId: string) : Transition =
        let sceneIdx = World.getScene mapId world
        let sceneLabel = MapEvents.sceneLabelAt sceneIdx state.Events

        if sceneLabel <> "" && state.Script.Labels.ContainsKey sceneLabel then
            this.Drive(Script.start sceneLabel world state.Script mapId)
        else
            this.ContinueQueuedScripts()

    member private this.EnterMap(nextState: OverworldState, playMusic: bool) : Transition =
        state <- nextState
        balanceOverlay <- None
        resetObjectPresence ()
        firedCoords <- Set.empty

        if playMusic then
            this.PlayMapMusic nextState.MapId

        this.RunMapCallbacks nextState.MapId
        syncFlaggedObjectPresenceFromWorld ()
        this.RunSceneScript nextState.MapId

    member private this.ApplyCallbackEffect(effect: ScriptEffect) =
        match effect with
        | SetVisible(obj, visible) -> setObjectVisible obj visible
        | MoveObject(objSym, x, y) -> this.MoveObjectTo(objSym, x, y)
        | ChangeBlock(x, y, blockId) -> this.ChangeBlockAt(x, y, blockId)
        | ReanchorMap -> this.ReanchorCamera()
        | PlayMusic song -> this.PlayScriptMusic song
        | ReloadMap -> this.ReloadCurrentMap()
        | _ -> ()

    /// Run map callbacks of the given kind (MAPCALLBACK_NEWMAP, MAPCALLBACK_TILES, etc.)
    /// These fire on map entry/reload to initialize flypoints, decorations, events.
    member private this.RunMapCallbacks(mapId: string) =
        for cb in state.Events.Callbacks do
            if state.Script.Labels.ContainsKey cb.Label then
                let step = Script.start cb.Label world state.Script mapId
                // Drive callbacks to completion without UI; stateful effects still
                // have to apply or map objects/tiles drift from script flags.
                let rec drive (s: ScriptStep) =
                    world <- s.World
                    match s.Outcome with
                    | Completed -> ()
                    | Suspended(vm, effect) ->
                        this.ApplyCallbackEffect effect
                        drive (Script.resume None world vm)
                drive step

    /// Resolve a text label to its M5 token string. Map-local text wins; std-script
    /// text (nurse prompts, bookshelves, signs) is the fallback; an unknown label
    /// shows the label itself.
    member private _.ResolveText(label: string) : string =
        let rawText label =
            match Map.tryFind label state.Text with
            | Some s -> s
            | None ->
                match Map.tryFind label StdScriptsData.text with
                | Some s -> s
                | None -> label + "<DONE>"

        let inlineText label =
            match Map.tryFind label state.Text with
            | Some s -> s
            | None ->
                match Map.tryFind label StdScriptsData.text with
                | Some s -> s
                | None -> label

        let cleanInline (text: string) =
            text.Replace("<DONE>", "").Replace("<PROMPT>", "").Replace("@", "")

        let bufferValue name =
            let value = World.getBuffer name world
            if value = "" then ""
            else cleanInline (inlineText value)

        let ramValue name =
            match name with
            | "wPlayerName" -> player.Name
            | "wMonOrItemNameBuffer" ->
                match bufferValue "wMonOrItemNameBuffer" with
                | "" -> bufferValue "STRING_BUFFER_4"
                | value -> value
            | _ -> bufferValue name

        let raw = rawText label

        // Substitute player/rival name placeholders and named text buffers.
        let withBuffers (text: string) : string =
            [ 1..5 ]
            |> List.fold
                (fun (acc: string) (i: int) ->
                    acc.Replace($"<STRING_BUFFER_{i}>", bufferValue $"STRING_BUFFER_{i}"))
                text

        let withRamBuffers (text: string) =
            Regex.Replace(
                text,
                "<RAM_([^>]+)>",
                MatchEvaluator(fun m -> ramValue m.Groups.[1].Value))

        raw.Replace("<PLAYER>", player.Name)
           .Replace("<RIVAL>", (let r = World.getBuffer "__rival_name" world in if r = "" then "SILVER" else r))
           .Replace("<MOM>", "MOM")
           .Replace("@", "")
           |> withBuffers
           |> withRamBuffers

    /// Add `qty` of an item to the bag.
    member private _.AddItem (item: string) (qty: int) =
        player <- { player with Bag = Bag.add item qty player.Bag }

    /// Remove up to `qty` of an item from the bag.
    member private _.RemoveItem (item: string) (qty: int) =
        player <- { player with Bag = Bag.remove item qty player.Bag }

    member private _.SyncDayCareFlags() =
        let setIf flag enabled world =
            if enabled then World.setFlag flag world else World.clearFlag flag world

        world <-
            world
            |> setIf "ENGINE_DAY_CARE_MAN_HAS_EGG" player.DayCare.HasEgg
            |> setIf "ENGINE_DAY_CARE_MAN_HAS_MON" player.DayCare.Mon1.IsSome
            |> setIf "ENGINE_DAY_CARE_LADY_HAS_MON" player.DayCare.Mon2.IsSome

    member private _.ApplyHaircut (brother: string) (partyIndex: int) =
        let mon = List.item partyIndex player.Party

        if Breeding.isEgg mon then
            haircutResult <- 1
        else
            let roll = encounterRng.Next(100)

            let result, deltas =
                match brother with
                | "YOUNGER" when roll < 60 -> 2, (1, 1, 1)
                | "YOUNGER" when roll < 90 -> 3, (3, 3, 1)
                | "YOUNGER" -> 4, (10, 10, 4)
                | _ when roll < 30 -> 2, (1, 1, 1)
                | _ when roll < 80 -> 3, (3, 3, 1)
                | _ -> 4, (5, 5, 2)

            let delta =
                let low, mid, high = deltas
                if mon.Friendship < 100 then low
                elif mon.Friendship < 200 then mid
                else high

            let updated = { mon with Friendship = min 255 (mon.Friendship + delta) }
            let party =
                player.Party
                |> List.mapi (fun i existing -> if i = partyIndex then updated else existing)
            player <- { player with Party = party }
            haircutResult <- result

    member private _.SelectBillsGrandfatherMon (partyIndex: int) =
        let mon = List.item partyIndex player.Party
        let speciesName =
            Species.all
            |> Map.tryPick (fun key stats -> if stats.Dex = mon.SpeciesId then Some key else None)
            |> Option.defaultValue mon.Nickname
        billsGrandfatherResult <- mon.SpeciesId
        world <- World.setBuffer "STRING_BUFFER_3" speciesName world

    member private _.FormatMagikarpLength (feet: int, inches: int) =
        sprintf "%d'%d\"" feet inches

    member private _.CalcMagikarpLength (mon: PartyMon) =
        let rrc8 count value =
            let v = value &&& 0xff
            ((v >>> count) ||| (v <<< (8 - count))) &&& 0xff

        let dvHigh = (mon.Dvs >>> 8) &&& 0xff
        let dvLow = mon.Dvs &&& 0xff
        let idHigh = (mon.OtId >>> 8) &&& 0xff
        let idLow = mon.OtId &&& 0xff
        let b = (rrc8 2 dvHigh) ^^^ (rrc8 1 idHigh)
        let c = (rrc8 2 dvLow) ^^^ (rrc8 1 idLow)
        let bc = (b <<< 8) ||| c

        let rawMillimeters =
            if b = 0 && c < 10 then
                bc + 190
            else
                let table =
                    [| 110, 1, 2
                       310, 2, 3
                       710, 4, 4
                       2710, 20, 5
                       7710, 50, 6
                       17710, 100, 7
                       32710, 150, 8
                       47710, 150, 9
                       57710, 100, 10
                       62710, 50, 11
                       64710, 20, 12
                       65210, 5, 13
                       65410, 2, 14
                       65510, 1, 15 |]

                match table |> Array.tryFind (fun (threshold, _, _) -> b < ((threshold >>> 8) &&& 0xff)) with
                | Some(threshold, divisor, hundreds) ->
                    let quotientLowByte = (((bc - threshold) &&& 0xffff) / divisor) &&& 0xff
                    hundreds * 100 + quotientLowByte
                | None ->
                    1600 + ((bc - 65510) &&& 0xffff)

        let totalInches = (rawMillimeters * 10) / 254
        totalInches / 12, totalInches % 12

    member private this.RecordMagikarpLength (mon: PartyMon) =
        let feet, inches = this.CalcMagikarpLength mon
        let renderedLength = this.FormatMagikarpLength(feet, inches)
        world <- World.setBuffer "STRING_BUFFER_1" renderedLength world

        let bestFeet = World.getVar "__best_magikarp_length_feet" world
        let bestInches = World.getVar "__best_magikarp_length_inches" world
        let beatsRecord = feet > bestFeet || (feet = bestFeet && inches > bestInches)

        if beatsRecord then
            world <-
                world
                |> World.setVar "__best_magikarp_length_feet" feet
                |> World.setVar "__best_magikarp_length_inches" inches
                |> World.setBuffer "__magikarp_record_holder" mon.OtName
            magikarpLengthResult <- 3
        else
            magikarpLengthResult <- 2

        sprintf "Let me measure<LINE>that MAGIKARP.<PARA>...Hm, it measures<LINE>%s.<PROMPT>" renderedLength

    member private this.MagikarpRecordText() =
        let feet = World.getVar "__best_magikarp_length_feet" world
        let inches = World.getVar "__best_magikarp_length_inches" world
        let holder =
            match World.getBuffer "__magikarp_record_holder" world with
            | "" -> player.Name
            | name -> name
        let renderedLength = this.FormatMagikarpLength(feet, inches)
        world <- World.setBuffer "STRING_BUFFER_1" renderedLength world
        sprintf "CURRENT RECORD<PARA>%s caught by<LINE>%s<PROMPT>" renderedLength holder

    member private _.UnownPuzzleText (puzzleId: int) =
        let name =
            match puzzleId with
            | 1 -> "OMANYTE"
            | 2 -> "AERODACTYL"
            | 3 -> "HO-OH"
            | _ -> "KABUTO"
        sprintf "The %s sliding<LINE>panels clicked into<CONT>place.<PROMPT>" name

    member private _.UnownPrinterText() =
        "ALPH RUINS STAMP<PARA>All UNOWN forms are<LINE>ready to print.<PROMPT>"

    member private _.PlayGameCornerGame (game: string) (lucky: bool) =
        if Bag.count "COIN_CASE" player.Bag > 0 && player.Coins >= 3 then
            let win =
                if lucky then encounterRng.Next(4) <> 0
                else encounterRng.Next(2) = 0

            let payout =
                if not win then 0
                elif game = "SLOT_MACHINE" && lucky then 15
                else 6

            player <- { player with Coins = min 9999 (player.Coins - 3 + payout) }

    member private _.RegisterPrizeDex (dex: int) =
        if dex > 0 && not (Set.contains dex player.DexOwn) then
            player <-
                { player with
                    DexSeen = Set.add dex player.DexSeen
                    DexOwn = Set.add dex player.DexOwn }

    member private _.CanNameRate (mon: PartyMon) =
        not (Breeding.isEgg mon)
        && (mon.OtName = player.Name || mon.OtName = "PLAYER")
        && mon.OtId = 0

    member private _.RenamePartyMon (partyIndex: int) (nickname: string) =
        let nickname = nickname.Trim()

        if nickname <> "" then
            let party =
                player.Party
                |> List.mapi (fun i mon ->
                    if i = partyIndex && mon.Nickname <> nickname then { mon with Nickname = nickname }
                    else mon)

            player <- { player with Party = party }

    member private _.GiveParkBallsForContest() =
        world <-
            world
            |> World.setVar "wParkBallsRemaining" 20
            |> World.setVar "__bug_contest_caught_species" 0
        player <- { player with Bag = Bag.add "PARK_BALL" 20 player.Bag }

    member private _.ContestDropOffMons() =
        match player.Party with
        | lead :: rest when lead.Hp > 0 ->
            contestPartyBackup <- Some rest
            player <- { player with Party = [ lead ] }
            0
        | _ -> 1

    member private _.ContestReturnMons() =
        match contestPartyBackup with
        | Some rest ->
            player <- { player with Party = (player.Party @ rest) |> List.truncate BoxOps.partyLength }
            contestPartyBackup <- None
        | None -> ()

    member private _.BugContestJudgingResult() =
        match World.getVar "__bug_contest_caught_species" world with
        | 123
        | 127 -> 1
        | species when species > 0 -> 3
        | _ -> 0

    member private _.CheckPartyFullAfterContest() =
        player <- { player with Bag = Bag.remove "PARK_BALL" 99 player.Bag }

        match World.getVar "__bug_contest_caught_species" world with
        | 0 -> 2
        | species ->
            if player.Party |> List.exists (fun mon -> mon.SpeciesId = species) then 0
            else 1

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
                HeldItem = battleMon.HeldItem
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

    member private _.CaptureBattleMon(mon: BattleMon) =
        let statusCode (status: StatusCondition) : string =
            match status with
            | Healthy -> ""
            | Sleep _ -> "SLP"
            | Poison -> "PSN"
            | BadPoison _ -> "PSN"
            | Burn -> "BRN"
            | Freeze -> "FRZ"
            | Paralysis -> "PAR"

        let moves =
            List.zip mon.Moves mon.Pp
            |> List.choose (fun (move, pp) ->
                let idx = MovesData.byIndex |> Array.tryFindIndex (fun m -> m.Name = move.Name)
                idx |> Option.map (fun i -> i, pp))

        let captured =
            { PartyMon.create mon.Species.Dex mon.Level with
                Hp = max 1 mon.Hp
                MaxHp = mon.MaxHp
                Status = statusCode mon.Status
                Moves = moves
                HeldItem = mon.HeldItem }

        let updatedPlayer =
            { player with
                DexSeen = Set.add mon.Species.Dex player.DexSeen
                DexOwn = Set.add mon.Species.Dex player.DexOwn }

        if World.hasFlag "ENGINE_BUG_CONTEST_TIMER" world then
            world <- World.setVar "__bug_contest_caught_species" mon.Species.Dex world

        if updatedPlayer.Party.Length < BoxOps.partyLength then
            player <- { updatedPlayer with Party = updatedPlayer.Party @ [ captured ] }
        else
            let box = updatedPlayer.Pc.Boxes.[updatedPlayer.Pc.CurrentBox]
            if box.Mons.Length < Storage.monsPerBox then
                let newBox = { box with Mons = box.Mons @ [ captured ] }
                let boxes = updatedPlayer.Pc.Boxes |> Array.mapi (fun i b -> if i = updatedPlayer.Pc.CurrentBox then newBox else b)
                player <- { updatedPlayer with Pc = { updatedPlayer.Pc with Boxes = boxes } }
            else
                player <- updatedPlayer

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
                match Trainers.lookupByName group id with
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

        BattleScene(
            content.Font,
            Battle.createTeam playerTeam enemyTeam 0x1234u,
            onBattleEnd = (fun state -> this.SyncBattleParty state),
            bag = player.Bag,
            onBagChange = (fun bag -> player <- { player with Bag = bag }),
            onCatch = (fun mon -> this.CaptureBattleMon mon))

    /// Drive the VM from a run step: enact pure/immediate effects inline (resuming
    /// at once), and for effects that need a child scene, push it and suspend.
    member private this.Drive(step: ScriptStep) : Transition =
        let mutable current = Some step
        let mutable result = Stay
        let mutable finished = false

        while not finished do
            match current with
            | None ->
                finished <- true
            | Some step ->
                current <- None
                world <- step.World

                let resume value vm =
                    current <- Some(Script.resume value world vm)

                let stop transition =
                    result <- transition
                    finished <- true

                match step.Outcome with
                | Completed ->
                    if scriptQueue.Count > 0 then
                        let vm, value = scriptQueue.Dequeue()
                        resume value vm
                    else
                        stop Stay
                | Suspended(vm, effect) ->
                    match effect with
                    // ----- effects that push a child scene and suspend -----
                    | ShowText(label, _faceFirst) ->
                        pending <- Some(vm, effect)
                        let speed = Options.textSpeedDelay player.Options.TextSpeed
                        let rendered = this.ResolveText label
                        lastText <- Some(label, rendered)
                        stop (Push(TextBoxScene.Of(content, rendered, speed) :> Scene))
                    | HallOfFame ->
                        let hallOfFameCount = World.getVar "__hall_of_fame_count" world
                        let allowCreditsSkip = hallOfFameCount > 0
                        world <-
                            world
                            |> World.setEvent "EVENT_BEAT_ELITE_FOUR"
                            |> World.setVar "__hall_of_fame_count" (hallOfFameCount + 1)
                            |> World.setVar "__credits_rolled" 1
                            |> World.setVar "__hall_of_fame_credits_skip" (if allowCreditsSkip then 1 else 0)
                            |> World.setBuffer "__post_credits_spawn" "NewBarkTown"
                        player <- { player with Party = Heal.healParty player.Party }
                        pending <- Some(vm, effect)
                        let speed = Options.textSpeedDelay player.Options.TextSpeed
                        let rendered = "Congratulations! You are the new Champion!<DONE>"
                        lastText <- Some("HallOfFame", rendered)
                        stop (Push(TextBoxScene.Of(content, rendered, speed) :> Scene))
                    | RollCredits ->
                        world <- World.setVar "__credits_rolled" 1 world
                        pending <- Some(vm, effect)
                        let allowSkip = World.hasEvent "EVENT_BEAT_ELITE_FOUR" world
                        stop (Push(CreditsScene(content, allowSkip) :> Scene))
                    | AskYesNo ->
                        pending <- Some(vm, effect)
                        stop (Push(YesNoScene(content.Font, fun r -> yesNoResult <- r) :> Scene))
                    | AskPhoneNumber _ ->
                        pending <- Some(vm, effect)
                        stop (Push(YesNoScene(content.Font, fun r -> askPhoneResult <- r) :> Scene))
                    | SetDayOfWeek ->
                        pending <- Some(vm, effect)
                        stop (Push(WeekdayScene(content, player.GameTime.Weekday, fun weekday ->
                            player <- { player with GameTime = { player.GameTime with Weekday = weekday } }
                            world <- World.setVar "VAR_WEEKDAY" weekday world) :> Scene))
                    | NameRival ->
                        pending <- Some(vm, effect)
                        stop (Push(NamingScene(content.Font, "RIVAL'S NAME", fun name ->
                            world <- World.setBuffer "__rival_name" name world
                            Pop) :> Scene))
                    | NameRater ->
                        pending <- Some(vm, effect)
                        stop (
                            Push(
                                PartyScene(
                                    content,
                                    player,
                                    (fun p -> player <- p),
                                    onSelect = (fun idx ->
                                        let mon = List.item idx player.Party

                                        if this.CanNameRate mon then
                                            Replace(
                                                NamingScene(content.Font, "NICKNAME", fun name ->
                                                    this.RenamePartyMon idx name
                                                    Pop) :> Scene)
                                        else
                                            Pop)) :> Scene))
                    | StartBattle ->
                        pending <- Some(vm, effect)
                        interpretHostEffect (HostEffect.PlaySfx "Sfx_Menu")
                        stop (Push(this.BuildBattle() :> Scene))
                    | GiveItem(item, qty, true) ->
                        this.AddItem item qty
                        pending <- Some(vm, effect)
                        let rendered = item.Replace("_", " ") + "<DONE>"
                        lastText <- Some("VerboseGiveItem", rendered)
                        stop (Push(TextBoxScene.Of(content, rendered) :> Scene))
                    | OpenMart(_martType, items) ->
                        pending <- Some(vm, effect)
                        stop (Push(MartScene(content, player, _martType, items, fun p -> player <- p) :> Scene))
                    | OpenPc ->
                        pending <- Some(vm, effect)
                        stop (Push(PcMenuScene(content, player, fun p -> player <- p) :> Scene))
                    | OpenMomBank ->
                        world <- World.setFlag "ENGINE_MOM_ACTIVE" world
                        pending <- Some(vm, effect)
                        stop (
                            Push(
                                MomBankScene(
                                    content,
                                    player,
                                    World.hasFlag "ENGINE_MOM_SAVING_MONEY" world,
                                    (fun p -> player <- p),
                                    (fun saving ->
                                        world <-
                                            if saving then World.setFlag "ENGINE_MOM_SAVING_MONEY" world
                                            else World.clearFlag "ENGINE_MOM_SAVING_MONEY" world)) :> Scene))
                    | OpenPokegear(tab, mapId, radioChannel) ->
                        pending <- Some(vm, effect)
                        stop (Push(this.MakePokegear(tab, mapId, radioChannel)))
                    | OpenScriptMenu menu ->
                        pending <- Some(vm, effect)
                        menuResult <- 0
                        let optionCount = scriptMenuOptionCount vm.MapId menu
                        stop (Push(ScriptMenuScene(content, menu, optionCount, fun result -> menuResult <- result) :> Scene))
                    | SelectApricornForKurt ->
                        match Apricorns.available player.Bag with
                        | [] -> resume (Some 0) vm
                        | apricorns ->
                            pending <- Some(vm, effect)
                            apricornResult <- None
                            stop (Push(ApricornSelectionScene(content, apricorns, fun result -> apricornResult <- result) :> Scene))
                    | DayCareResident resident ->
                        pending <- Some(vm, effect)
                        dayCareResult <- None
                        stop (
                            Push(
                                DayCareScene(
                                    content,
                                    player,
                                    resident,
                                    (fun p ->
                                        player <- p
                                        this.SyncDayCareFlags()),
                                    (fun result -> dayCareResult <- result)) :> Scene))
                    | DayCareManOutside ->
                        pending <- Some(vm, effect)
                        dayCareResult <- None
                        stop (
                            Push(
                                DayCareScene(
                                    content,
                                    player,
                                    "OUTSIDE",
                                    (fun p ->
                                        player <- p
                                        this.SyncDayCareFlags()),
                                    (fun result -> dayCareResult <- result)) :> Scene))
                    | DayCareMon slot ->
                        let mon =
                            if slot = 1 then player.DayCare.Mon1 else player.DayCare.Mon2

                        let other =
                            if slot = 1 then player.DayCare.Mon2 else player.DayCare.Mon1

                        let rendered =
                            match mon, other with
                            | Some mine, Some theirs ->
                                sprintf "%s is doing fine.\n%s and %s appear to care for each other." mine.Nickname mine.Nickname theirs.Nickname
                            | Some mine, None ->
                                sprintf "%s is doing fine." mine.Nickname
                            | None, _ ->
                                "No #MON is staying here."

                        pending <- Some(vm, effect)
                        lastText <- Some(sprintf "DayCareMon%d" slot, rendered)
                        stop (Push(TextBoxScene.Of(content, rendered + "<DONE>") :> Scene))
                    | MoveDeletion ->
                        pending <- Some(vm, effect)
                        stop (Push(MoveDeletionScene(content, player, fun p -> player <- p) :> Scene))
                    | Haircut brother ->
                        pending <- Some(vm, effect)
                        haircutResult <- 0
                        stop (
                            Push(
                                PartyScene(
                                    content,
                                    player,
                                    (fun p -> player <- p),
                                    onSelect = (fun idx ->
                                        this.ApplyHaircut brother idx
                                        Pop)) :> Scene))
                    | BillsGrandfather ->
                        pending <- Some(vm, effect)
                        billsGrandfatherResult <- 0
                        stop (
                            Push(
                                PartyScene(
                                    content,
                                    player,
                                    (fun p -> player <- p),
                                    onSelect = (fun idx ->
                                        this.SelectBillsGrandfatherMon idx
                                        Pop)) :> Scene))
                    | CheckMagikarpLength ->
                        pending <- Some(vm, effect)
                        magikarpLengthResult <- 1
                        stop (
                            Push(
                                PartyScene(
                                    content,
                                    player,
                                    (fun p -> player <- p),
                                    onSelect = (fun idx ->
                                        let mon = List.item idx player.Party
                                        if mon.SpeciesId = 129 then
                                            let rendered = this.RecordMagikarpLength mon
                                            lastText <- Some("MagikarpGuruMeasureText", rendered)
                                            Replace(TextBoxScene.Of(content, rendered) :> Scene)
                                        else
                                            magikarpLengthResult <- 0
                                            Pop)) :> Scene))
                    | MagikarpHouseSign ->
                        pending <- Some(vm, effect)
                        let rendered = this.MagikarpRecordText()
                        lastText <- Some("KarpGuruRecordText", rendered)
                        stop (Push(TextBoxScene.Of(content, rendered) :> Scene))
                    | UnownPuzzle puzzleId ->
                        pending <- Some(vm, effect)
                        unownPuzzleResult <- 1
                        let rendered = this.UnownPuzzleText puzzleId
                        lastText <- Some("UnownPuzzle", rendered)
                        stop (Push(TextBoxScene.Of(content, rendered) :> Scene))
                    | UnownPrinter ->
                        pending <- Some(vm, effect)
                        let rendered = this.UnownPrinterText()
                        lastText <- Some("UnownPrinter", rendered)
                        stop (Push(TextBoxScene.Of(content, rendered) :> Scene))

                    // ----- immediate effects: enact, continue this frame -----
                    | GiveItem(item, qty, false) ->
                        this.AddItem item qty
                        resume (Some 1) vm
                    | TakeItem(item, qty) ->
                        this.RemoveItem item qty
                        resume (Some 1) vm
                    | CheckItem item ->
                        resume (Some(if Bag.count item player.Bag > 0 then 1 else 0)) vm
                    | CheckFirstMonIsEgg ->
                        let isEgg =
                            player.Party
                            |> List.tryHead
                            |> Option.exists Breeding.isEgg
                        resume (Some(if isEgg then 1 else 0)) vm
                    | InitRoamMons ->
                        world <- Roaming.init world
                        resume None vm
                    | CheckMoney amount ->
                        resume (Some(fundsResult player.Money amount)) vm
                    | GiveMoney amount ->
                        player <- { player with Money = Money.give player.Money amount }
                        resume None vm
                    | TakeMoney amount ->
                        let ok = Money.canAfford player.Money amount
                        if ok then
                            player <- { player with Money = Money.take player.Money amount }
                        resume (Some(if ok then 1 else 0)) vm
                    | CheckCoins amount ->
                        resume (Some(fundsResult player.Coins amount)) vm
                    | GiveCoins amount ->
                        player <- { player with Coins = min 9999 (max 0 (player.Coins + amount)) }
                        resume None vm
                    | TakeCoins amount ->
                        let ok = player.Coins >= amount
                        if ok then
                            player <- { player with Coins = max 0 (player.Coins - amount) }
                        resume (Some(if ok then 1 else 0)) vm
                    | GameCornerGame(game, lucky) ->
                        this.PlayGameCornerGame game lucky
                        resume None vm
                    | RegisterPrizeDex dex ->
                        this.RegisterPrizeDex dex
                        resume None vm
                    | GiveParkBalls ->
                        this.GiveParkBallsForContest()
                        resume None vm
                    | ContestDropOffMons ->
                        resume (Some(this.ContestDropOffMons())) vm
                    | ContestReturnMons ->
                        this.ContestReturnMons()
                        resume None vm
                    | BugContestJudging ->
                        resume (Some(this.BugContestJudgingResult())) vm
                    | CheckPartyFullAfterContest ->
                        resume (Some(this.CheckPartyFullAfterContest())) vm
                    | AddPhoneContact phone ->
                        if Set.contains phone player.PhoneContacts || player.PhoneContacts.Count < 10 then
                            player <- { player with PhoneContacts = Set.add phone player.PhoneContacts }
                            resume (Some 0) vm
                        else
                            resume (Some 1) vm
                    | CheckPhoneContact phone ->
                        resume (Some(if Set.contains phone player.PhoneContacts then 1 else 0)) vm
                    | DisplayBalance display ->
                        balanceOverlay <- Some display
                        resume None vm
                    | CloseWindow ->
                        balanceOverlay <- None
                        resume None vm
                    | SetDstFlag enabled ->
                        player <- { player with GameTime = { player.GameTime with IsDst = enabled } }
                        resume None vm
                    | CheckTime time ->
                        resume (Some(if checkTimeMatches time then 1 else 0)) vm
                    | LoadWild(species, level) ->
                        stagedWild <- Some(species, level)
                        stagedTrainer <- None
                        resume None vm
                    | LoadTrainer(group, id) ->
                        stagedTrainer <- Some(group, id)
                        stagedWild <- None
                        resume None vm
                    | WinLossText(win, loss) ->
                        stagedWinText <- win
                        stagedLossText <- loss
                        resume None vm
                    | GivePoke(species, level, item) ->
                        match Map.tryFind species PokeGold.Game.Data.Species.all with
                        | Some stats ->
                            let mon = PokeGold.Game.Player.PartyMon.create stats.Dex level
                            let mon = match item with Some i -> { mon with HeldItem = Some i } | None -> mon
                            let mon = PokeGold.Game.Player.MoveLearn.seedStartingMoves mon
                            if player.Party.Length < 6 then
                                player <- { player with Party = player.Party @ [ mon ] }
                                resume (Some 1) vm
                            else
                                resume (Some 0) vm
                        | None ->
                            resume (Some 0) vm
                    | CheckPoke species ->
                        match Map.tryFind species PokeGold.Game.Data.Species.all with
                        | Some stats ->
                            let has = player.Party |> List.exists (fun m -> m.SpeciesId = stats.Dex)
                            resume (Some(if has then 1 else 0)) vm
                        | None ->
                            resume (Some 0) vm
                    | SetVisible(obj, visible) ->
                        setObjectVisible obj visible
                        resume None vm
                    | HealParty ->
                        player <- { player with Party = Heal.healParty player.Party }
                        match Map.tryFind "MUSIC_HEAL" MusicData.byId with
                        | Some path -> interpretHostEffect (HostEffect.PlayJingle path)
                        | None -> ()
                        resume None vm
                    | SetLastTalked obj ->
                        lastTalkedActor <- Actor.resolve (OverworldState.objectIndexOf state.MapId) lastTalkedActor obj
                        resume None vm
                    | FacePlayer ->
                        match lastTalkedActor |> Option.bind Actor.objectIndex with
                        | Some idx when idx >= 0 && idx < state.Npcs.Length ->
                            let npc = state.Npcs.[idx]
                            setNpcFacing idx (directionToward npc.CellX npc.CellY state.Player.CellX state.Player.CellY)
                        | _ -> ()
                        resume None vm
                    | FaceObject(a, b) ->
                        match tryActorCell a, tryActorCell b with
                        | Some(actor, ax, ay), Some(_, bx, by) ->
                            setActorFacing actor (directionToward ax ay bx by)
                        | _ -> ()
                        resume None vm
                    | TurnObject(obj, facing) ->
                        match tryActorCell obj, parseDirection facing with
                        | Some(actor, _, _), Some dir -> setActorFacing actor dir
                        | _ -> ()
                        resume None vm
                    | MoveObject(objSym, x, y) ->
                        this.MoveObjectTo(objSym, x, y)
                        resume None vm
                    | Follow(leader, follower) ->
                        match actorOfSymbol follower, actorOfSymbol leader with
                        | Some f, Some l when f <> l -> followPair <- Some(f, l)
                        | _ -> ()
                        resume None vm
                    | StopFollow ->
                        followPair <- None
                        resume None vm
                    | ReanchorMap ->
                        this.ReanchorCamera()
                        resume None vm
                    | Pause frames ->
                        if frames <= 0 then
                            resume None vm
                        else
                            pauseFrames <- frames
                            pauseVm <- Some vm
                            stop Stay
                    | ReloadMap ->
                        this.ReloadCurrentMap()
                        if World.getVar "__dont_restart_map_music" world = 0 then
                            this.PlayMapMusic state.MapId
                        resume None vm
                    | ChangeBlock(x, y, blockId) ->
                        this.ChangeBlockAt(x, y, blockId)
                        resume None vm
                    | PlayMusic song ->
                        this.PlayScriptMusic song
                        resume None vm
                    | PlaySound sfx ->
                        let sfxName =
                            if SongsData.byName.ContainsKey sfx then
                                Some sfx
                            else
                                let stem =
                                    if sfx.StartsWith("SFX_", StringComparison.OrdinalIgnoreCase) then
                                        sfx.Substring(4)
                                    else
                                        sfx
                                let pascal =
                                    stem.Split('_', StringSplitOptions.RemoveEmptyEntries)
                                    |> Array.map (fun p ->
                                        let lower = p.ToLowerInvariant()
                                        if lower.Length = 0 then lower else lower.Substring(0, 1).ToUpperInvariant() + lower.Substring(1))
                                    |> String.concat ""
                                let candidate = "Sfx_" + pascal
                                if SongsData.byName.ContainsKey candidate then Some candidate else None

                        sfxName |> Option.iter (HostEffect.PlaySfx >> interpretHostEffect)
                        resume None vm
                    | ScriptEffect.Cry _ ->
                        resume None vm
                    | WaitSfx ->
                        resume None vm
                    | ApplyMovement(objSym, label) ->
                        match this.TryStartMovement(vm, objSym, label) with
                        | true -> stop Stay
                        | false -> resume None vm
                    | ScriptEffect.Warp(map, x, y, facing) ->
                        match OverworldState.tryWarpExplicit content map x y facing state.Player.Facing with
                        | Some ns ->
                            scriptQueue.Enqueue(vm, None)
                            stop (this.EnterMap(ns, true))
                        | None ->
                            resume None vm

        result

    /// Begin an `applymovement`: resolve the symbolic actor to a live NPC and look up
    /// its baked movement script. Returns `true` if a run was started (the scene then
    /// suspends and ticks it each frame); `false` if either couldn't be resolved, so
    /// the caller resumes the VM immediately (a faithful no-op for unmodelled actors).
    member private _.TryStartMovement(vm: ScriptVm, objSym: string, label: string) : bool =
        let walkable = MapConnections.cellWalkable state.Map state.Collision state.Neighbors

        match actorOfSymbol objSym, OverworldState.movementScript state.MapId label with
        | Some ActorId.Player, Some cmds ->
            let playerNpc : NpcObject =
                { Event =
                    { X = state.Player.CellX
                      Y = state.Player.CellY
                      Sprite = ""
                      Movement = ""
                      RadiusX = 0
                      RadiusY = 0
                      Hour1 = 0
                      Hour2 = 0
                      Palette = ""
                      Type = ""
                      Sight = 0
                      Script = ""
                      EventFlag = None }
                  Kind = StandStill
                  HomeX = state.Player.CellX
                  HomeY = state.Player.CellY
                  RadiusX = 0
                  RadiusY = 0
                  CellX = state.Player.CellX
                  CellY = state.Player.CellY
                  SrcX = state.Player.SrcX
                  SrcY = state.Player.SrcY
                  Facing = state.Player.Facing
                  Motion = NpcStanding
                  Progress = 0
                  AnimFrame = state.Player.AnimFrame
                  Sleep = 0
                  Seed = 0u }

            let run = MovementRunner.start walkable cmds playerNpc
            runningMove <- Some(vm, ActorId.Player, run)
            true
        | Some(ActorId.Object i), Some cmds when i >= 0 && i < state.Npcs.Length ->
            let run = MovementRunner.start walkable cmds state.Npcs.[i]
            runningMove <- Some(vm, ActorId.Object i, run)
            true
        | _ -> false

    /// Explicit debug boot into the old Azalea vertical-slice seed. Normal game
    /// startup goes through Title/MainMenu/NewGame or save loading instead.
    static member DebugLoadAzalea(content: Content, sound: ISoundBoard) : OverworldScene =
        let ow = OverworldScene(content, sound, OverworldState.loadAzalea content)
        // Seed Azalea-appropriate debug state (story flags + party)
        let debugWorld =
            World.empty
            |> World.setEvent "EVENT_GOT_A_POKEMON_FROM_ELM"
            |> World.setEvent "EVENT_GAVE_MYSTERY_EGG_TO_ELM"
            |> World.setEvent "EVENT_GOT_POKEDEX"
            |> World.setEvent "EVENT_CLEARED_SLOWPOKE_WELL"
            |> World.setEvent "EVENT_AZALEA_TOWN_SLOWPOKETAIL_ROCKET"
            |> World.setEvent "EVENT_SLOWPOKE_WELL_ROCKETS"
            |> World.setEvent "EVENT_SLOWPOKE_WELL_KURT"
            |> World.setFlag "ENGINE_ZEPHYRBADGE"
            |> World.setFlag "ENGINE_HIVEBADGE"
            |> World.setFlag "ENGINE_FLYPOINT_AZALEA"
            |> World.setFlag "ENGINE_FLYPOINT_VIOLET"
            |> World.setFlag "ENGINE_FLYPOINT_CHERRYGROVE"
            |> World.setFlag "ENGINE_FLYPOINT_NEWBARK"
        ow.Restore(debugWorld, DebugSeed.seed PlayerStateOps.initial)
        ow

    /// Restore an overworld scene from a save (position, world flags, and player state).
    static member OfSave(content: Content, sound: ISoundBoard, save: SaveData) : OverworldScene =
        let scene = OverworldScene(content, sound, SaveData.apply content save)
        scene.Restore(SaveData.worldOf save, SaveData.playerOf save)
        scene

    member private _.CaptureBlockers() : string list =
        [ if pending.IsSome then "pending child scene"
          if runningMove.IsSome then "scripted movement"
          if pauseVm.IsSome || pauseFrames > 0 then "script pause"
          if scriptQueue.Count > 0 then "queued script continuation"
          if followPair.IsSome then "active follow relationship" ]

    /// True when all transient runtime state is idle and a persistent save snapshot
    /// can faithfully represent the overworld.
    member this.CanCapture: bool = this.CaptureBlockers().IsEmpty

    /// Snapshot this scene's persistable state (position, world flags, player state).
    member this.Capture() : SaveData =
        match this.CaptureBlockers() with
        | [] -> SaveData.captureWith state world player
        | blockers ->
            invalidOp ("Cannot capture overworld while transient runtime state is active: " + String.concat ", " blockers)

    /// Seed the script world and player state, then run callbacks and scene script.
    member this.Restore(w: World, p: PlayerState) =
        world <- w
        player <- p
        balanceOverlay <- None
        scriptQueue.Clear()
        resetObjectPresence ()
        this.RunMapCallbacks state.MapId
        syncFlaggedObjectPresenceFromWorld ()
        match this.RunSceneScript state.MapId with
        | Stay -> restoreTransition <- None
        | transition -> restoreTransition <- Some transition

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

    /// Typed runtime snapshot for tests and tooling. This is the non-string API the
    /// debug pipe can adapt from and the deterministic e2e harness can assert on.
    member this.RuntimeSnapshot: RuntimeOverworldSnapshot =
        let p = state.Player
        let px, py = Player.worldPixel p

        let actors =
            state.Npcs
            |> Array.mapi (fun i n ->
                let nx, ny = NpcObject.worldPixel n
                { Index = i
                  Sprite = n.Event.Sprite
                  CellX = n.CellX
                  CellY = n.CellY
                  PixelX = nx
                  PixelY = ny
                  Facing = n.Facing
                  Motion = string n.Motion
                  Visible = isObjectPresent i
                  EventFlag = n.Event.EventFlag
                  Script = n.Event.Script })
            |> Array.toList

        { MapId = state.MapId
          Player =
            { CellX = p.CellX
              CellY = p.CellY
              PixelX = px
              PixelY = py
              Facing = p.Facing
              Motion = string p.Motion
              Moving = p.Moving
              Name = player.Name
              PartyCount = player.Party.Length
              PartySpecies = player.Party |> List.map (fun mon -> mon.SpeciesId)
              PhoneContacts = player.PhoneContacts |> Set.toList
              GameTimeWeekday = player.GameTime.Weekday
              GameTimeIsDst = player.GameTime.IsDst
              Money = player.Money }
          Actors = actors
          LastTextLabel = lastText |> Option.map fst
          LastRenderedText = lastText |> Option.map snd
          EventCount = world.Events.Count
          EngineFlagCount = world.EngineFlags.Count
          Events = world.Events |> Set.toList
          EngineFlags = world.EngineFlags |> Set.toList
          Vars = world.Vars
          Scenes = world.Scenes
          SceneId = World.getScene state.MapId world
          CanCapture = this.CanCapture }

    /// Whether an NPC object is currently present (event-flag gated).
    member _.DebugVisible(o: ObjectEvent) : bool =
        state.Npcs
        |> Array.tryFindIndex (fun n -> n.Event = o)
        |> Option.map isObjectPresent
        |> Option.defaultWith (fun () -> MapEvents.objectVisible world o)

    /// Set or clear an `EVENT_*` story flag on the live world.
    member _.DebugSetEvent (flag: string) (value: bool) =
        world <- (if value then World.setEvent flag world else World.clearEvent flag world)

    /// Set or clear an `ENGINE_*` flag on the live world.
    member _.DebugSetFlag (flag: string) (value: bool) =
        world <- (if value then World.setFlag flag world else World.clearFlag flag world)

    /// Write a `VAR_*` game variable on the live world.
    member _.DebugSetVar (var: string) (value: int) = world <- World.setVar var value world

    /// Write a map scene id on the live world.
    member _.DebugSetScene (mapId: string) (scene: int) = world <- World.setScene mapId scene world

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
        scriptQueue.Clear()
        pending <- None
        restoreTransition <- None

        match this.EnterMap(ns, true) with
        | Stay -> ()
        | transition -> restoreTransition <- Some transition

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
            let transitionFromRestore = restoreTransition
            restoreTransition <- None

            match transitionFromRestore, runningMove with
            | Some transition, _ -> transition
            // An applymovement is animating: advance the actor a frame, write it back,
            // and resume the suspended script once the run reaches step_end.
            | None, Some(vm, actor, run) ->
                let walkable = MapConnections.cellWalkable state.Map state.Collision state.Neighbors
                let run' = MovementRunner.step walkable run

                match actor with
                | ActorId.Player ->
                    let playerMotion =
                        match run'.Npc.Motion with
                        | NpcWalking -> Walking
                        | NpcStanding -> Standing

                    let player =
                        { state.Player with
                            CellX = run'.Npc.CellX
                            CellY = run'.Npc.CellY
                            Facing = run'.Npc.Facing
                            Motion = playerMotion
                            SrcX = run'.Npc.SrcX
                            SrcY = run'.Npc.SrcY
                            Progress = run'.Npc.Progress
                            AnimFrame = run'.Npc.AnimFrame }
                    let camX, camY = Camera.followExt state.Map state.Neighbors player
                    state <-
                        { state with
                            Player = player
                            CamX = camX
                            CamY = camY }
                | ActorId.Object i ->
                    let npcs = Array.copy state.Npcs
                    if i < npcs.Length then
                        npcs.[i] <- run'.Npc
                    state <- { state with Npcs = npcs }

                match followPair with
                | Some(follower, leader) when leader = actor && follower <> actor ->
                    advanceFollowMotion follower
                    if run'.Npc.Moving then
                        setFollowerStep follower run'.Npc.SrcX run'.Npc.SrcY
                | Some(follower, _) when follower <> actor ->
                    advanceFollowMotion follower
                | _ -> ()

                if run'.Done then
                    runningMove <- None
                    this.Drive(Script.resume None world vm)
                else
                    runningMove <- Some(vm, actor, run')
                    Stay
            | None, None ->

            if pauseFrames > 0 then
                pauseFrames <- pauseFrames - 1

                if pauseFrames = 0 then
                    match pauseVm with
                    | Some vm ->
                        pauseVm <- None
                        this.Drive(Script.resume None world vm)
                    | None -> Stay
                else
                    Stay
            else

            match pending with
            // A pushed child scene popped — resume the suspended script with its result.
            | Some(vm, effect) ->
                pending <- None
                prevA <- buttons.A
                prevStart <- buttons.Start

                match effect with
                | HallOfFame when not hallOfFameCreditsShown ->
                    hallOfFameCreditsShown <- true
                    pending <- Some(vm, effect)
                    let allowSkip = World.getVar "__hall_of_fame_credits_skip" world <> 0
                    Push(CreditsScene(content, allowSkip) :> Scene)
                | _ ->
                    let value =
                        match effect with
                        | RollCredits -> None
                        | HallOfFame -> None
                        | AskYesNo -> Some yesNoResult
                        | OpenScriptMenu _ -> Some menuResult
                        | SelectApricornForKurt ->
                            match apricornResult with
                            | Some apricorn ->
                                this.RemoveItem apricorn 1
                                Some(Apricorns.itemId apricorn)
                            | None -> Some 0
                        | DayCareResident _ -> dayCareResult
                        | DayCareManOutside -> dayCareResult
                        | DayCareMon _ -> None
                        | MoveDeletion -> None
                        | Haircut _ -> Some haircutResult
                        | BillsGrandfather -> Some billsGrandfatherResult
                        | CheckMagikarpLength -> Some magikarpLengthResult
                        | MagikarpHouseSign -> None
                        | UnownPuzzle _ -> Some unownPuzzleResult
                        | UnownPrinter -> None
                        | AskPhoneNumber phone ->
                            if askPhoneResult = 0 then
                                Some 2
                            elif Set.contains phone player.PhoneContacts || player.PhoneContacts.Count < 10 then
                                player <- { player with PhoneContacts = Set.add phone player.PhoneContacts }
                                Some 0
                            else
                                Some 1
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
                                                match Trainers.lookupByName group id with
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
                                        let baseReward =
                                            match stagedTrainer with
                                            | Some(group, id) ->
                                                match Trainers.lookupByName group id with
                                                | Some trainer ->
                                                    let lastMonLevel =
                                                        trainer.Party
                                                        |> List.tryLast
                                                        |> Option.map (fun mon -> mon.Level)
                                                        |> Option.defaultValue enemyLevel
                                                    Experience.moneyEarned trainer.BaseReward lastMonLevel
                                                | None -> Experience.moneyEarned 25 enemyLevel
                                            | None -> 0

                                        let hasAmuletCoin =
                                            player.Party
                                            |> List.exists (fun mon -> mon.HeldItem = Some "AMULET_COIN")
                                        let reward = Experience.applyAmuletCoin hasAmuletCoin baseReward
                                        player <- { player with Money = Money.give player.Money reward }
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
                        | GiveItem(_, _, true) -> Some 1  // verbose give succeeded
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
                        | Pokemon -> Push(PartyScene(content, player, (fun p -> player <- p), onFieldMove = this.UseFieldMove) :> Scene)
                        | Pack    ->
                            // TODO: once PackScene reports which rod was used, wire that
                            // result back into the overworld so fishing checks the
                            // facing water tile and stages fishEncounter here.
                            Push(PackScene(content, player, fun p -> player <- p) :> Scene)
                        | Pokegear -> Push(this.MakePokegear(PhoneTab, state.MapId, None))
                        | Save    -> Push(SaveMenuScene(content, player.Name, fun () -> SaveFile.write (this.Capture())) :> Scene)
                        | Option  -> Push(OptionsScene(content, player, fun p -> player <- p) :> Scene)
                        | Exit    -> Pop), buttons) :> Scene)
                elif aPressed && not state.Player.Moving then
                    let fx, fy = Triggers.facedCell state.Player.CellX state.Player.CellY state.Player.Facing
                    let collId = MapConnections.collisionId state.Map state.Collision state.Neighbors fx fy

                    // Objects on special collision tiles still take A-button priority
                    // over field moves (e.g. Lake of Rage Red Gyarados sits on water).
                    let mutable talkedNpcCandidate: ActorId option = None

                    let objectScriptAt fx fy =
                        state.Npcs
                        |> Array.mapi (fun i n -> i, n)
                        |> Array.tryFind (fun (i, n) ->
                            isObjectPresent i && n.CellX = fx && n.CellY = fy)
                        |> Option.map (fun (i, _) ->
                            let script = state.Npcs.[i].Event.Script

                            if script <> "" && script <> "ObjectEvent" then
                                talkedNpcCandidate <- Some(ActorId.Object i)

                            script)

                    let startScript label =
                        lastTalkedActor <- talkedNpcCandidate
                        interpretHostEffect (HostEffect.PlaySfx "Sfx_Menu")
                        this.Drive(Script.start label world state.Script state.MapId)

                    match objectScriptAt fx fy with
                    | Some label when state.Script.Labels.ContainsKey label -> startScript label
                    | _ ->
                        // A counter/desk tile in front of the player: GSC reaches one tile
                        // past it to the NPC behind (the Mart clerk, Center nurse, etc.).
                        let isCounter fx fy =
                            Collision.isCounterId (
                                MapConnections.collisionId state.Map state.Collision state.Neighbors fx fy)

                        match Triggers.actionScript objectScriptAt isCounter state.Events state.Player.CellX state.Player.CellY state.Player.Facing with
                        | Some label when state.Script.Labels.ContainsKey label -> startScript label
                        | _ ->
                            match collId with
                            | id when id = FieldMoves.CollCutTree || id = FieldMoves.CollCutTree1A ->
                                this.UseFieldMove "CUT"
                            | id when id = FieldMoves.CollSurf || id = FieldMoves.CollWater21 ->
                                this.UseFieldMove "SURF"
                            | id when id = FieldMoves.CollWhirlpool || id = FieldMoves.CollWhirlpool2C ->
                                this.UseFieldMove "WHIRLPOOL"
                            | id when
                                id = FieldMoves.CollWaterfallRight
                                || id = FieldMoves.CollWaterfallLeft
                                || id = FieldMoves.CollWaterfallUp
                                || id = FieldMoves.CollWaterfall ->
                                this.UseFieldMove "WATERFALL"
                            | _ -> Stay
                else
                    let playerBefore = state.Player
                    let leaderBefore = followPair |> Option.bind (fun (_, leader) -> actorCell leader)
                    if not (tryPushStrengthBoulder buttons) then
                        state <- OverworldState.tickWithPlayerWalkable (Some this.PlayerTerrainWalkable) (fun i _ -> isObjectPresent i) buttons state
                    let after = state.Player.CellX, state.Player.CellY
                    let completedTranslation =
                        match playerBefore.Motion with
                        | Walking
                        | Hopping -> not state.Player.Moving
                        | _ -> false

                    if completedTranslation then
                        applyStrengthBoulderHoleFalls ()

                    match followPair, leaderBefore with
                    | Some(follower, leader), Some(lx, ly) ->
                        match actorCell leader with
                        | Some now when now <> (lx, ly) -> setFollowerStep follower lx ly
                        | _ -> ()
                    | _ -> ()

                    // Overworld locomotion SFX (GSC plays these as the action begins):
                    // a ledge hop on its first frame, a wall bump on its rising edge.
                    // Plain walking is silent.
                    if state.Player.Motion = Hopping && state.Player.Progress = 0 then
                        interpretHostEffect (HostEffect.PlaySfx "Sfx_JumpOverLedge")
                    elif state.Player.Bumped then
                        interpretHostEffect (HostEffect.PlaySfx "Sfx_Bump")

                    // Walking off the current map into a connected neighbour swaps the
                    // active map once the step settles (player rebased to the same world
                    // position, so the view is seamless).
                    match if completedTranslation then OverworldState.crossConnection content state else None with
                    | Some ns -> this.EnterMap(ns, true)
                    | None when not completedTranslation ->
                        Stay
                    | None ->
                        if player.RepelSteps > 0 then
                            player <- { player with RepelSteps = player.RepelSteps - 1 }

                        let mutable encounterTransition = Stay
                        let collId = MapConnections.collisionId state.Map state.Collision state.Neighbors (fst after) (snd after)

                        match WildEncounter.tryEncounter state.MapId collId encounterRng player world with
                        | Some(species, level) ->
                            stagedWild <- Some(species, level)
                            stagedTrainer <- None
                            encounterTransition <- Push(this.BuildBattle() :> Scene)
                        | None -> ()

                        match encounterTransition with
                        | Push _ -> encounterTransition
                        | _ ->
                            match TrainerSight.checkTrainerSightPresent state.Npcs isObjectPresent (fst after) (snd after) with
                            | Some npcIdx ->
                                let npc = state.Npcs.[npcIdx]

                                if state.Script.Labels.ContainsKey npc.Event.Script then
                                    interpretHostEffect (HostEffect.PlaySfx "Sfx_Menu")
                                    this.Drive(Script.start npc.Event.Script world state.Script state.MapId)
                                else
                                    Stay
                            | None ->
                                // Stepping onto a warp tile sends the player to its paired
                                // warp on the destination map (a no-op until that map is
                                // wired up). Otherwise a coord trigger may fire.
                                match MapEvents.warpAt (fst after) (snd after) state.Events with
                                | Some w ->
                                    match OverworldState.tryWarp content w.DestMap w.DestWarp with
                                    | Some dest -> this.EnterMap(dest, true)
                                    | None -> Stay
                                | None ->
                                    let currentScene =
                                        MapEvents.sceneAt (World.getScene state.MapId world) state.Events

                                    match Triggers.coordToFire currentScene firedCoords state.Events (fst after) (snd after) with
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
            for i = 0 to state.Npcs.Length - 1 do
                let n = state.Npcs.[i]
                if isObjectPresent i then
                    let spriteName =
                        World.getBuffer ("__sprite_" + n.Event.Sprite) world
                        |> fun s -> if s = "" then n.Event.Sprite else s

                    match this.SpriteFor spriteName with
                    | Some spr ->
                        let frame, flip = NpcObject.frameAndFlip n
                        let px, py = NpcObject.worldPixel n
                        SpriteRenderer.draw fb state.SpritePalette spr frame (px - state.CamX) (py - state.CamY) flip
                    | None -> ()

            match balanceOverlay with
            | None -> ()
            | Some MoneyTopRight ->
                WindowRenderer.drawBox fb content.Font TextRenderer.palette 11 0 9 3
                WindowRenderer.drawString fb content.Font TextRenderer.palette 12 1 (sprintf "$%d" player.Money)
            | Some CoinCase ->
                WindowRenderer.drawBox fb content.Font TextRenderer.palette 11 0 8 3
                WindowRenderer.drawString fb content.Font TextRenderer.palette 12 0 "COIN"
                WindowRenderer.drawString fb content.Font TextRenderer.palette 13 1 (string player.Coins)
            | Some MoneyAndCoins ->
                WindowRenderer.drawBox fb content.Font TextRenderer.palette 5 0 14 5
                WindowRenderer.drawString fb content.Font TextRenderer.palette 6 1 (sprintf "MONEY $%d" player.Money)
                WindowRenderer.drawString fb content.Font TextRenderer.palette 6 3 (sprintf "COIN %d" player.Coins)
