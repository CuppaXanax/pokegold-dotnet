module PokeGold.Tests.TmHmTests

open Xunit
open PokeGold.Game.Player

[<Fact>]
let ``HM01 maps to CUT`` () =
    Assert.Equal(Some "CUT", TmHm.moveForItem "HM_CUT")

[<Fact>]
let ``teaching a move adds it to party mon`` () =
    let mon = PartyMon.create 155 10

    match TmHm.teach "CUT" mon with
    | Some taught -> Assert.True(taught.Moves.Length > 0)
    | None -> Assert.Fail("should teach")
