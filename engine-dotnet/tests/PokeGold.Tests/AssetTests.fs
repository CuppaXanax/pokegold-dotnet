module PokeGold.Tests.AssetTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data

// These tests load real repository assets via the engine's loaders and
// spot-check counts/sizes/known bytes against the source files.

[<Fact>]
let ``repo root is discoverable`` () =
    Assert.True(System.IO.File.Exists(Assets.path "roms.sha1"))

[<Fact>]
let ``grayToIndex maps the four pret shades correctly`` () =
    Assert.Equal(0uy, Image.grayToIndex 255uy) // white -> 0
    Assert.Equal(1uy, Image.grayToIndex 170uy)
    Assert.Equal(2uy, Image.grayToIndex 85uy)
    Assert.Equal(3uy, Image.grayToIndex 0uy) // black -> 3

[<Fact>]
let ``johto_modern tileset PNG decodes to 96 tiles`` () =
    let tiles = Image.loadTiles "gfx/tilesets/johto_modern.png"
    Assert.Equal(96, tiles.Length)
    Assert.Equal(Tile.PixelCount, tiles.[0].Pixels.Length)

[<Fact>]
let ``johto_modern metatiles parse to 128 blocks of 16 tile ids`` () =
    let ts = Tileset.loadNamed "johto_modern"
    Assert.Equal(128, ts.Blocks.Length)
    Assert.Equal(16, ts.Blocks.[0].TileIds.Length)
    // Block 0x05 (grass) tile ids verified directly from johto_modern_metatiles.bin.
    let expected = [| 0x1Euy; 0x1Fuy; 0x1Euy; 0x1Fuy; 0x13uy; 0x15uy; 0x13uy; 0x15uy
                      0x13uy; 0x15uy; 0x13uy; 0x15uy; 0x3Euy; 0x3Fuy; 0x3Euy; 0x3Fuy |]
    Assert.Equal<byte[]>(expected, ts.Blocks.[0x05].TileIds)

[<Fact>]
let ``AzaleaTown map loads as 20x9 = 180 block ids`` () =
    let map = Map.load 20 9 "maps/AzaleaTown.blk"
    Assert.Equal(20, map.Width)
    Assert.Equal(9, map.Height)
    Assert.Equal(180, map.BlockIds.Length)
    // First block id in AzaleaTown.blk is 0x05 (grass).
    Assert.Equal(0x05uy, Map.blockAt map 0 0)

[<Fact>]
let ``a real .pal file loads into colors`` () =
    let pal = Palette.load "gfx/tilesets/bg_tiles.pal"
    Assert.True(pal.Colors.Length >= 4)
