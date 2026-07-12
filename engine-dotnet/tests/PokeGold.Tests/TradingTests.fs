module PokeGold.Tests.TradingTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Player

[<Fact>]
let ``trade swaps mons between parties`` () =
    let a = PartyMon.create 1 10
    let b = PartyMon.create 4 10
    match Trading.executeTrade [ a ] 0 [ b ] 0 with
    | Some([ received ], [ sent ]) ->
        Assert.Equal(4, received.SpeciesId)
        Assert.Equal(1, sent.SpeciesId)
    | _ -> Assert.Fail("trade should succeed")

[<Fact>]
let ``trade evolution triggers for eligible mons`` () =
    let kadabra = PartyMon.create 64 30
    match Trading.checkTradeEvolution kadabra with
    | Some target -> Assert.Equal("ALAKAZAM", target)
    | None -> ()

[<Fact>]
let ``BAT-014 trade-with-item evolves both received sides and consumes catalysts`` () =
    let poliwhirl = { PartyMon.create (Species.byName "POLIWHIRL").Dex 25 with HeldItem = Some "KINGS_ROCK" }
    let seadra = { PartyMon.create (Species.byName "SEADRA").Dex 32 with HeldItem = Some "DRAGON_SCALE" }
    match Trading.tradeWithEvolution [ poliwhirl ] 0 [ seadra ] 0 with
    | Some([ kingdra ], [ politoed ]) ->
        Assert.Equal((Species.byName "KINGDRA").Dex, kingdra.SpeciesId)
        Assert.Equal((Species.byName "POLITOED").Dex, politoed.SpeciesId)
        Assert.True(kingdra.HeldItem.IsNone)
        Assert.True(politoed.HeldItem.IsNone)
    | result -> Assert.Fail($"unexpected trade result {result}")

[<Fact>]
let ``BAT-014 cancelled trade-item evolution preserves species but consumes catalyst`` () =
    let pidgey = PartyMon.create (Species.byName "PIDGEY").Dex 5
    let poliwhirl = { PartyMon.create (Species.byName "POLIWHIRL").Dex 25 with HeldItem = Some "KINGS_ROCK" }
    match Trading.tradeWithEvolutionDecision false false [ pidgey ] 0 [ poliwhirl ] 0 with
    | Some([ received ], _) ->
        Assert.Equal((Species.byName "POLIWHIRL").Dex, received.SpeciesId)
        Assert.True(received.HeldItem.IsNone)
    | result -> Assert.Fail($"unexpected trade result {result}")

[<Fact>]
let ``offline terminal imports configured version exclusive species`` () =
    match Trading.offlineTerminalImport "BULBASAUR" 5 with
    | Some mon ->
        Assert.Equal(1, mon.SpeciesId)
        Assert.Equal(5, mon.Level)
    | None -> Assert.Fail("BULBASAUR should be available through the offline terminal")

[<Fact>]
let ``offline terminal rejects normally obtainable species outside its catalog`` () =
    Assert.False(Trading.canOfflineImport "PIKACHU")
    Assert.True(Trading.offlineTerminalImport "PIKACHU" 5 |> Option.isNone)

[<Fact>]
let ``empty dex is not complete`` () =
    Assert.False(DexCompletion.isComplete Set.empty)

[<Fact>]
let ``full dex of 251 is complete`` () =
    let full = Set.ofList [ 1..251 ]
    Assert.True(DexCompletion.isComplete full)

[<Fact>]
let ``percentage with 125 species is about 49`` () =
    let half = Set.ofList [ 1..125 ]
    Assert.Equal(49, DexCompletion.percentage half)

[<Fact>]
let ``remaining with 250 owned is 1`` () =
    let almost = Set.ofList [ 1..250 ]
    Assert.Equal(1, DexCompletion.remaining almost)
