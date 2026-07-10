module PokeGold.Tests.StorageOpsTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Save

// ── Helpers ─────────────────────────────────────────────────────────────────

let private makePlayer (partySize: int) : PlayerState =
    let party = List.init partySize (fun i -> PartyMon.create (1 + i) (5 + i))
    { PlayerStateOps.initial with Party = party }

let private fillBox (player: PlayerState) : PlayerState =
    // Fill the current box with Storage.monsPerBox mons.
    let box     = player.Pc.Boxes.[player.Pc.CurrentBox]
    let mons    = List.init Storage.monsPerBox (fun i -> PartyMon.create (100 + i) 5)
    let newBox  = { box with Mons = mons }
    let newBoxes = player.Pc.Boxes |> Array.mapi (fun i b -> if i = player.Pc.CurrentBox then newBox else b)
    { player with Pc = { player.Pc with Boxes = newBoxes } }

// ── deposit ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``deposit moves party mon into current box`` () =
    let p = makePlayer 2
    match BoxOps.deposit 0 p with
    | Ok p2 ->
        Assert.Equal(1, p2.Party.Length)
        Assert.Equal(1, p2.Pc.Boxes.[0].Mons.Length)
        Assert.Equal(p.Party.[0].SpeciesId, p2.Pc.Boxes.[0].Mons.[0].SpeciesId)
    | Error e -> Assert.Fail(sprintf "deposit failed: %s" e)

[<Fact>]
let ``deposit rejects when party has only one mon`` () =
    let p = makePlayer 1
    match BoxOps.deposit 0 p with
    | Error msg -> Assert.Contains("last", msg.ToLower())
    | Ok _ -> Assert.Fail("Should have rejected depositing the last mon")

[<Fact>]
let ``deposit rejects when current box is full`` () =
    let p = makePlayer 3 |> fillBox
    match BoxOps.deposit 0 p with
    | Error msg -> Assert.Contains("full", msg.ToLower())
    | Ok _ -> Assert.Fail("Should have rejected deposit into a full box")

[<Fact>]
let ``deposit rejects invalid party index`` () =
    let p = makePlayer 2
    match BoxOps.deposit 99 p with
    | Error _ -> ()
    | Ok _ -> Assert.Fail("Should have rejected out-of-range index")

[<Fact>]
let ``deposit places mon at end of box mon list`` () =
    let p = makePlayer 3
    // Pre-populate box with one mon.
    let mon0 = PartyMon.create 200 10
    let box  = p.Pc.Boxes.[0]
    let boxes = p.Pc.Boxes |> Array.mapi (fun i b -> if i = 0 then { b with Mons = [mon0] } else b)
    let p2 = { p with Pc = { p.Pc with Boxes = boxes } }
    match BoxOps.deposit 1 p2 with
    | Ok p3 ->
        Assert.Equal(2, p3.Pc.Boxes.[0].Mons.Length)
    | Error e -> Assert.Fail(e)

// ── withdraw ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``withdraw moves box mon into party`` () =
    let p = makePlayer 2
    match BoxOps.deposit 0 p with
    | Ok p2 ->
        match BoxOps.withdraw 0 0 p2 with
        | Ok p3 ->
            Assert.Equal(2, p3.Party.Length)
            Assert.Equal(0, p3.Pc.Boxes.[0].Mons.Length)
        | Error e -> Assert.Fail(sprintf "withdraw failed: %s" e)
    | Error e -> Assert.Fail(sprintf "deposit failed: %s" e)

[<Fact>]
let ``withdraw rejects when party is full`` () =
    let p = makePlayer BoxOps.partyLength
    // Manually place a mon in box 0.
    let mon  = PartyMon.create 150 20
    let box  = p.Pc.Boxes.[0]
    let boxes = p.Pc.Boxes |> Array.mapi (fun i b -> if i = 0 then { b with Mons = [mon] } else b)
    let p2 = { p with Pc = { p.Pc with Boxes = boxes } }
    match BoxOps.withdraw 0 0 p2 with
    | Error msg -> Assert.Contains("full", msg.ToLower())
    | Ok _ -> Assert.Fail("Should have rejected withdraw into a full party")

[<Fact>]
let ``withdraw rejects invalid box index`` () =
    let p = makePlayer 2
    match BoxOps.withdraw 99 0 p with
    | Error _ -> ()
    | Ok _ -> Assert.Fail("Should have rejected invalid box index")

[<Fact>]
let ``withdraw rejects invalid mon index`` () =
    let p = makePlayer 2
    match BoxOps.withdraw 0 99 p with
    | Error _ -> ()
    | Ok _ -> Assert.Fail("Should have rejected invalid mon index")

// ── deposit→withdraw round-trip ──────────────────────────────────────────────

[<Fact>]
let ``deposit then withdraw round-trips to equal PartyMon`` () =
    let mon = PartyMon.create 155 10
    let p   = { makePlayer 1 with Party = [PartyMon.create 1 5; mon] }
    match BoxOps.deposit 1 p with
    | Ok p2 ->
        let boxMon = p2.Pc.Boxes.[0].Mons.[0]
        Assert.Equal(mon.Id, boxMon.Id)
        match BoxOps.withdraw 0 0 p2 with
        | Ok p3 ->
            let withdrawn = List.last p3.Party
            Assert.Equal(mon, withdrawn)
        | Error e -> Assert.Fail(e)
    | Error e -> Assert.Fail(e)

// ── release ──────────────────────────────────────────────────────────────────

[<Fact>]
let ``release removes the mon from the box`` () =
    let p    = makePlayer 2
    match BoxOps.deposit 0 p with
    | Ok p2 ->
        let p3 = BoxOps.release 0 0 p2
        Assert.Equal(0, p3.Pc.Boxes.[0].Mons.Length)
    | Error e -> Assert.Fail(e)

