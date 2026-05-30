namespace PokeGold.Game

/// A map: a grid of block ids of a given size. Each block id indexes a
/// `Block` in the map's tileset.
type GameMap =
    { Width: int
      Height: int
      /// `Width * Height` block ids, row-major.
      BlockIds: byte[] }

module Map =

    /// Build a map from explicit dimensions and a `.blk` byte stream (one block
    /// id per byte, row-major). Length must equal `width * height`.
    let ofBlk (width: int) (height: int) (blk: byte[]) : GameMap =
        if blk.Length <> width * height then
            failwithf "blk size %d does not match %dx%d (%d)" blk.Length width height (width * height)

        { Width = width
          Height = height
          BlockIds = blk }

    /// Load a map from a repo-relative `.blk` path with explicit dimensions
    /// (dimensions come from constants/map_constants.asm).
    let load (width: int) (height: int) (blkRelative: string) : GameMap =
        ofBlk width height (Assets.readBytes blkRelative)

    /// The block id at map cell (x, y).
    let blockAt (map: GameMap) (x: int) (y: int) : byte = map.BlockIds.[y * map.Width + x]
