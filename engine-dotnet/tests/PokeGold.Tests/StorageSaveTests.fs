module PokeGold.Tests.StorageSaveTests

open Xunit
open PokeGold.Game.Player
open PokeGold.Game.Save

[<Fact>]
let ``Storage.empty has 14 boxes`` () =
    Assert.Equal(14, Storage.empty.Boxes.Length)

[<Fact>]
let ``Storage.empty boxes are named BOX 1 through BOX 14`` () =
    Assert.Equal("BOX 1",  Storage.empty.Boxes.[0].Name)
    Assert.Equal("BOX 14", Storage.empty.Boxes.[13].Name)

[<Fact>]
let ``Storage.empty boxes are all empty`` () =
    for b in Storage.empty.Boxes do
        Assert.Empty(b.Mons)

[<Fact>]
let ``Storage.empty has no PC items or mail`` () =
    Assert.Empty(Storage.empty.PcItems)
    Assert.Empty(Storage.empty.Mailbox)

[<Fact>]
let ``save round-trip preserves a deposited mon in box 3`` () =
    let content = PokeGold.Game.Data.Content()
    let ow = PokeGold.Game.Overworld.OverworldState.loadByIdAt content "AzaleaTown" 9 12 PokeGold.Game.Core.Down
    let mon = PartyMon.create 155 5
    let pc =
        { Storage.empty with
            Boxes =
                Storage.empty.Boxes
                |> Array.mapi (fun i b -> if i = 2 then { b with Mons = [ mon ] } else b) }
    let player = { PlayerStateOps.initial with Pc = pc }
    let json =
        SaveData.captureWith ow PokeGold.Game.Overworld.Script.World.empty player
        |> SaveFile.serialize
    let back = SaveFile.deserialize json |> Option.get
    let p2 = SaveData.playerOf back
    Assert.Equal(1, p2.Pc.Boxes.[2].Mons.Length)
    Assert.Equal(155, p2.Pc.Boxes.[2].Mons.[0].SpeciesId)
    // Other boxes still empty.
    Assert.Equal(0, p2.Pc.Boxes.[0].Mons.Length)

[<Fact>]
let ``a v3-style save with no Pc field loads with Storage.empty`` () =
    // A v3 save has no Pc field; it should deserialise to an empty PC (null-safe migration).
    let v3Json =
        """{"Version":3,"Overworld":{"MapId":"AzaleaTown","CellX":9,"CellY":12,"Facing":"Down"},"World":{"Events":[],"EngineFlags":[],"Vars":[],"Scenes":[]},"Bag":[],"Player":{"Name":"GOLD","Money":1000,"Party":[],"PocketedBag":{"Items":[],"Balls":[],"KeyItems":[],"TmHm":[]},"DexSeen":[],"DexOwn":[],"Badges":0,"Options":{"TextSpeed":2,"BoxBorder":0,"Sound":0}}}"""
    let save = SaveFile.deserialize v3Json |> Option.get
    let player = SaveData.playerOf save
    Assert.Equal(14, player.Pc.Boxes.Length)
    Assert.Empty(player.Pc.PcItems)
    Assert.Empty(player.Pc.Mailbox)

[<Fact>]
let ``save round-trip preserves box name rename`` () =
    let content = PokeGold.Game.Data.Content()
    let ow = PokeGold.Game.Overworld.OverworldState.loadByIdAt content "AzaleaTown" 9 12 PokeGold.Game.Core.Down
    let pc =
        { Storage.empty with
            Boxes =
                Storage.empty.Boxes
                |> Array.mapi (fun i b -> if i = 0 then { b with Name = "FIRE" } else b) }
    let player = { PlayerStateOps.initial with Pc = pc }
    let back =
        SaveData.captureWith ow PokeGold.Game.Overworld.Script.World.empty player
        |> SaveFile.serialize
        |> SaveFile.deserialize
        |> Option.get
        |> SaveData.playerOf
    Assert.Equal("FIRE", back.Pc.Boxes.[0].Name)
