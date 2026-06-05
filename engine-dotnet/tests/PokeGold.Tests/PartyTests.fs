module PokeGold.Tests.PartyTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Scenes

// ── Test fixtures ──────────────────────────────────────────────────────────────
//
// Party composition (3 mons, varied HP for HP-bar variety):
//   Slot 0: Cyndaquil (species 155) L5  — full HP
//   Slot 1: Pidgey    (species  16) L4  — reduced HP (≈ 1/3 max)
//   Slot 2: Totodile  (species 158) L5  — full HP

let private makePlayer () : PlayerState =
    let cyndaquil  = PartyMon.create 155 5
    let pidgey     = PartyMon.create 16  4
    let pidgeyWeak = { pidgey with Hp = max 1 (pidgey.MaxHp / 3) }
    let totodile   = PartyMon.create 158 5
    { PlayerStateOps.initial with Party = [ cyndaquil; pidgeyWeak; totodile ] }

/// Build a fresh PartyScene (default/menu mode) and a getter for the most-recent
/// `onChange` argument.
let private makeScene () =
    let mutable updated: PlayerState option = None
    let scene = PartyScene(Content(), makePlayer (), fun p -> updated <- Some p)
    scene, fun () -> updated

/// Build a PartyScene in picker mode (onSelect seam) with the given callback.
let private makePickerScene (picker: int -> Transition) =
    PartyScene(Content(), makePlayer (), (fun _ -> ()), picker)

/// Raw single-frame update.
let private update (scene: PartyScene) (b: Buttons) : Transition =
    (scene :> Scene).Update(b)

/// Simulate a button tap: one frame held (rising edge fires) + one frame released.
/// Returns the pressed-frame transition.
let private press (b: Buttons) (scene: PartyScene) : Transition =
    let t = update scene b
    update scene Buttons.none |> ignore
    t

let private pressDown  s = press { Buttons.none with Down  = true } s
let private pressUp    s = press { Buttons.none with Up    = true } s
let private pressA     s = press { Buttons.none with A     = true } s
let private pressB     s = press { Buttons.none with B     = true } s

/// Open the action submenu for the slot at the current cursor (press A then release).
let private openActionMenu (scene: PartyScene) =
    update scene { Buttons.none with A = true } |> ignore
    update scene Buttons.none |> ignore

// ── Cursor navigation ──────────────────────────────────────────────────────────

[<Fact>]
let ``PartyScene initial cursor is 0`` () =
    let scene, _ = makeScene ()
    Assert.Equal(0, scene.Cursor)

[<Fact>]
let ``PartyScene Down moves cursor from 0 to 1`` () =
    let scene, _ = makeScene ()
    pressDown scene |> ignore
    Assert.Equal(1, scene.Cursor)

[<Fact>]
let ``PartyScene Down moves through all slots and CANCEL`` () =
    let scene, _ = makeScene ()
    // 3 mons + CANCEL = 4 entries (indices 0..3)
    for expected in [| 1; 2; 3 |] do
        pressDown scene |> ignore
        Assert.Equal(expected, scene.Cursor)

[<Fact>]
let ``PartyScene Down wraps from CANCEL back to first slot`` () =
    let scene, _ = makeScene ()
    for _ in 1 .. 4 do pressDown scene |> ignore   // wrap: 0→1→2→3→0
    Assert.Equal(0, scene.Cursor)

[<Fact>]
let ``PartyScene Up from 0 wraps to last entry (CANCEL = 3)`` () =
    let scene, _ = makeScene ()
    pressUp scene |> ignore
    Assert.Equal(3, scene.Cursor)

[<Fact>]
let ``PartyScene Down returns Stay`` () =
    let scene, _ = makeScene ()
    Assert.Equal(Stay, pressDown scene)

[<Fact>]
let ``PartyScene Up returns Stay`` () =
    let scene, _ = makeScene ()
    Assert.Equal(Stay, pressUp scene)

// ── B / CANCEL close the scene ────────────────────────────────────────────────

[<Fact>]
let ``PartyScene B at top level returns Pop`` () =
    let scene, _ = makeScene ()
    Assert.Equal(Pop, pressB scene)

[<Fact>]
let ``PartyScene A on CANCEL row returns Pop`` () =
    let scene, _ = makeScene ()
    for _ in 1 .. 3 do pressDown scene |> ignore   // cursor → CANCEL (index 3)
    Assert.Equal(Pop, pressA scene)

