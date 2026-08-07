module PokeGold.Tests.OverworldSchedulerTests

open Xunit
open PokeGold.Game.Audio
open PokeGold.Game.Battle
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

type private FixedRandom(values: int list) =
    inherit System.Random()
    let mutable remaining = values

    override _.Next(maxValue: int) =
        match remaining with
        | value :: rest ->
            remaining <- rest
            value % maxValue
        | [] -> 0

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

let private driveBattlePackFirstItem frame (top: Scene) =
    match top with
    | :? BattleScene as battle ->
        let snap = battle.RuntimeSnapshot
        if frame % 2 <> 0 then
            Buttons.none
        elif snap.MessageActive || not (List.isEmpty snap.PendingMessages) then
            { Buttons.none with A = true }
        elif snap.Mode = "CommandMenu" && battle.CommandCursor <> 2 then
            { Buttons.none with Down = true }
        elif snap.Mode = "CommandMenu" || snap.Mode = "PackMenu" then
            { Buttons.none with A = true }
        else
            Buttons.none
    | _ -> Buttons.none

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

let private runContestJudging score species (rng: System.Random) =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "ContestJudgingScene"
            [| Special "BugContestJudging"; End |]

    let world =
        World.empty
        |> World.setVar "__bug_contest_caught_species" species
        |> World.setVar "__bug_contest_caught_score" score

    let scene = OverworldScene(content, SilentSound(), state, encounterRandom = rng)
    scene.Restore(world, PlayerStateOps.initial)
    scene

let private moveId name =
    MovesData.byIndex |> Array.findIndex (fun move -> move.Name = name)

let private driveStagedBattle (scene: OverworldScene) (selectMove: BattleScene -> int) maxFrames =
    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    let mutable finalBattle: BattleState option = None

    while frame < maxFrames && stack.Count > 1 do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top with
            | :? BattleScene as battle ->
                let snapshot = battle.RuntimeSnapshot
                if battle.CurrentState.Outcome.IsSome then
                    finalBattle <- Some battle.CurrentState

                if frame % 2 <> 0 then
                    Buttons.none
                elif snapshot.MessageActive || not snapshot.PendingMessages.IsEmpty then
                    { Buttons.none with A = true }
                elif snapshot.Mode = "CommandMenu" then
                    if battle.CommandCursor = 0 then { Buttons.none with A = true }
                    else { Buttons.none with Up = true }
                elif snapshot.Mode = "MoveMenu" then
                    let desired = selectMove battle
                    if battle.MoveCursor < desired then { Buttons.none with Down = true }
                    elif battle.MoveCursor > desired then { Buttons.none with Up = true }
                    else { Buttons.none with A = true }
                elif snapshot.Mode = "ForcedSwitch" then
                    let targetIndex =
                        battle.CurrentState.PlayerTeam
                        |> List.mapi (fun index mon -> index, mon)
                        |> List.tryFind (fun (index, mon) -> index > 0 && not (BattleMon.isFainted mon))
                        |> Option.map fst
                        |> Option.defaultWith (fun () -> failwith "forced replacement had no healthy bench member")
                    if battle.PartyCursor < targetIndex then { Buttons.none with Down = true }
                    elif battle.PartyCursor > targetIndex then { Buttons.none with Up = true }
                    else { Buttons.none with A = true }
                else
                    Buttons.none
            | :? TextBoxScene when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        applyTransition stack (top.Update buttons)

    if stack.Count <> 1 then
        let top = stack.[stack.Count - 1]
        let detail =
            match top with
            | :? BattleScene as battle ->
                let snapshot = battle.RuntimeSnapshot
                $"mode={snapshot.Mode} player={snapshot.PlayerSpecies} enemy={snapshot.EnemySpecies} outcome={snapshot.Outcome} messages={snapshot.PendingMessages.Length}"
            | _ -> top.GetType().Name
        failwithf "staged battle did not settle within %d frames: %s" maxFrames detail

    (scene :> Scene).Update Buttons.none |> ignore
    finalBattle |> Option.defaultWith (fun () -> failwith "staged battle never reached a terminal outcome")

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
let ``itemnotify shows the current item and source pocket then resumes`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "CeladonCafe"
            4
            3
            Up
            "ItemNotifyScript"
            [| Giveitem("POKE_BALL", 1)
               Itemnotify
               Setevent "EVENT_TEST_ITEM_NOTIFY_COMPLETE"
               End |]
    let scene = OverworldScene(content, SilentSound(), state)
    let initialPlayer = { PlayerStateOps.initial with Name = "GOLD" }
    scene.Restore(World.empty, initialPlayer)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    Assert.Equal(2, stack.Count)
    Assert.IsType<TextBoxScene>(stack.[1]) |> ignore
    Assert.False(World.hasEvent "EVENT_TEST_ITEM_NOTIFY_COMPLETE" scene.DebugWorld)

    let mutable sawTextBox = true
    let mutable frame = 0

    while frame < 1000 && (stack.Count > 1 || not (World.hasEvent "EVENT_TEST_ITEM_NOTIFY_COMPLETE" scene.DebugWorld)) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]
        if top :? TextBoxScene then sawTextBox <- true
        let buttons = if top :? TextBoxScene && frame % 2 = 0 then { Buttons.none with A = true } else Buttons.none
        applyTransition stack (top.Update buttons)

    if stack.Count = 1 then
        (scene :> Scene).Update Buttons.none |> ignore

    let rendered = scene.RuntimeSnapshot.LastRenderedText |> Option.defaultValue ""
    Assert.True(sawTextBox)
    Assert.Contains("GOLD put the", rendered)
    Assert.Contains("# BALL", rendered)
    Assert.Contains("BALL POCKET", rendered)
    Assert.True(World.hasEvent "EVENT_TEST_ITEM_NOTIFY_COMPLETE" scene.DebugWorld)
    Assert.Equal(1, Bag.count "POKE_BALL" scene.DebugPlayer.Bag)

[<Fact>]
let ``catchtutorial pushes an automated demo and resumes without mutating the player`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "Route29"
            20
            7
            Left
            "TutorialScript"
            [| Loadwildmon("RATTATA", 5)
               Catchtutorial "BATTLETYPE_TUTORIAL"
               Setevent "EVENT_TEST_TUTORIAL_COMPLETE"
               End |]
    let scene = OverworldScene(content, SilentSound(), state)
    let initialPlayer =
        { PlayerStateOps.initial with
            Party = [ PartyMon.create (Species.byName "CYNDAQUIL").Dex 5 ]
            Bag = Bag.empty |> Bag.add "POTION" 2 }
    scene.Restore(World.empty, initialPlayer)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)
    let mutable sawTutorial = false
    let mutable frame = 0

    while frame < 5000 && (stack.Count > 1 || not (World.hasEvent "EVENT_TEST_TUTORIAL_COMPLETE" scene.DebugWorld)) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]
        if top :? CatchTutorialScene then sawTutorial <- true
        applyTransition stack (top.Update Buttons.none)

    if stack.Count = 1 then
        (scene :> Scene).Update Buttons.none |> ignore

    Assert.True(sawTutorial)
    Assert.True(World.hasEvent "EVENT_TEST_TUTORIAL_COMPLETE" scene.DebugWorld)
    Assert.Equal<PlayerState>(initialPlayer, scene.DebugPlayer)

