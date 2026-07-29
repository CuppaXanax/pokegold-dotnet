module PokeGold.Tests.EggHatchTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Save

let private egg remaining =
    { PartyMon.create 1 5 with
        Nickname = "EGG"
        Moves = [ 33, 35 ]
        HatchSteps = Some remaining }

[<Fact>]
let ``walking decrements only active egg hatch counters`` () =
    let eggMon = egg 2
    let regular = PartyMon.create 4 5
    let player = { PlayerStateOps.initial with Party = [ eggMon; regular ] }

    let once = EggHatching.step player
    let twice = EggHatching.step once

    Assert.Equal(Some 1, once.Party.[0].HatchSteps)
    Assert.Equal(None, once.Party.[1].HatchSteps)
    Assert.Equal(None, twice.Party.[0].HatchSteps)
    Assert.False(Breeding.isEgg twice.Party.[0])

[<Fact>]
let ``egg hatching preserves its generated species level and moves`` () =
    let eggMon = egg 1
    let player = { PlayerStateOps.initial with Name = "KRIS"; Party = [ eggMon ] }

    let hatched = EggHatching.step player
    let mon = hatched.Party.[0]

    Assert.Equal(eggMon.SpeciesId, mon.SpeciesId)
    Assert.Equal(eggMon.Level, mon.Level)
    Assert.True(eggMon.Moves = mon.Moves)
    Assert.Equal("BULBASAUR", mon.Nickname)
    Assert.Equal("KRIS", mon.OtName)
    Assert.False(Breeding.isEgg mon)

[<Fact>]
let ``hatching registers the species only when the egg opens`` () =
    let eggMon = egg 1
    let player = { PlayerStateOps.initial with Party = [ eggMon ] }

    Assert.False(Set.contains eggMon.SpeciesId player.DexSeen)
    Assert.False(Set.contains eggMon.SpeciesId player.DexOwn)

    let hatched = EggHatching.step player

    Assert.True(Set.contains eggMon.SpeciesId hatched.DexSeen)
    Assert.True(Set.contains eggMon.SpeciesId hatched.DexOwn)

[<Fact>]
let ``save reload preserves egg hatch steps`` () =
    let content = Content()
    let state = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down
    let player = { PlayerStateOps.initial with Party = [ egg 37 ] }

    let restored =
        SaveData.captureWith state World.empty player
        |> SaveFile.serialize
        |> SaveFile.deserialize
        |> Option.map SaveData.playerOf
        |> Option.get

    Assert.Equal(Some 37, restored.Party.[0].HatchSteps)
