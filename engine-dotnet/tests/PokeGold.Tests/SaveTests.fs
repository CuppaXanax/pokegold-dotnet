module PokeGold.Tests.SaveTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Save

// SaveData.capture/apply are pure and IO-free; SaveFile.serialize/deserialize
// round-trip the model through JSON. We exercise both without touching disk.

[<Fact>]
let ``a save round-trips through JSON unchanged`` () =
    let save =
        { Version = SaveData.CurrentVersion
          Overworld = { MapId = "AzaleaTown"; CellX = 9; CellY = 12; Facing = "Left" } }

    let json = SaveFile.serialize save

    match SaveFile.deserialize json with
    | Some back ->
        Assert.Equal(save.Version, back.Version)
        Assert.Equal(save.Overworld.MapId, back.Overworld.MapId)
        Assert.Equal(save.Overworld.CellX, back.Overworld.CellX)
        Assert.Equal(save.Overworld.CellY, back.Overworld.CellY)
        Assert.Equal(save.Overworld.Facing, back.Overworld.Facing)
    | None -> Assert.Fail("expected a readable save")

[<Fact>]
let ``capture records the current schema version`` () =
    let content = Content()
    let state = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Up
    let save = SaveData.capture state
    Assert.Equal(SaveData.CurrentVersion, save.Version)

[<Fact>]
let ``capture then apply restores map, cell, and facing`` () =
    let content = Content()
    // Start from an explicit, off-default position and facing.
    let original = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Left

    let restored = SaveData.apply content (SaveData.capture original)

    Assert.Equal(original.MapId, restored.MapId)
    Assert.Equal(original.Player.CellX, restored.Player.CellX)
    Assert.Equal(original.Player.CellY, restored.Player.CellY)
    Assert.Equal(original.Player.Facing, restored.Player.Facing)
    // A restored player is at rest, not mid-step.
    Assert.Equal(Standing, restored.Player.Motion)

[<Fact>]
let ``deserialize rejects an unknown future version`` () =
    let json = """{ "Version": 999, "Overworld": { "MapId": "AzaleaTown", "CellX": 1, "CellY": 2, "Facing": "Down" } }"""
    Assert.Equal(None, SaveFile.deserialize json)

[<Fact>]
let ``deserialize returns None on malformed JSON`` () =
    Assert.Equal(None, SaveFile.deserialize "not json at all")
