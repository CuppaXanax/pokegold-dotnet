namespace PokeGold.Game.Player

open PokeGold.Game.Data

/// Level-based evolution checks.
/// Source: engine/pokemon/evolve.asm
module Evolution =

    /// Check if a PartyMon should evolve at its current level.
    /// Returns Some(targetSpeciesName) or None.
    let checkLevelEvolution (mon: PartyMon) : string option =
        let speciesName =
            Species.all
            |> Map.tryPick (fun name s -> if s.Dex = mon.SpeciesId then Some name else None)

        match speciesName with
        | None -> None
        | Some name ->
            match EvosAttacksAccess.forSpecies name with
            | None -> None
            | Some data ->
                data.Evolutions
                |> List.tryPick (fun evo ->
                    if evo.Method = "EVOLVE_LEVEL" then
                        match System.Int32.TryParse(evo.Param) with
                        | true, lvl when mon.Level >= lvl -> Some evo.Target
                        | _ -> None
                    else None)

    /// Apply an evolution to a PartyMon: change species, recalc stats.
    /// Keeps nickname if it was the default (species name), otherwise preserves custom name.
    let applyEvolution (targetSpecies: string) (mon: PartyMon) : PartyMon =
        match Map.tryFind targetSpecies Species.all with
        | None -> mon
        | Some stats ->
            let oldName =
                Species.all
                |> Map.tryPick (fun name s -> if s.Dex = mon.SpeciesId then Some name else None)
                |> Option.defaultValue ""
            let nickname =
                if mon.Nickname = oldName then targetSpecies
                else mon.Nickname
            let newMaxHp = PartyMon.deriveMaxHp stats.Dex mon.Level
            let hpGain = newMaxHp - mon.MaxHp
            { mon with
                SpeciesId = stats.Dex
                Nickname = nickname
                MaxHp = newMaxHp
                Hp = mon.Hp + max 0 hpGain }
