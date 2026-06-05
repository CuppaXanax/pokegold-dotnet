module PokeGold.Tests.PCBoxTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Scenes

// ── Helpers ─────────────────────────────────────────────────────────────────

let private content () = Content()

let private makePlayer (partySize: int) : PlayerState =
    let party = List.init partySize (fun i -> PartyMon.create (1 + i) (5 + i))
    { PlayerState.initial with Party = party }

let private makeScene (player: PlayerState) =
    let mutable updated: PlayerState option = None
    let scene = PCBoxScene(content (), player, fun p -> updated <- Some p)
    scene, fun () -> updated

let private update (scene: PCBoxScene) (b: Buttons) : Transition =
    (scene :> Scene).Update(b)

let private press (b: Buttons) (scene: PCBoxScene) : Transition =
    let t = update scene b
    update scene Buttons.none |> ignore
    t

let private pressA    s = press { Buttons.none with A     = true } s
let private pressB    s = press { Buttons.none with B     = true } s
let private pressDown s = press { Buttons.none with Down  = true } s
let private pressUp   s = press { Buttons.none with Up    = true } s
let private pressRight s = press { Buttons.none with Right = true } s
let private pressLeft  s = press { Buttons.none with Left  = true } s

/// Single (non-debounced) A update. Use for presses that PUSH a sub-scene:
/// the real scene stack leaves PCBoxScene dormant while the sub-scene is on top,
/// so a debounce `update none` frame here would run the Wait branch prematurely.
let private tapA (scene: PCBoxScene) : Transition =
    update scene { Buttons.none with A = true }

// ── PCBoxScene opening state ─────────────────────────────────────────────────

[<Fact>]
let ``PCBoxScene starts in Browsing mode`` () =
    let scene, _ = makeScene (makePlayer 2)
    Assert.Equal(PCBoxMode.Browsing, scene.Mode)

[<Fact>]
let ``PCBoxScene B in Browsing returns Pop`` () =
    let scene, _ = makeScene (makePlayer 2)
    Assert.Equal(Pop, pressB scene)

// ── Navigation: change box ───────────────────────────────────────────────────

[<Fact>]
let ``PCBoxScene Right cycles to next box`` () =
    let p = makePlayer 2
    let scene, getUpdated = makeScene p
    pressRight scene |> ignore
    match getUpdated () with
    | Some p2 -> Assert.Equal(1, p2.Pc.CurrentBox)
    | None -> Assert.Fail("onChange not called")

[<Fact>]
let ``PCBoxScene Left wraps around to box 13`` () =
    let p = makePlayer 2
    let scene, getUpdated = makeScene p
    pressLeft scene |> ignore
    match getUpdated () with
    | Some p2 -> Assert.Equal(Storage.numBoxes - 1, p2.Pc.CurrentBox)
    | None -> Assert.Fail("onChange not called")

// ── CANCEL entry exits ───────────────────────────────────────────────────────

[<Fact>]
let ``A on CANCEL returns Pop`` () =
    // With empty box: CANCEL is at index 2 (DEPOSIT=0, CHANGE BOX=1, CANCEL=2).
    let scene, _ = makeScene (makePlayer 2)
    pressDown scene |> ignore  // to CHANGE BOX (1)
    pressDown scene |> ignore  // to CANCEL (2)
    Assert.Equal(Pop, pressA scene)

// ── DEPOSIT path ─────────────────────────────────────────────────────────────

[<Fact>]
let ``A on DEPOSIT pushes PartyScene`` () =
    let scene, _ = makeScene (makePlayer 3)
    // With empty box, DEPOSIT is at index 0 (first entry).
    match pressA scene with
    | Push pushed ->
        // pushed should be a PartyScene
        Assert.NotNull(pushed)
    | other -> Assert.Fail(sprintf "Expected Push(PartyScene), got %A" other)

[<Fact>]
let ``deposit via PartyScene picker updates Pc and shrinks party`` () =
    let p = makePlayer 3
    let scene, getUpdated = makeScene p
    // DEPOSIT is at index 0 with an empty box; press A to push PartyScene.
    let push = tapA scene
    match push with
    | Push partyScene ->
        // Picker mode: press A to select first party mon (index 0).
        partyScene.Update({ Buttons.none with A = true }) |> ignore
    | other -> Assert.Fail(sprintf "Expected Push, got %A" other)
    // Scene is now in DepositWait; one more Update() processes the result.
    update scene Buttons.none |> ignore
    match getUpdated () with
    | Some p2 ->
        Assert.Equal(2, p2.Party.Length)          // party shrank from 3 to 2
        Assert.Equal(1, p2.Pc.Boxes.[0].Mons.Length)  // box 0 gained a mon
    | None -> Assert.Fail("onChange not called after deposit")

