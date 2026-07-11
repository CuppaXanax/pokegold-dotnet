module PokeGold.Tests.TmHmTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Player

[<Fact>]
let ``HM01 maps to CUT`` () =
    Assert.Equal(Some "CUT", TmHm.moveForItem "HM_CUT")

[<Fact>]
let ``HM items are identified as reusable`` () =
    Assert.True(TmHm.isHmItem "HM_CUT")
    Assert.True(TmHm.isHmItem "HM_SURF")
    Assert.False(TmHm.isHmItem "TM01")

[<Fact>]
let ``teaching a move adds it to party mon`` () =
    let mon = PartyMon.create 155 10

    match TmHm.teach "CUT" mon with
    | Some taught -> Assert.True(taught.Moves.Length > 0)
    | None -> Assert.Fail("should teach")

[<Fact>]
let ``BAT-013 generated table covers every TM and HM boundary`` () =
    Assert.Equal(57, TmHmData.moveByItem.Count)
    Assert.Equal(Some "DYNAMICPUNCH", TmHm.moveForItem "TM01")
    Assert.Equal(Some "HIDDEN_POWER", TmHm.moveForItem "TM10")
    Assert.Equal(Some "NIGHTMARE", TmHm.moveForItem "TM50")
    Assert.Equal(Some "CUT", TmHm.moveForItem "HM01")
    Assert.Equal(Some "WATERFALL", TmHm.moveForItem "HM07")

[<Fact>]
let ``BAT-013 source compatibility accepts and rejects real species pairs`` () =
    let mon species = PartyMon.create (Species.byName species).Dex 10
    Assert.True(TmHm.canLearnMove "DYNAMICPUNCH" (mon "PIKACHU"))
    Assert.False(TmHm.canLearnMove "DYNAMICPUNCH" (mon "CYNDAQUIL"))
    Assert.True(TmHm.canLearnMove "HIDDEN_POWER" (mon "CYNDAQUIL"))
    Assert.True(TmHm.canLearnMove "NIGHTMARE" (mon "ABRA"))
    Assert.True(TmHm.canLearnMove "SURF" (mon "TOTODILE"))
    Assert.False(TmHm.canLearnMove "SURF" (mon "CYNDAQUIL"))

[<Fact>]
let ``BAT-013 full moveset requires an explicit chosen-slot replacement`` () =
    let slot name pp = MovesData.byIndex |> Array.findIndex (fun move -> move.Name = name), pp
    let mon =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 10 with
            Moves = [ slot "TACKLE" 1; slot "LEER" 2; slot "SMOKESCREEN" 3; slot "EMBER" 4 ] }
    match TmHm.prepare "TM10" mon with
    | NeedsReplacement moveId ->
        let replaced = TmHm.replaceMove moveId 1 mon
        Assert.Equal<(int * int) list>(
            [ slot "TACKLE" 1; slot "HIDDEN_POWER" (Moves.byName "HIDDEN_POWER").Pp
              slot "SMOKESCREEN" 3; slot "EMBER" 4 ],
            replaced.Moves)
    | result -> Assert.Fail($"expected replacement request, got {result}")

[<Fact>]
let ``BAT-013 incompatible and already-known teaching do not mutate`` () =
    let cyndaquil = PartyMon.create (Species.byName "CYNDAQUIL").Dex 10
    Assert.Equal(Incompatible, TmHm.prepare "TM01" cyndaquil)
    let hiddenPower = MovesData.byIndex |> Array.findIndex (fun move -> move.Name = "HIDDEN_POWER")
    let knows = { cyndaquil with Moves = [ hiddenPower, 1 ] }
    Assert.Equal(AlreadyKnows, TmHm.prepare "TM10" knows)
