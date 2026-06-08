module PokeGold.Tests.TimeOfDayTests

open System
open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Player

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

[<Fact>]
let ``GameTimeState derives weekday and time from host clock seed`` () =
    let time = GameTimeState.fromClock (DateTimeOffset(2026, 6, 7, 22, 15, 0, TimeSpan.Zero))

    Assert.Equal(22, time.Hour)
    Assert.Equal(15, time.Minute)
    Assert.Equal(0, time.Weekday)
    Assert.False(time.IsDst)
    Assert.Equal(Nite, GameTimeState.timeOfDay time)

[<Fact>]
let ``PlayerStateOps initialAt uses deterministic game time`` () =
    let player = PlayerStateOps.initialAt (DateTimeOffset(2026, 6, 9, 8, 5, 0, TimeSpan.Zero))

    Assert.Equal(8, player.GameTime.Hour)
    Assert.Equal(5, player.GameTime.Minute)
    Assert.Equal(2, player.GameTime.Weekday)
