namespace PokeGold.Game.Data

/// Runtime access to wild encounter tables. The table itself is baked at build
/// time into `WildEncountersData.all` (`Data/Generated/WildEncounters.Generated.fs`).
module WildEncounters =

    let all : Map<string, WildEncounterTable> = WildEncountersData.all

    let forMap (mapName: string) : WildEncounterTable option =
        Map.tryFind mapName all
