namespace PokeGold.Game.Player

open PokeGold.Game.Data

type TmHmTeachResult =
    | UnknownTmHm
    | Incompatible
    | AlreadyKnows
    | LearnedImmediately of PartyMon
    | NeedsReplacement of moveId: int

module TmHm =

    let private legacyAliases =
        Map.ofList
            [ "HM_CUT", "HM01"; "HM_FLY", "HM02"; "HM_SURF", "HM03"
              "HM_STRENGTH", "HM04"; "HM_FLASH", "HM05"
              "HM_WHIRLPOOL", "HM06"; "HM_WATERFALL", "HM07" ]

    let normalizeItem item = Map.tryFind item legacyAliases |> Option.defaultValue item

    let isHmItem item = Set.contains (normalizeItem item) TmHmData.hmItems

    let isHmMove moveName = Set.contains moveName TmHmData.hmMoves

    let moveForItem item = Map.tryFind (normalizeItem item) TmHmData.moveByItem

    let private speciesName speciesId =
        Species.all
        |> Map.tryPick (fun name stats -> if stats.Dex = speciesId then Some name else None)

    let canLearnMove moveName (mon: PartyMon) =
        speciesName mon.SpeciesId
        |> Option.bind (fun name -> Map.tryFind name TmHmData.compatibleMovesBySpecies)
        |> Option.exists (Set.contains moveName)

    let prepare item mon =
        match moveForItem item with
        | None -> UnknownTmHm
        | Some moveName when not (canLearnMove moveName mon) -> Incompatible
        | Some moveName ->
            match MovesData.byIndex |> Array.tryFindIndex (fun move -> move.Name = moveName) with
            | None -> UnknownTmHm
            | Some moveId when mon.Moves |> List.exists (fun (known, _) -> known = moveId) -> AlreadyKnows
            | Some moveId when mon.Moves.Length < 4 ->
                LearnedImmediately { mon with Moves = mon.Moves @ [ moveId, MovesData.byIndex.[moveId].Pp ] }
            | Some moveId -> NeedsReplacement moveId

    let replaceMove moveId index (mon: PartyMon) =
        match MovesData.byIndex |> Array.tryItem moveId with
        | None -> mon
        | Some move ->
            { mon with
                Moves = mon.Moves |> List.mapi (fun i existing -> if i = index then moveId, move.Pp else existing) }

    /// Compatibility wrapper for immediate teaching callers. Full movesets use
    /// `prepare` and the player-controlled LearnMoveScene.
    let teach moveName mon =
        if not (canLearnMove moveName mon) then None
        else
            match MovesData.byIndex |> Array.tryFindIndex (fun move -> move.Name = moveName) with
            | None -> None
            | Some moveId when mon.Moves |> List.exists (fun (known, _) -> known = moveId) -> None
            | Some moveId when mon.Moves.Length < 4 ->
                Some { mon with Moves = mon.Moves @ [ moveId, MovesData.byIndex.[moveId].Pp ] }
            | Some _ -> None
