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
      Text: Map<string, string>
      /// Loaded, placed neighbour maps for border rendering, cross-join collision
      /// and walking off the edge into the next map. Empty until populated by a
      /// content-aware load (`withNeighbors`); a bare `build`/`createAt` has none.
      Neighbors: MapConnections.NeighborMap list
      /// The map's live overworld objects (NPCs, signs-as-objects, etc.), one per
      /// visible-or-not object event, advanced each frame by the `ObjectStep`
      /// system. Autonomous ones wander; the rest hold their pose.
      Npcs: NpcObject[] }

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
        let events = eventsFor mapId

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
          Events = events
          Script = scriptFor mapId
          Text = textFor mapId
          Neighbors = []
          Npcs = events.Objects |> Array.mapi NpcObject.fromEvent }

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

    /// A neighbour map's render/collision assets, if it is loadable (`.blk` + gfx +
    /// collision present); `None` for interiors whose assets aren't in the tree yet.
    let private neighborAssets (content: Content) (name: string) : (GameMap * Tileset * Collision) option =
        match dataFor name with
        | Some m when canLoad m.Meta ->
            let stem = tilesetStem m.Meta
            Some(
                content.Map(m.Meta.WidthBlocks, m.Meta.HeightBlocks, $"maps/{name}.blk"),
                content.Tileset stem,
                content.Collision stem
            )
        | _ -> None

    /// Load and place every loadable connected neighbour of `s`'s map, deriving each
    /// one's cell-frame placement from its `connection` offset and block dimensions.
    let withNeighbors (content: Content) (s: OverworldState) : OverworldState =
        let cw, ch = s.Map.Width * 2, s.Map.Height * 2

        let neighbors =
            match dataFor s.MapId with
            | Some m ->
                [ for c in m.Meta.Connections do
                      match dataFor c.Map with
                      | Some nm ->
                          match neighborAssets content c.Map with
                          | Some(map, tileset, coll) ->
                              { MapConnections.Placement =
                                  MapConnections.placement cw ch nm.Meta.WidthBlocks nm.Meta.HeightBlocks c
                                MapConnections.Map = map
                                MapConnections.Tileset = tileset
                                MapConnections.Collision = coll }
                          | None -> ()
                      | None -> () ]
            | None -> []

        { s with Neighbors = neighbors }

    /// Load a known map by id, placing the player on its first walkable cell.
    let loadById (content: Content) (mapId: string) : OverworldState =
        let map, tileset, coll, sprite = loadAssets content mapId
        create mapId map tileset coll sprite |> withNeighbors content

    /// Load a known map by id, placing the player at an explicit cell and facing
    /// (used to restore a saved position).
    let loadByIdAt (content: Content) (mapId: string) (cellX: int) (cellY: int) (facing: Direction) : OverworldState =
        let map, tileset, coll, sprite = loadAssets content mapId
        createAt mapId map tileset coll sprite cellX cellY facing |> withNeighbors content

    /// Load the Azalea Town overworld through the shared asset cache.
    let loadAzalea (content: Content) : OverworldState = loadById content "AzaleaTown"

    /// Advance the overworld by one frame of input (movement + camera follow). The
    /// cell queries consult connected neighbours, so the player walks, hops and is
    /// blocked seamlessly across map joins; the camera tracks into neighbour terrain.
    let tick (buttons: Buttons) (s: OverworldState) : OverworldState =
        let walkable = MapConnections.cellWalkable s.Map s.Collision s.Neighbors
        let collId = MapConnections.collisionId s.Map s.Collision s.Neighbors

        // Live NPCs are solid: the player can't walk onto a cell an object holds
        // (or is stepping out of). Ledge hops still don't re-validate the landing,
        // matching GSC — an NPC on a ledge-landing cell won't stop a jump.
        let npcCells = ObjectStep.occupiedCells s.Npcs
        let playerWalkable cx cy = walkable cx cy && not (Set.contains (struct (cx, cy)) npcCells)

        let player = Movement.stepWith playerWalkable collId buttons s.Player
        let camX, camY = Camera.followExt s.Map s.Neighbors player

        // The player is solid too: objects won't step onto the player's cell (or the
        // cell it's mid-step between). Use the post-step position so an NPC reacts to
        // where the player actually is this frame.
        let playerBlocked =
            seq {
                yield struct (player.CellX, player.CellY)

                if player.Motion <> Standing then
                    yield struct (player.SrcX, player.SrcY)
            }

        let npcs = ObjectStep.stepAllBlocked walkable playerBlocked s.Npcs

        { s with
            Player = player
            CamX = camX
            CamY = camY
            Npcs = npcs }

    /// If the player has walked off the current map into a connected neighbour,
    /// rebuild the overworld as that neighbour with the player rebased to the
    /// equivalent local cell (same world position, so the camera stays put). The
    /// caller resets any per-visit state (e.g. fired coord triggers). `None` while
    /// the player is still inside the current map's bounds or mid-step.
    let crossConnection (content: Content) (s: OverworldState) : OverworldState option =
        let cw, ch = s.Map.Width * 2, s.Map.Height * 2
        let cx, cy = s.Player.CellX, s.Player.CellY

        if s.Player.Motion <> Standing || (cx >= 0 && cy >= 0 && cx < cw && cy < ch) then
            None
        else
            MapConnections.resolve s.Neighbors cx cy
            |> Option.map (fun (n, lx, ly) -> loadByIdAt content n.Placement.Conn.Map lx ly s.Player.Facing)

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

    /// Resolve an explicit script warp (`warp`/`warpfacing MAP, x, y`): load the
    /// destination map and place the player at the given cell, facing the script's
    /// direction (or keeping `fallback` when the command gives none). `None` if the
    /// destination map is unknown or its assets aren't in the tree yet.
    let tryWarpExplicit
        (content: Content)
        (destMap: string)
        (x: int)
        (y: int)
        (facing: string option)
        (fallback: Direction)
        : OverworldState option =
        let dir =
            match facing with
            | Some s ->
                let u = s.ToUpperInvariant()
                if u.Contains "UP" then Up
                elif u.Contains "LEFT" then Left
                elif u.Contains "RIGHT" then Right
                elif u.Contains "DOWN" then Down
                else fallback
            | None -> fallback

        match mapIdOfConst destMap |> Option.bind dataFor with
        | Some m when canLoad m.Meta -> Some(loadByIdAt content m.Meta.Name x y dir)
        | _ -> None

    /// The named `applymovement` script for a map, if the map bakes it.
    let movementScript (mapId: string) (label: string) : MovementCmd[] option =
        dataFor mapId |> Option.bind (fun m -> Map.tryFind label m.Movements)

    /// The object index a script's symbolic actor operand (`AZALEATOWN_RIVAL`, …)
    /// refers to: its position in the map's `object_const_def` order, which is the
    /// same as its index in `Events.Objects` / `Npcs`. `None` if the name is unknown.
    let objectIndexOf (mapId: string) (objConst: string) : int option =
        dataFor mapId
        |> Option.bind (fun m -> Array.tryFindIndex ((=) objConst) m.ObjectConsts)
