namespace PokeGold.Game.Overworld

open PokeGold.Game.Core

/// The autonomous NPC-movement system: a pure stepper that advances every live
/// object one frame, reproducing GSC's wander state machine
/// (`engine/overworld/map_objects.asm`) at a high level — sleep a random spell,
/// pick a random direction, walk a tile if it's clear and inside the wander
/// radius, otherwise re-sleep. Randomness comes from a small LCG seeded per object
/// so the whole thing is deterministic and unit-testable.
module ObjectStep =

    /// A 32-bit linear congruential generator (Numerical Recipes constants). Stands
    /// in for the hardware `Random` routine — we only need plausible, deterministic
    /// pseudo-randomness for wandering, not bit-exact ROM parity.
    module Lcg =
        let next (s: uint32) : uint32 = s * 1664525u + 1013904223u
        /// The top byte of a state, mirroring GSC reading `hRandomAdd`.
        let byteOf (s: uint32) : int = int ((s >>> 24) &&& 0xFFu)

    /// Advance the seed and return (randomByte, newSeed).
    let private draw (s: uint32) : int * uint32 =
        let s' = Lcg.next s
        Lcg.byteOf s', s'

    let private delta (d: Direction) : int * int =
        match d with
        | Down -> 0, 1
        | Up -> 0, -1
        | Left -> -1, 0
        | Right -> 1, 0

    let private dirOfIndex (i: int) : Direction =
        match i &&& 3 with
        | 0 -> Down
        | 1 -> Up
        | 2 -> Left
        | _ -> Right

    /// Whether stepping to `(tx, ty)` would reach (or exceed) the object's wander
    /// limit — a faithful read of GSC `HasObjectReachedMovementLimit`: a 0 radius on
    /// an axis means "no limit there", otherwise the candidate cell may not land on
    /// the exact `home ± radius` boundary.
    let private reachedLimit (hx: int) (hy: int) (rx: int) (ry: int) (tx: int) (ty: int) : bool =
        if rx = 0 && ry = 0 then
            false
        else
            let xHit = rx <> 0 && (tx = hx - rx || tx = hx + rx)
            let yHit = ry <> 0 && (ty = hy - ry || ty = hy + ry)
            xHit || yHit

    /// The random sleep duration after a wander step or a blocked attempt — slow
    /// movers draw 0..127 frames, fast spinners 0..31 (GSC `RandomStepDuration_*`).
    let private sleepMask (kind: MovementKind) : int =
        match kind with
        | FastSpin -> 0x1F
        | _ -> 0x7F

    /// Pick the next wander direction index for a walk behaviour, matching the bit
    /// masks the GSC movement functions apply to a random byte.
    let private walkDir (kind: MovementKind) (r: int) : int =
        match kind with
        | RandomWalkX -> (r &&& 1) ||| 2
        | RandomWalkY -> r &&& 1
        | _ -> r &&& 3 // RandomWalkXY (and any other walk)

    /// Advance one object by one frame. `walkable cx cy` reports whether a cell can
    /// be stepped on (map + connection collision) and `occupied cx cy` whether
    /// another object already holds it.
    let step (walkable: int -> int -> bool) (occupied: int -> int -> bool) (n: NpcObject) : NpcObject =
        match n.Motion with
        | NpcWalking ->
            let progress = n.Progress + 1

            if progress >= NpcObject.StepFrames then
                // Tile reached: stand on it and sleep before the next decision.
                let r, seed = draw n.Seed

                { n with
                    Motion = NpcStanding
                    Progress = 0
                    AnimFrame = n.AnimFrame + 1
                    SrcX = n.CellX
                    SrcY = n.CellY
                    Sleep = r &&& sleepMask n.Kind
                    Seed = seed }
            else
                { n with
                    Progress = progress
                    AnimFrame = n.AnimFrame + 1 }

        | NpcStanding ->
            match n.Kind with
            | StandStill -> n
            | _ when n.Sleep > 0 -> { n with Sleep = n.Sleep - 1 }
            | RandomWalkXY
            | RandomWalkX
            | RandomWalkY ->
                // Decision frame: pick a direction and try to step.
                let r, seed = draw n.Seed
                let dir = dirOfIndex (walkDir n.Kind r)
                let dx, dy = delta dir
                let tx, ty = n.CellX + dx, n.CellY + dy

                let canMove =
                    walkable tx ty
                    && not (occupied tx ty)
                    && not (reachedLimit n.HomeX n.HomeY n.RadiusX n.RadiusY tx ty)

                if canMove then
                    { n with
                        Facing = dir
                        Motion = NpcWalking
                        SrcX = n.CellX
                        SrcY = n.CellY
                        CellX = tx
                        CellY = ty
                        Progress = 0
                        AnimFrame = n.AnimFrame + 1
                        Seed = seed }
                else
                    // Blocked: face the tried direction and sleep again.
                    let r2, seed2 = draw seed

                    { n with
                        Facing = dir
                        Sleep = r2 &&& sleepMask n.Kind
                        Seed = seed2 }
            | SlowSpin ->
                let r, seed = draw n.Seed
                let dir = dirOfIndex ((r &&& 0x0C) >>> 2)
                let r2, seed2 = draw seed

                { n with
                    Facing = dir
                    Sleep = r2 &&& sleepMask n.Kind
                    Seed = seed2 }
            | FastSpin ->
                // Pick a new facing, avoiding a repeat of the current one.
                let cur =
                    match n.Facing with
                    | Down -> 0
                    | Up -> 1
                    | Left -> 2
                    | Right -> 3

                let r, seed = draw n.Seed
                let pick = (r &&& 0x0C) >>> 2
                let pick = if pick = cur then pick ^^^ 0x03 else pick
                let r2, seed2 = draw seed

                { n with
                    Facing = dirOfIndex pick
                    Sleep = r2 &&& sleepMask n.Kind
                    Seed = seed2 }

    /// Advance every object one frame. Occupancy is threaded sequentially: each
    /// object sees the cells already committed by earlier objects this frame plus
    /// the not-yet-moved cells of later objects, so two NPCs never end on the same
    /// tile and none blocks itself. `walkable` already folds in map + connection
    /// collision.
    let stepAll (walkable: int -> int -> bool) (npcs: NpcObject[]) : NpcObject[] =
        let occ = System.Collections.Generic.HashSet<struct (int * int)>()

        for m in npcs do
            occ.Add(struct (m.CellX, m.CellY)) |> ignore

            if m.Moving then
                occ.Add(struct (m.SrcX, m.SrcY)) |> ignore

        npcs
        |> Array.map (fun n ->
            occ.Remove(struct (n.CellX, n.CellY)) |> ignore
            occ.Remove(struct (n.SrcX, n.SrcY)) |> ignore

            let occupied cx cy = occ.Contains(struct (cx, cy))
            let stepped = step walkable occupied n

            occ.Add(struct (stepped.CellX, stepped.CellY)) |> ignore

            if stepped.Moving then
                occ.Add(struct (stepped.SrcX, stepped.SrcY)) |> ignore

            stepped)
