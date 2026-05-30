namespace PokeGold.Game.Data

open PokeGold.Game.Core

/// A metatile ("block"): a 4×4 arrangement of tile ids, the unit a map is built
/// from. A block is 32×32 pixels (4 tiles × 8 px).
type Block =
    { /// 16 tile ids, row-major (4 wide × 4 tall).
      TileIds: byte[] }

/// A map tileset: the decoded tile graphics plus the block definitions that
/// arrange those tiles into 32×32 metatiles.
type Tileset =
    { /// Decoded 8×8 index tiles (from the tileset PNG).
      Tiles: Tile[]
      /// Block (metatile) definitions.
      Blocks: Block[] }

module Tileset =

    [<Literal>]
    let TilesPerBlock = 16

    [<Literal>]
    let BlockSize = 4 // tiles per side

    /// Parse metatile bytes (16 tile ids per block) into blocks.
    let parseBlocks (metatiles: byte[]) : Block[] =
        let count = metatiles.Length / TilesPerBlock

        Array.init count (fun i ->
            { TileIds = Array.sub metatiles (i * TilesPerBlock) TilesPerBlock })

    /// Load a tileset from a tileset PNG and a metatiles `.bin`, both repo-relative.
    let load (pngRelative: string) (metatilesRelative: string) : Tileset =
        { Tiles = Image.loadTiles pngRelative
          Blocks = parseBlocks (Assets.readBytes metatilesRelative) }

    /// Load a named pret tileset by convention:
    ///   gfx/tilesets/<name>.png  +  data/tilesets/<name>_metatiles.bin
    let loadNamed (name: string) : Tileset =
        load $"gfx/tilesets/{name}.png" $"data/tilesets/{name}_metatiles.bin"
