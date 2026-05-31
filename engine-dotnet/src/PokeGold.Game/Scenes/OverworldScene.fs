namespace PokeGold.Game.Scenes

open System.Collections.Generic
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Audio
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
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
    /// The player's bag (item constant → quantity).
    let mutable bag: Map<string, int> = Map.empty
    /// A suspended script awaiting the child scene we pushed for an effect.
    let mutable pending: (ScriptVm * ScriptEffect) option = None
    /// The most recent yes/no choice, written by the YesNoScene callback.
    let mutable yesNoResult = 0
    let mutable prevA = false
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

    /// Resolve a text label to its M5 token string; unknown labels show the label.
    member private _.ResolveText(label: string) : string =
        match Map.tryFind label state.Text with
        | Some s -> s
        | None -> label + "<DONE>"

    /// Add `qty` of an item to the bag.
    member private _.AddItem (item: string) (qty: int) =
        let cur = bag |> Map.tryFind item |> Option.defaultValue 0
        bag <- Map.add item (cur + qty) bag

    /// Remove up to `qty` of an item from the bag.
    member private _.RemoveItem (item: string) (qty: int) =
        let cur = bag |> Map.tryFind item |> Option.defaultValue 0
        let left = max 0 (cur - qty)
        bag <- if left = 0 then Map.remove item bag else Map.add item left bag

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
                Push(TextBoxScene.Of(content, this.ResolveText label) :> Scene)
            | AskYesNo ->
                pending <- Some(vm, effect)
                Push(YesNoScene(content.Font, fun r -> yesNoResult <- r) :> Scene)
            | StartBattle ->
                pending <- Some(vm, effect)
                sound.PlaySfx "Sfx_Menu"
                Push(BattleScene.StartDemo content :> Scene)
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
                this.Drive(Script.resume (Some(if bag.ContainsKey item then 1 else 0)) world vm)
            // ----- effects out of M9.4 scope: no-op, resume -----
            | SetLastTalked _
            | ApplyMovement _
            | FacePlayer
            | FaceObject _
            | SetVisible _
            | TurnObject _
            | LoadWild _
            | LoadTrainer _
            | WinLossText _
            | ReloadMap
            | PlayMusic _
            | PlaySound _
            | ScriptEffect.Cry _
            | WaitSfx -> this.Drive(Script.resume None world vm)
            // ----- warp: load the destination map, then continue the script -----
            | ScriptEffect.Warp(map, x, y, facing) ->
                match OverworldState.tryWarpExplicit content map x y facing state.Player.Facing with
                | Some ns ->
                    state <- ns
                    firedCoords <- Set.empty
                | None -> ()

                this.Drive(Script.resume None world vm)

    /// Load the Azalea Town overworld scene through the shared asset cache.
    static member Load(content: Content, sound: ISoundBoard) : OverworldScene =
        OverworldScene(content, sound, OverworldState.loadAzalea content)

    /// Restore an overworld scene from a save (position, world flags, and bag).
    static member OfSave(content: Content, sound: ISoundBoard, save: SaveData) : OverworldScene =
        let scene = OverworldScene(content, sound, SaveData.apply content save)
        scene.Restore(SaveData.worldOf save, SaveData.bagOf save)
        scene

    /// Snapshot this scene's persistable state (position, world flags, bag).
    member _.Capture() : SaveData = SaveData.captureWith state world bag

    /// Seed the script world and bag onto a freshly built scene (used by OfSave).
    member _.Restore(w: World, b: Map<string, int>) =
        world <- w
        bag <- b

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
            match pending with
            // A pushed child scene popped — resume the suspended script with its result.
            | Some(vm, effect) ->
                pending <- None
                prevA <- buttons.A

                let value =
                    match effect with
                    | AskYesNo -> Some yesNoResult
                    | StartBattle -> Some 1
                    | _ -> None

                this.Drive(Script.resume value world vm)
            | None ->
                let aPressed = buttons.A && not prevA
                prevA <- buttons.A

                if aPressed && not state.Player.Moving then
                    // Talk to / read whatever the player faces.
                    match Triggers.actionScript world state.Events state.Player.CellX state.Player.CellY state.Player.Facing with
                    | Some label when state.Script.Labels.ContainsKey label ->
                        sound.PlaySfx "Sfx_Menu"
                        this.Drive(Script.start label world state.Script)
                    | _ -> Stay
                else
                    let before = state.Player.CellX, state.Player.CellY
                    state <- OverworldState.tick buttons state
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
                        firedCoords <- Set.empty
                        Stay
                    | None ->

                    if after = before then
                        Stay
                    else
                        // Stepping onto a warp tile sends the player to its paired
                        // warp on the destination map (a no-op until that map is
                        // wired up). Otherwise a coord trigger may fire.
                        match MapEvents.warpAt (fst after) (snd after) state.Events with
                        | Some w ->
                            match OverworldState.tryWarp content w.DestMap w.DestWarp with
                            | Some dest ->
                                state <- dest
                                firedCoords <- Set.empty
                                Stay
                            | None -> Stay
                        | None ->
                            match Triggers.coordToFire activeScene firedCoords state.Events (fst after) (snd after) with
                            | Some c when state.Script.Labels.ContainsKey c.Script ->
                                firedCoords <- Set.add after firedCoords
                                this.Drive(Script.start c.Script world state.Script)
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
