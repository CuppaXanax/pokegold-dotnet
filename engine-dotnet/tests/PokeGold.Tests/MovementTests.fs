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
