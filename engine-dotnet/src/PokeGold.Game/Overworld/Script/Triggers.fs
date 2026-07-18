namespace PokeGold.Game.Overworld.Script

open PokeGold.Game.Core

/// The pure decision logic the overworld scene uses to turn player actions into
/// script runs: which script an A-press triggers (the NPC/sign the player faces)
/// and which coord trigger a step fires. Kept separate from the (stateful) scene
/// so it can be unit-tested directly.
module Triggers =

    /// The unit step for a facing direction.
    let private facingDelta (dir: Direction) : int * int =
        match dir with
        | Down -> 0, 1
        | Up -> 0, -1
        | Left -> -1, 0
        | Right -> 1, 0

    /// The cell the player at `(cellX, cellY)` is facing.
    let facedCell (cellX: int) (cellY: int) (facing: Direction) : int * int =
        let dx, dy = facingDelta facing
        cellX + dx, cellY + dy

    /// The script label an A-press runs: the object on the faced cell if it has a
    /// real script, otherwise a sign/bg event there. `None` if nothing is interactive
    /// in front of the player. `objectScriptAt fx fy` resolves the script of whatever
    /// object currently stands on a cell — the caller supplies it over the *live*
    /// object set (so a wandering NPC is talked to where it now stands, not at its
    /// spawn tile), filtered to visible objects. `isCounter fx fy` reports whether the
    /// faced cell is a shop/desk counter tile: GSC reaches one tile *past* a counter to
    /// the NPC standing behind it (so you can talk to a Mart clerk or Center nurse
    /// across the desk), mirroring `CheckFacingObject`'s counter-distance doubling.
    let actionScript
        (objectScriptAt: int -> int -> string option)
        (isCounter: int -> int -> bool)
        (events: MapEvents)
        (cellX: int)
        (cellY: int)
        (facing: Direction)
        : string option =
        let fx, fy = facedCell cellX cellY facing

        // Across a counter, reflect the search past the desk: (2*faced - player).
        let ox, oy =
            if isCounter fx fy then (2 * fx - cellX, 2 * fy - cellY) else (fx, fy)

        match objectScriptAt ox oy with
        | Some s when s <> "" && s <> "ObjectEvent" -> Some s
        | _ -> MapEvents.bgAt fx fy events |> Option.bind (fun b -> if b.Script <> "" then Some b.Script else None)

    /// Resolve a background event's source `conditional_event FLAG, .Script`
    /// header. The macro is map-event data, not a VM opcode: IFSET runs its body
    /// only when the flag is set and IFNOTSET only when it is clear.
    let conditionalBgScript (world: World) (bg: BgEvent) (program: ScriptProgram) : string option =
        match bg.Kind with
        | "BGEVENT_IFSET"
        | "BGEVENT_IFNOTSET" ->
            let header =
                match program.Labels.TryFind bg.Script with
                | Some pc when pc >= 0 && pc < program.Commands.Length ->
                    match program.Commands.[pc] with
                    | ConditionalEvent(flag :: target :: _) -> Some(flag, target)
                    | _ -> None
                | _ -> None

            match header with
            | Some(flag, target) ->
                let flagSet = World.hasEvent flag world
                let allowed =
                    (bg.Kind = "BGEVENT_IFSET" && flagSet)
                    || (bg.Kind = "BGEVENT_IFNOTSET" && not flagSet)

                if allowed then
                    if target.StartsWith "." then Some(bg.Script + target) else Some target
                else
                    None
            | None -> None
        | _ -> Some bg.Script

    /// The coord trigger a step onto `(cellX, cellY)` fires: one on that cell whose
    /// scene is the map's active scene and that hasn't fired yet. `None` otherwise.
    let coordToFire
        (activeScene: string)
        (fired: Set<int * int>)
        (events: MapEvents)
        (cellX: int)
        (cellY: int)
        : CoordEvent option =
        match MapEvents.coordAt cellX cellY events with
        | Some c when c.Scene = activeScene && not (Set.contains (cellX, cellY) fired) -> Some c
        | _ -> None
