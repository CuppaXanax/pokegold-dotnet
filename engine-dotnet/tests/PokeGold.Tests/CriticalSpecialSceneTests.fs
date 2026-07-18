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

type private RecordingSound() =
    let sfx = ResizeArray<string>()

    member _.PlayedSfx = List.ofSeq sfx

    interface ISoundBoard with
        member _.PlayMusic _ = ()
        member _.PlaySfx name = sfx.Add name
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
let ``elevator scene selects a different source floor`` () =
    let floors =
        [ { Name = "FLOOR_B1F"; Map = "BASEMENT"; Warp = 2 }
          { Name = "FLOOR_1F"; Map = "FIRST"; Warp = 4 }
          { Name = "FLOOR_2F"; Map = "SECOND"; Warp = 3 } ]
    let mutable selected: ElevatorFloor option = None
    let scene = ElevatorScene(Content(), floors, 1, fun floor -> selected <- floor)

    Assert.Equal(1, scene.Cursor)
    (scene :> Scene).Update({ Buttons.none with Down = true }) |> ignore
    (scene :> Scene).Update(Buttons.none) |> ignore
    Assert.Equal(2, scene.Cursor)

    let transition = (scene :> Scene).Update({ Buttons.none with A = true })
    Assert.Equal(Pop, transition)

    match selected with
    | Some floor -> Assert.Equal("SECOND", floor.Map)
    | None -> Assert.Fail("expected a selected destination")

[<Fact>]
let ``Mike NPC trade appends source Machop metadata at the offered level`` () =
    let drowzee = PartyMon.create (Species.byName "DROWZEE").Dex 10
    let pidgey = PartyMon.create (Species.byName "PIDGEY").Dex 8
    let player = { PlayerStateOps.initial with Party = [ drowzee; pidgey ] }
    let mike = NpcTrades.tryFind "NPC_TRADE_MIKE" |> Option.defaultWith (fun () -> failwith "missing Mike trade")
    let mutable traded: PlayerState option = None
    let scene = NpcTradeScene(Content(), player, mike, false, fun updated -> traded <- Some updated)

    press { Buttons.none with A = true } (scene :> Scene) |> ignore
    press { Buttons.none with A = true } (scene :> Scene) |> ignore

    let updated = traded |> Option.defaultWith (fun () -> failwith "expected Mike trade callback")
    let machop = updated.Party.[1]

    Assert.Equal<int list>([ pidgey.SpeciesId; (Species.byName "MACHOP").Dex ], updated.Party |> List.map _.SpeciesId)
    Assert.Equal(10, machop.Level)
    Assert.Equal("MUSCLE", machop.Nickname)
    Assert.Equal(0x3766, machop.Dvs)
    Assert.Equal(Some "GOLD_BERRY", machop.HeldItem)
    Assert.Equal("MIKE", machop.OtName)
    Assert.Equal(37460, machop.OtId)
    Assert.Contains(machop.SpeciesId, updated.DexSeen)
    Assert.Contains(machop.SpeciesId, updated.DexOwn)

[<Fact>]
let ``Emy NPC trade rejects a male Dragonair`` () =
    let dragonair = PartyMon.createWithDvs (Species.byName "DRAGONAIR").Dex 40 0xffff
    let player = { PlayerStateOps.initial with Party = [ dragonair ] }
    let emy = NpcTrades.tryFind "NPC_TRADE_EMY" |> Option.defaultWith (fun () -> failwith "missing Emy trade")
    let mutable traded = false
    let scene = NpcTradeScene(Content(), player, emy, false, fun _ -> traded <- true)

    press { Buttons.none with A = true } (scene :> Scene) |> ignore
    press { Buttons.none with A = true } (scene :> Scene) |> ignore

    Assert.False(traded)

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
    let menu = ScriptMenuScene(Content(), "TEST_MENU", 3, fun value -> selected <- value) :> Scene

    Assert.Equal(Pop, press { Buttons.none with A = true } menu)
    Assert.Equal(1, selected)

    let cancel = ScriptMenuScene(Content(), "TEST_MENU", 3, fun value -> selected <- value) :> Scene
    Assert.Equal(Pop, press { Buttons.none with B = true } cancel)
    Assert.Equal(0, selected)

    let four = ScriptMenuScene(Content(), "PRIZE_MENU", 4, fun value -> selected <- value) :> Scene
    press { Buttons.none with Down = true } four |> ignore
    press { Buttons.none with Down = true } four |> ignore
    Assert.Equal(Pop, press { Buttons.none with A = true } four)
    Assert.Equal(3, selected)

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

