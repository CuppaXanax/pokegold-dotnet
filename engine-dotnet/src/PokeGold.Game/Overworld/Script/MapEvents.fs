namespace PokeGold.Game.Overworld.Script

/// Queries over a map's parsed [`MapEvents`](#): visibility (gated on the world's
/// event flags) and per-cell lookups the integration layer (M9.4) uses to decide
/// what a step or an A-press triggers. The `MapEvents`/`WarpEvent`/… record types
/// themselves live in `PokeGold.MapData` (shared with the build-time generator);
/// this companion module holds the runtime queries that depend on `World`.
module MapEvents =

    /// A map with no events (for maps whose `.asm` isn't wired up yet).
    let empty: MapEvents =
        { Warps = [||]
          Coords = [||]
          Bgs = [||]
          Objects = [||]
          Scenes = [||]
          SceneLabels = [||]
          Callbacks = [||] }

    /// The scene the map starts in: the first entry of its scene table (mirroring
    /// `wCurMapSceneID` defaulting to 0), or `""` if the map has no scenes.
    let defaultScene (events: MapEvents) : string =
        if events.Scenes.Length > 0 then events.Scenes.[0] else ""

    /// The scene script LABEL for a world scene id. Used by the integration layer
    /// to run the correct scene script on map entry.
    let sceneLabelAt (sceneId: int) (events: MapEvents) : string =
        if sceneId >= 0 && sceneId < events.SceneLabels.Length then
            events.SceneLabels.[sceneId]
        else ""

    /// The scene CONSTANT NAME for a world scene id, for coord event matching.
    let sceneAt (sceneId: int) (events: MapEvents) : string =
        if sceneId >= 0 && sceneId < events.Scenes.Length then
            events.Scenes.[sceneId]
        else
            defaultScene events

    /// Is this object currently present, given the world's event flags? An object
    /// with no `EventFlag` is always present; otherwise it is hidden while its
    /// flag is set (GSC semantics: `EVENT_*` means "hidden when set").
    let objectVisible (world: World) (o: ObjectEvent) : bool =
        match o.EventFlag with
        | None -> true
        | Some flag -> not (World.hasEvent flag world)

    /// The objects currently present in the world (visibility-filtered).
    let visibleObjects (world: World) (events: MapEvents) : ObjectEvent[] =
        events.Objects |> Array.filter (objectVisible world)

    /// The warp on cell `(x, y)`, if any.
    let warpAt (x: int) (y: int) (events: MapEvents) : WarpEvent option =
        events.Warps |> Array.tryFind (fun w -> w.X = x && w.Y = y)

    /// The coordinate trigger on cell `(x, y)`, if any.
    let coordAt (x: int) (y: int) (events: MapEvents) : CoordEvent option =
        events.Coords |> Array.tryFind (fun c -> c.X = x && c.Y = y)

    /// The sign/bg event on cell `(x, y)`, if any.
    let bgAt (x: int) (y: int) (events: MapEvents) : BgEvent option =
        events.Bgs |> Array.tryFind (fun b -> b.X = x && b.Y = y)

    /// The visible object standing on cell `(x, y)`, if any — used to resolve the
    /// NPC the player is facing when they press A.
    let objectAt (world: World) (x: int) (y: int) (events: MapEvents) : ObjectEvent option =
        visibleObjects world events |> Array.tryFind (fun o -> o.X = x && o.Y = y)
