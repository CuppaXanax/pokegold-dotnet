namespace PokeGold.Game.Render

open PokeGold.Game.Core
open PokeGold.Game.Overworld

/// Composes the overworld scene into the framebuffer: the map first, then the
/// player sprite positioned by the camera.
module OverworldRenderer =

    /// Render an overworld state into `fb`.
    let draw (fb: Framebuffer) (s: OverworldState) =
        let px, py = Player.worldPixel s.Player
        fb.Clear(0uy, 0uy, 0uy, 255uy)
        MapRenderer.draw fb s.MapPalettes s.Tileset s.Map s.CamX s.CamY

        // Connected neighbour maps fill the screen beyond the current map's edges.
        // Each neighbour's block (0,0) sits at current cell (BaseCx, BaseCy), i.e.
        // pixel (BaseCx*16, BaseCy*16), so shifting the camera by that offset places
        // it correctly. Neighbours lie wholly outside the current map, so they never
        // overdraw it.
        for n in s.Neighbors do
            let camX = s.CamX - n.Placement.BaseCx * 16
            let camY = s.CamY - n.Placement.BaseCy * 16
            MapRenderer.draw fb s.MapPalettes n.Tileset n.Map camX camY

        let frame, hflip = Animation.frameAndFlip s.Player
        SpriteRenderer.draw fb s.SpritePalette s.Sprite frame (px - s.CamX) (py - s.CamY) hflip
