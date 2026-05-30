namespace PokeGold.Game.Data

/// Runtime access to species base stats. The table itself is baked at build time
/// into `SpeciesData.all` (`Data/Generated/Species.Generated.fs`); this module is
/// just the lookup seam — a future `mods/` overlay would merge here.
module Species =

    /// All species' base stats, keyed by constant name (e.g. "CYNDAQUIL").
    let all : Map<string, BaseStats> = SpeciesData.all

    /// Look up a species' base stats by its constant name (e.g. "CYNDAQUIL").
    let byName (name: string) : BaseStats = all.[name]
