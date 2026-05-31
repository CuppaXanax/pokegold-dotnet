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

    /// The script label an A-press runs: the visible object on the faced cell if
    /// it has one, otherwise a sign/bg event there. `None` if nothing is
    /// interactive in front of the player.
    let actionScript (world: World) (events: MapEvents) (cellX: int) (cellY: int) (facing: Direction) : string option =
        let fx, fy = facedCell cellX cellY facing

        match MapEvents.objectAt world fx fy events with
        | Some o when o.Script <> "" && o.Script <> "ObjectEvent" -> Some o.Script
        | _ -> MapEvents.bgAt fx fy events |> Option.bind (fun b -> if b.Script <> "" then Some b.Script else None)

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
