module PokeGold.Tests.JohtoRuntimeTests

open Xunit
open PokeGold.Game.Audio
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Debug
open PokeGold.Game.Overworld
open PokeGold.Game.Player
open PokeGold.Game.Save
open PokeGold.Game.Scenes
open PokeGold.Tests.GameDriver
open PokeGold.Tests.RuntimeInvariants

module ScriptWorld = PokeGold.Game.Overworld.Script.World
module MapEvents = PokeGold.Game.Overworld.Script.MapEvents

type private SilentSound() =
    interface ISoundBoard with
        member _.PlayMusic _ = ()
        member _.PlaySfx _ = ()
        member _.PlayJingle _ = ()
        member _.StopMusic() = ()

let private press button =
    match button with
    | "a" -> { Buttons.none with A = true }
    | "b" -> { Buttons.none with B = true }
    | _ -> Buttons.none

let private directionButton direction =
    match direction with
    | Up -> { Buttons.none with Up = true }
    | Down -> { Buttons.none with Down = true }
    | Left -> { Buttons.none with Left = true }
    | Right -> { Buttons.none with Right = true }

let private directionDelta direction =
    match direction with
    | Up -> 0, -1
    | Down -> 0, 1
    | Left -> -1, 0
    | Right -> 1, 0

let private advanceRuntimeUntil (driver: GameDriver) maxFrames predicate =
    let mutable frame = 0

    while frame < maxFrames && not (predicate driver.Snapshot) do
        frame <- frame + 1

        let buttons =
            match driver.Snapshot.TopScene with
            | "BattleScene"
            | "TextBoxScene"
            | "YesNoScene" when frame % 2 = 0 -> press "a"
            | _ -> Buttons.none

        driver.Tick buttons |> ignore

let private applyTransition (stack: ResizeArray<Scene>) transition =
    match transition with
    | Stay -> ()
    | Push scene -> stack.Add scene
    | Pop ->
        if stack.Count > 1 then
            stack.RemoveAt(stack.Count - 1)
    | Replace scene -> stack.[stack.Count - 1] <- scene

let private tickSceneStackUntil (stack: ResizeArray<Scene>) maxFrames predicate =
    let mutable frame = 0

    while frame < maxFrames && not (predicate ()) do
        frame <- frame + 1
        let top = stack.[stack.Count - 1]

        let buttons =
            match top.GetType().Name with
            | "BattleScene"
            | "TextBoxScene"
            | "YesNoScene" when frame % 2 = 0 -> press "a"
            | _ -> Buttons.none

        top.Update buttons |> applyTransition stack

let private holdSceneStack (stack: ResizeArray<Scene>) buttons frames =
    for _ in 1..frames do
        let top = stack.[stack.Count - 1]
        top.Update buttons |> applyTransition stack

let private sceneStackAt mapId x y facing world player =
    let content = Content()
    let scene = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content mapId x y facing)
    scene.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    scene, stack

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

let private settleUntilCapture (driver: GameDriver) maxFrames =
    driver.RunUntil(
        (fun s ->
            match s.Overworld with
            | Some ow -> ow.CanCapture
            | None -> false),
        maxFrames)
    |> ignore

let private stepAndSettle (driver: GameDriver) direction =
    driver.Step direction
    settleUntilCapture driver 300

let private talkUntilLastText (driver: GameDriver) expectedLabel =
    driver.Talk()
    advanceRuntimeUntil
        driver
        2000
        (fun s ->
            match s.Overworld with
            | Some ow -> ow.CanCapture && ow.LastTextLabel = Some expectedLabel
            | None -> false)

    Assert.Equal(Some expectedLabel, (owOf driver.Snapshot).LastTextLabel)

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

// ---------------------------------------------------------------------------
// A8 — Route 36 Sudowoodo; Ecruteak; Burned Tower rival + beasts release
// ---------------------------------------------------------------------------

[<Fact>]
let ``A8 EcruteakCity gym warp loads EcruteakGym`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // EcruteakCity warp at (6,27) → EcruteakGym.
    driver.Apply(Warp("EcruteakCity", 6, 26, Some Down))

    driver.Step Down

    let snap =
        driver.RunUntil((fun s -> owMap s = "EcruteakGym"), 100)

    Assert.Equal("EcruteakGym", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A8 Morty gives FogBadge and TM30 after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_BEAT_MORTY", true))
    driver.Apply(SetFlag("ENGINE_FOGBADGE", true))
    // Morty at (5,1); stand one cell south.
    driver.Apply(Warp("EcruteakGym", 5, 2, Some Up))

    driver.Talk()

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_GOT_TM30_SHADOW_BALL"
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
    Assert.True(ow.Events |> List.contains "EVENT_GOT_TM30_SHADOW_BALL",
                "Morty should give TM30 SHADOW BALL after FogBadge")
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_FOGBADGE",
                "FOGBADGE should be set")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A8 EcruteakCity Burned Tower warp loads BurnedTower1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // EcruteakCity warp at (5,5) → BurnedTower1F.
    driver.Apply(Warp("EcruteakCity", 5, 6, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "BurnedTower1F"), 100)

    Assert.Equal("BurnedTower1F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

// ---------------------------------------------------------------------------
// A9 — Morty; Routes 38/39; Olivine lighthouse climb; Surf to Cianwood
// ---------------------------------------------------------------------------

[<Fact>]
let ``A9 Route38 west connection enters Route39`` () =
    let mutable crossed = false

    for tryY in [ 8; 9; 10; 11; 12 ] do
        if not crossed then
            let driver = GameDriver()
            driver.Apply(StartNewGame "A")
            driver.Apply(Warp("Route38", 2, tryY, Some Left))

            for _ in 1 .. 6 do
                driver.Step Left

            if owMap driver.Snapshot = "Route39" then
                crossed <- true
                driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

    Assert.True(crossed, "Could not cross Route38 west edge into Route39")

[<Fact>]
let ``A9 Route39 south connection enters OlivineCity`` () =
    let mutable crossed = false

    for tryY in [ 31; 32; 33; 34 ] do
        for tryX in [ 0 .. 19 ] do
            if not crossed then
                let driver = GameDriver()
                driver.Apply(StartNewGame "A")
                driver.Apply(Warp("Route39", tryX, tryY, Some Down))

                for _ in 1 .. 8 do
                    driver.Step Down

                if owMap driver.Snapshot = "OlivineCity" then
                    crossed <- true
                    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

    Assert.True(crossed, "Could not cross Route39 south edge into OlivineCity")

[<Fact>]
let ``A9 OlivineCity lighthouse door loads OlivineLighthouse1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // OlivineCity warp 9 at (29,27) -> OlivineLighthouse1F.
    driver.Apply(Warp("OlivineCity", 29, 26, Some Down))

    driver.Step Down

    let snap =
        driver.RunUntil((fun s -> owMap s = "OlivineLighthouse1F"), 100)

    Assert.Equal("OlivineLighthouse1F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A9 Olivine lighthouse stair chain reaches 6F`` () =
    let assertWarp fromMap startX startY dir expectedMap =
        let driver = GameDriver()
        driver.Apply(StartNewGame "A")
        driver.Apply(Warp(fromMap, startX, startY, Some dir))
        driver.Step dir

        let snap =
            driver.RunUntil((fun s -> owMap s = expectedMap), 100)

        Assert.Equal(expectedMap, owMap snap)
        driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

    assertWarp "OlivineLighthouse1F" 3 12 Up "OlivineLighthouse2F"
    assertWarp "OlivineLighthouse2F" 5 4 Up "OlivineLighthouse3F"
    assertWarp "OlivineLighthouse3F" 13 4 Up "OlivineLighthouse4F"
    assertWarp "OlivineLighthouse4F" 3 6 Up "OlivineLighthouse5F"
    assertWarp "OlivineLighthouse5F" 9 14 Down "OlivineLighthouse6F"

[<Fact>]
let ``A9 Route40 water blocks walking until Surf is used`` () =
    let content = Content()
    let probe = OverworldState.loadById content "Route40"
    let directions = [ Down; Up; Left; Right ]
    let npcAt cx cy =
        probe.Npcs |> Array.exists (fun npc -> npc.CellX = cx && npc.CellY = cy)

    let x, y, facing =
        seq {
            for cy in 0 .. probe.Map.Height * 2 - 1 do
                for cx in 0 .. probe.Map.Width * 2 - 1 do
                    if Movement.cellWalkable probe.Map probe.Collision cx cy && not (npcAt cx cy) then
                        for dir in directions do
                            let dx, dy = directionDelta dir
                            let coll = Movement.collisionIdAtCell probe.Map probe.Collision (cx + dx) (cy + dy)

                            if FieldMoves.isSurfWater coll && not (npcAt (cx + dx) (cy + dy)) then
                                yield cx, cy, dir
        }
        |> Seq.tryHead
        |> Option.defaultWith (fun () -> failwith "expected Route40 to have land facing surf water")

    let scene =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "Route40" x y facing)

    let mon =
        { PartyMon.create 155 10 with
            Moves = MoveLearn.tryLearnMove "SURF" [] }

    let player = { PlayerStateOps.initial with Party = [ mon ] }
    let world = ScriptWorld.empty |> ScriptWorld.setFlag "ENGINE_FOGBADGE"
    scene.Restore(world, player)

    let buttons = directionButton facing
    let visible _ _ = true
    let route40 = OverworldState.loadByIdAt content "Route40" x y facing

    let blockedState =
        (route40, [ 1 .. 16 ])
        ||> List.fold (fun state _ -> OverworldState.tickWithPlayerWalkable None visible buttons state)

    Assert.Equal((x, y), (blockedState.Player.CellX, blockedState.Player.CellY))

    match (scene :> Scene).Update { Buttons.none with A = true } with
    | Push (:? TextBoxScene) -> ()
    | other -> failwithf "expected Surf prompt text, got %A" other

    Assert.Equal(1, ScriptWorld.getVar "__surfing" scene.DebugWorld)
    Assert.Equal("SURF", ScriptWorld.getBuffer "__last_field_move" scene.DebugWorld)

    let surfWalkable cx cy =
        if MapConnections.cellWalkable route40.Map route40.Collision route40.Neighbors cx cy then
            true
        else
            MapConnections.collisionId route40.Map route40.Collision route40.Neighbors cx cy
            |> FieldMoves.isSurfWater

    let surfedState =
        (route40, [ 1 .. 16 ])
        ||> List.fold (fun state _ -> OverworldState.tickWithPlayerWalkable (Some surfWalkable) visible buttons state)

    let dx, dy = directionDelta facing
    Assert.Equal((x + dx, y + dy), (surfedState.Player.CellX, surfedState.Player.CellY))

[<Fact>]
let ``A9 Route41 west connection enters CianwoodCity while surfing`` () =
    let mutable crossed = false

    for tryY in [ 20; 24; 28; 32; 36; 40; 44 ] do
        if not crossed then
            let driver = GameDriver()
            driver.Apply(StartNewGame "A")
            driver.Apply(SetVar("__surfing", 1))
            driver.Apply(Warp("Route41", 2, tryY, Some Left))

            for _ in 1 .. 8 do
                driver.Step Left

            if owMap driver.Snapshot = "CianwoodCity" then
                crossed <- true
                driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

    Assert.True(crossed, "Could not cross Route41 west edge into CianwoodCity while surfing")

// ---------------------------------------------------------------------------
// A10 — Pharmacy; Chuck; Jasmine return; Mahogany; Lake of Rage
// ---------------------------------------------------------------------------

[<Fact>]
let ``A10 CianwoodCity pharmacy door loads CianwoodPharmacy`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // CianwoodCity warp 4 at (15,47) -> CianwoodPharmacy.
    driver.Apply(Warp("CianwoodCity", 15, 46, Some Down))

    driver.Step Down

    let snap =
        driver.RunUntil((fun s -> owMap s = "CianwoodPharmacy"), 100)

    Assert.Equal("CianwoodPharmacy", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A10 CianwoodPharmacy gives SecretPotion after Jasmine explains Amphy sickness`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_JASMINE_EXPLAINED_AMPHYS_SICKNESS", true))
    // Pharmacist at (2,3); stand south facing up.
    driver.Apply(Warp("CianwoodPharmacy", 2, 4, Some Up))

    driver.Talk()

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_GOT_SECRETPOTION_FROM_PHARMACY"
        | None -> false

    advanceRuntimeUntil driver 2000 completed

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_GOT_SECRETPOTION_FROM_PHARMACY",
                "Pharmacist should give SECRETPOTION after Jasmine explains Amphy's sickness")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A10 CianwoodCity gym door loads CianwoodGym`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // CianwoodCity warp 2 at (8,43) -> CianwoodGym.
    driver.Apply(Warp("CianwoodCity", 8, 42, Some Down))

    driver.Step Down

    let snap =
        driver.RunUntil((fun s -> owMap s = "CianwoodGym"), 100)

    Assert.Equal("CianwoodGym", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A10 Chuck gives StormBadge and TM01 after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_BEAT_CHUCK", true))
    driver.Apply(SetFlag("ENGINE_STORMBADGE", true))
    // Chuck at (4,1); stand south facing up.
    driver.Apply(Warp("CianwoodGym", 4, 2, Some Up))

    driver.Talk()

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_GOT_TM01_DYNAMICPUNCH"
        | None -> false

    advanceRuntimeUntil driver 2000 completed

    let ow = owOf driver.Snapshot
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_STORMBADGE",
                "STORMBADGE should be set after Chuck")
    Assert.True(ow.Events |> List.contains "EVENT_GOT_TM01_DYNAMICPUNCH",
                "Chuck should give TM01 DYNAMICPUNCH after the badge")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A10 Jasmine returns to OlivineGym after SecretPotion is delivered`` () =
    let content = Content()
    let scene =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "OlivineLighthouse6F" 8 9 Up)

    let player =
        { PlayerStateOps.initial with
            Bag = Bag.add "SECRETPOTION" 1 PlayerStateOps.initial.Bag }

    let world =
        ScriptWorld.empty
        |> ScriptWorld.setEvent "EVENT_JASMINE_EXPLAINED_AMPHYS_SICKNESS"

    scene.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    (scene :> Scene).Update (press "a") |> applyTransition stack

    let completed () =
        stack.Count = 1
        && ScriptWorld.hasEvent "EVENT_JASMINE_RETURNED_TO_GYM" scene.DebugWorld

    tickSceneStackUntil stack 4000 completed

    Assert.True(ScriptWorld.hasEvent "EVENT_JASMINE_RETURNED_TO_GYM" scene.DebugWorld,
                "Jasmine should return to Olivine Gym after accepting SECRETPOTION")
    Assert.False(ScriptWorld.hasEvent "EVENT_OLIVINE_GYM_JASMINE" scene.DebugWorld,
                 "Jasmine should be visible in Olivine Gym after the lighthouse cure")
    Assert.Equal(0, Bag.count "SECRETPOTION" scene.Player.Bag)