[<Fact>]
let ``deposit rejected when box is full shows ShowMsg mode`` () =
    // Fill current box to capacity, then try to deposit.
    let p = makePlayer 3
    let mons  = List.init Storage.monsPerBox (fun i -> PartyMon.create (100 + i) 5)
    let box   = p.Pc.Boxes.[0]
    let boxes = p.Pc.Boxes |> Array.mapi (fun i b -> if i = 0 then { b with Mons = mons } else b)
    let p2 = { p with Pc = { p.Pc with Boxes = boxes } }
    let scene, _ = makeScene p2
    // With a full box, DEPOSIT is at index monsPerBox (20).
    // Navigate to DEPOSIT.
    for _ in 1 .. Storage.monsPerBox do
        pressDown scene |> ignore
    // Press A → should go DepositWait, push PartyScene.
    let push = tapA scene
    match push with
    | Push partyScene ->
        partyScene.Update({ Buttons.none with A = true }) |> ignore
    | other -> Assert.Fail(sprintf "Expected Push(PartyScene), got %A" other)
    update scene Buttons.none |> ignore
    // After failed deposit, mode should be ShowMsg.
    match scene.Mode with
    | PCBoxMode.ShowMsg msg -> Assert.Contains("full", msg.ToLower())
    | other -> Assert.Fail(sprintf "Expected ShowMsg after full-box deposit, got %A" other)

[<Fact>]
let ``deposit cancelled via B in PartyScene returns to Browsing`` () =
    let p = makePlayer 3
    let scene, _ = makeScene p
    let push = pressA scene  // open deposit picker
    match push with
    | Push partyScene ->
        // Press B to cancel.
        partyScene.Update({ Buttons.none with B = true }) |> ignore
    | other -> Assert.Fail(sprintf "Expected Push, got %A" other)
    update scene Buttons.none |> ignore
    Assert.Equal(PCBoxMode.Browsing, scene.Mode)

// ── WITHDRAW path─────────────────────────────────────────────────────────────

[<Fact>]
let ``withdraw from box via action menu updates party and box`` () =
    // Deposit a mon first.
    let p = makePlayer 3
    let p2 =
        match BoxOps.deposit 0 p with
        | Ok pp -> pp
        | Error e -> failwith e
    let scene, getUpdated = makeScene p2
    // Cursor is at index 0 (the newly deposited mon).
    let push = pressA scene  // open action menu
    match push with
    | Stay ->
        // Action menu should be open.
        match scene.Mode with
        | PCBoxMode.ActionMenu _ ->
            // A on WITHDRAW (index 0 in action menu).
            pressA scene |> ignore
            match getUpdated () with
            | Some p3 ->
                Assert.Equal(3, p3.Party.Length)          // party grew back to 3
                Assert.Equal(0, p3.Pc.Boxes.[0].Mons.Length)  // box empty again
            | None -> Assert.Fail("onChange not called")
        | other -> Assert.Fail(sprintf "Expected ActionMenu, got %A" other)
    | other -> Assert.Fail(sprintf "Expected Stay (action menu opened), got %A" other)

// ── RELEASE path ─────────────────────────────────────────────────────────────

[<Fact>]
let ``release via action menu removes mon from box`` () =
    let p = makePlayer 3
    let p2 = match BoxOps.deposit 0 p with Ok pp -> pp | Error e -> failwith e
    let scene, getUpdated = makeScene p2
    // Navigate to RELEASE in the action submenu.
    let push = pressA scene  // open action menu on the box mon (index 0 in list)
    match push with
    | Stay ->
        match scene.Mode with
        | PCBoxMode.ActionMenu _ ->
            // Navigate to RELEASE (index 2: WITHDRAW=0, STATS=1, RELEASE=2).
            pressDown scene |> ignore
            pressDown scene |> ignore
            // A on RELEASE → push YesNoScene.
            let yesNoPush = tapA scene
            match yesNoPush with
            | Push yesno ->
                // Confirm YES.
                yesno.Update({ Buttons.none with A = true }) |> ignore
            | other -> Assert.Fail(sprintf "Expected Push(YesNoScene), got %A" other)
            // Process ReleaseWait.
            update scene Buttons.none |> ignore
            match getUpdated () with
            | Some p3 ->
                Assert.Equal(0, p3.Pc.Boxes.[0].Mons.Length)  // box is empty
            | None -> Assert.Fail("onChange not called")
        | other -> Assert.Fail(sprintf "Expected ActionMenu, got %A" other)
    | other -> Assert.Fail(sprintf "Expected Stay, got %A" other)

