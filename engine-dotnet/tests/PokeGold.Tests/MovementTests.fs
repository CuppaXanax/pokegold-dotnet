module PokeGold.Tests.MovementTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld

// Movement is now a pure system: Movement.step advances an immutable PlayerState
// given the map, collision, and this frame's input. These tests drive it directly
// against the real Azalea Town map/collision — no framebuffer needed.

let private StepFrames = Player.StepFrames

let private press dir =
    match dir with
    | Down -> { Buttons.none with Down = true }
    | Up -> { Buttons.none with Up = true }
    | Left -> { Buttons.none with Left = true }
    | Right -> { Buttons.none with Right = true }

/// Load the world and place the player on the default start cell.
let private start () =
    let map = Map.load 20 9 "maps/AzaleaTown.blk"
    let coll = Collision.loadNamed "johto_modern"
    let sx, sy = Movement.findStartCell map coll
    map, coll, Player.create sx sy

/// Run `n` ticks of the given input from a starting player state.
let private run map coll dir n (p0: PlayerState) =
    let mutable p = p0
    for _ in 1..n do
        p <- Movement.step map coll (press dir) p
    p

[<Fact>]
let ``player starts on the map facing down`` () =
    let _, _, p = start ()
    Assert.Equal(Down, p.Facing)
    Assert.InRange(p.CellX, 0, 39) // 20 blocks × 2 cells
    Assert.InRange(p.CellY, 0, 17) // 9 blocks × 2 cells

[<Theory>]
[<InlineData(0)>] // Down
[<InlineData(1)>] // Up
[<InlineData(2)>] // Left
[<InlineData(3)>] // Right
let ``facing follows input immediately on the first tick`` (d: int) =
    let dir = [| Down; Up; Left; Right |].[d]
    let map, coll, p0 = start ()
    let p = Movement.step map coll (press dir) p0
    Assert.Equal(dir, p.Facing)

[<Fact>]
let ``a grid step moves exactly one cell in the faced direction`` () =
    // From the start cell, every direction either moves exactly one cell (if the
    // neighbor is walkable) or none (if it's blocked) — never more than one.
    let dirs = [ Down, (0, 1); Up, (0, -1); Left, (-1, 0); Right, (1, 0) ]
    let mutable anyMoved = false

    for dir, (dx, dy) in dirs do
        let map, coll, p0 = start ()
        let p = run map coll dir StepFrames p0
        let mx, my = p.CellX - p0.CellX, p.CellY - p0.CellY
        // Either it didn't move, or it moved exactly one cell the faced way.
        Assert.True((mx, my) = (0, 0) || (mx, my) = (dx, dy))
        if (mx, my) = (dx, dy) then anyMoved <- true

    // A real town center can't be fully walled in.
    Assert.True(anyMoved)

[<Fact>]
let ``input is locked mid-step (one step per StepFrames held)`` () =
    // Find a direction the player can actually walk.
    let dirs = [ Down, (0, 1); Up, (0, -1); Left, (-1, 0); Right, (1, 0) ]

    let walkable =
        dirs
        |> List.tryFind (fun (dir, (dx, dy)) ->
            let map, coll, p0 = start ()
            let p = run map coll dir StepFrames p0
            (p.CellX - p0.CellX, p.CellY - p0.CellY) = (dx, dy))

    match walkable with
    | None -> failwith "no walkable direction from start"
    | Some(dir, (dx, dy)) ->
        let map, coll, p0 = start ()
        // Face the walk direction first so this exercises step-locking, not the
        // turn-in-place that a fresh direction would trigger.
        let p0 = { p0 with Facing = dir }
        // Hold for two step-periods: should advance exactly two cells, not more.
        let p = run map coll dir (StepFrames * 2 + 2) p0
        Assert.Equal(p0.CellX + dx * 2, p.CellX)
        Assert.Equal(p0.CellY + dy * 2, p.CellY)

// --- Ledge hops (M4 addendum) -------------------------------------------------

let private HopFrames = Player.HopFrames

let private delta dir =
    match dir with
    | Down -> 0, 1
    | Up -> 0, -1
    | Left -> -1, 0
    | Right -> 1, 0

[<Fact>]
let ``tryLedge decodes the ledge mask table`` () =
    // Low nybble of HI_NYBBLE_LEDGES ($a0) selects the allowed approach facings.
    Assert.Equal<Direction list option>(Some [ Right ], Collision.tryLedge 0xa0uy)
    Assert.Equal<Direction list option>(Some [ Left ], Collision.tryLedge 0xa1uy)
    Assert.Equal<Direction list option>(Some [ Up ], Collision.tryLedge 0xa2uy)
    Assert.Equal<Direction list option>(Some [ Down ], Collision.tryLedge 0xa3uy)
    Assert.Equal<Direction list option>(Some [ Right; Down ], Collision.tryLedge 0xa4uy)
    Assert.Equal<Direction list option>(Some [ Down; Left ], Collision.tryLedge 0xa5uy)
    Assert.Equal<Direction list option>(Some [ Up; Right ], Collision.tryLedge 0xa6uy)
    Assert.Equal<Direction list option>(Some [ Up; Left ], Collision.tryLedge 0xa7uy)

