module PokeGold.Tests.MartTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Scenes
open PokeGold.Game.Overworld.Script

// ── Pure core: Mart.buy ──────────────────────────────────────────────────────

[<Fact>]
let ``buy debits exact money for 3 items at price 200`` () =
    let bag   = Bag.empty
    let money = 2000
    match Mart.buy "POTION" 200 3 money bag with
    | Ok(newMoney, _) -> Assert.Equal(2000 - 600, newMoney)
    | Error e -> Assert.Fail(sprintf "Unexpected error %A" e)

[<Fact>]
let ``buy adds correct quantity to bag`` () =
    let bag   = Bag.empty
    let money = 2000
    match Mart.buy "POTION" 200 3 money bag with
    | Ok(_, newBag) -> Assert.Equal(3, Bag.count "POTION" newBag)
    | Error e -> Assert.Fail(sprintf "Unexpected error %A" e)

[<Fact>]
let ``buy adds to existing stack`` () =
    let bag   = Bag.add "POTION" 5 Bag.empty
    let money = 2000
    match Mart.buy "POTION" 200 3 money bag with
    | Ok(_, newBag) -> Assert.Equal(8, Bag.count "POTION" newBag)
    | Error e -> Assert.Fail(sprintf "Unexpected error %A" e)

[<Fact>]
let ``buy returns CantAfford when money is insufficient`` () =
    let bag   = Bag.empty
    let money = 100
    match Mart.buy "POTION" 300 1 money bag with
    | Error Mart.CantAfford -> ()
    | other -> Assert.Fail(sprintf "Expected CantAfford, got %A" other)

[<Fact>]
let ``buy with insufficient money leaves money unchanged`` () =
    let money = 100
    match Mart.buy "POTION" 300 1 money Bag.empty with
    | Error Mart.CantAfford -> Assert.Equal(100, money)
    | other -> Assert.Fail(sprintf "Expected CantAfford, got %A" other)

[<Fact>]
let ``buy with insufficient money leaves bag unchanged`` () =
    let bag = Bag.empty
    match Mart.buy "POTION" 300 1 100 bag with
    | Error Mart.CantAfford -> Assert.Equal(0, Bag.count "POTION" bag)
    | other -> Assert.Fail(sprintf "Expected CantAfford, got %A" other)

[<Fact>]
let ``buy exactly at cost succeeds`` () =
    let price = 300
    match Mart.buy "POTION" price 1 price Bag.empty with
    | Ok _ -> ()
    | Error e -> Assert.Fail(sprintf "Expected success at exact cost, got %A" e)

[<Fact>]
let ``buy one below cost fails`` () =
    let price = 300
    match Mart.buy "POTION" price 1 (price - 1) Bag.empty with
    | Error Mart.CantAfford -> ()
    | other -> Assert.Fail(sprintf "Expected CantAfford, got %A" other)

[<Fact>]
let ``buy caps bag stack at 99`` () =
    let bag   = Bag.add "POTION" 97 Bag.empty
    let money = 9999
    match Mart.buy "POTION" 300 5 money bag with
    | Ok(_, newBag) -> Assert.Equal(99, Bag.count "POTION" newBag)  // capped at 99
    | Error e -> Assert.Fail(sprintf "Unexpected error %A" e)

// ── Pure core: Mart.sell ─────────────────────────────────────────────────────

[<Fact>]
let ``sell credits floor(price x qty / 2)`` () =
    let bag   = Bag.add "POTION" 5 Bag.empty
    let money = 100
    // POTION price = 300; sell 2 -> 300*2/2 = 300
    let price = ItemsData.byId.["POTION"].Price
    match Mart.sell "POTION" price 2 money bag with
    | Ok(newMoney, _) -> Assert.Equal(money + Money.sellPrice price 2, newMoney)
    | Error e -> Assert.Fail(sprintf "Unexpected error %A" e)

[<Fact>]
let ``sell removes item from bag`` () =
    let bag   = Bag.add "POTION" 5 Bag.empty
    let price = ItemsData.byId.["POTION"].Price
    match Mart.sell "POTION" price 2 1000 bag with
    | Ok(_, newBag) -> Assert.Equal(3, Bag.count "POTION" newBag)
    | Error e -> Assert.Fail(sprintf "Unexpected error %A" e)

[<Fact>]
let ``sell integer floor for odd price`` () =
    // Price 5 per item, qty 1 -> 5/2 = 2 (floor).
    let bag   = Bag.add "POTION" 3 Bag.empty
    match Mart.sell "POTION" 5 1 0 bag with
    | Ok(newMoney, _) -> Assert.Equal(2, newMoney)
    | Error e -> Assert.Fail(sprintf "Unexpected error %A" e)

[<Fact>]
let ``sell refuses key items`` () =
    // BICYCLE is a key item and should be refused.
    let bag   = Bag.add "BICYCLE" 1 Bag.empty
    let price = ItemsData.byId |> Map.tryFind "BICYCLE" |> Option.map (fun d -> d.Price) |> Option.defaultValue 0
    match Mart.sell "BICYCLE" price 1 1000 bag with
    | Error Mart.CantSell -> ()
    | other -> Assert.Fail(sprintf "Expected CantSell for key item, got %A" other)

