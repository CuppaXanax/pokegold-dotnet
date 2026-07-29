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

type private RecordingSound() =
    let music = ResizeArray<string>()
    let sfx = ResizeArray<string>()

    member _.Music = music |> Seq.toList
    member _.Sfx = sfx |> Seq.toList

    interface PokeGold.Game.Audio.ISoundBoard with
        member _.PlayMusic path = music.Add path
        member _.PlaySfx name = sfx.Add name
        member _.PlayJingle _ = ()
        member _.StopMusic() = music.Add "__STOP__"

let private sourceCommand predicate =
    MapsData.all
    |> Seq.tryPick (fun (KeyValue(mapId, map)) ->
        map.Script.Commands
        |> Array.tryPick (fun command ->
            if predicate command then Some(mapId, command) else None))
    |> Option.defaultWith (fun () -> failwith "expected a generated map-script command under test")

let private sourceMapCommand predicate =
    MapsData.all
    |> Seq.tryPick (fun (KeyValue(mapId, map)) ->
        map.Script.Commands
        |> Array.tryPick (fun command ->
            if predicate mapId command then Some(mapId, command) else None))
    |> Option.defaultWith (fun () -> failwith "expected a resolvable generated map-script command under test")

let private runGeneratedCommand content mapId command world player =
    let scene =
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
        scene
    (scene :> Scene).Update Buttons.none |> ignore
    scene

let private generatedSceneWithSound sound content mapId commands labels world player =
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
                      Labels = Map.ofList (("CommandConformance", 0) :: labels) } }
    let scene = OverworldScene(content, sound, state)
    scene.Restore(world, player)
    scene

let private generatedScene content mapId commands labels world player =
    generatedSceneWithSound (SilentSound()) content mapId commands labels world player

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

let private sourceStdCommand predicate =
    StdScriptsData.program.Commands
    |> Array.tryFind predicate
    |> Option.defaultWith (fun () -> failwith "expected a generated standard-script command under test")

let private dismissTextBox (textBox: Scene) =
    let mutable dismissed = false
    let mutable frame = 0

    while not dismissed && frame < 1000 do
        frame <- frame + 1
        let buttons =
            if frame % 2 = 0 then { Buttons.none with A = true }
            else Buttons.none
        dismissed <- (textBox.Update buttons = Pop)

    Assert.True(dismissed, "expected text box to complete")

let private directionOf =
    function
    | "DOWN" -> Down
    | "UP" -> Up
    | "LEFT" -> Left
    | "RIGHT" -> Right
    | value -> failwithf "unexpected source facing %s" value

let private sourceAmount (args: string list) =
    args
    |> List.tryPick (fun arg ->
        match System.Int32.TryParse arg with
        | true, value -> Some value
        | _ -> None)
    |> Option.defaultWith (fun () -> failwithf "expected numeric source amount in %A" args)

let private battleReadyPlayer species =
    { PlayerStateOps.initial with
        Party = [ PartyMon.create (Species.byName species).Dex 5 ] }

let private advance scene frames =
    for _ in 1 .. frames do
        (scene :> Scene).Update Buttons.none |> ignore

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

[<Fact>]
let ``generated text window commands preserve script continuation`` () =
    let content = Content()

    [ sourceCommand (function | Opentext -> true | _ -> false)
      sourceCommand (function | Closetext -> true | _ -> false)
      sourceCommand (function | Waitbutton -> true | _ -> false)
      sourceCommand (function | Promptbutton -> true | _ -> false) ]
    |> List.iteri (fun index (mapId, command) ->
        let completed = $"EVENT_TEST_TEXT_WINDOW_{index}"
        let scene =
            generatedScene
                content
                mapId
                [| command; Setevent completed; End |]
                []
                World.empty
                PlayerStateOps.initial
        (scene :> Scene).Update Buttons.none |> ignore
        Assert.True(World.hasEvent completed scene.DebugWorld))

[<Fact>]
let ``generated text commands open live text boxes and preserve their source control flow`` () =
    let content = Content()

    let writetextMap, writetext =
        sourceCommand (function | Writetext _ -> true | _ -> false)
    let written =
        generatedScene
            content
            writetextMap
            [| writetext; Setevent "EVENT_TEST_WRITETEXT_RESUMED"; End |]
            []
            World.empty
            PlayerStateOps.initial
    let textBox =
        match (written :> Scene).Update Buttons.none with
        | Push (:? TextBoxScene as textBox) -> textBox :> Scene
        | other -> failwithf "expected generated writetext to open a TextBoxScene, got %A" other
    Assert.Equal(Some (match writetext with | Writetext label -> label | _ -> failwith "expected writetext"), written.RuntimeSnapshot.LastTextLabel)
    dismissTextBox textBox
    (written :> Scene).Update Buttons.none |> ignore
    Assert.True(World.hasEvent "EVENT_TEST_WRITETEXT_RESUMED" written.DebugWorld)

    [ sourceCommand (function | Jumptext _ -> true | _ -> false)
      sourceCommand (function | Jumptextfaceplayer _ -> true | _ -> false) ]
    |> List.iteri (fun index (mapId, command) ->
        let completed = $"EVENT_TEST_JUMPTEXT_{index}"
        let scene =
            generatedScene
                content
                mapId
                [| command; Setevent completed; End |]
                []
                World.empty
                PlayerStateOps.initial
        let textBox =
            match (scene :> Scene).Update Buttons.none with
            | Push (:? TextBoxScene as textBox) -> textBox :> Scene
            | other -> failwithf "expected generated jumptext to open a TextBoxScene, got %A" other
        dismissTextBox textBox
        (scene :> Scene).Update Buttons.none |> ignore
        Assert.False(World.hasEvent completed scene.DebugWorld))

