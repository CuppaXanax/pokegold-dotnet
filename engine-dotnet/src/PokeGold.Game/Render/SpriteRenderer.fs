namespace PokeGold.Game.Render

open PokeGold.Game.Core
open PokeGold.Game.Data

/// Draws decoded sprite frames onto the framebuffer.
module SpriteRenderer =

    /// Draw a frame into `fb` with its top-left at (x, y), looking colors up in
    /// `palette`. Index 0 is transparent (skipped). When `hflip` is set the frame
    /// is mirrored horizontally, which turns the left-facing frames into
    /// right-facing ones.
    let draw (fb: Framebuffer) (palette: Palette) (sprite: Sprite) (frame: int) (x: int) (y: int) (hflip: bool) =
        let px = sprite.Frames.[frame]

        for r in 0..15 do
            for c in 0..15 do
                let sc = if hflip then 15 - c else c
                let idx = int px.[r * Sprite.Size + sc]

                if idx <> 0 && idx < palette.Colors.Length then
                    let col = palette.Colors.[idx]
                    fb.SetPixel(x + c, y + r, col.R, col.G, col.B, col.A)