[<Fact>]
let ``sell refuses item with price 0`` () =
    // Find an item with price 0 — or use a made-up id that resolves to no data.
    match Mart.sell "FAKE_ITEM_ZZZ" 0 1 1000 Bag.empty with
    | Error Mart.CantSell -> ()
    | other -> Assert.Fail(sprintf "Expected CantSell for price-0 item, got %A" other)

[<Fact>]
let ``sell refuses when bag count is insufficient`` () =
    let bag   = Bag.add "POTION" 2 Bag.empty
    let price = ItemsData.byId.["POTION"].Price
    match Mart.sell "POTION" price 3 1000 bag with
    | Error Mart.NotInBag -> ()
    | other -> Assert.Fail(sprintf "Expected NotInBag, got %A" other)

// ── Pure core: Mart.canSell ──────────────────────────────────────────────────

[<Fact>]
let ``canSell returns true for normal buyable item`` () =
    Assert.True(Mart.canSell "POTION")

[<Fact>]
let ``canSell returns false for key item BICYCLE`` () =
    Assert.False(Mart.canSell "BICYCLE")

[<Fact>]
let ``canSell returns false for unknown item`` () =
    Assert.False(Mart.canSell "UNKNOWN_ITEM_ZZZ")

// ── Data: MartsData.byConstant ───────────────────────────────────────────────

[<Fact>]
let ``MartsData byConstant has 34 entries`` () =
    Assert.Equal(34, Map.count MartsData.byConstant)

[<Fact>]
let ``MartsData byConstant MART_AZALEA has 9 items`` () =
    let items = MartsData.byConstant.["MART_AZALEA"]
    Assert.Equal(9, List.length items)

[<Fact>]
let ``MartsData byConstant MART_AZALEA contains FLOWER_MAIL`` () =
    let items = MartsData.byConstant.["MART_AZALEA"]
    Assert.Contains("FLOWER_MAIL", items)

[<Fact>]
let ``MartsData byConstant MART_CHERRYGROVE has 4 items`` () =
    let items = MartsData.byConstant.["MART_CHERRYGROVE"]
    Assert.Equal(4, List.length items)

[<Fact>]
let ``MartsData byConstant and byLabel are consistent for Azalea`` () =
    Assert.Equal<string list>(
        MartsData.byLabel.["MartAzalea"],
        MartsData.byConstant.["MART_AZALEA"])

// ── Script: pokemart parse and VM wiring ────────────────────────────────────

[<Fact>]
let ``parser yields Pokemart for pokemart opcode`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tpokemart MARTTYPE_STANDARD, MART_AZALEA\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Pokemart("MARTTYPE_STANDARD", "MART_AZALEA"); End ],
        ScriptProgram.blockAt "S" prog)

[<Fact>]
let ``interpreter suspends with OpenMart effect for pokemart`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tpokemart MARTTYPE_STANDARD, MART_AZALEA\n\
             \tend\n"

    match (Script.start "S" World.empty prog).Outcome with
    | Suspended(_, OpenMart("MARTTYPE_STANDARD", items)) ->
        Assert.Equal(9, List.length items)
        Assert.Contains("FLOWER_MAIL", items)
    | other -> Assert.Fail(sprintf "Expected Suspended OpenMart, got %A" other)

[<Fact>]
let ``interpreter resumes past OpenMart with None`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tpokemart MARTTYPE_STANDARD, MART_AZALEA\n\
             \tsetval 77\n\
             \tend\n"

    let step1 = Script.start "S" World.empty prog
    match step1.Outcome with
    | Suspended(vm, OpenMart _) ->
        let step2 = Script.resume None step1.World vm
        Assert.Equal(Completed, step2.Outcome)
    | other -> Assert.Fail(sprintf "Expected OpenMart suspension, got %A" other)

[<Fact>]
let ``OpenMart for unknown MART_* constant yields empty item list`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tpokemart MARTTYPE_STANDARD, MART_NONEXISTENT\n\
             \tend\n"

    match (Script.start "S" World.empty prog).Outcome with
    | Suspended(_, OpenMart(_, items)) -> Assert.Empty(items)
    | other -> Assert.Fail(sprintf "Expected Suspended OpenMart, got %A" other)

// ── Scene smoke: MartScene navigation ────────────────────────────────────────

let private makePlayer () : PlayerState =
    let bag =
        Bag.empty
        |> Bag.add "POTION"    5
        |> Bag.add "ANTIDOTE"  2
        |> Bag.add "POKE_BALL" 3
    { PlayerState.initial with Money = 99999; Bag = bag }

let private martItems = MartsData.byConstant.["MART_AZALEA"]

let private makeScene () =
    let mutable updated: PlayerState option = None
    let p = makePlayer ()
    let scene = MartScene(Content(), p, "MARTTYPE_STANDARD", martItems, fun p -> updated <- Some p)
    scene, fun () -> updated