[<Fact>]
let ``Silver Cave Red disappear operand updates its event flag and live actor cache`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "SilverCaveRoom3"
            9
            11
            Up
            "DisappearRed"
            [| Disappear "SILVERCAVEROOM3_RED"
               End |]
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, PlayerStateOps.initial)

    Assert.True(World.hasEvent "EVENT_RED_IN_MT_SILVER" scene.DebugWorld)
    Assert.True(scene.RuntimeSnapshot.Actors |> List.exists (fun actor -> actor.Script = "Red" && not actor.Visible))

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
let ``SCR-008 slot winnings from a small balance buy Goldenrod Abra`` () =
    let content = Content()
    let slotState =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "SlotMachineScene"
            (Array.append (Array.create 67 (Special "SlotMachine")) [| End |])

    let startingPlayer =
        { PlayerStateOps.initial with
            Coins = 3
            Bag = Bag.add "COIN_CASE" 1 PlayerStateOps.initial.Bag }

    let slots =
        OverworldScene(
            content,
            SilentSound(),
            slotState,
            encounterRandom = FixedRandom(List.replicate 67 16)
        )

    slots.Restore(World.empty, startingPlayer)
    Assert.Equal(204, slots.DebugPlayer.Coins)

    let prizeCounter =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "GoldenrodGameCorner" 18 3 Up)

    prizeCounter.Restore(slots.DebugWorld, slots.DebugPlayer)

    let stack = ResizeArray<Scene>()
    stack.Add(prizeCounter :> Scene)

    let tick buttons =
        (stack.[stack.Count - 1].Update buttons) |> applyTransition stack

    tick { Buttons.none with A = true }
    tick Buttons.none

    let completed () =
        let player = prizeCounter.DebugPlayer
        stack.Count = 1
        && player.Coins = 4
        && (player.Party |> List.exists (fun mon -> mon.SpeciesId = 63 && mon.Level = 10))
        && Set.contains 63 player.DexSeen
        && Set.contains 63 player.DexOwn

    let mutable frame = 0
    let mutable cancelMoves = 0

    while frame < 6000 && not (completed ()) do
        frame <- frame + 1
        let boughtAbra = prizeCounter.DebugPlayer.Party |> List.exists (fun mon -> mon.SpeciesId = 63)

        let buttons =
            match stack.[stack.Count - 1].GetType().Name with
            | "TextBoxScene"
            | "YesNoScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | "ScriptMenuScene" when boughtAbra && frame % 2 = 0 && cancelMoves < 3 ->
                cancelMoves <- cancelMoves + 1
                { Buttons.none with Down = true }
            | "ScriptMenuScene" when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        tick buttons

    Assert.True(completed (), "Slot winnings should reach Goldenrod's Abra price and redeem through its real prize-counter script.")

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
let ``bug contest score uses calculated stats remaining hp held item and DVs`` () =
    let species = Species.byName "CATERPIE"
    let mon =
        { PartyMon.createWithDvs species.Dex 10 0x0000 with
            Hp = 16
            HeldItem = Some "BERRY" }
    let stats = BattleMon.calculateStats species mon.Level mon.Dvs mon.StatExp
    let expected =
        stats.MaxHp * 4
        + stats.Attack
        + stats.Defense
        + stats.Speed
        + stats.SpAttack
        + stats.SpDefense
        + mon.Hp / 8
        + 1

    Assert.Equal(expected, BugContestScore.calculate mon)
    Assert.Equal(0, BugContestScore.dvBonus 0x0000)
    Assert.Equal(30, BugContestScore.dvBonus 0xffff)

[<Fact>]
let ``bug contest NPC scores vary with a seeded contest RNG`` () =
    let first = (runContestJudging 400 123 (System.Random(1))).DebugWorld
    let second = (runContestJudging 400 123 (System.Random(2))).DebugWorld
    let scores (world: World) =
        [ 1..5 ]
        |> List.map (fun index -> World.getVar (sprintf "__bug_contest_npc_score_%d" index) world)

    Assert.False(scores first = scores second)
    Assert.All(scores first, fun score -> Assert.InRange(score, 226, 375))
    Assert.All(scores second, fun score -> Assert.InRange(score, 226, 375))

[<Fact>]
let ``bug contest placement maps the player to first second third or no place`` () =
    let npcScores = [ 300; 286; 357; 332; 318 ]
    Assert.Equal(1, BugContestPlacement.playerPlacement 400 npcScores)
    Assert.Equal(2, BugContestPlacement.playerPlacement 340 npcScores)
    Assert.Equal(3, BugContestPlacement.playerPlacement 320 npcScores)
    Assert.Equal(0, BugContestPlacement.playerPlacement 250 npcScores)

[<Fact>]
let ``bug contest timer uses the source 20 minute 60 Hz frame budget`` () =
    let started = World.empty |> World.setFlag "ENGINE_BUG_CONTEST_TIMER" |> BugContestTimer.start
    let afterOne = BugContestTimer.tick started

    Assert.Equal(20 * 60, BugContestTimer.DurationSeconds)
    Assert.Equal(60, BugContestTimer.FramesPerSecond)
    Assert.Equal(20 * 60 * 60, BugContestTimer.DurationFrames)
    Assert.Equal(BugContestTimer.DurationFrames - 1, World.getVar BugContestTimer.RemainingVar afterOne)
    Assert.True(World.hasFlag "ENGINE_BUG_CONTEST_TIMER" afterOne)
    Assert.Equal(0, World.getVar BugContestTimer.TimeUpVar afterOne)

[<Fact>]
let ``bug contest timer expiry leaves a script consumable timeout state`` () =
    let started =
        World.empty
        |> World.setFlag "ENGINE_BUG_CONTEST_TIMER"
        |> BugContestTimer.start
        |> World.setVar BugContestTimer.RemainingVar 1

    let expired = BugContestTimer.tick started

    Assert.Equal(0, World.getVar BugContestTimer.RemainingVar expired)
    Assert.Equal(1, World.getVar BugContestTimer.TimeUpVar expired)
    Assert.False(World.hasFlag "ENGINE_BUG_CONTEST_TIMER" expired)

[<Fact>]
let ``bug contest timer state survives save world round trip`` () =
    let world =
        World.empty
        |> World.setFlag "ENGINE_BUG_CONTEST_TIMER"
        |> BugContestTimer.start
        |> BugContestTimer.tick

    let overworld = OverworldState.loadByIdAt (Content()) "NewBarkTown" 5 5 Down
    let save = PokeGold.Game.Save.SaveData.captureWith overworld world PlayerStateOps.initial
    let restored = PokeGold.Game.Save.SaveData.worldOf save

    Assert.Equal(BugContestTimer.DurationFrames - 1, World.getVar BugContestTimer.RemainingVar restored)
    Assert.True(World.hasFlag "ENGINE_BUG_CONTEST_TIMER" restored)

[<Fact>]
let ``bug contest timer advances at an idle frame boundary`` () =
    let content = Content()
    let state = OverworldState.loadByIdAt content "NewBarkTown" 5 5 Down
    let scene = OverworldScene(content, SilentSound(), state)
    let world = World.empty |> World.setFlag "ENGINE_BUG_CONTEST_TIMER" |> BugContestTimer.start
    scene.Restore(world, PlayerStateOps.initial)

    scene.AdvanceBugContestTimer()

    Assert.Equal(BugContestTimer.DurationFrames - 1, World.getVar BugContestTimer.RemainingVar scene.DebugWorld)

[<Fact>]
let ``bug contest judging returns the placement prize mapping and no catch returns zero`` () =
    let judging score species =
        let scene = runContestJudging score species (FixedRandom(List.replicate 10 0))
        World.getVar "__bug_contest_placement" scene.DebugWorld

    Assert.Equal(1, judging 400 123)
    Assert.Equal(2, judging 340 123)
    Assert.Equal(3, judging 320 123)
    Assert.Equal(0, judging 0 0)

[<Fact>]
let ``bug contest capture tracks the caught species and score`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "ContestCatchScene"
            [| Loadwildmon("SCYTHER", 5); Startbattle; End |]

    let player =
        { PlayerStateOps.initial with
            Party = [ PartyMon.create (Species.byName "CYNDAQUIL").Dex 20 ]
            Bag = Bag.empty |> Bag.add "MASTER_BALL" 1 }
    let world = World.empty |> World.setFlag "ENGINE_BUG_CONTEST_TIMER"
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    while frame < 1000 && stack.Count > 1 do
        frame <- frame + 1
        applyTransition stack (stack.[stack.Count - 1].Update (driveBattlePackFirstItem frame stack.[stack.Count - 1]))

    Assert.Equal(1, stack.Count)
    (scene :> Scene).Update Buttons.none |> ignore
    let caught = scene.DebugPlayer.Party |> List.last
    Assert.Equal((Species.byName "SCYTHER").Dex, caught.SpeciesId)
    Assert.Equal(BugContestScore.calculate caught, World.getVar "__bug_contest_caught_score" scene.DebugWorld)

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
    Assert.False(scene.CanCapture)

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
    let trainer = Trainers.lookupByName "YOUNGSTER" "JOEY1" |> Option.get
    let finalEnemyLevel = trainer.Party |> List.last |> fun enemy -> enemy.Level
    let expectedPrize = Experience.moneyEarned trainer.BaseReward finalEnemyLevel |> Experience.applyAmuletCoin true
    Assert.Equal(1000 + expectedPrize, scene.DebugPlayer.Money)

    let settledMoney = scene.DebugPlayer.Money
    for _ in 1..5 do (scene :> Scene).Update Buttons.none |> ignore
    Assert.Equal(settledMoney, scene.DebugPlayer.Money)

[<Fact>]
let ``EPIC1 matrix legal wild and ordinary trainer victories synchronize persistent battle state`` () =
    let content = Content()
    let psychic = Moves.byName "PSYCHIC_M"

    let legalMewtwo () =
        let baseMon = PartyMon.create (Species.byName "MEWTWO").Dex 100
        Assert.Contains("PSYCHIC_M", MoveLearn.startingMoveNames "MEWTWO" 100)
        { baseMon with
            Hp = baseMon.MaxHp - 10
            Status = "PAR"
            HeldItem = Some "LEFTOVERS"
            Moves = [ moveId "PSYCHIC_M", psychic.Pp ] }

    let run label commands money =
        let hero = legalMewtwo ()
        let state = scriptedScene content "NewBarkTown" 5 5 Down label commands
        let scene = OverworldScene(content, SilentSound(), state)
        scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ hero ]; Money = money })
        let battle = driveStagedBattle scene (fun _ -> 0) 4000
        let synced = scene.DebugPlayer.Party |> List.find (fun mon -> mon.Id = hero.Id)
        let finalHero = battle.PlayerTeam |> List.find (fun mon -> mon.PersistentId = Some hero.Id)

        Assert.Equal(Some Win, battle.Outcome)
        Assert.Equal(hero.Id, synced.Id)
        Assert.Equal(finalHero.Hp, synced.Hp)
        Assert.Equal("PAR", synced.Status)
        Assert.Equal(Some "LEFTOVERS", synced.HeldItem)
        Assert.Equal<(int * int) list>([ moveId "PSYCHIC_M", finalHero.Pp.Head ], synced.Moves)
        Assert.True(synced.Exp > hero.Exp)
        Assert.True(synced.StatExp.Hp > hero.StatExp.Hp)
        scene, hero, battle

    let wildScene, wildHero, wildBattle =
        run "Epic1WildBattle" [| Loadwildmon("RATTATA", 5); Startbattle; End |] 777
    Assert.False(Battle.isTrainerBattle wildBattle)
    Assert.Equal(777, wildScene.DebugPlayer.Money)
    Assert.Equal(psychic.Pp - 1, (wildScene.DebugPlayer.Party |> List.find (fun mon -> mon.Id = wildHero.Id)).Moves.Head |> snd)

    let trainerScene, trainerHero, trainerBattle =
        run "Epic1OrdinaryTrainerBattle" [| Loadtrainer("YOUNGSTER", "JOEY1"); Startbattle; End |] 1000
    let trainer = Trainers.lookupByName "YOUNGSTER" "JOEY1" |> Option.defaultWith (fun () -> failwith "JOEY1 not found")
    let finalDefeat = trainerBattle.DefeatEvents |> List.last
    Assert.True(Battle.isTrainerBattle trainerBattle)
    Assert.Equal(1000 + Experience.moneyEarned trainer.BaseReward finalDefeat.DefeatedLevel, trainerScene.DebugPlayer.Money)
    Assert.Equal(psychic.Pp - 1, (trainerScene.DebugPlayer.Party |> List.find (fun mon -> mon.Id = trainerHero.Id)).Moves.Head |> snd)