[<Fact>]
let ``generated yesorno script routes a live menu choice into its source branch`` () =
    let content = Content()
    let mapId, yesorno = sourceCommand (function | Yesorno -> true | _ -> false)
    let scene =
        generatedScene
            content
            mapId
            [| yesorno
               Iftrue "Yes"
               End
               Setevent "EVENT_TEST_YESNO_SELECTED"
               End |]
            [ "Yes", 3 ]
            World.empty
            PlayerStateOps.initial
    let menu =
        match (scene :> Scene).Update Buttons.none with
        | Push (:? YesNoScene as menu) -> menu :> Scene
        | other -> failwithf "expected generated yesorno to open YesNoScene, got %A" other
    Assert.Equal(Pop, menu.Update { Buttons.none with A = true })
    (scene :> Scene).Update Buttons.none |> ignore
    Assert.True(World.hasEvent "EVENT_TEST_YESNO_SELECTED" scene.DebugWorld)

[<Fact>]
let ``generated phone commands mutate contacts and route their source results`` () =
    let content = Content()

    let addMap, add = sourceCommand (function | Addcellnum _ -> true | _ -> false)
    let addPhone =
        match add with
        | Addcellnum phone -> phone
        | _ -> failwith "expected addcellnum"
    let added = runGeneratedCommand content addMap add World.empty PlayerStateOps.initial
    Assert.Contains(addPhone, added.DebugPlayer.PhoneContacts)

    let checkMap, check = sourceCommand (function | Checkcellnum _ -> true | _ -> false)
    let checkPhone =
        match check with
        | Checkcellnum phone -> phone
        | _ -> failwith "expected checkcellnum"
    let checkedScene =
        generatedScene
            content
            checkMap
            [| check; Iftrue "Known"; End; Setevent "EVENT_TEST_PHONE_KNOWN"; End |]
            [ "Known", 3 ]
            World.empty
            { PlayerStateOps.initial with PhoneContacts = Set.singleton checkPhone }
    (checkedScene :> Scene).Update Buttons.none |> ignore
    Assert.True(World.hasEvent "EVENT_TEST_PHONE_KNOWN" checkedScene.DebugWorld)

    let askMap, ask = sourceCommand (function | Askforphonenumber _ -> true | _ -> false)
    let askPhone =
        match ask with
        | Askforphonenumber phone -> phone
        | _ -> failwith "expected askforphonenumber"
    let asked =
        generatedScene
            content
            askMap
            [| ask; Ifequal(0, "Accepted"); End; Setevent "EVENT_TEST_PHONE_ACCEPTED"; End |]
            [ "Accepted", 3 ]
            World.empty
            PlayerStateOps.initial
    let menu =
        match (asked :> Scene).Update Buttons.none with
        | Push (:? YesNoScene as menu) -> menu :> Scene
        | other -> failwithf "expected generated askforphonenumber to open YesNoScene, got %A" other
    Assert.Equal(Pop, menu.Update { Buttons.none with A = true })
    (asked :> Scene).Update Buttons.none |> ignore
    Assert.Contains(askPhone, asked.DebugPlayer.PhoneContacts)
    Assert.True(World.hasEvent "EVENT_TEST_PHONE_ACCEPTED" asked.DebugWorld)

[<Fact>]
let ``generated special phone commands expose their source call to standard call checks`` () =
    let content = Content()
    let mapId, special =
        sourceCommand (function | Specialphonecall call when call <> "SPECIALCALL_NONE" -> true | _ -> false)
    let call =
        match special with
        | Specialphonecall call -> call
        | _ -> failwith "expected specialphonecall"
    let specialScene = runGeneratedCommand content mapId special World.empty PlayerStateOps.initial
    Assert.Equal(call, World.getBuffer "__special_phone_call" specialScene.DebugWorld)

    let check = sourceStdCommand (function | Checkphonecall -> true | _ -> false)
    let checkMap, _ = sourceCommand (fun _ -> true)
    let checkedScene =
        generatedScene
            content
            checkMap
            [| check; Iftrue "CallPending"; End; Setevent "EVENT_TEST_PHONE_CALL_PENDING"; End |]
            [ "CallPending", 3 ]
            (World.setBuffer "__special_phone_call" call World.empty)
            PlayerStateOps.initial
    (checkedScene :> Scene).Update Buttons.none |> ignore
    Assert.True(World.hasEvent "EVENT_TEST_PHONE_CALL_PENDING" checkedScene.DebugWorld)

