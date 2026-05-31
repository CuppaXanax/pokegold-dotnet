namespace PokeGold.Game.Overworld

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld.Script

/// A loaded overworld: the map and its render/collision assets, the player, and
/// the current camera. All immutable; scenes hold one of these and replace it
/// each frame via the pure Overworld systems.
type OverworldState =
    { /// Stable identifier of the loaded map, used to rebuild it on load.
      MapId: string
      Map: GameMap
      Tileset: Tileset
      Collision: Collision
      Sprite: Sprite
      MapPalette: Palette
      SpritePalette: Palette
      Player: PlayerState
      CamX: int
      CamY: int
      /// The map's parsed event tables (warps, coord triggers, signs, objects).
      Events: MapEvents
      /// The map's parsed script program (labels → commands).
      Script: ScriptProgram
      /// The map's text labels resolved to M5 token strings.
      Text: Map<string, string> }

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

    /// The map id → its `.asm` event-table file, when wired up. Adding a map's
    /// events means adding a case here (mirrors `loadAssets`).
    let private eventsPath (mapId: string) : string option =
        match mapId with
        | "AzaleaTown" -> Some "maps/AzaleaTown.asm"
        | _ -> None

    /// Parse a map's event tables, or empty if the map isn't wired up.
    let private eventsFor (mapId: string) : MapEvents =
        match eventsPath mapId with
        | Some path -> MapEventParser.parseFile path
        | None -> MapEvents.empty

    /// Parse a map's script program, or an empty program if not wired up.
    let private scriptFor (mapId: string) : ScriptProgram =
        match eventsPath mapId with
        | Some path -> ScriptParser.parseFile path
        | None -> { Commands = [||]; Labels = Map.empty }

    /// Parse a map's text labels, or an empty table if not wired up.
    let private textFor (mapId: string) : Map<string, string> =
        match eventsPath mapId with
        | Some path -> MapText.parseFile path
        | None -> Map.empty

    /// Build an overworld around an already-loaded map, placing the player with
    /// the given placement function (start cell vs a saved cell/facing).
    let private build (mapId: string) (map: GameMap) (tileset: Tileset) (coll: Collision) (sprite: Sprite) (player: PlayerState) : OverworldState =
        let camX, camY = Camera.follow map player

        { MapId = mapId
          Map = map
          Tileset = tileset
          Collision = coll
          Sprite = sprite
          MapPalette = mapPalette
          SpritePalette = spritePalette
          Player = player
          CamX = camX
          CamY = camY
          Events = eventsFor mapId
          Script = scriptFor mapId
          Text = textFor mapId }

    /// Build an overworld for an already-loaded map/tileset/collision/sprite,
    /// placing the player on the first walkable cell from the map center.
    let create (mapId: string) (map: GameMap) (tileset: Tileset) (coll: Collision) (sprite: Sprite) : OverworldState =
        let sx, sy = Movement.findStartCell map coll
        build mapId map tileset coll sprite (Player.create sx sy)

    /// Build an overworld for an already-loaded map, placing the player at an
    /// explicit cell and facing (used to restore a saved position).
    let createAt (mapId: string) (map: GameMap) (tileset: Tileset) (coll: Collision) (sprite: Sprite) (cellX: int) (cellY: int) (facing: Direction) : OverworldState =
        build mapId map tileset coll sprite (Player.createFacing cellX cellY facing)

    /// Asset spec for a known map id: the loaders needed to (re)build it. Adding a
    /// new map means adding a case here; save/load and the scene both go through it.
    let private loadAssets (content: Content) (mapId: string) : GameMap * Tileset * Collision * Sprite =
        match mapId with
        | "AzaleaTown" ->
            content.Map(20, 9, "maps/AzaleaTown.blk"),
            content.Tileset "johto_modern",
            content.Collision "johto_modern",
            content.Sprite "chris"
        | other -> failwithf "Unknown map id '%s'" other

    /// Load a known map by id, placing the player on its first walkable cell.
    let loadById (content: Content) (mapId: string) : OverworldState =
        let map, tileset, coll, sprite = loadAssets content mapId
        create mapId map tileset coll sprite

    /// Load a known map by id, placing the player at an explicit cell and facing
    /// (used to restore a saved position).
    let loadByIdAt (content: Content) (mapId: string) (cellX: int) (cellY: int) (facing: Direction) : OverworldState =
        let map, tileset, coll, sprite = loadAssets content mapId
        createAt mapId map tileset coll sprite cellX cellY facing

    /// Load the Azalea Town overworld through the shared asset cache.
    let loadAzalea (content: Content) : OverworldState = loadById content "AzaleaTown"

    /// Advance the overworld by one frame of input (movement + camera follow).
    let tick (buttons: Buttons) (s: OverworldState) : OverworldState =
        let player = Movement.step s.Map s.Collision buttons s.Player
        let camX, camY = Camera.follow s.Map player
        { s with Player = player; CamX = camX; CamY = camY }
