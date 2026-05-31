module PokeGold.Tests.ObjectTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script

// A minimal object event for building a live NPC under test.
let private mkEvent (x: int) (y: int) (movement: string) (rx: int) (ry: int) : ObjectEvent =
    { X = x
      Y = y
      Sprite = "SPRITE_LASS"
      Movement = movement
      RadiusX = rx
      RadiusY = ry
      Hour1 = 0
      Hour2 = 0
      Palette = "PAL_OW_BLUE"
      Type = "OBJECTTYPE_SCRIPT"
      Sight = 0
      Script = ""
      EventFlag = None }

// Always-open world, never-occupied: isolate the radius/axis logic from terrain.
let private open' = fun (_: int) (_: int) -> true
let private clear = fun (_: int) (_: int) -> false

/// Step a single NPC n frames against the given queries.
let private run (frames: int) (walkable: int -> int -> bool) (occupied: int -> int -> bool) (n0: NpcObject) : NpcObject =
    let mutable n = n0

    for _ in 1..frames do
        n <- ObjectStep.step walkable occupied n

    n

/// Collect the cells an NPC visits over n frames.
let private trail (frames: int) (walkable: int -> int -> bool) (n0: NpcObject) : (int * int) list =
    let mutable n = n0
    [ for _ in 1..frames do
          n <- ObjectStep.step walkable clear n
          yield n.CellX, n.CellY ]

// ---- generated data -------------------------------------------------------

[<Fact>]
let ``SpriteMovementData bakes every row with its function and facing`` () =
    Assert.Equal(37, Map.count SpriteMovementData.all)
    Assert.Equal(("SPRITEMOVEFN_RANDOM_WALK_XY", "DOWN"), SpriteMovementData.all.["SPRITEMOVEDATA_WANDER"])
    Assert.Equal(("SPRITEMOVEFN_STANDING", "UP"), SpriteMovementData.all.["SPRITEMOVEDATA_STANDING_UP"])
    Assert.Equal(("SPRITEMOVEFN_FAST_RANDOM_SPIN", "DOWN"), SpriteMovementData.all.["SPRITEMOVEDATA_SPINRANDOM_FAST"])

[<Fact>]
let ``fromEvent derives behaviour and facing from the movement constant`` () =
    let wander = NpcObject.fromEvent 0 (mkEvent 5 5 "SPRITEMOVEDATA_WANDER" 2 2)
    Assert.Equal(RandomWalkXY, wander.Kind)

    let still = NpcObject.fromEvent 0 (mkEvent 5 5 "SPRITEMOVEDATA_STILL" 0 0)
    Assert.Equal(StandStill, still.Kind)

    let up = NpcObject.fromEvent 0 (mkEvent 5 5 "SPRITEMOVEDATA_STANDING_UP" 0 0)
    Assert.Equal(StandStill, up.Kind)
    Assert.Equal(Up, up.Facing)

// ---- behaviour ------------------------------------------------------------

[<Fact>]
let ``a standing object never moves`` () =
    let n = NpcObject.fromEvent 0 (mkEvent 8 8 "SPRITEMOVEDATA_STANDING_DOWN" 0 0)
    let after = run 500 open' clear n
    Assert.Equal((8, 8), (after.CellX, after.CellY))

[<Fact>]
let ``a walk-left-right wanderer only ever changes its X`` () =
    let n = NpcObject.fromEvent 3 (mkEvent 10 10 "SPRITEMOVEDATA_WALK_LEFT_RIGHT" 3 0) // unlimited X
    let cells = trail 4000 open' n
    Assert.All(cells, fun (_, y) -> Assert.Equal(10, y))
    Assert.Contains(cells, fun (x, _) -> x <> 10) // it actually moved on X

[<Fact>]
let ``a walk-up-down wanderer only ever changes its Y`` () =
    let n = NpcObject.fromEvent 1 (mkEvent 10 10 "SPRITEMOVEDATA_WALK_UP_DOWN" 0 3)
    let cells = trail 4000 open' n
    Assert.All(cells, fun (x, _) -> Assert.Equal(10, x))
    Assert.Contains(cells, fun (_, y) -> y <> 10)

[<Fact>]
let ``a free wanderer stays strictly inside its movement radius`` () =
    let rx, ry = 2, 2
    let n = NpcObject.fromEvent 7 (mkEvent 20 15 "SPRITEMOVEDATA_WANDER" rx ry)
    let cells = trail 6000 open' n

    Assert.All(
        cells,
        fun (x, y) ->
            Assert.True(abs (x - 20) < rx, sprintf "x=%d out of radius" x)
            Assert.True(abs (y - 15) < ry, sprintf "y=%d out of radius" y)
    )

    // And it really roams (doesn't just sit on its home cell forever).
    Assert.Contains(cells, fun c -> c <> (20, 15))

[<Fact>]
let ``a wanderer boxed in by walls never leaves its home cell`` () =
    let n = NpcObject.fromEvent 2 (mkEvent 12 9 "SPRITEMOVEDATA_WANDER" 4 4)
    // Nothing is walkable: every attempted step is blocked.
    let blocked = fun (_: int) (_: int) -> false
    let cells = trail 3000 blocked n
    Assert.All(cells, fun c -> Assert.Equal((12, 9), c))

[<Fact>]
let ``a spinning object turns over time but never translates`` () =
    let n = NpcObject.fromEvent 0 (mkEvent 6 6 "SPRITEMOVEDATA_SPINRANDOM_FAST" 0 0)
    let mutable cur = n
    let facings = System.Collections.Generic.HashSet<Direction>()

    for _ in 1..2000 do
        cur <- ObjectStep.step open' clear cur
        facings.Add cur.Facing |> ignore
        Assert.Equal((6, 6), (cur.CellX, cur.CellY))

    Assert.True(facings.Count > 1, "a fast spinner should face more than one direction over time")

