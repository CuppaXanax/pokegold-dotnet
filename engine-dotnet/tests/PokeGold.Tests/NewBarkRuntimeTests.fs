module PokeGold.Tests.NewBarkRuntimeTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Debug
open PokeGold.Game.Save
open PokeGold.Tests.GameDriver
open PokeGold.Tests.RuntimeInvariants

let private press button =
    match button with
    | "start" -> { Buttons.none with Start = true }
    | "a" -> { Buttons.none with A = true }
    | "down" -> { Buttons.none with Down = true }
    | "left" -> { Buttons.none with Left = true }
    | _ -> Buttons.none

let private chooseNewGame (driver: GameDriver) =
    if SaveFile.tryRead() |> Option.isSome then
        driver.Press(press "down")
    driver.Press(press "a")

let private assertTraceCore (driver: GameDriver) =
    driver.Trace
    |> List.iter (fun tick -> assertHold core tick.Snapshot)

let private tickCutscene (driver: GameDriver) frames =
    for frame in 1 .. frames do
        let buttons =
            if driver.Snapshot.TopScene = "TextBoxScene" && frame % 2 = 0 then
                press "a"
            else
                Buttons.none

        driver.Tick buttons |> ignore

let private tickStoryFlow (driver: GameDriver) maxFrames completed =
    let mutable frame = 0
    while frame < maxFrames && not (completed driver.Snapshot) do
        frame <- frame + 1
        let buttons =
            match driver.Snapshot.TopScene with
            | "TextBoxScene"
            | "YesNoScene" when frame % 2 = 0 -> press "a"
            | _ -> Buttons.none

        driver.Tick buttons |> ignore

[<Fact>]
let ``Mom intro drives PokeGear weekday and DST setup through runtime scenes`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(Warp("PlayersHouse1F", 9, 0, Some Down))

    let mutable sawWeekdayScene = false
    let mutable sawYesNoScene = false
    let mutable weekdayStep = 0

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            sawWeekdayScene
            && ow.CanCapture
            && ow.SceneId = 1
            && ow.Player.PhoneContacts |> List.contains "PHONE_MOM"
            && ow.Player.GameTimeIsDst
            && (ow.Vars |> Map.tryFind "VAR_WEEKDAY" = Some ow.Player.GameTimeWeekday)
            && ow.LastTextLabel = Some "InstructionsNextText"
        | None -> false

    let mutable frame = 0
    while frame < 5000 && not (completed driver.Snapshot) do
        frame <- frame + 1
        let buttons =
            match driver.Snapshot.TopScene with
            | "TextBoxScene" ->
                if frame % 2 = 0 then press "a" else Buttons.none
            | "YesNoScene" ->
                sawYesNoScene <- true
                if frame % 2 = 0 then press "a" else Buttons.none
            | "WeekdayScene" ->
                sawWeekdayScene <- true
                match weekdayStep with
                | 0 ->
                    weekdayStep <- 1
                    press "a"
                | 1 ->
                    weekdayStep <- 2
                    Buttons.none
                | 2 ->
                    weekdayStep <- 3
                    press "a"
                | _ -> Buttons.none
            | _ -> Buttons.none

        driver.Tick buttons |> ignore

    assertTraceCore driver
    Assert.True(sawWeekdayScene, "Mom intro should push weekday setup UI")
    Assert.True(sawYesNoScene, "Mom intro should ask DST and phone yes/no prompts")
    Assert.True(completed driver.Snapshot, "Mom intro did not finish with PokeGear, Mom phone contact, and RTC state committed")

[<Fact>]
let ``Elm starter flow gives Totodile and unlocks New Bark exit`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetScene("ELMS_LAB", 1))
    driver.Apply(Warp("ElmsLab", 7, 4, Some Up))

    driver.Talk()

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.Player.PartyCount = 1
            && ow.Events |> List.contains "EVENT_GOT_A_POKEMON_FROM_ELM"
            && ow.Events |> List.contains "EVENT_GOT_TOTODILE_FROM_ELM"
            && ow.Player.PhoneContacts |> List.contains "PHONE_ELM"
            && (ow.Scenes |> Map.tryFind "NEW_BARK_TOWN" = Some 1)
        | None -> false

    tickStoryFlow driver 5000 completed

    let ow = driver.Snapshot.Overworld |> Option.defaultWith (fun () -> failwith "expected overworld")
    let totodileDex = (Species.byName "TOTODILE").Dex

    assertTraceCore driver
    Assert.True(completed driver.Snapshot, "starter script did not set Elm/New Bark state")
    Assert.Contains(totodileDex, ow.Player.PartySpecies)
    Assert.Equal(Some "GotElmsNumberText", ow.LastTextLabel)

    let traceBeforeExitCheck = driver.Trace.Length
    driver.Apply(Warp("NewBarkTown", 2, 8, Some Left))
    driver.Step Left

    let after = driver.Snapshot.Overworld |> Option.defaultWith (fun () -> failwith "expected overworld")
    let newTextAfterWarp =
        driver.Trace
        |> List.skip traceBeforeExitCheck
        |> List.exists (fun tick -> tick.Snapshot.TopScene = "TextBoxScene")

    Assert.Equal("NewBarkTown", after.MapId)
    Assert.Equal(1, after.SceneId)
    Assert.Equal(1, after.Player.CellX)
    Assert.False(newTextAfterWarp, "teacher blocker should not fire after actual starter acquisition")

