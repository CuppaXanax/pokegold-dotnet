namespace PokeGold.Game.Data

open PokeGold.Game.Core

/// Walkability derived directly from the disassembly's collision data.
///
/// The model has three source layers, all parsed from the repo:
///   1. `constants/collision_constants.asm` — `DEF NAME EQU <expr>` defines the
///      `COLL_*` ids plus `LAND_TILE`/`WATER_TILE`/`WALL_TILE`/`TALK`.
///   2. `data/collision/collision_permissions.asm` — a 256-entry table mapping
///      each `COLL_*` id to a permission (land / water / wall, optionally `| TALK`).
///   3. `data/tilesets/<name>_collision.asm` — `tilecoll TL, TR, BL, BR` per block,
///      naming a `COLL_*` id for each of the block's four 16×16 quadrants.
///
/// A block is 32×32 px = a 2×2 grid of 16×16 collision cells (quadrants). The
/// player walks on that 16-px cell grid; a cell is walkable on foot iff its
/// permission (with the `TALK` bit masked off) is `LAND_TILE`.
type Collision =
    { /// Per block: 4 COLL ids in quadrant order TL, TR, BL, BR.
      BlockColl: byte[][]
      /// 256 entries: COLL id → permission byte.
      Permissions: byte[]
      /// Resolved base permission ids.
      Land: byte
      Water: byte
      Wall: byte }

module Collision =

    [<Literal>]
    let CellSize = 16

    /// High nybble that marks a ledge (`HI_NYBBLE_LEDGES`, collision_constants.asm).
    [<Literal>]
    let LedgeHiNybble = 0xA0

    [<Literal>]
    let Ice = 0x23uy

    [<Literal>]
    let Ice2B = 0x2Buy

    /// Load the collision model for a named tileset (e.g. "johto_modern").
    let loadNamed (name: string) : Collision =
        let collisionBytes =
            CollisionData.tilesets
            |> Map.tryFind name
            |> Option.defaultValue [||]

        { BlockColl =
              collisionBytes
              |> Array.chunkBySize 4
              |> Array.map (fun block -> block |> Array.toList |> List.toArray)
          Permissions = CollisionData.permissions
          Land = CollisionData.landTile
          Water = CollisionData.waterTile
          Wall = CollisionData.wallTile }

    /// The permission byte (TALK bit masked off) for a block quadrant.
    /// `qx`/`qy` are 0/1 selecting the left/right and top/bottom 16×16 cell.
    let permissionAt (coll: Collision) (blockId: int) (qx: int) (qy: int) : byte =
        if blockId < 0 || blockId >= coll.BlockColl.Length then coll.Wall
        else
            let quadrant = qy * 2 + qx
            let collId = int coll.BlockColl.[blockId].[quadrant]
            (coll.Permissions.[collId]) &&& 0x0Fuy

    /// Whether a block quadrant can be walked on foot (land, not water/wall).
    let isWalkable (coll: Collision) (blockId: int) (qx: int) (qy: int) : bool =
        permissionAt coll blockId qx qy = coll.Land

    /// The raw `COLL_*` id for a block quadrant (no TALK masking, no permission
    /// lookup) — needed to inspect special-behavior tiles such as ledges. An
    /// out-of-range block reads as `COLL_FLOOR` (0), which is simply "not special".
    let collisionIdAt (coll: Collision) (blockId: int) (qx: int) (qy: int) : byte =
        if blockId < 0 || blockId >= coll.BlockColl.Length then 0uy
        else coll.BlockColl.[blockId].[qy * 2 + qx]

    let isIceId (collId: byte) : bool =
        collId = Ice || collId = Ice2B

    /// If `collId` is a ledge tile, the facings from which a hop over it is
    /// allowed (one cardinal facing, or two for the diagonal ledge ids); `None`
    /// for any non-ledge tile. Mirrors `player_movement.asm`'s `ledge_table`:
    /// low nybble 0→R 1→L 2→U 3→D 4→{R,D} 5→{D,L} 6→{U,R} 7→{U,L}.
    let tryLedge (collId: byte) : Direction list option =
        if (int collId &&& 0xF0) <> LedgeHiNybble then None
        else
            match int collId &&& 0x07 with
            | 0 -> Some [ Right ]
            | 1 -> Some [ Left ]
            | 2 -> Some [ Up ]
            | 3 -> Some [ Down ]
            | 4 -> Some [ Right; Down ]
            | 5 -> Some [ Down; Left ]
            | 6 -> Some [ Up; Right ]
            | _ -> Some [ Up; Left ]

    /// Whether a raw `COLL_*` id is a counter/desk tile (`COLL_COUNTER` $90 or the
    /// unused `COLL_COUNTER_98` $98). The player talks *across* a counter to the NPC
    /// behind it, so the action handler reaches one tile further when facing one.
    let isCounterId (collId: byte) : bool =
        collId = 0x90uy || collId = 0x98uy
