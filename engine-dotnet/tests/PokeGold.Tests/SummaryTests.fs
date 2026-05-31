module PokeGold.Tests.SummaryTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Battle
open PokeGold.Game.Player
open PokeGold.Game.Scenes

// ── Fixtures ───────────────────────────────────────────────────────────────────
//
// Cyndaquil (species 155) at level 10 with two moves (SCRATCH=10, EMBER=52)
// and a held POTION; used across most tests.

let private makeMon () : PartyMon =
    let mon = PartyMon.create 155 10
    { mon with
        Moves    = [ (10, 30); (52, 20) ]   // SCRATCH (id 10), EMBER (id 52)
        HeldItem = Some "POTION"
        OtName   = "GOLD"
        OtId     = 12345 }

let private makeScene () = SummaryScene(Content(), makeMon ())

let private update (scene: SummaryScene) (b: Buttons) : Transition =
    (scene :> Scene).Update(b)

/// Simulate a single button press: one frame held then one frame released.
let private press (b: Buttons) (scene: SummaryScene) : Transition =
    let t = update scene b
    update scene Buttons.none |> ignore
    t

let private pressRight s = press { Buttons.none with Right = true } s
let private pressLeft  s = press { Buttons.none with Left  = true } s
let private pressB     s = press { Buttons.none with B     = true } s

// ── Page navigation ─────────────────────────────────────────────────────────────

[<Fact>]
let ``SummaryScene initial page is 0 (Info)`` () =
    let scene = makeScene ()
    Assert.Equal(0, scene.Page)

[<Fact>]
let ``SummaryScene Right advances to page 1`` () =
    let scene = makeScene ()
    pressRight scene |> ignore
    Assert.Equal(1, scene.Page)

[<Fact>]
let ``SummaryScene Right advances to page 2`` () =
    let scene = makeScene ()
    pressRight scene |> ignore
    pressRight scene |> ignore
    Assert.Equal(2, scene.Page)

[<Fact>]
let ``SummaryScene Right wraps from page 2 back to page 0`` () =
    let scene = makeScene ()
    for _ in 1 .. 3 do pressRight scene |> ignore
    Assert.Equal(0, scene.Page)

[<Fact>]
let ``SummaryScene Left from page 0 wraps to page 2`` () =
    let scene = makeScene ()
    pressLeft scene |> ignore
    Assert.Equal(2, scene.Page)

[<Fact>]
let ``SummaryScene Left from page 2 goes to page 1`` () =
    let scene = makeScene ()
    pressLeft scene |> ignore   // 0 → 2
    pressLeft scene |> ignore   // 2 → 1
    Assert.Equal(1, scene.Page)

[<Fact>]
let ``SummaryScene Right returns Stay`` () =
    let scene = makeScene ()
    Assert.Equal(Stay, pressRight scene)

[<Fact>]
let ``SummaryScene Left returns Stay`` () =
    let scene = makeScene ()
    Assert.Equal(Stay, pressLeft scene)

[<Fact>]
let ``SummaryScene B returns Pop from page 0`` () =
    let scene = makeScene ()
    Assert.Equal(Pop, pressB scene)

[<Fact>]
let ``SummaryScene B returns Pop from page 1`` () =
    let scene = makeScene ()
    pressRight scene |> ignore
    Assert.Equal(Pop, pressB scene)

[<Fact>]
let ``SummaryScene B returns Pop from page 2`` () =
    let scene = makeScene ()
    pressRight scene |> ignore
    pressRight scene |> ignore
    Assert.Equal(Pop, pressB scene)

// ── Summary.statsOf pure helper ─────────────────────────────────────────────────

// Cyndaquil base stats (verified by BattleTests): Hp=39, Atk=52, Def=43, Spd=65, SpA=60, SpD=50.

[<Fact>]
let ``Summary.statsOf Cyndaquil L10 MaxHp matches BattleMon.calcHp`` () =
    let mon = PartyMon.create 155 10
    let stats = Summary.statsOf mon
    let cyndaquil = Species.all |> Map.tryPick (fun _ s -> if s.Dex = 155 then Some s else None)
    match cyndaquil with
    | Some s -> Assert.Equal(BattleMon.calcHp s.Hp 10, stats.MaxHp)
    | None   -> Assert.Fail("Cyndaquil not found in species data")

