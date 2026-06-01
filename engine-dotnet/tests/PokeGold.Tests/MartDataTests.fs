module PokeGold.Tests.MartDataTests

open Xunit
open PokeGold.Game.Data

[<Fact>]
let ``MartsData byLabel MartCherrygrove has 4 items in order`` () =
    let items = MartsData.byLabel.["MartCherrygrove"]
    Assert.Equal<string list>([ "POTION"; "ANTIDOTE"; "PARLYZ_HEAL"; "AWAKENING" ], items)

[<Fact>]
let ``MartsData byLabel MartAzalea has 9 items including FLOWER_MAIL`` () =
    let items = MartsData.byLabel.["MartAzalea"]
    Assert.Equal(9, List.length items)
    Assert.Contains("FLOWER_MAIL", items)

[<Fact>]
let ``MartsData order starts with MartCherrygrove`` () =
    Assert.Equal("MartCherrygrove", List.head MartsData.order)

[<Fact>]
let ``MartsData byLabel has 34 entries`` () =
    Assert.Equal(34, Map.count MartsData.byLabel)

[<Fact>]
let ``MartsData order has 34 entries`` () =
    Assert.Equal(34, List.length MartsData.order)

[<Fact>]
let ``MartsData byLabel MartIndigoPlateau contains FULL_RESTORE`` () =
    let items = MartsData.byLabel.["MartIndigoPlateau"]
    Assert.Contains("FULL_RESTORE", items)