[<Fact>]
let ``generated menu commands retain source data and route live selections`` () =
    let content = Content()
    let loadMap, load = sourceCommand (function | Loadmenu _ -> true | _ -> false)
    let menuLabel =
        match load with
        | Loadmenu label -> label
        | _ -> failwith "expected loadmenu"
    let vertical = sourceCommand (function | Verticalmenu -> true | _ -> false) |> snd
    let verticalScene =
        generatedScene
            content
            loadMap
            [| load; vertical; Ifequal(1, "Selected"); End; Setevent "EVENT_TEST_VERTICAL_MENU"; End |]
            [ "Selected", 4 ]
            World.empty
            PlayerStateOps.initial
    let verticalMenu =
        match (verticalScene :> Scene).Update Buttons.none with
        | Push (:? ScriptMenuScene as menu) -> menu :> Scene
        | other -> failwithf "expected generated verticalmenu to open ScriptMenuScene, got %A" other
    Assert.Equal(Pop, verticalMenu.Update { Buttons.none with A = true })
    (verticalScene :> Scene).Update Buttons.none |> ignore
    Assert.Equal(menuLabel, World.getBuffer "__loaded_menu" verticalScene.DebugWorld)
    Assert.True(World.hasEvent "EVENT_TEST_VERTICAL_MENU" verticalScene.DebugWorld)

    let twoD = sourceCommand (function | TwoDMenu -> true | _ -> false) |> snd
    let twoDScene =
        generatedScene
            content
            loadMap
            [| load; twoD; Ifequal(1, "Selected"); End; Setevent "EVENT_TEST_2D_MENU"; End |]
            [ "Selected", 4 ]
            World.empty
            PlayerStateOps.initial
    let twoDMenu =
        match (twoDScene :> Scene).Update Buttons.none with
        | Push (:? ScriptMenuScene as menu) -> menu :> Scene
        | other -> failwithf "expected generated 2dmenu to open ScriptMenuScene, got %A" other
    Assert.Equal(Pop, twoDMenu.Update { Buttons.none with A = true })
    (twoDScene :> Scene).Update Buttons.none |> ignore
    Assert.True(World.hasEvent "EVENT_TEST_2D_MENU" twoDScene.DebugWorld)

    let coordsMap, coords = sourceCommand (function | MenuCoords _ -> true | _ -> false)
    let sourceCoords =
        match coords with
        | MenuCoords values -> String.concat "," values
        | _ -> failwith "expected menu_coords"
    let coordsScene = runGeneratedCommand content coordsMap coords World.empty PlayerStateOps.initial
    Assert.Equal(sourceCoords, World.getBuffer "__menu_coords" coordsScene.DebugWorld)

[<Fact>]
let ``generated closewindow command resumes its source script`` () =
    let content = Content()
    let mapId, close = sourceCommand (function | Closewindow -> true | _ -> false)
    let scene =
        generatedScene
            content
            mapId
            [| close; Setevent "EVENT_TEST_CLOSEWINDOW_RESUMED"; End |]
            []
            World.empty
            PlayerStateOps.initial
    (scene :> Scene).Update Buttons.none |> ignore
    Assert.True(World.hasEvent "EVENT_TEST_CLOSEWINDOW_RESUMED" scene.DebugWorld)

[<Fact>]
let ``generated loadwildmon stages its source opponent for battle`` () =
    let content = Content()
    let mapId, command = sourceCommand (function | Loadwildmon _ -> true | _ -> false)
    let species, level =
        match command with
        | Loadwildmon(species, level) -> species, level
        | _ -> failwith "expected loadwildmon"
    let scene =
        generatedScene content mapId [| command; Startbattle |] [] World.empty (battleReadyPlayer "CYNDAQUIL")
    let battle =
        match (scene :> Scene).Update Buttons.none with
        | Push (:? BattleScene as battle) -> battle
        | other -> failwithf "expected source loadwildmon to start a BattleScene, got %A" other
    Assert.Equal((Species.byName species).Name, battle.CurrentState.Enemy.Species.Name)
    Assert.Equal(level, battle.CurrentState.Enemy.Level)

