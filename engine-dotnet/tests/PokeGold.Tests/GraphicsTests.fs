module PokeGold.Tests.GraphicsTests

open Xunit
open PokeGold.Game.Core

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