[<Fact>]
let ``new game reaches PlayersHouse2F through title menu and naming input`` () =
    let driver = GameDriver()

    driver.Press(press "start")
    Assert.Equal("MainMenuScene", driver.Snapshot.TopScene)

    chooseNewGame driver
    Assert.Equal("NamingScene", driver.Snapshot.TopScene)

    driver.Press(press "a")
    driver.Press(press "start")

    let snap = driver.RunUntil((fun s -> s.TopScene = "OverworldScene"), 10)
    let ow = snap.Overworld |> Option.defaultWith (fun () -> failwith "expected overworld")

    Assert.Equal("PlayersHouse2F", ow.MapId)
    Assert.Equal("A", ow.Player.Name)
    Assert.True(ow.EventCount > 0)
    assertTraceCore driver

[<Theory>]
[<InlineData(8)>]
[<InlineData(9)>]
let ``New Bark teacher blocker prevents early west exit without overlap`` triggerY =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(Warp("NewBarkTown", 2, triggerY, Some Left))
    driver.Apply(SetScene("NewBarkTown", 0))
    Assert.Equal(0, (driver.Snapshot.Overworld |> Option.get).SceneId)

    driver.Step Left
    tickCutscene driver 1600

    let snap = driver.Snapshot
    let ow = snap.Overworld |> Option.defaultWith (fun () -> failwith "expected overworld")
    let sawText =
        driver.Trace
        |> List.exists (fun tick -> tick.Snapshot.TopScene = "TextBoxScene")
    let teacherMoved =
        driver.Trace
        |> List.exists (fun tick ->
            match tick.Snapshot.Overworld with
            | Some ow ->
                ow.Actors
                |> List.tryFind (fun actor -> actor.Index = 0)
                |> Option.exists (fun actor -> actor.CellX <> 6 || actor.CellY <> 8)
            | None -> false)

    assertTraceCore driver
    Assert.Equal("NewBarkTown", ow.MapId)
    Assert.True(sawText, "teacher blocker should show text")
    Assert.True(teacherMoved, "teacher actor should move during blocker cutscene")
    Assert.True(ow.Player.CellX > 1, $"player should be brought back from west exit, got x={ow.Player.CellX}")

[<Fact>]
let ``New Bark coord trigger waits until player finishes stepping onto tile`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(Warp("NewBarkTown", 2, 8, Some Left))
    driver.Apply(SetScene("NewBarkTown", 0))

    let firstStepFrame = driver.Tick(press "left")
    let ow = firstStepFrame.Overworld |> Option.defaultWith (fun () -> failwith "expected overworld")

    Assert.Equal("OverworldScene", firstStepFrame.TopScene)
    Assert.True(ow.Player.Moving)
    Assert.Equal(1, ow.Player.CellX)
    Assert.True(Option.isNone ow.LastTextLabel, "coord trigger should not fire at step start")

    driver.Hold(press "left", 15)

    Assert.True(driver.Trace |> List.exists (fun tick -> tick.Snapshot.TopScene = "TextBoxScene"))

[<Fact>]
let ``New Bark teacher does not block after starter scene is set by ROM constant`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(Warp("NewBarkTown", 2, 8, Some Left))
    driver.Apply(SetScene("NEW_BARK_TOWN", 1))
    Assert.Equal(1, (driver.Snapshot.Overworld |> Option.get).SceneId)

    driver.Step Left

    let ow = driver.Snapshot.Overworld |> Option.defaultWith (fun () -> failwith "expected overworld")
    Assert.Equal("OverworldScene", driver.Snapshot.TopScene)
    Assert.Equal(1, ow.Player.CellX)
    Assert.False(driver.Trace |> List.exists (fun tick -> tick.Snapshot.TopScene = "TextBoxScene"))
    assertTraceCore driver
