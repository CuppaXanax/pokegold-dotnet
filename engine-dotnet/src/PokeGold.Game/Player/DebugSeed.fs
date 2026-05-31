namespace PokeGold.Game.Player

/// Debug-only seeds for headless testing and menu development.
/// NEVER apply this to a real loaded save; only call from debug/dev entry points.
module DebugSeed =

    /// Seed the player state with a small starter party, money, items in all
    /// four pockets, and several Pokédex flags. Safe to call multiple times
    /// (overwrites previous seed). Debug-only — never applied to a real save.
    let seed (player: PlayerState) : PlayerState =
        let cyndaquil = PartyMon.create 155 5   // Cyndaquil L5
        let pidgey = PartyMon.create 16 4        // Pidgey L4
        // Make Pidgey have reduced HP so HP bars vary in the debug view
        let pidgeyWeak = { pidgey with Hp = max 1 (pidgey.MaxHp / 3) }
        // Totodile L5 at full HP
        let totodile = PartyMon.create 158 5

        let bag =
            Bag.empty
            |> Bag.add "POTION" 5
            |> Bag.add "ANTIDOTE" 2
            |> Bag.add "FULL_RESTORE" 1
            |> Bag.add "POKE_BALL" 10
            |> Bag.add "GREAT_BALL" 5
            |> Bag.add "BICYCLE" 1
            |> Bag.add "TOWN_MAP" 1
            |> Bag.add "TM01" 1

        { player with
            Name = "DEBUG"
            Money = 3000
            Party = [ cyndaquil; pidgeyWeak; totodile ]
            Bag = bag
            DexSeen = Set.ofList [ 155; 16; 158; 1; 2; 3 ]
            DexOwn = Set.ofList [ 155; 16; 158 ] }
