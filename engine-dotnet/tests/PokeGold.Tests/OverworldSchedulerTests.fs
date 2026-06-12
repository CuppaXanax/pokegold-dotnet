module PokeGold.Tests.OverworldSchedulerTests

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

let private directionButton (dir: Direction) : Buttons =
    match dir with
    | Down -> { Buttons.none with Down = true }
    | Up -> { Buttons.none with Up = true }
    | Left -> { Buttons.none with Left = true }
    | Right -> { Buttons.none with Right = true }

let private delta (dir: Direction) : int * int =
    match dir with
    | Down -> 0, 1
    | Up -> 0, -1
    | Left -> -1, 0
    | Right -> 1, 0

let private openStep (state: OverworldState) : (int * int * Direction) =
    let sx, sy = Movement.findStartCell state.Map state.Collision

    [ Down; Up; Left; Right ]
    |> List.tryPick (fun dir ->
        let dx, dy = delta dir

        if Movement.cellWalkable state.Map state.Collision (sx + dx) (sy + dy) then
            Some(sx, sy, dir)
        else
            None)
    |> Option.defaultWith (fun () -> failwith "expected at least one open step")

let private applyTransition (stack: ResizeArray<Scene>) (transition: Transition) =
    match transition with
    | Stay -> ()
    | Push scene -> stack.Add scene
    | Pop ->
        if stack.Count > 1 then
            stack.RemoveAt(stack.Count - 1)
    | Replace scene ->
        stack.[stack.Count - 1] <- scene

let private tickStack (stack: ResizeArray<Scene>) (frame: int) =
    let top = stack.[stack.Count - 1]
    let buttons =
        match top.GetType().Name with
        | "YesNoScene" -> { Buttons.none with A = true }
        | "TextBoxScene" when frame % 2 = 0 -> { Buttons.none with A = true }
        | _ -> Buttons.none

    (top.Update buttons) |> applyTransition stack

let private scriptedScene content mapId x y facing label commands =
    let baseState = OverworldState.loadByIdAt content mapId x y facing

    { baseState with
        Events =
            { baseState.Events with
                Scenes = [| "SCENE_TEST" |]
                SceneLabels = [| label |]
                Coords = [||]
                Callbacks = [||] }
        Script =
            { Commands = commands
              Labels = Map.ofList [ label, 0 ] } }

let private moveId name =
    MovesData.byIndex |> Array.findIndex (fun move -> move.Name = name)

let private averageBrightness (fb: Framebuffer) =
    let pixels = fb.Pixels
    let mutable total = 0L
    let mutable i = 0

    while i < pixels.Length do
        total <- total + int64 (int pixels.[i]) + int64 (int pixels.[i + 1]) + int64 (int pixels.[i + 2])
        i <- i + 4

    float total / float Display.PixelCount

let private renderBrightness (scene: Scene) =
    let fb = Framebuffer()
    scene.Render fb
    averageBrightness fb

[<Fact>]
let ``restore runs map callbacks through the scheduler`` () =
    let content = Content()
    let scene =
        OverworldScene(
            content,
            SilentSound(),
            OverworldState.loadByIdAt content "NewBarkTown" 13 6 Down)

    let world = World.empty |> World.setEvent "EVENT_FIRST_TIME_BANKING_WITH_MOM"
    scene.Restore(world, PlayerStateOps.initial)

    Assert.True(World.hasFlag "ENGINE_FLYPOINT_NEW_BARK" scene.DebugWorld)
    Assert.False(World.hasEvent "EVENT_FIRST_TIME_BANKING_WITH_MOM" scene.DebugWorld)

[<Fact>]
let ``fade specials render palette overlay and resume script after palette steps`` () =
    let content = Content()
    let doneEvent = "EVENT_TEST_FADE_DONE"
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "FadeScene"
            [| Special "FadeOutToBlack"
               Special "FadeInFromBlack"
               Setevent doneEvent
               End |]

    let baseline =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "NewBarkTown" 5 5 Down)
    baseline.Restore(World.empty, PlayerStateOps.initial)
    let baselineBrightness = renderBrightness (baseline :> Scene)

    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, PlayerStateOps.initial)

    let tickFade frames =
        for _ in 1 .. frames do
            match (scene :> Scene).Update Buttons.none with
            | Stay -> ()
            | other -> failwithf "expected fade tick to stay, got %A" other

    Assert.False(World.hasEvent doneEvent scene.DebugWorld)
    Assert.False(scene.CanCapture)

    tickFade 4
    let midFadeBrightness = renderBrightness (scene :> Scene)
    Assert.True(midFadeBrightness < baselineBrightness * 0.75, sprintf "mid fade brightness %f was not below baseline %f" midFadeBrightness baselineBrightness)

    tickFade 4
    Assert.False(World.hasEvent doneEvent scene.DebugWorld)
    let blackBrightness = renderBrightness (scene :> Scene)
    Assert.True(blackBrightness < 1.0, sprintf "fade-in should begin from black, brightness was %f" blackBrightness)

    tickFade 8
    Assert.True(World.hasEvent doneEvent scene.DebugWorld)
    Assert.True(scene.CanCapture)
    let restoredBrightness = renderBrightness (scene :> Scene)
    Assert.True(restoredBrightness > baselineBrightness * 0.8, sprintf "fade-in did not restore scene brightness %f vs %f" restoredBrightness baselineBrightness)