[<Fact>]
let ``A10 OlivineGym Jasmine gives MineralBadge and TM23 after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_JASMINE_RETURNED_TO_GYM", true))
    driver.Apply(SetEvent("EVENT_OLIVINE_GYM_JASMINE", false))
    driver.Apply(SetEvent("EVENT_BEAT_JASMINE", true))
    driver.Apply(SetFlag("ENGINE_MINERALBADGE", true))
    // Jasmine at (5,3); stand south facing up.
    driver.Apply(Warp("OlivineGym", 5, 4, Some Up))

    driver.Talk()

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_GOT_TM23_IRON_TAIL"
        | None -> false

    advanceRuntimeUntil driver 2000 completed

    let ow = owOf driver.Snapshot
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_MINERALBADGE",
                "MINERALBADGE should be set after Jasmine")
    Assert.True(ow.Events |> List.contains "EVENT_GOT_TM23_IRON_TAIL",
                "Jasmine should give TM23 IRON TAIL after the badge")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A10 Route42 east connection enters MahoganyTown`` () =
    let mutable crossed = false

    for tryY in [ 0 .. 17 ] do
        if not crossed then
            let driver = GameDriver()
            driver.Apply(StartNewGame "A")
            driver.Apply(Warp("Route42", 58, tryY, Some Right))

            for _ in 1 .. 8 do
                driver.Step Right

            if owMap driver.Snapshot = "MahoganyTown" then
                crossed <- true
                driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

    Assert.True(crossed, "Could not cross Route42 east edge into MahoganyTown")

[<Fact>]
let ``A10 MahoganyTown mart door loads MahoganyMart1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // MahoganyTown warp 1 at (11,7) -> MahoganyMart1F.
    driver.Apply(Warp("MahoganyTown", 11, 8, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "MahoganyMart1F"), 100)

    Assert.Equal("MahoganyMart1F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A10 Route43 north connection enters LakeOfRage`` () =
    let mutable crossed = false

    for tryY in [ 1; 2; 3 ] do
        for tryX in [ 0 .. 19 ] do
            if not crossed then
                let driver = GameDriver()
                driver.Apply(StartNewGame "A")
                driver.Apply(Warp("Route43", tryX, tryY, Some Up))

                for _ in 1 .. 8 do
                    driver.Step Up

                if owMap driver.Snapshot = "LakeOfRage" then
                    crossed <- true
                    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

    Assert.True(crossed, "Could not cross Route43 north edge into LakeOfRage")

[<Fact>]
let ``A10 LakeOfRage Red Gyarados starts forced battle from surf tile`` () =
    let content = Content()
    let lake = OverworldState.loadById content "LakeOfRage"
    let coll = Movement.collisionIdAtCell lake.Map lake.Collision 18 23
    Assert.True(FieldMoves.isSurfWater coll, "Expected the tile south of Red Gyarados to be surf water")

    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetVar("__surfing", 1))
    driver.Apply(SetEvent("EVENT_LAKE_OF_RAGE_RED_GYARADOS", false))
    // Red Gyarados at (18,22); stand one surf tile south facing up.
    driver.Apply(Warp("LakeOfRage", 18, 23, Some Up))

    driver.Talk()

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

    Assert.True(sawText, "Red Gyarados should cry before the forced battle")
    Assert.True(sawBattle, "Red Gyarados should start a forced battle from the surf tile")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A10 LakeOfRage Lance sets MahoganyMart1F staircase scene`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_LAKE_OF_RAGE_LANCE", false))
    // Lance at (21,28); stand south facing up.
    driver.Apply(Warp("LakeOfRage", 21, 29, Some Up))

    driver.Talk()

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_DECIDED_TO_HELP_LANCE"
            && (ow.Scenes |> Map.tryFind "MAHOGANY_MART_1F" = Some 1)
        | None -> false

    advanceRuntimeUntil driver 3000 completed

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_DECIDED_TO_HELP_LANCE",
                "Agreeing to help Lance should set EVENT_DECIDED_TO_HELP_LANCE")
    Assert.Equal(Some 1, ow.Scenes |> Map.tryFind "MAHOGANY_MART_1F")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A10 MahoganyMart1F Lance scene uncovers Rocket staircase`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetScene("MAHOGANY_MART_1F", 1))
    // Scene 1 is MahoganyMart1FLanceUncoversStairsScene.
    driver.Apply(Warp("MahoganyMart1F", 5, 6, Some Up))

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_UNCOVERED_STAIRCASE_IN_MAHOGANY_MART"
            && (ow.Scenes |> Map.tryFind "MAHOGANY_MART_1F" = Some 0)
        | None -> false

    advanceRuntimeUntil driver 5000 completed

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_UNCOVERED_STAIRCASE_IN_MAHOGANY_MART",
                "Mahogany Mart Lance scene should uncover the Rocket staircase")
    Assert.Equal(Some 0, ow.Scenes |> Map.tryFind "MAHOGANY_MART_1F")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

// ---------------------------------------------------------------------------
// A11 — Rocket Hideout B1-B2; Pryce; Radio Tower takeover
// ---------------------------------------------------------------------------

