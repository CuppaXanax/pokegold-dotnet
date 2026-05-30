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
