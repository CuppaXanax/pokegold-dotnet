module PokeGold.Tests.NewBarkRuntimeTests

open Xunit
open PokeGold.Game.Core
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
