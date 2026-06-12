module PokeGold.Tests.BreedingTests

open Xunit
open PokeGold.Game.Player

[<Fact>]
let ``breeding generates an egg from deposited mons`` () =
    let parent1 = PartyMon.create 1 20
    let parent2 = PartyMon.create 1 20
    let egg = Breeding.generateEgg parent1 parent2

    Assert.Equal(1, egg.SpeciesId)
    Assert.Equal(5, egg.Level)
    Assert.Equal("EGG", egg.Nickname)

[<Fact>]
let ``egg helper recognizes generated eggs`` () =
    let parent1 = PartyMon.create 1 20
    let parent2 = PartyMon.create 1 20

    Assert.True(Breeding.isEgg (Breeding.generateEgg parent1 parent2))
    Assert.False(Breeding.isEgg parent1)
