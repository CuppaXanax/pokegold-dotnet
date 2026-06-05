module PokeGold.Tests.PcItemTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Save

// ── Helpers ──────────────────────────────────────────────────────────────────

let private withBagItem (itemId: string) (qty: int) (player: PlayerState) : PlayerState =
    { player with Bag = Bag.add itemId qty player.Bag }

let private withStashItem (itemId: string) (qty: int) (player: PlayerState) : PlayerState =
    let newStash = player.Pc.PcItems @ [(itemId, qty)]
    { player with Pc = { player.Pc with PcItems = newStash } }

let private makeMail (author: string) (body: string) : Mail =
    { Author = author; Body = body; Species = 1 }

// ── depositItem ──────────────────────────────────────────────────────────────

[<Fact>]
let ``depositItem moves item from bag to stash`` () =
    let p = PlayerStateOps.initial |> withBagItem "POTION" 3
    match PcItemOps.depositItem "POTION" 2 p with
    | Ok p2 ->
        Assert.Equal(1, Bag.count "POTION" p2.Bag)
        Assert.Equal(1, p2.Pc.PcItems.Length)
        Assert.Equal(("POTION", 2), p2.Pc.PcItems.[0])
    | Error e -> Assert.Fail(sprintf "depositItem failed: %s" e)

[<Fact>]
let ``depositItem fails when bag has fewer than qty`` () =
    let p = PlayerStateOps.initial |> withBagItem "POTION" 1
    match PcItemOps.depositItem "POTION" 2 p with
    | Error _ -> ()
    | Ok _ -> Assert.Fail("Should have rejected deposit with insufficient bag count")

[<Fact>]
let ``depositItem removes bag entry when count reaches zero`` () =
    let p = PlayerStateOps.initial |> withBagItem "POTION" 2
    match PcItemOps.depositItem "POTION" 2 p with
    | Ok p2 -> Assert.Equal(0, Bag.count "POTION" p2.Bag)
    | Error e -> Assert.Fail(e)

[<Fact>]
let ``depositItem stacks into an existing stash entry`` () =
    let p = PlayerStateOps.initial |> withBagItem "POTION" 5 |> withStashItem "POTION" 10
    match PcItemOps.depositItem "POTION" 3 p with
    | Ok p2 ->
        let stashed = p2.Pc.PcItems |> List.find (fun (id, _) -> id = "POTION") |> snd
        Assert.Equal(13, stashed)
    | Error e -> Assert.Fail(e)

[<Fact>]
let ``depositItem caps stash stack at 99`` () =
    let p = PlayerStateOps.initial |> withBagItem "POTION" 10 |> withStashItem "POTION" 95
    match PcItemOps.depositItem "POTION" 10 p with
    | Ok p2 ->
        let stashed = p2.Pc.PcItems |> List.find (fun (id, _) -> id = "POTION") |> snd
        Assert.Equal(99, stashed)
    | Error e -> Assert.Fail(e)

// ── withdrawItem ─────────────────────────────────────────────────────────────

[<Fact>]
let ``withdrawItem moves item from stash to bag`` () =
    let p = PlayerStateOps.initial |> withStashItem "POTION" 5
    match PcItemOps.withdrawItem "POTION" 2 p with
    | Ok p2 ->
        Assert.Equal(2, Bag.count "POTION" p2.Bag)
        let stashed = p2.Pc.PcItems |> List.tryFind (fun (id, _) -> id = "POTION") |> Option.map snd |> Option.defaultValue 0
        Assert.Equal(3, stashed)
    | Error e -> Assert.Fail(sprintf "withdrawItem failed: %s" e)

[<Fact>]
let ``withdrawItem removes stash entry when count reaches zero`` () =
    let p = PlayerStateOps.initial |> withStashItem "POTION" 2
    match PcItemOps.withdrawItem "POTION" 2 p with
    | Ok p2 ->
        Assert.Empty(p2.Pc.PcItems)
        Assert.Equal(2, Bag.count "POTION" p2.Bag)
    | Error e -> Assert.Fail(e)

