module PokeGold.Tests.TradingTests

open Xunit
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