[<Fact>]
let ``generated trainer battle commands stage source trainer text and last talked actor`` () =
    let content = Content()
    let trainerMap, trainer = sourceCommand (function | Loadtrainer _ -> true | _ -> false)
    let group, id =
        match trainer with
        | Loadtrainer(group, id) -> group, id
        | _ -> failwith "expected loadtrainer"
    let _, winLoss = sourceCommand (function | Winlosstext _ -> true | _ -> false)
    let winText, lossText =
        match winLoss with
        | Winlosstext(winText, lossText) -> winText, lossText
        | _ -> failwith "expected winlosstext"
    let battleScene =
        generatedScene content trainerMap [| winLoss; trainer; Startbattle |] [] World.empty (battleReadyPlayer "CYNDAQUIL")
    let battle =
        match (battleScene :> Scene).Update Buttons.none with
        | Push (:? BattleScene as battle) -> battle
        | other -> failwithf "expected source trainer commands to start a BattleScene, got %A" other
    match battle.CurrentState.Kind with
    | PokeGold.Game.Battle.TrainerBattle context ->
        Assert.Equal(group, context.Group)
        Assert.Equal(id, context.Id)
        Assert.Equal(Some winText, context.WinText)
        Assert.Equal(Some lossText, context.LossText)
    | kind -> failwithf "expected trainer battle, got %A" kind

    let lastTalkedMap, lastTalked = sourceCommand (function | Setlasttalked _ -> true | _ -> false)
    let actor =
        match lastTalked with
        | Setlasttalked actor -> actor
        | _ -> failwith "expected setlasttalked"
    let faced =
        generatedScene content lastTalkedMap [| lastTalked; Faceplayer; End |] [] World.empty PlayerStateOps.initial
    (faced :> Scene).Update Buttons.none |> ignore
    let index =
        OverworldState.objectIndexOf lastTalkedMap actor
        |> Option.defaultWith (fun () -> failwithf "source actor %s was not found on %s" actor lastTalkedMap)
    let npc = faced.DebugState.Npcs.[index]
    let expected =
        if abs (faced.DebugState.Player.CellX - npc.CellX) >= abs (faced.DebugState.Player.CellY - npc.CellY) then
            if faced.DebugState.Player.CellX >= npc.CellX then Right else Left
        elif faced.DebugState.Player.CellY >= npc.CellY then Down
        else Up
    Assert.Equal(expected, npc.Facing)

[<Fact>]
let ``generated giveegg script adds its source egg and rejects a full party`` () =
    let content = Content()
    let mapId, command = sourceCommand (function | Giveegg _ -> true | _ -> false)
    let species, level =
        match command with
        | Giveegg(species, level) -> species, level
        | _ -> failwith "expected giveegg"
    let received = runGeneratedCommand content mapId command World.empty PlayerStateOps.initial
    let egg = Assert.Single(received.DebugPlayer.Party)
    Assert.Equal((Species.byName species).Dex, egg.SpeciesId)
    Assert.Equal(level, egg.Level)
    Assert.Equal("EGG", egg.Nickname)
    Assert.True(egg.HatchSteps.IsSome)

    let fullParty =
        { PlayerStateOps.initial with
            Party = List.replicate 6 (PartyMon.create (Species.byName species).Dex level) }
    let refused = runGeneratedCommand content mapId command World.empty fullParty
    Assert.Equal(6, refused.DebugPlayer.Party.Length)

[<Fact>]
let ``generated money and coin commands use their source amounts and affordability`` () =
    let content = Content()
    let checkMoneyMap, checkMoney = sourceCommand (function | Checkmoney _ -> true | _ -> false)
    let moneyAmount =
        match checkMoney with
        | Checkmoney args -> sourceAmount args
        | _ -> failwith "expected checkmoney"
    let moneyResult money =
        runGeneratedCommands content checkMoneyMap [| checkMoney; Writemem "__result"; End |]
            (Map.ofList [ "CommandConformance", 0 ]) World.empty { PlayerStateOps.initial with Money = money }
    Assert.Equal(1, World.getVar "__result" (moneyResult moneyAmount).DebugWorld)
    Assert.Equal(2, World.getVar "__result" (moneyResult (moneyAmount - 1)).DebugWorld)

    let takeMoneyMap, takeMoney = sourceCommand (function | Takemoney _ -> true | _ -> false)
    let takeMoneyAmount =
        match takeMoney with
        | Takemoney args -> sourceAmount args
        | _ -> failwith "expected takemoney"
    let taken =
        runGeneratedCommand content takeMoneyMap takeMoney World.empty { PlayerStateOps.initial with Money = takeMoneyAmount }
    Assert.Equal(0, taken.DebugPlayer.Money)
    let unaffordable =
        runGeneratedCommand content takeMoneyMap takeMoney World.empty { PlayerStateOps.initial with Money = takeMoneyAmount - 1 }
    Assert.Equal(takeMoneyAmount - 1, unaffordable.DebugPlayer.Money)

    let giveMoney = Givemoney [ "YOUR_MONEY"; string takeMoneyAmount ]
    let given = runGeneratedCommand content takeMoneyMap giveMoney World.empty { PlayerStateOps.initial with Money = 0 }
    Assert.Equal(takeMoneyAmount, given.DebugPlayer.Money)

    let coinAmount command =
        match command with
        | Checkcoins(Some amount)
        | Takecoins(Some amount)
        | Givecoins(Some amount) -> amount
        | _ -> failwithf "expected concrete source coin amount, got %A" command
    let checkCoinsMap, checkCoins = sourceCommand (function | Checkcoins _ -> true | _ -> false)
    let checkedCoins =
        runGeneratedCommands content checkCoinsMap [| checkCoins; Writemem "__result"; End |]
            (Map.ofList [ "CommandConformance", 0 ]) World.empty { PlayerStateOps.initial with Coins = coinAmount checkCoins }
    Assert.Equal(1, World.getVar "__result" checkedCoins.DebugWorld)
    let takeCoinsMap, takeCoins = sourceCommand (function | Takecoins _ -> true | _ -> false)
    let takenCoins =
        runGeneratedCommand content takeCoinsMap takeCoins World.empty { PlayerStateOps.initial with Coins = coinAmount takeCoins }
    Assert.Equal(0, takenCoins.DebugPlayer.Coins)
    let giveCoinsMap, giveCoins = sourceCommand (function | Givecoins _ -> true | _ -> false)
    let givenCoins =
        runGeneratedCommand content giveCoinsMap giveCoins World.empty { PlayerStateOps.initial with Coins = 0 }
    Assert.Equal(coinAmount giveCoins, givenCoins.DebugPlayer.Coins)