[<Fact>]
let ``EPIC1 matrix legal Will runtime victory synchronizes all five generated defeats`` () =
    let content = Content()
    let will = Trainers.lookup "WILL" 1 |> Option.defaultWith (fun () -> failwith "WILL not found")
    let thunder = Moves.byName "THUNDER"
    let fireBlast = Moves.byName "FIRE_BLAST"
    let psychic = Moves.byName "PSYCHIC_M"
    let baseMon = PartyMon.create (Species.byName "MEWTWO").Dex 100
    Assert.True(TmHm.canLearnMove "THUNDER" baseMon)
    Assert.True(TmHm.canLearnMove "FIRE_BLAST" baseMon)
    Assert.Contains("PSYCHIC_M", MoveLearn.startingMoveNames "MEWTWO" 100)
    let hero =
        { baseMon with
            HeldItem = Some "LEFTOVERS"
            Moves = [ moveId "THUNDER", thunder.Pp; moveId "FIRE_BLAST", fireBlast.Pp; moveId "PSYCHIC_M", psychic.Pp ] }
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "Epic1WillBattle"
            [| Loadtrainer("WILL", "WILL1")
               Startbattle
               End |]
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ hero ]; Money = 1500 })

    let selectMove (battle: BattleScene) =
        battle.CurrentState.Player.Moves
        |> List.mapi (fun index move -> index, move)
        |> List.filter (fun (index, _) -> index < battle.CurrentState.Player.Pp.Length && battle.CurrentState.Player.Pp.[index] > 0)
        |> List.maxBy (fun (_, move) -> Damage.calc battle.CurrentState.Player battle.CurrentState.Enemy move false Damage.MaxRoll false)
        |> fst

    let battle = driveStagedBattle scene selectMove 15000
    let synced = scene.DebugPlayer.Party |> List.find (fun mon -> mon.Id = hero.Id)
    let finalHero = battle.PlayerTeam |> List.find (fun mon -> mon.PersistentId = Some hero.Id)
    let finalDefeat = battle.DefeatEvents |> List.last

    Assert.Equal(Some Win, battle.Outcome)
    Assert.Equal(will.Party.Length, battle.DefeatEvents.Length)
    Assert.Equal<string list>(
        will.Party |> List.map (fun mon -> mon.Species) |> List.sort,
        battle.DefeatEvents |> List.map (fun defeat -> defeat.DefeatedSpecies.Name) |> List.sort)
    Assert.True(battle.EnemyTeam |> List.forall BattleMon.isFainted)
    Assert.Equal(hero.Id, synced.Id)
    Assert.Equal(finalHero.Hp, synced.Hp)
    Assert.Equal(Some "LEFTOVERS", synced.HeldItem)
    Assert.Equal<int list>(finalHero.Pp, synced.Moves |> List.map snd)
    Assert.True(synced.Exp > hero.Exp)
    Assert.True(synced.StatExp.Hp > hero.StatExp.Hp)
    Assert.Equal(1500 + Experience.moneyEarned will.BaseReward finalDefeat.DefeatedLevel, scene.DebugPlayer.Money)

[<Fact>]
let ``EPIC1 matrix legal Red runtime victory synchronizes generated six member battle`` () =
    let content = Content()
    let red = Trainers.lookup "RED" 1 |> Option.defaultWith (fun () -> failwith "RED not found")
    let legalParty =
        [ "FERALIGATR"; "TYRANITAR"; "DRAGONITE"; "SNORLAX"; "ESPEON"; "HO_OH" ]
        |> List.map (fun species ->
            let mon = PartyMon.create (Species.byName species).Dex 100 |> MoveLearn.seedStartingMoves
            Assert.NotEmpty(mon.Moves)
            mon)
    let party =
        legalParty
        |> List.mapi (fun index mon -> if index = 3 then { mon with HeldItem = Some "LEFTOVERS" } else mon)
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "Epic1RedBattle"
            [| Loadtrainer("RED", "RED1")
               Startbattle
               End |]
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = party; Money = 2500 })

    let selectMove (battle: BattleScene) =
        battle.CurrentState.Player.Moves
        |> List.mapi (fun index move -> index, move)
        |> List.filter (fun (index, _) -> index < battle.CurrentState.Player.Pp.Length && battle.CurrentState.Player.Pp.[index] > 0)
        |> List.maxBy (fun (_, move) -> Damage.calc battle.CurrentState.Player battle.CurrentState.Enemy move false Damage.MaxRoll false)
        |> fst

    let battle = driveStagedBattle scene selectMove 20000

    Assert.Equal(Some Win, battle.Outcome)
    Assert.Equal(red.Party.Length, battle.DefeatEvents.Length)
    Assert.Equal<string list>(
        red.Party |> List.map (fun mon -> mon.Species) |> List.sort,
        battle.DefeatEvents |> List.map (fun defeat -> defeat.DefeatedSpecies.Name) |> List.sort)
    Assert.Equal<Set<System.Guid>>(party |> List.map _.Id |> Set.ofList, scene.DebugPlayer.Party |> List.map _.Id |> Set.ofList)
    for original in party do
        let synced = scene.DebugPlayer.Party |> List.find (fun mon -> mon.Id = original.Id)
        let finalMon = battle.PlayerTeam |> List.find (fun mon -> mon.PersistentId = Some original.Id)
        Assert.Equal(finalMon.Hp, synced.Hp)
        Assert.Equal(finalMon.HeldItem, synced.HeldItem)
        Assert.Equal<int list>(finalMon.Pp, synced.Moves |> List.map snd)
    Assert.True(scene.DebugPlayer.Party |> List.exists (fun mon -> mon.Exp > 0))
    Assert.True(scene.DebugPlayer.Party |> List.exists (fun mon -> mon.StatExp.Hp > 0))
    Assert.Equal(2500 + Experience.moneyEarned red.BaseReward (battle.DefeatEvents |> List.last).DefeatedLevel, scene.DebugPlayer.Money)

[<Fact>]
let ``BAT-001 runtime trainer parties match source moves and held items`` () =
    let expectedParties =
        [ "FALKNER",
          [ "PIDGEY", 7, None, [ "TACKLE"; "MUD_SLAP" ]
            "PIDGEOTTO", 9, None, [ "TACKLE"; "MUD_SLAP"; "GUST" ] ]
          "WHITNEY",
          [ "CLEFAIRY", 18, None, [ "DOUBLESLAP"; "MIMIC"; "ENCORE"; "METRONOME" ]
            "MILTANK", 20, None, [ "ROLLOUT"; "ATTRACT"; "STOMP"; "MILK_DRINK" ] ]
          "CHAMPION",
          [ "GYARADOS", 44, None, [ "FLAIL"; "RAIN_DANCE"; "SURF"; "HYPER_BEAM" ]
            "DRAGONITE", 47, None, [ "THUNDER_WAVE"; "TWISTER"; "THUNDER"; "HYPER_BEAM" ]
            "DRAGONITE", 47, None, [ "THUNDER_WAVE"; "TWISTER"; "BLIZZARD"; "HYPER_BEAM" ]
            "AERODACTYL", 46, None, [ "WING_ATTACK"; "ANCIENTPOWER"; "ROCK_SLIDE"; "HYPER_BEAM" ]
            "CHARIZARD", 46, None, [ "FLAMETHROWER"; "WING_ATTACK"; "SLASH"; "HYPER_BEAM" ]
            "DRAGONITE", 50, None, [ "FIRE_BLAST"; "SAFEGUARD"; "OUTRAGE"; "HYPER_BEAM" ] ]
          "RED",
          [ "PIKACHU", 81, None, [ "CHARM"; "QUICK_ATTACK"; "THUNDERBOLT"; "THUNDER" ]
            "ESPEON", 73, None, [ "MUD_SLAP"; "REFLECT"; "SWIFT"; "PSYCHIC_M" ]
            "SNORLAX", 75, None, [ "AMNESIA"; "SNORE"; "REST"; "BODY_SLAM" ]
            "VENUSAUR", 77, None, [ "SUNNY_DAY"; "GIGA_DRAIN"; "SYNTHESIS"; "SOLARBEAM" ]
            "CHARIZARD", 77, None, [ "FLAMETHROWER"; "WING_ATTACK"; "SLASH"; "FIRE_SPIN" ]
            "BLASTOISE", 77, None, [ "RAIN_DANCE"; "SURF"; "BLIZZARD"; "WHIRLPOOL" ] ] ]

    let content = Content()
    let tackle = Moves.byName "TACKLE"
    let playerMon =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 50 with
            Moves = [ moveId "TACKLE", tackle.Pp ] }

    for group, expected in expectedParties do
        let state =
            scriptedScene
                content
                "NewBarkTown"
                5
                5
                Down
                $"{group}BattleScene"
                [| Loadtrainer(group, "1"); Startbattle; End |]
        let scene = OverworldScene(content, SilentSound(), state)
        scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ playerMon ] })

        match (scene :> Scene).Update Buttons.none with
        | Push (:? BattleScene as battle) ->
            let trainer = Trainers.lookup group 1 |> Option.defaultWith (fun () -> failwith $"{group} not found")
            let actual =
                battle.CurrentState.EnemyTeam
                |> List.map (fun mon ->
                    mon.Species.Name,
                    mon.Level,
                    mon.HeldItem,
                    (mon.Moves |> List.map (fun move -> move.Name)))

            Assert.Equal<(string * int * string option * string list) list>(expected, actual)
            Assert.Equal<string list>(trainer.AiItems, battle.CurrentState.EnemyAiItems)
        | transition -> failwithf "expected %s battle scene, got %A" group transition

[<Fact>]
let ``BAT-001 runtime item trainer preserves held item`` () =
    let content = Content()
    let tackle = Moves.byName "TACKLE"
    let playerMon =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 50 with
            Moves = [ moveId "TACKLE", tackle.Pp ] }
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "ItemTrainerBattleScene"
            [| Loadtrainer("POKEFANM", "1"); Startbattle; End |]
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ playerMon ] })

    match (scene :> Scene).Update Buttons.none with
    | Push (:? BattleScene as battle) ->
        let raichu = Assert.Single(battle.CurrentState.EnemyTeam)
        Assert.Equal("RAICHU", raichu.Species.Name)
        Assert.Equal(14, raichu.Level)
        Assert.Equal(Some "BERRY", raichu.HeldItem)
    | transition -> failwithf "expected item trainer battle scene, got %A" transition

[<Fact>]
let ``BAT-002 runtime derives normal and item moves while preserving explicit moves`` () =
    let cases =
        [ "BUG_CATCHER",
          [ [ "TACKLE"; "STRING_SHOT" ]
            [ "TACKLE"; "STRING_SHOT" ] ]
          "FALKNER",
          [ [ "TACKLE"; "MUD_SLAP" ]
            [ "TACKLE"; "MUD_SLAP"; "GUST" ] ]
          "POKEFANM",
          [ [ "THUNDERSHOCK"; "TAIL_WHIP"; "QUICK_ATTACK"; "THUNDERBOLT" ] ] ]

    let content = Content()
    let tackle = Moves.byName "TACKLE"
    let playerMon =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 50 with
            Moves = [ moveId "TACKLE", tackle.Pp ] }

    for group, expectedMoves in cases do
        let state =
            scriptedScene
                content
                "NewBarkTown"
                5
                5
                Down
                $"{group}MovesBattleScene"
                [| Loadtrainer(group, "1"); Startbattle; End |]
        let scene = OverworldScene(content, SilentSound(), state)
        scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ playerMon ] })

        match (scene :> Scene).Update Buttons.none with
        | Push (:? BattleScene as battle) ->
            let actualMoves =
                battle.CurrentState.EnemyTeam
                |> List.map (fun mon -> mon.Moves |> List.map (fun move -> move.Name))

            Assert.Equal<string list list>(expectedMoves, actualMoves)
        | transition -> failwithf "expected %s battle scene, got %A" group transition

