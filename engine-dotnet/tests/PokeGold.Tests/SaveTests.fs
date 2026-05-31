module PokeGold.Tests.SaveTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Save

// SaveData.capture/apply are pure and IO-free; SaveFile.serialize/deserialize
// round-trip the model through JSON. We exercise both without touching disk.

[<Fact>]
let ``a save round-trips through JSON unchanged`` () =
    let save =
        { Version = SaveData.CurrentVersion
          Overworld = { MapId = "AzaleaTown"; CellX = 9; CellY = 12; Facing = "Left" }
          World =
            { Events = [| "EVENT_A" |]
              EngineFlags = [||]
              Vars = [| { Name = "VAR_X"; Value = 3 } |]
              Scenes = [||] }
          Bag = [| { Item = "POTION"; Qty = 2 } |] }

    let json = SaveFile.serialize save

    match SaveFile.deserialize json with
    | Some back ->
        Assert.Equal(save.Version, back.Version)
        Assert.Equal(save.Overworld.MapId, back.Overworld.MapId)
        Assert.Equal(save.Overworld.CellX, back.Overworld.CellX)
        Assert.Equal(save.Overworld.CellY, back.Overworld.CellY)
        Assert.Equal(save.Overworld.Facing, back.Overworld.Facing)
        Assert.Equal<string[]>(save.World.Events, back.World.Events)
        Assert.Equal(2, (SaveData.bagOf back).["POTION"])
        Assert.Equal(3, (SaveData.worldOf back |> World.getVar "VAR_X"))
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

// ---- M9.5 — world (flags/vars/scenes) + bag persistence, warps -------------

[<Fact>]
let ``captureWith then JSON round-trip restores world flags, vars, and bag`` () =
    let content = Content()
    let state = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down

    let world =
        World.empty
        |> World.setEvent "EVENT_CLEARED_SLOWPOKE_WELL"
        |> World.setFlag "ENGINE_ZEPHYRBADGE"
        |> World.setVar "VAR_BADGES" 1
        |> World.setScene "" 2

    let bag = Map.ofList [ "POTION", 3; "GS_BALL", 1 ]

    // Round-trip through the actual on-disk JSON shape.
    let back =
        SaveData.captureWith state world bag
        |> SaveFile.serialize
        |> SaveFile.deserialize
        |> Option.get

    let w = SaveData.worldOf back
    Assert.True(World.hasEvent "EVENT_CLEARED_SLOWPOKE_WELL" w)
    Assert.True(World.hasFlag "ENGINE_ZEPHYRBADGE" w)
    Assert.Equal(1, World.getVar "VAR_BADGES" w)
    Assert.Equal(2, World.getScene "" w)

    let b = SaveData.bagOf back
    Assert.Equal(3, b.["POTION"])
    Assert.Equal(1, b.["GS_BALL"])

[<Fact>]
let ``a v1 (position-only) save loads with an empty world and bag`` () =
    // Older saves predate the world/bag block; they must still load cleanly.
    let json = """{ "Version": 1, "Overworld": { "MapId": "AzaleaTown", "CellX": 1, "CellY": 2, "Facing": "Down" } }"""

    match SaveFile.deserialize json with
    | Some save ->
        Assert.Equal(World.empty, SaveData.worldOf save)
        Assert.True((SaveData.bagOf save).IsEmpty)
    | None -> Assert.Fail("a v1 save should still be readable")

[<Fact>]
let ``tryWarp loads the destination map and lands on the paired warp`` () =
    let content = Content()

    // AzaleaTown's first warp tile is (15, 9); warp id 1 lands the player there.
    match OverworldState.tryWarp content "AZALEA_TOWN" 1 with
    | Some s ->
        Assert.Equal("AzaleaTown", s.MapId)
        Assert.Equal((15, 9), (s.Player.CellX, s.Player.CellY))
        Assert.Equal(Standing, s.Player.Motion)
    | None -> Assert.Fail("AZALEA_TOWN should resolve to a loadable map")

[<Fact>]
let ``tryWarp is a no-op for a destination map whose assets are not in the tree`` () =
    let content = Content()
    // RUINS_OF_ALPH_HO_OH_CHAMBER is a real map (baked metadata/events) but has no
    // `.blk` block layout in the tree yet, so it isn't loadable — the warp no-ops.
    Assert.Equal(None, OverworldState.tryWarp content "RUINS_OF_ALPH_HO_OH_CHAMBER" 1)
