namespace PokeGold.Game.Data

open PokeGold.Game.Core

/// An overworld sprite: a stack of 16×16 frames decoded from a pret sprite PNG
/// (16 px wide). A full walking sprite is 96 px tall (six frames):
///   0 stand-down  1 stand-up  2 stand-side
///   3 walk-down   4 walk-up   5 walk-side
/// "Right" is drawn by horizontally flipping the "side" (left-facing) frames.
/// "Still" sprites (a clerk/nurse who only ever stands, e.g. behind a counter)
/// ship just the three standing frames (48 px tall); the frame count is taken
/// from the PNG, so they no longer fail to load.
type Sprite =
    { /// Each frame is a 16×16 grid of 2-bit indices (256 entries), row-major.
      Frames: byte[][] }

module Sprite =

    [<Literal>]
    let Size = 16

    /// Frames in a full walking sprite (the player and most NPCs).
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

    /// Build the sprite's frames from its decoded tiles (4 tiles per 16×16 frame).
    /// The frame count is derived from the tile count, so both full six-frame
    /// walking sprites and shorter "still" sprites (e.g. a three-frame nurse) load.
    let ofTiles (tiles: Tile[]) : Sprite =
        { Frames = Array.init (tiles.Length / 4) (composeFrame tiles) }

    /// Load a named pret overworld sprite (gfx/sprites/<name>.png).
    let loadNamed (name: string) : Sprite =
        ofTiles (Image.loadTiles $"gfx/sprites/{name}.png")