[<Fact>]
let ``BAT-003 Route 2 encounter constructs a complete source wild opponent`` () =
    let content = Content()
    let route = OverworldState.loadById content "Route2"
    let occupied x y = route.Npcs |> Array.exists (fun npc -> npc.CellX = x && npc.CellY = y)

    let startX, startY, direction =
        [ for y in 0 .. route.Map.Height * 2 - 1 do
              for x in 0 .. route.Map.Width * 2 - 1 do
                  let collision = Movement.collisionIdAtCell route.Map route.Collision x y

                  if WildEncounter.isEncounterTile collision && not (occupied x y) then
                      for direction in [ Down; Up; Left; Right ] do
                          let dx, dy = delta direction
                          let sx, sy = x - dx, y - dy

                          if sx >= 0
                             && sy >= 0
                             && sx < route.Map.Width * 2
                             && sy < route.Map.Height * 2
                             && Movement.cellWalkable route.Map route.Collision sx sy
                             && not (WildEncounter.isEncounterTile (Movement.collisionIdAtCell route.Map route.Collision sx sy))
                             && not (occupied sx sy) then
                              yield sx, sy, direction ]
        |> List.tryHead
        |> Option.defaultWith (fun () -> failwith "expected Route 2 grass with an open adjacent land cell")

    let state = OverworldState.loadByIdAt content "Route2" startX startY direction
    let rng = FixedRandom([ 0; 255; 99; 192; 19; 0xAB; 0xCD ])
    let scene = OverworldScene(content, SilentSound(), state, encounterRandom = rng)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ MoveLearn.seedStartingMoves (PartyMon.create 155 20) ] })

    let mutable transition = Stay
    let mutable frame = 0
    let isPush = function Push _ -> true | _ -> false

    while frame < 32 && not (isPush transition) do
        let buttons = if frame = 0 then directionButton direction else Buttons.none
        transition <- (scene :> Scene).Update buttons
        frame <- frame + 1

    match transition with
    | Push (:? BattleScene as battle) ->
        let pikachu = Assert.Single(battle.CurrentState.EnemyTeam)
        Assert.Equal("PIKACHU", pikachu.Species.Name)
        Assert.Equal(4, pikachu.Level)
        Assert.Equal<string list>([ "THUNDERSHOCK"; "GROWL" ], pikachu.Moves |> List.map (fun move -> move.Name))
        Assert.Equal<int list>(pikachu.Moves |> List.map (fun move -> move.Pp), pikachu.Pp)
        Assert.Equal(Some "BERRY", pikachu.HeldItem)
        Assert.Equal(0xABCD, pikachu.Dvs)
        Assert.Equal(Male, pikachu.Gender)
        Assert.Equal(Healthy, pikachu.Status)
        Assert.Equal(pikachu.MaxHp, pikachu.Hp)
    | other -> failwithf "expected Route 2 wild battle, got %A after %d frames" other frame

[<Fact>]
let ``BAT-004 production battles reject missing or invalid staged combatants`` () =
    let content = Content()
    let validMon = MoveLearn.seedStartingMoves (PartyMon.create (Species.byName "CYNDAQUIL").Dex 20)
    let validPlayer = { PlayerStateOps.initial with Party = [ validMon ] }

    let expectFailure label commands player expectedMessage =
        let state = scriptedScene content "NewBarkTown" 5 5 Down label commands
        let scene = OverworldScene(content, SilentSound(), state)

        let error =
            Assert.Throws<System.InvalidOperationException>(fun () ->
                scene.Restore(World.empty, player)
                (scene :> Scene).Update Buttons.none |> ignore)

        Assert.Contains(expectedMessage, error.Message)

    expectFailure "NoOpponentBattle" [| Startbattle; End |] validPlayer "no staged opponent"
    expectFailure "UnknownTrainerBattle" [| Loadtrainer("NOT_A_TRAINER", "1"); Startbattle; End |] validPlayer "Unknown trainer"
    expectFailure "UnknownWildBattle" [| Loadwildmon("MISSINGNO", 5); Startbattle; End |] validPlayer "Unknown wild species"
    expectFailure "EmptyPartyBattle" [| Loadwildmon("PIDGEY", 3); Startbattle; End |] PlayerStateOps.initial "no usable player Pokemon"
    expectFailure
        "FaintedPartyBattle"
        [| Loadwildmon("PIDGEY", 3); Startbattle; End |]
        { PlayerStateOps.initial with Party = [ { validMon with Hp = 0 } ] }
        "no usable player Pokemon"

[<Fact>]
let ``BAT-005 duplicate party members keep individual battle state`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "IdentityBattle"
            [| Loadwildmon("MAGIKARP", 2); Startbattle; End |]

    let stats = Species.byName "CYNDAQUIL"
    let first =
        { PartyMon.create stats.Dex 50 with
            Nickname = "FIRST"
            Exp = 100
            Moves = [ moveId "TACKLE", 5 ]
            HeldItem = Some "BERRY" }
    let second =
        { PartyMon.create stats.Dex 50 with
            Nickname = "SECOND"
            Exp = 200
            Hp = first.MaxHp - 7
            Status = "PAR"
            Moves = [ moveId "EMBER", 7 ]
            HeldItem = Some "ANTIDOTE" }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ first; second ] })

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    while frame < 1000 && stack.Count > 1 do
        frame <- frame + 1
        let buttons = if frame % 2 = 0 then { Buttons.none with A = true } else Buttons.none
        applyTransition stack (stack.[stack.Count - 1].Update buttons)

    Assert.Equal(1, stack.Count)
    (scene :> Scene).Update Buttons.none |> ignore

    let actualFirst, actualSecond =
        match scene.DebugPlayer.Party with
        | [ a; b ] -> a, b
        | party -> failwithf "expected two party members, got %d" party.Length

    Assert.Equal("FIRST", actualFirst.Nickname)
    Assert.Equal(first.Id, actualFirst.Id)
    Assert.Equal(first.Hp, actualFirst.Hp)
    Assert.Equal("", actualFirst.Status)
    Assert.Equal(Some "BERRY", actualFirst.HeldItem)
    Assert.Equal<(int * int) list>([ moveId "TACKLE", 4 ], actualFirst.Moves)
    Assert.True(actualFirst.Exp > first.Exp, "the participating lead should receive EXP")

    Assert.Equal("SECOND", actualSecond.Nickname)
    Assert.Equal(second.Id, actualSecond.Id)
    Assert.NotEqual(actualFirst.Id, actualSecond.Id)
    Assert.Equal(second.Hp, actualSecond.Hp)
    Assert.Equal("PAR", actualSecond.Status)
    Assert.Equal(Some "ANTIDOTE", actualSecond.HeldItem)
    Assert.Equal<(int * int) list>([ moveId "EMBER", 7 ], actualSecond.Moves)
    Assert.Equal(second.Exp, actualSecond.Exp)

[<Fact>]
let ``BAT-007 switching and fainting identical members round-trips by identity`` () =
    let content = Content()
    let state =
        scriptedScene content "NewBarkTown" 5 5 Down "RoundTripBattle"
            [| Loadwildmon("TEDDIURSA", 5); Startbattle; End |]
    let stats = Species.byName "CYNDAQUIL"
    let firstStatExp = { Hp = 16; Attack = 25; Defense = 36; Speed = 49; Special = 64 }
    let secondStatExp = { Hp = 81; Attack = 100; Defense = 121; Speed = 144; Special = 169 }
    let firstBase =
        { PartyMon.createWithDvs stats.Dex 20 0x1234 with StatExp = firstStatExp }
        |> PartyMon.withLevel 20
    let secondBase =
        { PartyMon.createWithDvs stats.Dex 20 0xABCD with StatExp = secondStatExp }
        |> PartyMon.withLevel 20
    let first =
        { firstBase with
            Nickname = "FAINTS"
            Hp = 1
            Status = "PSN"
            Moves = [ moveId "SPLASH", 5 ]
            HeldItem = Some "ANTIDOTE"
            Exp = 111
            Friendship = 80 }
    let second =
        { secondBase with
            Nickname = "FINISHES"
            Moves = [ moveId "DRAGON_RAGE", 10 ]
            HeldItem = Some "AWAKENING"
            Exp = 222
            Friendship = 120 }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ first; second ] })
    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    let mutable finalBattle: BattleState option = None
    while frame < 1500 && stack.Count > 1 do
        frame <- frame + 1
        let battle = stack.[stack.Count - 1] :?> BattleScene
        if battle.CurrentState.Outcome.IsSome then finalBattle <- Some battle.CurrentState
        let snap = battle.RuntimeSnapshot
        let buttons =
            if frame % 2 <> 0 then Buttons.none
            elif snap.MessageActive || not snap.PendingMessages.IsEmpty then { Buttons.none with A = true }
            elif snap.Mode = "ForcedSwitch" && battle.PartyCursor = 0 then { Buttons.none with Down = true }
            elif snap.Mode = "ForcedSwitch" then { Buttons.none with A = true }
            elif snap.Mode = "CommandMenu" then { Buttons.none with A = true }
            elif snap.Mode = "MoveMenu" then { Buttons.none with A = true }
            else Buttons.none
        applyTransition stack ((battle :> Scene).Update buttons)

    Assert.Equal(1, stack.Count)
    (scene :> Scene).Update Buttons.none |> ignore
    let battle = finalBattle |> Option.defaultWith (fun () -> failwith "battle never resolved")
    let finalSecond = battle.PlayerTeam |> List.find (fun mon -> mon.PersistentId = Some second.Id)
    let actualFirst, actualSecond = scene.DebugPlayer.Party.[0], scene.DebugPlayer.Party.[1]
    let progressedFirst, progressedSecond =
        let progressed = BattleProgression.applyEvents battle.DefeatEvents [ first; second ]
        progressed.[0], progressed.[1]

    Assert.Equal(first.Id, actualFirst.Id)
    Assert.Equal(0, actualFirst.Hp)
    Assert.Equal("", actualFirst.Status)
    Assert.Equal<(int * int) list>([ moveId "SPLASH", 4 ], actualFirst.Moves)
    Assert.Equal(first.HeldItem, actualFirst.HeldItem)
    Assert.Equal(progressedFirst.Exp, actualFirst.Exp)
    Assert.Equal(first.Dvs, actualFirst.Dvs)
    Assert.Equal(progressedFirst.StatExp, actualFirst.StatExp)
    Assert.Equal(first.Friendship, actualFirst.Friendship)

    Assert.Equal(second.Id, actualSecond.Id)
    Assert.Equal(finalSecond.Hp, actualSecond.Hp)
    Assert.Equal<(int * int) list>([ moveId "DRAGON_RAGE", 9 ], actualSecond.Moves)
    Assert.Equal(second.HeldItem, actualSecond.HeldItem)
    Assert.Equal(progressedSecond.Exp, actualSecond.Exp)
    Assert.Equal(second.Dvs, actualSecond.Dvs)
    Assert.Equal(progressedSecond.StatExp, actualSecond.StatExp)
    Assert.Equal(second.Friendship, actualSecond.Friendship)

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
               Ifequal(0, "WildCatchScene.Caught")
               Writetext "WrongCatchResult"
               End
               Writetext "CatchDone"
               End |]
        |> fun s ->
            { s with
                Script =
                    { s.Script with
                        Labels = Map.ofList [ "WildCatchScene", 0; "WildCatchScene.Caught", 5 ] }
                Text =
                    Map.ofList
                        [ "CatchDone", "done<DONE>"
                          "WrongCatchResult", "wrong catch result<DONE>" ] }

    let ember = Moves.byName "EMBER"
    let mon =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 20 with
            Moves = [ moveId "EMBER", ember.Pp ] }
    let player =
        { PlayerStateOps.initial with
            Money = 777
            Party = [ mon ]
            Bag = Bag.add "MASTER_BALL" 1 Bag.empty }
    let scene = OverworldScene(content, SilentSound(), state, encounterRandom = FixedRandom([ 191; 0xAB; 0xCD ]))
    scene.Restore(World.empty, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    while frame < 1000 && stack.Count > 1 do
        frame <- frame + 1
        let buttons = driveBattlePackFirstItem frame stack.[stack.Count - 1]

        applyTransition stack (stack.[stack.Count - 1].Update buttons)

    Assert.Equal(1, stack.Count)

    match (scene :> Scene).Update Buttons.none with
    | Push (:? TextBoxScene) -> ()
    | other -> failwithf "expected resumed catch script text, got %A" other

    let rattataDex = (Species.byName "RATTATA").Dex
    Assert.Equal(0, Bag.count "MASTER_BALL" scene.DebugPlayer.Bag)
    Assert.Equal(2, scene.DebugPlayer.Party.Length)
    Assert.Equal(0xABCD, (List.last scene.DebugPlayer.Party).Dvs)
    Assert.Contains(rattataDex, scene.DebugPlayer.DexOwn)
    Assert.Contains(rattataDex, scene.DebugPlayer.DexSeen)
    Assert.Equal(777, scene.DebugPlayer.Money)
    Assert.Equal(Some "CatchDone", scene.RuntimeSnapshot.LastTextLabel)

[<Fact>]
let ``SCR-001 wild RUN resumes startbattle with source DRAW result`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "WildEscape"
            [| Loadwildmon("RATTATA", 5)
               Startbattle
               Ifequal(2, "WildEscape.Draw")
               Writetext "WrongResult"
               End
               Writetext "DrawResult"
               End |]
        |> fun s ->
            { s with
                Script =
                    { s.Script with
                        Labels = Map.ofList [ "WildEscape", 0; "WildEscape.Draw", 5 ] }
                Text =
                    Map.ofList
                        [ "WrongResult", "wrong result<DONE>"
                          "DrawResult", "draw result<DONE>" ] }
    let runner = { PartyMon.create 155 20 with Moves = MoveLearn.tryLearnMove "TACKLE" [] }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ runner ] })

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    while frame < 1000 && scene.RuntimeSnapshot.LastTextLabel.IsNone do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]
        let buttons =
            match top with
            | :? BattleScene as battle when frame % 2 = 0 ->
                let snapshot = battle.RuntimeSnapshot

                if snapshot.MessageActive || not snapshot.PendingMessages.IsEmpty then
                    { Buttons.none with A = true }
                elif snapshot.Mode = "CommandMenu" && battle.CommandCursor = 0 then
                    { Buttons.none with Down = true }
                elif snapshot.Mode = "CommandMenu" && battle.CommandCursor = 2 then
                    { Buttons.none with Right = true }
                elif snapshot.Mode = "CommandMenu" && battle.CommandCursor = 3 then
                    { Buttons.none with A = true }
                else
                    Buttons.none
            | :? TextBoxScene when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        applyTransition stack (top.Update buttons)

    Assert.Equal(Some "DrawResult", scene.RuntimeSnapshot.LastTextLabel)

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
        let buttons = driveBattlePackFirstItem frame stack.[stack.Count - 1]

        applyTransition stack (stack.[stack.Count - 1].Update buttons)

    Assert.Equal(6, scene.DebugPlayer.Party.Length)
    Assert.Single(scene.DebugPlayer.Pc.Boxes.[scene.DebugPlayer.Pc.CurrentBox].Mons)

