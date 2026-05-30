namespace PokeGold.Game

open System
open System.Text.RegularExpressions

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

    /// Evaluate a tiny RGBDS constant expression supporting `|`, `<<`, hex
    /// (`$xx`/`0xxx`), decimal, and previously-defined names.
    let private evalExpr (consts: Map<string, int>) (expr: string) : int =
        let operand (tok: string) : int =
            let t = tok.Trim()

            if t.Length = 0 then 0
            elif t.StartsWith "$" then Convert.ToInt32(t.Substring 1, 16)
            elif t.StartsWith "0x" || t.StartsWith "0X" then Convert.ToInt32(t.Substring 2, 16)
            else
                match consts.TryFind t with
                | Some v -> v
                | None ->
                    match Int32.TryParse t with
                    | true, v -> v
                    | _ -> 0

        // OR has lowest precedence; each term may contain a left-shift.
        expr.Split('|')
        |> Array.fold
            (fun acc term ->
                let v =
                    term.Split([| "<<" |], StringSplitOptions.None)
                    |> Array.map operand
                    |> Array.reduce (fun a b -> a <<< b)

                acc ||| v)
            0

    /// Parse all `DEF NAME EQU <expr>` lines into a name→value map, evaluating
    /// expressions against names already defined above them.
    let private parseConstants (text: string) : Map<string, int> =
        let rx = Regex(@"^\s*DEF\s+(\w+)\s+EQU\s+(.+?)\s*$", RegexOptions.IgnoreCase)

        text.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.fold
            (fun consts raw ->
                let line =
                    let i = raw.IndexOf ';'
                    if i >= 0 then raw.Substring(0, i) else raw

                let m = rx.Match line

                if m.Success then
                    let name = m.Groups.[1].Value
                    let value = evalExpr consts m.Groups.[2].Value
                    Map.add name value consts
                else
                    consts)
            Map.empty

    /// Parse the 256-entry CollisionPermissionTable (`db <expr>` rows).
    let private parsePermissions (consts: Map<string, int>) (text: string) : byte[] =
        text.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.choose (fun raw ->
            let line =
                let i = raw.IndexOf ';'
                if i >= 0 then raw.Substring(0, i) else raw

            let t = line.Trim()

            if t.StartsWith "db " then Some(byte (evalExpr consts (t.Substring 3)))
            else None)

    /// Parse `tilecoll TL, TR, BL, BR` rows into per-block COLL ids.
    let private parseTilesetCollision (consts: Map<string, int>) (text: string) : byte[][] =
        text.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.choose (fun raw ->
            let line =
                let i = raw.IndexOf ';'
                if i >= 0 then raw.Substring(0, i) else raw

            let t = line.Trim()

            if t.StartsWith "tilecoll" then
                let ids =
                    t.Substring(8).Split(',')
                    |> Array.map (fun s ->
                        let name = "COLL_" + s.Trim()

                        match consts.TryFind name with
                        | Some v -> byte v
                        | None -> 0uy)

                if ids.Length = 4 then Some ids else None
            else
                None)

    /// Load the collision model for a named tileset (e.g. "johto_modern").
    let loadNamed (name: string) : Collision =
        let consts =
            parseConstants (Assets.readText "constants/collision_constants.asm")

        let permissions =
            parsePermissions consts (Assets.readText "data/collision/collision_permissions.asm")

        let blockColl =
            parseTilesetCollision consts (Assets.readText $"data/tilesets/{name}_collision.asm")

        let lookup k = consts |> Map.tryFind k |> Option.defaultValue 0 |> byte

        { BlockColl = blockColl
          Permissions = permissions
          Land = lookup "LAND_TILE"
          Water = lookup "WATER_TILE"
          Wall = lookup "WALL_TILE" }

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
