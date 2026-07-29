module PokeGold.Tests.BreedingTests

open Xunit
open PokeGold.Game.Battle
open PokeGold.Game.Data
open PokeGold.Game.Player

[<Fact>]
let ``undiscovered egg group rejects breeding`` () =
    Assert.False(Breeding.compatible (PartyMon.create 250 0x0000) (PartyMon.create 1 0xFFFF))

[<Fact>]
let ``Ditto breeds with a breedable Pokemon and supplies the non-Ditto offspring species`` () =
    let bulbasaur = PartyMon.create 1 0x0000
    let ditto = PartyMon.create 132 0x0000

    Assert.True(Breeding.compatible ditto bulbasaur)
    Assert.True(Breeding.compatible bulbasaur ditto)
    Assert.Equal(1, (Breeding.generateEgg ditto bulbasaur).SpeciesId)
    Assert.Equal(1, (Breeding.generateEgg bulbasaur ditto).SpeciesId)

[<Fact>]
let ``same-gender Pokemon in a shared egg group cannot breed`` () =
    let maleBulbasaur = PartyMon.createWithDvs 1 20 0xF000
    let maleCharmander = PartyMon.createWithDvs 4 20 0xF000

    Assert.Equal(Male, BattleMon.genderFromDvs (Species.byName "BULBASAUR") maleBulbasaur.Dvs)
    Assert.Equal(Male, BattleMon.genderFromDvs (Species.byName "CHARMANDER") maleCharmander.Dvs)
    Assert.False(Breeding.compatible maleBulbasaur maleCharmander)

[<Fact>]
let ``breeding generates an egg from deposited mons`` () =
    let parent1 = PartyMon.createWithDvs 1 20 0x0000
    let parent2 = PartyMon.createWithDvs 4 20 0xF000
    let egg = Breeding.generateEgg parent1 parent2

    Assert.Equal(1, egg.SpeciesId)
    Assert.Equal(5, egg.Level)
    Assert.Equal("EGG", egg.Nickname)
    Assert.Contains(egg.Moves |> List.map fst, fun move -> move = (MovesData.byIndex |> Array.findIndex (fun m -> m.Name = "TACKLE")))
    Assert.Contains(egg.Moves |> List.map fst, fun move -> move = (MovesData.byIndex |> Array.findIndex (fun m -> m.Name = "GROWL")))

[<Fact>]
let ``egg inherits an eligible father egg move`` () =
    let mother = PartyMon.createWithDvs 1 20 0x0000
    let father =
        { PartyMon.createWithDvs 4 20 0xF000 with
            Moves = [ MovesData.byIndex |> Array.findIndex (fun move -> move.Name = "SKULL_BASH"), 15 ] }

    let egg = Breeding.generateEgg mother father

    Assert.Contains(egg.Moves |> List.map fst, fun move -> move = (MovesData.byIndex |> Array.findIndex (fun m -> m.Name = "SKULL_BASH")))

[<Fact>]
let ``hatch steps use each species hatch cycle count`` () =
    let bulbasaur = Species.byName "BULBASAUR"
    let hoOh = Species.byName "HO_OH"

    Assert.Equal(20, bulbasaur.HatchCycles)
    Assert.Equal(120, hoOh.HatchCycles)
    Assert.Equal(bulbasaur.HatchCycles * 256, Breeding.hatchStepsFor bulbasaur.Dex)
    Assert.Equal(hoOh.HatchCycles * 256, Breeding.hatchStepsFor hoOh.Dex)
    Assert.NotEqual(Breeding.hatchStepsFor bulbasaur.Dex, Breeding.hatchStepsFor hoOh.Dex)

[<Fact>]
let ``egg helper recognizes generated eggs`` () =
    let parent1 = PartyMon.createWithDvs 1 20 0x0000
    let parent2 = PartyMon.createWithDvs 4 20 0xF000

    Assert.True(Breeding.isEgg (Breeding.generateEgg parent1 parent2))
    Assert.False(Breeding.isEgg parent1)
