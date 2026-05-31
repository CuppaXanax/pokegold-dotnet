namespace PokeGold.Game.Overworld

open PokeGold.Game.Data
open PokeGold.Game.Overworld.Script

/// Map connections: how an adjacent map is positioned relative to the current one,
/// and the "extended" block/collision lookups that let the player see, walk on, and
/// cross into a connected neighbour. All pure — the geometry is the runtime port of
/// the `connection` macro in `data/maps/attributes.asm:32-86`.
///
/// A connection's `Offset` is the neighbour's alignment along the shared edge, in
/// BLOCKS (x for north/south, y for west/east). Working on the 16-px cell grid
/// (2 cells per block), a current-map cell `(cx, cy)` maps to the neighbour-local
/// cell `(cx - BaseCx, cy - BaseCy)`, which is valid only inside the neighbour's
/// `[0, CellW) x [0, CellH)` bounds. The four directions differ only in `Base*`:
///   north → neighbour sits above   (BaseCx = off*2, BaseCy = -nH)
///   south → neighbour sits below   (BaseCx = off*2, BaseCy =  ch)
///   west  → neighbour sits left    (BaseCx = -nW,   BaseCy = off*2)
///   east  → neighbour sits right   (BaseCx =  cw,   BaseCy = off*2)
/// (Derived from the macro's `_src`/`_tgt`/`_blk`/`_map` arithmetic.)
module MapConnections =

    /// A neighbour's placement in the current map's cell frame.
    type Placement =
        { Conn: Connection
          /// Current cell that aligns with the neighbour's local cell (0, 0).
          BaseCx: int
          BaseCy: int
          /// Neighbour size in cells.
          CellW: int
          CellH: int }

    /// A loaded, placed neighbour map — its assets plus where it sits.
    type NeighborMap =
        { Placement: Placement
          Map: GameMap
          Tileset: Tileset
          Collision: Collision }

    /// Place a connection in the current map's cell frame. `cw`/`ch` are the CURRENT
    /// map's cell dimensions; `nWBlocks`/`nHBlocks` the neighbour's block dimensions.
    let placement (cw: int) (ch: int) (nWBlocks: int) (nHBlocks: int) (c: Connection) : Placement =
        let nW = nWBlocks * 2
        let nH = nHBlocks * 2
        let off2 = c.Offset * 2

        let baseCx, baseCy =
            match c.Direction with
            | "north" -> off2, -nH
            | "south" -> off2, ch
            | "west" -> -nW, off2
            | "east" -> cw, off2
            | _ -> System.Int32.MaxValue / 2, System.Int32.MaxValue / 2 // unknown → matches nothing

        { Conn = c
          BaseCx = baseCx
          BaseCy = baseCy
          CellW = nW
          CellH = nH }

    /// The neighbour-local cell for a current-map cell, if it lies in this placement.
    let localCell (p: Placement) (cx: int) (cy: int) : (int * int) option =
        let lx = cx - p.BaseCx
        let ly = cy - p.BaseCy

        if lx >= 0 && ly >= 0 && lx < p.CellW && ly < p.CellH then
            Some(lx, ly)
        else
            None

    /// The first neighbour whose bounds contain the current-map cell, with its local
    /// coordinates.
    let resolve (neighbors: NeighborMap list) (cx: int) (cy: int) : (NeighborMap * int * int) option =
        neighbors
        |> List.tryPick (fun n -> localCell n.Placement cx cy |> Option.map (fun (lx, ly) -> n, lx, ly))

    /// Whether the cell is inside the current map's bounds.
    let inline private inBounds (map: GameMap) (cx: int) (cy: int) : bool =
        cx >= 0 && cy >= 0 && cx < map.Width * 2 && cy < map.Height * 2

    /// Walkability across the join: the current map inside its bounds, otherwise the
    /// neighbour covering the cell, otherwise the border (not walkable).
    let cellWalkable (map: GameMap) (coll: Collision) (neighbors: NeighborMap list) (cx: int) (cy: int) : bool =
        if inBounds map cx cy then
            Movement.cellWalkable map coll cx cy
        else
            match resolve neighbors cx cy with
            | Some(n, lx, ly) -> Movement.cellWalkable n.Map n.Collision lx ly
            | None -> false

    /// Collision id across the join (used for ledge detection); 0 outside any map.
    let collisionId (map: GameMap) (coll: Collision) (neighbors: NeighborMap list) (cx: int) (cy: int) : byte =
        if inBounds map cx cy then
            Movement.collisionIdAtCell map coll cx cy
        else
            match resolve neighbors cx cy with
            | Some(n, lx, ly) -> Movement.collisionIdAtCell n.Map n.Collision lx ly
            | None -> 0uy
