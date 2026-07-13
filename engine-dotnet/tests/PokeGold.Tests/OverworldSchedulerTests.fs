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
    let trainer = Trainers.lookupByName "YOUNGSTER" "JOEY1" |> Option.get
    let finalEnemyLevel = trainer.Party |> List.last |> fun enemy -> enemy.Level
    let expectedPrize = Experience.moneyEarned trainer.BaseReward finalEnemyLevel |> Experience.applyAmuletCoin true
    Assert.Equal(1000 + expectedPrize, scene.DebugPlayer.Money)

    let settledMoney = scene.DebugPlayer.Money
    for _ in 1..5 do (scene :> Scene).Update Buttons.none |> ignore
    Assert.Equal(settledMoney, scene.DebugPlayer.Money)

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
            let actual =
                battle.CurrentState.EnemyTeam
                |> List.map (fun mon ->
                    mon.Species.Name,
                    mon.Level,
                    mon.HeldItem,
                    (mon.Moves |> List.map (fun move -> move.Name)))

            Assert.Equal<(string * int * string option * string list) list>(expected, actual)
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
               Writetext "CatchDone"
               End |]
        |> fun s -> { s with Text = Map.ofList [ "CatchDone", "done<DONE>" ] }

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
    let splash = Moves.byName "SPLASH"
    let dragonRage = Moves.byName "DRAGON_RAGE"
    let lead =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 2 with
            Hp = 1
            Moves = [ moveId "SPLASH", splash.Pp ] }
    let finisher =
        { PartyMon.create (Species.byName "MEWTWO").Dex 100 with
            Moves = [ moveId "DRAGON_RAGE", dragonRage.Pp ] }
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
    Assert.Equal<(int * int) list>([ moveId "DRAGON_RAGE", dragonRage.Pp - 2 ], actualFinisher.Moves)
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
