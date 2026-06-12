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

type private IdleScene() =
    interface Scene with
        member _.Update _ = Stay
        member _.Render _ = ()

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

let private runModalScene (scene: Scene) maxFrames =
    let stack = ResizeArray<Scene>()
    stack.Add(IdleScene() :> Scene)
    stack.Add(scene)

    let mutable frame = 0
    while frame < maxFrames && stack.Count > 1 do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top.GetType().Name with
            | "TextBoxScene"
            | "YesNoScene"
            | "PartyScene"
            | "MoveDeletionScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tickStack stack buttons

    Assert.Equal(1, stack.Count)

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

[<Fact>]
let ``daycare man deposits selected party mon`` () =
    let content = Content()
    let cyndaquil = PartyMon.create 155 10
    let pidgey = PartyMon.create 16 8
    let initial = { PlayerStateOps.initial with Party = [ cyndaquil; pidgey ] }
    let mutable changed = initial
    let mutable result = Some -1

    let scene =
        DayCareScene(content, initial, "MAN", (fun p -> changed <- p), (fun r -> result <- r)) :> Scene

    runModalScene scene 2000

    Assert.Equal(None, result)
    Assert.Equal<PartyMon list>([ pidgey ], changed.Party)
    Assert.Equal(Some cyndaquil, changed.DayCare.Mon1)
    Assert.True(changed.DayCare.EggSteps = 0)

[<Fact>]
let ``daycare man withdraws mon for base fee`` () =
    let content = Content()
    let cyndaquil = PartyMon.create 155 10
    let pidgey = PartyMon.create 16 8
    let initial =
        { PlayerStateOps.initial with
            Money = 300
            Party = [ pidgey ]
            DayCare = { PlayerStateOps.initial.DayCare with Mon1 = Some cyndaquil } }

    let mutable changed = initial
    let mutable result = Some -1
    let scene =
        DayCareScene(content, initial, "MAN", (fun p -> changed <- p), (fun r -> result <- r)) :> Scene

    runModalScene scene 2000

    Assert.Equal(None, result)
    Assert.Equal(200, changed.Money)
    Assert.Equal<PartyMon list>([ pidgey; cyndaquil ], changed.Party)
    Assert.Equal(None, changed.DayCare.Mon1)

[<Fact>]
let ``daycare outside egg pickup adds generated egg and clears egg state`` () =
    let content = Content()
    let cyndaquil = PartyMon.create 155 10
    let pidgey = PartyMon.create 16 8
    let initial =
        { PlayerStateOps.initial with
            Party = [ pidgey ]
            DayCare =
                { Mon1 = Some cyndaquil
                  Mon2 = Some pidgey
                  EggSteps = 0
                  HasEgg = true } }

    let mutable changed = initial
    let mutable result = Some -1
    let scene =
        DayCareScene(content, initial, "OUTSIDE", (fun p -> changed <- p), (fun r -> result <- r)) :> Scene

    runModalScene scene 2000

    Assert.Equal(Some 0, result)
    Assert.False(changed.DayCare.HasEgg)
    Assert.Equal(2, changed.Party.Length)
    Assert.Equal("EGG", (List.last changed.Party).Nickname)

[<Fact>]
let ``move deleter removes selected move and compacts remaining moves`` () =
    let content = Content()
    let cyndaquil =
        { PartyMon.create 155 10 with
            Moves = [ 33, 35; 45, 30; 52, 25 ] }

    let initial = { PlayerStateOps.initial with Party = [ cyndaquil ] }
    let mutable changed = initial
    let scene = MoveDeletionScene(content, initial, fun p -> changed <- p) :> Scene

    runModalScene scene 2500

    Assert.Equal<(int * int) list>([ 45, 30; 52, 25 ], changed.Party.Head.Moves)

[<Fact>]
let ``magnet train officer gates on pass and warps Saffron to Goldenrod`` () =
    let content = Content()
    let overworld =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "SaffronMagnetTrainStation" 9 10 Up)

    let world = World.empty |> World.setEvent "EVENT_RESTORED_POWER_TO_KANTO"
    let player =
        { PlayerStateOps.initial with
            Bag = Bag.empty |> Bag.add "PASS" 1 }

    overworld.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    tickStack stack { Buttons.none with A = true }
    tickStack stack Buttons.none

    let completed () =
        stack.Count = 1 && overworld.DebugState.MapId = "GoldenrodMagnetTrainStation"

    let mutable frame = 0
    while frame < 4000 && not (completed ()) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top.GetType().Name with
            | "TextBoxScene"
            | "YesNoScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tickStack stack buttons

    Assert.True(completed (), "PASS-holder boarding in Saffron should arrive at Goldenrod Magnet Train Station.")

let private runHaircutBrother x y weekday initialMoney expectedMoney =
    let content = Content()
    let overworld =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "GoldenrodUnderground" x y Left)

    let cyndaquil = { PartyMon.create 155 10 with Friendship = 70 }
    let world = World.empty |> World.setVar "VAR_WEEKDAY" weekday
    let player =
        { PlayerStateOps.initial with
            Money = initialMoney
            Party = [ cyndaquil ] }

    overworld.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    tickStack stack { Buttons.none with A = true }
    tickStack stack Buttons.none

    let completed () =
        stack.Count = 1
        && overworld.CanCapture
        && World.hasFlag "ENGINE_GOLDENROD_UNDERGROUND_GOT_HAIRCUT" overworld.DebugWorld
        && overworld.DebugPlayer.Money = expectedMoney
        && overworld.DebugPlayer.Party.Head.Friendship > 70

    let mutable frame = 0
    while frame < 6000 && not (completed ()) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top.GetType().Name with
            | "TextBoxScene"
            | "YesNoScene"
            | "PartyScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tickStack stack buttons

    Assert.True(completed (), "Haircut brother should complete one paid haircut and increase friendship.")

[<Fact>]
let ``older haircut brother charges money and raises selected mon friendship`` () =
    runHaircutBrother 8 14 2 1000 500

[<Fact>]
let ``younger haircut brother charges money and raises selected mon friendship`` () =
    runHaircutBrother 8 15 3 1000 700