let private update (scene: MartScene) (b: Buttons) : Transition =
    (scene :> Scene).Update(b)

let private press (b: Buttons) (scene: MartScene) : Transition =
    let t = update scene b
    update scene Buttons.none |> ignore
    t

let private pressA    s = press { Buttons.none with A     = true } s
let private pressB    s = press { Buttons.none with B     = true } s
let private pressDown s = press { Buttons.none with Down  = true } s
let private pressUp   s = press { Buttons.none with Up    = true } s

[<Fact>]
let ``MartScene starts in TopMenu`` () =
    let scene, _ = makeScene ()
    Assert.Equal(TopMenu, scene.Mode)

[<Fact>]
let ``MartScene B from TopMenu returns Pop`` () =
    let scene, _ = makeScene ()
    Assert.Equal(Pop, pressB scene)

[<Fact>]
let ``MartScene A on QUIT returns Pop`` () =
    let scene, _ = makeScene ()
    // Navigate Down twice to QUIT (index 2).
    pressDown scene |> ignore
    pressDown scene |> ignore
    Assert.Equal(Pop, pressA scene)

[<Fact>]
let ``MartScene A on BUY enters Buying mode`` () =
    let scene, _ = makeScene ()
    pressA scene |> ignore   // A on BUY (cursor 0)
    Assert.Equal(Buying, scene.Mode)

[<Fact>]
let ``MartScene B from Buying returns to TopMenu`` () =
    let scene, _ = makeScene ()
    pressA scene |> ignore   // enter Buying
    pressB scene |> ignore
    Assert.Equal(TopMenu, scene.Mode)

[<Fact>]
let ``MartScene A on item enters BuyQty mode`` () =
    let scene, _ = makeScene ()
    pressA scene |> ignore   // enter Buying
    pressA scene |> ignore   // select first item
    match scene.Mode with
    | BuyQty _ -> ()
    | other -> Assert.Fail(sprintf "Expected BuyQty, got %A" other)

[<Fact>]
let ``MartScene full buy flow: money and bag updated`` () =
    let scene, getUpdated = makeScene ()
    // Enter BUY (cursor 0 = BUY).
    update scene { Buttons.none with A = true } |> ignore  // enter Buying
    update scene Buttons.none |> ignore
    // Select first item in Buying list.
    update scene { Buttons.none with A = true } |> ignore  // enter BuyQty
    update scene Buttons.none |> ignore
    // Confirm qty 1 → Push(YesNoScene).  Do NOT release before YesNo fires,
    // or the BuyWait handler will see yesNoResult=0 and cancel the buy.
    let yesNoPush = update scene { Buttons.none with A = true }
    // Simulate YesNo saying YES.
    match yesNoPush with
    | Push yesno ->
        yesno.Update({ Buttons.none with A = true }) |> ignore
    | _ ->
        Assert.Fail(sprintf "Expected Push(YesNoScene), got %A" yesNoPush)
    // One frame processes the BuyWait result.
    update scene Buttons.none |> ignore
    // Assert money was debited and bag has the item.
    match getUpdated() with
    | Some p ->
        let firstItemId = martItems.[0]
        let price = ItemsData.byId.[firstItemId].Price
        Assert.Equal(99999 - price, p.Money)
        Assert.True(Bag.count firstItemId p.Bag >= 1)
    | None -> Assert.Fail("onChange was not called")

[<Fact>]
let ``MartScene A on SELL enters Selling mode`` () =
    let scene, _ = makeScene ()
    pressDown scene |> ignore   // cursor to SELL (index 1)
    pressA scene |> ignore
    Assert.Equal(Selling, scene.Mode)

[<Fact>]
let ``MartScene sell POTION: money increases by sell price and bag shrinks`` () =
    let scene, getUpdated = makeScene ()
    let initialMoney = 99999
    let potionPrice  = ItemsData.byId.["POTION"].Price
    let expectedGain = Money.sellPrice potionPrice 1
    // Navigate to SELL.
    pressDown scene |> ignore
    update scene { Buttons.none with A = true } |> ignore  // enter Selling
    update scene Buttons.none |> ignore
    // Press A on POTION (first sellable item in Items pocket).
    update scene { Buttons.none with A = true } |> ignore  // enter SellQty
    update scene Buttons.none |> ignore
    // Confirm qty 1 → Push(YesNoScene).  Do NOT release before YesNo fires.
    let yesNoPush = update scene { Buttons.none with A = true }
    match yesNoPush with
    | Push yesno ->
        yesno.Update({ Buttons.none with A = true }) |> ignore
    | _ ->
        Assert.Fail(sprintf "Expected Push(YesNoScene), got %A" yesNoPush)
    update scene Buttons.none |> ignore
    match getUpdated() with
    | Some p ->
        Assert.Equal(initialMoney + expectedGain, p.Money)
        Assert.Equal(4, Bag.count "POTION" p.Bag)
    | None -> Assert.Fail("onChange was not called")
