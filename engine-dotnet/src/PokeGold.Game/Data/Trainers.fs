namespace PokeGold.Game.Data

/// Runtime access to trainer party data. The table itself is baked at build time
/// into `TrainersData.all` (`Data/Generated/Trainers.Generated.fs`).
module Trainers =

    let all : Map<string * int, TrainerData> = TrainersData.all

    let lookup (group: string) (id: int) : TrainerData option =
        all
        |> Map.tryFind (group.ToUpperInvariant(), id)

    /// Look up a trainer by group name and trainer constant name (e.g., "HIKER", "ANTHONY2").
    /// Resolves the constant name through the generated trainer_constants.asm table.
    let lookupByName (group: string) (name: string) : TrainerData option =
        let g = group.ToUpperInvariant()

        match System.Int32.TryParse(name) with
        | true, id -> lookup g id
        | _ ->
            match TrainersData.byConstant |> Map.tryFind (name.ToUpperInvariant()) with
            | Some(grp, id) when grp = g -> lookup grp id
            | _ -> None
