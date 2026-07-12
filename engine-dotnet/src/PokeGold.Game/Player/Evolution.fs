namespace PokeGold.Game.Player

open PokeGold.Game.Battle
open PokeGold.Game.Core
open PokeGold.Game.Data

type EvolutionTrigger =
    | LevelUp of TimeOfDay
    | ItemUse of item: string
    | Trade of isTimeCapsule: bool

type EvolutionCandidate =
    { Target: string
      ConsumeHeldItem: bool }

type EvolutionRequest =
    { MonId: System.Guid
      Candidate: EvolutionCandidate }

/// Source-ordered evolution eligibility and mutation.
/// Source: engine/pokemon/evolve.asm.
module Evolution =

    let private speciesName (mon: PartyMon) =
        Species.all |> Map.tryPick (fun name stats -> if stats.Dex = mon.SpeciesId then Some name else None)

    let private parseInt (value: string) =
        match System.Int32.TryParse value with
        | true, parsed -> Some parsed
        | _ -> None

    let private timeMatches condition time =
        match condition, time with
        | "TR_ANYTIME", _ -> true
        | "TR_MORNDAY", (Morn | Day) -> true
        | "TR_NITE", Nite -> true
        | _ -> false

    let private statMatches comparison (mon: PartyMon) =
        match speciesName mon |> Option.bind (fun name -> Map.tryFind name Species.all) with
        | None -> false
        | Some species ->
            let stats = BattleMon.calculateStats species mon.Level mon.Dvs mon.StatExp
            match comparison with
            | "ATK_LT_DEF" -> stats.Attack < stats.Defense
            | "ATK_GT_DEF" -> stats.Attack > stats.Defense
            | "ATK_EQ_DEF" -> stats.Attack = stats.Defense
            | _ -> false

    let private qualifies trigger (mon: PartyMon) (evo: EvolutionEntry) =
        match trigger, evo.Method with
        | LevelUp _, "EVOLVE_LEVEL" -> parseInt evo.Param |> Option.exists (fun level -> mon.Level >= level)
        | LevelUp time, "EVOLVE_HAPPINESS" -> mon.Friendship >= 220 && timeMatches evo.Param time
        | LevelUp _, "EVOLVE_STAT" ->
            parseInt evo.Param |> Option.exists (fun level -> mon.Level >= level) && statMatches evo.Param2 mon
        | ItemUse item, "EVOLVE_ITEM" -> evo.Param = item
        | Trade isTimeCapsule, "EVOLVE_TRADE" ->
            if evo.Param = "-1" then true
            else not isTimeCapsule && mon.HeldItem = Some evo.Param
        | _ -> false

    /// Find the first qualifying source record. Everstone blocks every route;
    /// item-use performs this check before entering evolve.asm in the ROM.
    let tryFind trigger (mon: PartyMon) =
        if mon.HeldItem = Some "EVERSTONE" then None
        else
            speciesName mon
            |> Option.bind EvosAttacksAccess.forSpecies
            |> Option.bind (fun data ->
                data.Evolutions
                |> List.tryFind (qualifies trigger mon)
                |> Option.map (fun evo ->
                    { Target = evo.Target
                      ConsumeHeldItem =
                        match trigger, evo.Method with
                        | Trade _, "EVOLVE_TRADE" when evo.Param <> "-1" -> true
                        | _ -> false }))

    let checkLevelEvolution (mon: PartyMon) = tryFind (LevelUp Day) mon |> Option.map _.Target

    let prepareAttempt candidate (mon: PartyMon) =
        if candidate.ConsumeHeldItem then { mon with HeldItem = None } else mon

    /// Apply an accepted evolution after attempt-time catalysts are consumed.
    let applyCandidate candidate (mon: PartyMon) =
        let mon = prepareAttempt candidate mon
        match Map.tryFind candidate.Target Species.all with
        | None -> mon
        | Some stats ->
            let oldName = speciesName mon |> Option.defaultValue ""
            let nickname = if mon.Nickname = oldName then candidate.Target else mon.Nickname
            let newMaxHp = PartyMon.deriveMaxHpWith stats.Dex mon.Level mon.Dvs mon.StatExp
            let hpGain = newMaxHp - mon.MaxHp
            { mon with
                SpeciesId = stats.Dex
                Nickname = nickname
                MaxHp = newMaxHp
                Hp = max 0 (mon.Hp + hpGain) }

    let applyEvolution target (mon: PartyMon) =
        applyCandidate { Target = target; ConsumeHeldItem = false } mon
