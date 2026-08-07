module PokeGold.Tests.GraphicsTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Render

[<Fact>]
let ``rgb555 expands 5-bit channels to 8-bit, opaque`` () =
    let white = Palette.rgb555 31 31 31
    Assert.Equal(255uy, white.R)
    Assert.Equal(255uy, white.G)
    Assert.Equal(255uy, white.B)
    Assert.Equal(255uy, white.A)

    let black = Palette.rgb555 0 0 0
    Assert.Equal(0uy, black.R)
    Assert.Equal(0uy, black.G)
    Assert.Equal(0uy, black.B)
    Assert.Equal(255uy, black.A)

    // 5-bit magenta -> R and B full, G zero.
    let magenta = Palette.rgb555 31 0 31
    Assert.Equal(255uy, magenta.R)
    Assert.Equal(0uy, magenta.G)
    Assert.Equal(255uy, magenta.B)

[<Fact>]
let ``parse reads pret .pal RGB lines and ignores comments`` () =
    let text = "; blue\nRGB 30, 26, 15\nRGB 04, 17, 31\n"
    let pal = Palette.parse text
    Assert.Equal(2, pal.Colors.Length)
    Assert.Equal(Palette.rgb555 30 26 15, pal.Colors.[0])
    Assert.Equal(Palette.rgb555 4 17 31, pal.Colors.[1])

[<Fact>]
let ``parsePalBank reads grouped palette banks from .pal text`` () =
    let text =
        "RGB 1, 2, 3\nRGB 4, 5, 6\nRGB 7, 8, 9\nRGB 10, 11, 12\n\n" +
        "RGB 13, 14, 15\nRGB 16, 17, 18\nRGB 19, 20, 21\nRGB 22, 23, 24\n"

    let banks = Palette.parsePalBank text

    Assert.Equal(2, banks.Length)
    Assert.Equal(4, banks.[0].Colors.Length)
    Assert.Equal(Palette.rgb555 1 2 3, banks.[0].Colors.[0])
    Assert.Equal(Palette.rgb555 10 11 12, banks.[0].Colors.[3])
    Assert.Equal(Palette.rgb555 13 14 15, banks.[1].Colors.[0])
    Assert.Equal(Palette.rgb555 22 23 24, banks.[1].Colors.[3])

[<Fact>]
let ``parsePalBank loads real bg_tiles palette data`` () =
    let banks = Palette.parsePalBank (Assets.readText "gfx/tilesets/bg_tiles.pal")

    Assert.True(banks.Length >= 8)
    Assert.Equal(4, banks.[0].Colors.Length)
    Assert.Equal(Palette.rgb555 27 31 27, banks.[8].Colors.[0])

[<Fact>]
let ``generated overworld palettes preserve source banks and tile attributes`` () =
    Assert.Equal(42, PaletteData.backgroundBanks.Length)
    Assert.Equal(32, PaletteData.objectBanks.Length)

    let attributes = PaletteData.tilesets.["johto_modern"]
    Assert.Equal(96, attributes.Length)
    Assert.Equal<byte>(0uy, attributes.[0])
    Assert.Equal<byte>(5uy, attributes.[1])
    Assert.Equal<byte>(5uy, attributes.[2])
    Assert.Equal<byte>(1uy, attributes.[3])

[<Fact>]
let ``map renderer selects the source palette bank for each tile`` () =
    let indexedTile = { Pixels = Array.create Tile.PixelCount 1uy }
    let block = { TileIds = Array.init 16 (fun index -> byte (index % 2)) }
    let tileset =
        { Tiles = [| indexedTile; indexedTile |]
          Blocks = [| block |]
          PaletteIds = [| 0uy; 1uy |] }
    let map = Map.ofBlk 1 1 [| 0uy |]
    let palettes =
        [| Palette.ofColors [ Palette.rgb555 0 0 0; Palette.rgb555 31 0 0 ]
           Palette.ofColors [ Palette.rgb555 0 0 0; Palette.rgb555 0 31 0 ] |]
    let fb = Framebuffer()

    MapRenderer.draw fb palettes tileset map 0 0

    let colorAt x =
        let offset = x * 4
        fb.Pixels.[offset], fb.Pixels.[offset + 1], fb.Pixels.[offset + 2]

    Assert.Equal((255uy, 0uy, 0uy), colorAt 0)
    Assert.Equal((0uy, 255uy, 0uy), colorAt 8)

