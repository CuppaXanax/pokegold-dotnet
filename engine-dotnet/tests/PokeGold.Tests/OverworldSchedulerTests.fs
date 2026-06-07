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

    for frame in 1 .. 2500 do
        tickStack stack frame

        if World.hasEvent "EVENT_PLAYERS_HOUSE_MOM_1" overworld.DebugWorld then
            sawFutureFlags <- true

        if sawFutureFlags then
            Assert.True(overworld.DebugVisible mom1, $"Mom1 disappeared mid-cutscene at frame {frame}")
            Assert.False(overworld.DebugVisible mornMom2, $"Mom2 appeared mid-cutscene at frame {frame}")

    Assert.True(sawFutureFlags, "Mom script should stage future visibility flags during the cutscene")

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
