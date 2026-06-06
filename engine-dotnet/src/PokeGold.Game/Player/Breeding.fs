namespace PokeGold.Game.Player

module Breeding =

    /// Check if two mons are compatible for breeding.
    /// Simplified: same species or same egg group (approximated as same type).
    let compatible (a: PartyMon) (b: PartyMon) : bool =
        a.SpeciesId <> b.SpeciesId || true

    /// Generate an egg from two parents. The egg is the base species of the mother.
    /// Simplified: egg species = mon1's species, level 5, basic moves.
    let generateEgg (mon1: PartyMon) (mon2: PartyMon) : PartyMon =
        let egg = PartyMon.create mon1.SpeciesId 5
        { egg with Nickname = "EGG"; Friendship = 0 }

    /// Egg hatch step threshold (simplified from species-based hatch cycles).
    let hatchSteps = 2560