[<Fact>]
let ``money and coin script effects mutate player state`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "MoneyScene"
            [| Checkmoney [ "YOUR_MONEY"; "3000" ]
               Takemoney [ "YOUR_MONEY"; "500" ]
               Givecoins(Some 50)
               Checkcoins(Some 50)
               Takecoins(Some 20)
               End |]

    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, PlayerStateOps.initial)

    Assert.Equal(2500, scene.DebugPlayer.Money)
    Assert.Equal(30, scene.DebugPlayer.Coins)

[<Fact>]
let ``card flip special plays a three coin fair game when player has coin case`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "CardFlipScene"
            [| Special "CardFlip"; End |]

    let player =
        { PlayerStateOps.initial with
            Coins = 3
            Bag = Bag.add "COIN_CASE" 1 PlayerStateOps.initial.Bag }

    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, player)

    Assert.True([ 0; 6 ] |> List.contains scene.DebugPlayer.Coins)

[<Fact>]
let ``checkcoins returns HAVE_LESS for insufficient coins`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "CoinBranchScene"
            [| Checkcoins(Some 50)
               Ifequal(2, "Less")
               Givecoins(Some 10)
               End
               Givecoins(Some 1)
               End |]

    let state =
        { state with
            Script = { state.Script with Labels = Map.add "Less" 4 state.Script.Labels } }

    let player = { PlayerStateOps.initial with Coins = 49 }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, player)

    Assert.Equal(50, scene.DebugPlayer.Coins)

[<Fact>]
let ``contest drop off masks party to lead mon`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "ContestDropOffScene"
            [| Special "ContestDropOffMons"; End |]

    let player =
        { PlayerStateOps.initial with
            Party = [ PartyMon.create 155 10; PartyMon.create 158 10 ] }

    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, player)

    Assert.Equal<int list>([ 155 ], scene.DebugPlayer.Party |> List.map (fun mon -> mon.SpeciesId))

[<Fact>]
let ``bug contest judging gives first place for Scyther or Pinsir`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "ContestJudgingScene"
            [| Special "BugContestJudging"
               Ifequal(1, "Won")
               End
               Setevent "EVENT_TEST_BUG_CONTEST_WIN"
               End |]

    let state =
        { state with
            Script = { state.Script with Labels = Map.add "Won" 3 state.Script.Labels } }

    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty |> World.setVar "__bug_contest_caught_species" 123, PlayerStateOps.initial)

    Assert.True(World.hasEvent "EVENT_TEST_BUG_CONTEST_WIN" scene.DebugWorld)

[<Fact>]
let ``check party full after contest reports caught mon and removes park balls`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "ContestCaughtScene"
            [| Special "CheckPartyFullAfterContest"
               Ifequal(0, "Caught")
               End
               Setevent "EVENT_TEST_CONTEST_CAUGHT"
               End |]

    let state =
        { state with
            Script = { state.Script with Labels = Map.add "Caught" 3 state.Script.Labels } }

    let player =
        { PlayerStateOps.initial with
            Party = [ PartyMon.create 123 14 ]
            Bag = Bag.add "PARK_BALL" 20 PlayerStateOps.initial.Bag }

    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty |> World.setVar "__bug_contest_caught_species" 123, player)

    Assert.True(World.hasEvent "EVENT_TEST_CONTEST_CAUGHT" scene.DebugWorld)
    Assert.Equal(0, Bag.count "PARK_BALL" scene.DebugPlayer.Bag)

