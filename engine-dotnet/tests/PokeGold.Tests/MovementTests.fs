module PokeGold.Tests.MovementTests

open Xunit
open PokeGold.Game

// One full grid step takes this many ticks (mirrors GameCore's stepFrames).
let private StepFrames = 16

let private press dir =
    match dir with
    | Down -> { Buttons.none with Down = true }
    | Up -> { Buttons.none with Up = true }
    | Left -> { Buttons.none with Left = true }
    | Right -> { Buttons.none with Right = true }

[<Fact>]
let ``player starts on the map facing down`` () =
    let g = GameCore()
    Assert.Equal(Down, g.Facing)
    Assert.InRange(g.PlayerCellX, 0, 39) // 20 blocks × 2 cells
    Assert.InRange(g.PlayerCellY, 0, 17) // 9 blocks × 2 cells

[<Theory>]
[<InlineData(0)>] // Down
[<InlineData(1)>] // Up
[<InlineData(2)>] // Left
[<InlineData(3)>] // Right
let ``facing follows input immediately on the first tick`` (d: int) =
    let dir = [| Down; Up; Left; Right |].[d]
    let g = GameCore()
    g.Tick(press dir)
    Assert.Equal(dir, g.Facing)

[<Fact>]
let ``a grid step moves exactly one cell in the faced direction`` () =
    // From the start cell, every direction either moves exactly one cell (if the
    // neighbor is walkable) or none (if it's blocked) — never more than one.
    let dirs = [ Down, (0, 1); Up, (0, -1); Left, (-1, 0); Right, (1, 0) ]
    let mutable anyMoved = false

    for dir, (dx, dy) in dirs do
        let g = GameCore()
        let sx, sy = g.PlayerCellX, g.PlayerCellY
        for _ in 1..StepFrames do g.Tick(press dir)
        let mx, my = g.PlayerCellX - sx, g.PlayerCellY - sy
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
            let g = GameCore()
            let sx, sy = g.PlayerCellX, g.PlayerCellY
            for _ in 1..StepFrames do g.Tick(press dir)
            (g.PlayerCellX - sx, g.PlayerCellY - sy) = (dx, dy))

    match walkable with
    | None -> failwith "no walkable direction from start"
    | Some(dir, (dx, dy)) ->
        let g = GameCore()
        let sx, sy = g.PlayerCellX, g.PlayerCellY
        // Hold for two step-periods: should advance exactly two cells, not more.
        for _ in 1 .. (StepFrames * 2 + 2) do g.Tick(press dir)
        Assert.Equal(sx + dx * 2, g.PlayerCellX)
        Assert.Equal(sy + dy * 2, g.PlayerCellY)
