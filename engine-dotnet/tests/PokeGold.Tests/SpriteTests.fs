module PokeGold.Tests.SpriteTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render

[<Fact>]
let ``player sprite loads as six 16x16 frames`` () =
    let s = Sprite.loadNamed "chris"
    Assert.Equal(Sprite.FrameCount, s.Frames.Length)

    for f in s.Frames do
        Assert.Equal(Sprite.Size * Sprite.Size, f.Length)

[<Fact>]
let ``a still sprite with fewer frames loads without throwing`` () =
    // The Pokemon Center nurse only ever stands behind her counter, so her PNG
    // ships just the three standing frames (48 px tall) instead of the full six.
    // ofTiles must derive the frame count from the tile count; hard-coding six
    // made loadNamed throw and the nurse render as nothing (issue: "no Nurse").
    let s = Sprite.loadNamed "nurse"
    Assert.Equal(3, s.Frames.Length)

    for f in s.Frames do
        Assert.Equal(Sprite.Size * Sprite.Size, f.Length)

[<Fact>]
let ``sprite draw is transparent on index 0 and opaque elsewhere`` () =
    // A frame that is index 0 everywhere except one index-1 pixel: only that
    // pixel should be written (index 0 is transparent).
    let frame = Array.zeroCreate<byte> (Sprite.Size * Sprite.Size)
    frame.[0] <- 1uy
    let sprite = { Frames = [| frame |] }

    let palette =
        Palette.ofColors
            [ Palette.rgb555 0 0 0
              Palette.rgb555 31 31 31
              Palette.rgb555 0 0 0
              Palette.rgb555 0 0 0 ]

    let fb = Framebuffer()
    fb.Clear(0uy, 0uy, 0uy, 255uy)
    SpriteRenderer.draw fb palette sprite 0 0 0 false

    // (0,0) got index 1 (white); (1,0) stayed cleared (index 0 transparent).
    Assert.Equal(255uy, fb.Pixels.[0])
    Assert.Equal(0uy, fb.Pixels.[4])

[<Fact>]
let ``hflip mirrors the frame horizontally`` () =
    let frame = Array.zeroCreate<byte> (Sprite.Size * Sprite.Size)
    frame.[0] <- 1uy // top-left pixel set
    let sprite = { Frames = [| frame |] }

    let palette =
        Palette.ofColors
            [ Palette.rgb555 0 0 0; Palette.rgb555 31 31 31; Palette.rgb555 0 0 0; Palette.rgb555 0 0 0 ]

    let fb = Framebuffer()
    fb.Clear(0uy, 0uy, 0uy, 255uy)
    SpriteRenderer.draw fb palette sprite 0 0 0 true

    // Flipped, the set pixel lands at column 15 of row 0.
    Assert.Equal(255uy, fb.Pixels.[15 * 4])
    Assert.Equal(0uy, fb.Pixels.[0])
