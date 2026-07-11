namespace PokeGold.Game.Player

open PokeGold.Game.Battle
open PokeGold.Game.Data

/// Applies source-ordered per-defeat arithmetic and level-crossing effects.
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

    let private growthRate mon =
        Species.all
        |> Map.tryPick (fun _ stats -> if stats.Dex = mon.SpeciesId then Some stats.GrowthRate else None)
        |> Option.defaultValue 0

    let private levelHappiness friendship =
        friendship + (if friendship < 100 then 5 elif friendship < 200 then 3 else 2)
        |> min 255

    let private addProgression amount statYield (event: DefeatProgressionEvent) (mon: PartyMon) =
        let traded = mon.OtId <> 0
        let boosted =
            amount
            |> fun value -> if traded then boostByHalf value else value
            |> fun value -> if event.IsTrainer then boostByHalf value else value
            |> fun value -> if Set.contains mon.Id event.LuckyEggHolderIds then boostByHalf value else value
        let awarded = addStatExp statYield mon
        let rate = growthRate awarded
        let newExp = min (Experience.expForLevel rate 100) (awarded.Exp + boosted)
        let newLevel, _ = Experience.levelAfterExp rate awarded.Level awarded.Exp (newExp - awarded.Exp)
        if newLevel = awarded.Level then
            { awarded with Exp = newExp }
        else
            let leveled =
                { PartyMon.withLevel newLevel awarded with
                    Exp = newExp
                    Friendship = levelHappiness awarded.Friendship }
            [ awarded.Level + 1 .. newLevel ]
            |> List.fold (fun current level ->
                MoveLearn.learnMovesForLevel { current with Level = level }) leveled
            |> fun current -> { current with Level = newLevel }

    let private applyPool baseDivisor recipients (event: DefeatProgressionEvent) (party: Party) =
        if Set.isEmpty recipients then party
        else
            let divisor = baseDivisor * Set.count recipients
            let baseExp = event.DefeatedSpecies.BaseExp / divisor
            let exp = baseExp * event.DefeatedLevel / 7
            let statExp = scaledStatExp divisor event.StatExpYield
            party
            |> List.map (fun mon ->
                if Set.contains mon.Id recipients then addProgression exp statExp event mon
                else mon)

    let applyEvent (event: DefeatProgressionEvent) (party: Party) : Party =
        let shareDivisor = if Set.isEmpty event.ExpShareHolderIds then 1 else 2
        party
        |> applyPool shareDivisor event.ParticipantIds event
        |> applyPool shareDivisor event.ExpShareHolderIds event

    let applyEvents (events: DefeatProgressionEvent list) (party: Party) : Party =
        events |> List.fold (fun current event -> applyEvent event current) party

    /// Gold defers evolution until victorious battle cleanup. A flagged member
    /// is visited once, so it can evolve by at most one stage per battle.
    let applyBattle outcome (events: DefeatProgressionEvent list) (party: Party) : Party =
        let progressed = applyEvents events party
        if outcome <> Some Win then progressed
        else
            let originalLevels = party |> List.map (fun mon -> mon.Id, mon.Level) |> Map.ofList
            progressed
            |> List.map (fun mon ->
                let gainedLevel = Map.tryFind mon.Id originalLevels |> Option.exists (fun level -> mon.Level > level)
                if not gainedLevel || mon.HeldItem = Some "EVERSTONE" then mon
                else
                    match Evolution.checkLevelEvolution mon with
                    | Some target -> Evolution.applyEvolution target mon |> MoveLearn.learnMovesForLevel
                    | None -> mon)