[<Fact>]
let ``withdrawItem fails when stash has fewer than qty`` () =
    let p = PlayerStateOps.initial |> withStashItem "POTION" 1
    match PcItemOps.withdrawItem "POTION" 2 p with
    | Error _ -> ()
    | Ok _ -> Assert.Fail("Should have rejected withdraw with insufficient stash count")

[<Fact>]
let ``withdrawItem fails when item not in stash at all`` () =
    let p = PlayerStateOps.initial
    match PcItemOps.withdrawItem "POTION" 1 p with
    | Error _ -> ()
    | Ok _ -> Assert.Fail("Should have rejected withdraw of absent item")

// ── deposit→withdraw round-trip ───────────────────────────────────────────────

[<Fact>]
let ``deposit then withdraw round-trips correctly`` () =
    let p = PlayerStateOps.initial |> withBagItem "POTION" 5
    match PcItemOps.depositItem "POTION" 3 p with
    | Ok p2 ->
        match PcItemOps.withdrawItem "POTION" 3 p2 with
        | Ok p3 ->
            Assert.Equal(5, Bag.count "POTION" p3.Bag)
            Assert.Empty(p3.Pc.PcItems)
        | Error e -> Assert.Fail(e)
    | Error e -> Assert.Fail(e)

// ── tossItem ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``tossItem removes items from stash`` () =
    let p = PlayerStateOps.initial |> withStashItem "POTION" 5
    let p2 = PcItemOps.tossItem "POTION" 3 p
    let stashed = p2.Pc.PcItems |> List.tryFind (fun (id, _) -> id = "POTION") |> Option.map snd |> Option.defaultValue 0
    Assert.Equal(2, stashed)

[<Fact>]
let ``tossItem removes all when qty equals stash count`` () =
    let p = PlayerStateOps.initial |> withStashItem "POTION" 3
    let p2 = PcItemOps.tossItem "POTION" 3 p
    Assert.Empty(p2.Pc.PcItems)

[<Fact>]
let ``tossItem clamps silently when qty exceeds stash count`` () =
    let p = PlayerStateOps.initial |> withStashItem "POTION" 2
    let p2 = PcItemOps.tossItem "POTION" 99 p
    Assert.Empty(p2.Pc.PcItems)

[<Fact>]
let ``tossItem is a no-op for absent item`` () =
    let p = PlayerStateOps.initial
    let p2 = PcItemOps.tossItem "POTION" 1 p
    Assert.Equal(p, p2)

// ── storeMail ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``storeMail appends to mailbox`` () =
    let p    = PlayerStateOps.initial
    let mail = makeMail "RED" "Hello!"
    match PcItemOps.storeMail mail p with
    | Ok p2 ->
        Assert.Equal(1, p2.Pc.Mailbox.Length)
        Assert.Equal(mail, p2.Pc.Mailbox.[0])
    | Error e -> Assert.Fail(sprintf "storeMail failed: %s" e)

[<Fact>]
let ``storeMail preserves insertion order`` () =
    let p = PlayerStateOps.initial
    let m1 = makeMail "RED"   "Hi"
    let m2 = makeMail "BLUE"  "Yo"
    let p2 =
        match PcItemOps.storeMail m1 p with
        | Ok pp -> match PcItemOps.storeMail m2 pp with Ok ppp -> ppp | Error e -> failwith e
        | Error e -> failwith e
    Assert.Equal(2, p2.Pc.Mailbox.Length)
    Assert.Equal("RED",  p2.Pc.Mailbox.[0].Author)
    Assert.Equal("BLUE", p2.Pc.Mailbox.[1].Author)

[<Fact>]
let ``storeMail fails when mailbox is full`` () =
    // Fill mailbox to capacity (10).
    let mutable p = PlayerStateOps.initial
    for i in 1 .. Storage.mailboxCapacity do
        match PcItemOps.storeMail (makeMail (sprintf "A%d" i) "x") p with
        | Ok pp -> p <- pp
        | Error e -> Assert.Fail(sprintf "Unexpected failure filling mailbox: %s" e)
    Assert.Equal(Storage.mailboxCapacity, p.Pc.Mailbox.Length)
    match PcItemOps.storeMail (makeMail "EXTRA" "y") p with
    | Error msg -> Assert.Contains("full", msg.ToLower())
    | Ok _ -> Assert.Fail("Should have rejected over-capacity mail")