[<Fact>]
let ``release with invalid index is a no-op`` () =
    let p = makePlayer 2
    let p2 = BoxOps.release 99 0 p
    Assert.Equal(p, p2)

// ── switchBox ────────────────────────────────────────────────────────────────

[<Fact>]
let ``switchBox changes the current box`` () =
    let p = makePlayer 1
    let p2 = BoxOps.switchBox 5 p
    Assert.Equal(5, p2.Pc.CurrentBox)

[<Fact>]
let ``switchBox clamps to valid range`` () =
    let p = makePlayer 1
    Assert.Equal(0, (BoxOps.switchBox -1 p).Pc.CurrentBox)
    Assert.Equal(Storage.numBoxes - 1, (BoxOps.switchBox 999 p).Pc.CurrentBox)

// ── renameBox ────────────────────────────────────────────────────────────────

[<Fact>]
let ``renameBox updates the box name`` () =
    let p = makePlayer 1
    let p2 = BoxOps.renameBox 3 "FIRE" p
    Assert.Equal("FIRE", p2.Pc.Boxes.[3].Name)

[<Fact>]
let ``renameBox with invalid index is a no-op`` () =
    let p = makePlayer 1
    let p2 = BoxOps.renameBox 99 "X" p
    Assert.Equal(p, p2)

// ── move across boxes ────────────────────────────────────────────────────────

[<Fact>]
let ``deposit into box 3 then withdraw from box 3`` () =
    let mon = PartyMon.create 4 15   // Charmander lv 15
    let p   = { makePlayer 1 with Party = [PartyMon.create 1 5; mon] }
    // Switch to box 3, deposit mon at party index 1.
    let p2 = BoxOps.switchBox 3 p
    match BoxOps.deposit 1 p2 with
    | Ok p3 ->
        Assert.Equal(0, p3.Pc.Boxes.[0].Mons.Length)   // box 0 still empty
        Assert.Equal(1, p3.Pc.Boxes.[3].Mons.Length)   // box 3 has the mon
        Assert.Equal(4, p3.Pc.Boxes.[3].Mons.[0].SpeciesId)
    | Error e -> Assert.Fail(e)

// ── HEADLINE: save/reload persistence via BoxOps ─────────────────────────────

[<Fact>]
let ``deposit via BoxOps then save-reload round-trip persists mon in box`` () =
    let content = Content()
    let ow      = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down
    let mon     = PartyMon.create 155 10  // Cyndaquil lv 10
    let p0      = { makePlayer 1 with Party = [PartyMon.create 1 5; mon] }
    // Deposit mon (party index 1) into the default current box (box 0).
    match BoxOps.deposit 1 p0 with
    | Ok p1 ->
        Assert.Equal(1, p1.Party.Length)       // party shrank
        Assert.Equal(1, p1.Pc.Boxes.[0].Mons.Length)
        // Save → serialize → deserialize → playerOf
        let back =
            SaveData.captureWith ow World.empty p1
            |> SaveFile.serialize
            |> SaveFile.deserialize
            |> Option.get
            |> SaveData.playerOf
        Assert.Equal(1, back.Party.Length)
        Assert.Equal(1, back.Pc.Boxes.[0].Mons.Length)
        Assert.Equal(155, back.Pc.Boxes.[0].Mons.[0].SpeciesId)
        Assert.Equal(mon.Id, back.Pc.Boxes.[0].Mons.[0].Id)
        Assert.Equal(mon.Nickname, back.Pc.Boxes.[0].Mons.[0].Nickname)
        Assert.Equal(mon.Level, back.Pc.Boxes.[0].Mons.[0].Level)
    | Error e -> Assert.Fail(sprintf "deposit failed: %s" e)

[<Fact>]
let ``deposit into box 7, save-reload, mon still in box 7`` () =
    let content = Content()
    let ow      = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down
    let mon     = PartyMon.create 152 8  // Chikorita lv 8
    let p0      = { makePlayer 1 with Party = [PartyMon.create 1 5; mon] }
    let p1      = BoxOps.switchBox 7 p0
    match BoxOps.deposit 1 p1 with
    | Ok p2 ->
        let back =
            SaveData.captureWith ow World.empty p2
            |> SaveFile.serialize
            |> SaveFile.deserialize
            |> Option.get
            |> SaveData.playerOf
        Assert.Equal(0, back.Pc.Boxes.[0].Mons.Length)   // box 0 untouched
        Assert.Equal(1, back.Pc.Boxes.[7].Mons.Length)
        Assert.Equal(152, back.Pc.Boxes.[7].Mons.[0].SpeciesId)
    | Error e -> Assert.Fail(e)

[<Fact>]
let ``deposit and withdraw across boxes, all empty boxes survive save-reload`` () =
    let content = Content()
    let ow      = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down
    let p0 = makePlayer 3
    // Deposit into box 0 and box 13.
    match BoxOps.deposit 0 p0 with
    | Ok p1 ->
        let p2 = BoxOps.switchBox 13 p1
        match BoxOps.deposit 0 p2 with
        | Ok p3 ->
            let back =
                SaveData.captureWith ow World.empty p3
                |> SaveFile.serialize
                |> SaveFile.deserialize
                |> Option.get
                |> SaveData.playerOf
            Assert.Equal(1, back.Pc.Boxes.[0].Mons.Length)
            Assert.Equal(1, back.Pc.Boxes.[13].Mons.Length)
            // All other boxes empty
            for i in 1..12 do
                Assert.Equal(0, back.Pc.Boxes.[i].Mons.Length)
        | Error e -> Assert.Fail(e)
    | Error e -> Assert.Fail(e)