[<Fact>]
let ``Daisy grooming source probability and friendship boundaries are exact`` () =
    Assert.Equal((2, (3, 3, 1)), Grooming.daisyOutcome 254)
    Assert.Equal((0, (0, 0, 0)), Grooming.daisyOutcome 255)
    Assert.Equal(3, Grooming.friendshipDelta 99 (3, 3, 1))
    Assert.Equal(3, Grooming.friendshipDelta 100 (3, 3, 1))
    Assert.Equal(3, Grooming.friendshipDelta 199 (3, 3, 1))
    Assert.Equal(1, Grooming.friendshipDelta 200 (3, 3, 1))

[<Fact>]
let ``Daisy grooming selects a party mon and applies the source friendship tier`` () =
    let content = Content()
    let overworld =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "BluesHouse" 2 4 Up, encounterRandom = System.Random(0))
    let cyndaquil = { PartyMon.create 155 10 with Friendship = 70 }
    let world = World.empty |> World.setVar "VAR_HOUR" 15
    let player = { PlayerStateOps.initial with Party = [ cyndaquil ] }
    overworld.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    tickStack stack { Buttons.none with A = true }
    tickStack stack Buttons.none

    let completed () =
        stack.Count = 1
        && overworld.CanCapture
        && World.hasFlag "ENGINE_DAISYS_GROOMING" overworld.DebugWorld
        && overworld.DebugPlayer.Party.Head.Friendship = 73

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

    Assert.True(completed (), "Daisy should groom one selected non-Egg and add source low-tier friendship.")

[<Fact>]
let ``Oak Pokedex ratings preserve every source threshold boundary`` () =
    let thresholds =
        [ 9; 19; 34; 49; 64; 79; 94; 109; 124; 139
          154; 169; 184; 199; 214; 229; 239; 248; 255 ]

    Assert.Equal<int list>(thresholds, OakPokedexRating.all |> List.map _.MaxOwned)

    OakPokedexRating.all
    |> List.iteri (fun index rating ->
        Assert.Equal(index + 1, (OakPokedexRating.forOwned rating.MaxOwned).Number)

        if index > 0 then
            let priorThreshold = OakPokedexRating.all.[index - 1].MaxOwned
            Assert.Equal(index + 1, (OakPokedexRating.forOwned (priorThreshold + 1)).Number))

    let final = OakPokedexRating.forOwned 251
    Assert.Equal(19, final.Number)
    Assert.Equal("Sfx_DexFanfare230Plus", final.Sfx)
    Assert.Contains("Whoa! A perfect", final.Text)
    Assert.Contains("Congratulations!", final.Text)

[<Fact>]
let ``SCR-009 Oaks Lab runtime shows source Oak rating and resumes his script`` () =
    let content = Content()
    let sound = RecordingSound()
    let overworld =
        OverworldScene(content, sound, OverworldState.loadByIdAt content "OaksLab" 4 3 Up)

    let player =
        { PlayerStateOps.initial with
            DexSeen = Set.ofList [ 1 .. 200 ]
            DexOwn = Set.ofList [ 1 .. 169 ] }

    let world = World.empty |> World.setEvent "EVENT_OPENED_MT_SILVER"
    overworld.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    tickStack stack { Buttons.none with A = true }
    tickStack stack Buttons.none

    let mutable ratingText = None
    let mutable frame = 0

    while frame < 6000 && not (stack.Count = 1 && overworld.CanCapture && ratingText.IsSome) do
        frame <- frame + 1

        match overworld.RuntimeSnapshot.LastTextLabel, overworld.RuntimeSnapshot.LastRenderedText with
        | Some "ProfOaksPCBoot", Some text -> ratingText <- Some text
        | _ -> ()

        let buttons =
            match stack.[stack.Count - 1].GetType().Name with
            | "TextBoxScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tickStack stack buttons

    let text = ratingText |> Option.defaultWith (fun () -> failwith "Oak rating UI never appeared")
    Assert.Contains("200 #MON seen", text)
    Assert.Contains("169 #MON owned", text)
    Assert.Contains("Have you met KURT?", text)
    Assert.Contains("His custom #\nBALLS should help.", text)
    Assert.Contains("Sfx_DexFanfare140169", sound.PlayedSfx)
    Assert.Equal(Some "OakLabGoodbyeText", overworld.RuntimeSnapshot.LastTextLabel)
    Assert.True(stack.Count = 1 && overworld.CanCapture, "Oak's Lab script should resume through its goodbye text.")