// ── readMail ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``readMail returns correct mail at index`` () =
    let p    = PlayerStateOps.initial
    let mail = makeMail "SILVER" "Fight me!"
    match PcItemOps.storeMail mail p with
    | Ok p2 ->
        match PcItemOps.readMail 0 p2 with
        | Some m -> Assert.Equal(mail, m)
        | None   -> Assert.Fail("Expected Some mail at index 0")
    | Error e -> Assert.Fail(e)

[<Fact>]
let ``readMail returns None for out-of-range index`` () =
    let p = PlayerStateOps.initial
    Assert.Equal(None, PcItemOps.readMail 0 p)
    Assert.Equal(None, PcItemOps.readMail -1 p)
    Assert.Equal(None, PcItemOps.readMail 99 p)

// ── takeMail ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``takeMail removes the entry at the given index`` () =
    let p  = PlayerStateOps.initial
    let m1 = makeMail "RED"  "Hi"
    let m2 = makeMail "BLUE" "Yo"
    let p2 =
        match PcItemOps.storeMail m1 p with
        | Ok pp -> match PcItemOps.storeMail m2 pp with Ok ppp -> ppp | Error e -> failwith e
        | Error e -> failwith e
    let p3 = PcItemOps.takeMail 0 p2
    Assert.Equal(1, p3.Pc.Mailbox.Length)
    Assert.Equal("BLUE", p3.Pc.Mailbox.[0].Author)

[<Fact>]
let ``takeMail is a no-op for out-of-range index`` () =
    let p    = PlayerStateOps.initial
    let mail = makeMail "RED" "Hi"
    match PcItemOps.storeMail mail p with
    | Ok p2 ->
        let p3 = PcItemOps.takeMail 99 p2
        Assert.Equal(1, p3.Pc.Mailbox.Length)
        let p4 = PcItemOps.takeMail -1 p2
        Assert.Equal(1, p4.Pc.Mailbox.Length)
    | Error e -> Assert.Fail(e)

// ── HEADLINE: save/reload persistence via PcItemOps ──────────────────────────

[<Fact>]
let ``deposit item and store mail then save-reload round-trip persists both`` () =
    let content = Content()
    let ow      = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down
    let p0      = PlayerStateOps.initial |> withBagItem "POTION" 5
    // Deposit 3 POTIONs into the PC stash.
    match PcItemOps.depositItem "POTION" 3 p0 with
    | Ok p1 ->
        Assert.Equal(2, Bag.count "POTION" p1.Bag)
        Assert.Equal(1, p1.Pc.PcItems.Length)
        // Store a mail message.
        let mail = makeMail "SILVER" "Battle me!"
        match PcItemOps.storeMail mail p1 with
        | Ok p2 ->
            Assert.Equal(1, p2.Pc.Mailbox.Length)
            // Save → serialize → deserialize → playerOf
            let back =
                SaveData.captureWith ow World.empty p2
                |> SaveFile.serialize
                |> SaveFile.deserialize
                |> Option.get
                |> SaveData.playerOf
            // PC items survived
            Assert.Equal(1, back.Pc.PcItems.Length)
            let (id, qty) = back.Pc.PcItems.[0]
            Assert.Equal("POTION", id)
            Assert.Equal(3, qty)
            // Bag item survived
            Assert.Equal(2, Bag.count "POTION" back.Bag)
            // Mailbox survived
            Assert.Equal(1, back.Pc.Mailbox.Length)
            Assert.Equal("SILVER",     back.Pc.Mailbox.[0].Author)
            Assert.Equal("Battle me!", back.Pc.Mailbox.[0].Body)
        | Error e -> Assert.Fail(sprintf "storeMail failed: %s" e)
    | Error e -> Assert.Fail(sprintf "depositItem failed: %s" e)
