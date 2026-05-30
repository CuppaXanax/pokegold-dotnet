namespace PokeGold.Game.Render

open PokeGold.Game.Core
open PokeGold.Game.Data

/// Renders a map onto the framebuffer by expanding each block into its 4×4 tiles
/// and drawing them through a palette. A camera offset (in pixels) selects which
/// part of the map is visible.
module MapRenderer =

    [<Literal>]
    let TilesPerBlockSide = 4

    /// Pixel size of one block (4 tiles × 8 px).
    [<Literal>]
    let BlockPixels = 32

    /// Draw `map` (built from `tileset`) into `fb`, with the top-left of the
    /// viewport at map pixel (camX, camY). Only blocks intersecting the screen
    /// are visited.
    let draw
        (fb: Framebuffer)
        (palette: Palette)
        (tileset: Tileset)
        (map: GameMap)
        (camX: int)
        (camY: int)
        =
        // Range of blocks visible given the camera and screen size.
        let firstBx = max 0 (camX / BlockPixels)
        let firstBy = max 0 (camY / BlockPixels)
        let lastBx = min (map.Width - 1) ((camX + Display.Width) / BlockPixels)
        let lastBy = min (map.Height - 1) ((camY + Display.Height) / BlockPixels)

        for by in firstBy..lastBy do
            for bx in firstBx..lastBx do
                let blockId = int (Map.blockAt map bx by)

                if blockId < tileset.Blocks.Length then
                    let block = tileset.Blocks.[blockId]

                    for trow in 0 .. TilesPerBlockSide - 1 do
                        for tcol in 0 .. TilesPerBlockSide - 1 do
                            let tileId = int block.TileIds.[trow * TilesPerBlockSide + tcol]

                            if tileId < tileset.Tiles.Length then
                                let px = bx * BlockPixels + tcol * Tile.Size - camX
                                let py = by * BlockPixels + trow * Tile.Size - camY
                                Graphics.drawTile fb palette px py tileset.Tiles.[tileId]
