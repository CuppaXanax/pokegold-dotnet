module PokeGold.Tests.CriticalSpecialSceneTests

open Xunit
open PokeGold.Game.Audio
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Scenes

type private SilentSound() =
    interface ISoundBoard with
        member _.PlayMusic _ = ()
        member _.PlaySfx _ = ()
        member _.PlayJingle _ = ()
        member _.StopMusic() = ()

let private press buttons (scene: Scene) =
    let transition = scene.Update buttons
    scene.Update Buttons.none |> ignore
    transition

let private applyTransition (stack: ResizeArray<Scene>) transition =
    match transition with
    | Stay -> ()
    | Push scene -> stack.Add scene
    | Pop ->
        if stack.Count > 1 then
            stack.RemoveAt(stack.Count - 1)
    | Replace scene -> stack.[stack.Count - 1] <- scene

let private tickStack (stack: ResizeArray<Scene>) buttons =
    let top = stack.[stack.Count - 1]
    top.Update buttons |> applyTransition stack

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

[<Fact>]
let ``apricorn picker returns selected item and cancel`` () =
    let mutable selected = None
    let picker =
        ApricornSelectionScene(Content(), [ "RED_APRICORN"; "BLU_APRICORN" ], fun value -> selected <- value) :> Scene

    press { Buttons.none with Down = true } picker |> ignore
    Assert.Equal(Pop, press { Buttons.none with A = true } picker)
    Assert.Equal(Some "BLU_APRICORN", selected)

    let cancel =
        ApricornSelectionScene(Content(), [ "RED_APRICORN" ], fun value -> selected <- value) :> Scene

    Assert.Equal(Pop, press { Buttons.none with B = true } cancel)
    Assert.Equal(None, selected)

[<Fact>]
let ``Kurt apricorn picker consumes selected apricorn and starts ball making`` () =
    let content = Content()
    let overworld =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "KurtsHouse" 3 3 Up)

    let world =
        World.empty
        |> World.setEvent "EVENT_CLEARED_SLOWPOKE_WELL"
        |> World.setEvent "EVENT_KURT_GAVE_YOU_LURE_BALL"

    let player =
        { PlayerStateOps.initial with
            Bag = Bag.empty |> Bag.add "BLU_APRICORN" 1 }

    overworld.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    tickStack stack { Buttons.none with A = true }
    tickStack stack Buttons.none

    let completed () =
        stack.Count = 1
        && overworld.CanCapture
        && Bag.count "BLU_APRICORN" overworld.DebugPlayer.Bag = 0
        && World.hasEvent "EVENT_GAVE_KURT_BLU_APRICORN" overworld.DebugWorld
        && World.hasFlag "ENGINE_KURT_MAKING_BALLS" overworld.DebugWorld

    let mutable frame = 0
    while frame < 1000 && not (completed ()) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top.GetType().Name with
            | "TextBoxScene"
            | "ApricornSelectionScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tickStack stack buttons

    Assert.True(completed (), "Kurt should consume the chosen apricorn and start making the matching ball.")
