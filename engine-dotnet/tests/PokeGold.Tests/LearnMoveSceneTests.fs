module PokeGold.Tests.LearnMoveSceneTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Scenes

let private moveId name =
    MovesData.byIndex |> Array.findIndex (fun move -> move.Name = name)

let private press (scene: Scene) buttons =
    let transition = scene.Update buttons
    scene.Update Buttons.none |> ignore
    transition

let private moves =
    [ moveId "TACKLE", 1
      moveId "GROWL", 2
      moveId "LEECH_SEED", 3
      moveId "VINE_WHIP", 4 ]

[<Fact>]
let ``BAT-012 learn move choice returns the selected slot exactly once`` () =
    let mutable decisions = []
    let scene =
        LearnMoveScene(
            Content().Font,
            "BULBASAUR",
            "POISONPOWDER",
            moves,
            fun decision -> decisions <- decisions @ [ decision ]) :> Scene

    Assert.Equal(Stay, press scene { Buttons.none with A = true })
    Assert.Equal(Stay, press scene { Buttons.none with Down = true })
    Assert.Equal(Stay, press scene { Buttons.none with Down = true })
    Assert.Equal(Pop, press scene { Buttons.none with A = true })
    Assert.Equal<LearnMoveDecision list>([ ReplaceMove 2 ], decisions)

    Assert.Equal(Pop, scene.Update Buttons.none)
    Assert.Equal<LearnMoveDecision list>([ ReplaceMove 2 ], decisions)

[<Fact>]
let ``BAT-012 declining is confirmed and NO loops back to move learning`` () =
    let mutable decisions = []
    let scene =
        LearnMoveScene(
            Content().Font,
            "BULBASAUR",
            "SLEEP_POWDER",
            moves,
            fun decision -> decisions <- decisions @ [ decision ]) :> Scene

    // Decline the initial forget prompt, then decline the source's separate
    // "stop learning" confirmation. This must return to the first prompt.
    Assert.Equal(Stay, press scene { Buttons.none with B = true })
    Assert.Equal(Stay, press scene { Buttons.none with B = true })
    Assert.Empty(decisions)

    // Re-enter move selection, cancel it, and confirm that learning should stop.
    Assert.Equal(Stay, press scene { Buttons.none with A = true })
    Assert.Equal(Stay, press scene { Buttons.none with B = true })
    Assert.Equal(Pop, press scene { Buttons.none with A = true })
    Assert.Equal<LearnMoveDecision list>([ DeclineMove ], decisions)

[<Fact>]
let ``BAT-012 HM rejection keeps choosing until a deletable slot is selected`` () =
    let hmMoves =
        [ moveId "CUT", 7
          moveId "GROWL", 2
          moveId "LEECH_SEED", 3
          moveId "VINE_WHIP", 4 ]
    let mutable decisions = []
    let scene =
        LearnMoveScene(
            Content().Font,
            "BULBASAUR",
            "POISONPOWDER",
            hmMoves,
            fun decision -> decisions <- decisions @ [ decision ]) :> Scene

    Assert.Equal(Stay, press scene { Buttons.none with A = true })
    Assert.Equal(Stay, press scene { Buttons.none with A = true })
    Assert.Empty(decisions)

    Assert.Equal(Stay, press scene { Buttons.none with A = true })
    Assert.Equal(Stay, press scene { Buttons.none with Down = true })
    Assert.Equal(Pop, press scene { Buttons.none with A = true })
    Assert.Equal<LearnMoveDecision list>([ ReplaceMove 1 ], decisions)
