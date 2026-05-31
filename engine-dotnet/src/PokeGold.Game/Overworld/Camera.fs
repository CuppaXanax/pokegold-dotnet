namespace PokeGold.Game.Overworld

open PokeGold.Game.Core
open PokeGold.Game.Data

/// The camera system: a pure function that centers the viewport on the player,
/// clamped to the map bounds.
module Camera =

    /// Pixel size of one 32-px block (4 tiles × 8 px).
    let private blockPixels = Tileset.BlockSize * Tile.Size

    /// Camera top-left (in map pixels) that centers the player on screen, clamped
    /// so the viewport never leaves the map.
    let follow (map: GameMap) (p: PlayerState) : int * int =
        let mapPixelW = map.Width * blockPixels
        let mapPixelH = map.Height * blockPixels
        let maxCamX = max 0 (mapPixelW - Display.Width)
        let maxCamY = max 0 (mapPixelH - Display.Height)
        let px, py = Player.worldPixel p
        let clamp lo hi v = max lo (min hi v)
        clamp 0 maxCamX (px + 8 - Display.Width / 2), clamp 0 maxCamY (py + 8 - Display.Height / 2)

    /// Pixel size of one 16-px cell.
    let private cellPixels = 16

    /// Like [`follow`], but the clamp bounds are expanded to cover any connected
    /// neighbour maps, so the camera scrolls smoothly across a join and reveals the
    /// neighbour's terrain as the player approaches (and crosses) the shared edge.
    let followExt (map: GameMap) (neighbors: MapConnections.NeighborMap list) (p: PlayerState) : int * int =
        let mapPixelW = map.Width * blockPixels
        let mapPixelH = map.Height * blockPixels

        // Bounding box of the current map unioned with every neighbour's extent.
        let minX, minY, maxX, maxY =
            neighbors
            |> List.fold
                (fun (mnx, mny, mxx, mxy) (n: MapConnections.NeighborMap) ->
                    let x0 = n.Placement.BaseCx * cellPixels
                    let y0 = n.Placement.BaseCy * cellPixels
                    let x1 = x0 + n.Placement.CellW * cellPixels
                    let y1 = y0 + n.Placement.CellH * cellPixels
                    min mnx x0, min mny y0, max mxx x1, max mxy y1)
                (0, 0, mapPixelW, mapPixelH)

        let maxCamX = max minX (maxX - Display.Width)
        let maxCamY = max minY (maxY - Display.Height)
        let px, py = Player.worldPixel p
        let clamp lo hi v = max lo (min hi v)
        clamp minX maxCamX (px + 8 - Display.Width / 2), clamp minY maxCamY (py + 8 - Display.Height / 2)
