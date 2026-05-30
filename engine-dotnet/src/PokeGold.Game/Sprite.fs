namespace PokeGold.Game

/// The player's overworld sprite: six 16×16 frames decoded from a pret sprite
/// PNG (16 px wide, 96 px tall). The frames, top to bottom, are:
///   0 stand-down  1 stand-up  2 stand-side
///   3 walk-down   4 walk-up   5 walk-side
/// "Right" is drawn by horizontally flipping the "side" (left-facing) frames.
type Sprite =
    { /// Each frame is a 16×16 grid of 2-bit indices (256 entries), row-major.
      Frames: byte[][] }

module Sprite =

    [<Literal>]
    let Size = 16

    [<Literal>]
    let FrameCount = 6

    /// Assemble a 16×16 frame from four 8×8 tiles laid out TL, TR, BL, BR.
    let private composeFrame (tiles: Tile[]) (frame: int) : byte[] =
        let px = Array.zeroCreate<byte> (Size * Size)
        let tl = tiles.[frame * 4]
        let tr = tiles.[frame * 4 + 1]
        let bl = tiles.[frame * 4 + 2]
        let br = tiles.[frame * 4 + 3]

        for r in 0..15 do
            for c in 0..15 do
                let tile, tr', tc =
                    match r < 8, c < 8 with
                    | true, true -> tl, r, c
                    | true, false -> tr, r, c - 8
                    | false, true -> bl, r - 8, c
                    | false, false -> br, r - 8, c - 8

                px.[r * Size + c] <- tile.Pixels.[tr' * 8 + tc]

        px

    /// Build the sprite's frames from its decoded tiles (24 = 6 frames × 4 tiles).
    let ofTiles (tiles: Tile[]) : Sprite =
        { Frames = Array.init FrameCount (composeFrame tiles) }

    /// Load a named pret overworld sprite (gfx/sprites/<name>.png).
    let loadNamed (name: string) : Sprite =
        ofTiles (Image.loadTiles $"gfx/sprites/{name}.png")

    /// Draw a frame into `fb` with its top-left at (x, y), looking colors up in
    /// `palette`. Index 0 is transparent (skipped). When `hflip` is set the frame
    /// is mirrored horizontally, which turns the left-facing frames into
    /// right-facing ones.
    let draw (fb: Framebuffer) (palette: Palette) (sprite: Sprite) (frame: int) (x: int) (y: int) (hflip: bool) =
        let px = sprite.Frames.[frame]

        for r in 0..15 do
            for c in 0..15 do
                let sc = if hflip then 15 - c else c
                let idx = int px.[r * Size + sc]

                if idx <> 0 && idx < palette.Colors.Length then
                    let col = palette.Colors.[idx]
                    fb.SetPixel(x + c, y + r, col.R, col.G, col.B, col.A)
