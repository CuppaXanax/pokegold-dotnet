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

// ---------------------------------------------------------------------------
// A1 — PlayersHouse2F → New Bark → Route 29 → Cherrygrove
// ---------------------------------------------------------------------------

let private owMap (s: RuntimeSnapshot) =
    s.Overworld |> Option.map (fun ow -> ow.MapId) |> Option.defaultValue ""

let private owOf (s: RuntimeSnapshot) =
    s.Overworld |> Option.defaultWith (fun () -> failwith "expected overworld")

[<Fact>]
let ``A1 bedroom stairs warp loads PlayersHouse1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Place one cell south of the stairway warp at (7,0)
    driver.Apply(Warp("PlayersHouse2F", 7, 1, Some Up))

    // Step up onto the warp tile
    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "PlayersHouse1F"), 100)

    let ow = owOf snap
    Assert.Equal("PlayersHouse1F", ow.MapId)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A1 PlayersHouse1F door warp reaches NewBarkTown`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Skip the Mom intro cutscene (scene 0 = MeetMom, scene 1 = noop)
    driver.Apply(SetScene("PLAYERS_HOUSE_1F", 1))
    // Door warps are at (6,7) and (7,7); place one cell north of (7,7)
    driver.Apply(Warp("PlayersHouse1F", 7, 6, Some Down))

    driver.Step Down

    let snap =
        driver.RunUntil((fun s -> owMap s = "NewBarkTown"), 100)

    let ow = owOf snap
    Assert.Equal("NewBarkTown", ow.MapId)
    // Destination is NewBarkTown warp 2 at (13,5)
    Assert.Equal(13, ow.Player.CellX)
    Assert.Equal(5, ow.Player.CellY)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A1 NewBarkTown west connection enters Route29`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Scene 1 to avoid the teacher blocker; walk from near the west edge
    driver.Apply(Warp("NewBarkTown", 2, 8, Some Left))
    driver.Apply(SetScene("NEW_BARK_TOWN", 1))

    // Walk left — need to cross x=0 and step off the map
    for _ in 1 .. 6 do
        driver.Step Left

    let snap =
        driver.RunUntil((fun s -> owMap s = "Route29"), 200)

    let ow = owOf snap
    Assert.Equal("Route29", ow.MapId)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A1 Route29 west connection enters CherrygroveCity`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Route29 is 60×18 cells; walk left from near the west edge.
    // y=6 is the main path just below the sign at (3,5).
    driver.Apply(Warp("Route29", 3, 6, Some Left))

    for _ in 1 .. 8 do
        driver.Step Left

    let ow = owOf driver.Snapshot
    Assert.Equal("CherrygroveCity", ow.MapId)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A1 Route29 east connection returns to NewBarkTown`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Route29 is 60×18 cells; place near the east edge (x=60 is the boundary)
    driver.Apply(Warp("Route29", 57, 8, Some Right))

    for _ in 1 .. 8 do
        driver.Step Right

    let snap =
        driver.RunUntil((fun s -> owMap s = "NewBarkTown"), 200)

    let ow = owOf snap
    Assert.Equal("NewBarkTown", ow.MapId)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)
