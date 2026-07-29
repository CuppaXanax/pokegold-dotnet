namespace PokeGold.Game.Data

/// Runtime access to generated egg-move lists.
module EggMoves =

    let forSpecies (name: string) : Set<string> =
        EggMovesData.bySpecies |> Map.tryFind name |> Option.defaultValue Set.empty
