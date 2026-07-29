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

let private runGeneratedCommands content mapId commands labels world player =
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
                    { Commands = commands
                      Labels = labels } }
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

[<Fact>]
let ``generated conditional branches take their source targets`` () =
    let content = Content()
    let branch predicate input =
        let mapId, command = sourceCommand predicate
        let target =
            match command with
            | Iffalse target
            | Iftrue target -> target
            | Ifequal(_, target)
            | Ifnotequal(_, target)
            | Ifgreater(_, target)
            | Ifless(_, target) -> target
            | _ -> failwith "source command did not resolve to a conditional branch"
        let scene =
            runGeneratedCommands content mapId
                [| Setval input; command; Loadmem("__branch", 0); End; Loadmem("__branch", 1); End |]
                (Map.ofList [ "CommandConformance", 0; target, 4 ])
                World.empty PlayerStateOps.initial
        Assert.Equal(1, World.getVar "__branch" scene.DebugWorld)

    branch (function | Iffalse _ -> true | _ -> false) 0
    branch (function | Iftrue _ -> true | _ -> false) 1
    let comparison predicate valueOf input =
        let _, command = sourceCommand predicate
        branch predicate (input (valueOf command))
    comparison (function | Ifequal _ -> true | _ -> false)
        (function | Ifequal(value, _) -> value | _ -> failwith "expected ifequal") id
    comparison (function | Ifnotequal _ -> true | _ -> false)
        (function | Ifnotequal(value, _) -> value | _ -> failwith "expected ifnotequal") ((+) 1)
    comparison (function | Ifgreater _ -> true | _ -> false)
        (function | Ifgreater(value, _) -> value | _ -> failwith "expected ifgreater") ((+) 1)
    comparison (function | Ifless _ -> true | _ -> false)
        (function | Ifless(value, _) -> value | _ -> failwith "expected ifless") (fun value -> value - 1)

[<Fact>]
let ``generated scall and sjump commands preserve their source control flow`` () =
    let content = Content()
    let scallMap, scall = sourceCommand (function | Scall _ -> true | _ -> false)
    let scallTarget =
        match scall with
        | Scall target -> target
        | _ -> failwith "source command did not resolve to scall"
    let called =
        runGeneratedCommands content scallMap
            [| scall; Loadmem("__returned", 1); End; Loadmem("__called", 1); End |]
            (Map.ofList [ "CommandConformance", 0; scallTarget, 3 ])
            World.empty PlayerStateOps.initial
    Assert.Equal(1, World.getVar "__called" called.DebugWorld)
    Assert.Equal(1, World.getVar "__returned" called.DebugWorld)

    let jumpMap, jump = sourceCommand (function | Sjump _ -> true | _ -> false)
    let jumpTarget =
        match jump with
        | Sjump target -> target
        | _ -> failwith "source command did not resolve to sjump"
    let jumped =
        runGeneratedCommands content jumpMap
            [| jump; Loadmem("__fell_through", 1); End; Loadmem("__jumped", 1); End |]
            (Map.ofList [ "CommandConformance", 0; jumpTarget, 3 ])
            World.empty PlayerStateOps.initial
    Assert.Equal(0, World.getVar "__fell_through" jumped.DebugWorld)
    Assert.Equal(1, World.getVar "__jumped" jumped.DebugWorld)

