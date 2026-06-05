module PokeGold.Tests.EvolutionTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Player

[<Fact>]
let ``Bulbasaur evolves to Ivysaur at level 16`` () =
    let bulbasaur = PartyMon.create 1 16
    match Evolution.checkLevelEvolution bulbasaur with
    | Some target -> Assert.Equal("IVYSAUR", target)
    | None -> Assert.Fail("should evolve")

[<Fact>]
let ``Bulbasaur does not evolve at level 15`` () =
    let bulbasaur = PartyMon.create 1 15
    Assert.True(Evolution.checkLevelEvolution bulbasaur |> Option.isNone)

[<Fact>]
let ``applyEvolution changes species and updates stats`` () =
    let bulbasaur = PartyMon.create 1 16
    let evolved = Evolution.applyEvolution "IVYSAUR" bulbasaur
    let ivysaurDex = (Species.byName "IVYSAUR").Dex
    Assert.Equal(ivysaurDex, evolved.SpeciesId)
    Assert.Equal("IVYSAUR", evolved.Nickname)
