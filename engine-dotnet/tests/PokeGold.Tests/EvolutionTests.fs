module PokeGold.Tests.EvolutionTests

open Xunit
open PokeGold.Game.Core
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

[<Fact>]
let ``BAT-014 friendship evolution observes threshold and time branches`` () =
    let eevee friendship = { PartyMon.create (Species.byName "EEVEE").Dex 20 with Friendship = friendship }
    Assert.True(Evolution.tryFind (LevelUp Day) (eevee 219) |> Option.isNone)
    Assert.Equal("ESPEON", (Evolution.tryFind (LevelUp Morn) (eevee 220)).Value.Target)
    Assert.Equal("ESPEON", (Evolution.tryFind (LevelUp Day) (eevee 220)).Value.Target)
    Assert.Equal("UMBREON", (Evolution.tryFind (LevelUp Nite) (eevee 220)).Value.Target)

[<Fact>]
let ``BAT-014 item evolution selects the exact source branch and Everstone blocks`` () =
    let gloom = PartyMon.create (Species.byName "GLOOM").Dex 20
    Assert.Equal("VILEPLUME", (Evolution.tryFind (ItemUse "LEAF_STONE") gloom).Value.Target)
    Assert.Equal("BELLOSSOM", (Evolution.tryFind (ItemUse "SUN_STONE") gloom).Value.Target)
    Assert.True(Evolution.tryFind (ItemUse "LEAF_STONE") { gloom with HeldItem = Some "EVERSTONE" } |> Option.isNone)

[<Fact>]
let ``BAT-014 Tyrogue stat evolution compares calculated attack and defense`` () =
    let target dvs =
        let mon = PartyMon.createWithDvs (Species.byName "TYROGUE").Dex 20 dvs
        (Evolution.tryFind (LevelUp Day) mon).Value.Target
    Assert.Equal("HITMONCHAN", target 0x0F00)
    Assert.Equal("HITMONLEE", target 0xF000)
    Assert.Equal("HITMONTOP", target 0x0000)

[<Fact>]
let ``BAT-014 trade evolution enforces and consumes held catalysts`` () =
    let poliwhirl held = { PartyMon.create (Species.byName "POLIWHIRL").Dex 25 with HeldItem = held }
    Assert.True(Evolution.tryFind (Trade false) (poliwhirl None) |> Option.isNone)
    let candidate = (Evolution.tryFind (Trade false) (poliwhirl (Some "KINGS_ROCK"))).Value
    Assert.Equal("POLITOED", candidate.Target)
    Assert.True(candidate.ConsumeHeldItem)
    Assert.True((Evolution.applyCandidate candidate (poliwhirl (Some "KINGS_ROCK"))).HeldItem.IsNone)
    Assert.True(Evolution.tryFind (Trade true) (poliwhirl (Some "KINGS_ROCK")) |> Option.isNone)

[<Fact>]
let ``BAT-014 bare trade ignores unrelated held item but Everstone blocks`` () =
    let kadabra held = { PartyMon.create (Species.byName "KADABRA").Dex 16 with HeldItem = held }
    let candidate = (Evolution.tryFind (Trade false) (kadabra (Some "BERRY"))).Value
    Assert.Equal("ALAKAZAM", candidate.Target)
    Assert.False(candidate.ConsumeHeldItem)
    Assert.True(Evolution.tryFind (Trade false) (kadabra (Some "EVERSTONE")) |> Option.isNone)
