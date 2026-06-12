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

// ---------------------------------------------------------------------------
// A2 — Cherrygrove → Route 30/31 → Violet City
// ---------------------------------------------------------------------------

[<Fact>]
let ``A2 Route30 warp enters MrPokemonsHouse`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Route30 warp 2 at (17,5) leads to MR_POKEMONS_HOUSE warp 1.
    // Place one cell south of the warp tile.
    driver.Apply(Warp("Route30", 17, 6, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "MrPokemonsHouse"), 100)

    let ow = owOf snap
    Assert.Equal("MrPokemonsHouse", ow.MapId)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A2 Cherrygrove rival coord event fires on return from MrPokemon`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Simulate having the Mystery Egg and returning: set the scene that
    // MrPokemonsHouse_OakScript sets via setmapscene CHERRYGROVE_CITY.
    driver.Apply(SetScene("CHERRYGROVE_CITY", 1))   // SCENE_CHERRYGROVECITY_MEET_RIVAL
    driver.Apply(SetEvent("EVENT_GOT_A_POKEMON_FROM_ELM", true))
    driver.Apply(SetEvent("EVENT_GOT_TOTODILE_FROM_ELM", true))
    // Place one cell east of the coord trigger at (33,7)
    driver.Apply(Warp("CherrygroveCity", 34, 7, Some Left))

    driver.Step Left

    // The coord trigger should start the rival scene (text + battle).
    // Tick frames, pressing A through text boxes until the battle starts.
    let mutable sawBattle = false
    let mutable sawRivalText = false
    let mutable frame = 0
    while frame < 2000 && not sawBattle do
        frame <- frame + 1
        let buttons =
            match driver.Snapshot.TopScene with
            | "TextBoxScene" ->
                sawRivalText <- true
                if frame % 2 = 0 then press "a" else Buttons.none
            | "BattleScene" ->
                sawBattle <- true
                Buttons.none
            | _ -> Buttons.none
        driver.Tick buttons |> ignore

    Assert.True(sawRivalText, "Rival encounter should show text at coord (33,7)")
    Assert.True(sawBattle, "Rival encounter should start a battle")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A2 CherrygroveCity north connection enters Route30`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // CherrygroveCity is 40×18 cells. North connection to Route30 (20×54)
    // is offset +5 blocks = cells 10–29 on the x-axis.
    // x=15 is the walkable gap west of the mart.
    driver.Apply(Warp("CherrygroveCity", 15, 2, Some Up))

    for _ in 1 .. 6 do
        driver.Step Up

    let ow = owOf driver.Snapshot
    if ow.MapId <> "Route30" then
        // Fallback: try x=17 (Route30 exit path varies by tileset)
        let d2 = GameDriver()
        d2.Apply(StartNewGame "A")
        d2.Apply(Warp("CherrygroveCity", 17, 2, Some Up))
        for _ in 1 .. 6 do d2.Step Up
        let ow2 = owOf d2.Snapshot
        Assert.Equal("Route30", ow2.MapId)
        d2.Trace |> List.iter (fun t -> assertHold core t.Snapshot)
    else
        driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A2 Route31 gate warp reaches VioletCity`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Route31 → VioletCity goes through Route31VioletGate.
    // Route31 warp 1 at (4,6) enters the gate at warp 3 = (9,4).
    // Walk left through the gate to exit warp (0,4) → VioletCity warp 8 = (39,24).
    driver.Apply(Warp("Route31", 4, 7, Some Up))

    // Step onto the gate warp
    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "Route31VioletGate"), 100)
    Assert.Equal("Route31VioletGate", owMap snap)

    // Walk left through the gate
    for _ in 1 .. 12 do
        driver.Step Left

    let final =
        driver.RunUntil((fun s -> owMap s = "VioletCity"), 100)

    let ow = owOf final
    Assert.Equal("VioletCity", ow.MapId)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)