[<Fact>]
let ``trainer battle runtime grants EXP and Amulet Coin prize money`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "TrainerBattleScene"
            [| Loadtrainer("YOUNGSTER", "JOEY1")
               Startbattle
               Writetext "BattleDone"
               End |]
        |> fun s -> { s with Text = Map.ofList [ "BattleDone", "done<DONE>" ] }

    let mon =
        let baseMon = PartyMon.create (Species.byName "CYNDAQUIL").Dex 50
        { baseMon with
            Moves = [ moveId "EMBER", (Moves.byName "EMBER").Pp ]
            HeldItem = Some "AMULET_COIN" }
    let player =
        { PlayerStateOps.initial with
            Money = 1000
            Party = [ mon ] }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    while frame < 1000 && stack.Count > 1 do
        frame <- frame + 1
        let buttons =
            match stack.[stack.Count - 1].GetType().Name with
            | "BattleScene"
            | "TextBoxScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        applyTransition stack (stack.[stack.Count - 1].Update buttons)

    Assert.Equal(1, stack.Count)

    match (scene :> Scene).Update Buttons.none with
    | Push (:? TextBoxScene) -> ()
    | other -> failwithf "expected resumed battle script to push text, got %A" other

    Assert.Equal(Some "BattleDone", scene.RuntimeSnapshot.LastTextLabel)
    Assert.True(scene.DebugPlayer.Party.[0].Exp > 0, "battle reward should grant EXP")
    Assert.True(scene.DebugPlayer.Money > 1000, "trainer battle should award prize money")

[<Fact>]
let ``wild battle runtime catches Pokemon with Master Ball`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "WildCatchScene"
            [| Loadwildmon("RATTATA", 4)
               Startbattle
               Writetext "CatchDone"
               End |]
        |> fun s -> { s with Text = Map.ofList [ "CatchDone", "done<DONE>" ] }

    let ember = Moves.byName "EMBER"
    let mon =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 20 with
            Moves = [ moveId "EMBER", ember.Pp ] }
    let player =
        { PlayerStateOps.initial with
            Party = [ mon ]
            Bag = Bag.add "MASTER_BALL" 1 Bag.empty }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    while frame < 1000 && stack.Count > 1 do
        frame <- frame + 1
        let buttons =
            match frame % 6 with
            | 0 -> { Buttons.none with Right = true }
            | 2 -> { Buttons.none with A = true }
            | 4 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        applyTransition stack (stack.[stack.Count - 1].Update buttons)

    Assert.Equal(1, stack.Count)

    match (scene :> Scene).Update Buttons.none with
    | Push (:? TextBoxScene) -> ()
    | other -> failwithf "expected resumed catch script text, got %A" other

    let rattataDex = (Species.byName "RATTATA").Dex
    Assert.Equal(0, Bag.count "MASTER_BALL" scene.DebugPlayer.Bag)
    Assert.Equal(2, scene.DebugPlayer.Party.Length)
    Assert.Contains(rattataDex, scene.DebugPlayer.DexOwn)
    Assert.Contains(rattataDex, scene.DebugPlayer.DexSeen)
    Assert.Equal(Some "CatchDone", scene.RuntimeSnapshot.LastTextLabel)

[<Fact>]
let ``caught Pokemon goes to PC when party is full`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "PcCatchScene"
            [| Loadwildmon("RATTATA", 4)
               Startbattle
               End |]
    let party = [ for i in 1 .. 6 -> PartyMon.create (Species.byName "CYNDAQUIL").Dex (10 + i) ]
    let player =
        { PlayerStateOps.initial with
            Party = party
            Bag = Bag.add "MASTER_BALL" 1 Bag.empty }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    while frame < 1000 && stack.Count > 1 do
        frame <- frame + 1
        let buttons =
            match frame % 6 with
            | 0 -> { Buttons.none with Right = true }
            | 2 -> { Buttons.none with A = true }
            | 4 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        applyTransition stack (stack.[stack.Count - 1].Update buttons)

    Assert.Equal(6, scene.DebugPlayer.Party.Length)
    Assert.Single(scene.DebugPlayer.Pc.Boxes.[scene.DebugPlayer.Pc.CurrentBox].Mons)

[<Fact>]
let ``wild battle loss heals party and halves money on script resume`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "WhiteoutScene"
            [| Loadwildmon("MEWTWO", 100)
               Startbattle
               Writetext "AfterLoss"
               End |]
        |> fun s -> { s with Text = Map.ofList [ "AfterLoss", "after<DONE>" ] }

    let splash = Moves.byName "SPLASH"
    let faintable =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 2 with
            Hp = 1
            Moves = [ moveId "SPLASH", splash.Pp ] }
    let player =
        { PlayerStateOps.initial with
            Money = 2000
            Party = [ faintable ] }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    while frame < 1000 && stack.Count > 1 do
        frame <- frame + 1
        let buttons = if frame % 2 = 0 then { Buttons.none with A = true } else Buttons.none
        applyTransition stack (stack.[stack.Count - 1].Update buttons)

    Assert.Equal(1, stack.Count)

    match (scene :> Scene).Update Buttons.none with
    | Push (:? TextBoxScene) -> ()
    | other -> failwithf "expected post-loss script text, got %A" other

    Assert.Equal(1000, scene.DebugPlayer.Money)
    Assert.True(scene.DebugPlayer.Party.[0].Hp > 1, "loss should heal party")
    Assert.Equal(Some "AfterLoss", scene.RuntimeSnapshot.LastTextLabel)

