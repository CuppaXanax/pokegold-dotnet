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

[<Fact>]
let ``Kurt picker scans apricorns in disassembly order and returns item ids`` () =
    let bag =
        Bag.empty
        |> Bag.add "PNK_APRICORN" 1
        |> Bag.add "RED_APRICORN" 1
        |> Bag.add "WHT_APRICORN" 1

    Assert.Equal<string list>(
        [ "RED_APRICORN"; "WHT_APRICORN"; "PNK_APRICORN" ],
        Apricorns.available bag)

    Assert.Equal(0x59, Apricorns.itemId "BLU_APRICORN")
