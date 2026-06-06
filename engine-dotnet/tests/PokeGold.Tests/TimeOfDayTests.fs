module PokeGold.Tests.TimeOfDayTests

open Xunit
open PokeGold.Game.Core

[<Fact>]
let ``fromHour returns Morn for 4-9`` () =
    Assert.Equal(Morn, TimeOfDay.fromHour 4)
    Assert.Equal(Morn, TimeOfDay.fromHour 9)

[<Fact>]
let ``fromHour returns Day for 10-17`` () =
    Assert.Equal(Day, TimeOfDay.fromHour 10)
    Assert.Equal(Day, TimeOfDay.fromHour 17)

[<Fact>]
let ``fromHour returns Nite for 18-3`` () =
    Assert.Equal(Nite, TimeOfDay.fromHour 18)
    Assert.Equal(Nite, TimeOfDay.fromHour 23)
    Assert.Equal(Nite, TimeOfDay.fromHour 0)
    Assert.Equal(Nite, TimeOfDay.fromHour 3)

[<Fact>]
let ``toIndex maps Morn=0 Day=1 Nite=2`` () =
    Assert.Equal(0, TimeOfDay.toIndex Morn)
    Assert.Equal(1, TimeOfDay.toIndex Day)
    Assert.Equal(2, TimeOfDay.toIndex Nite)
