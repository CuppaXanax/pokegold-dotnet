module PokeGold.Tests.SaveTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
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
              Scenes = [||]
              StringBuffers = [||] }
          Bag = [| { Item = "POTION"; Qty = 2 } |]
          Player = Unchecked.defaultof<_> }

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
let ``captureWith then JSON round-trip restores world flags, vars, and player`` () =
    let content = Content()
    let state = OverworldState.loadByIdAt content "AzaleaTown" 9 12 Down

    let world =
        World.empty
        |> World.setEvent "EVENT_CLEARED_SLOWPOKE_WELL"
        |> World.setFlag "ENGINE_ZEPHYRBADGE"
        |> World.setVar "VAR_BADGES" 1
        |> World.setScene "" 2

    let player = { PlayerStateOps.initial with Bag = Bag.empty |> Bag.add "POTION" 3 |> Bag.add "GS_BALL" 1 }

    // Round-trip through the actual on-disk JSON shape.
    let back =
        SaveData.captureWith state world player
        |> SaveFile.serialize
        |> SaveFile.deserialize
        |> Option.get

    let w = SaveData.worldOf back
    Assert.True(World.hasEvent "EVENT_CLEARED_SLOWPOKE_WELL" w)
    Assert.True(World.hasFlag "ENGINE_ZEPHYRBADGE" w)
    Assert.Equal(1, World.getVar "VAR_BADGES" w)
    Assert.Equal(2, World.getScene "" w)

    let p = SaveData.playerOf back
    Assert.Equal(3, Bag.count "POTION" p.Bag)
    Assert.Equal(1, Bag.count "GS_BALL" p.Bag)

[<Fact>]
let ``a v1 (position-only) save loads with an empty world and bag`` () =
    // Older saves predate the world/bag block; they must still load cleanly.
    let json = """{ "Version": 1, "Overworld": { "MapId": "AzaleaTown", "CellX": 1, "CellY": 2, "Facing": "Down" } }"""

    match SaveFile.deserialize json with
    | Some save ->
        Assert.Equal(World.empty, SaveData.worldOf save)
        let player = SaveData.playerOf save
        Assert.Equal(0, Bag.count "POTION" player.Bag)
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
let ``tryWarp loads dark cave maps using cave metatile and collision aliases`` () =
    let content = Content()
    // gfx/tilesets.asm gives Dark Cave its own GFX but aliases its metatiles and
    // collision to Cave, so DARK_CAVE_VIOLET_ENTRANCE is loadable.
    match OverworldState.tryWarp content "DARK_CAVE_VIOLET_ENTRANCE" 1 with
    | Some s ->
        Assert.Equal("DarkCaveVioletEntrance", s.MapId)
        Assert.Equal((3, 15), (s.Player.CellX, s.Player.CellY))
    | None -> Assert.Fail("DARK_CAVE_VIOLET_ENTRANCE should resolve to a loadable map")

// ---- M10.4 — explicit script warps (warp / warpfacing) ---------------------

[<Fact>]
let ``tryWarpExplicit loads the destination map at the given cell and facing`` () =
    let content = Content()

    match OverworldState.tryWarpExplicit content "AZALEA_TOWN" 7 11 (Some "UP") Down with
    | Some s ->
        Assert.Equal("AzaleaTown", s.MapId)
        Assert.Equal((7, 11), (s.Player.CellX, s.Player.CellY))
        Assert.Equal(Up, s.Player.Facing)
    | None -> Assert.Fail("AZALEA_TOWN should resolve to a loadable map")

[<Fact>]
let ``tryWarpExplicit keeps the fallback facing when the command gives none`` () =
    let content = Content()

    match OverworldState.tryWarpExplicit content "AZALEA_TOWN" 7 11 None Left with
    | Some s -> Assert.Equal(Left, s.Player.Facing)
    | None -> Assert.Fail("AZALEA_TOWN should resolve to a loadable map")

[<Fact>]
let ``tryWarpExplicit is a no-op for unknown maps and loads dark cave aliases`` () =
    let content = Content()
    Assert.Equal(None, OverworldState.tryWarpExplicit content "MAP_THAT_DOES_NOT_EXIST" 1 1 None Down)

    match OverworldState.tryWarpExplicit content "DARK_CAVE_VIOLET_ENTRANCE" 1 1 None Down with
    | Some s ->
        Assert.Equal("DarkCaveVioletEntrance", s.MapId)
        Assert.Equal((1, 1), (s.Player.CellX, s.Player.CellY))
        Assert.Equal(Down, s.Player.Facing)
    | None -> Assert.Fail("DARK_CAVE_VIOLET_ENTRANCE should resolve to a loadable map")