[<Fact>]
let ``phone contact script effects mutate player state`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "PhoneScene"
            [| Addcellnum "PHONE_MOM"
               Checkcellnum "PHONE_MOM"
               Iffalse "PhoneScene.Fail"
               Writetext "PhoneOk"
               End
               Writetext "PhoneFail"
               End |]
        |> fun s -> { s with Script = { s.Script with Labels = Map.add "AskPhoneScene.Fail" 6 s.Script.Labels } }
        |> fun s -> { s with Text = Map.ofList [ "PhoneOk", "ok<DONE>"; "PhoneFail", "fail<DONE>" ] }

    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, PlayerStateOps.initial)

    Assert.Contains("PHONE_MOM", scene.DebugPlayer.PhoneContacts)
    Assert.Equal(Some "PhoneOk", scene.RuntimeSnapshot.LastTextLabel)

[<Fact>]
let ``askforphonenumber returns ROM contact result codes`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "AskPhoneScene"
            [| Askforphonenumber "PHONE_MOM"
               Ifnotequal(0, "AskPhoneScene.Fail")
               Checkcellnum "PHONE_MOM"
               Iffalse "AskPhoneScene.Fail"
               Writetext "PhoneOk"
               End
               Writetext "PhoneFail"
               End |]
        |> fun s -> { s with Text = Map.ofList [ "PhoneOk", "ok<DONE>"; "PhoneFail", "fail<DONE>" ] }

    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, PlayerStateOps.initial)

    let prompt =
        match (scene :> Scene).Update Buttons.none with
        | Push (:? YesNoScene as yesNo) -> yesNo
        | other -> failwithf "expected YesNoScene, got %A" other

    Assert.Equal(Pop, (prompt :> Scene).Update { Buttons.none with A = true })

    match (scene :> Scene).Update Buttons.none with
    | Push _ -> ()
    | other -> failwithf "expected resumed phone script to push text, got %A" other

    Assert.Contains("PHONE_MOM", scene.DebugPlayer.PhoneContacts)
    Assert.Equal(Some "PhoneOk", scene.RuntimeSnapshot.LastTextLabel)

[<Fact>]
let ``askforphonenumber reports full contact list`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "PhoneFullScene"
            [| Askforphonenumber "PHONE_MOM"
               Ifequal(1, "PhoneFullScene.Full")
               Writetext "PhoneFail"
               End
               Writetext "PhoneFull"
               End |]
        |> fun s -> { s with Script = { s.Script with Labels = Map.add "PhoneFullScene.Full" 4 s.Script.Labels } }
        |> fun s -> { s with Text = Map.ofList [ "PhoneFull", "full<DONE>"; "PhoneFail", "fail<DONE>" ] }
    let fullContacts = [ for i in 0 .. 9 -> sprintf "PHONE_SLOT_%02d" i ] |> Set.ofList
    let player = { PlayerStateOps.initial with PhoneContacts = fullContacts }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, player)

    let prompt =
        match (scene :> Scene).Update Buttons.none with
        | Push (:? YesNoScene as yesNo) -> yesNo
        | other -> failwithf "expected YesNoScene, got %A" other

    Assert.Equal(Pop, (prompt :> Scene).Update { Buttons.none with A = true })

    match (scene :> Scene).Update Buttons.none with
    | Push _ -> ()
    | other -> failwithf "expected full-phone script to push text, got %A" other

    Assert.DoesNotContain("PHONE_MOM", scene.DebugPlayer.PhoneContacts)
    Assert.Equal(Some "PhoneFull", scene.RuntimeSnapshot.LastTextLabel)

[<Fact>]
let ``BankOfMom scene persists saving flag and resumes script`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "PlayersHouse1F"
            7
            7
            Up
            "BankScene"
            [| Special "BankOfMom"
               Checkflag "ENGINE_MOM_ACTIVE"
               Iffalse "BankScene.Fail"
               Checkflag "ENGINE_MOM_SAVING_MONEY"
               Iffalse "BankScene.Fail"
               Writetext "BankOk"
               End
               Writetext "BankFail"
               End |]
        |> fun s -> { s with Script = { s.Script with Labels = Map.add "BankScene.Fail" 7 s.Script.Labels } }
        |> fun s -> { s with Text = Map.ofList [ "BankOk", "ok<DONE>"; "BankFail", "fail<DONE>" ] }

    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, PlayerStateOps.initial)

    let bank =
        match (scene :> Scene).Update Buttons.none with
        | Push (:? MomBankScene as bank) -> bank
        | other -> failwithf "expected MomBankScene, got %A" other

    (bank :> Scene).Update { Buttons.none with Down = true } |> ignore
    (bank :> Scene).Update Buttons.none |> ignore
    (bank :> Scene).Update { Buttons.none with Down = true } |> ignore
    (bank :> Scene).Update Buttons.none |> ignore
    (bank :> Scene).Update { Buttons.none with A = true } |> ignore
    (bank :> Scene).Update Buttons.none |> ignore
    (bank :> Scene).Update { Buttons.none with A = true } |> ignore
    (bank :> Scene).Update Buttons.none |> ignore
    Assert.Equal(Pop, (bank :> Scene).Update { Buttons.none with B = true })

    match (scene :> Scene).Update Buttons.none with
    | Push _ -> ()
    | other -> failwithf "expected bank script to push text, got %A" other

    Assert.True(World.hasFlag "ENGINE_MOM_ACTIVE" scene.DebugWorld)
    Assert.True(World.hasFlag "ENGINE_MOM_SAVING_MONEY" scene.DebugWorld)
    Assert.Equal(Some "BankOk", scene.RuntimeSnapshot.LastTextLabel)

