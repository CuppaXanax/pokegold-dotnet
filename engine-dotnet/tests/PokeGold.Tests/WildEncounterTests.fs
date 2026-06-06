module PokeGold.Tests.WildEncounterTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Overworld

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
