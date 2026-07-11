namespace PokeGold.Game.Player

open PokeGold.Game.Battle
open PokeGold.Game.Data

/// Applies source-ordered per-defeat arithmetic. Level-crossing side effects are
/// deliberately deferred to BAT-010; this module only persists EXP/stat EXP.
module BattleProgression =

    let private boostByHalf value = value + value / 2
    let private saturatingAdd value amount = min 65535 (value + amount)

    let private scaledStatExp divisor (statYield: StatExperience) =
        { Hp = statYield.Hp / divisor
          Attack = statYield.Attack / divisor
          Defense = statYield.Defense / divisor
          Speed = statYield.Speed / divisor
          Special = statYield.Special / divisor }

    let private addStatExp (statYield: StatExperience) (mon: PartyMon) =
        let add value amount =
            let once = saturatingAdd value amount
            if mon.Pokerus <> 0 then saturatingAdd once amount else once
        { mon with
            StatExp =
                { Hp = add mon.StatExp.Hp statYield.Hp
                  Attack = add mon.StatExp.Attack statYield.Attack
                  Defense = add mon.StatExp.Defense statYield.Defense
                  Speed = add mon.StatExp.Speed statYield.Speed
                  Special = add mon.StatExp.Special statYield.Special } }

    let private addExp amount (event: DefeatProgressionEvent) (mon: PartyMon) =
        let traded = mon.OtId <> 0
        let boosted =
            amount
            |> fun value -> if traded then boostByHalf value else value
            |> fun value -> if event.IsTrainer then boostByHalf value else value
            |> fun value -> if Set.contains mon.Id event.LuckyEggHolderIds then boostByHalf value else value
        let growthRate =
            Species.all
            |> Map.tryPick (fun _ stats -> if stats.Dex = mon.SpeciesId then Some stats.GrowthRate else None)
            |> Option.defaultValue 0
        { mon with Exp = min (Experience.expForLevel growthRate 100) (mon.Exp + boosted) }

    let private applyPool baseDivisor recipients (event: DefeatProgressionEvent) (party: Party) =
        if Set.isEmpty recipients then party
        else
            let divisor = baseDivisor * Set.count recipients
            let baseExp = event.DefeatedSpecies.BaseExp / divisor
            let exp = baseExp * event.DefeatedLevel / 7
            let statExp = scaledStatExp divisor event.StatExpYield
            party
            |> List.map (fun mon ->
                if Set.contains mon.Id recipients then mon |> addExp exp event |> addStatExp statExp
                else mon)

    let applyEvent (event: DefeatProgressionEvent) (party: Party) : Party =
        let shareDivisor = if Set.isEmpty event.ExpShareHolderIds then 1 else 2
        party
        |> applyPool shareDivisor event.ParticipantIds event
        |> applyPool shareDivisor event.ExpShareHolderIds event

    let applyEvents (events: DefeatProgressionEvent list) (party: Party) : Party =
        events |> List.fold (fun current event -> applyEvent event current) party
