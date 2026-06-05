module PokeGold.Tests.PackTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Scenes

// ── Test fixtures ──────────────────────────────────────────────────────────────
//
// Bag composition used by most tests:
//   Item    pocket: POTION x5, ANTIDOTE x2
//   Ball    pocket: POKE_BALL x10
//   KeyItem pocket: BICYCLE x1  (CantToss = true, FieldMenu = ITEMMENU_CLOSE)
//   TmHm    pocket: (empty — only CANCEL shown)
//
// Item pocket actions for POTION (FieldMenu = ITEMMENU_PARTY):
//   [USE, GIVE, TOSS, CANCEL]  — TOSS at index 2
// KeyItem pocket actions for BICYCLE (FieldMenu = ITEMMENU_CLOSE, CantToss = true):
//   [USE, TOSS, CANCEL]        — TOSS at index 1

let private makePlayer () : PlayerState =
    let bag =
        Bag.empty
        |> Bag.add "POTION"    5
        |> Bag.add "ANTIDOTE"  2
        |> Bag.add "POKE_BALL" 10
        |> Bag.add "BICYCLE"   1
    { PlayerStateOps.initial with Bag = bag }

/// Build a fresh PackScene and a getter for the most-recent onChange argument.
let private makeScene () =
    let mutable updated: PlayerState option = None
    let scene = PackScene(Content(), makePlayer (), fun p -> updated <- Some p)
    scene, fun () -> updated

/// Raw single-frame update.
let private update (scene: PackScene) (b: Buttons) : Transition =
    (scene :> Scene).Update(b)

/// Simulate a button tap: one frame held + one frame released; returns the
/// pressed-frame transition.  Use raw `update` when you need to intercept the
/// Push(YesNoScene) return before the release frame runs.
let private press (b: Buttons) (scene: PackScene) : Transition =
    let t = update scene b
    update scene Buttons.none |> ignore
    t

let private pressLeft  s = press { Buttons.none with Left  = true } s
let private pressRight s = press { Buttons.none with Right = true } s
let private pressUp    s = press { Buttons.none with Up    = true } s
let private pressDown  s = press { Buttons.none with Down  = true } s
let private pressA     s = press { Buttons.none with A     = true } s
let private pressB     s = press { Buttons.none with B     = true } s

// ── Pocket switching ───────────────────────────────────────────────────────────

[<Fact>]
let ``PackScene initial pocket is ITEM (0)`` () =
    let scene, _ = makeScene ()
    Assert.Equal(0, scene.PocketIndex)

[<Fact>]
let ``PackScene Right switches from ITEM to BALL`` () =
    let scene, _ = makeScene ()
    pressRight scene |> ignore
    Assert.Equal(1, scene.PocketIndex)

[<Fact>]
let ``PackScene Right cycles through all four pockets`` () =
    let scene, _ = makeScene ()
    for expected in [| 1; 2; 3; 0 |] do
        pressRight scene |> ignore
        Assert.Equal(expected, scene.PocketIndex)

[<Fact>]
let ``PackScene Left wraps from ITEM (0) to TM/HM (3)`` () =
    let scene, _ = makeScene ()
    pressLeft scene |> ignore
    Assert.Equal(3, scene.PocketIndex)

[<Fact>]
let ``PackScene Left decrements pocket`` () =
    let scene, _ = makeScene ()
    for _ in 1 .. 3 do pressRight scene |> ignore  // pocket = 3
    pressLeft scene |> ignore
    Assert.Equal(2, scene.PocketIndex)

[<Fact>]
let ``PackScene pocket switch preserves independent cursors`` () =
    let scene, _ = makeScene ()
    // Move cursor to row 1 in ITEM pocket, then switch to BALL pocket.
    pressDown scene |> ignore
    Assert.Equal(1, scene.Cursor)
    pressRight scene |> ignore          // now in BALL pocket
    Assert.Equal(0, scene.Cursor)       // BALL cursor is independent

// ── Cursor movement within a pocket ───────────────────────────────────────────

[<Fact>]
let ``PackScene Down moves cursor`` () =
    let scene, _ = makeScene ()
    pressDown scene |> ignore
    Assert.Equal(1, scene.Cursor)

[<Fact>]
let ``PackScene Up from cursor 0 stays at 0 (no wrap)`` () =
    let scene, _ = makeScene ()
    pressUp scene |> ignore
    Assert.Equal(0, scene.Cursor)

[<Fact>]
let ``PackScene Down reaches CANCEL row after last item`` () =
    let scene, _ = makeScene ()
    // ITEM pocket: POTION(0), ANTIDOTE(1), CANCEL(2) — press Down twice.
    pressDown scene |> ignore
    pressDown scene |> ignore
    Assert.Equal(2, scene.Cursor)

