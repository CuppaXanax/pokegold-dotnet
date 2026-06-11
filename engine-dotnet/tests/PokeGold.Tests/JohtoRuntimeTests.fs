module PokeGold.Tests.JohtoRuntimeTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Debug
open PokeGold.Tests.GameDriver
open PokeGold.Tests.RuntimeInvariants

let private press button =
    match button with
    | "a" -> { Buttons.none with A = true }
    | "b" -> { Buttons.none with B = true }
    | _ -> Buttons.none

[<Fact>]
let ``Goldenrod Flower Shop runtime gate gives SquirtBottle after PlainBadge`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(Warp("GoldenrodFlowerShop", 3, 4, Some Left))
    driver.Apply(SetFlag("ENGINE_PLAINBADGE", true))

    driver.Talk()

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_GOT_SQUIRTBOTTLE"
            && ow.LastTextLabel = Some "GoldenrodFlowerShopTeacherLalalaHavePlentyOfWaterText"
        | None -> false

    let mutable frame = 0
    while frame < 1000 && not (completed driver.Snapshot) do
        frame <- frame + 1
        let buttons =
            match driver.Snapshot.TopScene with
            | "TextBoxScene" ->
                if frame % 2 = 0 then press "a" else Buttons.none
            | _ -> Buttons.none

        driver.Tick buttons |> ignore

    driver.Trace |> List.iter (fun tick -> assertHold core tick.Snapshot)

    let detail =
        match driver.Snapshot.Overworld with
        | Some ow ->
            sprintf
                "top=%s map=%s player=%d,%d text=%A events=[%s] flags=[%s]"
                driver.Snapshot.TopScene
                ow.MapId
                ow.Player.CellX
                ow.Player.CellY
                ow.LastTextLabel
                (String.concat "," ow.Events)
                (String.concat "," ow.EngineFlags)
        | None -> sprintf "top=%s no overworld" driver.Snapshot.TopScene

    Assert.True(completed driver.Snapshot, "PlainBadge-gated Flower Shop script should give SquirtBottle. " + detail)
