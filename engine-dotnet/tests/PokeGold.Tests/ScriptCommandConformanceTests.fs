module PokeGold.Tests.ScriptCommandConformanceTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Scenes

type private SilentSound() =
    interface PokeGold.Game.Audio.ISoundBoard with
        member _.PlayMusic _ = ()
        member _.PlaySfx _ = ()
        member _.PlayJingle _ = ()
        member _.StopMusic() = ()

let private sourceCommand predicate =
    MapsData.all
    |> Seq.tryPick (fun (KeyValue(mapId, map)) ->
        map.Script.Commands
        |> Array.tryPick (fun command ->
            if predicate command then Some(mapId, command) else None))
    |> Option.defaultWith (fun () -> failwith "expected a generated map-script command under test")

let private runGeneratedCommand content mapId command world player =
    let baseState = OverworldState.loadByIdAt content mapId 1 1 Down
    let state =
        { baseState with
            Events =
                { baseState.Events with
                    Scenes = [| "SCENE_COMMAND_CONFORMANCE" |]
                    SceneLabels = [| "CommandConformance" |]
                    Coords = [||]
                    Callbacks = [||] }
            Script =
                    { Commands = [| command; End |]
                      Labels = Map.ofList [ "CommandConformance", 0 ] } }
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(world, player)
    (scene :> Scene).Update Buttons.none |> ignore
    scene

let private directionOf =
    function
    | "DOWN" -> Down
    | "UP" -> Up
    | "LEFT" -> Left
    | "RIGHT" -> Right
    | value -> failwithf "unexpected source facing %s" value

[<Fact>]
let ``generated giveitem and takeitem scripts mutate the live bag by their source operands`` () =
    let content = Content()
    let giveMap, give =
        sourceCommand (function | Giveitem _ -> true | _ -> false)
    let item, quantity =
        match give with
        | Giveitem(item, quantity) -> item, quantity
        | _ -> failwith "source label did not resolve to giveitem"
    let given = runGeneratedCommand content giveMap give World.empty PlayerStateOps.initial
    Assert.Equal(quantity, Bag.count item given.DebugPlayer.Bag)

    let takeMap, take =
        sourceCommand (function | Takeitem _ -> true | _ -> false)
    let item, quantity =
        match take with
        | Takeitem(item, quantity) -> item, quantity
        | _ -> failwith "source label did not resolve to takeitem"
    let player = { PlayerStateOps.initial with Bag = Bag.add item quantity Bag.empty }
    let taken = runGeneratedCommand content takeMap take World.empty player
    Assert.Equal(0, Bag.count item taken.DebugPlayer.Bag)

    let verboseMap, verbose =
        sourceCommand (function | Verbosegiveitem _ -> true | _ -> false)
    let item, quantity =
        match verbose with
        | Verbosegiveitem(item, quantity) -> item, quantity
        | _ -> failwith "source command did not resolve to verbosegiveitem"
    let verboseScene = runGeneratedCommand content verboseMap verbose World.empty PlayerStateOps.initial
    Assert.Equal(quantity, Bag.count item verboseScene.DebugPlayer.Bag)

[<Fact>]
let ``generated givepoke script creates its source species and level in the live party`` () =
    let content = Content()
    let mapId, command =
        sourceCommand (function | Givepoke _ -> true | _ -> false)
    let species, level =
        match command with
        | Givepoke(species, level, _, _, _) -> species, level
        | _ -> failwith "source label did not resolve to givepoke"
    let scene = runGeneratedCommand content mapId command World.empty PlayerStateOps.initial
    let received = Assert.Single(scene.DebugPlayer.Party)
    Assert.Equal((Species.byName species).Dex, received.SpeciesId)
    Assert.Equal(level, received.Level)

[<Fact>]
let ``generated warp scripts load their source destination`` () =
    let content = Content()
    let warpMap, warp =
        sourceCommand (function | Warp(destination, _, _) when destination <> "NONE" -> true | _ -> false)
    let destination, x, y =
        match warp with
        | Warp(destination, x, y) -> destination, x, y
        | _ -> failwith "source label did not resolve to warp"
    let warped = runGeneratedCommand content warpMap warp World.empty PlayerStateOps.initial
    let expectedWarp =
        OverworldState.tryWarpExplicit content destination x y None Down
        |> Option.defaultWith (fun () -> failwith "source warp did not resolve")
    Assert.Equal(expectedWarp.MapId, warped.DebugState.MapId)
    Assert.Equal((expectedWarp.Player.CellX, expectedWarp.Player.CellY), (warped.DebugState.Player.CellX, warped.DebugState.Player.CellY))

[<Fact>]
let ``generated changeblock scripts replace the source map block at their source cell`` () =
    let content = Content()
    let mapId, command =
        sourceCommand (function | Changeblock _ -> true | _ -> false)
    let x, y, block =
        match command with
        | Changeblock(x, y, block) -> x, y, block
        | _ -> failwith "source label did not resolve to changeblock"
    let scene = runGeneratedCommand content mapId command World.empty PlayerStateOps.initial
    Assert.Equal(byte block, Map.blockAt scene.DebugState.Map (x / 2) (y / 2))

[<Fact>]
let ``generated event and flag scripts persist and clear their source world state`` () =
    let content = Content()
    let eventMap, eventCommand =
        sourceCommand (function | Setevent _ -> true | _ -> false)
    let eventName =
        match eventCommand with
        | Setevent name -> name
        | _ -> failwith "source label did not resolve to setevent"
    let eventScene = runGeneratedCommand content eventMap eventCommand World.empty PlayerStateOps.initial
    Assert.True(World.hasEvent eventName eventScene.DebugWorld)

    let clearEventMap, clearEventCommand =
        sourceCommand (function | Clearevent _ -> true | _ -> false)
    let clearEventName =
        match clearEventCommand with
        | Clearevent name -> name
        | _ -> failwith "source command did not resolve to clearevent"
    let clearedEvent =
        runGeneratedCommand content clearEventMap clearEventCommand (World.setEvent clearEventName World.empty) PlayerStateOps.initial
    Assert.False(World.hasEvent clearEventName clearedEvent.DebugWorld)

    let flagMap, flagCommand =
        sourceCommand (function | Setflag _ -> true | _ -> false)
    let flagName =
        match flagCommand with
        | Setflag name -> name
        | _ -> failwith "source label did not resolve to setflag"
    let flagScene = runGeneratedCommand content flagMap flagCommand World.empty PlayerStateOps.initial
    Assert.True(World.hasFlag flagName flagScene.DebugWorld)

    let clearFlagMap, clearFlagCommand =
        sourceCommand (function | Clearflag _ -> true | _ -> false)
    let clearFlagName =
        match clearFlagCommand with
        | Clearflag name -> name
        | _ -> failwith "source command did not resolve to clearflag"
    let clearedFlag =
        runGeneratedCommand content clearFlagMap clearFlagCommand (World.setFlag clearFlagName World.empty) PlayerStateOps.initial
    Assert.False(World.hasFlag clearFlagName clearedFlag.DebugWorld)