[<Fact>]
let ``celadon prize counter exchanges coins for Porygon and registers dex`` () =
    let content = Content()
    let overworld =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "CeladonGameCornerPrizeRoom" 4 2 Up)

    let player =
        { PlayerStateOps.initial with
            Coins = 9999
            Bag = Bag.add "COIN_CASE" 1 PlayerStateOps.initial.Bag }

    overworld.Restore(World.empty, player)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    tickStack stack { Buttons.none with A = true }
    tickStack stack Buttons.none

    let completed () =
        let player = overworld.DebugPlayer
        stack.Count = 1
        && player.Coins = 0
        && (player.Party |> List.exists (fun mon -> mon.SpeciesId = 137 && mon.Level = 20))
        && Set.contains 137 player.DexSeen
        && Set.contains 137 player.DexOwn

    let mutable frame = 0
    let mutable menuDowns = 0

    while frame < 6000 && not (completed ()) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top.GetType().Name with
            | "TextBoxScene"
            | "YesNoScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | "ScriptMenuScene" when frame % 2 = 0 && menuDowns < 2 ->
                menuDowns <- menuDowns + 1
                { Buttons.none with Down = true }
            | "ScriptMenuScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tickStack stack buttons

    Assert.True(completed (), "Celadon prize counter should sell Porygon for 9999 coins and register it in the dex.")

[<Fact>]
let ``name rater renames selected owned party mon`` () =
    let content = Content()
    let overworld =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "GoldenrodNameRater" 2 5 Up)

    let player =
        { PlayerStateOps.initial with
            Party = [ PartyMon.create 155 10 ] }

    overworld.Restore(World.empty, player)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)

    let tap buttons =
        tickStack stack buttons
        tickStack stack Buttons.none

    tap { Buttons.none with A = true }
    tap { Buttons.none with A = true }
    tap { Buttons.none with A = true }
    tap { Buttons.none with A = true }
    tap { Buttons.none with A = true }
    tap { Buttons.none with Start = true }

    let mutable frame = 0
    while frame < 60 && stack.Count <> 1 do
        frame <- frame + 1
        tickStack stack Buttons.none

    Assert.Equal("AAA", overworld.DebugPlayer.Party.Head.Nickname)

let private runBillsGrandpa (initialParty: PartyMon list) (completed: ResizeArray<Scene> -> OverworldScene -> bool) =
    let content = Content()
    let overworld =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "BillsHouse" 2 4 Up)

    let player =
        { PlayerStateOps.initial with
            Party = initialParty }

    overworld.Restore(World.empty, player)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    tickStack stack { Buttons.none with A = true }
    tickStack stack Buttons.none

    let mutable frame = 0
    while frame < 6000 && not (completed stack overworld) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top.GetType().Name with
            | "TextBoxScene"
            | "YesNoScene"
            | "PartyScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tickStack stack buttons

    Assert.True(completed stack overworld, "Bill's grandfather script should reach the expected branch.")

[<Fact>]
let ``Bills grandfather rewards Everstone for showing Lickitung`` () =
    runBillsGrandpa
        [ PartyMon.create 108 10 ]
        (fun stack overworld ->
            stack.Count = 1
            && Bag.count "EVERSTONE" overworld.DebugPlayer.Bag = 1
            && World.hasEvent "EVENT_SHOWED_LICKITUNG_TO_BILLS_GRANDPA" overworld.DebugWorld
            && World.hasEvent "EVENT_GOT_EVERSTONE_FROM_BILLS_GRANDPA" overworld.DebugWorld)

[<Fact>]
let ``Bills grandfather rejects the wrong shown Pokemon`` () =
    runBillsGrandpa
        [ PartyMon.create 155 10 ]
        (fun stack overworld ->
            let snapshot = overworld.RuntimeSnapshot
            stack.Count = 1
            && snapshot.LastTextLabel = Some "BillsGrandpaWrongPokemonText"
            && Bag.count "EVERSTONE" overworld.DebugPlayer.Bag = 0
            && not (World.hasEvent "EVENT_SHOWED_LICKITUNG_TO_BILLS_GRANDPA" overworld.DebugWorld))

let private runMagikarpGuru (world: World) (player: PlayerState) (completed: ResizeArray<Scene> -> OverworldScene -> bool) =
    let content = Content()
    let overworld =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "LakeOfRageMagikarpHouse" 2 4 Up)

    overworld.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    tickStack stack { Buttons.none with A = true }
    tickStack stack Buttons.none

    let mutable frame = 0
    while frame < 6000 && not (completed stack overworld) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top.GetType().Name with
            | "TextBoxScene"
            | "PartyScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tickStack stack buttons

    Assert.True(completed stack overworld, "Magikarp guru script should reach the expected length branch.")

