module PokeGold.Tests.PlayerPCTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Scenes

// ── Helpers ───────────────────────────────────────────────────────────────────

let private content () = Content()

let private makeScene (player: PlayerState) =
    let mutable updated: PlayerState option = None
    let scene = PlayerPCScene(content (), player, fun p -> updated <- Some p)
    scene, fun () -> updated

let private update (scene: PlayerPCScene) (b: Buttons) : Transition =
    (scene :> Scene).Update(b)

/// Press + release (debounced). Safe for in-scene actions that don't push a sub-scene.
let private press (b: Buttons) (scene: PlayerPCScene) : Transition =
    let t = update scene b
    update scene Buttons.none |> ignore
    t

let private pressA     s = press { Buttons.none with A    = true } s
let private pressB     s = press { Buttons.none with B    = true } s
let private pressDown  s = press { Buttons.none with Down = true } s

let private makeMail (author: string) (body: string) : Mail =
    { Author = author; Body = body; Species = 1 }

// ── Opening state ─────────────────────────────────────────────────────────────

[<Fact>]
let ``PlayerPCScene starts in PcItemMain mode`` () =
    let scene, _ = makeScene PlayerStateOps.initial
    Assert.Equal(PlayerPCMode.PcItemMain, scene.Mode)

[<Fact>]
let ``B in PcItemMain returns Pop`` () =
    let scene, _ = makeScene PlayerStateOps.initial
    Assert.Equal(Pop, pressB scene)

// ── LOG OFF ───────────────────────────────────────────────────────────────────

[<Fact>]
let ``A on LOG OFF returns Pop`` () =
    let scene, _ = makeScene PlayerStateOps.initial
    // LOG OFF is at cursor 4 (WITHDRAW=0, DEPOSIT=1, TOSS=2, MAIL BOX=3, LOG OFF=4).
    for _ in 1 .. 4 do pressDown scene |> ignore
    Assert.Equal(Pop, pressA scene)

// ── WITHDRAW with empty stash ─────────────────────────────────────────────────

[<Fact>]
let ``A on WITHDRAW with empty stash shows PcItemMsg mode`` () =
    let scene, _ = makeScene PlayerStateOps.initial
    // WITHDRAW is at cursor 0 — just press A.
    pressA scene |> ignore
    match scene.Mode with
    | PlayerPCMode.PcItemMsg msg -> Assert.Contains("items", msg.ToLower())
    | other -> Assert.Fail(sprintf "Expected PcItemMsg, got %A" other)

// ── DEPOSIT flow ──────────────────────────────────────────────────────────────

[<Fact>]
let ``A on DEPOSIT with depositable items enters PcItemPick mode`` () =
    let player = { PlayerStateOps.initial with Bag = Bag.add "POTION" 3 Bag.empty }
    let scene, _ = makeScene player
    // DEPOSIT is at cursor 1.
    pressDown scene |> ignore
    pressA scene |> ignore
    match scene.Mode with
    | PlayerPCMode.PcItemPick("DEPOSIT", _) -> ()
    | other -> Assert.Fail(sprintf "Expected PcItemPick(DEPOSIT,_), got %A" other)

[<Fact>]
let ``A on DEPOSIT with empty depositable list shows PcItemMsg`` () =
    // Empty bag and no depositable items (only KeyItems in bag are excluded).
    let scene, _ = makeScene PlayerStateOps.initial
    pressDown scene |> ignore   // move to DEPOSIT
    pressA scene |> ignore
    match scene.Mode with
    | PlayerPCMode.PcItemMsg msg -> Assert.Contains("items", msg.ToLower())
    | other -> Assert.Fail(sprintf "Expected PcItemMsg, got %A" other)

[<Fact>]
let ``A in PcItemPick DEPOSIT deposits item and fires onChange`` () =
    let player = { PlayerStateOps.initial with Bag = Bag.add "POTION" 3 Bag.empty }
    let scene, getUpdated = makeScene player
    // Navigate to DEPOSIT, enter pick mode.
    pressDown scene |> ignore
    pressA scene |> ignore
    // Now in PcItemPick — press A to deposit first item (POTION).
    pressA scene |> ignore
    match getUpdated () with
    | Some p2 ->
        Assert.Equal(2, Bag.count "POTION" p2.Bag)        // bag shrank by 1
        Assert.Equal(1, p2.Pc.PcItems.Length)              // stash gained 1
        Assert.Equal(("POTION", 1), p2.Pc.PcItems.[0])
    | None -> Assert.Fail("onChange not called after deposit")

[<Fact>]
let ``B in PcItemPick returns to PcItemMain`` () =
    let player = { PlayerStateOps.initial with Bag = Bag.add "POTION" 3 Bag.empty }
    let scene, _ = makeScene player
    pressDown scene |> ignore
    pressA scene |> ignore
    // Should be in PcItemPick now.
    pressB scene |> ignore
    Assert.Equal(PlayerPCMode.PcItemMain, scene.Mode)

// ── WITHDRAW flow ─────────────────────────────────────────────────────────────

