namespace PokeGold.Game.Data

/// Runtime access to baked Pokédex metadata.
module Dex =
    let all : DexEntry[] = DexData.all
    let byNum : Map<int, DexEntry> = DexData.byNum