[<Fact>]
let ``Magikarp guru rewards a new length record`` () =
    let magikarp = PartyMon.create 129 10
    let world =
        World.empty
        |> World.setEvent "EVENT_CLEARED_ROCKET_HIDEOUT"
        |> World.setEvent "EVENT_LAKE_OF_RAGE_ASKED_FOR_MAGIKARP"
    let player =
        { PlayerStateOps.initial with
            Party = [ magikarp ] }

    runMagikarpGuru
        world
        player
        (fun stack overworld ->
            stack.Count = 1
            && Bag.count "ETHER" overworld.DebugPlayer.Bag = 1
            && (World.getVar "__best_magikarp_length_feet" overworld.DebugWorld > 0
                || World.getVar "__best_magikarp_length_inches" overworld.DebugWorld > 0))

[<Fact>]
let ``Magikarp guru rejects records that are too short`` () =
    let magikarp = PartyMon.create 129 10
    let world =
        World.empty
        |> World.setEvent "EVENT_CLEARED_ROCKET_HIDEOUT"
        |> World.setEvent "EVENT_LAKE_OF_RAGE_ASKED_FOR_MAGIKARP"
        |> World.setVar "__best_magikarp_length_feet" 99
        |> World.setVar "__best_magikarp_length_inches" 11
    let player =
        { PlayerStateOps.initial with
            Party = [ magikarp ] }

    runMagikarpGuru
        world
        player
        (fun stack overworld ->
            let snapshot = overworld.RuntimeSnapshot
            stack.Count = 1
            && snapshot.LastTextLabel = Some "MagikarpLengthRaterText_TooShort"
            && Bag.count "ETHER" overworld.DebugPlayer.Bag = 0
            && World.getVar "__best_magikarp_length_feet" overworld.DebugWorld = 99)

[<Fact>]
let ``Magikarp house sign prints the current record`` () =
    let content = Content()
    let overworld =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "LakeOfRage" 25 32 Up)
    let world =
        World.empty
        |> World.setEvent "EVENT_CLEARED_ROCKET_HIDEOUT"
        |> World.setVar "__best_magikarp_length_feet" 6
        |> World.setVar "__best_magikarp_length_inches" 8
        |> World.setBuffer "__magikarp_record_holder" "GURU"

    overworld.Restore(world, PlayerStateOps.initial)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    tickStack stack { Buttons.none with A = true }
    tickStack stack Buttons.none

    let completed () =
        let snapshot = overworld.RuntimeSnapshot
        stack.Count = 1
        && snapshot.LastTextLabel = Some "KarpGuruRecordText"
        && snapshot.LastRenderedText |> Option.exists (fun text -> text.Contains("6'8\"") && text.Contains("GURU"))

    let mutable frame = 0
    while frame < 4000 && not (completed ()) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top.GetType().Name with
            | "TextBoxScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tickStack stack buttons

    Assert.True(completed (), "Magikarp house sign should print the stored length record.")

[<Fact>]
let ``Unown puzzle auto-solve warpchecks into Kabuto inner chamber`` () =
    let content = Content()
    let overworld =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "RuinsOfAlphKabutoChamber" 3 3 Up)

    overworld.Restore(World.empty, PlayerStateOps.initial)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    tickStack stack { Buttons.none with A = true }
    tickStack stack Buttons.none

    let completed () =
        stack.Count = 1
        && overworld.DebugState.MapId = "RuinsOfAlphInnerChamber"
        && World.hasEvent "EVENT_SOLVED_KABUTO_PUZZLE" overworld.DebugWorld
        && World.hasFlag "ENGINE_UNLOCKED_UNOWNS_A_TO_K" overworld.DebugWorld

    let mutable frame = 0
    while frame < 6000 && not (completed ()) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top.GetType().Name with
            | "TextBoxScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tickStack stack buttons

    Assert.True(completed (), "Kabuto chamber puzzle should auto-solve, warpcheck the opened hole, and enter the inner chamber.")

[<Fact>]
let ``Unown printer opens when all Unown forms are counted`` () =
    let content = Content()
    let overworld =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "RuinsOfAlphResearchCenter" 7 2 Up)
    let world = World.empty |> World.setVar "VAR_UNOWNCOUNT" 26

    overworld.Restore(world, PlayerStateOps.initial)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    tickStack stack { Buttons.none with A = true }
    tickStack stack Buttons.none

    let completed () =
        let snapshot = overworld.RuntimeSnapshot
        stack.Count = 1
        && snapshot.LastTextLabel = Some "UnownPrinter"
        && snapshot.LastRenderedText |> Option.exists (fun text -> text.Contains("ALPH RUINS STAMP"))

    let mutable frame = 0
    while frame < 4000 && not (completed ()) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top.GetType().Name with
            | "TextBoxScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tickStack stack buttons

    Assert.True(completed (), "Research center printer should open the Unown printer when VAR_UNOWNCOUNT is complete.")