[<Fact>]
let ``generated pokemart opens a mart scene with its resolved source inventory`` () =
    let content = Content()
    let mapId, command = sourceCommand (function | Pokemart _ -> true | _ -> false)
    let mart =
        match command with
        | Pokemart(_, mart) -> mart
        | _ -> failwith "expected pokemart"
    let scene = generatedScene content mapId [| command; End |] [] World.empty { PlayerStateOps.initial with Money = 999_999 }
    let martScene =
        match (scene :> Scene).Update Buttons.none with
        | Push (:? MartScene as martScene) -> martScene
        | other -> failwithf "expected source pokemart to open a MartScene, got %A" other
    (martScene :> Scene).Update { Buttons.none with A = true } |> ignore
    (martScene :> Scene).Update Buttons.none |> ignore
    (martScene :> Scene).Update { Buttons.none with A = true } |> ignore
    match martScene.Mode with
    | BuyQty(item, _, _) -> Assert.Equal(MartsData.byConstant.[mart] |> List.head, item)
    | mode -> failwithf "expected resolved mart inventory selection, got %A" mode

[<Fact>]
let ``direct runtime handlers resolve absent generated checkpoke and name-buffer commands`` () =
    let content = Content()
    let mapId = sourceCommand (fun _ -> true) |> fst
    let eggSpecies =
        sourceCommand (function | Giveegg _ -> true | _ -> false)
        |> snd
        |> function
            | Giveegg(species, _) -> species
            | _ -> failwith "expected giveegg"
    let hasSpecies =
        runGeneratedCommands content mapId [| Checkpoke eggSpecies; Writemem "__result"; End |]
            (Map.ofList [ "CommandConformance", 0 ])
            World.empty
            (battleReadyPlayer eggSpecies)
    Assert.Equal(1, World.getVar "__result" hasSpecies.DebugWorld)
    let lacksSpecies =
        runGeneratedCommands content mapId [| Checkpoke eggSpecies; Writemem "__result"; End |]
            (Map.ofList [ "CommandConformance", 0 ])
            World.empty
            PlayerStateOps.initial
    Assert.Equal(0, World.getVar "__result" lacksSpecies.DebugWorld)

    let item = "POTION"
    let trainerGroup, trainerId = "YOUNGSTER", "JOEY1"
    let names =
        runGeneratedCommands content mapId
            [| Getmonname("STRING_BUFFER_1", eggSpecies)
               Getitemname("STRING_BUFFER_2", item)
               Gettrainername("STRING_BUFFER_3", trainerGroup, trainerId)
               End |]
            (Map.ofList [ "CommandConformance", 0 ])
            World.empty
            PlayerStateOps.initial
    Assert.Equal((Species.byName eggSpecies).Name, World.getBuffer "STRING_BUFFER_1" names.DebugWorld)
    Assert.Equal(Items.byId.[item].Name, World.getBuffer "STRING_BUFFER_2" names.DebugWorld)
    let trainerName =
        Trainers.lookupByName trainerGroup trainerId
        |> Option.map (fun trainer -> trainer.Name)
        |> Option.defaultWith (fun () -> failwith "expected generated trainer")
    Assert.Equal(trainerName, World.getBuffer "STRING_BUFFER_3" names.DebugWorld)

