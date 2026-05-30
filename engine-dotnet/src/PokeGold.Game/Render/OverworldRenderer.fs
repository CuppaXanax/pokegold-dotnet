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
        MapRenderer.draw fb s.MapPalette s.Tileset s.Map s.CamX s.CamY

        let frame, hflip = Animation.frameAndFlip s.Player
        SpriteRenderer.draw fb s.SpritePalette s.Sprite frame (px - s.CamX) (py - s.CamY) hflip