[<Fact>]
let ``PackScene A on CANCEL row returns Pop`` () =
    let scene, _ = makeScene ()
    // Navigate to CANCEL (index 2 in ITEM pocket).
    pressDown scene |> ignore
    pressDown scene |> ignore
    Assert.Equal(Pop, pressA scene)

[<Fact>]
let ``PackScene B returns Pop`` () =
    let scene, _ = makeScene ()
    Assert.Equal(Pop, pressB scene)

[<Fact>]
let ``PackScene Left/Right return Stay`` () =
    let scene, _ = makeScene ()
    Assert.Equal(Stay, pressLeft  scene)
    Assert.Equal(Stay, pressRight scene)

// ── Toss: full confirmed flow ──────────────────────────────────────────────────
//
// POTION is at cursor 0 in ITEM pocket.
// Actions = [USE, GIVE, TOSS, CANCEL]; TOSS is at action index 2.
// We drive the scene manually to avoid the press-helper releasing buttons at
// the wrong moment (pushing YesNoScene must not be immediately followed by a
// WaitToss frame before the YesNo callback fires).

let private navigateToToss (scene: PackScene) =
    // Open action menu for item at cursor 0 (POTION).
    update scene { Buttons.none with A = true } |> ignore
    update scene Buttons.none |> ignore
    // Navigate down to TOSS at index 2.
    for _ in 1 .. 2 do
        update scene { Buttons.none with Down = true } |> ignore
        update scene Buttons.none |> ignore
    // Confirm TOSS → enter TossQty mode (returns Stay).
    update scene { Buttons.none with A = true } |> ignore
    update scene Buttons.none |> ignore

[<Fact>]
let ``PackScene toss POTION x1 reduces count by 1`` () =
    let mutable updated: PlayerState option = None
    let scene = PackScene(Content(), makePlayer (), fun p -> updated <- Some p)
    navigateToToss scene

    // Now in TossQty(qty=1). Press A to push YesNoScene — DON'T release yet.
    let pushResult = update scene { Buttons.none with A = true }
    // Simulate the YesNoScene confirming YES (fires the onResult callback).
    match pushResult with
    | Push yesno ->
        yesno.Update({ Buttons.none with A = true }) |> ignore
    | _ ->
        Assert.Fail("Expected Push(YesNoScene) from TossQty confirm")

    // One more frame on PackScene processes the WaitToss result.
    update scene Buttons.none |> ignore

    Assert.True(updated.IsSome, "onChange should have been called")
    Assert.Equal(4, Bag.count "POTION" updated.Value.Bag)

[<Fact>]
let ``PackScene toss all POTION removes item from bag`` () =
    let mutable updated: PlayerState option = None
    let scene = PackScene(Content(), makePlayer (), fun p -> updated <- Some p)
    navigateToToss scene

    // In TossQty(qty=1, maxQty=5). Press Up 4 times to reach qty=5.
    for _ in 1 .. 4 do
        update scene { Buttons.none with Up = true } |> ignore
        update scene Buttons.none |> ignore

    // Confirm qty=5 — Push YesNoScene.
    let pushResult = update scene { Buttons.none with A = true }
    match pushResult with
    | Push yesno ->
        yesno.Update({ Buttons.none with A = true }) |> ignore
    | _ ->
        Assert.Fail("Expected Push(YesNoScene)")

    update scene Buttons.none |> ignore

    Assert.True(updated.IsSome)
    Assert.Equal(0, Bag.count "POTION" updated.Value.Bag)

[<Fact>]
let ``PackScene toss cancelled (NO) leaves bag unchanged`` () =
    let mutable updated: PlayerState option = None
    let scene = PackScene(Content(), makePlayer (), fun p -> updated <- Some p)
    navigateToToss scene

    let pushResult = update scene { Buttons.none with A = true }
    match pushResult with
    | Push yesno ->
        // Simulate NO: move cursor down (YES→NO) then press A.
        yesno.Update({ Buttons.none with Down = true }) |> ignore
        yesno.Update({ Buttons.none with A    = true }) |> ignore
    | _ ->
        Assert.Fail("Expected Push(YesNoScene)")

    update scene Buttons.none |> ignore

    Assert.True(updated.IsNone, "onChange must NOT be called when user selects NO")