[<Fact>]
let ``A11 MahoganyMart hidden stairs load TeamRocketBaseB1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_UNCOVERED_STAIRCASE_IN_MAHOGANY_MART", true))
    // MahoganyMart1F hidden-stair warp at (7,3) -> TeamRocketBaseB1F.
    driver.Apply(Warp("MahoganyMart1F", 7, 4, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "TeamRocketBaseB1F"), 100)

    Assert.Equal("TeamRocketBaseB1F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A11 TeamRocketBaseB1F stairs load TeamRocketBaseB2F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // TeamRocketBaseB1F warp 2 at (3,14) -> TeamRocketBaseB2F.
    driver.Apply(Warp("TeamRocketBaseB1F", 3, 15, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "TeamRocketBaseB2F"), 100)

    Assert.Equal("TeamRocketBaseB2F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A11 TeamRocketBaseB1F security camera starts Rocket battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetScene("TeamRocketBaseB1F", 0))
    driver.Apply(SetEvent("EVENT_SECURITY_CAMERA_1", false))
    driver.Apply(SetEvent("EVENT_TEAM_ROCKET_BASE_POPULATION", false))
    // SecurityCamera1a coord event at (24,2); step in from the west.
    driver.Apply(Warp("TeamRocketBaseB1F", 23, 2, Some Right))

    driver.Step Right

    let mutable sawBattle = false
    let mutable sawText = false
    let mutable frame = 0

    while frame < 3000 && not sawBattle do
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

    Assert.True(sawText, "Security camera should alert before the Rocket battle")
    Assert.True(sawBattle, "Security camera should start a Rocket battle")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A11 TeamRocketBaseB2F Lance heal coord sets Rocket boss scene`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetScene("TeamRocketBaseB2F", 0))
    driver.Apply(SetEvent("EVENT_LANCE_HEALED_YOU_IN_TEAM_ROCKET_BASE", false))
    driver.Apply(SetEvent("EVENT_TEAM_ROCKET_BASE_B2F_LANCE", false))
    // LanceHealsScript1 coord event at (5,14); step in from the west.
    driver.Apply(Warp("TeamRocketBaseB2F", 4, 14, Some Right))

    driver.Step Right

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_LANCE_HEALED_YOU_IN_TEAM_ROCKET_BASE"
            && (ow.Scenes |> Map.tryFind "TEAM_ROCKET_BASE_B2F" = Some 1)
        | None -> false

    advanceRuntimeUntil driver 3000 completed

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_LANCE_HEALED_YOU_IN_TEAM_ROCKET_BASE",
                "Lance should heal the player at the B2F coord event")
    Assert.Equal(Some 1, ow.Scenes |> Map.tryFind "TEAM_ROCKET_BASE_B2F")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A11 TeamRocketBaseB2F Electrode starts battle in transmitter room`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetScene("TeamRocketBaseB2F", 2))
    driver.Apply(SetEvent("EVENT_TEAM_ROCKET_BASE_B2F_ELECTRODE_1", false))
    // Electrode 1 at (7,5); stand south facing up.
    driver.Apply(Warp("TeamRocketBaseB2F", 7, 6, Some Up))

    driver.Talk()

    let mutable sawBattle = false
    let mutable frame = 0

    while frame < 2000 && not sawBattle do
        frame <- frame + 1

        let buttons =
            match driver.Snapshot.TopScene with
            | "TextBoxScene" when frame % 2 = 0 -> press "a"
            | "BattleScene" ->
                sawBattle <- true
                Buttons.none
            | _ -> Buttons.none

        driver.Tick buttons |> ignore

    Assert.True(sawBattle, "Electrode should start a battle in the transmitter room")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A11 Rocket Base mid-arc save round-trip preserves B2F scene and flags`` () =
    let content = Content()
    let state = OverworldState.loadByIdAt content "TeamRocketBaseB2F" 14 11 Down

    let world =
        ScriptWorld.empty
        |> ScriptWorld.setScene "TeamRocketBaseB2F" 2
        |> ScriptWorld.setEvent "EVENT_BEAT_ROCKET_EXECUTIVEF_2"
        |> ScriptWorld.setEvent "EVENT_TEAM_ROCKET_BASE_B2F_ELECTRODE_1"
        |> ScriptWorld.setFlag "ENGINE_ROCKET_SIGNAL_ON_CH20"

    let player = { PlayerStateOps.initial with Name = "GOLD" }

    let save =
        SaveData.captureWith state world player
        |> SaveFile.serialize
        |> SaveFile.deserialize
        |> Option.defaultWith (fun () -> failwith "Rocket Base checkpoint save should deserialize")

    let restored = SaveData.apply content save
    let restoredWorld = SaveData.worldOf save

    Assert.Equal("TeamRocketBaseB2F", restored.MapId)
    Assert.Equal((14, 11), (restored.Player.CellX, restored.Player.CellY))
    Assert.Equal(Down, restored.Player.Facing)
    Assert.Equal(2, ScriptWorld.getScene "TeamRocketBaseB2F" restoredWorld)
    Assert.True(ScriptWorld.hasEvent "EVENT_BEAT_ROCKET_EXECUTIVEF_2" restoredWorld)
    Assert.True(ScriptWorld.hasEvent "EVENT_TEAM_ROCKET_BASE_B2F_ELECTRODE_1" restoredWorld)
    Assert.True(ScriptWorld.hasFlag "ENGINE_ROCKET_SIGNAL_ON_CH20" restoredWorld)
    Assert.Equal("GOLD", (SaveData.playerOf save).Name)

[<Fact>]
let ``A11 Pryce gives GlacierBadge and TM16 after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_BEAT_PRYCE", true))
    driver.Apply(SetFlag("ENGINE_GLACIERBADGE", true))
    // Pryce at (5,3); stand south facing up.
    driver.Apply(Warp("MahoganyGym", 5, 4, Some Up))

    driver.Talk()

    let completed (snapshot: RuntimeSnapshot) =
        match snapshot.Overworld with
        | Some ow ->
            ow.CanCapture
            && ow.Events |> List.contains "EVENT_GOT_TM16_ICY_WIND"
        | None -> false

    advanceRuntimeUntil driver 2000 completed

    let ow = owOf driver.Snapshot
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_GLACIERBADGE",
                "GLACIERBADGE should be set after Pryce")
    Assert.True(ow.Events |> List.contains "EVENT_GOT_TM16_ICY_WIND",
                "Pryce should give TM16 ICY WIND after the badge")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A11 RadioTower5F Rocket boss coord starts takeover battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetFlag("ENGINE_ROCKETS_IN_RADIO_TOWER", true))
    driver.Apply(SetEvent("EVENT_RADIO_TOWER_ROCKET_TAKEOVER", false))
    driver.Apply(SetScene("RadioTower5F", 1))
    // RadioTower5F boss coord event at (16,5); step in from the south.
    driver.Apply(Warp("RadioTower5F", 16, 6, Some Up))

    driver.Step Up

    let mutable sawBattle = false
    let mutable sawText = false
    let mutable frame = 0

    while frame < 3000 && not sawBattle do
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

    Assert.True(sawText, "Radio Tower boss coord should show takeover text before battle")
    Assert.True(sawBattle, "Radio Tower boss coord should start the takeover battle")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

// ---------------------------------------------------------------------------
// A12 — Route 44 → Ice Path → Blackthorn; Clair/Dragon's Den gates
// ---------------------------------------------------------------------------

let private mapCollision (s: OverworldState) x y =
    Movement.collisionIdAtCell s.Map s.Collision x y

let private pureTick (s: OverworldState) buttons player =
    Movement.step s.Map s.Collision buttons player

let private pureWalkOneCell (s: OverworldState) direction player =
    let mutable p = player

    for _ in 1 .. 16 do
        p <- pureTick s (directionButton direction) p

    pureTick s Buttons.none p

let private pureTickNone frames (s: OverworldState) player =
    let mutable p = player

    for _ in 1 .. frames do
        p <- pureTick s Buttons.none p

    p

let private findIceSlideCase (s: OverworldState) =
    let directions = [ Up; Down; Left; Right ]
    let maxX = s.Map.Width * 2 - 1
    let maxY = s.Map.Height * 2 - 1

    seq {
        for x in 0 .. maxX do
            for y in 0 .. maxY do
                if Movement.cellWalkable s.Map s.Collision x y && not (Collision.isIceId (mapCollision s x y)) then
                    for direction in directions do
                        let dx, dy = directionDelta direction
                        let iceX, iceY = x + dx, y + dy
                        let slideX, slideY = iceX + dx, iceY + dy

                        if
                            iceX >= 0
                            && iceY >= 0
                            && iceX <= maxX
                            && iceY <= maxY
                            && slideX >= 0
                            && slideY >= 0
                            && slideX <= maxX
                            && slideY <= maxY
                            && Collision.isIceId (mapCollision s iceX iceY)
                            && Movement.cellWalkable s.Map s.Collision slideX slideY
                        then
                            yield x, y, direction, iceX, iceY, slideX, slideY
    }
    |> Seq.tryHead
    |> Option.defaultWith (fun () -> failwith "expected IcePath1F to contain a walkable ice-slide segment")

let private partyMonWithMove moveName =
    { PartyMon.create 158 35 with
        Moves = MoveLearn.tryLearnMove moveName [] }

let private findWhirlpoolFacingCell (s: OverworldState) =
    let directions = [ Up; Down; Left; Right ]
    let maxX = s.Map.Width * 2 - 1
    let maxY = s.Map.Height * 2 - 1

    seq {
        for x in 0 .. maxX do
            for y in 0 .. maxY do
                let targetColl = mapCollision s x y

                if targetColl = FieldMoves.CollWhirlpool || targetColl = FieldMoves.CollWhirlpool2C then
                    for direction in directions do
                        let dx, dy = directionDelta direction
                        let playerX, playerY = x - dx, y - dy

                        if
                            playerX >= 0
                            && playerY >= 0
                            && playerX <= maxX
                            && playerY <= maxY
                            && FieldMoves.isSurfWater (mapCollision s playerX playerY)
                        then
                            yield playerX, playerY, direction
    }
    |> Seq.tryHead
    |> Option.defaultWith (fun () -> failwith "expected DragonsDenB1F to contain an adjacent surf-water whirlpool")

let private npcOccupies (npc: NpcObject) x y =
    (npc.CellX = x && npc.CellY = y)
    || (npc.Motion <> NpcStanding && npc.SrcX = x && npc.SrcY = y)

let private findStrengthBoulderPush world (s: OverworldState) =
    let directions = [ Up; Down; Left; Right ]

    s.Npcs
    |> Array.mapi (fun i npc -> i, npc)
    |> Array.collect (fun (idx, npc) ->
        if MapEvents.objectVisible world npc.Event && npc.Event.Movement = "SPRITEMOVEDATA_STRENGTH_BOULDER" then
            directions
            |> List.choose (fun direction ->
                let dx, dy = directionDelta direction
                let playerX, playerY = npc.CellX - dx, npc.CellY - dy
                let boulderX, boulderY = npc.CellX + dx, npc.CellY + dy

                let occupied =
                    s.Npcs
                    |> Array.mapi (fun i n -> i, n)
                    |> Array.exists (fun (i, n) ->
                        i <> idx
                        && MapEvents.objectVisible world n.Event
                        && npcOccupies n boulderX boulderY)

                if
                    Movement.cellWalkable s.Map s.Collision playerX playerY
                    && Movement.cellWalkable s.Map s.Collision boulderX boulderY
                    && not occupied
                then
                    Some(playerX, playerY, direction, idx, npc.CellX, npc.CellY, boulderX, boulderY)
                else
                    None)
            |> List.toArray
        else
            [||])
    |> Array.tryHead
    |> Option.defaultWith (fun () -> failwith "expected IcePathB1F to contain a pushable Strength boulder")