// ── Action submenu ─────────────────────────────────────────────────────────────

[<Fact>]
let ``PartyScene A on party slot opens action submenu and returns Stay`` () =
    let scene, _ = makeScene ()
    let t = update scene { Buttons.none with A = true }
    Assert.Equal(Stay, t)

[<Fact>]
let ``PartyScene B in action submenu closes submenu and returns Stay`` () =
    let scene, _ = makeScene ()
    openActionMenu scene
    let t = pressB scene
    Assert.Equal(Stay, t)

[<Fact>]
let ``PartyScene STATS pushes a child scene (transition is Push)`` () =
    let scene, _ = makeScene ()
    openActionMenu scene
    // STATS is at action index 0; cursor starts there.
    let t = pressA scene
    match t with
    | Push _ -> ()
    | other  -> Assert.Fail(sprintf "Expected Push from STATS, got %A" other)

// ── SWITCH / reorder ───────────────────────────────────────────────────────────

[<Fact>]
let ``PartyScene SWITCH swaps slots 0 and 2 in onChange PlayerState`` () =
    let mutable updated: PlayerState option = None
    let scene = PartyScene(Content(), makePlayer (), fun p -> updated <- Some p)

    openActionMenu scene           // action menu for slot 0; cursor at STATS(0)
    pressDown scene |> ignore      // action cursor → SWITCH (index 1)
    pressA    scene |> ignore      // select SWITCH → SwitchPick(0); main cursor still at 0
    // Move main cursor to slot 2
    pressDown scene |> ignore
    pressDown scene |> ignore
    pressA    scene |> ignore      // confirm swap slots 0 ↔ 2

    Assert.True(updated.IsSome, "onChange should be called after SWITCH")
    let newParty = updated.Value.Party
    // Original: [Cyndaquil(155), Pidgey(16), Totodile(158)]
    // After swapping 0 ↔ 2: [Totodile(158), Pidgey(16), Cyndaquil(155)]
    Assert.Equal(158, newParty.[0].SpeciesId)
    Assert.Equal( 16, newParty.[1].SpeciesId)
    Assert.Equal(155, newParty.[2].SpeciesId)

[<Fact>]
let ``PartyScene SWITCH same slot does not call onChange`` () =
    let mutable updated: PlayerState option = None
    let scene = PartyScene(Content(), makePlayer (), fun p -> updated <- Some p)
    openActionMenu scene
    pressDown scene |> ignore     // action → SWITCH
    pressA    scene |> ignore     // enter SwitchPick; main cursor at 0
    pressA    scene |> ignore     // pick slot 0 again → no-op
    Assert.True(updated.IsNone, "onChange must not fire when swapping a slot with itself")

[<Fact>]
let ``PartyScene SWITCH B in SwitchPick returns Stay and does not call onChange`` () =
    let mutable updated: PlayerState option = None
    let scene = PartyScene(Content(), makePlayer (), fun p -> updated <- Some p)
    openActionMenu scene
    pressDown scene |> ignore     // action → SWITCH
    pressA    scene |> ignore     // enter SwitchPick
    let t = pressB scene          // cancel switch
    Assert.Equal(Stay, t)
    Assert.True(updated.IsNone)

// ── ITEM / held-item take ──────────────────────────────────────────────────────

[<Fact>]
let ``PartyScene ITEM on mon with no held item returns Stay`` () =
    let scene, _ = makeScene ()
    openActionMenu scene
    pressDown scene |> ignore    // action → SWITCH
    pressDown scene |> ignore    // action → ITEM (index 2)
    let t = pressA scene
    Assert.Equal(Stay, t)

[<Fact>]
let ``PartyScene ITEM on mon with no held item does not call onChange`` () =
    let mutable updated: PlayerState option = None
    let scene = PartyScene(Content(), makePlayer (), fun p -> updated <- Some p)
    openActionMenu scene
    pressDown scene |> ignore
    pressDown scene |> ignore    // action → ITEM
    pressA scene |> ignore
    Assert.True(updated.IsNone, "onChange must not fire when mon holds nothing")

