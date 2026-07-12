module PokeGold.Tests.EvolutionSceneTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Scenes

let private press (scene: Scene) buttons =
    let transition = scene.Update buttons
    scene.Update Buttons.none |> ignore
    transition

[<Fact>]
let ``BAT-014 evolution accepts exactly once`` () =
    let mutable decisions = []
    let scene =
        EvolutionScene(
            Content().Font,
            "CYNDAQUIL",
            "QUILAVA",
            fun decision -> decisions <- decisions @ [ decision ]) :> Scene

    Assert.Equal(Stay, scene.Update Buttons.none)
    Assert.Equal(Pop, press scene { Buttons.none with A = true })
    Assert.Equal<EvolutionDecision list>([ AcceptEvolution ], decisions)

    Assert.Equal(Pop, scene.Update { Buttons.none with B = true })
    Assert.Equal<EvolutionDecision list>([ AcceptEvolution ], decisions)

[<Fact>]
let ``BAT-014 evolution cancellation is explicit and one-shot`` () =
    let mutable decisions = []
    let scene =
        EvolutionScene(
            Content().Font,
            "EEVEE",
            "UMBREON",
            fun decision -> decisions <- decisions @ [ decision ]) :> Scene

    Assert.Equal(Pop, press scene { Buttons.none with B = true })
    Assert.Equal<EvolutionDecision list>([ CancelEvolution ], decisions)

    Assert.Equal(Pop, scene.Update { Buttons.none with A = true })
    Assert.Equal<EvolutionDecision list>([ CancelEvolution ], decisions)
