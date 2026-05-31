module PokeGold.Tests.PlayerStateTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Save

// --- Generated data spot-checks -------------------------------------------

[<Fact>]
let ``ItemsData has POTION with correct pocket and price`` () =
    let p = ItemsData.byId.["POTION"]
    Assert.Equal(Pocket.Item, p.Pocket)
    Assert.Equal(300, p.Price)
    Assert.False(System.String.IsNullOrEmpty p.Description)

[<Fact>]
let ``ItemsData all array has ~196 items`` () =
    // The disassembly has 195 named items (excluding NO_ITEM)
    Assert.True(ItemsData.all.Length >= 150)
    Assert.True(ItemsData.all.Length <= 260)

[<Fact>]
let ``DexData has CYNDAQUIL at num 155`` () =
    let e = DexData.byNum.[155]
    Assert.Equal("CYNDAQUIL", e.Name)
    Assert.False(System.String.IsNullOrEmpty e.Category)

[<Fact>]
let ``DexData has 251 entries`` () =
    Assert.Equal(251, DexData.all.Length)

// --- Bag tests -----------------------------------------------------------

[<Fact>]
let ``Bag add routes to correct pocket`` () =
    let bag = Bag.empty |> Bag.add "POTION" 3 |> Bag.add "POKE_BALL" 5
    Assert.Equal(3, bag.Items |> List.find (fun (id,_) -> id = "POTION") |> snd)
    Assert.Equal(5, bag.Balls |> List.find (fun (id,_) -> id = "POKE_BALL") |> snd)

[<Fact>]
let ``Bag stack caps at 99`` () =
    let bag = Bag.empty |> Bag.add "POTION" 50 |> Bag.add "POTION" 60
    Assert.Equal(99, Bag.count "POTION" bag)

[<Fact>]
let ``Bag remove decrements and removes`` () =
    let bag = Bag.empty |> Bag.add "ANTIDOTE" 3 |> Bag.remove "ANTIDOTE" 2
    Assert.Equal(1, Bag.count "ANTIDOTE" bag)
    let empty = bag |> Bag.remove "ANTIDOTE" 1
    Assert.Equal(0, Bag.count "ANTIDOTE" empty)

[<Fact>]
let ``Bag ofFlat migration round-trip`` () =
    let flat = Map.ofList [ "POTION", 3; "POKE_BALL", 5; "BICYCLE", 1 ]
    let bag = Bag.ofFlat flat
    Assert.Equal(3, Bag.count "POTION" bag)
    Assert.Equal(5, Bag.count "POKE_BALL" bag)
    Assert.Equal(1, Bag.count "BICYCLE" bag)
    let back = Bag.toFlat bag
    Assert.Equal(3, back.["POTION"])

// --- PartyMon stat derivation tests -------------------------------------------

[<Fact>]
let ``PartyMon MaxHp matches BattleMon for same species+level`` () =
    let cyndaquil = PartyMon.create 155 5
    let species = PokeGold.Game.Data.Species.all.["CYNDAQUIL"]
    let bm = PokeGold.Game.Battle.BattleMon.ofSpecies species 5 []
    Assert.Equal(bm.MaxHp, cyndaquil.MaxHp)

// --- SaveData v3 round-trip tests -----------------------------------------------

[<Fact>]
let ``SaveData v3 round-trip preserves PlayerState`` () =
    let content = PokeGold.Game.Data.Content()
    let state = PokeGold.Game.Overworld.OverworldState.loadByIdAt content "AzaleaTown" 9 12 PokeGold.Game.Core.Up
    let player =
        { PlayerState.initial with
            Money = 1234
            DexSeen = Set.ofList [1; 2; 155]
            Party = [ PartyMon.create 155 5 ] }
    let save = SaveData.captureWith state PokeGold.Game.Overworld.Script.World.empty player
    let json = SaveFile.serialize save
    let back = SaveFile.deserialize json |> Option.get
    let p2 = SaveData.playerOf back
    Assert.Equal(1234, p2.Money)
    Assert.True(p2.DexSeen.Contains 155)
    Assert.Equal(1, p2.Party.Length)
    Assert.Equal(155, p2.Party.[0].SpeciesId)

[<Fact>]
let ``SaveData v2 migration: flat bag repocketed, party empty, money uses default`` () =
    let v2Json = """{"Version":2,"Overworld":{"MapId":"AzaleaTown","CellX":9,"CellY":12,"Facing":"Down"},"World":{"Events":[],"EngineFlags":[],"Vars":[],"Scenes":[]},"Bag":[{"Item":"POTION","Qty":3},{"Item":"POKE_BALL","Qty":5}]}"""
    let save = SaveFile.deserialize v2Json |> Option.get
    let player = SaveData.playerOf save
    // v2 saves had no player state, so money, party, dex all use initial/empty defaults
    Assert.Equal(PlayerState.initial.Money, player.Money)  // Default starting money
    Assert.Equal(0, player.Party.Length)
    Assert.Equal(3, Bag.count "POTION" player.Bag)
    Assert.Equal(5, Bag.count "POKE_BALL" player.Bag)