[<Fact>]
let ``MapRadio opens PokeGear radio tab and resumes script`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "RadioScene"
            [| Special "PlaceMoneyTopRight"
               Closewindow
               Setval 4
               Special "MapRadio"
               Writetext "RadioOk"
               End |]
        |> fun s -> { s with Text = Map.ofList [ "RadioOk", "ok<DONE>" ] }

    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, PlayerStateOps.initial)

    let gear =
        match (scene :> Scene).Update Buttons.none with
        | Push (:? PokegearScene as gear) -> gear
        | other -> failwithf "expected PokegearScene, got %A" other

    Assert.Equal(RadioTab, gear.CurrentTab)
    Assert.Equal(Pop, (gear :> Scene).Update { Buttons.none with B = true })

    match (scene :> Scene).Update Buttons.none with
    | Push _ -> ()
    | other -> failwithf "expected radio script to push text, got %A" other

    Assert.Equal(Some "RadioOk", scene.RuntimeSnapshot.LastTextLabel)

[<Fact>]
let ``RTC script effects use persistent game time state`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "RtcScene"
            [| Special "SetDayOfWeek"
               Readvar "VAR_WEEKDAY"
               Ifnotequal(5, "RtcScene.Fail")
               Checktime "NITE"
               Iffalse "RtcScene.Fail"
               Special "InitialSetDSTFlag"
               Writetext "RtcOk"
               End
               Writetext "RtcFail"
               End |]
        |> fun s -> { s with Text = Map.ofList [ "RtcOk", "ok<DONE>"; "RtcFail", "fail<DONE>" ] }

    let player =
        { PlayerStateOps.initial with
            GameTime = GameTimeState.create 22 10 5 false }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, player)

    let weekdayScene =
        match (scene :> Scene).Update Buttons.none with
        | Push (:? WeekdayScene as weekday) -> weekday
        | other -> failwithf "expected WeekdayScene, got %A" other

    Assert.Equal(5, weekdayScene.Weekday)
    (weekdayScene :> Scene).Update { Buttons.none with A = true } |> ignore
    (weekdayScene :> Scene).Update Buttons.none |> ignore
    Assert.Equal(Pop, (weekdayScene :> Scene).Update { Buttons.none with A = true })

    match (scene :> Scene).Update Buttons.none with
    | Push _ -> ()
    | other -> failwithf "expected resumed script to push text, got %A" other

    Assert.Equal(Some "RtcOk", scene.RuntimeSnapshot.LastTextLabel)
    Assert.True(scene.DebugPlayer.GameTime.IsDst)
    Assert.Equal(5, World.getVar "VAR_WEEKDAY" scene.DebugWorld)

[<Fact>]
let ``party HM field move dispatches through overworld runtime`` () =
    let content = Content()
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove "FLASH" [] }
    let player = { PlayerStateOps.initial with Party = [ mon ] }
    let world = World.empty |> World.setFlag "ENGINE_ZEPHYRBADGE"
    let scene = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "NewBarkTown" 5 5 Down)
    scene.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)

    let press buttons =
        let top = stack.[stack.Count - 1]
        applyTransition stack (top.Update buttons)
        applyTransition stack (stack.[stack.Count - 1].Update Buttons.none)

    press { Buttons.none with Start = true }
    press { Buttons.none with Down = true }
    press { Buttons.none with A = true }
    press { Buttons.none with A = true }

    for _ in 1 .. 3 do
        press { Buttons.none with Down = true }

    press { Buttons.none with A = true }

    Assert.Equal("TextBoxScene", stack.[stack.Count - 1].GetType().Name)
    Assert.Equal(1, World.getVar "__flash_active" scene.DebugWorld)
    Assert.Equal("FLASH", World.getBuffer "__last_field_move" scene.DebugWorld)