[<Fact>]
let ``generated actor commands update their source objects and complete movement`` () =
    let content = Content()
    let mapId = sourceCommand (fun _ -> true) |> fst

    let disappearMap, disappear = sourceCommand (function | Disappear _ -> true | _ -> false)
    let disappeared =
        runGeneratedCommand content disappearMap disappear World.empty PlayerStateOps.initial
    let disappearingObject =
        match disappear with
        | Disappear obj -> obj
        | _ -> failwith "expected disappear"
    let disappearedIndex =
        OverworldState.objectIndexOf disappearMap disappearingObject
        |> Option.defaultWith (fun () -> failwith "expected generated disappear object")
    Assert.False(disappeared.RuntimeSnapshot.Actors.[disappearedIndex].Visible)

    let appearMap, appear = sourceCommand (function | Appear _ -> true | _ -> false)
    let appeared = runGeneratedCommand content appearMap appear World.empty PlayerStateOps.initial
    let appearingObject =
        match appear with
        | Appear obj -> obj
        | _ -> failwith "expected appear"
    let appearedIndex =
        OverworldState.objectIndexOf appearMap appearingObject
        |> Option.defaultWith (fun () -> failwith "expected generated appear object")
    Assert.True(appeared.RuntimeSnapshot.Actors.[appearedIndex].Visible)

    let turnMap, turn =
        sourceMapCommand (fun mapId ->
            function
            | Turnobject(obj, _) -> OverworldState.objectIndexOf mapId obj |> Option.isSome
            | _ -> false)
    let turned = runGeneratedCommand content turnMap turn World.empty PlayerStateOps.initial
    let turnObject, turnFacing =
        match turn with
        | Turnobject(obj, facing) -> obj, directionOf facing
        | _ -> failwith "expected turnobject"
    let turnIndex =
        OverworldState.objectIndexOf turnMap turnObject
        |> Option.defaultWith (fun () -> failwith "expected generated turnobject object")
    Assert.Equal(turnFacing, turned.RuntimeSnapshot.Actors.[turnIndex].Facing)

    let moveMap, move = sourceCommand (function | Moveobject _ -> true | _ -> false)
    let moved = runGeneratedCommand content moveMap move World.empty PlayerStateOps.initial
    let moveObject, x, y =
        match move with
        | Moveobject(obj, x, y) -> obj, x, y
        | _ -> failwith "expected moveobject"
    let moveIndex =
        OverworldState.objectIndexOf moveMap moveObject
        |> Option.defaultWith (fun () -> failwith "expected generated moveobject object")
    Assert.Equal((x, y), (moved.RuntimeSnapshot.Actors.[moveIndex].CellX, moved.RuntimeSnapshot.Actors.[moveIndex].CellY))

    let movementMap, movement = sourceCommand (function | Applymovement _ -> true | _ -> false)
    let movedActor, movementLabel =
        match movement with
        | Applymovement(obj, label) -> obj, label
        | _ -> failwith "expected applymovement"
    let movementIndex =
        OverworldState.objectIndexOf movementMap movedActor
        |> Option.defaultWith (fun () -> failwith "expected generated movement object")
    let moving =
        generatedScene content movementMap [| movement; Setevent "EVENT_TEST_MOVEMENT_DONE"; End |] [] World.empty PlayerStateOps.initial
    let before = moving.RuntimeSnapshot.Actors.[movementIndex]
    advance moving 500
    let after = moving.RuntimeSnapshot.Actors.[movementIndex]
    Assert.True(World.hasEvent "EVENT_TEST_MOVEMENT_DONE" moving.DebugWorld)
    Assert.NotEqual((before.CellX, before.CellY, before.Facing), (after.CellX, after.CellY, after.Facing))
    Assert.True(not (System.String.IsNullOrEmpty movementLabel))

    let faceMap, lastTalked = sourceCommand (function | Setlasttalked _ -> true | _ -> false)
    let faceObject =
        match lastTalked with
        | Setlasttalked obj -> obj
        | _ -> failwith "expected setlasttalked"
    let faced =
        generatedScene content faceMap [| lastTalked; Faceplayer; End |] [] World.empty PlayerStateOps.initial
    (faced :> Scene).Update Buttons.none |> ignore
    let faceIndex =
        OverworldState.objectIndexOf faceMap faceObject
        |> Option.defaultWith (fun () -> failwith "expected generated faceplayer object")
    let npc = faced.RuntimeSnapshot.Actors.[faceIndex]
    let expectedFace =
        if abs (faced.DebugState.Player.CellX - npc.CellX) >= abs (faced.DebugState.Player.CellY - npc.CellY) then
            if faced.DebugState.Player.CellX >= npc.CellX then Right else Left
        elif faced.DebugState.Player.CellY >= npc.CellY then Down
        else Up
    Assert.Equal(expectedFace, npc.Facing)

    let faceObjectScene =
        generatedScene content faceMap [| Faceobject("PLAYER", faceObject); End |] [] World.empty PlayerStateOps.initial
    (faceObjectScene :> Scene).Update Buttons.none |> ignore
    let target = faceObjectScene.RuntimeSnapshot.Actors.[faceIndex]
    Assert.Equal(
        (if target.CellX >= faceObjectScene.DebugState.Player.CellX then Right else Left),
        faceObjectScene.DebugState.Player.Facing)

