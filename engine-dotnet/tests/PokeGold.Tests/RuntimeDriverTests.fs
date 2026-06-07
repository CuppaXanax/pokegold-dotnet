module PokeGold.Tests.RuntimeDriverTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Debug
open PokeGold.Tests.GameDriver

[<Fact>]
let ``driver press advances through the real game tick path`` () =
    let driver = GameDriver()

    driver.Press({ Buttons.none with Start = true })

    Assert.Equal("MainMenuScene", driver.Snapshot.TopScene)
    Assert.True(driver.Trace.Length >= 2)

[<Fact>]
let ``driver can apply typed setup controls and inspect snapshots`` () =
    let driver = GameDriver()

    driver.Apply LoadDebugAzalea

    let ow = driver.Snapshot.Overworld |> Option.defaultWith (fun () -> failwith "expected overworld")
    Assert.Equal("OverworldScene", driver.Snapshot.TopScene)
    Assert.Equal("AzaleaTown", ow.MapId)

[<Fact>]
let ``driver runUntil reports the reached snapshot`` () =
    let driver = GameDriver()
    driver.Apply(Press { Buttons.none with Start = true })

    let snap = driver.RunUntil((fun s -> s.TopScene = "MainMenuScene"), 3)

    Assert.Equal("MainMenuScene", snap.TopScene)
