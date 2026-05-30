namespace PokeGold.Game.Overworld

open PokeGold.Game.Core
open PokeGold.Game.Data

/// A loaded overworld: the map and its render/collision assets, the player, and
/// the current camera. All immutable; scenes hold one of these and replace it
/// each frame via the pure Overworld systems.
type OverworldState =
    { Map: GameMap
      Tileset: Tileset
      Collision: Collision
      Sprite: Sprite
      MapPalette: Palette
      SpritePalette: Palette
      Player: PlayerState
      CamX: int
      CamY: int }

module OverworldState =

    // DMG-style 4-shade green palette for the map's tile indices 0..3.
    let private mapPalette =
        Palette.ofColors
            [ Palette.rgb555 30 31 26
              Palette.rgb555 17 24 14
              Palette.rgb555 6 13 10
              Palette.rgb555 1 4 3 ]

    // Sprite palette: index 0 is transparent (skipped at draw time); 1..3 are a
    // light→dark grayscale so the player reads clearly against the green map.
    let private spritePalette =
        Palette.ofColors
            [ Palette.rgb555 0 0 0
              Palette.rgb555 31 31 31
              Palette.rgb555 13 13 15
              Palette.rgb555 2 2 3 ]

    /// Build an overworld for an already-loaded map/tileset/collision/sprite,
    /// placing the player on the first walkable cell from the map center.
    let create (map: GameMap) (tileset: Tileset) (coll: Collision) (sprite: Sprite) : OverworldState =
        let sx, sy = Movement.findStartCell map coll
        let player = Player.create sx sy
        let camX, camY = Camera.follow map player

        { Map = map
          Tileset = tileset
          Collision = coll
          Sprite = sprite
          MapPalette = mapPalette
          SpritePalette = spritePalette
          Player = player
          CamX = camX
          CamY = camY }

    /// Load the Azalea Town overworld through the shared asset cache.
    let loadAzalea (content: Content) : OverworldState =
        create
            (content.Map(20, 9, "maps/AzaleaTown.blk"))
            (content.Tileset "johto_modern")
            (content.Collision "johto_modern")
            (content.Sprite "chris")

    /// Advance the overworld by one frame of input (movement + camera follow).
    let tick (buttons: Buttons) (s: OverworldState) : OverworldState =
        let player = Movement.step s.Map s.Collision buttons s.Player
        let camX, camY = Camera.follow s.Map player
        { s with Player = player; CamX = camX; CamY = camY }