[<Fact>]
let ``generated timing and state commands delay continuation or preserve source values`` () =
    let content = Content()
    let delayed predicate eventName =
        let mapId, command = sourceCommand predicate
        let frames =
            match command with
            | Pause frames -> frames
            | Showemote(_, _, frames) -> frames
            | Earthquake(Some frames) -> frames
            | Earthquake None
            | TreeShake -> 30
            | _ -> failwith "expected delayed command"
        let scene =
            generatedScene content mapId [| command; Setevent eventName; End |] [] World.empty PlayerStateOps.initial
        (scene :> Scene).Update Buttons.none |> ignore
        Assert.False(World.hasEvent eventName scene.DebugWorld)
        advance scene (max 0 (frames - 2))
        Assert.False(World.hasEvent eventName scene.DebugWorld)
        advance scene 1
        Assert.True(World.hasEvent eventName scene.DebugWorld)

    delayed (function | Pause frames when frames > 0 -> true | _ -> false) "EVENT_TEST_PAUSE"
    delayed (function | Showemote(_, _, frames) when frames > 0 -> true | _ -> false) "EVENT_TEST_EMOTE"
    delayed (function | Earthquake _ -> true | _ -> false) "EVENT_TEST_EARTHQUAKE"
    let treeMap = sourceCommand (fun _ -> true) |> fst
    let tree =
        generatedScene content treeMap [| TreeShake; Setevent "EVENT_TEST_TREE_SHAKE"; End |] [] World.empty PlayerStateOps.initial
    (tree :> Scene).Update Buttons.none |> ignore
    Assert.False(World.hasEvent "EVENT_TEST_TREE_SHAKE" tree.DebugWorld)
    advance tree 30
    Assert.True(World.hasEvent "EVENT_TEST_TREE_SHAKE" tree.DebugWorld)

    let variableMap, variable = sourceCommand (function | Variablesprite _ -> true | _ -> false)
    let sprite, replacement =
        match variable with
        | Variablesprite(sprite, replacement) -> sprite, replacement
        | _ -> failwith "expected variablesprite"
    let variableScene = runGeneratedCommand content variableMap variable World.empty PlayerStateOps.initial
    Assert.Equal(replacement, World.getBuffer ("__sprite_" + sprite) variableScene.DebugWorld)

    let blackoutMap, blackout = sourceCommand (function | Blackoutmod _ -> true | _ -> false)
    let blackoutName =
        match blackout with
        | Blackoutmod name -> name
        | _ -> failwith "expected blackoutmod"
    let blackoutScene = runGeneratedCommand content blackoutMap blackout World.empty PlayerStateOps.initial
    Assert.Equal(blackoutName, World.getBuffer "__blackout_map" blackoutScene.DebugWorld)
    Assert.Equal(1, World.getVar "wLastSpawnMap" blackoutScene.DebugWorld)

    let dontRestartMap, dontRestart = sourceCommand (function | Dontrestartmapmusic -> true | _ -> false)
    let dontRestartScene = runGeneratedCommand content dontRestartMap dontRestart World.empty PlayerStateOps.initial
    Assert.Equal(1, World.getVar "__dont_restart_map_music" dontRestartScene.DebugWorld)

    let teleportScene =
        generatedScene content treeMap [| TeleportFrom; End |] [] World.empty PlayerStateOps.initial
    (teleportScene :> Scene).Update Buttons.none |> ignore
    Assert.Equal(treeMap, World.getBuffer "__teleport_from_map" teleportScene.DebugWorld)

[<Fact>]
let ``generated audio and reload commands dispatch their source runtime effects`` () =
    let content = Content()
    let sound = RecordingSound()
    let run predicate =
        let mapId, command = sourceCommand predicate
        let scene =
            generatedSceneWithSound sound content mapId [| command; Setevent "EVENT_TEST_EFFECT"; End |] [] World.empty PlayerStateOps.initial
        (scene :> Scene).Update Buttons.none |> ignore
        Assert.True(World.hasEvent "EVENT_TEST_EFFECT" scene.DebugWorld)
        command

    let music = run (function | Playmusic _ -> true | _ -> false)
    match music with
    | Playmusic song -> Assert.NotEmpty(sound.Music)
    | _ -> failwith "expected playmusic"
    let sfx = run (function | Playsound _ -> true | _ -> false)
    match sfx with
    | Playsound _ -> Assert.NotEmpty(sound.Sfx)
    | _ -> failwith "expected playsound"
    run (function | Playmapmusic -> true | _ -> false) |> ignore
    run (function | Musicfadeout -> true | _ -> false) |> ignore
    Assert.Contains("__STOP__", sound.Music)
    [ (function | Reloadmap -> true | _ -> false)
      (function | Refreshmap -> true | _ -> false)
      (function | Newloadmap -> true | _ -> false) ]
    |> List.iter (fun predicate -> run predicate |> ignore)