[<Fact>]
let ``facing water triggers Surf field move through overworld A press`` () =
    let content = Content()
    let probe = OverworldState.loadById content "NewBarkTown"
    let directions = [ Down, (0, 1); Up, (0, -1); Left, (-1, 0); Right, (1, 0) ]
    let start =
        seq {
            for y in 0 .. probe.Map.Height * 2 - 1 do
                for x in 0 .. probe.Map.Width * 2 - 1 do
                    if Movement.cellWalkable probe.Map probe.Collision x y then
                        for dir, (dx, dy) in directions do
                            let coll = Movement.collisionIdAtCell probe.Map probe.Collision (x + dx) (y + dy)
                            if coll = FieldMoves.CollSurf || coll = FieldMoves.CollWater21 then
                                yield x, y, dir
        }
        |> Seq.tryHead
        |> Option.defaultWith (fun () -> failwith "expected New Bark to have a land cell facing water")

    let x, y, facing = start
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove "SURF" [] }
    let player = { PlayerStateOps.initial with Party = [ mon ] }
    let world = World.empty |> World.setFlag "ENGINE_FOGBADGE"
    let scene = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "NewBarkTown" x y facing)
    scene.Restore(world, player)

    match (scene :> Scene).Update { Buttons.none with A = true } with
    | Push (:? TextBoxScene) -> ()
    | other -> failwithf "expected Surf prompt text, got %A" other

    Assert.Equal(1, World.getVar "__surfing" scene.DebugWorld)
    Assert.Equal("SURF", World.getBuffer "__last_field_move" scene.DebugWorld)

[<Fact>]
let ``runtime text resolver substitutes rival name buffer`` () =
    let content = Content()
    let state =
        scriptedScene content "NewBarkTown" 5 5 Down "RivalTextScene" [| Writetext "RivalText"; End |]
        |> fun s -> { s with Text = Map.ofList [ "RivalText", "Rival is <RIVAL><DONE>" ] }

    let scene = OverworldScene(content, SilentSound(), state)
    let world = World.empty |> World.setBuffer "__rival_name" "SNEASEL"

    scene.Restore(world, PlayerStateOps.initial)

    Assert.Equal(Some "Rival is SNEASEL<DONE>", scene.RuntimeSnapshot.LastRenderedText)

[<Fact>]
let ``idle overworld can be captured but transient scripts cannot`` () =
    let content = Content()
    let idle = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down)
    idle.Restore(World.empty, PlayerStateOps.initial)

    Assert.True(idle.CanCapture)
    idle.Capture() |> ignore

    let busy =
        OverworldScene(
            content,
            SilentSound(),
            OverworldState.loadByIdAt content "PlayersHouse1F" 7 7 Up)
    let initialWorld =
        World.empty
        |> World.setEvent "EVENT_INITIALIZED_EVENTS"
        |> World.setEvent "EVENT_PLAYERS_HOUSE_MOM_2"

    busy.Restore(initialWorld, PlayerStateOps.initial)

    Assert.False(busy.CanCapture)
    Assert.Throws<System.InvalidOperationException>(fun () -> busy.Capture() |> ignore) |> ignore

[<Fact>]
let ``direct event flags do not pop already loaded Mom objects`` () =
    let content = Content()
    let scene =
        OverworldScene(
            content,
            SilentSound(),
            OverworldState.loadByIdAt content "PlayersHouse1F" 7 7 Up)

    let initialWorld =
        World.empty
        |> World.setEvent "EVENT_INITIALIZED_EVENTS"
        |> World.setEvent "EVENT_PLAYERS_HOUSE_MOM_2"
        |> World.setScene "PlayersHouse1F" 1

    scene.Restore(initialWorld, PlayerStateOps.initial)

    let events = (MapsData.byName "PlayersHouse1F").Value.Events.Objects
    let mom1 = events.[0]
    let mornMom2 = events.[1]

    Assert.True(scene.DebugVisible mom1)
    Assert.False(scene.DebugVisible mornMom2)

    scene.DebugSetEvent "EVENT_PLAYERS_HOUSE_MOM_1" true
    scene.DebugSetEvent "EVENT_PLAYERS_HOUSE_MOM_2" false

    Assert.True(scene.DebugVisible mom1)
    Assert.False(scene.DebugVisible mornMom2)
    Assert.True(World.hasEvent "EVENT_PLAYERS_HOUSE_MOM_1" scene.DebugWorld)
    Assert.False(World.hasEvent "EVENT_PLAYERS_HOUSE_MOM_2" scene.DebugWorld)