// ── ShowMsg clears on A or B ────────────────────────────────────────────────

[<Fact>]
let ``ShowMsg clears on A`` () =
    let p = makePlayer 1  // single mon → deposit will fail
    let scene, _ = makeScene p
    let push = tapA scene  // attempt deposit
    match push with
    | Push partyScene ->
        partyScene.Update({ Buttons.none with A = true }) |> ignore
    | other -> Assert.Fail(sprintf "Expected Push, got %A" other)
    update scene Buttons.none |> ignore
    // Should be in ShowMsg (last mon rejection).
    match scene.Mode with
    | PCBoxMode.ShowMsg _ -> ()
    | other -> Assert.Fail(sprintf "Expected ShowMsg, got %A" other)
    pressA scene |> ignore
    Assert.Equal(PCBoxMode.Browsing, scene.Mode)

// ── PcMenuScene tests ─────────────────────────────────────────────────────────

let private makePcMenu (player: PlayerState) =
    let mutable updated: PlayerState option = None
    let scene = PcMenuScene(content (), player, fun p -> updated <- Some p)
    scene, fun () -> updated

let private pressAMenu (scene: PcMenuScene) =
    let t = (scene :> Scene).Update({ Buttons.none with A = true })
    (scene :> Scene).Update(Buttons.none) |> ignore
    t

let private pressBMenu (scene: PcMenuScene) =
    let t = (scene :> Scene).Update({ Buttons.none with B = true })
    (scene :> Scene).Update(Buttons.none) |> ignore
    t

let private pressDownMenu (scene: PcMenuScene) =
    (scene :> Scene).Update({ Buttons.none with Down = true }) |> ignore
    (scene :> Scene).Update(Buttons.none) |> ignore

[<Fact>]
let ``PcMenuScene B returns Pop`` () =
    let scene, _ = makePcMenu (makePlayer 2)
    Assert.Equal(Pop, pressBMenu scene)

[<Fact>]
let ``PcMenuScene A on BILL'S PC pushes PCBoxScene`` () =
    let scene, _ = makePcMenu (makePlayer 2)
    // BILL'S PC is at cursor 0.
    match pressAMenu scene with
    | Push pushed -> Assert.NotNull(pushed)
    | other -> Assert.Fail(sprintf "Expected Push(PCBoxScene), got %A" other)

[<Fact>]
let ``PcMenuScene A on LOG OFF returns Pop`` () =
    let scene, _ = makePcMenu (makePlayer 2)
    // LOG OFF is at cursor 2 (after BILL'S PC and PLAYER'S PC).
    pressDownMenu scene
    pressDownMenu scene
    Assert.Equal(Pop, pressAMenu scene)

// ── Script wiring: OpenPc effect ─────────────────────────────────────────────

open PokeGold.Game.Overworld.Script

[<Fact>]
let ``Special PokemonCenterPC suspends with OpenPc effect`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tspecial PokemonCenterPC\n\
             \tend\n"
    match (Script.start "S" World.empty prog "").Outcome with
    | Suspended(_, OpenPc) -> ()
    | other -> Assert.Fail(sprintf "Expected Suspended(_, OpenPc), got %A" other)

[<Fact>]
let ``OpenPc resumes with None after the PC closes`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tspecial PokemonCenterPC\n\
             \tsetval 42\n\
             \tend\n"
    let step1 = Script.start "S" World.empty prog ""
    match step1.Outcome with
    | Suspended(vm, OpenPc) ->
        let step2 = Script.resume None step1.World vm
        Assert.Equal(Completed, step2.Outcome)
    | other -> Assert.Fail(sprintf "Expected OpenPc suspension, got %A" other)