[<Fact>]
let ``stepping is deterministic for a given seed`` () =
    let a = NpcObject.fromEvent 4 (mkEvent 14 14 "SPRITEMOVEDATA_WANDER" 3 3)
    let b = NpcObject.fromEvent 4 (mkEvent 14 14 "SPRITEMOVEDATA_WANDER" 3 3)
    Assert.Equal<(int * int) list>(trail 1500 open' a, trail 1500 open' b)

[<Fact>]
let ``stepAll keeps two wanderers off the same cell`` () =
    // Two wanderers whose ranges overlap heavily on a fully-open field.
    let a = NpcObject.fromEvent 0 (mkEvent 5 5 "SPRITEMOVEDATA_WANDER" 3 3)
    let b = NpcObject.fromEvent 1 (mkEvent 6 5 "SPRITEMOVEDATA_WANDER" 3 3)
    let mutable npcs = [| a; b |]

    for _ in 1..3000 do
        npcs <- ObjectStep.stepAll open' npcs
        let p0 = npcs.[0].CellX, npcs.[0].CellY
        let p1 = npcs.[1].CellX, npcs.[1].CellY
        Assert.NotEqual<int * int>(p0, p1)

[<Fact>]
let ``an object never steps onto a pinned player cell`` () =
    // A wanderer on a fully-open field whose radius covers the pinned cell: it must
    // never occupy it, because the player is solid.
    let n = NpcObject.fromEvent 0 (mkEvent 5 5 "SPRITEMOVEDATA_WANDER" 3 3)
    let pin = struct (6, 5)
    let mutable npcs = [| n |]

    for _ in 1..4000 do
        npcs <- ObjectStep.stepAllBlocked open' (Seq.singleton pin) npcs
        Assert.NotEqual<struct (int * int)>(pin, struct (npcs.[0].CellX, npcs.[0].CellY))

[<Fact>]
let ``occupiedCells reports a standing object's single cell`` () =
    let n = NpcObject.fromEvent 0 (mkEvent 7 4 "SPRITEMOVEDATA_STILL" 0 0)
    let cells = ObjectStep.occupiedCells [| n |]
    Assert.True(Set.contains (struct (7, 4)) cells)
    Assert.Equal(1, Set.count cells)

// ---- M10 follow-ups: live interaction + visibility-gated solidity -----------

[<Fact>]
let ``actionScript talks to a wandering NPC at its current cell, not its spawn`` () =
    // An NPC that has wandered two tiles east of where it spawned.
    let ev = { mkEvent 5 5 "SPRITEMOVEDATA_WANDER" 2 2 with Script = "WandererScript" }
    let npc = { NpcObject.fromEvent 0 ev with CellX = 7; CellY = 5 }

    // The scene resolves the faced object over the *live* NPC set.
    let objectScriptAt fx fy =
        [| npc |]
        |> Array.tryFind (fun n -> n.CellX = fx && n.CellY = fy)
        |> Option.map (fun n -> n.Event.Script)

    // Facing the NPC's current cell (7,5) talks to it...
    Assert.Equal(Some "WandererScript", Triggers.actionScript objectScriptAt MapEvents.empty 8 5 Left)
    // ...while facing the now-empty spawn tile (5,5) triggers nothing (no ghost).
    Assert.Equal(None, Triggers.actionScript objectScriptAt MapEvents.empty 6 5 Left)

[<Fact>]
let ``hidden event-gated objects neither block the player nor wander`` () =
    // Azalea Town's Slowpokes/Rockets/Rival are gated behind event flags that are
    // unset in a fresh world, so they are absent — they must not render (scene-side),
    // not occupy a tile (no invisible walls), and not wander.
    let content = PokeGold.Game.Data.Content()
    let st0 = OverworldState.loadById content "AzaleaTown"
    let vis (n: NpcObject) = MapEvents.objectVisible World.empty n.Event

    Assert.True(st0.Npcs |> Array.exists (vis >> not), "expected event-gated objects in Azalea")

    // Hidden objects' tiles are not in the occupancy set the player is blocked by.
    let visibleCells = ObjectStep.occupiedCells (st0.Npcs |> Array.filter vis)

    for n in st0.Npcs do
        if not (vis n) then
            Assert.False(Set.contains (struct (n.CellX, n.CellY)) visibleCells)

    // Stepping the world leaves every hidden object exactly where it spawned.
    let mutable st = st0

    for _ in 1..300 do
        st <- OverworldState.tick vis Buttons.none st

    for i in 0 .. st.Npcs.Length - 1 do
        let a = st0.Npcs.[i]
        let b = st.Npcs.[i]

        if not (vis a) then
            Assert.Equal<int * int>((a.CellX, a.CellY), (b.CellX, b.CellY))

[<Fact>]
let ``visible NPCs still wander when the world is stepped`` () =
    // The flip side: with everything visible, an unobstructed wanderer does move,
    // proving the visibility filter doesn't freeze present objects.
    let content = PokeGold.Game.Data.Content()
    let st0 = OverworldState.loadById content "AzaleaTown"
    let always = fun (_: NpcObject) -> true

    let mutable st = st0

    for _ in 1..600 do
        st <- OverworldState.tick always Buttons.none st

    let moved =
        Array.exists2 (fun (a: NpcObject) (b: NpcObject) -> (a.CellX, a.CellY) <> (b.CellX, b.CellY)) st0.Npcs st.Npcs

    Assert.True(moved, "expected at least one wandering NPC to have moved")
