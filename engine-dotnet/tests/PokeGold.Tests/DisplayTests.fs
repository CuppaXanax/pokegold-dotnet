module PokeGold.Tests.DisplayTests

open Xunit
open PokeGold.Game
open PokeGold.Game.Core

[<Fact>]
let ``framebuffer has 160x144 RGBA pixels`` () =
    let fb = Framebuffer()
    Assert.Equal(Display.PixelCount * 4, fb.Pixels.Length)
    Assert.Equal(160 * 144, fb.Pixels.Length / 4)

[<Fact>]
let ``Clear fills every pixel with the given color`` () =
    let fb = Framebuffer()
    fb.Clear(10uy, 20uy, 30uy, 40uy)
    let p = fb.Pixels
    Assert.Equal(10uy, p.[0])
    Assert.Equal(20uy, p.[1])
    Assert.Equal(30uy, p.[2])
    Assert.Equal(40uy, p.[3])
    Assert.Equal(10uy, p.[p.Length - 4])
    Assert.Equal(40uy, p.[p.Length - 1])

[<Fact>]
let ``SetPixel writes the addressed pixel and ignores out-of-range`` () =
    let fb = Framebuffer()
    fb.SetPixel(2, 1, 1uy, 2uy, 3uy, 4uy)
    let i = (1 * Display.Width + 2) * 4
    Assert.Equal(1uy, fb.Pixels.[i])
    Assert.Equal(4uy, fb.Pixels.[i + 3])
    // Out of range must not throw.
    fb.SetPixel(-1, 0, 9uy, 9uy, 9uy, 9uy)
    fb.SetPixel(0, Display.Height, 9uy, 9uy, 9uy, 9uy)

[<Fact>]
let ``Tick advances the frame counter`` () =
    let core = Game()
    Assert.Equal(0UL, core.Frame)
    core.Tick(Buttons.none)
    core.Tick(Buttons.none)
    Assert.Equal(2UL, core.Frame)
