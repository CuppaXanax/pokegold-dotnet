namespace PokeGold.Game.Player

/// Pokédex completion check — the project's Definition of Done.
module DexCompletion =

    /// Total number of species in Gen 2.
    [<Literal>]
    let TotalSpecies = 251

    /// D9 built-in unlocks for event-only species that cannot be obtained from
    /// cartridge-local Gold encounter data.
    let builtInEventUnlockSpecies : Set<string> =
        Set.ofList [ "CELEBI"; "MEW" ]

    let isBuiltInEventUnlock species =
        Set.contains species builtInEventUnlockSpecies

    /// Check if the player has completed the Pokédex (all 251 species owned).
    let isComplete (dexOwn: Set<int>) : bool =
        dexOwn.Count >= TotalSpecies

    /// How many species remain to complete the dex.
    let remaining (dexOwn: Set<int>) : int =
        max 0 (TotalSpecies - dexOwn.Count)

    /// Percentage complete.
    let percentage (dexOwn: Set<int>) : int =
        dexOwn.Count * 100 / TotalSpecies