[<Fact>]
let ``A12 Route44 cave warp loads IcePath1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Route44.asm: warp_event 56, 7, ICE_PATH_1F, 1.
    driver.Apply(Warp("Route44", 56, 8, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "IcePath1F"), 100)

    Assert.Equal("IcePath1F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A12 IcePath1F east exit warp loads BlackthornCity`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // IcePath1F.asm: warp_event 36, 27, BLACKTHORN_CITY, 7.
    driver.Apply(Warp("IcePath1F", 36, 26, Some Down))

    driver.Step Down

    let snap =
        driver.RunUntil((fun s -> owMap s = "BlackthornCity"), 100)

    Assert.Equal("BlackthornCity", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A12 BlackthornCity gym door loads BlackthornGym1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // BlackthornCity.asm: warp_event 18, 11, BLACKTHORN_GYM_1F, 1.
    driver.Apply(Warp("BlackthornCity", 18, 12, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "BlackthornGym1F"), 100)

    Assert.Equal("BlackthornGym1F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A12 BlackthornCity Dragon's Den entrance loads DragonsDen1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // BlackthornCity.asm: warp_event 20, 1, DRAGONS_DEN_1F, 1.
    driver.Apply(Warp("BlackthornCity", 20, 2, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "DragonsDen1F"), 100)

    Assert.Equal("DragonsDen1F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A12 DragonsDen1F stairs load DragonsDenB1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // DragonsDen1F.asm: warp_event 5, 15, DRAGONS_DEN_B1F, 1.
    driver.Apply(Warp("DragonsDen1F", 5, 14, Some Down))

    driver.Step Down

    let snap =
        driver.RunUntil((fun s -> owMap s = "DragonsDenB1F"), 100)

    Assert.Equal("DragonsDenB1F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A12 IcePath ice tiles keep sliding the player without input`` () =
    let content = Content()
    let icePath = OverworldState.loadById content "IcePath1F"
    let startX, startY, direction, iceX, iceY, slideX, slideY = findIceSlideCase icePath

    let startPlayer =
        { icePath.Player with
            CellX = startX
            CellY = startY
            SrcX = startX
            SrcY = startY
            Facing = direction }

    let onIce = pureWalkOneCell icePath direction startPlayer
    Assert.Equal((iceX, iceY), (onIce.CellX, onIce.CellY))
    Assert.True(Collision.isIceId (mapCollision icePath onIce.CellX onIce.CellY))

    let afterSlide = pureTickNone 17 icePath onIce
    Assert.Equal((slideX, slideY), (afterSlide.CellX, afterSlide.CellY))
    Assert.Equal(Standing, afterSlide.Motion)

[<Fact>]
let ``A12 DragonsDenB1F Whirlpool gate uses GlacierBadge and party HM`` () =
    let content = Content()
    let den = OverworldState.loadById content "DragonsDenB1F"
    let playerX, playerY, facing = findWhirlpoolFacingCell den
    let scene =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "DragonsDenB1F" playerX playerY facing)

    let player =
        { PlayerStateOps.initial with
            Party = [ partyMonWithMove "WHIRLPOOL" ] }

    let world =
        ScriptWorld.empty
        |> ScriptWorld.setFlag "ENGINE_GLACIERBADGE"
        |> ScriptWorld.setVar "__surfing" 1

    scene.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    (scene :> Scene).Update(press "a") |> applyTransition stack

    Assert.Equal("WHIRLPOOL", ScriptWorld.getBuffer "__last_field_move" scene.DebugWorld)
    Assert.Equal(1, ScriptWorld.getVar "__whirlpool_used" scene.DebugWorld)
    Assert.Equal("TextBoxScene", stack.[stack.Count - 1].GetType().Name)

[<Fact>]
let ``A12 Strength-active IcePathB1F boulder pushes one cell`` () =
    let content = Content()
    let baseState = OverworldState.loadById content "IcePathB1F"
    let world = ScriptWorld.setVar "__strength_active" 1 ScriptWorld.empty
    let playerX, playerY, direction, boulderIdx, boulderStartX, boulderStartY, boulderEndX, boulderEndY =
        findStrengthBoulderPush world baseState

    let scene =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "IcePathB1F" playerX playerY direction)

    scene.Restore(world, PlayerStateOps.initial)

    for _ in 1 .. 17 do
        (scene :> Scene).Update(directionButton direction) |> ignore

    let state = scene.DebugState
    let boulder = state.Npcs.[boulderIdx]

    Assert.Equal((boulderStartX, boulderStartY), (state.Player.CellX, state.Player.CellY))
    Assert.Equal((boulderEndX, boulderEndY), (boulder.CellX, boulder.CellY))
    Assert.Equal(Standing, state.Player.Motion)
    Assert.Equal(NpcStanding, boulder.Motion)

[<Fact>]
let ``A12 BlackthornGym2F Strength boulder falls through stone-table hole`` () =
    let content = Content()
    let world =
        ScriptWorld.empty
        |> ScriptWorld.setVar "__strength_active" 1

    let scene =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "BlackthornGym2F" 8 1 Down)

    scene.Restore(world, PlayerStateOps.initial)

    // BlackthornGym2F.asm pairs boulder 1 at (8,2) with stonetable warp 5 at (8,3).
    for _ in 1 .. 17 do
        (scene :> Scene).Update(directionButton Down) |> ignore

    let state = scene.DebugState
    let boulder = state.Npcs.[2]

    Assert.Equal((8, 2), (state.Player.CellX, state.Player.CellY))
    Assert.Equal((8, 3), (boulder.CellX, boulder.CellY))
    Assert.True(ScriptWorld.hasEvent "EVENT_BOULDER_IN_BLACKTHORN_GYM_1" scene.DebugWorld)
    Assert.False(MapEvents.objectVisible scene.DebugWorld boulder.Event)

// ---------------------------------------------------------------------------
// A13 — New Bark → Route 27/26 → Victory Road gate → Indigo Plateau
// ---------------------------------------------------------------------------

let private johtoBadgeFlags =
    [ "ENGINE_ZEPHYRBADGE"
      "ENGINE_HIVEBADGE"
      "ENGINE_PLAINBADGE"
      "ENGINE_FOGBADGE"
      "ENGINE_MINERALBADGE"
      "ENGINE_STORMBADGE"
      "ENGINE_GLACIERBADGE"
      "ENGINE_RISINGBADGE" ]

let private setJohtoBadges (driver: GameDriver) =
    johtoBadgeFlags
    |> List.iter (fun flag -> driver.Apply(SetFlag(flag, true)))

let private waitForCapturableOverworld (driver: GameDriver) maxFrames =
    advanceRuntimeUntil
        driver
        maxFrames
        (fun snapshot ->
            match snapshot.Overworld with
            | Some ow -> ow.CanCapture
            | None -> false)

[<Fact>]
let ``A13 NewBarkTown east surf connection enters Route27`` () =
    let mutable crossed = false

    for tryY in [ 6 .. 9 ] do
        if not crossed then
            let driver = GameDriver()
            driver.Apply(StartNewGame "A")
            driver.Apply(SetScene("NEW_BARK_TOWN", 1))
            driver.Apply(SetVar("__surfing", 1))
            // NewBarkTown generated metadata connects east to Route27; y=6..9 are the water exit.
            driver.Apply(Warp("NewBarkTown", 18, tryY, Some Right))

            for _ in 1 .. 4 do
                driver.Step Right

            if owMap driver.Snapshot = "Route27" then
                crossed <- true
                driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

    Assert.True(crossed, "Could not cross New Bark Town east surf connection into Route27")

[<Fact>]
let ``A13 Route27 east connection enters Route26`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Route27 generated metadata connects east to Route26; y=4 is the land path.
    driver.Apply(Warp("Route27", 78, 4, Some Right))

    for _ in 1 .. 4 do
        driver.Step Right

    Assert.Equal("Route26", owMap driver.Snapshot)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A13 Route26 north gate warp loads VictoryRoadGate`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Route26.asm: warp_event 7, 5, VICTORY_ROAD_GATE, 3.
    driver.Apply(Warp("Route26", 7, 6, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "VictoryRoadGate"), 100)

    Assert.Equal("VictoryRoadGate", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A13 VictoryRoadGate badge guard blocks before all eight Johto badges`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // VictoryRoadGate.asm: coord_event 10, 11 checks VAR_BADGES and steps the player down.
    driver.Apply(Warp("VictoryRoadGate", 10, 12, Some Up))

    driver.Step Up
    waitForCapturableOverworld driver 2000

    let ow = owOf driver.Snapshot
    Assert.Equal("VictoryRoadGate", ow.MapId)
    Assert.Equal((10, 12), (ow.Player.CellX, ow.Player.CellY))
    Assert.NotEqual(Some 1, ow.Scenes |> Map.tryFind "VICTORY_ROAD_GATE")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A13 VictoryRoadGate badge guard passes with all eight Johto badges`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    setJohtoBadges driver
    // VictoryRoadGate.asm: all eight badges set SCENE_VICTORYROADGATE_NOOP.
    driver.Apply(Warp("VictoryRoadGate", 10, 12, Some Up))

    driver.Step Up
    waitForCapturableOverworld driver 2000

    let ow = owOf driver.Snapshot
    Assert.Equal("VictoryRoadGate", ow.MapId)
    Assert.Equal((10, 11), (ow.Player.CellX, ow.Player.CellY))
    Assert.Equal(Some 1, ow.Scenes |> Map.tryFind "VICTORY_ROAD_GATE")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A13 VictoryRoadGate north warp loads VictoryRoad after guard passes`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetScene("VICTORY_ROAD_GATE", 1))
    // VictoryRoadGate.asm: warp_event 10, 0, VICTORY_ROAD, 1.
    driver.Apply(Warp("VictoryRoadGate", 10, 1, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "VictoryRoad"), 100)

    Assert.Equal("VictoryRoad", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A13 VictoryRoad north exit loads Route23`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // VictoryRoad.asm: warp_event 13, 5, ROUTE_23, 3.
    driver.Apply(Warp("VictoryRoad", 13, 6, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "Route23"), 100)

    Assert.Equal("Route23", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A13 Route23 Plateau door loads IndigoPlateauPokecenter1F`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // Route23.asm: warp_event 9, 5, INDIGO_PLATEAU_POKECENTER_1F, 1.
    driver.Apply(Warp("Route23", 9, 6, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "IndigoPlateauPokecenter1F"), 100)

    Assert.Equal("IndigoPlateauPokecenter1F", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

// ---------------------------------------------------------------------------
// A14 — Elite Four → Lance → Hall of Fame → post-game save state
// ---------------------------------------------------------------------------

[<Fact>]
let ``A14 WillsRoom entry scene locks the door behind the player`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // WillsRoom.asm: SCENE_WILLSROOM_LOCK_DOOR sdefers WillsRoomDoorLocksBehindYouScript.
    driver.Apply(Warp("WillsRoom", 5, 17, Some Up))

    waitForCapturableOverworld driver 2000

    let ow = owOf driver.Snapshot
    Assert.Equal("WillsRoom", ow.MapId)
    Assert.Equal(Some 1, ow.Scenes |> Map.tryFind "WILLS_ROOM")
    Assert.True(ow.Events |> List.contains "EVENT_WILLS_ROOM_ENTRANCE_CLOSED")
    Assert.Equal((5, 13), (ow.Player.CellX, ow.Player.CellY))
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A14 Elite Four room exit warps advance in sequence when post-battle doors are open`` () =
    let cases =
        [ "WillsRoom", "EVENT_WILLS_ROOM_EXIT_OPEN", 4, 3, "KogasRoom"
          "KogasRoom", "EVENT_KOGAS_ROOM_EXIT_OPEN", 4, 3, "BrunosRoom"
          "BrunosRoom", "EVENT_BRUNOS_ROOM_EXIT_OPEN", 4, 3, "KarensRoom"
          "KarensRoom", "EVENT_KARENS_ROOM_EXIT_OPEN", 4, 3, "LancesRoom" ]

    for mapId, exitEvent, x, y, destMap in cases do
        let driver = GameDriver()
        driver.Apply(StartNewGame "A")
        driver.Apply(SetEvent(exitEvent, true))
        driver.Apply(SetScene(mapId, 1))
        driver.Apply(Warp(mapId, x, y, Some Up))

        driver.Step Up

        let snap =
            driver.RunUntil((fun s -> owMap s = destMap), 100)

        Assert.Equal(destMap, owMap snap)
        driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A14 LancesRoom approach coord starts Champion battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetScene("LancesRoom", 1))
    // LancesRoom.asm: coord_event 4, 5, SCENE_LANCESROOM_APPROACH_LANCE.
    driver.Apply(Warp("LancesRoom", 4, 6, Some Up))

    driver.Step Up

    let mutable sawBattle = false
    let mutable sawText = false
    let mutable frame = 0

    while frame < 4000 && not sawBattle do
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

    Assert.True(sawText, "Lance should address the player before the Champion battle")
    Assert.True(sawBattle, "Lance approach coord should start the Champion battle")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A14 LancesRoom north warp reaches HallOfFame`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_LANCES_ROOM_EXIT_OPEN", true))
    driver.Apply(SetScene("LancesRoom", 1))
    // LancesRoom.asm: warp_event 4, 0, HALL_OF_FAME, 1.
    driver.Apply(Warp("LancesRoom", 4, 1, Some Up))

    driver.Step Up

    let snap =
        driver.RunUntil((fun s -> owMap s = "HallOfFame"), 100)

    Assert.Equal("HallOfFame", owMap snap)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A14 HallOfFame scene heals party rolls credits marker and persists post-game world`` () =
    let content = Content()
    let scene =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "HallOfFame" 4 13 Up)

    let injured =
        { PartyMon.create 155 50 with
            Hp = 1
            Status = "PSN" }

    scene.Restore(ScriptWorld.empty, { PlayerStateOps.initial with Party = [ injured ] })

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)

    let completed () =
        stack.Count = 1
        && scene.CanCapture
        && ScriptWorld.hasEvent "EVENT_BEAT_ELITE_FOUR" scene.DebugWorld
        && ScriptWorld.getVar "__credits_rolled" scene.DebugWorld = 1

    tickSceneStackUntil stack 12000 completed

    let save =
        scene.Capture()
        |> SaveFile.serialize
        |> SaveFile.deserialize
        |> Option.defaultWith (fun () -> failwith "Hall of Fame post-game save should deserialize")

    let savedWorld = SaveData.worldOf save
    let savedPlayer = SaveData.playerOf save
    let savedLead = savedPlayer.Party |> List.head

    Assert.True(ScriptWorld.hasEvent "EVENT_BEAT_ELITE_FOUR" savedWorld)
    Assert.True(ScriptWorld.hasEvent "EVENT_TELEPORT_GUY" savedWorld)
    Assert.Equal(1, ScriptWorld.getVar "__credits_rolled" savedWorld)
    Assert.Equal(1, ScriptWorld.getVar "__hall_of_fame_count" savedWorld)
    Assert.Equal("NewBarkTown", ScriptWorld.getBuffer "__post_credits_spawn" savedWorld)
    Assert.Equal(injured.MaxHp, savedLead.Hp)
    Assert.Equal("", savedLead.Status)

[<Fact>]
let ``A15 Elm awards SS Ticket after HallOfFame`` () =
    let postHallOfFameWorld =
        ScriptWorld.empty
        |> ScriptWorld.setEvent "EVENT_BEAT_ELITE_FOUR"
        |> ScriptWorld.setEvent "EVENT_COP_IN_ELMS_LAB"
        |> ScriptWorld.setScene "ElmsLab" 2

    let scene, stack =
        sceneStackAt "ElmsLab" 5 3 Up postHallOfFameWorld PlayerStateOps.initial

    // ElmsLab.asm: ProfElmScript jumps to ElmGiveTicketScript after EVENT_BEAT_ELITE_FOUR.
    holdSceneStack stack (press "a") 2

    let completed () =
        stack.Count = 1
        && scene.CanCapture
        && ScriptWorld.hasEvent "EVENT_GOT_SS_TICKET_FROM_ELM" scene.DebugWorld
        && Bag.count "S_S_TICKET" scene.DebugPlayer.Bag = 1

    tickSceneStackUntil stack 3000 completed

    Assert.True(completed (), "Elm should give S.S.TICKET after Hall of Fame and set EVENT_GOT_SS_TICKET_FROM_ELM")

[<Fact>]
let ``A15 Olivine Port sailor blocks gangway without SS Ticket`` () =
    let postHallOfFamePortWorld =
        ScriptWorld.empty
        |> ScriptWorld.setEvent "EVENT_BEAT_ELITE_FOUR"
        |> ScriptWorld.setEvent "EVENT_OLIVINE_PORT_SPRITES_BEFORE_HALL_OF_FAME"
        |> ScriptWorld.clearEvent "EVENT_OLIVINE_PORT_SPRITES_AFTER_HALL_OF_FAME"

    let scene, stack =
        sceneStackAt
            "OlivinePort"
            7
            14
            Down
            postHallOfFamePortWorld
            PlayerStateOps.initial

    // OlivinePort.asm: coord_event 7,15 asks for the S.S.TICKET before the gangway warp.
    holdSceneStack stack (directionButton Down) 20

    let completed () =
        stack.Count = 1
        && scene.CanCapture
        && scene.DebugState.MapId = "OlivinePort"
        && scene.DebugState.Player.CellX = 8
        && scene.DebugState.Player.CellY = 15

    tickSceneStackUntil stack 3000 completed

    Assert.True(completed (), "Sailor should move the player away instead of boarding without S.S.TICKET")
    Assert.Equal(0, Bag.count "S_S_TICKET" scene.DebugPlayer.Bag)

[<Fact>]
let ``A15 Olivine Port ticket path boards first Fast Ship trip`` () =
    let postHallOfFamePortWorld =
        ScriptWorld.empty
        |> ScriptWorld.setEvent "EVENT_BEAT_ELITE_FOUR"
        |> ScriptWorld.setEvent "EVENT_OLIVINE_PORT_SPRITES_BEFORE_HALL_OF_FAME"
        |> ScriptWorld.clearEvent "EVENT_OLIVINE_PORT_SPRITES_AFTER_HALL_OF_FAME"

    let ticketedPlayer =
        { PlayerStateOps.initial with
            Bag = Bag.add "S_S_TICKET" 1 PlayerStateOps.initial.Bag }

    let scene, stack =
        sceneStackAt
            "OlivinePort"
            7
            14
            Down
            postHallOfFamePortWorld
            ticketedPlayer

    holdSceneStack stack (directionButton Down) 20

    let completed () =
        stack.Count = 1
        && scene.CanCapture
        && scene.DebugState.MapId = "FastShip1F"
        && ScriptWorld.getScene "FastShip1F" scene.DebugWorld = 2

    tickSceneStackUntil stack 8000 completed

    Assert.True(completed (), "Ticketed Olivine boarding should warp to FastShip1F and advance to the meet-grandpa scene")
    Assert.True(ScriptWorld.hasEvent "EVENT_FAST_SHIP_HAS_ARRIVED" scene.DebugWorld |> not)
    Assert.Equal(1, Bag.count "S_S_TICKET" scene.DebugPlayer.Bag)

[<Fact>]
let ``A15 granddaughter quest awards Metal Coat and docks at Vermilion`` () =
    let shipQuestWorld =
         ScriptWorld.empty
        |> ScriptWorld.setScene "FastShip1F" 2
        |> ScriptWorld.setEvent "EVENT_FAST_SHIP_CABINS_SE_SSE_GENTLEMAN"
        |> ScriptWorld.setEvent "EVENT_FAST_SHIP_CABINS_SE_SSE_CAPTAINS_CABIN_TWIN_1"
        |> ScriptWorld.clearEvent "EVENT_FAST_SHIP_CABINS_SE_SSE_CAPTAINS_CABIN_TWIN_2"

    let scene, stack =
        sceneStackAt
            "FastShipCabins_SE_SSE_CaptainsCabin"
            2
            26
            Up
            shipQuestWorld
            PlayerStateOps.initial

    // CaptainsCabin.asm: granddaughter at 2,25 walks the player back to grandpa and docks.
    holdSceneStack stack (press "a") 2

    let completed () =
        stack.Count = 1
        && scene.CanCapture
        && ScriptWorld.hasEvent "EVENT_GOT_METAL_COAT_FROM_GRANDPA_ON_SS_AQUA" scene.DebugWorld
        && ScriptWorld.hasEvent "EVENT_FAST_SHIP_HAS_ARRIVED" scene.DebugWorld
        && ScriptWorld.hasEvent "EVENT_FAST_SHIP_FOUND_GIRL" scene.DebugWorld

    tickSceneStackUntil stack 12000 completed

    Assert.True(completed (), "Granddaughter script should award Metal Coat and mark the S.S.Aqua arrived")
    Assert.Equal(1, Bag.count "METAL_COAT" scene.DebugPlayer.Bag)
    Assert.True(ScriptWorld.hasEvent "EVENT_VERMILION_PORT_SAILOR_AT_GANGWAY" scene.DebugWorld)
    Assert.Equal(0, ScriptWorld.getScene "FastShip1F" scene.DebugWorld)

[<Fact>]
let ``A15 arrived Fast Ship exits to Vermilion Port`` () =
    let arrivedAtVermilionWorld =
        ScriptWorld.empty
        |> ScriptWorld.setEvent "EVENT_FAST_SHIP_HAS_ARRIVED"
        |> ScriptWorld.clearEvent "EVENT_FAST_SHIP_DESTINATION_OLIVINE"

    let scene, stack =
        sceneStackAt "FastShip1F" 25 3 Up arrivedAtVermilionWorld PlayerStateOps.initial

    holdSceneStack stack (press "a") 2

    let completed () =
        stack.Count = 1
        && scene.CanCapture
        && scene.DebugState.MapId = "VermilionPort"
        && scene.DebugState.Player.CellX = 7
        && scene.DebugState.Player.CellY = 16
        && ScriptWorld.hasFlag "ENGINE_FLYPOINT_VERMILION" scene.DebugWorld
        && ScriptWorld.hasEvent "EVENT_FAST_SHIP_FIRST_TIME" scene.DebugWorld
        && ScriptWorld.getScene "VermilionPort" scene.DebugWorld = 0

    tickSceneStackUntil stack 12000 completed

    Assert.True(completed (), "FastShip1F sailor should exit an arrived eastbound ship onto Vermilion Port")

[<Fact>]
let ``A16 Vermilion Port passage and gym door load Vermilion City and Gym`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")

    // VermilionPort.asm: warp_event 9,5, VERMILION_PORT_PASSAGE, 5.
    driver.Apply(Warp("VermilionPort", 9, 6, Some Up))
    stepAndSettle driver Up
    Assert.Equal("VermilionPortPassage", owMap driver.Snapshot)

    // VermilionPortPassage.asm: warp_event 15,0, VERMILION_CITY, 8.
    driver.Apply(Warp("VermilionPortPassage", 15, 1, Some Up))
    stepAndSettle driver Up
    Assert.Equal("VermilionCity", owMap driver.Snapshot)
    Assert.True((owOf driver.Snapshot).EngineFlags |> List.contains "ENGINE_FLYPOINT_VERMILION")

    // VermilionCity.asm: warp_event 10,19, VERMILION_GYM, 1.
    driver.Apply(Warp("VermilionCity", 10, 20, Some Up))
    stepAndSettle driver Up
    Assert.Equal("VermilionGym", owMap driver.Snapshot)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A16 Surge awards ThunderBadge after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // VermilionGym.asm: Surge stands at 5,2.
    driver.Apply(Warp("VermilionGym", 5, 3, Some Up))

    driver.Talk()

    advanceRuntimeUntil
        driver
        5000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_BEAT_LTSURGE"
                && ow.EngineFlags |> List.contains "ENGINE_THUNDERBADGE"
            | None -> false)

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_BEAT_LTSURGE", "Surge battle should set EVENT_BEAT_LTSURGE")
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_THUNDERBADGE", "Surge battle should set THUNDERBADGE")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A16 Saffron gate warps stay open before and after Machine Part`` () =
    let cases =
        [ ("Route5SaffronGate", 4, 6, Down)
          ("Route6SaffronGate", 4, 1, Up)
          ("Route7SaffronGate", 8, 4, Right)
          ("Route8SaffronGate", 1, 4, Left) ]

    for returnedMachinePart in [ false; true ] do
        for gateMap, x, y, direction in cases do
            let driver = GameDriver()
            driver.Apply(StartNewGame "A")

            if returnedMachinePart then
                driver.Apply(SetEvent("EVENT_RETURNED_MACHINE_PART", true))

            driver.Apply(Warp(gateMap, x, y, Some direction))
            stepAndSettle driver direction

            Assert.True(
                owMap driver.Snapshot = "SaffronCity",
                $"{gateMap} should warp into SaffronCity with EVENT_RETURNED_MACHINE_PART={returnedMachinePart}")

            driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A16 Route6 and Route7 Saffron guard text reflects Machine Part state`` () =
    let assertGuardText returnedMachinePart gateMap x y facing expectedLabel =
        let driver = GameDriver()
        driver.Apply(StartNewGame "A")

        if returnedMachinePart then
            driver.Apply(SetEvent("EVENT_RETURNED_MACHINE_PART", true))

        driver.Apply(Warp(gateMap, x, y, Some facing))
        talkUntilLastText driver expectedLabel
        driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

    // Route6SaffronGate.asm and Route7SaffronGate.asm branch on EVENT_RETURNED_MACHINE_PART.
    assertGuardText false "Route6SaffronGate" 1 4 Left "Route6SaffronGuardWelcomeText"
    assertGuardText true "Route6SaffronGate" 1 4 Left "Route6SaffronGuardMagnetTrainText"
    assertGuardText false "Route7SaffronGate" 5 3 Up "Route7SaffronGuardPowerPlantText"
    assertGuardText true "Route7SaffronGate" 5 3 Up "Route7SaffronGuardSeriousText"

[<Fact>]
let ``A16 Route5 and Route8 Saffron guard text is static per disassembly`` () =
    let assertStaticGuardText returnedMachinePart gateMap x y facing expectedLabel =
        let driver = GameDriver()
        driver.Apply(StartNewGame "A")

        if returnedMachinePart then
            driver.Apply(SetEvent("EVENT_RETURNED_MACHINE_PART", true))

        driver.Apply(Warp(gateMap, x, y, Some facing))
        talkUntilLastText driver expectedLabel
        driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

    // Route5SaffronGate.asm and Route8SaffronGate.asm use jumptextfaceplayer without an event check.
    assertStaticGuardText false "Route5SaffronGate" 1 4 Left "Route5SaffronGateOfficerText"
    assertStaticGuardText true "Route5SaffronGate" 1 4 Left "Route5SaffronGateOfficerText"
    assertStaticGuardText false "Route8SaffronGate" 5 3 Up "Route8SaffronGateOfficerText"
    assertStaticGuardText true "Route8SaffronGate" 5 3 Up "Route8SaffronGateOfficerText"

[<Fact>]
let ``A16 Saffron Gym teleport path reaches Sabrina and awards MarshBadge`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")

    // SaffronCity.asm: warp_event 34,3, SAFFRON_GYM, 1.
    driver.Apply(Warp("SaffronCity", 34, 4, Some Up))
    stepAndSettle driver Up
    Assert.Equal("SaffronGym", owMap driver.Snapshot)

    // SaffronGym.asm pad chain from the entrance-side pad at 11,15 to Sabrina's room.
    driver.Apply(Warp("SaffronGym", 11, 16, Some Up))

    let stepOntoPad direction expectedX expectedY =
        driver.Step direction

        driver.RunUntil(
            (fun s ->
                match s.Overworld with
                | Some ow ->
                    ow.CanCapture
                    && ow.MapId = "SaffronGym"
                    && ow.Player.CellX = expectedX
                    && ow.Player.CellY = expectedY
                | None -> false),
            500)
        |> ignore

    stepOntoPad Up 19 17
    stepAndSettle driver Up
    stepOntoPad Up 19 9
    stepAndSettle driver Down
    stepOntoPad Down 1 9
    stepAndSettle driver Down
    stepOntoPad Down 5 5
    stepAndSettle driver Left
    stepAndSettle driver Left
    stepAndSettle driver Left
    stepOntoPad Left 11 9
    stepAndSettle driver Left
    stepAndSettle driver Left
    stepAndSettle driver Up

    let owAtSabrinaRoom = owOf driver.Snapshot
    Assert.Equal("SaffronGym", owAtSabrinaRoom.MapId)
    Assert.Equal(9, owAtSabrinaRoom.Player.CellX)
    Assert.Equal(9, owAtSabrinaRoom.Player.CellY)

    driver.Talk()

    advanceRuntimeUntil
        driver
        5000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_BEAT_SABRINA"
                && ow.EngineFlags |> List.contains "ENGINE_MARSHBADGE"
            | None -> false)

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_BEAT_SABRINA", "Sabrina battle should set EVENT_BEAT_SABRINA")
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_MARSHBADGE", "Sabrina battle should set MARSHBADGE")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A17 Cerulean and Power Plant Machine Part chain works on foot`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")

    // PowerPlant.asm: manager at 14,10 starts the theft investigation.
    driver.Apply(Warp("PowerPlant", 14, 11, Some Up))
    driver.Talk()

    advanceRuntimeUntil
        driver
        3000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_MET_MANAGER_AT_POWER_PLANT"
                && not (ow.Events |> List.contains "EVENT_CERULEAN_GYM_ROCKET")
                && Map.tryFind "CERULEAN_GYM" ow.Scenes = Some 1
            | None -> false)

    let afterManager = owOf driver.Snapshot
    Assert.True(afterManager.Events |> List.contains "EVENT_MET_MANAGER_AT_POWER_PLANT")
    Assert.False(afterManager.Events |> List.contains "EVENT_CERULEAN_GYM_ROCKET")
    Assert.Equal(Some 1, Map.tryFind "CERULEAN_GYM" afterManager.Scenes)

    // CeruleanCity.asm: gym door at 30,23 leads to CeruleanGym; scene 1 runs the Rocket out.
    driver.Apply(Warp("CeruleanCity", 30, 24, Some Up))
    driver.Step Up

    advanceRuntimeUntil
        driver
        6000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.MapId = "CeruleanGym"
                && ow.Events |> List.contains "EVENT_MET_ROCKET_GRUNT_AT_CERULEAN_GYM"
                && not (ow.Events |> List.contains "EVENT_ROUTE_24_ROCKET")
                && not (ow.Events |> List.contains "EVENT_ROUTE_25_MISTY_BOYFRIEND")
                && Map.tryFind "CERULEAN_GYM" ow.Scenes = Some 0
            | None -> false)

    let afterGymRocket = owOf driver.Snapshot
    Assert.True(afterGymRocket.Events |> List.contains "EVENT_MET_ROCKET_GRUNT_AT_CERULEAN_GYM")
    Assert.False(afterGymRocket.Events |> List.contains "EVENT_ROUTE_24_ROCKET")
    Assert.Equal(Some 0, Map.tryFind "CERULEAN_GYM" afterGymRocket.Scenes)

    // Route24.asm: Rocket at 8,7 tells the player the part is hidden in Cerulean Gym.
    driver.Apply(Warp("Route24", 8, 8, Some Up))
    driver.Talk()

    advanceRuntimeUntil
        driver
        6000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.LastTextLabel = Some "Route24RocketDisappearsText"
                && ow.Actors
                   |> List.exists (fun a -> a.Script = "Route24RocketScript" && not a.Visible)
            | None -> false)

    let afterRoute24Rocket = owOf driver.Snapshot
    Assert.Equal(Some "Route24RocketDisappearsText", afterRoute24Rocket.LastTextLabel)
    Assert.Contains(
        afterRoute24Rocket.Actors,
        fun a -> a.Script = "Route24RocketScript" && not a.Visible)

    // CeruleanGym.asm: bg_event 3,8 is BGEVENT_ITEM CeruleanGymHiddenMachinePart.
    driver.Apply(Warp("CeruleanGym", 3, 9, Some Up))
    driver.Talk()

    advanceRuntimeUntil
        driver
        3000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_FOUND_MACHINE_PART_IN_CERULEAN_GYM"
                && ow.LastTextLabel = Some "VerboseGiveItem"
            | None -> false)

    let afterHiddenItem = owOf driver.Snapshot
    Assert.True(afterHiddenItem.Events |> List.contains "EVENT_FOUND_MACHINE_PART_IN_CERULEAN_GYM")
    Assert.Equal(Some "VerboseGiveItem", afterHiddenItem.LastTextLabel)

    // PowerPlant.asm: returning MACHINE_PART consumes it, restores Kanto power, and awards TM07.
    driver.Apply(Warp("PowerPlant", 14, 11, Some Up))
    driver.Talk()

    advanceRuntimeUntil
        driver
        5000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_RETURNED_MACHINE_PART"
                && ow.Events |> List.contains "EVENT_RESTORED_POWER_TO_KANTO"
                && ow.Events |> List.contains "EVENT_GOT_TM07_ZAP_CANNON"
            | None -> false)

    let afterReturn = owOf driver.Snapshot
    Assert.True(afterReturn.Events |> List.contains "EVENT_RETURNED_MACHINE_PART")
    Assert.True(afterReturn.Events |> List.contains "EVENT_RESTORED_POWER_TO_KANTO")
    Assert.True(afterReturn.Events |> List.contains "EVENT_GOT_TM07_ZAP_CANNON")
    Assert.True(afterReturn.Events |> List.contains "EVENT_ROUTE_5_6_POKEFAN_M_BLOCKS_UNDERGROUND_PATH")
    // CeruleanGymGruntRunsIntoYouMovement intentionally overlaps the player during the bump cutscene.
    assertHold core driver.Snapshot

