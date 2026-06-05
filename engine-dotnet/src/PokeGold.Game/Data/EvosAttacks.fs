namespace PokeGold.Game.Data

/// Runtime access to evolution and learnset data. The table itself is baked at
/// build time into `EvosAttacksData.all` (`Data/Generated/EvosAttacks.Generated.fs`).
module EvosAttacksAccess =

    let all : Map<string, EvosAttacks> = EvosAttacksData.all

    let forSpecies (name: string) : EvosAttacks option = Map.tryFind name all