[<Fact>]
let ``A in PcItemPick WITHDRAW withdraws item and fires onChange`` () =
    // Seed PC stash with a POTION.
    let pc = { Storage.empty with PcItems = [("POTION", 3)] }
    let player = { PlayerStateOps.initial with Pc = pc }
    let scene, getUpdated = makeScene player
    // WITHDRAW is at cursor 0; press A to enter pick mode.
    pressA scene |> ignore
    // In PcItemPick — press A to withdraw first item.
    pressA scene |> ignore
    match getUpdated () with
    | Some p2 ->
        Assert.Equal(1, Bag.count "POTION" p2.Bag)
        let stashed = p2.Pc.PcItems |> List.tryFind (fun (id, _) -> id = "POTION") |> Option.map snd |> Option.defaultValue 0
        Assert.Equal(2, stashed)
    | None -> Assert.Fail("onChange not called after withdraw")

// ── PcItemMsg clears on A ─────────────────────────────────────────────────────

[<Fact>]
let ``PcItemMsg clears on A and returns to PcItemMain`` () =
    let scene, _ = makeScene PlayerStateOps.initial
    pressA scene |> ignore  // WITHDRAW with empty stash → PcItemMsg
    match scene.Mode with
    | PlayerPCMode.PcItemMsg _ -> ()
    | other -> Assert.Fail(sprintf "Expected PcItemMsg, got %A" other)
    pressA scene |> ignore
    Assert.Equal(PlayerPCMode.PcItemMain, scene.Mode)

// ── MAIL BOX ──────────────────────────────────────────────────────────────────

[<Fact>]
let ``A on MAIL BOX with no mail shows PcItemMsg`` () =
    let scene, _ = makeScene PlayerStateOps.initial
    // MAIL BOX is at cursor 3.
    for _ in 1 .. 3 do pressDown scene |> ignore
    pressA scene |> ignore
    match scene.Mode with
    | PlayerPCMode.PcItemMsg msg -> Assert.Contains("mail", msg.ToLower())
    | other -> Assert.Fail(sprintf "Expected PcItemMsg, got %A" other)

[<Fact>]
let ``A on MAIL BOX with mail enters PcMailBrowse`` () =
    let mail = makeMail "SILVER" "Battle!"
    let pc   = { Storage.empty with Mailbox = [mail] }
    let player = { PlayerStateOps.initial with Pc = pc }
    let scene, _ = makeScene player
    for _ in 1 .. 3 do pressDown scene |> ignore
    pressA scene |> ignore
    match scene.Mode with
    | PlayerPCMode.PcMailBrowse _ -> ()
    | other -> Assert.Fail(sprintf "Expected PcMailBrowse, got %A" other)

[<Fact>]
let ``A in PcMailBrowse enters PcMailRead`` () =
    let mail = makeMail "SILVER" "Battle!"
    let pc   = { Storage.empty with Mailbox = [mail] }
    let player = { PlayerStateOps.initial with Pc = pc }
    let scene, _ = makeScene player
    for _ in 1 .. 3 do pressDown scene |> ignore
    pressA scene |> ignore   // enter PcMailBrowse
    pressA scene |> ignore   // select first mail → PcMailRead
    match scene.Mode with
    | PlayerPCMode.PcMailRead 0 -> ()
    | other -> Assert.Fail(sprintf "Expected PcMailRead 0, got %A" other)

[<Fact>]
let ``A in PcMailRead returns to PcMailBrowse`` () =
    let mail = makeMail "SILVER" "Battle!"
    let pc   = { Storage.empty with Mailbox = [mail] }
    let player = { PlayerStateOps.initial with Pc = pc }
    let scene, _ = makeScene player
    for _ in 1 .. 3 do pressDown scene |> ignore
    pressA scene |> ignore   // PcMailBrowse
    pressA scene |> ignore   // PcMailRead
    pressA scene |> ignore   // back to PcMailBrowse
    match scene.Mode with
    | PlayerPCMode.PcMailBrowse _ -> ()
    | other -> Assert.Fail(sprintf "Expected PcMailBrowse, got %A" other)

[<Fact>]
let ``B in PcMailBrowse returns to PcItemMain`` () =
    let mail = makeMail "RED" "Hey"
    let pc   = { Storage.empty with Mailbox = [mail] }
    let player = { PlayerStateOps.initial with Pc = pc }
    let scene, _ = makeScene player
    for _ in 1 .. 3 do pressDown scene |> ignore
    pressA scene |> ignore   // PcMailBrowse
    pressB scene |> ignore   // back to main
    Assert.Equal(PlayerPCMode.PcItemMain, scene.Mode)

// ── PcMenuScene wiring ────────────────────────────────────────────────────────

[<Fact>]
let ``PcMenuScene A on PLAYER'S PC pushes PlayerPCScene`` () =
    let mutable updated: PlayerState option = None
    let scene = PcMenuScene(content (), PlayerStateOps.initial, fun p -> updated <- Some p)
    // PLAYER'S PC is at cursor 1 (BILL'S PC=0, PLAYER'S PC=1, LOG OFF=2).
    (scene :> Scene).Update({ Buttons.none with Down = true }) |> ignore
    (scene :> Scene).Update(Buttons.none) |> ignore
    let t = (scene :> Scene).Update({ Buttons.none with A = true })
    (scene :> Scene).Update(Buttons.none) |> ignore
    match t with
    | Push pushed ->
        // pushed should be a PlayerPCScene
        Assert.NotNull(pushed)
    | other -> Assert.Fail(sprintf "Expected Push(PlayerPCScene), got %A" other)
