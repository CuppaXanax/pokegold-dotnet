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