[<Fact>]
let ``Mom cutscene keeps Mom1 as the live actor while staging future flags`` () =
    let content = Content()
    let overworld =
        OverworldScene(
            content,
            SilentSound(),
            OverworldState.loadByIdAt content "PlayersHouse1F" 7 7 Up)

    let initialWorld =
        World.empty
        |> World.setEvent "EVENT_INITIALIZED_EVENTS"
        |> World.setEvent "EVENT_PLAYERS_HOUSE_MOM_2"

    overworld.Restore(initialWorld, PlayerStateOps.initial)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)

    let events = (MapsData.byName "PlayersHouse1F").Value.Events.Objects
    let mom1 = events.[0]
    let mornMom2 = events.[1]
    let mutable sawFutureFlags = false
    let mutable sawPokegearReceipt = false

    for frame in 1 .. 2500 do
        tickStack stack frame

        match overworld.RuntimeSnapshot.LastTextLabel, overworld.RuntimeSnapshot.LastRenderedText with
        | Some "ReceivedItemText", Some text ->
            Assert.Contains("#GEAR", text)
            Assert.DoesNotContain("PokegearName", text)
            Assert.DoesNotContain("STRING_BUFFER", text)
            Assert.DoesNotContain("ItemText", text)
            sawPokegearReceipt <- true
        | _ -> ()

        if World.hasEvent "EVENT_PLAYERS_HOUSE_MOM_1" overworld.DebugWorld then
            sawFutureFlags <- true

        if sawFutureFlags then
            Assert.True(overworld.DebugVisible mom1, $"Mom1 disappeared mid-cutscene at frame {frame}")
            Assert.False(overworld.DebugVisible mornMom2, $"Mom2 appeared mid-cutscene at frame {frame}")

    Assert.True(sawFutureFlags, "Mom script should stage future visibility flags during the cutscene")
    Assert.True(sawPokegearReceipt, "Mom script should render the Pokegear receipt text")

[<Fact>]
let ``runtime text resolver substitutes named RAM buffers`` () =
    let content = Content()
    let state =
        scriptedScene content "NewBarkTown" 5 5 Down "RamTextScene" [| Writetext "RamText"; End |]
        |> fun s -> { s with Text = Map.ofList [ "RamText", "<RAM_wBattleMonNickname> fainted!<DONE>" ] }

    let overworld = OverworldScene(content, SilentSound(), state)
    let world = World.empty |> World.setBuffer "wBattleMonNickname" "CYNDAQUIL"

    overworld.Restore(world, PlayerStateOps.initial)

    Assert.Equal(Some "RamText", overworld.RuntimeSnapshot.LastTextLabel)
    Assert.Equal(Some "CYNDAQUIL fainted!<DONE>", overworld.RuntimeSnapshot.LastRenderedText)

[<Fact>]
let ``Elm intro scene keeps Elm as a stable actor through player movement`` () =
    let content = Content()
    let overworld =
        OverworldScene(
            content,
            SilentSound(),
            OverworldState.loadByIdAt content "ElmsLab" 4 8 Up)

    overworld.Restore(World.empty, PlayerStateOps.initial)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    let elm = (MapsData.byName "ElmsLab").Value.Events.Objects.[0]
    let mutable reachedText = false

    for frame in 1 .. 400 do
        tickStack stack frame

        if stack.Count > 1 then
            reachedText <- true
            Assert.True(overworld.DebugVisible elm, $"Elm disappeared before intro text at frame {frame}")
            Assert.Equal(Left, overworld.DebugState.Npcs.[0].Facing)

    Assert.True(reachedText, "Elm intro should reach its text scene")

[<Fact>]
let ``New Bark teacher remains a live actor while stopping the player`` () =
    let content = Content()
    let baseState = OverworldState.loadByIdAt content "NewBarkTown" 1 8 Right
    let testState =
        { baseState with
            Events =
                { baseState.Events with
                    Scenes = [| "SCENE_TEST_TEACHER" |]
                    SceneLabels = [| "NewBarkTown_TeacherStopsYouScene1" |]
                    Coords = [||] } }

    let overworld = OverworldScene(content, SilentSound(), testState)
    overworld.Restore(World.empty, PlayerStateOps.initial)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    let teacher = (MapsData.byName "NewBarkTown").Value.Events.Objects.[0]
    let startX = overworld.DebugState.Npcs.[0].CellX
    let mutable sawTeacherMove = false

    for frame in 1 .. 1600 do
        tickStack stack frame

        if overworld.DebugState.Npcs.[0].CellX <> startX then
            sawTeacherMove <- true

        Assert.True(overworld.DebugVisible teacher, $"Teacher disappeared during stop-player scene at frame {frame}")

    Assert.True(sawTeacherMove, "teacher stop scene should move the teacher actor")

