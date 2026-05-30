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
