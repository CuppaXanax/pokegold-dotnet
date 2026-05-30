namespace PokeGold.Game.Core

/// Game Boy screen geometry. The game core renders into a framebuffer of this
/// fixed size; the host is responsible for presenting it scaled to the window.
module Display =

    /// Game Boy screen width in pixels.
    [<Literal>]
    let Width = 160

    /// Game Boy screen height in pixels.
    [<Literal>]
    let Height = 144

    /// Number of pixels in one frame.
    [<Literal>]
    let PixelCount = 160 * 144

    /// Game Boy frame rate (Hz). One logic tick per frame.
    [<Literal>]
    let FrameRate = 59.7275

/// A 160x144 framebuffer of 32-bit RGBA pixels (one uint32 per pixel, 0xRRGGBBAA
/// in memory order R,G,B,A). The game core writes pixels here each frame and the
/// host uploads the buffer to a texture for presentation.
type Framebuffer() =
    let pixels = Array.zeroCreate<byte> (Display.PixelCount * 4)

    /// Raw RGBA bytes, length = PixelCount * 4, row-major top-to-bottom.
    member _.Pixels = pixels

    /// Fill the entire buffer with a single RGBA color.
    member _.Clear(r: byte, g: byte, b: byte, a: byte) =
        let mutable i = 0
        while i < pixels.Length do
            pixels.[i] <- r
            pixels.[i + 1] <- g
            pixels.[i + 2] <- b
            pixels.[i + 3] <- a
            i <- i + 4

    /// Set a single pixel at (x, y) to an RGBA color. Out-of-range is ignored.
    member _.SetPixel(x: int, y: int, r: byte, g: byte, b: byte, a: byte) =
        if x >= 0 && x < Display.Width && y >= 0 && y < Display.Height then
            let i = (y * Display.Width + x) * 4
            pixels.[i] <- r
            pixels.[i + 1] <- g
            pixels.[i + 2] <- b
            pixels.[i + 3] <- a
