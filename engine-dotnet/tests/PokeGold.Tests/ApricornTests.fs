module PokeGold.Tests.ApricornTests

open Xunit
open PokeGold.Game.Player

[<Fact>]
let ``RED_APRICORN converts to LEVEL_BALL`` () =
    let p = { PlayerStateOps.initial with Bag = Bag.add "RED_APRICORN" 1 Bag.empty }
    match Apricorns.convert "RED_APRICORN" p with
    | Some p2 ->
        Assert.Equal(0, Bag.count "RED_APRICORN" p2.Bag)
        Assert.Equal(1, Bag.count "LEVEL_BALL" p2.Bag)
    | None -> Assert.Fail("should convert")
