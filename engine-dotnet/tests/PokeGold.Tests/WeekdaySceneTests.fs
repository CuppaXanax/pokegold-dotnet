module PokeGold.Tests.WeekdaySceneTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Scenes

let private update (scene: WeekdayScene) buttons =
    (scene :> Scene).Update buttons

let private press buttons scene =
    let transition = update scene buttons
    update scene Buttons.none |> ignore
    transition

[<Fact>]
let ``weekday scene starts from supplied weekday`` () =
    let scene = WeekdayScene(Content(), 2, ignore)

    Assert.Equal(2, scene.Weekday)

[<Fact>]
let ``weekday scene uses original up and down cycling`` () =
    let scene = WeekdayScene(Content(), 0, ignore)

    press { Buttons.none with Up = true } scene |> ignore
    Assert.Equal(1, scene.Weekday)

    press { Buttons.none with Down = true } scene |> ignore
    Assert.Equal(0, scene.Weekday)

    press { Buttons.none with Down = true } scene |> ignore
    Assert.Equal(6, scene.Weekday)

[<Fact>]
let ``weekday scene confirms selected day`` () =
    let mutable selected = None
    let scene = WeekdayScene(Content(), 4, fun day -> selected <- Some day)

    Assert.Equal(Stay, press { Buttons.none with A = true } scene)
    Assert.Equal(Pop, press { Buttons.none with A = true } scene)
    Assert.Equal(Some 4, selected)

[<Fact>]
let ``weekday scene can reject confirmation and choose again`` () =
    let mutable selected = None
    let scene = WeekdayScene(Content(), 4, fun day -> selected <- Some day)

    press { Buttons.none with A = true } scene |> ignore
    Assert.Equal(Stay, press { Buttons.none with B = true } scene)
    press { Buttons.none with Up = true } scene |> ignore
    Assert.Equal(Stay, press { Buttons.none with Start = true } scene)
    Assert.Equal(Pop, press { Buttons.none with Start = true } scene)

    Assert.Equal(Some 5, selected)