[<Fact>]
let ``Summary.statsOf Cyndaquil L10 Atk matches BattleMon.calcStat`` () =
    let mon = PartyMon.create 155 10
    let stats = Summary.statsOf mon
    let cyndaquil = Species.all |> Map.tryPick (fun _ s -> if s.Dex = 155 then Some s else None)
    match cyndaquil with
    | Some s -> Assert.Equal(BattleMon.calcStat s.Attack 10, stats.Atk)
    | None   -> Assert.Fail("Cyndaquil not found in species data")

[<Fact>]
let ``Summary.statsOf Cyndaquil L10 Speed matches BattleMon.calcStat`` () =
    let mon = PartyMon.create 155 10
    let stats = Summary.statsOf mon
    let cyndaquil = Species.all |> Map.tryPick (fun _ s -> if s.Dex = 155 then Some s else None)
    match cyndaquil with
    | Some s -> Assert.Equal(BattleMon.calcStat s.Speed 10, stats.Spd)
    | None   -> Assert.Fail("Cyndaquil not found in species data")

[<Fact>]
let ``Summary.statsOf Cyndaquil L10 SpA matches BattleMon.calcStat`` () =
    let mon = PartyMon.create 155 10
    let stats = Summary.statsOf mon
    let cyndaquil = Species.all |> Map.tryPick (fun _ s -> if s.Dex = 155 then Some s else None)
    match cyndaquil with
    | Some s -> Assert.Equal(BattleMon.calcStat s.SpAttack 10, stats.SpA)
    | None   -> Assert.Fail("Cyndaquil not found in species data")

[<Fact>]
let ``Summary.statsOf unknown species returns default fallback`` () =
    let mon = { PartyMon.create 155 10 with SpeciesId = 9999 }
    let stats = Summary.statsOf mon
    Assert.Equal(5, stats.Atk)
    Assert.Equal(1, stats.MaxHp)

// ── Moves.tryByIndex ─────────────────────────────────────────────────────────────

[<Fact>]
let ``Moves.tryByIndex 1 returns POUND`` () =
    match Moves.tryByIndex 1 with
    | Some m -> Assert.Equal("POUND", m.Name)
    | None   -> Assert.Fail("Expected POUND at index 1")

[<Fact>]
let ``Moves.tryByIndex 15 returns CUT`` () =
    // CUT = 0x0f = 15 in the GSC move constants.
    match Moves.tryByIndex 15 with
    | Some m -> Assert.Equal("CUT", m.Name)
    | None   -> Assert.Fail("Expected CUT at index 15")

[<Fact>]
let ``Moves.tryByIndex 0 returns None (NO_MOVE)`` () =
    Assert.True(Moves.tryByIndex(0).IsNone, "Index 0 (NO_MOVE) should return None")

[<Fact>]
let ``Moves.tryByIndex out-of-range returns None`` () =
    Assert.True(Moves.tryByIndex(9999).IsNone)
    Assert.True(Moves.tryByIndex(-1).IsNone)

// ── Render smoke tests ────────────────────────────────────────────────────────────

[<Fact>]
let ``SummaryScene renders non-zero pixels on every page`` () =
    let mon = makeMon ()
    for p in 0 .. 2 do
        let scene = SummaryScene(Content(), mon)
        // Navigate to page p.
        for _ in 1 .. p do
            (scene :> Scene).Update({ Buttons.none with Right = true }) |> ignore
            (scene :> Scene).Update(Buttons.none) |> ignore
        let fb = Framebuffer()
        (scene :> Scene).Render(fb)
        let mutable anyNonZero = false
        for py in 0 .. 7 do
            for px in 0 .. 7 do
                let i = (py * Display.Width + px) * 4
                if fb.Pixels.[i] <> 0uy || fb.Pixels.[i+1] <> 0uy || fb.Pixels.[i+2] <> 0uy then
                    anyNonZero <- true
        Assert.True(anyNonZero, sprintf "Page %d should render non-zero pixels at tile (0,0)" p)

[<Fact>]
let ``SummaryScene renders with empty moves list without crashing`` () =
    let mon = { makeMon () with Moves = [] }
    let scene = SummaryScene(Content(), mon)
    // Navigate to moves page.
    pressRight scene |> ignore
    pressRight scene |> ignore
    let fb = Framebuffer()
    (scene :> Scene).Render(fb)   // must not throw
