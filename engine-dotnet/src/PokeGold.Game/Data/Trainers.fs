namespace PokeGold.Game.Data

/// Runtime access to trainer party data. The table itself is baked at build time
/// into `TrainersData.all` (`Data/Generated/Trainers.Generated.fs`).
module Trainers =

    let all : Map<string * int, TrainerData> = TrainersData.all

    let lookup (group: string) (id: int) : TrainerData option =
        all
        |> Map.tryFind (group.ToUpperInvariant(), id)