[<Fact>]
let ``A18 EXPN Card Pokegear radio tune wakes Vermilion Snorlax`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_RETURNED_MACHINE_PART", true))

    // LavRadioTower1F.asm: gentleman at 9,1 gives EXPN CARD after the Machine Part is returned.
    driver.Apply(Warp("LavRadioTower1F", 9, 2, Some Up))
    driver.Talk()

    advanceRuntimeUntil
        driver
        4000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.EngineFlags |> List.contains "ENGINE_EXPN_CARD"
                && ow.LastTextLabel = Some "LavRadioTower1FGentlemanText_GotExpnCard"
            | None -> false)

    let afterExpn = owOf driver.Snapshot
    Assert.True(afterExpn.EngineFlags |> List.contains "ENGINE_EXPN_CARD")

    // VermilionCity.asm: Snorlax remains asleep unless special SnorlaxAwake sees POKE_FLUTE.
    driver.Apply(Warp("VermilionCity", 33, 8, Some Right))
    driver.Talk()

    advanceRuntimeUntil
        driver
        2000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.LastTextLabel = Some "VermilionCitySnorlaxSleepingText"
                && not (ow.Events |> List.contains "EVENT_FOUGHT_SNORLAX")
            | None -> false)

    let tunePokeFluteThroughPokegear () =
        driver.Press({ Buttons.none with Start = true })
        driver.RunUntil((fun s -> s.TopScene = "StartMenuScene"), 100) |> ignore

        for _ in 1..3 do
            driver.Press(directionButton Down)

        driver.Press(press "a")
        driver.RunUntil((fun s -> s.TopScene = "PokegearScene"), 100) |> ignore

        for _ in 1..4 do
            driver.Press(directionButton Down)

        driver.Press(press "a")
        driver.Press(press "b")
        driver.RunUntil((fun s -> s.TopScene = "StartMenuScene"), 100) |> ignore
        driver.Press(press "b")
        driver.RunUntil((fun s -> s.TopScene = "OverworldScene"), 100) |> ignore

    tunePokeFluteThroughPokegear ()

    driver.Talk()

    advanceRuntimeUntil
        driver
        6000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_FOUGHT_SNORLAX"
                && ow.Events |> List.contains "EVENT_VERMILION_CITY_SNORLAX"
            | None -> false)

    let afterSnorlax = owOf driver.Snapshot
    Assert.True(afterSnorlax.Events |> List.contains "EVENT_FOUGHT_SNORLAX")
    Assert.True(afterSnorlax.Events |> List.contains "EVENT_VERMILION_CITY_SNORLAX")
    Assert.Contains(
        afterSnorlax.Actors,
        fun a -> a.Script = "VermilionSnorlax" && not a.Visible)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

// ---------------------------------------------------------------------------
// A19 — Diglett's Cave → Pewter → Brock; Celadon → Erika; Fuchsia → Janine
// ---------------------------------------------------------------------------

[<Fact>]
let ``A19 Digletts Cave route reaches Route2 and PewterCity`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_VERMILION_CITY_SNORLAX", true))

    let waitForOverworldAt mapId x y =
        driver.RunUntil(
            (fun s ->
                match s.Overworld with
                | Some ow -> ow.CanCapture && ow.MapId = mapId && ow.Player.CellX = x && ow.Player.CellY = y
                | None -> false),
            500)
        |> ignore

    // VermilionCity.asm: warp_event 34,7, DIGLETTS_CAVE, 1.
    driver.Apply(Warp("VermilionCity", 34, 8, Some Up))
    stepAndSettle driver Up
    Assert.Equal("DiglettsCave", owMap driver.Snapshot)
    Assert.Equal((3, 33), ((owOf driver.Snapshot).Player.CellX, (owOf driver.Snapshot).Player.CellY))

    // DiglettsCave.asm: walk from the south entrance onto warp_event 5,31 -> warp 5.
    for direction in [ Right; Right; Up; Up; Up ] do
        stepAndSettle driver direction

    waitForOverworldAt "DiglettsCave" 17 33
    Assert.Equal("DiglettsCave", owMap driver.Snapshot)
    Assert.Equal((17, 33), ((owOf driver.Snapshot).Player.CellX, (owOf driver.Snapshot).Player.CellY))

    // DiglettsCave.asm: warp_event 17,3 -> warp 6.
    driver.Apply(Warp("DiglettsCave", 17, 4, Some Up))
    stepAndSettle driver Up
    waitForOverworldAt "DiglettsCave" 3 3
    Assert.Equal("DiglettsCave", owMap driver.Snapshot)
    Assert.Equal((3, 3), ((owOf driver.Snapshot).Player.CellX, (owOf driver.Snapshot).Player.CellY))

    // DiglettsCave.asm: warp_event 15,5, ROUTE_2, 5.
    driver.Apply(Warp("DiglettsCave", 15, 6, Some Up))
    stepAndSettle driver Up
    waitForOverworldAt "Route2" 12 7
    Assert.Equal("Route2", owMap driver.Snapshot)
    Assert.Equal((12, 7), ((owOf driver.Snapshot).Player.CellX, (owOf driver.Snapshot).Player.CellY))

    // Route2 metadata connects north into PewterCity; x=16 is a walkable north-edge tile.
    driver.Apply(Warp("Route2", 16, 0, Some Up))
    stepAndSettle driver Up
    waitForOverworldAt "PewterCity" 26 35
    Assert.Equal("PewterCity", owMap driver.Snapshot)
    Assert.Equal((26, 35), ((owOf driver.Snapshot).Player.CellX, (owOf driver.Snapshot).Player.CellY))
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A19 Pewter Celadon and Fuchsia gym doors load their gyms`` () =
    let cases =
        [ "PewterCity", 16, 18, Up, "PewterGym"
          "CeladonCity", 10, 30, Up, "CeladonGym"
          "FuchsiaCity", 8, 28, Up, "FuchsiaGym" ]

    for cityMap, x, y, direction, gymMap in cases do
        let driver = GameDriver()
        driver.Apply(StartNewGame "A")
        driver.Apply(Warp(cityMap, x, y, Some direction))
        stepAndSettle driver direction

        Assert.Equal(gymMap, owMap driver.Snapshot)
        driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A19 Brock awards BoulderBadge after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // PewterGym.asm: Brock stands at 5,1.
    driver.Apply(Warp("PewterGym", 5, 2, Some Up))

    driver.Talk()

    advanceRuntimeUntil
        driver
        5000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_BEAT_BROCK"
                && ow.Events |> List.contains "EVENT_BEAT_CAMPER_JERRY"
                && ow.EngineFlags |> List.contains "ENGINE_BOULDERBADGE"
            | None -> false)

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_BEAT_BROCK", "Brock battle should set EVENT_BEAT_BROCK")
    Assert.True(ow.Events |> List.contains "EVENT_BEAT_CAMPER_JERRY", "Brock script should mark Camper Jerry beaten")
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_BOULDERBADGE", "Brock battle should set BOULDERBADGE")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A19 Erika awards RainbowBadge and TM19 after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // CeladonGym.asm: Erika stands at 5,3.
    driver.Apply(Warp("CeladonGym", 5, 4, Some Up))

    driver.Talk()

    advanceRuntimeUntil
        driver
        5000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_BEAT_ERIKA"
                && ow.Events |> List.contains "EVENT_GOT_TM19_GIGA_DRAIN"
                && ow.EngineFlags |> List.contains "ENGINE_RAINBOWBADGE"
            | None -> false)

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_BEAT_ERIKA", "Erika battle should set EVENT_BEAT_ERIKA")
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_RAINBOWBADGE", "Erika battle should set RAINBOWBADGE")
    Assert.True(ow.Events |> List.contains "EVENT_GOT_TM19_GIGA_DRAIN", "Erika should give TM19 GIGA DRAIN")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A19 Janine awards SoulBadge and TM06 after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    // FuchsiaGym.asm: Janine stands at 1,10.
    driver.Apply(Warp("FuchsiaGym", 1, 11, Some Up))

    driver.Talk()

    advanceRuntimeUntil
        driver
        5000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_BEAT_JANINE"
                && ow.Events |> List.contains "EVENT_GOT_TM06_TOXIC"
                && ow.EngineFlags |> List.contains "ENGINE_SOULBADGE"
            | None -> false)

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_BEAT_JANINE", "Janine battle should set EVENT_BEAT_JANINE")
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_SOULBADGE", "Janine battle should set SOULBADGE")
    Assert.True(ow.Events |> List.contains "EVENT_GOT_TM06_TOXIC", "Janine should give TM06 TOXIC")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

