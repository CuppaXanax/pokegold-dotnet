namespace PokeGold.Game

/// Drawing primitives that compose decoded tiles onto the framebuffer.
module Graphics =

    /// Draw an 8×8 tile into `fb` with its top-left at pixel (x, y), looking up
    /// each pixel's index in `palette`. Pixels outside the framebuffer or indices
    /// outside the palette are skipped.
    let drawTile (fb: Framebuffer) (palette: Palette) (x: int) (y: int) (tile: Tile) =
        for row in 0..7 do
            for col in 0..7 do
                let idx = int tile.Pixels.[row * 8 + col]

                if idx < palette.Colors.Length then
                    let c = palette.Colors.[idx]
                    fb.SetPixel(x + col, y + row, c.R, c.G, c.B, c.A)
