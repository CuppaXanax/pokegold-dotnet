module PokeGold.Tests.CriticalSpecialSceneTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Scenes

let private press buttons (scene: Scene) =
    let transition = scene.Update buttons
    scene.Update Buttons.none |> ignore
    transition

[<Fact>]
let ``Mom bank scene deposits and withdraws money`` () =
    let mutable changed = PlayerStateOps.initial
    let scene =
        MomBankScene(
            Content(),
            { PlayerStateOps.initial with Money = 3000; MomSavings = 500 },
            false,
            (fun p -> changed <- p),
            ignore) :> Scene

    press { Buttons.none with Down = true } scene |> ignore
    press { Buttons.none with A = true } scene |> ignore
    press { Buttons.none with A = true } scene |> ignore

    Assert.Equal(2900, changed.Money)
    Assert.Equal(600, changed.MomSavings)

    press { Buttons.none with A = true } scene |> ignore
    press { Buttons.none with Up = true } scene |> ignore
    press { Buttons.none with A = true } scene |> ignore
    press { Buttons.none with A = true } scene |> ignore

    Assert.Equal(3000, changed.Money)
    Assert.Equal(500, changed.MomSavings)

[<Fact>]
let ``Mom bank scene toggles saving preference`` () =
    let mutable saving = false
    let scene =
        MomBankScene(
            Content(),
            PlayerStateOps.initial,
            false,
            ignore,
            (fun value -> saving <- value)) :> Scene

    press { Buttons.none with Down = true } scene |> ignore
    press { Buttons.none with Down = true } scene |> ignore
    press { Buttons.none with A = true } scene |> ignore

    Assert.True(saving)

[<Fact>]
let ``PokeGear scene opens requested tabs`` () =
    let player =
        { PlayerStateOps.initial with
            PhoneContacts = Set.ofList [ "PHONE_MOM"; "PHONE_ELM" ] }

    let mapScene = PokegearScene(Content().Font, player, initialTab = MapTab, mapId = "NewBarkTown")
    let radioScene = PokegearScene(Content().Font, player, initialTab = RadioTab, mapId = "NewBarkTown", radioChannel = 3)

    Assert.Equal(MapTab, mapScene.CurrentTab)
    Assert.Equal(RadioTab, radioScene.CurrentTab)

[<Fact>]
let ``PokeGear radio tunes a station through the onTune callback`` () =
    let mutable tuned = ""

    let scene =
        PokegearScene(
            Content().Font,
            PlayerStateOps.initial,
            initialTab = RadioTab,
            mapId = "VermilionCity",
            stations = [ "OAKS_POKEMON_TALK", "OAK'S TALK"; "POKE_FLUTE", "POKe FLUTE" ],
            onTune = fun id -> tuned <- id)

    press { Buttons.none with Down = true } (scene :> Scene) |> ignore
    press { Buttons.none with A = true } (scene :> Scene) |> ignore

    Assert.Equal("POKE_FLUTE", tuned)
    Assert.Equal(Some "POKE_FLUTE", scene.TunedStation)

[<Fact>]
let ``script menu returns one-based selections and zero on cancel`` () =
    let mutable selected = -1
    let menu = ScriptMenuScene(Content(), "TEST_MENU", fun value -> selected <- value) :> Scene

    Assert.Equal(Pop, press { Buttons.none with A = true } menu)
    Assert.Equal(1, selected)

    let cancel = ScriptMenuScene(Content(), "TEST_MENU", fun value -> selected <- value) :> Scene
    Assert.Equal(Pop, press { Buttons.none with B = true } cancel)
    Assert.Equal(0, selected)