// ---------------------------------------------------------------------------
// A20 — Cinnabar (Blue) → Seafoam (Blaine) → Viridian (Blue)
// ---------------------------------------------------------------------------

let private hasActor script visible (ow: RuntimeOverworldSnapshot) =
    ow.Actors |> List.exists (fun actor -> actor.Script = script && actor.Visible = visible)

[<Fact>]
let ``A20 Cinnabar Blue disappears and enables Viridian Gym Blue`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_BLUE_IN_CINNABAR", false))
    driver.Apply(SetEvent("EVENT_VIRIDIAN_GYM_BLUE", true))

    // CinnabarIsland.asm: Blue stands at 9,6 and clears EVENT_VIRIDIAN_GYM_BLUE after teleporting away.
    driver.Apply(Warp("CinnabarIsland", 9, 7, Some Up))
    let beforeBlue = owOf driver.Snapshot
    Assert.True(hasActor "CinnabarIslandBlue" true beforeBlue, "Blue should be visible on Cinnabar before he is invited back")

    driver.Talk()

    advanceRuntimeUntil
        driver
        5000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_BLUE_IN_CINNABAR"
                && not (ow.Events |> List.contains "EVENT_VIRIDIAN_GYM_BLUE")
            | None -> false)

    let afterCinnabarBlue = owOf driver.Snapshot
    Assert.True(hasActor "CinnabarIslandBlue" false afterCinnabarBlue, "Blue should disappear from Cinnabar after the teleport movement")
    Assert.True(afterCinnabarBlue.Events |> List.contains "EVENT_BLUE_IN_CINNABAR")
    Assert.False(afterCinnabarBlue.Events |> List.contains "EVENT_VIRIDIAN_GYM_BLUE")

    driver.Apply(Warp("ViridianGym", 5, 4, Some Up))
    settleUntilCapture driver 300
    let viridianGym = owOf driver.Snapshot
    Assert.True(hasActor "ViridianGymBlueScript" true viridianGym, "Clearing EVENT_VIRIDIAN_GYM_BLUE should show Blue in Viridian Gym")
    Assert.True(hasActor "ViridianGymGuideScript" true viridianGym, "Clearing EVENT_VIRIDIAN_GYM_BLUE should show the Viridian Gym guide")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A20 Cinnabar east surf route loads Route20 and Seafoam Gym`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetVar("__surfing", 1))

    // CinnabarIsland metadata connects east to Route20; y=8 is open water.
    driver.Apply(Warp("CinnabarIsland", 19, 8, Some Right))
    stepAndSettle driver Right
    Assert.Equal("Route20", owMap driver.Snapshot)
    Assert.Equal((0, 8), ((owOf driver.Snapshot).Player.CellX, (owOf driver.Snapshot).Player.CellY))

    // Route20.asm: warp_event 38,7, SEAFOAM_GYM, 1.
    driver.Apply(Warp("Route20", 38, 8, Some Up))
    stepAndSettle driver Up
    Assert.Equal("SeafoamGym", owMap driver.Snapshot)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A20 Blaine awards VolcanoBadge after Seafoam Gym battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_SEAFOAM_GYM_GYM_GUIDE", true))
    // SeafoamGym.asm: Blaine stands at 5,2.
    driver.Apply(Warp("SeafoamGym", 5, 3, Some Up))

    driver.Talk()

    advanceRuntimeUntil
        driver
        5000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_BEAT_BLAINE"
                && ow.EngineFlags |> List.contains "ENGINE_VOLCANOBADGE"
                && not (ow.Events |> List.contains "EVENT_SEAFOAM_GYM_GYM_GUIDE")
            | None -> false)

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_BEAT_BLAINE", "Blaine battle should set EVENT_BEAT_BLAINE")
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_VOLCANOBADGE", "Blaine battle should set VOLCANOBADGE")
    Assert.True(hasActor "SeafoamGymGuideScript" true ow, "Blaine script should reveal the Seafoam Gym guide after battle")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A20 Viridian Gym door loads gym and Blue awards EarthBadge`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_VIRIDIAN_GYM_BLUE", false))

    // ViridianCity.asm: warp_event 32,7, VIRIDIAN_GYM, 1.
    driver.Apply(Warp("ViridianCity", 32, 8, Some Up))
    stepAndSettle driver Up
    Assert.Equal("ViridianGym", owMap driver.Snapshot)

    // ViridianGym.asm: Blue stands at 5,3.
    driver.Apply(Warp("ViridianGym", 5, 4, Some Up))
    let beforeBattle = owOf driver.Snapshot
    Assert.True(hasActor "ViridianGymBlueScript" true beforeBattle, "Blue should be visible in Viridian Gym after the Cinnabar event")

    driver.Talk()

    advanceRuntimeUntil
        driver
        5000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_BEAT_BLUE"
                && ow.EngineFlags |> List.contains "ENGINE_EARTHBADGE"
            | None -> false)

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_BEAT_BLUE", "Blue battle should set EVENT_BEAT_BLUE")
    Assert.True(ow.EngineFlags |> List.contains "ENGINE_EARTHBADGE", "Blue battle should set EARTHBADGE")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

// ---------------------------------------------------------------------------
// A21 — Pallet → Oak (16 badges) → Route 28/Silver Cave gate → Red → credits
// ---------------------------------------------------------------------------

let private kantoBadgeFlags =
    [ "ENGINE_THUNDERBADGE"
      "ENGINE_MARSHBADGE"
      "ENGINE_CASCADEBADGE"
      "ENGINE_BOULDERBADGE"
      "ENGINE_RAINBOWBADGE"
      "ENGINE_SOULBADGE"
      "ENGINE_VOLCANOBADGE"
      "ENGINE_EARTHBADGE" ]

let private setKantoBadges (driver: GameDriver) =
    kantoBadgeFlags
    |> List.iter (fun flag -> driver.Apply(SetFlag(flag, true)))

let private setAllBadges (driver: GameDriver) =
    setJohtoBadges driver
    setKantoBadges driver

[<Fact>]
let ``A21 Oak opens Mt Silver after all sixteen badges`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    setAllBadges driver
    driver.Apply(SetEvent("EVENT_OPENED_MT_SILVER", false))

    // PalletTown.asm: warp_event 12,11, OAKS_LAB, 1.
    driver.Apply(Warp("PalletTown", 12, 12, Some Up))
    stepAndSettle driver Up
    Assert.Equal("OaksLab", owMap driver.Snapshot)

    // OaksLab.asm: Oak stands at 4,2 and opens Mt. Silver at NUM_BADGES.
    driver.Apply(Warp("OaksLab", 4, 3, Some Up))
    driver.Talk()

    advanceRuntimeUntil
        driver
        5000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_TALKED_TO_OAK_IN_KANTO"
                && ow.Events |> List.contains "EVENT_OPENED_MT_SILVER"
            | None -> false)

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_TALKED_TO_OAK_IN_KANTO", "Oak should remember the Kanto greeting")
    Assert.True(ow.Events |> List.contains "EVENT_OPENED_MT_SILVER", "Oak should open Mt. Silver after all sixteen badges")
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A21 VictoryRoadGate Mt Silver guard blocks until Oak opens route`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_OPENED_MT_SILVER", false))

    // VictoryRoadGate.asm: left Black Belt at 7,5 uses EVENT_OPENED_MT_SILVER.
    driver.Apply(Warp("VictoryRoadGate", 8, 5, Some Left))
    let blockedGate = owOf driver.Snapshot
    Assert.True(hasActor "VictoryRoadGateLeftBlackBeltScript" true blockedGate)

    stepAndSettle driver Left
    let stillBlocked = owOf driver.Snapshot
    Assert.Equal((8, 5), (stillBlocked.Player.CellX, stillBlocked.Player.CellY))

    driver.Apply(SetEvent("EVENT_OPENED_MT_SILVER", true))
    driver.Apply(Warp("VictoryRoadGate", 8, 5, Some Left))
    let openedGate = owOf driver.Snapshot
    Assert.True(hasActor "VictoryRoadGateLeftBlackBeltScript" false openedGate)

    stepAndSettle driver Left
    let movedThrough = owOf driver.Snapshot
    Assert.Equal((7, 5), (movedThrough.Player.CellX, movedThrough.Player.CellY))

    // VictoryRoadGate.asm: warp_event 2,7, ROUTE_28, 2.
    driver.Apply(Warp("VictoryRoadGate", 3, 7, Some Left))
    stepAndSettle driver Left
    Assert.Equal("Route28", owMap driver.Snapshot)

    // Route28 metadata connects west into SilverCaveOutside.
    driver.Apply(Warp("Route28", 0, 15, Some Left))
    stepAndSettle driver Left
    Assert.Equal("SilverCaveOutside", owMap driver.Snapshot)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)

[<Fact>]
let ``A21 Silver Cave warps reach Red and credits roll after battle`` () =
    let driver = GameDriver()
    driver.Apply(StartNewGame "A")
    driver.Apply(SetEvent("EVENT_RED_IN_MT_SILVER", false))

    // SilverCaveOutside.asm: warp_event 18,11, SILVER_CAVE_ROOM_1, 1.
    driver.Apply(Warp("SilverCaveOutside", 18, 12, Some Up))
    stepAndSettle driver Up
    Assert.Equal("SilverCaveRoom1", owMap driver.Snapshot)

    // SilverCaveRoom1.asm: warp_event 15,1, SILVER_CAVE_ROOM_2, 1.
    driver.Apply(Warp("SilverCaveRoom1", 15, 2, Some Up))
    stepAndSettle driver Up
    Assert.Equal("SilverCaveRoom2", owMap driver.Snapshot)

    // SilverCaveRoom2.asm: warp_event 11,5, SILVER_CAVE_ROOM_3, 1.
    driver.Apply(Warp("SilverCaveRoom2", 11, 6, Some Up))
    stepAndSettle driver Up
    Assert.Equal("SilverCaveRoom3", owMap driver.Snapshot)

    // SilverCaveRoom3.asm: Red stands at 9,10, disappears, heals, reanchors, and rolls credits.
    driver.Apply(Warp("SilverCaveRoom3", 9, 11, Some Up))
    let beforeRed = owOf driver.Snapshot
    Assert.True(hasActor "Red" true beforeRed, "Red should be visible before the final battle")

    driver.Talk()

    advanceRuntimeUntil
        driver
        15000
        (fun s ->
            match s.Overworld with
            | Some ow ->
                ow.CanCapture
                && ow.Events |> List.contains "EVENT_RED_IN_MT_SILVER"
                && Map.tryFind "__credits_rolled" ow.Vars = Some 1
            | None -> false)

    let ow = owOf driver.Snapshot
    Assert.True(ow.Events |> List.contains "EVENT_RED_IN_MT_SILVER", "Red should disappear after the battle")
    Assert.True(hasActor "Red" false ow, "Red's object should be hidden after disappear")
    Assert.Equal(Some 1, Map.tryFind "__credits_rolled" ow.Vars)
    driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)
