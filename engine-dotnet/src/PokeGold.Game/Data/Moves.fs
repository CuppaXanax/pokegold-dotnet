namespace PokeGold.Game.Data

/// Runtime access to move data. The table itself is baked at build time into
/// `MovesData.all` (`Data/Generated/Moves.Generated.fs`); this module is just the
/// lookup seam — a future `mods/` overlay would merge here.
module Moves =

    /// All moves' data, keyed by constant name (e.g. "TACKLE").
    let all : Map<string, MoveData> = MovesData.all

    /// Look up a move's data by its constant name (e.g. "TACKLE").
    let byName (name: string) : MoveData = all.[name]

    /// Look up a move by its 1-based GSC numeric constant (0 = NO_MOVE, 1 = POUND, …).
    /// Returns None for out-of-range or zero IDs.
    let tryByIndex (id: int) : MoveData option =
        if id > 0 && id < MovesData.byIndex.Length then
            let m = MovesData.byIndex.[id]
            if m.Name = "" then None else Some m
        else
            None
