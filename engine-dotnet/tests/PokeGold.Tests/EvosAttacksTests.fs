module PokeGold.Tests.EvosAttacksTests

open Xunit
open PokeGold.Game.Data

[<Fact>]
let ``Bulbasaur evolution and learnset parse correctly`` () =
    let bulbasaur = EvosAttacksAccess.forSpecies "BULBASAUR" |> Option.defaultWith (fun () -> failwith "BULBASAUR not found")

    Assert.Equal("EVOLVE_LEVEL", bulbasaur.Evolutions.[0].Method)
    Assert.Equal("16", bulbasaur.Evolutions.[0].Param)
    Assert.Equal("", bulbasaur.Evolutions.[0].Param2)
    Assert.Equal("IVYSAUR", bulbasaur.Evolutions.[0].Target)
    Assert.Equal(1, bulbasaur.Learnset.[0].Level)
    Assert.Equal("TACKLE", bulbasaur.Learnset.[0].Move)

[<Fact>]
let ``Eevee exposes multiple evolution routes`` () =
    let eevee = EvosAttacksAccess.forSpecies "EEVEE" |> Option.defaultWith (fun () -> failwith "EEVEE not found")

    Assert.True(eevee.Evolutions.Length >= 5)
    Assert.True(eevee.Evolutions |> List.exists (fun entry -> entry.Method = "EVOLVE_ITEM" && entry.Param = "THUNDERSTONE" && entry.Target = "JOLTEON"))
    Assert.True(eevee.Evolutions |> List.exists (fun entry -> entry.Method = "EVOLVE_HAPPINESS" && entry.Param = "TR_MORNDAY" && entry.Target = "ESPEON"))