[<Fact>]
let ``generated variable and memory commands mutate the live world by their source operands`` () =
    let content = Content()
    let run command mapId world prefix =
        runGeneratedCommands content mapId (Array.concat [ prefix; [| command; Writemem "__result"; End |] ])
            (Map.ofList [ "CommandConformance", 0 ]) world PlayerStateOps.initial

    let addMap, add = sourceCommand (function | Addval _ -> true | _ -> false)
    let addValue =
        match add with
        | Addval value -> value
        | _ -> failwith "source command did not resolve to addval"
    Assert.Equal(11 + addValue, World.getVar "__result" (run add addMap World.empty [| Setval 11 |]).DebugWorld)

    let readVarMap, readVar = sourceCommand (function | Readvar _ -> true | _ -> false)
    let varName =
        match readVar with
        | Readvar name -> name
        | _ -> failwith "source command did not resolve to readvar"
    Assert.Equal(37, World.getVar "__result" (run readVar readVarMap (World.setVar varName 37 World.empty) [||]).DebugWorld)

    let loadVarMap, loadVar = sourceCommand (function | Loadvar _ -> true | _ -> false)
    let loadedVar, loadedValue =
        match loadVar with
        | Loadvar(name, value) -> name, value
        | _ -> failwith "source command did not resolve to loadvar"
    let loaded = run loadVar loadVarMap World.empty [||]
    Assert.Equal(loadedValue, World.getVar loadedVar loaded.DebugWorld)
    Assert.Equal(loadedValue, World.getVar "__result" loaded.DebugWorld)

    let readMemMap, readMem = sourceCommand (function | Readmem _ -> true | _ -> false)
    let readAddress =
        match readMem with
        | Readmem address -> address
        | _ -> failwith "source command did not resolve to readmem"
    Assert.Equal(53, World.getVar "__result" (run readMem readMemMap (World.setVar readAddress 53 World.empty) [||]).DebugWorld)

    let writeMemMap, writeMem = sourceCommand (function | Writemem _ -> true | _ -> false)
    let writeAddress =
        match writeMem with
        | Writemem address -> address
        | _ -> failwith "source command did not resolve to writemem"
    Assert.Equal(71, World.getVar writeAddress (run writeMem writeMemMap World.empty [| Setval 71 |]).DebugWorld)

    let randomMap, random = sourceCommand (function | Random _ -> true | _ -> false)
    let limit =
        match random with
        | Random value -> value
        | _ -> failwith "source command did not resolve to random"
    Assert.InRange(World.getVar "__result" (run random randomMap World.empty [| Setval 0 |]).DebugWorld, 0, limit - 1)

[<Fact>]
let ``generated check and scene commands expose source state through the live world`` () =
    let content = Content()
    let result command mapId world player =
        runGeneratedCommands content mapId [| command; Writemem "__result"; End |]
            (Map.ofList [ "CommandConformance", 0 ]) world player

    let eventMap, checkEvent = sourceCommand (function | Checkevent _ -> true | _ -> false)
    let eventName =
        match checkEvent with
        | Checkevent name -> name
        | _ -> failwith "source command did not resolve to checkevent"
    Assert.Equal(1, World.getVar "__result"
        (result checkEvent eventMap (World.setEvent eventName World.empty) PlayerStateOps.initial).DebugWorld)

    let flagMap, checkFlag = sourceCommand (function | Checkflag _ -> true | _ -> false)
    let flagName =
        match checkFlag with
        | Checkflag name -> name
        | _ -> failwith "source command did not resolve to checkflag"
    Assert.Equal(1, World.getVar "__result"
        (result checkFlag flagMap (World.setFlag flagName World.empty) PlayerStateOps.initial).DebugWorld)

    let mapSceneMap, setMapScene = sourceCommand (function | Setmapscene _ -> true | _ -> false)
    let destinationMap, scene =
        match setMapScene with
        | Setmapscene(map, scene) -> map, scene
        | _ -> failwith "source command did not resolve to setmapscene"
    let mapScene = runGeneratedCommand content mapSceneMap setMapScene World.empty PlayerStateOps.initial
    Assert.Equal(scene, World.getScene destinationMap mapScene.DebugWorld)

    let sceneMap, checkScene = sourceCommand (function | Checkscene -> true | _ -> false)
    let checkedScene =
        runGeneratedCommands content sceneMap [| Setscene 6; checkScene; Writemem "__result"; End |]
            (Map.ofList [ "CommandConformance", 0 ]) World.empty PlayerStateOps.initial
    Assert.Equal(6, World.getVar "__result" checkedScene.DebugWorld)

    let setSceneMap, setScene = sourceCommand (function | Setscene _ -> true | _ -> false)
    let scene =
        match setScene with
        | Setscene value -> value
        | _ -> failwith "source command did not resolve to setscene"
    let setCurrentScene = runGeneratedCommand content setSceneMap setScene World.empty PlayerStateOps.initial
    Assert.Equal(scene, World.getScene setSceneMap setCurrentScene.DebugWorld)

    let itemMap, checkItem = sourceCommand (function | Checkitem _ -> true | _ -> false)
    let item =
        match checkItem with
        | Checkitem item -> item
        | _ -> failwith "source command did not resolve to checkitem"
    let player = { PlayerStateOps.initial with Bag = Bag.add item 1 Bag.empty }
    Assert.Equal(1, World.getVar "__result" (result checkItem itemMap World.empty player).DebugWorld)
