module PokeGold.Tests.WildEncounterTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Battle
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Save

type private FixedRandom(values: int list) =
    inherit System.Random()
    let mutable remaining = values

    override _.Next(maxValue: int) =
        match remaining with
        | value :: rest ->
            remaining <- rest
            value % maxValue
        | [] -> 0

[<Fact>]
let ``isEncounterTile identifies grass and water tiles`` () =
    Assert.True(WildEncounter.isEncounterTile 0x18uy)
    Assert.True(WildEncounter.isEncounterTile 0x14uy)
    Assert.True(WildEncounter.isEncounterTile 0x29uy)
    Assert.False(WildEncounter.isEncounterTile 0x00uy)

[<Fact>]
let ``selectSlot maps rolls to 7 probability slots`` () =
    Assert.Equal(0, WildEncounter.selectSlot 0)
    Assert.Equal(0, WildEncounter.selectSlot 29)
    Assert.Equal(1, WildEncounter.selectSlot 30)
    Assert.Equal(2, WildEncounter.selectSlot 60)
    Assert.Equal(6, WildEncounter.selectSlot 99)

[<Fact>]
let ``shouldEncounter returns true when roll below threshold`` () =
    Assert.True(WildEncounter.shouldEncounter 25 0)
    Assert.False(WildEncounter.shouldEncounter 25 255)

[<Fact>]
let ``Cleanse Tag reduces encounter rate`` () =
    let lead = { PartyMon.create 155 10 with HeldItem = Some "CLEANSE_TAG" }
    let player = { PlayerStateOps.initial with Party = [ lead ] }

    Assert.Equal(16, WildEncounter.effectiveRate player 25)
    Assert.Equal(25, WildEncounter.effectiveRate PlayerStateOps.initial 25)

[<Fact>]
let ``fishEncounter returns rod-specific encounters`` () =
    let oldRod = WildEncounter.fishEncounter "OLD_ROD" (System.Random(0))
    Assert.Equal(("MAGIKARP", 10), oldRod)

    let goodRod = WildEncounter.fishEncounter "GOOD_ROD" (System.Random(0))
    Assert.Contains(fst goodRod, [| "MAGIKARP"; "POLIWAG" |])
    Assert.True(15 <= snd goodRod && snd goodRod <= 24)

    let superRod = WildEncounter.fishEncounter "SUPER_ROD" (System.Random(0))
    Assert.Contains(fst superRod, [| "POLIWAG"; "MAGIKARP"; "POLIWHIRL"; "TENTACRUEL" |])
    Assert.True(20 <= snd superRod && snd superRod <= 34)

[<Fact>]
let ``SPROUT_TOWER_2F has grass encounters`` () =
    match WildEncounters.forMap "SPROUT_TOWER_2F" with
    | Some t ->
        Assert.Equal(7, t.GrassMorn.Length)
        Assert.Equal("RATTATA", t.GrassMorn.[0].Species)
    | None -> Assert.Fail("should have data")

[<Fact>]
let ``RUINS_OF_ALPH_OUTSIDE has water encounters`` () =
    match WildEncounters.forMap "RUINS_OF_ALPH_OUTSIDE" with
    | Some t ->
        Assert.Equal(3, t.Water.Length)
        Assert.Equal("WOOPER", t.Water.[0].Species)
    | None -> Assert.Fail("should have data")

[<Fact>]
let ``BAT-003 generated species preserve wild item slots and gender ratio`` () =
    let pikachu = Species.byName "PIKACHU"

    Assert.Equal(None, pikachu.Item1)
    Assert.Equal(Some "BERRY", pikachu.Item2)
    Assert.Equal(127, pikachu.GenderRatio)

[<Fact>]
let ``BAT-003 wild held item rolls match source boundaries`` () =
    let furret = Species.byName "FURRET"

    Assert.Equal(None, WildOpponent.rollHeldItem WildBattleType.Normal (FixedRandom([ 191 ])) furret)
    Assert.Equal(Some "GOLD_BERRY", WildOpponent.rollHeldItem WildBattleType.Normal (FixedRandom([ 192; 19 ])) furret)
    Assert.Equal(Some "BERRY", WildOpponent.rollHeldItem WildBattleType.Normal (FixedRandom([ 192; 20 ])) furret)
    Assert.Equal(Some "BERRY", WildOpponent.rollHeldItem WildBattleType.ForceItem (FixedRandom([])) furret)

[<Fact>]
let ``BAT-003 wild DVs and gender derive from source attributes`` () =
    let pikachu = Species.byName "PIKACHU"
    let hoOh = Species.byName "HO_OH"
    let dvs = WildOpponent.rollDvs WildBattleType.Normal (FixedRandom([ 0xAB; 0xCD ]))

    Assert.Equal(0xABCD, dvs)
    Assert.Equal(0xEAAA, WildOpponent.rollDvs WildBattleType.ForceShiny (FixedRandom([])))
    Assert.Equal(Male, WildOpponent.genderFromDvs pikachu dvs)
    Assert.Equal(Genderless, WildOpponent.genderFromDvs hoOh dvs)

[<Fact>]
let ``InitRoamMons seeds beasts at disassembly starting routes`` () =
    let roamers = Roaming.init World.empty |> Roaming.active

    Assert.Equal(3, roamers.Length)
    Assert.Equal(("RAIKOU", 40, "Route42", 0), (roamers.[0].Species, roamers.[0].Level, roamers.[0].MapId, roamers.[0].Hp))
    Assert.Equal(("ENTEI", 40, "Route37", 0), (roamers.[1].Species, roamers.[1].Level, roamers.[1].MapId, roamers.[1].Hp))
    Assert.Equal(("SUICUNE", 40, "Route38", 0), (roamers.[2].Species, roamers.[2].Level, roamers.[2].MapId, roamers.[2].Hp))

[<Fact>]
let ``roamer world state survives save capture`` () =
    let content = Content()
    let state = OverworldState.loadByIdAt content "Route42" 10 10 Down
    let world = Roaming.init World.empty
    let save = SaveData.captureWith state world PlayerStateOps.initial

    Assert.Equal<Roamer list>(Roaming.active world, Roaming.active (SaveData.worldOf save))

[<Fact>]
let ``roamer on current grass route overrides ordinary encounter`` () =
    let rng = FixedRandom([ 0; 1 ])
    let world = Roaming.init World.empty

    let encountered =
        WildEncounter.tryEncounter "Route42" WildEncounter.CollTallGrass rng PlayerStateOps.initial world

    Assert.Equal(Some("RAIKOU", 40), encountered)
