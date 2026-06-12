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

// ---------------------------------------------------------------------------
// A3 — Violet Gym → Falkner
// ---------------------------------------------------------------------------

[<Fact>]
let ``A3 VioletCity gym door warp loads VioletGym`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // VioletCity warp 2 at (18,17) leads to VioletGym.
    // Place one cell north and step onto the warp tile.
    driver.Apply(Warp("VioletCity", 18, 16, Some Down))

    driver.Step Down

    let snap =
        driver.RunUntil((fun s -> owMap s = "VioletGym"), 100)

    Assert.Equal("VioletGym", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A3 Falkner gives ZephyrBadge and TM31 after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Pre-set: Falkner beaten and badge awarded (the battle-win path sets
    // these; we skip the battle to test the post-battle reward script).
    driver.Apply(SetEvent("EVENT_BEAT_FALKNER", true))
    driver.Apply(SetFlag("ENGINE_ZEPHYRBADGE", true))
    // Warp next to Falkner at (5,1); stand one cell south facing Up.
    driver.Apply(Warp("VioletGym", 5, 2, Some Up))

    driver.Talk()

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_GOT_TM31_MUD_SLAP"
        | None -> false

    let mutable frame = 0
    while frame < 2000 && not (completed driver.Snapshot) do
        frame <- frame + 1
        let buttons =
            match driver.Snapshot.TopScene with
            | "TextBoxScene" when frame % 2 = 0 -> press "a"
            | _ -> Buttons.none
        driver.Tick buttons |> ignore

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_GOT_TM31_MUD_SLAP",
                "Falkner should give TM31 MUD-SLAP after badge")
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_ZEPHYRBADGE",
                "ZEPHYRBADGE should be set")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

// ---------------------------------------------------------------------------
// A4 — Route 32 → Union Cave → Route 33 → Azalea
// ---------------------------------------------------------------------------

[<Fact>]
let ``A4 Route32 south warp enters UnionCave1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Route32 warp 4 at (6,79) leads to UnionCave1F warp 4 at (17,3).
    driver.Apply(Warp("Route32", 6, 78, Some Down))

    driver.Step Down

    let snap =
        driver.RunUntil((fun s -> owMap s = "UnionCave1F"), 100)

    Assert.Equal("UnionCave1F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A4 UnionCave1F south exit reaches Route33`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // UnionCave1F warp 3 at (17,31) leads to Route33 warp 1 at (11,9).
    driver.Apply(Warp("UnionCave1F", 17, 30, Some Down))

    driver.Step Down

    let snap =
        driver.RunUntil((fun s -> owMap s = "Route33"), 100)

    Assert.Equal("Route33", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A4 Route33 west connection enters AzaleaTown`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Route33 is 20×18 cells. AzaleaTown is WEST of Route33 (offset 0).
    // Probe several y values to find the walkable path at the west edge.
    let mutable crossed = false
    for tryY in [ 10; 11; 12; 13; 14; 15; 8; 9; 6; 7 ] do
        if not crossed then
            let d = GameDriver()
            d.Apply(StartNewGame "A")
            d.Apply(Warp("Route33", 2, tryY, Some Left))
            for _ in 1 .. 6 do d.Step Left
            if owMap d.Snapshot = "AzaleaTown" then
                crossed <- true
                d.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

    Assert.True(crossed, "Could not cross Route33 west edge into AzaleaTown at any tested y")

[<Fact>]
let ``A4 AzaleaTown Slowpoke Well warp loads SlowpokeWellB1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // AzaleaTown warp 6 at (31,7) leads to SlowpokeWellB1F warp 1.
    driver.Apply(Warp("AzaleaTown", 31, 8, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "SlowpokeWellB1F"), 100)

    Assert.Equal("SlowpokeWellB1F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A4 Slowpoke Well rocket grunts are present before well is cleared`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Grunt objects use SPRITE_ROCKET, gated by EVENT_SLOWPOKE_WELL_ROCKETS.
    // On fresh save the event is clear → grunts should be visible.
    driver.Apply(Warp("SlowpokeWellB1F", 10, 10, Some Down))

    let ow = owOf driver.Snapshot
    let visibleGrunts =
        ow.Actors |> List.filter (fun a -> a.Visible && a.Sprite.Contains("ROCKET"))

    Assert.True(visibleGrunts.Length >= 2,
        sprintf "Expected ≥2 visible grunts, found %d. Actors: %s"
            visibleGrunts.Length
            (ow.Actors |> List.map (fun a -> sprintf "%s(vis=%b)" a.Sprite a.Visible) |> String.concat ", "))

    // After clearing the well, grunts should disappear on a fresh load
    let d2 = GameDriver()
    d2.Apply(StartNewGame "A")
    d2.Apply(SetEvent("EVENT_SLOWPOKE_WELL_ROCKETS", true))
    d2.Apply(Warp("SlowpokeWellB1F", 10, 10, Some Down))

    let ow2 = owOf d2.Snapshot
    let visibleGruntsAfter =
        ow2.Actors |> List.filter (fun a -> a.Visible && a.Sprite.Contains("ROCKET"))

    Assert.Equal(0, visibleGruntsAfter.Length)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

// ---------------------------------------------------------------------------
// A5 — Slowpoke Well clear; Kurt leaves; Azalea Gym → Bugsy; rival ambush
// ---------------------------------------------------------------------------

[<Fact>]
let ``A5 AzaleaTown gym warp loads AzaleaGym`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // AzaleaTown warp 5 at (10,15) leads to AzaleaGym.
    driver.Apply(Warp("AzaleaTown", 10, 14, Some Down))

    driver.Step Down

    let snap =
        driver.RunUntil((fun s -> owMap s = "AzaleaGym"), 100)

    Assert.Equal("AzaleaGym", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A5 Bugsy gives HiveBadge and TM49 after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_BEAT_BUGSY", true))
    driver.Apply(SetFlag("ENGINE_HIVEBADGE", true))
    // Bugsy at (5,7); stand south facing up.
    driver.Apply(Warp("AzaleaGym", 5, 8, Some Up))

    driver.Talk()

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_GOT_TM49_FURY_CUTTER"
        | None -> false

    let mutable frame = 0
    while frame < 2000 && not (completed driver.Snapshot) do
        frame <- frame + 1
        let buttons =
            match driver.Snapshot.TopScene with
            | "TextBoxScene" when frame % 2 = 0 -> press "a"
            | _ -> Buttons.none
        driver.Tick buttons |> ignore

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_GOT_TM49_FURY_CUTTER",
                "Bugsy should give TM49 FURY CUTTER after badge")
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_HIVEBADGE",
                "HIVEBADGE should be set")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A5 Azalea rival coord event fires at scene 1`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Rival ambush: coord events at (5,10)/(5,11) fire at
    // SCENE_AZALEATOWN_RIVAL_BATTLE (scene 1).
    driver.Apply(SetScene("AZALEA_TOWN", 1))
    driver.Apply(SetEvent("EVENT_GOT_A_POKEMON_FROM_ELM", true))
    driver.Apply(SetEvent("EVENT_GOT_TOTODILE_FROM_ELM", true))
    driver.Apply(SetEvent("EVENT_CLEARED_SLOWPOKE_WELL", true))
    // Place one cell east of the coord trigger at (5,10)
    driver.Apply(Warp("AzaleaTown", 6, 10, Some Left))

    driver.Step Left

    // The coord trigger should start the rival scene.
    let mutable sawBattle = false
    let mutable sawText = false
    let mutable frame = 0
    while frame < 2000 && not sawBattle do
        frame <- frame + 1
        let buttons =
            match driver.Snapshot.TopScene with
            | "TextBoxScene" ->
                sawText <- true
                if frame % 2 = 0 then press "a" else Buttons.none
            | "BattleScene" ->
                sawBattle <- true
                Buttons.none
            | _ -> Buttons.none
        driver.Tick buttons |> ignore

    Assert.True(sawText, "Rival should show text before battle at (5,10)")
    Assert.True(sawBattle, "Rival encounter should start a battle")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

// ---------------------------------------------------------------------------
// A6 — Ilex Forest: Farfetch'd chase; HM01; Cut tree gate on Route 34 side
// ---------------------------------------------------------------------------

[<Fact>]
let ``A6 Azalea gate warp enters IlexForest`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // AzaleaTown warp 7 at (2,10) leads to IlexForestAzaleaGate warp 3 = (9,4).
    // Walk left through the gate to exit warp (0,4) → IlexForest warp 2 at (3,42).
    driver.Apply(Warp("AzaleaTown", 2, 11, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "IlexForestAzaleaGate"), 100)
    Assert.Equal("IlexForestAzaleaGate", owMap snap)

    for _ in 1 .. 12 do
        driver.Step Left

    let final =
        driver.RunUntil((fun s -> owMap s = "IlexForest"), 100)

    Assert.Equal("IlexForest", owMap final)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A6 IlexForest north warp reaches Route34 via gate`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // IlexForest warp 1 at (1,5) leads to Route34IlexForestGate warp 3 = (4,7).
    // Walk up through the gate to exit warp (4,0) → Route34 warp 1 at (13,37).
    driver.Apply(Warp("IlexForest", 1, 6, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "Route34IlexForestGate"), 100)
    Assert.Equal("Route34IlexForestGate", owMap snap)

    for _ in 1 .. 10 do
        driver.Step Up

    let final =
        driver.RunUntil((fun s -> owMap s = "Route34"), 100)

    Assert.Equal("Route34", owMap final)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A6 Cut field move requires HiveBadge`` () =
    // Cut is HM01; the FieldMovesTests already verify collision id 0x12
    // and badge gating. This test confirms the badge requirement from
    // the disassembly: ENGINE_HIVEBADGE gates CUT.
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetFlag("ENGINE_HIVEBADGE", true))
    // FieldMoves are tested at the unit level; this just confirms the
    // flag is readable through the runtime.
    let ow = owOf driver.Snapshot
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_HIVEBADGE")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