[<Fact>]
let ``Cherrygrove rival remains a live actor through shove and exit`` () =
    let content = Content()
    let script =
        [| Appear "CHERRYGROVECITY_RIVAL"
           Applymovement("CHERRYGROVECITY_RIVAL", "CherrygroveCity_RivalWalksToYou")
           Applymovement("PLAYER", "CherrygroveCity_RivalPushesYouOutOfTheWay")
           Applymovement("CHERRYGROVECITY_RIVAL", "CherrygroveCity_RivalExitsStageLeft")
           Disappear "CHERRYGROVECITY_RIVAL"
           End |]

    let overworld =
        OverworldScene(
            content,
            SilentSound(),
            scriptedScene content "CherrygroveCity" 34 7 Right "TestRivalShove" script)

    overworld.Restore(World.setEvent "EVENT_RIVAL_CHERRYGROVE_CITY" World.empty, PlayerStateOps.initial)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    let rival = (MapsData.byName "CherrygroveCity").Value.Events.Objects.[1]
    let mutable sawVisible = false

    for frame in 1 .. 600 do
        tickStack stack frame

        if overworld.DebugVisible rival then
            sawVisible <- true

    Assert.True(sawVisible, "rival should become visible during shove scene")
    Assert.False(overworld.DebugVisible rival)
    Assert.True(World.hasEvent "EVENT_RIVAL_CHERRYGROVE_CITY" overworld.DebugWorld)

[<Fact>]
let ``Slowpoke Well Kurt remains a live actor through reappearance movement`` () =
    let content = Content()
    let script =
        [| Disappear "SLOWPOKEWELLB1F_KURT"
           Moveobject("SLOWPOKEWELLB1F_KURT", 11, 6)
           Appear "SLOWPOKEWELLB1F_KURT"
           Applymovement("SLOWPOKEWELLB1F_KURT", "KurtSlowpokeWellVictoryMovementData")
           End |]

    let overworld =
        OverworldScene(
            content,
            SilentSound(),
            scriptedScene content "SlowpokeWellB1F" 12 6 Right "TestKurtVictory" script)

    overworld.Restore(World.setEvent "EVENT_SLOWPOKE_WELL_KURT" World.empty, PlayerStateOps.initial)

    let stack = ResizeArray<Scene>()
    stack.Add(overworld :> Scene)
    let kurt = (MapsData.byName "SlowpokeWellB1F").Value.Events.Objects.[6]
    let mutable sawVisible = false

    for frame in 1 .. 500 do
        tickStack stack frame

        if overworld.DebugVisible kurt then
            sawVisible <- true

    Assert.True(sawVisible, "Kurt should become visible before his victory movement")
    Assert.True(overworld.DebugVisible kurt)
    Assert.Equal((6, 3), (overworld.DebugState.Npcs.[6].CellX, overworld.DebugState.Npcs.[6].CellY))
    Assert.Equal(Left, overworld.DebugState.Npcs.[6].Facing)

[<Fact>]
let ``script warp runs destination entry scripts before queued continuation`` () =
    let content = Content()
    let baseState = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down
    let warpProgram =
        { Commands =
            [| ScriptCommand.Warp("NEW_BARK_TOWN", 13, 6)
               Setevent "EVENT_AFTER_TEST_WARP"
               End |]
          Labels = Map.ofList [ "TestWarp", 0 ] }

    let testState =
        { baseState with
            Events =
                { baseState.Events with
                    Scenes = [| "SCENE_TEST" |]
                    SceneLabels = [| "TestWarp" |]
                    Callbacks = [||] }
            Script = warpProgram }

    let scene = OverworldScene(content, SilentSound(), testState)
    scene.Restore(World.empty, PlayerStateOps.initial)

    Assert.Equal("NewBarkTown", scene.Capture().Overworld.MapId)
    Assert.True(World.hasFlag "ENGINE_FLYPOINT_NEW_BARK" scene.DebugWorld)
    Assert.True(World.hasEvent "EVENT_AFTER_TEST_WARP" scene.DebugWorld)

[<Fact>]
let ``step warp uses the same destination entry callback path`` () =
    let content = Content()
    let probe = OverworldState.loadByIdAt content "AzaleaTown" 0 0 Down
    let cx, cy, dir = openStep probe
    let dx, dy = delta dir
    let tx, ty = cx + dx, cy + dy

    let state =
        { OverworldState.loadByIdAt content "AzaleaTown" cx cy dir with
            Events =
                { probe.Events with
                    Warps = [| { X = tx; Y = ty; DestMap = "NEW_BARK_TOWN"; DestWarp = 1 } |]
                    Coords = [||] } }

    let scene = OverworldScene(content, SilentSound(), state) :> Scene

    for _ in 0 .. Player.StepFrames do
        scene.Update(directionButton dir) |> ignore

    let overworld = scene :?> OverworldScene
    Assert.Equal("NewBarkTown", overworld.Capture().Overworld.MapId)
    Assert.True(World.hasFlag "ENGINE_FLYPOINT_NEW_BARK" overworld.DebugWorld)