[<Fact>]
let ``PartyScene ITEM takes held item into bag and calls onChange`` () =
    let mutable updated: PlayerState option = None
    let player = makePlayer ()
    // Give slot 0 a held POTION.
    let mon0    = List.item 0 player.Party
    let player' = { player with Party = { mon0 with HeldItem = Some "POTION" } :: List.tail player.Party }
    let scene   = PartyScene(Content(), player', fun p -> updated <- Some p)

    openActionMenu scene
    pressDown scene |> ignore   // action → SWITCH
    pressDown scene |> ignore   // action → ITEM
    pressA    scene |> ignore   // take the item

    Assert.True(updated.IsSome, "onChange should fire after taking held item")
    Assert.True(updated.Value.Party.[0].HeldItem.IsNone, "HeldItem should be cleared after take")
    Assert.Equal(1, Bag.count "POTION" updated.Value.Bag)

// ── onSelect picker seam ──────────────────────────────────────────────────────

[<Fact>]
let ``PartyScene onSelect mode: A on slot 0 calls onSelect with 0`` () =
    let mutable picked = -1
    let scene = makePickerScene (fun i -> picked <- i; Stay)
    pressA scene |> ignore
    Assert.Equal(0, picked)

[<Fact>]
let ``PartyScene onSelect mode: A on slot 1 calls onSelect with 1`` () =
    let mutable picked = -1
    let scene = makePickerScene (fun i -> picked <- i; Stay)
    pressDown scene |> ignore
    pressA scene |> ignore
    Assert.Equal(1, picked)

[<Fact>]
let ``PartyScene onSelect mode: A returns Transition from onSelect`` () =
    let child = TextBoxScene.Of(Content(), "test<DONE>") :> Scene
    let scene = makePickerScene (fun _ -> Push child)
    let t = pressA scene
    Assert.Equal(Push child, t)

[<Fact>]
let ``PartyScene onSelect mode: A on CANCEL row still returns Pop`` () =
    let scene = makePickerScene (fun _ -> Stay)
    for _ in 1 .. 3 do pressDown scene |> ignore   // cursor → CANCEL
    Assert.Equal(Pop, pressA scene)

[<Fact>]
let ``PartyScene onSelect mode: B still returns Pop`` () =
    let scene = makePickerScene (fun _ -> Stay)
    Assert.Equal(Pop, pressB scene)

// ── Render smoke test ─────────────────────────────────────────────────────────

[<Fact>]
let ``PartyScene Render draws non-zero pixels at box top-left (tile 0,0)`` () =
    let scene, _ = makeScene ()
    let fb = Framebuffer()
    (scene :> Scene).Render(fb)
    let mutable anyNonZero = false
    for py in 0 .. 7 do
        for px in 0 .. 7 do
            let i = (py * Display.Width + px) * 4
            if fb.Pixels.[i] <> 0uy || fb.Pixels.[i+1] <> 0uy || fb.Pixels.[i+2] <> 0uy then
                anyNonZero <- true
    Assert.True(anyNonZero, "BoxTopLeft glyph at tile (0,0) should write non-zero pixels")

[<Fact>]
let ``DVs 0xAFFF are shiny (atk=10, def=15, spd=15, spc=15)`` () =
    Assert.True(PartyMon.isShiny 0xAFFF)

[<Fact>]
let ``DVs 0x0000 are not shiny (atk=0, bit 1 not set)`` () =
    Assert.False(PartyMon.isShiny 0x0000)

[<Fact>]
let ``DVs 0x2AAA are shiny (atk=2, def=10, spd=10, spc=10)`` () =
    Assert.True(PartyMon.isShiny 0x2AAA)

[<Fact>]
let ``DVs 0x2A9A are not shiny (spd=9, below threshold)`` () =
    Assert.False(PartyMon.isShiny 0x2A9A)

[<Fact>]
let ``DVs 0x1FFF are not shiny (atk=1, bit 1 not set)`` () =
    Assert.False(PartyMon.isShiny 0x1FFF)

[<Fact>]
let ``DVs 0x0000 yield Unown letter A (index 0)`` () =
    Assert.Equal(0, PartyMon.unownLetter 0x0000)
    Assert.Equal('A', PartyMon.unownChar 0)

[<Fact>]
let ``DVs 0xFFFF yield Unown letter Z (index 25)`` () =
    Assert.Equal(25, PartyMon.unownLetter 0xFFFF)
    Assert.Equal('Z', PartyMon.unownChar 25)

[<Fact>]
let ``DVs produce expected intermediate letters`` () =
    Assert.Equal(25, PartyMon.unownLetter 0x6666)
    Assert.Equal(8, PartyMon.unownLetter 0x2222)