[<Fact>]
let ``map palette resolution follows source environment time and water banks`` () =
    let day = OverworldState.resolveMapPalettes "AzaleaTown" Day false
    let night = OverworldState.resolveMapPalettes "AzaleaTown" Nite false
    let indoor = OverworldState.resolveMapPalettes "AzaleaPokecenter1F" Day false

    Assert.Equal(PaletteData.backgroundBanks.[8], day.[0])
    Assert.Equal(PaletteData.backgroundBanks.[40], day.[3])
    Assert.Equal(PaletteData.backgroundBanks.[16], night.[0])
    Assert.Equal(PaletteData.backgroundBanks.[41], night.[3])
    Assert.Equal(PaletteData.backgroundBanks.[32], indoor.[0])

[<Fact>]
let ``sprite palette resolution honors explicit color and source default`` () =
    let explicitBlue = OverworldState.resolveSpritePalette "SPRITE_OAK" "PAL_NPC_BLUE" Day
    let oakDefault = OverworldState.resolveSpritePalette "SPRITE_OAK" "0" Day

    Assert.Equal(PaletteData.objectBanks.[9], explicitBlue)
    Assert.Equal(PaletteData.objectBanks.[11], oakDefault)
    Assert.NotEqual(explicitBlue, oakDefault)

[<Fact>]
let ``decode reads a 2bpp tile into the expected 8x8 index grid`` () =
    // Vertical bands: columns map to indices 0,0,1,1,2,2,3,3.
    // Each row: low bitplane 0x33, high bitplane 0x0F.
    let bytes =
        [| for _ in 1..8 do
               yield 0x33uy
               yield 0x0Fuy |]

    let tile = Tile.decode bytes 0
    let expectedRow = [| 0uy; 0uy; 1uy; 1uy; 2uy; 2uy; 3uy; 3uy |]

    for row in 0..7 do
        for col in 0..7 do
            Assert.Equal(expectedRow.[col], tile.Pixels.[row * 8 + col])

[<Fact>]
let ``decodeSheet splits data into 16-byte tiles`` () =
    let bytes = Array.zeroCreate<byte> (Tile.BytesPerTile * 3)
    let sheet = Tile.decodeSheet bytes
    Assert.Equal(3, sheet.Length)

[<Fact>]
let ``drawTile writes palette colors into the framebuffer`` () =
    let fb = Framebuffer()

    let bytes =
        [| for _ in 1..8 do
               yield 0x33uy
               yield 0x0Fuy |]

    let tile = Tile.decode bytes 0

    let palette =
        Palette.ofColors
            [ Palette.rgb555 31 0 0 // idx 0 -> red
              Palette.rgb555 0 31 0 // idx 1 -> green
              Palette.rgb555 0 0 31 // idx 2 -> blue
              Palette.rgb555 31 31 31 ] // idx 3 -> white

    Graphics.drawTile fb palette 0 0 tile

    let colorAt x y =
        let i = (y * Display.Width + x) * 4
        (fb.Pixels.[i], fb.Pixels.[i + 1], fb.Pixels.[i + 2])

    // Column 0 -> index 0 -> red; column 2 -> index 1 -> green; column 6 -> index 3 -> white.
    Assert.Equal((255uy, 0uy, 0uy), colorAt 0 0)
    Assert.Equal((0uy, 255uy, 0uy), colorAt 2 0)
    Assert.Equal((255uy, 255uy, 255uy), colorAt 6 0)

[<Fact>]
let ``drawTile offsets the tile to the given position`` () =
    let fb = Framebuffer()
    let tile = { Pixels = Array.create Tile.PixelCount 1uy }
    let palette = Palette.ofColors [ Palette.rgb555 0 0 0; Palette.rgb555 31 0 0 ]
    Graphics.drawTile fb palette 16 24 tile
    let i = (24 * Display.Width + 16) * 4
    Assert.Equal(255uy, fb.Pixels.[i]) // red at the drawn corner
    Assert.Equal(0uy, fb.Pixels.[0]) // origin untouched
