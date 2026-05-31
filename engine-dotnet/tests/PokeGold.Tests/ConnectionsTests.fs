module PokeGold.Tests.ConnectionsTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Audio
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Scenes

// M10.3 — map connections. The `connection` macro geometry (attributes.asm:32-86)
// is ported into MapConnections as pure cell-frame placement; the overworld then
// reads neighbour blocks for collision and crosses into the neighbour map when the
// player walks off the edge. Azalea Town has two real, loadable neighbours:
// WEST → Route34 (offset -18), EAST → Route33 (offset 0).

let private press (dir: Direction) : Buttons =
    match dir with
    | Down -> { Buttons.none with Down = true }
    | Up -> { Buttons.none with Up = true }
    | Left -> { Buttons.none with Left = true }
    | Right -> { Buttons.none with Right = true }

type private SilentSound() =
    interface ISoundBoard with
        member _.PlayMusic _ = ()
        member _.PlaySfx _ = ()
        member _.StopMusic() = ()

// Azalea is 20x9 blocks → 40x18 cells. Route33 is 10x9 (20x18 cells); Route34 is
// 10x27 (20x54 cells).

[<Fact>]
let ``an east connection places the neighbour just past the right edge`` () =
    // EAST offset is a Y alignment of 0: Route33's top aligns with Azalea's top,
    // its left edge (local x 0) abuts Azalea's right edge (cell x 40).
    let c = { Direction = "east"; Map = "Route33"; MapConst = "ROUTE_33"; Offset = 0 }
    let p = MapConnections.placement 40 18 10 9 c

    Assert.Equal(40, p.BaseCx)
    Assert.Equal(0, p.BaseCy)
    Assert.Equal(20, p.CellW)
    Assert.Equal(18, p.CellH)
    Assert.Equal<(int * int) option>(Some(0, 5), MapConnections.localCell p 40 5)
    Assert.Equal<(int * int) option>(Some(19, 17), MapConnections.localCell p 59 17)
    Assert.Equal<(int * int) option>(None, MapConnections.localCell p 60 5) // past the neighbour
    Assert.Equal<(int * int) option>(None, MapConnections.localCell p 39 5) // still inside Azalea

[<Fact>]
let ``a west connection with a negative offset shifts and bottom-aligns the neighbour`` () =
    // WEST offset is a Y alignment of -18 blocks: Route34 (54 cells tall) extends 36
    // cells above Azalea and bottom-aligns with it; its right edge abuts cell x -1.
    let c = { Direction = "west"; Map = "Route34"; MapConst = "ROUTE_34"; Offset = -18 }
    let p = MapConnections.placement 40 18 10 27 c

    Assert.Equal(-20, p.BaseCx)
    Assert.Equal(-36, p.BaseCy)
    Assert.Equal(20, p.CellW)
    Assert.Equal(54, p.CellH)
    // The cell immediately west of Azalea's bottom-left maps into Route34's right edge.
    Assert.Equal<(int * int) option>(Some(19, 53), MapConnections.localCell p -1 17)
    Assert.Equal<(int * int) option>(Some(19, 0), MapConnections.localCell p -1 -36)

[<Fact>]
let ``Azalea Town loads its two connected neighbours`` () =
    let content = Content()
    let state = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down
    let maps = state.Neighbors |> List.map (fun n -> n.Placement.Conn.Map) |> List.sort
    Assert.Equal<string list>([ "Route33"; "Route34" ], maps)

[<Fact>]
let ``walkability is read from the neighbour map past the edge`` () =
    let content = Content()
    let state = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down

    let route33 =
        state.Neighbors |> List.find (fun n -> n.Placement.Conn.Map = "Route33")

    // For every row, a cell one past Azalea's east edge must agree with the matching
    // cell on Route33's west edge — the extended lookup just forwards to the neighbour.
    for ly in 0 .. route33.Placement.CellH - 1 do
        let viaNeighbour = Movement.cellWalkable route33.Map route33.Collision 0 ly
        let extended = MapConnections.cellWalkable state.Map state.Collision state.Neighbors 40 ly
        Assert.Equal(viaNeighbour, extended)

[<Fact>]
let ``crossConnection rebases the player onto the neighbour map`` () =
    let content = Content()
    // Stand the player one cell past Azalea's east edge (covered by Route33 local 0,9).
    let state = OverworldState.loadByIdAt content "AzaleaTown" 40 9 Right

    match OverworldState.crossConnection content state with
    | Some ns ->
        Assert.Equal("Route33", ns.MapId)
        Assert.Equal((0, 9), (ns.Player.CellX, ns.Player.CellY))
        Assert.Equal(Right, ns.Player.Facing)
    | None -> Assert.Fail("expected to cross east into Route33")

[<Fact>]
let ``crossConnection does nothing while inside the map`` () =
    let content = Content()
    let state = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down
    Assert.Equal<OverworldState option>(None, OverworldState.crossConnection content state)

[<Fact>]
let ``walking off Azalea's east edge enters Route33`` () =
    let content = Content()
    let probe = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down

    let route33 =
        probe.Neighbors |> List.find (fun n -> n.Placement.Conn.Map = "Route33")

    // Find a row where the player can actually walk across the join: both of Azalea's
    // last two east columns and Route33's first two west columns must be walkable.
    let row =
        [ 0 .. 17 ]
        |> List.tryFind (fun ly ->
            Movement.cellWalkable probe.Map probe.Collision 38 ly
            && Movement.cellWalkable probe.Map probe.Collision 39 ly
            && Movement.cellWalkable route33.Map route33.Collision 0 ly
            && Movement.cellWalkable route33.Map route33.Collision 1 ly)

    match row with
    | None -> failwith "no walkable east-edge crossing row on Azalea Town"
    | Some ly ->
        let sound = SilentSound()
        let start = OverworldState.loadByIdAt content "AzaleaTown" 38 ly Right
        let scene = OverworldScene(content, sound, start) :> Scene

        // Walk east for a few tiles — enough to step (38)->(39)->(40, off-map) and settle.
        for _ in 0 .. (Player.StepFrames * 4) do
            scene.Update(press Right) |> ignore

        Assert.Equal("Route33", (scene :?> OverworldScene).Capture().Overworld.MapId)
