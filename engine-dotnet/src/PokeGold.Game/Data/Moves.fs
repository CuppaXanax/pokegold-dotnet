namespace PokeGold.Game.Data

/// Runtime access to move data. The table itself is baked at build time into
/// `MovesData.all` (`Data/Generated/Moves.Generated.fs`); this module is just the
/// lookup seam — a future `mods/` overlay would merge here.
module Moves =

    /// All moves' data, keyed by constant name (e.g. "TACKLE").
    let all : Map<string, MoveData> = MovesData.all

    /// Look up a move's data by its constant name (e.g. "TACKLE").
    let byName (name: string) : MoveData = all.[name]
