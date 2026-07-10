namespace PokeGold.Game.Player

open PokeGold.Game.Data

/// Move learning on level-up.
module MoveLearn =

    /// Reproduce engine/pokemon/evolve.asm::FillMoves for a newly generated mon.
    /// Learnset entries are consumed in source order through the current level;
    /// duplicates already in the four slots are skipped and overflow forgets
    /// the oldest move.
    let startingMoveNames (speciesName: string) (level: int) : string list =
        let data =
            EvosAttacksAccess.forSpecies speciesName
            |> Option.defaultWith (fun () -> invalidArg (nameof speciesName) $"Unknown species learnset: {speciesName}")

        data.Learnset
        |> List.takeWhile (fun entry -> entry.Level <= level)
        |> List.fold (fun moves entry ->
            if List.contains entry.Move moves then
                moves
            elif moves.Length < 4 then
                moves @ [ entry.Move ]
            else
                List.tail moves @ [ entry.Move ]) []

    /// Resolve a generated trainer party row to its meaningful move names.
    /// Explicit TRAINERTYPE_MOVES/ITEM_MOVES slots override FillMoves and retain
    /// source order; NO_MOVE slots do not become battle commands.
    let trainerMoveNames (mon: TrainerMon) : string list =
        match mon.ExplicitMoves with
        | [] -> startingMoveNames mon.Species mon.Level
        | explicitMoves -> explicitMoves |> List.filter (fun move -> move <> "NO_MOVE")

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

        match speciesName with
        | None -> mon
        | Some name ->
            let moves =
                startingMoveNames name mon.Level
                |> List.map (fun moveName ->
                    let idx = MovesData.byIndex |> Array.findIndex (fun move -> move.Name = moveName)
                    idx, MovesData.byIndex.[idx].Pp)

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
