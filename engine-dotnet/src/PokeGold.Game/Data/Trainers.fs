namespace PokeGold.Game.Data

/// Runtime access to trainer party data. The table itself is baked at build time
/// into `TrainersData.all` (`Data/Generated/Trainers.Generated.fs`).
module Trainers =

    let all : Map<string * int, TrainerData> = TrainersData.all

    let lookup (group: string) (id: int) : TrainerData option =
        all
        |> Map.tryFind (group.ToUpperInvariant(), id)

    /// Look up a trainer by group name and trainer constant name (e.g., "HIKER", "ANTHONY2").
    /// Resolves the constant name to a numeric id by scanning the group's entries.
    let lookupByName (group: string) (name: string) : TrainerData option =
        let g = group.ToUpperInvariant()
        // Try parsing as an int first (some callers may pass numeric ids)
        match System.Int32.TryParse(name) with
        | true, id -> lookup g id
        | _ ->
            // Scan the group for a matching constant name suffix
            // The trainer constant is GROUP + NAME (e.g., HIKER_ANTHONY2 → name ANTHONY)
            // Strip trailing digits to match the Name field, or match by position
            all
            |> Map.toSeq
            |> Seq.tryFind (fun ((grp, _), data) ->
                grp = g && name.StartsWith(data.Name, System.StringComparison.OrdinalIgnoreCase))
            |> Option.map snd
            |> Option.orElseWith (fun () ->
                // Fallback: try exact position match from trainer constants
                // Constants are 1-based sequential, so we need the constant table
                // For now, scan all entries in the group and match by index
                let groupEntries =
                    all |> Map.toSeq
                    |> Seq.filter (fun ((grp, _), _) -> grp = g)
                    |> Seq.sortBy (fun ((_, id), _) -> id)
                    |> Seq.toArray
                // The constant name often contains the trainer Name + a suffix number
                // e.g., ANTHONY2 → name "ANTHONY", id 5
                groupEntries
                |> Array.tryFind (fun ((_, _), data) ->
                    name.StartsWith(data.Name, System.StringComparison.OrdinalIgnoreCase))
                |> Option.map snd)