[<Fact>]
let ``tryLedge ignores non-ledge tiles`` () =
    Assert.Equal<Direction list option>(None, Collision.tryLedge 0x00uy) // floor
    Assert.Equal<Direction list option>(None, Collision.tryLedge 0x07uy) // wall
    Assert.Equal<Direction list option>(None, Collision.tryLedge 0xb0uy) // side wall

/// Find a real ledge on Azalea Town whose forward cell is blocked and whose
/// two-cell landing is in-bounds. Returns (cell, hop direction).
let private findLedge map coll =
    let cellsW = map.Width * 2
    let cellsH = map.Height * 2

    seq {
        for cy in 0 .. cellsH - 1 do
            for cx in 0 .. cellsW - 1 do
                match Collision.tryLedge (Movement.collisionIdAtCell map coll cx cy) with
                | Some dirs ->
                    for d in dirs do
                        let dx, dy = delta d
                        let lx, ly = cx + 2 * dx, cy + 2 * dy
                        // Forward blocked (so .TryStep fails) and landing in-bounds.
                        if
                            not (Movement.cellWalkable map coll (cx + dx) (cy + dy))
                            && lx >= 0
                            && ly >= 0
                            && lx < cellsW
                            && ly < cellsH
                        then
                            yield (cx, cy), d
                | None -> ()
    }
    |> Seq.tryHead

[<Fact>]
let ``standing on a ledge, pressing its direction hops two cells`` () =
    let map, coll, _ = start ()

    match findLedge map coll with
    | None -> failwith "no usable ledge found on Azalea Town"
    | Some((cx, cy), d) ->
        let dx, dy = delta d
        let p0 = { Player.create cx cy with Facing = d }

        // Mid-hop the player is marked Hopping and lifted by the arc.
        let mid = run map coll d (HopFrames / 2) p0
        Assert.True(mid.Hopping)
        Assert.True(mid.Moving)

        let p = run map coll d (HopFrames + 1) p0
        Assert.Equal(cx + 2 * dx, p.CellX)
        Assert.Equal(cy + 2 * dy, p.CellY)
        Assert.False(p.Hopping)
        Assert.False(p.Moving)

[<Fact>]
let ``a hop takes longer than a normal step`` () =
    Assert.True(Player.HopFrames > Player.StepFrames)

[<Fact>]
let ``a blocked non-ledge direction does not hop`` () =
    let map, coll, _ = start ()

    match findLedge map coll with
    | None -> failwith "no usable ledge found on Azalea Town"
    | Some((cx, cy), d) ->
        // Pick a direction the ledge does NOT permit and whose forward is blocked.
        let allowed = Collision.tryLedge (Movement.collisionIdAtCell map coll cx cy) |> Option.get

        let blockedNonLedge =
            [ Down; Up; Left; Right ]
            |> List.tryFind (fun d2 ->
                let dx, dy = delta d2
                not (List.contains d2 allowed)
                && not (Movement.cellWalkable map coll (cx + dx) (cy + dy)))

        match blockedNonLedge with
        | None -> () // nothing to assert for this particular ledge
        | Some d2 ->
            let p0 = { Player.create cx cy with Facing = d2 }
            let p = run map coll d2 HopFrames p0
            Assert.Equal(cx, p.CellX)
            Assert.Equal(cy, p.CellY)
            Assert.False(p.Hopping)

// --- Turn-in-place & wall-bump (Q1) ------------------------------------------

[<Fact>]
let ``tapping a direction you don't face turns in place without moving`` () =
    // Player starts facing Down; pressing Up should pivot to face Up but stay put.
    let map, coll, p0 = start ()
    let p = run map coll Up (Player.TurnFrames - 1) p0
    Assert.Equal(Up, p.Facing) // facing follows input immediately
    Assert.Equal(p0.CellX, p.CellX) // but the cell never changes during a turn
    Assert.Equal(p0.CellY, p.CellY)
    Assert.False(p.Moving)
    Assert.Equal<Motion>(Turning, p.Motion)

[<Fact>]
let ``a turn costs extra frames before the first step`` () =
    // Find a direction that is walkable from the start cell but isn't the initial
    // facing, so the held press must turn before it can step.
    let map, coll, _ = start ()

    let walkableNonFacing =
        [ Up, (0, -1); Left, (-1, 0); Right, (1, 0) ]
        |> List.tryFind (fun (d, (dx, dy)) ->
            let _, _, p0 = start ()
            let faced = { p0 with Facing = d }
            let p = run map coll d StepFrames faced
            (p.CellX - p0.CellX, p.CellY - p0.CellY) = (dx, dy))

    match walkableNonFacing with
    | None -> () // start cell only opens downward; nothing to assert here
    | Some(d, _) ->
        // Frames until the cell first changes (a step begins) when holding d.
        let framesUntilMove faced =
            let _, _, p0 = start ()
            let p0 = if faced then { p0 with Facing = d } else p0
            let mutable p = p0
            let mutable n = 0

            while (p.CellX, p.CellY) = (p0.CellX, p0.CellY) && n < 1000 do
                p <- Movement.step map coll (press d) p
                n <- n + 1

            n

        // A pre-faced player steps on frame one; a fresh-facing player must first
        // spend the turn — exactly TurnFrames + 1 extra frames (the +1 is the turn
        // init frame) before the same step begins.
        Assert.Equal(1, framesUntilMove true)
        Assert.Equal(1 + Player.TurnFrames + 1, framesUntilMove false)

