namespace PokeGold.Game.Overworld

open System.IO
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

    /// The build-time-generated static data for a map id, if it exists. Events,
    /// scripts and text now come from the baked `MapsData` table — the overworld
    /// load path no longer parses any `maps/<Name>.asm` at runtime.
    let private dataFor (mapId: string) : GeneratedMap option = MapsData.byName mapId

    /// Parse a map's event tables, or empty if the map isn't in the generated table.
    let private eventsFor (mapId: string) : MapEvents =
        match dataFor mapId with
        | Some m -> m.Events
        | None -> MapEvents.empty

    /// A map's script program, or an empty program if not in the generated table.
    let private scriptFor (mapId: string) : ScriptProgram =
        match dataFor mapId with
        | Some m -> m.Script
        | None -> { Commands = [||]; Labels = Map.empty }

    /// A map's text labels, or an empty table if not in the generated table.
    let private textFor (mapId: string) : Map<string, string> =
        match dataFor mapId with
        | Some m -> m.Text
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

    /// Map a `TILESET_*` constant to its asset stem (e.g. `TILESET_JOHTO_MODERN`
    /// → `johto_modern`), which names both the gfx/metatiles and the collision.
    let private tilesetStem (meta: MapMeta) : string =
        let t = meta.Tileset
        let t = if t.StartsWith "TILESET_" then t.Substring "TILESET_".Length else t
        t.ToLowerInvariant()

    /// Whether a map's binary/asset files are all present, so it can be loaded.
    /// Map geometry/events/text are baked, but the `.blk` block layout, tileset
    /// gfx and collision are still on-disk assets; an interior whose assets aren't
    /// in the tree yet simply isn't loadable (warps onto it are a no-op).
    let private canLoad (meta: MapMeta) : bool =
        let stem = tilesetStem meta
        File.Exists(Assets.path $"maps/{meta.Name}.blk")
        && File.Exists(Assets.path $"gfx/tilesets/{stem}.png")
        && File.Exists(Assets.path $"data/tilesets/{stem}_collision.asm")

    /// Asset spec for a map id: the loaders needed to (re)build it, derived from the
    /// baked metadata (dimensions, tileset). The player overworld sprite is fixed.
    let private loadAssets (content: Content) (mapId: string) : GameMap * Tileset * Collision * Sprite =
        match dataFor mapId with
        | Some m ->
            let stem = tilesetStem m.Meta
            content.Map(m.Meta.WidthBlocks, m.Meta.HeightBlocks, $"maps/{mapId}.blk"),
            content.Tileset stem,
            content.Collision stem,
            content.Sprite "chris"
        | None -> failwithf "Unknown map id '%s'" mapId

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

    /// A destination `MAP_*` constant → its loadable map id, resolved from the baked
    /// metadata's name↔const link (every one of the 368 maps, not just Azalea).
    let private constToName : Map<string, string> =
        MapsData.all
        |> Seq.map (fun kv -> kv.Value.Meta.Const, kv.Value.Meta.Name)
        |> Map.ofSeq

    let private mapIdOfConst (mapConst: string) : string option = Map.tryFind mapConst constToName

    /// Resolve a warp to its destination overworld: load the destination map and
    /// place the player on its `destWarp`-th warp tile (GSC pairs warps by id).
    /// `None` if the destination map is unknown, its assets aren't in the tree yet,
    /// or the warp id is out of range.
    let tryWarp (content: Content) (destMap: string) (destWarp: int) : OverworldState option =
        match mapIdOfConst destMap |> Option.bind dataFor with
        | Some m when canLoad m.Meta ->
            let warps = m.Events.Warps
            if destWarp >= 1 && destWarp <= warps.Length then
                let w = warps.[destWarp - 1]
                Some(loadByIdAt content m.Meta.Name w.X w.Y Down)
            else
                None
        | _ -> None