[<Fact>]
let ``generated check and buffer commands expose source values and end control flow`` () =
    let content = Content()
    let mapId = sourceCommand (fun _ -> true) |> fst
    let checkMap, check = sourceCommand (function | Checktime _ -> true | _ -> false)
    let time =
        match check with
        | Checktime time -> time
        | _ -> failwith "expected checktime"
    let player =
        { PlayerStateOps.initial with
            GameTime =
                match time with
                | "MORN" -> GameTimeState.create 6 0 0 false
                | "DAY" -> GameTimeState.create 12 0 0 false
                | "NITE" -> GameTimeState.create 22 0 0 false
                | _ -> GameTimeState.create 12 0 0 false }
    let timeChecked =
        runGeneratedCommands content checkMap [| check; Writemem "__checktime"; End |]
            (Map.ofList [ "CommandConformance", 0 ]) World.empty player
    Assert.Equal(1, World.getVar "__checktime" timeChecked.DebugWorld)

    let getStringMap, getString = sourceCommand (function | Getstring _ -> true | _ -> false)
    let getNumMap, getNum = sourceCommand (function | Getnum _ -> true | _ -> false)
    let landmark = sourceStdCommand (function | Getcurlandmarkname _ -> true | _ -> false)
    let stringBuffer, stringValue =
        match getString with
        | Getstring(buffer, value) -> buffer, value
        | _ -> failwith "expected getstring"
    let numBuffer, numVar =
        match getNum with
        | Getnum(buffer, variable) -> buffer, variable
        | _ -> failwith "expected getnum"
    let landmarkBuffer =
        match landmark with
        | Getcurlandmarkname buffer -> buffer
        | _ -> failwith "expected getcurlandmarkname"
    let buffered =
        generatedScene content getStringMap [| getString; Getnum(numBuffer, numVar); End |] []
            (World.setVar numVar 42 World.empty) PlayerStateOps.initial
    (buffered :> Scene).Update Buttons.none |> ignore
    Assert.Equal(stringValue.Replace("_", " "), World.getBuffer stringBuffer buffered.DebugWorld)
    Assert.Equal("42", World.getBuffer numBuffer buffered.DebugWorld)
    let landmarkScene =
        generatedScene content getStringMap [| landmark; End |] [] World.empty PlayerStateOps.initial
    (landmarkScene :> Scene).Update Buttons.none |> ignore
    Assert.Equal(getStringMap.Replace("_", " "), World.getBuffer landmarkBuffer landmarkScene.DebugWorld)
    Assert.True(not (System.String.IsNullOrEmpty getNumMap))

    let ended =
        generatedScene content mapId [| End; Setevent "EVENT_TEST_END_FELL_THROUGH"; End |] [] World.empty PlayerStateOps.initial
    (ended :> Scene).Update Buttons.none |> ignore
    Assert.False(World.hasEvent "EVENT_TEST_END_FELL_THROUGH" ended.DebugWorld)

    let endifMap, endif = sourceCommand (function | Endifjustbattled -> true | _ -> false)
    let endedBattle =
        generatedScene content endifMap [| endif; Setevent "EVENT_TEST_BATTLE_FELL_THROUGH"; End |] []
            (World.setVar "__just_battled" 1 World.empty) PlayerStateOps.initial
    (endedBattle :> Scene).Update Buttons.none |> ignore
    Assert.False(World.hasEvent "EVENT_TEST_BATTLE_FELL_THROUGH" endedBattle.DebugWorld)
    Assert.Equal(0, World.getVar "__just_battled" endedBattle.DebugWorld)

[<Fact>]
let ``direct runtime handlers prove warpfacing and doorstate source semantics`` () =
    let content = Content()
    let mapId = sourceCommand (fun _ -> true) |> fst
    let warp =
        generatedScene content mapId [| Warpfacing("UP", "NewBarkTown", 6, 6); End |] [] World.empty PlayerStateOps.initial
    (warp :> Scene).Update Buttons.none |> ignore
    Assert.Equal("NewBarkTown", warp.DebugState.MapId)
    Assert.Equal(Up, warp.DebugState.Player.Facing)

    let door =
        generatedScene content "GoldenrodGym" [| Doorstate(Some 1, Some "OPEN1"); End |] [] World.empty PlayerStateOps.initial
    (door :> Scene).Update Buttons.none |> ignore
    Assert.Equal(0x2duy, Map.blockAt door.DebugState.Map 8 3)
