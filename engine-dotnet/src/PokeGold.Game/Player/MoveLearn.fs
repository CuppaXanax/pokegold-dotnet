namespace PokeGold.Game.Player

open PokeGold.Game.Data

/// Move learning on level-up.
module MoveLearn =

    /// Get moves a species learns at a specific level.
    let movesAtLevel (speciesName: string) (level: int) : string list =
        match EvosAttacksAccess.forSpecies speciesName with
        | None -> []
        | Some data ->
            data.Learnset
            |> List.choose (fun entry ->
                if entry.Level = level then Some entry.Move else None)

    /// Try to learn a move by name. If fewer than 4 moves, add it. If 4 moves,
    /// replace the first move with lowest power (simplified AI).
    /// Returns the updated moves list.
    let tryLearnMove (moveName: string) (currentMoves: (int * int) list) : (int * int) list =
        match MovesData.byIndex |> Array.tryFindIndex (fun move -> move.Name = moveName) with
        | None -> currentMoves
        | Some moveIndex ->
            let moveData = MovesData.byIndex.[moveIndex]

            if currentMoves |> List.exists (fun (id, _) -> id = moveIndex) then
                currentMoves
            elif currentMoves.Length < 4 then
                currentMoves @ [ moveIndex, moveData.Pp ]
            else
                let weakestIndex =
                    currentMoves
                    |> List.mapi (fun i (id, _) ->
                        let power =
                            if id > 0 && id < MovesData.byIndex.Length then
                                MovesData.byIndex.[id].Power
                            else
                                0
                        i, power)
                    |> List.minBy snd
                    |> fst

                currentMoves
                |> List.mapi (fun i entry ->
                    if i = weakestIndex then moveIndex, moveData.Pp else entry)

    /// Seed a newly created mon's moves from its species learnset.
    /// Gives all moves at or below its current level (up to 4, latest moves preferred).
    let seedStartingMoves (mon: PartyMon) : PartyMon =
        let speciesName =
            Species.all
            |> Map.tryPick (fun name stats -> if stats.Dex = mon.SpeciesId then Some name else None)
            |> Option.defaultValue ""

        match EvosAttacksAccess.forSpecies speciesName with
        | None -> mon
        | Some data ->
            let eligible =
                data.Learnset
                |> List.filter (fun entry -> entry.Level <= mon.Level)
                |> List.rev
                |> List.truncate 4
                |> List.rev

            let moves =
                eligible
                |> List.choose (fun entry ->
                    MovesData.byIndex
                    |> Array.tryFindIndex (fun move -> move.Name = entry.Move)
                    |> Option.map (fun idx -> idx, MovesData.byIndex.[idx].Pp))

            { mon with Moves = moves }

    /// Apply level-up move learning to a PartyMon.
    /// Checks the learnset for the mon's species at its current level.
    let learnMovesForLevel (mon: PartyMon) : PartyMon =
        let speciesName =
            Species.all
            |> Map.tryPick (fun name stats -> if stats.Dex = mon.SpeciesId then Some name else None)
            |> Option.defaultValue ""

        let newMoves = movesAtLevel speciesName mon.Level

        let updatedMoves =
            newMoves
            |> List.fold (fun moves moveName -> tryLearnMove moveName moves) mon.Moves

        { mon with Moves = updatedMoves }
