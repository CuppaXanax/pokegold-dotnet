module PokeGold.Tests.MovementScriptTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script

// ---- parser ---------------------------------------------------------------

let private azaleaSnippet =
    "AzaleaTownRivalBattleApproachMovement1:\n\
     \tstep LEFT\n\
     \tstep LEFT\n\
     \tturn_head UP\n\
     \tstep_end\n\
     \n\
     SomeOrdinaryScript:\n\
     \twritetext SomeText\n\
     \tend\n"

[<Fact>]
let ``parseMovements decodes a movement block and ignores ordinary scripts`` () =
    let m = MovementParser.parseMovements azaleaSnippet
    Assert.True(m.ContainsKey "AzaleaTownRivalBattleApproachMovement1")
    Assert.False(m.ContainsKey "SomeOrdinaryScript")

    Assert.Equal<MovementCmd[]>(
        [| MoveStep 2; MoveStep 2; MoveTurnHead 1; MoveStepEnd |],
        m.["AzaleaTownRivalBattleApproachMovement1"]
    )

[<Fact>]
let ``parseObjectConsts keeps declaration order`` () =
    let text = "\tobject_const_def\n\tconst FOO_A\n\tconst FOO_B\n\tconst FOO_C\n\nFoo_MapScripts:\n"
    Assert.Equal<string[]>([| "FOO_A"; "FOO_B"; "FOO_C" |], MovementParser.parseObjectConsts text)

// ---- generated data -------------------------------------------------------

[<Fact>]
let ``Azalea bakes its rival movement script and object constants`` () =
    let cmds = OverworldState.movementScript "AzaleaTown" "AzaleaTownRivalBattleApproachMovement1"
    Assert.True(cmds.IsSome)
    Assert.Equal(8, cmds.Value.Length) // 6 steps left, face up, end
    Assert.Equal(MoveTurnHead 1, cmds.Value.[6])
    Assert.Equal(MoveStepEnd, cmds.Value.[7])

[<Fact>]
let ``object constant resolves to its object-table index`` () =
    Assert.Equal(Some 9, OverworldState.objectIndexOf "AzaleaTown" "AZALEATOWN_RIVAL")
    Assert.Equal(Some 0, OverworldState.objectIndexOf "AzaleaTown" "AZALEATOWN_AZALEA_ROCKET1")
    Assert.Equal(None, OverworldState.objectIndexOf "AzaleaTown" "NOT_A_REAL_OBJECT")

// ---- runner ---------------------------------------------------------------

let private mkNpc x y =
    NpcObject.fromEvent
        0
        { X = x
          Y = y
          Sprite = "SPRITE_LASS"
          Movement = "SPRITEMOVEDATA_STILL"
          RadiusX = 0
          RadiusY = 0
          Hour1 = 0
          Hour2 = 0
          Palette = ""
          Type = ""
          Sight = 0
          Script = ""
          EventFlag = None }

let private open' = fun (_: int) (_: int) -> true

let private runToEnd (walkable: int -> int -> bool) (r0: MovementRunner.Run) : MovementRunner.Run =
    let mutable r = r0
    let mutable guard = 0

    while not r.Done && guard < 10000 do
        r <- MovementRunner.step walkable r
        guard <- guard + 1

    r

[<Fact>]
let ``a run of six left-steps walks six tiles and ends facing up`` () =
    let cmds =
        [| MoveStep 2; MoveStep 2; MoveStep 2; MoveStep 2; MoveStep 2; MoveStep 2; MoveTurnHead 1; MoveStepEnd |]

    let r = runToEnd open' (MovementRunner.start open' cmds (mkNpc 11 11))

    Assert.True(r.Done)
    Assert.Equal((5, 11), (r.Npc.CellX, r.Npc.CellY)) // 11 - 6 = 5
    Assert.Equal(Up, r.Npc.Facing)

[<Fact>]
let ``a step into a wall faces the direction but does not move`` () =
    let blocked = fun (_: int) (_: int) -> false
    let r = runToEnd blocked (MovementRunner.start blocked [| MoveStep 2; MoveStepEnd |] (mkNpc 11 11))

    Assert.True(r.Done)
    Assert.Equal((11, 11), (r.Npc.CellX, r.Npc.CellY))
    Assert.Equal(Left, r.Npc.Facing)

[<Fact>]
let ``turn_head changes facing without translating`` () =
    let r = runToEnd open' (MovementRunner.start open' [| MoveTurnHead 3; MoveStepEnd |] (mkNpc 4 4)) // RIGHT

    Assert.True(r.Done)
    Assert.Equal((4, 4), (r.Npc.CellX, r.Npc.CellY))
    Assert.Equal(Right, r.Npc.Facing)

[<Fact>]
let ``step_sleep holds for the requested number of frames`` () =
    let mutable r = MovementRunner.start open' [| MoveStepSleep 30; MoveStepEnd |] (mkNpc 4 4)

    for _ in 1..29 do
        r <- MovementRunner.step open' r

    Assert.False(r.Done)
    r <- MovementRunner.step open' r
    Assert.True(r.Done)

[<Fact>]
let ``an empty step_end script completes immediately`` () =
    Assert.True((MovementRunner.start open' [| MoveStepEnd |] (mkNpc 0 0)).Done)