[<Fact>]
let ``PackScene toss quantity clamps at 1 (Down in TossQty at min)`` () =
    let mutable updated: PlayerState option = None
    let scene = PackScene(Content(), makePlayer (), fun p -> updated <- Some p)
    navigateToToss scene

    // In TossQty(qty=1). Pressing Down tries to go to 0 — should clamp to 1.
    update scene { Buttons.none with Down = true } |> ignore
    update scene Buttons.none |> ignore

    // Confirm qty=1.
    let pushResult = update scene { Buttons.none with A = true }
    match pushResult with
    | Push yesno ->
        yesno.Update({ Buttons.none with A = true }) |> ignore
    | _ ->
        Assert.Fail("Expected Push(YesNoScene)")

    update scene Buttons.none |> ignore

    Assert.True(updated.IsSome)
    Assert.Equal(4, Bag.count "POTION" updated.Value.Bag)  // only 1 tossed

[<Fact>]
let ``PackScene toss B in TossQty cancels and returns to Browsing`` () =
    let mutable updated: PlayerState option = None
    let scene = PackScene(Content(), makePlayer (), fun p -> updated <- Some p)
    navigateToToss scene

    // Press B in TossQty → should return Stay and go back to Browsing.
    let t = press { Buttons.none with B = true } scene
    Assert.Equal(Stay, t)
    // onChange must not be called.
    Assert.True(updated.IsNone)

// ── Toss: CantToss items are refused ──────────────────────────────────────────
//
// BICYCLE is in KEY ITEM pocket (index 2).
// Its actions = [USE, TOSS, CANCEL]; TOSS is at action index 1.
// CantToss = true → pressing A on TOSS enters InfoMsg mode, not TossQty.

[<Fact>]
let ``PackScene CantToss item enters InfoMsg not TossQty`` () =
    let mutable updated: PlayerState option = None
    let scene = PackScene(Content(), makePlayer (), fun p -> updated <- Some p)

    // Switch to KEY ITEM pocket.
    pressRight scene |> ignore  // pocket 1
    pressRight scene |> ignore  // pocket 2 (KEY ITEM)
    Assert.Equal(2, scene.PocketIndex)

    // Open action menu for BICYCLE (cursor = 0).
    pressA scene |> ignore

    // Navigate to TOSS at action index 1.
    pressDown scene |> ignore

    // Press A on TOSS: cantToss=true → should enter InfoMsg, return Stay.
    // (In TossQty, A would return Stay too — so we check the NEXT A press:
    //  InfoMsg dismisses on A → Stay; TossQty confirms qty → Push(YesNo).)
    update scene { Buttons.none with A = true } |> ignore  // enters InfoMsg or TossQty
    update scene Buttons.none |> ignore

    // Discriminate: press A again.
    // InfoMsg: dismisses, returns Stay.
    // TossQty: pushes YesNoScene, returns Push.
    let t = update scene { Buttons.none with A = true }
    Assert.Equal(Stay, t)  // Must be Stay (InfoMsg dismissed), not Push

    update scene Buttons.none |> ignore

    // onChange was never called.
    Assert.True(updated.IsNone, "CantToss item must not mutate the bag")
    Assert.Equal(1, Bag.count "BICYCLE" (makePlayer ()).Bag)

[<Fact>]
let ``PackScene CantToss item B in InfoMsg dismisses and returns Stay`` () =
    let scene, _ = makeScene ()
    pressRight scene |> ignore
    pressRight scene |> ignore  // pocket 2 (KEY ITEM)
    pressA scene |> ignore      // action menu for BICYCLE
    pressDown scene |> ignore   // navigate to TOSS
    pressA scene |> ignore      // enters InfoMsg

    // B in InfoMsg should return Stay (not Pop).
    let t = pressB scene
    Assert.Equal(Stay, t)

// ── Action menu: B cancels back to browsing ────────────────────────────────────

[<Fact>]
let ``PackScene B in ActionMenu returns to Browsing and returns Stay`` () =
    let scene, _ = makeScene ()
    pressA scene |> ignore  // open action menu for POTION
    let t = pressB scene    // cancel
    Assert.Equal(Stay, t)

// ── Render smoke test ──────────────────────────────────────────────────────────

[<Fact>]
let ``PackScene Render draws non-zero pixels at main box top-left (tile 0,0)`` () =
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
let ``PackScene Render draws non-zero pixels at description box top-left (tile 0,11)`` () =
    let scene, _ = makeScene ()
    let fb = Framebuffer()
    (scene :> Scene).Render(fb)

    // Description box top-left is at pixel (0, 11*8) = (0, 88).
    let mutable anyNonZero = false
    for py in 88 .. 95 do
        for px in 0 .. 7 do
            let i = (py * Display.Width + px) * 4
            if fb.Pixels.[i] <> 0uy || fb.Pixels.[i+1] <> 0uy || fb.Pixels.[i+2] <> 0uy then
                anyNonZero <- true

    Assert.True(anyNonZero, "BoxTopLeft glyph at tile (0,11) should write non-zero pixels")