[<Fact>]
let ``walking into a wall bumps in place and pulses the SFX hook once`` () =
    let map, coll, _ = start ()

    match findLedge map coll with
    | None -> failwith "no usable ledge found on Azalea Town"
    | Some((cx, cy), _) ->
        let allowed = Collision.tryLedge (Movement.collisionIdAtCell map coll cx cy) |> Option.get

        let blockedNonLedge =
            [ Down; Up; Left; Right ]
            |> List.tryFind (fun d2 ->
                let dx, dy = delta d2
                not (List.contains d2 allowed)
                && not (Movement.cellWalkable map coll (cx + dx) (cy + dy)))

        match blockedNonLedge with
        | None -> () // this ledge has no plain blocked neighbour to bump
        | Some d2 ->
            let p0 = { Player.create cx cy with Facing = d2 }

            // First frame of the bump: no movement, but the SFX hook fires.
            let p1 = Movement.step map coll (press d2) p0
            Assert.Equal(cx, p1.CellX)
            Assert.Equal(cy, p1.CellY)
            Assert.False(p1.Moving)
            Assert.False(p1.Hopping)
            Assert.True(p1.Bumped)
            Assert.Equal<Motion>(Bumping, p1.Motion)

            // The hook pulses once per cycle, not every frame it's held.
            let p2 = Movement.step map coll (press d2) p1
            Assert.False(p2.Bumped)

[<Fact>]
let ``a held bump animates a half-speed stand-step-stand-step leg cycle`` () =
    // GSC's wall-bump runs the same 4-phase leg animation as a walk — stand, step,
    // stand, step — but at half speed (a pose every 8 frames, vs 4 for a walk; see
    // SetFacingBumpAction's `OBJECT_STEP_FRAME >> 3`). So across one BumpFrames
    // cycle the drawn pose must hold for 8-frame blocks and return to the neutral
    // standing pose between the two strides.
    let map, coll, _ = start ()

    match findLedge map coll with
    | None -> failwith "no usable ledge found on Azalea Town"
    | Some((cx, cy), _) ->
        let allowed = Collision.tryLedge (Movement.collisionIdAtCell map coll cx cy) |> Option.get

        let blockedNonLedge =
            [ Down; Up; Left; Right ]
            |> List.tryFind (fun d2 ->
                let dx, dy = delta d2
                not (List.contains d2 allowed)
                && not (Movement.cellWalkable map coll (cx + dx) (cy + dy)))

        match blockedNonLedge with
        | None -> () // this ledge has no plain blocked neighbour to bump
        | Some d2 ->
            let p0 = { Player.create cx cy with Facing = d2 }

            // Sample the drawn pose every frame across one full bump cycle
            // (Progress 0..BumpFrames-1, all held in the Bumping motion).
            let mutable p = p0

            let poses =
                [ for _ in 1 .. Player.BumpFrames do
                      p <- Movement.step map coll (press d2) p
                      yield Animation.frameAndFlip p ]

            // Four 8-frame phases: each holds a single pose (half walk speed).
            let blocks = poses |> List.chunkBySize 8
            Assert.Equal(4, List.length blocks)

            for b in blocks do
                Assert.Equal(1, b |> List.distinct |> List.length)

            let reps = blocks |> List.map List.head

            match reps with
            | [ stand0; step1; stand2; step3 ] ->
                // The legs return to the same neutral pose between strides...
                Assert.Equal<int * bool>(stand0, stand2)
                // ...and each stride is a distinct, stepping pose.
                Assert.NotEqual<int * bool>(stand0, step1)
                Assert.NotEqual<int * bool>(stand2, step3)
            | _ -> failwith "expected exactly four animation phases"

[<Fact>]
let ``the ledge-hop arc rises to a 12px apex and lands flat (GSC table)`` () =
    // A horizontal hop keeps the row fixed, so worldPixel's vertical offset is the
    // GSC arc table exactly. Drive a synthetic hop and sample every frame.
    let hopAt progress =
        { Player.create 5 5 with
            Facing = Right
            Motion = Hopping
            SrcX = 5
            SrcY = 5
            CellX = 7
            CellY = 5
            Progress = progress }

    let baseline = 5 * Player.CellPixels

    let lifts =
        [ for f in 0 .. Player.HopFrames - 1 -> baseline - snd (Player.worldPixel (hopAt f)) ]

    Assert.Equal(12, List.max lifts) // −12 px apex
    Assert.Equal(4, List.head lifts) // already lifted 4 px on the first frame
    Assert.Equal(0, List.last lifts) // lands flat on the baseline