[<Theory>]
[<InlineData("CHERRYGROVE_CITY", "CherrygroveCity", 29, 4)>]
[<InlineData("NOT_A_SPAWN", "PlayersHouse2F", 3, 3)>]
let ``BAT-017 defeat applies source blackout spawn and aborts continuation``
    (blackoutMap: string, expectedMap: string, expectedX: int, expectedY: int) =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "WhiteoutScene"
            [| Blackoutmod blackoutMap
               Loadwildmon("MEWTWO", 100)
               Startbattle
               Writetext "AfterLoss"
               End |]
        |> fun s -> { s with Text = Map.ofList [ "AfterLoss", "after<DONE>" ] }

    let splash = Moves.byName "SPLASH"
    let faintable =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 2 with
            Hp = 1
            Status = "PSN"
            Moves = [ moveId "SPLASH", splash.Pp ] }
    let player =
        { PlayerStateOps.initial with
            Money = 2001
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

    Assert.Equal(Stay, (scene :> Scene).Update Buttons.none)
    Assert.Equal(expectedMap, scene.RuntimeSnapshot.MapId)
    Assert.Equal((expectedX, expectedY), (scene.RuntimeSnapshot.Player.CellX, scene.RuntimeSnapshot.Player.CellY))
    Assert.Equal(1000, scene.DebugPlayer.Money)
    Assert.Equal(scene.DebugPlayer.Party.[0].MaxHp, scene.DebugPlayer.Party.[0].Hp)
    Assert.Equal("", scene.DebugPlayer.Party.[0].Status)
    Assert.Equal(splash.Pp, snd scene.DebugPlayer.Party.[0].Moves.[0])
    Assert.NotEqual(Some "AfterLoss", scene.RuntimeSnapshot.LastTextLabel)
    for _ in 1..5 do Assert.Equal(Stay, (scene :> Scene).Update Buttons.none)

[<Fact>]
let ``BAT-021 real Falkner battle requires replacement before repeated trainer cycles`` () =
    let content = Content()
    let tackle = Moves.byName "TACKLE"
    let psychic = Moves.byName "PSYCHIC_M"
    let lead =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 5 with
            Hp = 1
            Moves = [ moveId "TACKLE", tackle.Pp ] }
    let finisher =
        { PartyMon.create (Species.byName "MEWTWO").Dex 100 with
            Moves = [ moveId "PSYCHIC_M", psychic.Pp ] }
    let scene =
        OverworldScene(
            content,
            SilentSound(),
            OverworldState.loadByIdAt content "VioletGym" 5 2 Up)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ lead; finisher ] })

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update { Buttons.none with A = true })

    let mutable frame = 0
    let mutable forcedSelections = 0
    let mutable enemySequence: string list = []
    let mutable finalBattle: BattleState option = None

    let completed () =
        stack.Count = 1
        && scene.CanCapture
        && World.hasEvent "EVENT_BEAT_FALKNER" scene.DebugWorld
        && World.hasEvent "EVENT_GOT_TM31_MUD_SLAP" scene.DebugWorld

    while frame < 6000 && not (completed ()) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top with
            | :? BattleScene as battle ->
                let snap = battle.RuntimeSnapshot
                if not (List.contains snap.EnemySpecies enemySequence) then
                    enemySequence <- enemySequence @ [ snap.EnemySpecies ]

                if battle.CurrentState.Outcome.IsSome then
                    finalBattle <- Some battle.CurrentState

                if frame % 2 <> 0 then
                    Buttons.none
                elif snap.MessageActive || not snap.PendingMessages.IsEmpty then
                    { Buttons.none with A = true }
                elif snap.Mode = "ForcedSwitch" && battle.PartyCursor = 0 then
                    { Buttons.none with Down = true }
                elif snap.Mode = "ForcedSwitch" then
                    forcedSelections <- forcedSelections + 1
                    { Buttons.none with A = true }
                elif snap.Mode = "CommandMenu" || snap.Mode = "MoveMenu" then
                    { Buttons.none with A = true }
                else
                    Buttons.none
            | :? TextBoxScene when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        applyTransition stack (top.Update buttons)

    Assert.True(completed (), "Falkner's real map script should settle after both source party members faint")
    Assert.Equal(1, forcedSelections)
    Assert.Equal<string list>([ "PIDGEY"; "PIDGEOTTO" ], enemySequence)

    let battle = finalBattle |> Option.defaultWith (fun () -> failwith "Falkner battle never resolved")
    let finalFinisher = battle.PlayerTeam |> List.find (fun mon -> mon.PersistentId = Some finisher.Id)
    let actualLead, actualFinisher = scene.DebugPlayer.Party.[0], scene.DebugPlayer.Party.[1]

    Assert.Equal(lead.Id, actualLead.Id)
    Assert.Equal(0, actualLead.Hp)
    Assert.Equal(finisher.Id, actualFinisher.Id)
    Assert.Equal(finalFinisher.Hp, actualFinisher.Hp)
    Assert.Equal<(int * int) list>([ moveId "PSYCHIC_M", psychic.Pp - 2 ], actualFinisher.Moves)
    Assert.True(World.hasFlag "ENGINE_ZEPHYRBADGE" scene.DebugWorld)