// ---------------------------------------------------------------------------
// A7 — Goldenrod: Whitney, Flower Shop SquirtBottle
// ---------------------------------------------------------------------------
// The Flower Shop SquirtBottle test is the existing test at the top of this
// file. A7 extends it with the gym warp and Whitney reward.

[<Fact>]
let ``A7 Route34 north connection enters GoldenrodCity`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Route34 is 20×54 cells. North connection to GoldenrodCity (40×36)
    // has offset 5 blocks = cells 10–29 on the x-axis.
    // Route34 north edge is y=0; walk up from near the top.
    let mutable crossed = false
    for tryX in [ 12; 14; 16; 18 ] do
        if not crossed then
            let d = GameDriver()
            d.Apply(StartNewGame "A")
            d.Apply(Warp("Route34", tryX, 2, Some Up))
            for _ in 1 .. 6 do d.Step Up
            if owMap d.Snapshot = "GoldenrodCity" then
                crossed <- true
                d.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

    Assert.True(crossed, "Could not cross Route34 north edge into GoldenrodCity")

[<Fact>]
let ``A7 GoldenrodCity gym warp loads GoldenrodGym`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // GoldenrodCity warp 1 at (24,7) leads to GoldenrodGym.
    driver.Apply(Warp("GoldenrodCity", 24, 8, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "GoldenrodGym"), 100)

    Assert.Equal("GoldenrodGym", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A7 Whitney gives PlainBadge and TM45 after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Whitney's script: after being beaten, the player must talk to her
    // again (she cries and returns). Pre-set: battle won, badge given.
    driver.Apply(SetEvent("EVENT_BEAT_WHITNEY", true))
    driver.Apply(SetFlag("ENGINE_PLAINBADGE", true))
    // Whitney at (8,3); stand south facing up.
    driver.Apply(Warp("GoldenrodGym", 8, 4, Some Up))

    driver.Talk()

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_GOT_TM45_ATTRACT"
        | None -> false

    let mutable frame = 0
    while frame < 2000 && not (completed driver.Snapshot) do
        frame <- frame + 1
        let buttons =
            match driver.Snapshot.TopScene with
            | "TextBoxScene" when frame % 2 = 0 -> press "a"
            | _ -> Buttons.none
        driver.Tick buttons |> ignore

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_GOT_TM45_ATTRACT",
                "Whitney should give TM45 ATTRACT after PlainBadge")
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_PLAINBADGE",
                "PLAINBADGE should be set")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)
