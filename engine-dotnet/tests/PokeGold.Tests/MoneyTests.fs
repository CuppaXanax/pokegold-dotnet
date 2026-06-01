module PokeGold.Tests.MoneyTests

open Xunit
open PokeGold.Game.Player

[<Fact>]
let ``give clamps at maxMoney`` () =
    Assert.Equal(Money.maxMoney, Money.give Money.maxMoney 1)
    Assert.Equal(Money.maxMoney, Money.give 999998 2)
    Assert.Equal(Money.maxMoney, Money.give 0 1_000_000)

[<Fact>]
let ``give with zero amount returns money unchanged`` () =
    Assert.Equal(500, Money.give 500 0)

[<Fact>]
let ``give never returns below zero`` () =
    Assert.Equal(0, Money.give 0 0)
    // Degenerate: negative amount should still clamp at 0.
    Assert.Equal(0, Money.give 0 (-50))

[<Fact>]
let ``take floors at zero`` () =
    Assert.Equal(0, Money.take 100 200)
    Assert.Equal(0, Money.take 0 1)
    Assert.Equal(50, Money.take 100 50)
    Assert.Equal(0, Money.take 100 100)

[<Fact>]
let ``canAfford exact boundary`` () =
    Assert.True(Money.canAfford 100 100)
    Assert.True(Money.canAfford 101 100)
    Assert.False(Money.canAfford 99 100)
    Assert.False(Money.canAfford 0 1)

[<Fact>]
let ``buyTotal is price times qty`` () =
    Assert.Equal(600, Money.buyTotal 200 3)
    Assert.Equal(0, Money.buyTotal 100 0)
    Assert.Equal(300, Money.buyTotal 300 1)

[<Fact>]
let ``sellPrice is integer floor of buyPrice times qty divided by 2`` () =
    Assert.Equal(150, Money.sellPrice 300 1)   // 300/2 = 150
    Assert.Equal(450, Money.sellPrice 300 3)   // 900/2 = 450
    Assert.Equal(2,   Money.sellPrice 5 1)     // 5/2 = 2 (floor of 2.5)
    Assert.Equal(0,   Money.sellPrice 1 1)     // 1/2 = 0
    Assert.Equal(1,   Money.sellPrice 2 1)     // 2/2 = 1
    Assert.Equal(350, Money.sellPrice 700 1)   // 700/2 = 350