[<Fact>]
let ``BAT-022 Ether battle use persists PP and bag state after runtime victory`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "EtherBattle"
            [| Loadwildmon("RATTATA", 5)
               Startbattle
               End |]
    let tackle = Moves.byName "TACKLE"
    let dragonRage = Moves.byName "DRAGON_RAGE"
    let playerMon =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 20 with
            Moves = [ moveId "TACKLE", 0; moveId "DRAGON_RAGE", dragonRage.Pp ] }
    let player =
        { PlayerStateOps.initial with
            Party = [ playerMon ]
            Bag = Bag.empty |> Bag.add "ETHER" 1 }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    let mutable finalBattle: BattleState option = None

    while frame < 2000 && stack.Count > 1 do
        frame <- frame + 1
        let battle = stack.[stack.Count - 1] :?> BattleScene
        if battle.CurrentState.Outcome.IsSome then finalBattle <- Some battle.CurrentState
        let snap = battle.RuntimeSnapshot

        let buttons =
            if frame % 2 <> 0 then
                Buttons.none
            elif snap.MessageActive || not snap.PendingMessages.IsEmpty then
                { Buttons.none with A = true }
            elif snap.Mode = "CommandMenu" then
                if Bag.count "ETHER" battle.CurrentBag > 0 then
                    if battle.CommandCursor = 2 then { Buttons.none with A = true }
                    else { Buttons.none with Down = true }
                elif battle.CommandCursor = 0 then
                    { Buttons.none with A = true }
                else
                    { Buttons.none with Down = true }
            elif snap.Mode = "PackMenu" || snap.Mode = "TargetMenu" || snap.Mode = "MoveTargetMenu" then
                { Buttons.none with A = true }
            elif snap.Mode = "MoveMenu" then
                if battle.MoveCursor = 0 then { Buttons.none with Down = true }
                else { Buttons.none with A = true }
            else
                Buttons.none

        applyTransition stack ((battle :> Scene).Update buttons)

    Assert.Equal(1, stack.Count)
    (scene :> Scene).Update Buttons.none |> ignore
    let battle = finalBattle |> Option.defaultWith (fun () -> failwith "Ether battle never resolved")
    Assert.Equal(Some Win, battle.Outcome)
    Assert.Equal(0, Bag.count "ETHER" scene.DebugPlayer.Bag)
    Assert.Equal<(int * int) list>([ moveId "TACKLE", 10; moveId "DRAGON_RAGE", dragonRage.Pp - 1 ], scene.DebugPlayer.Party.[0].Moves)

[<Fact>]
let ``BAT-022 runtime Revive targets fainted bench consumes a turn and synchronizes identity`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "RuntimeReviveBattle"
            [| Loadwildmon("MAGIKARP", 2)
               Startbattle
               End |]
    let ember = Moves.byName "EMBER"
    let splash = Moves.byName "SPLASH"
    let active =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 20 with
            Moves = [ moveId "EMBER", ember.Pp ] }
    let faintedBase = PartyMon.create (Species.byName "CHIKORITA").Dex 18
    let fainted =
        { faintedBase with
            Hp = 0
            Status = "PSN"
            Moves = [ moveId "TACKLE", (Moves.byName "TACKLE").Pp ] }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ active; fainted ]; Bag = Bag.add "REVIVE" 1 Bag.empty })

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let battleAtStart = stack.[stack.Count - 1] :?> BattleScene
    Assert.Equal(2, battleAtStart.CurrentState.PlayerTeam.Length)
    Assert.Equal(Some fainted.Id, battleAtStart.CurrentState.PlayerTeam.[1].PersistentId)
    Assert.True(BattleMon.isFainted battleAtStart.CurrentState.PlayerTeam.[1])

    let mutable frame = 0
    let mutable sawBenchTarget = false
    let mutable enemyActedForRevive = false
    let mutable finalBattle: BattleState option = None

    while frame < 3000 && stack.Count > 1 do
        frame <- frame + 1
        let battle = stack.[stack.Count - 1] :?> BattleScene
        let snapshot = battle.RuntimeSnapshot

        if battle.CurrentState.Outcome.IsSome then
            finalBattle <- Some battle.CurrentState

        if snapshot.Mode = "TargetMenu" && battle.PartyCursor = 1 then
            sawBenchTarget <- true

        if Bag.count "REVIVE" battle.CurrentBag = 0
           && battle.CurrentState.Enemy.Moves.Head.Name = "SPLASH"
           && battle.CurrentState.Enemy.Pp.Head = splash.Pp - 1 then
            enemyActedForRevive <- true

        let buttons =
            if frame % 2 <> 0 then
                Buttons.none
            elif snapshot.MessageActive || not snapshot.PendingMessages.IsEmpty then
                { Buttons.none with A = true }
            elif snapshot.Mode = "CommandMenu" && Bag.count "REVIVE" battle.CurrentBag > 0 then
                if battle.CommandCursor = 2 then { Buttons.none with A = true }
                else { Buttons.none with Down = true }
            elif snapshot.Mode = "CommandMenu" then
                if battle.CommandCursor = 0 then { Buttons.none with A = true }
                else { Buttons.none with Down = true }
            elif snapshot.Mode = "PackMenu" then
                { Buttons.none with A = true }
            elif snapshot.Mode = "TargetMenu" then
                if battle.PartyCursor = 0 then { Buttons.none with Down = true }
                else { Buttons.none with A = true }
            elif snapshot.Mode = "MoveMenu" then
                { Buttons.none with A = true }
            else
                Buttons.none

        applyTransition stack ((battle :> Scene).Update buttons)

    Assert.Equal(1, stack.Count)
    (scene :> Scene).Update Buttons.none |> ignore
    Assert.True(sawBenchTarget, "The real PACK target menu must expose the fainted bench member")
    Assert.True(enemyActedForRevive, "A successful Revive must consume the player's battle turn")

    let battle = finalBattle |> Option.defaultWith (fun () -> failwith "runtime Revive battle never resolved")
    Assert.Equal(Some Win, battle.Outcome)
    let revived = scene.DebugPlayer.Party |> List.find (fun mon -> mon.Id = fainted.Id)
    Assert.Equal(fainted.Id, revived.Id)
    Assert.Equal(max 1 (fainted.MaxHp / 2), revived.Hp)
    Assert.Equal("", revived.Status)
    Assert.Equal(0, Bag.count "REVIVE" scene.DebugPlayer.Bag)
    Assert.Equal<(int * int) list>([ moveId "EMBER", ember.Pp - 1 ], scene.DebugPlayer.Party.[0].Moves)

[<Fact>]
let ``BAT-022 runtime Max Revive targets a fainted bench at full HP`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "RuntimeMaxReviveBattle"
            [| Loadwildmon("MAGIKARP", 2)
               Startbattle
               End |]
    let splash = Moves.byName "SPLASH"
    let active =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 20 with
            Moves = [ moveId "EMBER", (Moves.byName "EMBER").Pp ] }
    let faintedBase = PartyMon.create (Species.byName "CHIKORITA").Dex 18
    let fainted =
        { faintedBase with
            Hp = 0
            Status = "BRN"
            Moves = [ moveId "TACKLE", (Moves.byName "TACKLE").Pp ] }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ active; fainted ]; Bag = Bag.add "MAX_REVIVE" 1 Bag.empty })

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    let mutable sawBenchTarget = false
    while frame < 2000 && Bag.count "MAX_REVIVE" ((stack.[stack.Count - 1] :?> BattleScene).CurrentBag) > 0 do
        frame <- frame + 1
        let battle = stack.[stack.Count - 1] :?> BattleScene
        let snapshot = battle.RuntimeSnapshot

        if snapshot.Mode = "TargetMenu" && battle.PartyCursor = 1 then
            sawBenchTarget <- true

        let buttons =
            if frame % 2 <> 0 then
                Buttons.none
            elif snapshot.MessageActive || not snapshot.PendingMessages.IsEmpty then
                { Buttons.none with A = true }
            elif snapshot.Mode = "CommandMenu" then
                if battle.CommandCursor = 2 then { Buttons.none with A = true }
                else { Buttons.none with Down = true }
            elif snapshot.Mode = "PackMenu" then
                { Buttons.none with A = true }
            elif snapshot.Mode = "TargetMenu" then
                if battle.PartyCursor = 0 then { Buttons.none with Down = true }
                else { Buttons.none with A = true }
            else
                Buttons.none

        applyTransition stack ((battle :> Scene).Update buttons)

    let battle = stack.[stack.Count - 1] :?> BattleScene
    let revived = battle.CurrentState.PlayerTeam.[1]
    Assert.True(sawBenchTarget)
    Assert.Equal(fainted.MaxHp, revived.Hp)
    Assert.Equal(Healthy, revived.Status)
    Assert.Equal(Some fainted.Id, revived.PersistentId)
    Assert.Equal(0, Bag.count "MAX_REVIVE" battle.CurrentBag)
    Assert.Equal(splash.Pp - 1, battle.CurrentState.Enemy.Pp.Head)

[<Fact>]
let ``BAT-022 runtime Pack status and direct items consume a turn and persist`` () =
    let run item status expectedStage =
        let content = Content()
        let state =
            scriptedScene
                content
                "NewBarkTown"
                5
                5
                Down
                $"Runtime{item}Battle"
                [| Loadwildmon("MAGIKARP", 2)
                   Startbattle
                   End |]
        let splash = Moves.byName "SPLASH"
        let ember = Moves.byName "EMBER"
        let playerMon =
            { PartyMon.create (Species.byName "CYNDAQUIL").Dex 20 with
                Status = status
                Moves = [ moveId "EMBER", ember.Pp ] }
        let scene = OverworldScene(content, SilentSound(), state)
        scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ playerMon ]; Bag = Bag.add item 1 Bag.empty })

        let stack = ResizeArray<Scene>()
        stack.Add(scene :> Scene)
        applyTransition stack ((scene :> Scene).Update Buttons.none)

        let mutable frame = 0
        while frame < 2000 && Bag.count item ((stack.[stack.Count - 1] :?> BattleScene).CurrentBag) > 0 do
            frame <- frame + 1
            let battle = stack.[stack.Count - 1] :?> BattleScene
            let snapshot = battle.RuntimeSnapshot
            let buttons =
                if frame % 2 <> 0 then
                    Buttons.none
                elif snapshot.MessageActive || not snapshot.PendingMessages.IsEmpty then
                    { Buttons.none with A = true }
                elif snapshot.Mode = "CommandMenu" then
                    if battle.CommandCursor = 2 then { Buttons.none with A = true }
                    else { Buttons.none with Down = true }
                elif snapshot.Mode = "PackMenu" || snapshot.Mode = "TargetMenu" then
                    { Buttons.none with A = true }
                else
                    Buttons.none

            applyTransition stack ((battle :> Scene).Update buttons)

        let battle = stack.[stack.Count - 1] :?> BattleScene
        Assert.Equal(0, Bag.count item battle.CurrentBag)
        Assert.Equal(Healthy, battle.CurrentState.Player.Status)
        Assert.Equal(expectedStage, battle.CurrentState.Player.AtkStage)
        Assert.Equal(splash.Pp - 1, battle.CurrentState.Enemy.Pp.Head)

    run "PSNCUREBERRY" "PSN" 0
    run "X_ATTACK" "" 1

[<Fact>]
let ``BAT-022 runtime trainer battle rejects Poke Doll without consuming a turn`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "TrainerDollBattle"
            [| Loadtrainer("YOUNGSTER", "JOEY1")
               Startbattle
               End |]
    let ember = Moves.byName "EMBER"
    let playerMon =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 20 with
            Moves = [ moveId "EMBER", ember.Pp ] }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ playerMon ]; Bag = Bag.add "POKE_DOLL" 1 Bag.empty })

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    let mutable attempted = false
    while frame < 2000 && not attempted do
        frame <- frame + 1
        let battle = stack.[stack.Count - 1] :?> BattleScene
        let snapshot = battle.RuntimeSnapshot
        let buttons =
            if frame % 2 <> 0 then
                Buttons.none
            elif snapshot.MessageActive || not snapshot.PendingMessages.IsEmpty then
                { Buttons.none with A = true }
            elif snapshot.Mode = "CommandMenu" then
                if battle.CommandCursor = 2 then { Buttons.none with A = true }
                else { Buttons.none with Down = true }
            elif snapshot.Mode = "PackMenu" then
                attempted <- true
                { Buttons.none with A = true }
            else
                Buttons.none

        applyTransition stack ((battle :> Scene).Update buttons)

    let battle = stack.[stack.Count - 1] :?> BattleScene
    Assert.True(attempted)
    Assert.Equal(1, Bag.count "POKE_DOLL" battle.CurrentBag)
    Assert.Equal((Moves.byName "TACKLE").Pp, battle.CurrentState.Enemy.Pp.Head)
    Assert.True(battle.CurrentState.Outcome.IsNone)

