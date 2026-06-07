module PokeGold.Tests.RuntimeInvariantTests

open Xunit
open PokeGold.Game.Debug
open PokeGold.Tests.GameDriver
open PokeGold.Tests.RuntimeInvariants

[<Fact>]
let ``core invariants hold for title snapshot`` () =
    let driver = GameDriver()

    assertHold core driver.Snapshot

[<Fact>]
let ``core invariants hold for debug Azalea snapshot`` () =
    let driver = GameDriver()
    driver.Apply LoadDebugAzalea

    assertHold core driver.Snapshot

[<Fact>]
let ``actor-player overlap invariant catches visible overlap`` () =
    let driver = GameDriver()
    driver.Apply LoadDebugAzalea
    let snapshot = driver.Snapshot
    let ow = snapshot.Overworld |> Option.defaultWith (fun () -> failwith "expected overworld")
    let actor =
        ow.Actors
        |> List.find (fun actor -> actor.Visible)
    let overlapping =
        { actor with
            CellX = ow.Player.CellX
            CellY = ow.Player.CellY }
    let snapshot' =
        { snapshot with
            Overworld = Some { ow with Actors = overlapping :: (ow.Actors |> List.filter (fun a -> a.Index <> actor.Index)) } }

    let failures = noVisibleActorOverlapsPlayer snapshot'

    Assert.Single failures |> ignore