[<Fact>]
let ``BAT-023 Berry consumption persists through runtime switch capture cleanup and save reload`` () =
    let content = Content()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "HeldItemCapture"
            [| Loadwildmon("MAGIKARP", 5)
               Startbattle
               End |]
    let splash = Moves.byName "SPLASH"
    let leadBase = PartyMon.create (Species.byName "CYNDAQUIL").Dex 20
    let lead =
        { leadBase with
            Hp = max 1 (leadBase.MaxHp / 2)
            Moves = [ moveId "SPLASH", splash.Pp ]
            HeldItem = Some "BERRY" }
    let reserve =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 20 with
            Moves = [ moveId "SPLASH", splash.Pp ]
            HeldItem = Some "CHARCOAL" }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ lead; reserve ]; Bag = Bag.add "MASTER_BALL" 1 Bag.empty })

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    let mutable frame = 0
    let mutable phase = 0

    let completed () =
        stack.Count = 1 && scene.CanCapture && phase = 3

    while frame < 3000 && not (completed ()) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top with
            | :? BattleScene as battle ->
                if phase = 0 && battle.CurrentState.PlayerTeam |> List.exists (fun mon -> mon.PersistentId = Some lead.Id && mon.HeldItem.IsNone) then
                    phase <- 1

                if phase = 1 && battle.CurrentState.Player.PersistentId = Some reserve.Id then
                    phase <- 2

                if phase = 2 && battle.CurrentState.Outcome = Some Win then
                    phase <- 3

                let snap = battle.RuntimeSnapshot
                if frame % 2 <> 0 then
                    Buttons.none
                elif snap.MessageActive || not snap.PendingMessages.IsEmpty then
                    { Buttons.none with A = true }
                elif phase = 0 then
                    match snap.Mode with
                    | "CommandMenu"
                    | "MoveMenu" -> { Buttons.none with A = true }
                    | _ -> Buttons.none
                elif phase = 1 then
                    match snap.Mode with
                    | "CommandMenu" when battle.CommandCursor = 1 -> { Buttons.none with A = true }
                    | "CommandMenu" when battle.CommandCursor = 0 -> { Buttons.none with Right = true }
                    | "CommandMenu" -> { Buttons.none with Left = true }
                    | "PartyMenu" when battle.PartyCursor = 0 -> { Buttons.none with Down = true }
                    | "PartyMenu" -> { Buttons.none with A = true }
                    | _ -> Buttons.none
                elif phase = 2 then
                    match snap.Mode with
                    | "CommandMenu" when battle.CommandCursor = 2 -> { Buttons.none with A = true }
                    | "CommandMenu" when battle.CommandCursor = 0 -> { Buttons.none with Down = true }
                    | "CommandMenu" -> { Buttons.none with Left = true }
                    | "PackMenu" -> { Buttons.none with A = true }
                    | _ -> Buttons.none
                else
                    Buttons.none
            | :? TextBoxScene when frame % 2 = 0 -> { Buttons.none with A = true }
            | _ -> Buttons.none

        applyTransition stack (top.Update buttons)

    Assert.True(completed (), "The held-item runtime route should consume Berry, switch, capture, and reach an idle save point")
    let consumedLead = scene.DebugPlayer.Party |> List.find (fun mon -> mon.Id = lead.Id)
    let retainedReserve = scene.DebugPlayer.Party |> List.find (fun mon -> mon.Id = reserve.Id)
    Assert.Equal(None, consumedLead.HeldItem)
    Assert.Equal(Some "CHARCOAL", retainedReserve.HeldItem)
    Assert.Equal(3, scene.DebugPlayer.Party.Length)

    let save =
        scene.Capture()
        |> PokeGold.Game.Save.SaveFile.serialize
        |> PokeGold.Game.Save.SaveFile.deserialize
        |> Option.defaultWith (fun () -> failwith "expected held-item runtime save to deserialize")
    let reloaded = OverworldScene.OfSave(content, SilentSound(), save)
    let reloadedLead = reloaded.DebugPlayer.Party |> List.find (fun mon -> mon.Id = lead.Id)
    let reloadedReserve = reloaded.DebugPlayer.Party |> List.find (fun mon -> mon.Id = reserve.Id)
    Assert.Equal(None, reloadedLead.HeldItem)
    Assert.Equal(Some "CHARCOAL", reloadedReserve.HeldItem)

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
    let scene = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "DarkCaveVioletEntrance" 1 1 Down)
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
let ``OVR-010 Party Flash illuminates dark caves persists in caves and resets outdoors`` () =
    let content = Content()
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove "FLASH" [] }
    let player = { PlayerStateOps.initial with Party = [ mon ] }
    let world = World.empty |> World.setFlag "ENGINE_ZEPHYRBADGE"
    let scene = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "DarkCaveVioletEntrance" 1 1 Down)
    scene.Restore(world, player)
    let unlitBrightness = renderBrightness (scene :> Scene)

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
    let litBrightness = renderBrightness (scene :> Scene)
    Assert.True(litBrightness > unlitBrightness * 3.0, sprintf "Flash should illuminate a dark cave (%f -> %f)" unlitBrightness litBrightness)

    press { Buttons.none with A = true }
    let restored = OverworldScene.OfSave(content, SilentSound(), scene.Capture())
    Assert.Equal(1, World.getVar "__flash_active" restored.DebugWorld)
    Assert.True(renderBrightness (restored :> Scene) > unlitBrightness * 3.0)

    scene.DebugWarp "RockTunnel1F" 1 1 Down
    Assert.Equal(1, World.getVar "__flash_active" scene.DebugWorld)
    Assert.True(renderBrightness (scene :> Scene) > unlitBrightness * 3.0)

    scene.DebugWarp "Route31" 1 1 Down
    Assert.Equal(0, World.getVar "__flash_active" scene.DebugWorld)

let private route39HeadbuttFixture (content: Content) =
    let route = OverworldState.loadById content "Route39"
    let occupied x y = route.Npcs |> Array.exists (fun npc -> npc.CellX = x && npc.CellY = y)

    [ for y in 0 .. route.Map.Height * 2 - 1 do
          for x in 0 .. route.Map.Width * 2 - 1 do
              let collision = Movement.collisionIdAtCell route.Map route.Collision x y

              if (collision = 0x15uy || collision = 0x1duy)
                 && not (occupied x y)
                 && MapEvents.bgAt x y route.Events |> Option.isNone then
                  for direction in [ Down; Up; Left; Right ] do
                      let dx, dy = delta direction
                      let px, py = x - dx, y - dy

                      if px >= 0
                         && py >= 0
                         && px < route.Map.Width * 2
                         && py < route.Map.Height * 2
                         && Movement.cellWalkable route.Map route.Collision px py
                         && not (occupied px py) then
                          yield px, py, direction, x, y ]
    |> List.tryHead
    |> Option.defaultWith (fun () -> failwith "expected Route 39 to contain an unoccupied Headbutt tree")

[<Fact>]
let ``OVR-011 Route39 Party Headbutt starts a catchable forest battle`` () =
    let content = Content()
    let playerX, playerY, facing, treeX, treeY = route39HeadbuttFixture content

    let headbutter = { PartyMon.create 155 20 with Moves = MoveLearn.tryLearnMove "HEADBUTT" [] }
    let scene =
        OverworldScene(
            content,
            SilentSound(),
            OverworldState.loadByIdAt content "Route39" playerX playerY facing,
            encounterRandom = FixedRandom([ 0; 0; 191; 0xAB; 0xCD ]))
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ headbutter ] })

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

    let mutable frame = 0
    while frame < 300 && not (stack.[stack.Count - 1] :? BattleScene) do
        frame <- frame + 1
        tickStack stack frame

    match stack.[stack.Count - 1] with
    | :? BattleScene as battle ->
        Assert.Equal("Wild", battle.RuntimeSnapshot.Kind)
        let opponent = Assert.Single(battle.CurrentState.EnemyTeam)
        Assert.Equal("CATERPIE", opponent.Species.Name)
        Assert.Equal(10, opponent.Level)
        Assert.Equal("HEADBUTT", World.getBuffer "__last_field_move" scene.DebugWorld)
    | other -> failwithf "expected a catchable Headbutt battle after the tree shake, got %s" (other.GetType().Name)

[<Fact>]
let ``OVR-011 Route39 Headbutt reports the source no-encounter branch`` () =
    let content = Content()
    let playerX, playerY, facing, treeX, treeY = route39HeadbuttFixture content
    let headbutter = { PartyMon.create 155 20 with Moves = MoveLearn.tryLearnMove "HEADBUTT" [] }
    let scene =
        OverworldScene(
            content,
            SilentSound(),
            OverworldState.loadByIdAt content "Route39" playerX playerY facing,
            encounterRandom = FixedRandom([ 8 ]))
    scene.Restore(World.empty, { PlayerStateOps.initial with Party = [ headbutter ] })

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)

    match (scene :> Scene).Update { Buttons.none with A = true } with
    | Push (:? TextBoxScene as text) -> stack.Add(text :> Scene)
    | other -> failwithf "expected Headbutt no-encounter text, got %A" other

    Assert.Equal(Some "HeadbuttNothing", scene.RuntimeSnapshot.LastTextLabel)
    Assert.Equal(Some "Nope. Nothing...<DONE>", scene.RuntimeSnapshot.LastRenderedText)

    let mutable frame = 0
    while frame < 300 && stack.Count > 1 do
        frame <- frame + 1
        tickStack stack frame

    Assert.Equal(1, stack.Count)

    match (scene :> Scene).Update Buttons.none with
    | Stay -> ()
    | other -> failwithf "Headbutt no-encounter branch should not start a battle, got %A" other

    Assert.True(scene.CanCapture)

let private oldRodPlayer () =
    { PlayerStateOps.initial with
        Party = [ MoveLearn.seedStartingMoves (PartyMon.create 155 10) ]
        Bag = Bag.add "OLD_ROD" 1 Bag.empty }

let private selectOldRodFromPack (scene: OverworldScene) =
    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)

    let press buttons =
        let top = stack.[stack.Count - 1]
        applyTransition stack (top.Update buttons)
        applyTransition stack (stack.[stack.Count - 1].Update Buttons.none)

    press { Buttons.none with Start = true }
    press { Buttons.none with Down = true }
    press { Buttons.none with Down = true }
    press { Buttons.none with A = true }
    press { Buttons.none with Right = true }
    press { Buttons.none with Right = true }
    press { Buttons.none with A = true }
    press { Buttons.none with A = true }
    stack

[<Fact>]
let ``UI-001 Pack Old Rod starts a generated Union Cave fish battle`` () =
    let content = Content()
    let state = OverworldState.loadByIdAt content "UnionCave1F" 5 4 Right

    Assert.Equal(0x29uy, Movement.collisionIdAtCell state.Map state.Collision 6 4)
    Assert.Equal("FISHGROUP_LAKE", MapsData.byName "UnionCave1F" |> Option.get |> fun map -> map.Meta.FishingGroup)

    let scene =
        OverworldScene(
            content,
            SilentSound(),
            state,
            encounterRandom = FixedRandom([ 0; 0; 191; 0xAB; 0xCD ]))
    scene.Restore(World.empty, oldRodPlayer ())

    let stack = selectOldRodFromPack scene

    let mutable frame = 0
    while frame < 300 && not (stack.[stack.Count - 1] :? BattleScene) do
        frame <- frame + 1
        tickStack stack frame

    match stack.[stack.Count - 1] with
    | :? BattleScene as battle ->
        let opponent = Assert.Single(battle.CurrentState.EnemyTeam)
        Assert.Equal("MAGIKARP", opponent.Species.Name)
        Assert.Equal(10, opponent.Level)
        Assert.Equal(1, Bag.count "OLD_ROD" scene.DebugPlayer.Bag)
        Assert.Equal(0, World.getVar "VAR_BATTLETYPE" scene.DebugWorld)
    | other -> failwithf "expected a fishing battle after Old Rod USE, got %s" (other.GetType().Name)

[<Fact>]
let ``UI-001 Pack Old Rod reports the source no-bite branch`` () =
    let content = Content()
    let scene =
        OverworldScene(
            content,
            SilentSound(),
            OverworldState.loadByIdAt content "UnionCave1F" 5 4 Right,
            encounterRandom = FixedRandom([ 128 ]))
    scene.Restore(World.empty, oldRodPlayer ())

    let stack = selectOldRodFromPack scene
    applyTransition stack ((scene :> Scene).Update Buttons.none)

    Assert.Equal("TextBoxScene", stack.[stack.Count - 1].GetType().Name)
    Assert.Equal(Some "RodNothingText", scene.RuntimeSnapshot.LastTextLabel)
    Assert.Equal(Some "Not even a nibble!<PROMPT>", scene.RuntimeSnapshot.LastRenderedText)

    let mutable frame = 0
    while frame < 300 && stack.Count > 1 do
        frame <- frame + 1
        tickStack stack frame

    Assert.Equal(1, stack.Count)
    Assert.True(scene.CanCapture)

[<Fact>]
let ``UI-001 Pack Old Rod rejects non-water and surfing`` () =
    let content = Content()

    let assertNoFish state expectedFacingCollision =
        let scene =
            OverworldScene(
                content,
                SilentSound(),
                state,
                encounterRandom = FixedRandom([ 0; 0 ]))
        scene.Restore(World.empty, oldRodPlayer ())

        let facingX, facingY =
            match state.Player.Facing with
            | Down -> state.Player.CellX, state.Player.CellY + 1
            | Up -> state.Player.CellX, state.Player.CellY - 1
            | Left -> state.Player.CellX - 1, state.Player.CellY
            | Right -> state.Player.CellX + 1, state.Player.CellY

        Assert.Equal(expectedFacingCollision, Movement.collisionIdAtCell state.Map state.Collision facingX facingY)

        let stack = selectOldRodFromPack scene
        applyTransition stack ((scene :> Scene).Update Buttons.none)

        Assert.Equal("TextBoxScene", stack.[stack.Count - 1].GetType().Name)
        Assert.Equal(Some "RodNothingText", scene.RuntimeSnapshot.LastTextLabel)

    assertNoFish (OverworldState.loadByIdAt content "UnionCave1F" 5 4 Left) 0uy

    let surfingState = OverworldState.loadByIdAt content "UnionCave1F" 6 4 Right
    assertNoFish surfingState 0x29uy

[<Fact>]
let ``OVR-011 generated tree tables retain source map sets scores and weighted slots`` () =
        Assert.Equal("TREEMON_SET_FOREST", TreeMonsData.mapSets.["ROUTE_39"])
        Assert.Equal("TREEMON_SET_CANYON", TreeMonsData.mapSets.["ROUTE_29"])
        Assert.Equal("TREEMON_SET_NONE", TreeMonsData.mapSets.["ROUTE_40"])

        let forest = TreeMonsData.tables.["FOREST"]
        let assertRareSlot index weight species level =
            let slot = List.item index forest.Rare
            Assert.Equal(weight, slot.Weight)
            Assert.Equal(species, slot.Species)
            Assert.Equal(level, slot.Level)

        Assert.Equal(6, forest.Rare.Length)
        assertRareSlot 0 50 "CATERPIE" 10
        assertRareSlot 1 15 "PINECO" 10
        assertRareSlot 2 15 "PINECO" 10
        assertRareSlot 3 10 "EXEGGCUTE" 10
        assertRareSlot 4 5 "EXEGGCUTE" 10
        assertRareSlot 5 5 "BUTTERFREE" 10

        Assert.Equal(6, TreeEncounter.coordinateScore 18 6)
        Assert.Equal(TreeEncounter.Rare, TreeEncounter.score 18 6 6)
        Assert.Equal(TreeEncounter.Good, TreeEncounter.score 18 6 4)
        Assert.Equal(TreeEncounter.Bad, TreeEncounter.score 18 6 0)
        Assert.Equal(Some("PINECO", 10), TreeEncounter.tryHeadbutt "Route39" 18 6 6 (FixedRandom([ 0; 50 ])))
        Assert.Equal(None, TreeEncounter.tryHeadbutt "Route39" 18 6 6 (FixedRandom([ 8 ])))
        Assert.Equal(None, TreeEncounter.tryHeadbutt "Route40" 18 6 0 (FixedRandom([ 0; 0 ])))

[<Fact>]
let ``OVR-009 Party Fly selects discovered source destination and persists its spawn`` () =
    let content = Content()
    Assert.Equal(24, MapsData.flyPoints.Length)
    let goldenrodPoint =
        MapsData.flyPoints
        |> Array.find (fun point -> point.Landmark = "LANDMARK_GOLDENROD_CITY")
    Assert.Equal("ENGINE_FLYPOINT_GOLDENROD", goldenrodPoint.Flag)
    Assert.Equal("SPAWN_GOLDENROD", goldenrodPoint.Spawn)
    Assert.Equal(("GoldenrodCity", 15, 28), (goldenrodPoint.MapId, goldenrodPoint.X, goldenrodPoint.Y))
    let rockTunnelPoint =
        MapsData.flyPoints
        |> Array.find (fun point -> point.Landmark = "LANDMARK_ROCK_TUNNEL")
    Assert.Equal("ENGINE_FLYPOINT_ROCK_TUNNEL", rockTunnelPoint.Flag)
    Assert.Equal(("Route10North", 11, 2), (rockTunnelPoint.MapId, rockTunnelPoint.X, rockTunnelPoint.Y))

    let flyer = { PartyMon.create 150 100 with Moves = MoveLearn.tryLearnMove "FLY" [] }
    let player = { PlayerStateOps.initial with Party = [ flyer ] }
    let world =
        World.empty
        |> World.setFlag "ENGINE_STORMBADGE"
        |> World.setFlag "ENGINE_FLYPOINT_NEW_BARK"
        |> World.setFlag "ENGINE_FLYPOINT_GOLDENROD"
    let scene = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "NewBarkTown" 5 5 Down)
    scene.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)

    let press buttons =
        let top = stack.[stack.Count - 1]
        applyTransition stack (top.Update buttons)
        applyTransition stack (stack.[stack.Count - 1].Update Buttons.none)

    let openFlyPickerFromParty () =
        press { Buttons.none with A = true }

        for _ in 1 .. 3 do
            press { Buttons.none with Down = true }

        press { Buttons.none with A = true }

    let openFlyPicker () =
        press { Buttons.none with Start = true }
        press { Buttons.none with Down = true }
        press { Buttons.none with A = true }
        openFlyPickerFromParty ()

    openFlyPicker ()
    match stack.[stack.Count - 1] with
    | :? FlyDestinationScene as picker ->
        Assert.Equal<string list>([ "SPAWN_NEW_BARK"; "SPAWN_GOLDENROD" ], picker.Destinations |> List.map _.Spawn)
    | other -> Assert.Fail(sprintf "expected FlyDestinationScene, got %s" (other.GetType().Name))

    press { Buttons.none with B = true }
    Assert.Equal("NewBarkTown", scene.DebugState.MapId)
    Assert.Equal((5, 5), (scene.DebugState.Player.CellX, scene.DebugState.Player.CellY))

    openFlyPickerFromParty ()
    Assert.Equal("FlyDestinationScene", stack.[stack.Count - 1].GetType().Name)
    press { Buttons.none with Down = true }
    press { Buttons.none with A = true }

    Assert.Equal("GoldenrodCity", scene.DebugState.MapId)
    Assert.Equal((15, 28), (scene.DebugState.Player.CellX, scene.DebugState.Player.CellY))
    Assert.Equal("FLY", World.getBuffer "__last_field_move" scene.DebugWorld)
    Assert.Equal(0, World.getVar "__fly_requested" scene.DebugWorld)

    let restored = OverworldScene.OfSave(content, SilentSound(), scene.Capture())
    Assert.Equal("GoldenrodCity", restored.DebugState.MapId)
    Assert.Equal((15, 28), (restored.DebugState.Player.CellX, restored.DebugState.Player.CellY))

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
