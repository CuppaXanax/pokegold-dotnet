namespace PokeGold.Game.Battle

open PokeGold.Game.Data

/// Gen-2 trainer AI: start legal moves at source score 20, apply enabled
/// scoring layers, then randomly choose among the lowest-scoring moves.
/// Source: engine/battle/ai/{move,scoring,switch,items}.asm.
module BattleAI =

    type TrainerProfile = { MoveFlags: string list; ItemSwitchFlags: string list; Items: string list }

    type TrainerItemUse = { Item: string; Enemy: BattleMon }

    type AiContext = {
        EnemyTurnsTaken: int
        PlayerTurnsTaken: int
        EnemySide: SideState
        PlayerSide: SideState
        WeatherType: string option
        EnemyTeam: BattleMon list
        PlayerTeam: BattleMon list
    }

    let private emptyProfile = { MoveFlags = []; ItemSwitchFlags = []; Items = [] }

    let profileForTrainer group id = Trainers.lookupByName group id |> Option.map (fun trainer -> { MoveFlags = trainer.AiMoveFlags; ItemSwitchFlags = trainer.AiItemSwitchFlags; Items = trainer.AiItems }) |> Option.defaultValue emptyProfile

    let private sourceLayerOrder =
        [ "AI_BASIC"; "AI_SETUP"; "AI_TYPES"; "AI_OFFENSIVE"; "AI_SMART"
          "AI_OPPORTUNIST"; "AI_AGGRESSIVE"; "AI_CAUTIOUS"; "AI_STATUS"; "AI_RISKY" ]

    let private statusOnlyEffects = Set.ofList [ "EFFECT_SLEEP"; "EFFECT_TOXIC"; "EFFECT_POISON"; "EFFECT_PARALYZE" ]
    let private stallMoves =
        Set.ofList
            [ "SWORDS_DANCE"; "TAIL_WHIP"; "LEER"; "GROWL"; "DISABLE"; "MIST"; "COUNTER"; "LEECH_SEED"; "GROWTH"
              "STRING_SHOT"; "MEDITATE"; "AGILITY"; "RAGE"; "MIMIC"; "SCREECH"; "HARDEN"; "WITHDRAW"; "DEFENSE_CURL"
              "BARRIER"; "LIGHT_SCREEN"; "HAZE"; "REFLECT"; "FOCUS_ENERGY"; "BIDE"; "AMNESIA"; "TRANSFORM"; "SPLASH"
              "ACID_ARMOR"; "SHARPEN"; "CONVERSION"; "SUBSTITUTE"; "FLAME_WHEEL" ]
    let private residualMoves =
        Set.ofList [ "MIST"; "LEECH_SEED"; "POISONPOWDER"; "STUN_SPORE"; "THUNDER_WAVE"; "FOCUS_ENERGY"; "BIDE"
                     "POISON_GAS"; "TRANSFORM"; "CONVERSION"; "SUBSTITUTE"; "SPIKES" ]
    let private recklessEffects = Set.ofList [ "EFFECT_SELFDESTRUCT"; "EFFECT_RAMPAGE"; "EFFECT_MULTI_HIT"; "EFFECT_DOUBLE_HIT" ]
    let private riskyEffects = Set.ofList [ "EFFECT_SELFDESTRUCT"; "EFFECT_OHKO" ]

    /// Effects explicitly dispatched by AI_Smart_EffectHandlers in scoring.asm.
    let smartHandlerEffects =
        Set.ofList
            [ "EFFECT_SLEEP"; "EFFECT_LEECH_HIT"; "EFFECT_SELFDESTRUCT"; "EFFECT_DREAM_EATER"; "EFFECT_MIRROR_MOVE"
              "EFFECT_EVASION_UP"; "EFFECT_ALWAYS_HIT"; "EFFECT_ACCURACY_DOWN"; "EFFECT_RESET_STATS"; "EFFECT_BIDE"
              "EFFECT_FORCE_SWITCH"; "EFFECT_HEAL"; "EFFECT_TOXIC"; "EFFECT_LIGHT_SCREEN"; "EFFECT_OHKO"
              "EFFECT_RAZOR_WIND"; "EFFECT_SUPER_FANG"; "EFFECT_TRAP_TARGET"; "EFFECT_UNUSED_2B"; "EFFECT_CONFUSE"
              "EFFECT_SP_DEF_UP_2"; "EFFECT_REFLECT"; "EFFECT_PARALYZE"; "EFFECT_SPEED_DOWN_HIT"; "EFFECT_SUBSTITUTE"
              "EFFECT_HYPER_BEAM"; "EFFECT_RAGE"; "EFFECT_MIMIC"; "EFFECT_LEECH_SEED"; "EFFECT_DISABLE"
              "EFFECT_COUNTER"; "EFFECT_ENCORE"; "EFFECT_PAIN_SPLIT"; "EFFECT_SNORE"; "EFFECT_CONVERSION2"
              "EFFECT_LOCK_ON"; "EFFECT_DEFROST_OPPONENT"; "EFFECT_SLEEP_TALK"; "EFFECT_DESTINY_BOND"; "EFFECT_REVERSAL"
              "EFFECT_SPITE"; "EFFECT_HEAL_BELL"; "EFFECT_PRIORITY_HIT"; "EFFECT_THIEF"; "EFFECT_MEAN_LOOK"
              "EFFECT_NIGHTMARE"; "EFFECT_FLAME_WHEEL"; "EFFECT_CURSE"; "EFFECT_PROTECT"; "EFFECT_FORESIGHT"
              "EFFECT_PERISH_SONG"; "EFFECT_SANDSTORM"; "EFFECT_ENDURE"; "EFFECT_ROLLOUT"; "EFFECT_SWAGGER"
              "EFFECT_FURY_CUTTER"; "EFFECT_ATTRACT"; "EFFECT_SAFEGUARD"; "EFFECT_MAGNITUDE"; "EFFECT_BATON_PASS"
              "EFFECT_PURSUIT"; "EFFECT_RAPID_SPIN"; "EFFECT_MORNING_SUN"; "EFFECT_SYNTHESIS"; "EFFECT_MOONLIGHT"
              "EFFECT_HIDDEN_POWER"; "EFFECT_RAIN_DANCE"; "EFFECT_SUNNY_DAY"; "EFFECT_BELLY_DRUM"; "EFFECT_PSYCH_UP"
              "EFFECT_MIRROR_COAT"; "EFFECT_SKULL_BASH"; "EFFECT_TWISTER"; "EFFECT_EARTHQUAKE"; "EFFECT_FUTURE_SIGHT"
              "EFFECT_GUST"; "EFFECT_STOMP"; "EFFECT_SOLARBEAM"; "EFFECT_THUNDER"; "EFFECT_FLY" ]

    let private defaultContext enemyTurnsTaken =
                { EnemyTurnsTaken = enemyTurnsTaken; PlayerTurnsTaken = 0; EnemySide = SideState.Empty; PlayerSide = SideState.Empty; WeatherType = None; EnemyTeam = []; PlayerTeam = [] }

    let private percentThreshold percent = percent * 255 / 100

    let private rollBelow threshold (rng: Rng) =
        let roll, nextRng = Rng.next rng
        roll < threshold, nextRng

    let private chancePercent percent (rng: Rng) =
        rollBelow (percentThreshold percent) rng

    let private chance50 rng = rollBelow (percentThreshold 50 + 1) rng
    let private chance20 rng = rollBelow (percentThreshold 20 - 1) rng
    let private chance80 rng =
        let skipped, nextRng = chance20 rng
        not skipped, nextRng

    let private atMostHalf (mon: BattleMon) = mon.Hp * 2 <= mon.MaxHp
    let private atMostQuarter (mon: BattleMon) = mon.Hp * 4 <= mon.MaxHp
    let private atFullHealth (mon: BattleMon) = mon.Hp >= mon.MaxHp
    let private hasEffect effect (mon: BattleMon) = mon.Moves |> List.exists (fun move -> move.Effect = effect)
    let private isAsleep (mon: BattleMon) = match mon.Status with Sleep _ -> true | _ -> false
    let private isFaster (user: BattleMon) (target: BattleMon) = BattleMon.effectiveSpeed user > BattleMon.effectiveSpeed target
    let private statStages (mon: BattleMon) = [ mon.AtkStage; mon.DefStage; mon.SpdStage; mon.SpAtkStage; mon.SpDefStage; mon.AccStage; mon.EvaStage ]
    let private hasSuperEffectiveMove (user: BattleMon) (target: BattleMon) = user.Moves |> List.exists (fun move -> move.Power > 0 && Damage.effectivenessTimesTen move target > 10)
    let private isPhysical (move: MoveData) = move.Power > 0 && TypeChart.isPhysical move.Type
    let private isSpecial (move: MoveData) = move.Power > 0 && not (TypeChart.isPhysical move.Type)
    let private isBadPoisoned (mon: BattleMon) = match mon.Status with BadPoison _ -> true | _ -> false
    let private lastPp (mon: BattleMon) =
        mon.Volatile.LastCounterMove
        |> Option.bind (fun move ->
            mon.Moves
            |> List.tryFindIndex (fun candidate -> candidate.Name = move.Name)
            |> Option.bind (fun index -> mon.Pp |> List.tryItem index))
    let private targetIsCharging target effects =
        target.Volatile.ChargingMove |> Option.exists (fun move -> Set.contains move.Effect effects)
    let private isOppositeGender (user: BattleMon) (target: BattleMon) =
        match user.Gender, target.Gender with
        | Male, Female
        | Female, Male -> true
        | _ -> false

    let private isRedundant (context: AiContext) (user: BattleMon) (target: BattleMon) (move: MoveData) =
        match move.Effect with
        | "EFFECT_DREAM_EATER" -> not (isAsleep target)
        | "EFFECT_HEAL" | "EFFECT_MORNING_SUN" | "EFFECT_SYNTHESIS" | "EFFECT_MOONLIGHT" -> atFullHealth user
        | "EFFECT_LIGHT_SCREEN" -> context.EnemySide.LightScreenTimer.IsSome
        | "EFFECT_MIST" -> user.Volatile.Mist
        | "EFFECT_FOCUS_ENERGY" -> user.Volatile.FocusEnergy
        | "EFFECT_CONFUSE" -> target.Volatile.Confusion.IsSome || context.PlayerSide.SafeguardTimer.IsSome
        | "EFFECT_TRANSFORM" -> user.Volatile.Transformed
        | "EFFECT_REFLECT" -> context.EnemySide.ReflectTimer.IsSome
        | "EFFECT_SUBSTITUTE" -> user.Volatile.Substitute.IsSome
        | "EFFECT_LEECH_SEED" -> target.Volatile.LeechSeed
        | "EFFECT_DISABLE" -> target.Volatile.DisableTimer.IsSome
        | "EFFECT_ENCORE" -> target.Volatile.EncoreTimer.IsSome
        | "EFFECT_SNORE" | "EFFECT_SLEEP_TALK" -> not (isAsleep user)
        | "EFFECT_MEAN_LOOK" -> target.Volatile.CantEscape
        | "EFFECT_NIGHTMARE" -> target.Status = Healthy || target.Volatile.Nightmare
        | "EFFECT_SPIKES" -> context.PlayerSide.Spikes > 0
        | "EFFECT_FORESIGHT" -> target.Volatile.Foresight
        | "EFFECT_PERISH_SONG" -> context.PlayerSide.PerishCounter.IsSome
        | "EFFECT_SANDSTORM" -> context.WeatherType = Some "SAND"
        | "EFFECT_ATTRACT" -> not (isOppositeGender user target) || target.Volatile.Attracted
        | "EFFECT_SAFEGUARD" -> context.EnemySide.SafeguardTimer.IsSome
        | "EFFECT_RAIN_DANCE" -> context.WeatherType = Some "RAIN"
        | "EFFECT_SUNNY_DAY" -> context.WeatherType = Some "SUN"
        | "EFFECT_TELEPORT" -> true
        | _ -> false

    let private statUpEffects =
        Set.ofList
            [ "EFFECT_ATTACK_UP"; "EFFECT_DEFENSE_UP"; "EFFECT_SPEED_UP"; "EFFECT_SP_ATK_UP"; "EFFECT_SP_DEF_UP"
              "EFFECT_ACCURACY_UP"; "EFFECT_EVASION_UP"; "EFFECT_ATTACK_UP_2"; "EFFECT_DEFENSE_UP_2"; "EFFECT_SPEED_UP_2"
              "EFFECT_SP_ATK_UP_2"; "EFFECT_SP_DEF_UP_2"; "EFFECT_ACCURACY_UP_2"; "EFFECT_EVASION_UP_2" ]
    let private statDownEffects =
        Set.ofList
            [ "EFFECT_ATTACK_DOWN"; "EFFECT_DEFENSE_DOWN"; "EFFECT_SPEED_DOWN"; "EFFECT_SP_ATK_DOWN"; "EFFECT_SP_DEF_DOWN"
              "EFFECT_ACCURACY_DOWN"; "EFFECT_EVASION_DOWN"; "EFFECT_ATTACK_DOWN_2"; "EFFECT_DEFENSE_DOWN_2"; "EFFECT_SPEED_DOWN_2"
              "EFFECT_SP_ATK_DOWN_2"; "EFFECT_SP_DEF_DOWN_2"; "EFFECT_ACCURACY_DOWN_2"; "EFFECT_EVASION_DOWN_2" ]

    let private applyBasic (context: AiContext) (user: BattleMon) (target: BattleMon) (scores: int array) =
        user.Moves
        |> List.iteri (fun index move ->
            if isRedundant context user target move
               || (Set.contains move.Effect statusOnlyEffects && (target.Status <> Healthy || context.PlayerSide.SafeguardTimer.IsSome)) then
                scores.[index] <- scores.[index] + 10)

    let private applySetup (context: AiContext) (user: BattleMon) (scores: int array) (rng: Rng) =
        let mutable nextRng = rng
        user.Moves
        |> List.iteri (fun index move ->
            let relevantTurn, isSetup =
                if Set.contains move.Effect statUpEffects then context.EnemyTurnsTaken, true
                elif Set.contains move.Effect statDownEffects then context.PlayerTurnsTaken, true
                else 0, false
            if isSetup then
                if relevantTurn = 0 then
                    let encourage, afterRoll = chance50 nextRng
                    nextRng <- afterRoll
                    if encourage then scores.[index] <- scores.[index] - 2
                else
                    let skipDiscourage, afterRoll = chancePercent 12 nextRng
                    nextRng <- afterRoll
                    if not skipDiscourage then scores.[index] <- scores.[index] + 2)
        nextRng

    let private applyTypes (user: BattleMon) (target: BattleMon) (scores: int array) =
        user.Moves
        |> List.iteri (fun index move ->
            let effectiveness = Damage.effectivenessTimesTen move target
            if effectiveness = 0 then
                scores.[index] <- scores.[index] + 10
            elif move.Power > 0 && effectiveness > 10 then
                scores.[index] <- scores.[index] - 1
            elif move.Power > 0 && effectiveness < 10 then
                let hasAlternativeDamageType =
                    user.Moves
                    |> List.exists (fun candidate -> candidate.Power > 0 && candidate.Type <> move.Type)
                if hasAlternativeDamageType then scores.[index] <- scores.[index] + 1)

    let private applyOffensive (user: BattleMon) (scores: int array) =
        user.Moves
        |> List.iteri (fun index move -> if move.Power = 0 then scores.[index] <- scores.[index] + 2)

    let private estimatedDamage (user: BattleMon) (target: BattleMon) (move: MoveData) =
        if move.Power <= 0 then 0 else Damage.calc user target move false Damage.MaxRoll false

    let private smartAdjustment (context: AiContext) (user: BattleMon) (target: BattleMon) (move: MoveData) (rng: Rng) =
        let randomAdjustment percent adjustment =
            let selected, nextRng = chancePercent percent rng
            (if selected then adjustment else 0), nextRng
        let randomAdjustment50 adjustment =
            let selected, nextRng = chance50 rng
            (if selected then adjustment else 0), nextRng
        let randomAdjustment80 adjustment =
            let selected, nextRng = chance80 rng
            (if selected then adjustment else 0), nextRng
        let randomAdjustment90 adjustment =
            let skipped, nextRng = rollBelow (percentThreshold 10) rng
            (if skipped then 0 else adjustment), nextRng
        match move.Effect with
        | "EFFECT_SLEEP" when hasEffect "EFFECT_DREAM_EATER" user || hasEffect "EFFECT_NIGHTMARE" user -> randomAdjustment50 -2
        | "EFFECT_LEECH_HIT" ->
            let effectiveness = Damage.effectivenessTimesTen move target
            if effectiveness < 10 then randomAdjustment 60 1
            elif effectiveness > 10 && not (atFullHealth user) then randomAdjustment80 -1
            else 0, rng
        | "EFFECT_DREAM_EATER" when isAsleep target -> randomAdjustment 90 -3
        | "EFFECT_MIRROR_MOVE" ->
            match target.Volatile.LastCounterMove with
            | None -> if isFaster user target then 10, rng else 1, rng
            | Some _ ->
                let first, rng' = randomAdjustment50 -1
                if isFaster user target then
                    let second, rng'' = randomAdjustment90 -1
                    first + second, rng''
                else first, rng'
        | "EFFECT_EVASION_UP" ->
            if user.EvaStage >= 6 then 10, rng
            elif atMostQuarter user then 2, rng
            elif atFullHealth user && isBadPoisoned target then -2, rng
            elif user.EvaStage > target.AccStage then 1, rng
            else 0, rng
        | "EFFECT_ALWAYS_HIT" when user.AccStage <= -2 || target.EvaStage >= 3 -> randomAdjustment80 -2
        | "EFFECT_ACCURACY_DOWN" ->
            if atMostQuarter target then 2, rng
            elif user.AccStage < target.EvaStage then 1, rng
            else 0, rng
        | "EFFECT_RESET_STATS" ->
            if statStages user |> List.exists (fun stage -> stage <= -3)
               || statStages target |> List.exists (fun stage -> stage >= 3) then randomAdjustment 84 -1
            else 1, rng
        | "EFFECT_BIDE" when not (atFullHealth user) -> randomAdjustment90 1
        | "EFFECT_FORCE_SWITCH" when not (hasSuperEffectiveMove target user) -> 1, rng
        | "EFFECT_SELFDESTRUCT" when not (atMostHalf user) -> 3, rng
        | "EFFECT_SELFDESTRUCT" when not (atMostQuarter user) -> randomAdjustment 92 3
        | "EFFECT_HEAL" | "EFFECT_MORNING_SUN" | "EFFECT_SYNTHESIS" | "EFFECT_MOONLIGHT" ->
            if atMostQuarter user then randomAdjustment 90 -2
            elif not (atMostHalf user) then 1, rng
            else 0, rng
        | "EFFECT_TOXIC" | "EFFECT_LEECH_SEED" when atMostHalf target -> 1, rng
        | "EFFECT_LIGHT_SCREEN" | "EFFECT_REFLECT" when not (atFullHealth user) -> randomAdjustment 92 1
        | "EFFECT_OHKO" when target.Level > user.Level -> 10, rng
        | "EFFECT_OHKO" when atMostHalf target -> 1, rng
        | "EFFECT_RAZOR_WIND" | "EFFECT_UNUSED_2B" when atMostHalf user || target.Volatile.Protect -> randomAdjustment80 1
        | "EFFECT_TRAP_TARGET" when target.Volatile.Trapped.IsSome -> randomAdjustment50 1
        | "EFFECT_CONFUSE" when atMostQuarter target -> 2, rng
        | "EFFECT_SP_DEF_UP_2" when atMostHalf user || user.SpDefStage >= 3 -> 1, rng
        | "EFFECT_SP_DEF_UP_2" when user.SpDefStage < 2 && (not (TypeChart.isPhysical target.Species.Type1) || not (TypeChart.isPhysical target.Species.Type2)) -> randomAdjustment80 -2
        | "EFFECT_FLY" when targetIsCharging target (Set.ofList [ "EFFECT_FLY"; "EFFECT_DIG" ]) && isFaster user target -> -3, rng
        | "EFFECT_SUPER_FANG" when atMostQuarter target -> 1, rng
        | "EFFECT_PARALYZE" when atMostQuarter target -> randomAdjustment50 1
        | "EFFECT_PARALYZE" when not (isFaster user target) && not (atMostQuarter user) -> randomAdjustment80 -2
        | "EFFECT_SPEED_DOWN_HIT" when move.Name = "ICY_WIND" && context.PlayerTurnsTaken = 0 && isFaster target user && not (atMostQuarter user) -> randomAdjustment 88 -2
        | "EFFECT_SUBSTITUTE" when atMostHalf user -> 10, rng
        | "EFFECT_HYPER_BEAM" when atMostQuarter user -> randomAdjustment50 -1
        | "EFFECT_HYPER_BEAM" when not (atMostHalf user) -> randomAdjustment 65 1
        | "EFFECT_RAGE" when user.Volatile.Rage ->
            let baseAdjustment = if user.Volatile.RageCounter >= 2 then -1 else 0
            let extra = if user.Volatile.RageCounter >= 3 then -1 else 0
            baseAdjustment + extra, rng
        | "EFFECT_RAGE" when atMostHalf user -> 1, rng
        | "EFFECT_RAGE" -> randomAdjustment 20 -1
        | "EFFECT_MIMIC" when target.Volatile.LastCounterMove.IsNone -> if isFaster user target then 10, rng else 1, rng
        | "EFFECT_MIMIC" when atMostHalf user -> 1, rng
        | "EFFECT_DISABLE" when isFaster user target && target.Volatile.LastCounterMove.IsSome -> randomAdjustment 60 -1
        | "EFFECT_DISABLE" -> randomAdjustment 92 1
        | "EFFECT_COUNTER" ->
            match target.Volatile.LastCounterMove with
            | Some lastMove when isPhysical lastMove -> randomAdjustment 60 -1
            | _ -> 1, rng
        | "EFFECT_MIRROR_COAT" ->
            match target.Volatile.LastCounterMove with
            | Some lastMove when isSpecial lastMove -> randomAdjustment 60 -1
            | _ -> 1, rng
        | "EFFECT_ENCORE" when isFaster user target && target.Volatile.LastMove.IsSome -> randomAdjustment 72 -2
        | "EFFECT_ENCORE" -> 3, rng
        | "EFFECT_PAIN_SPLIT" when user.Hp * 2 > target.Hp -> 1, rng
        | "EFFECT_CONVERSION2" when target.Volatile.LastMove.IsSome -> randomAdjustment90 1
        | "EFFECT_LOCK_ON" when target.Volatile.LockOn -> 10, rng
        | "EFFECT_LOCK_ON" when user.AccStage <= -2 || target.EvaStage >= 3 -> randomAdjustment50 -2
        | "EFFECT_DEFROST_OPPONENT" when user.Status = Freeze -> -3, rng
        | "EFFECT_SNORE" | "EFFECT_SLEEP_TALK" -> if isAsleep user then -3, rng else 3, rng
        | "EFFECT_DESTINY_BOND" | "EFFECT_REVERSAL" | "EFFECT_SKULL_BASH" when not (atMostQuarter user) -> 1, rng
        | "EFFECT_SPITE" ->
            match lastPp target with
            | Some pp when pp < 6 -> randomAdjustment 60 -2
            | Some pp when pp >= 15 -> 1, rng
            | _ -> 0, rng
        | "EFFECT_HEAL_BELL" ->
            let afflicted = context.EnemyTeam |> List.exists (fun mon -> mon.Status <> Healthy)
            if not afflicted then 10, rng
            elif user.Status = Freeze || isAsleep user then randomAdjustment50 -2
            elif user.Status <> Healthy then -1, rng
            else 0, rng
        | "EFFECT_PRIORITY_HIT" when BattleMon.effectiveSpeed user < BattleMon.effectiveSpeed target && estimatedDamage user target move >= target.Hp -> -3, rng
        | "EFFECT_THIEF" -> 30, rng
        | "EFFECT_MEAN_LOOK" when atMostHalf user || (context.PlayerTeam |> List.filter (BattleMon.isFainted >> not) |> List.length <= 1) -> 10, rng
        | "EFFECT_MEAN_LOOK" when target.Volatile.CantEscape -> 1, rng
        | "EFFECT_CURSE" when user.Species.Type1 = TypeChart.value "GHOST" || user.Species.Type2 = TypeChart.value "GHOST" ->
            if atMostQuarter user || target.Volatile.Curse then 10, rng
            elif atMostHalf user then 1, rng
            elif context.PlayerTurnsTaken = 0 then randomAdjustment50 -2
            else 0, rng
        | "EFFECT_CURSE" when atMostHalf user || user.AtkStage >= 3 -> 1, rng
        | "EFFECT_PERISH_SONG" when context.EnemyTeam |> List.filter (BattleMon.isFainted >> not) |> List.length < 2 -> 5, rng
        | "EFFECT_PERISH_SONG" when target.Volatile.CantEscape -> randomAdjustment50 -2
        | "EFFECT_PERISH_SONG" -> randomAdjustment50 1
        | "EFFECT_NIGHTMARE" -> randomAdjustment50 -1
        | "EFFECT_FLAME_WHEEL" | "EFFECT_SACRED_FIRE" when target.Status = Freeze -> -5, rng
        | "EFFECT_PROTECT" | "EFFECT_ENDURE" when user.Volatile.ProtectCount > 0 -> 3, rng
        | "EFFECT_FORESIGHT" when target.Species.Type1 = TypeChart.value "GHOST" || target.Species.Type2 = TypeChart.value "GHOST" -> randomAdjustment 60 -2
        | "EFFECT_SANDSTORM" when target.Species.Type1 = TypeChart.value "ROCK" || target.Species.Type1 = TypeChart.value "GROUND" || target.Species.Type1 = TypeChart.value "STEEL" -> 2, rng
        | "EFFECT_SANDSTORM" when atMostHalf target -> 1, rng
        | "EFFECT_ENDURE" when atFullHealth user || not (atMostQuarter user) -> 2, rng
        | "EFFECT_ROLLOUT" | "EFFECT_FURY_CUTTER" when atMostQuarter user || user.Status = Paralysis || user.Volatile.Confusion.IsSome -> randomAdjustment 80 1
        | "EFFECT_SWAGGER" | "EFFECT_ATTRACT" when context.PlayerTurnsTaken = 0 -> randomAdjustment80 -1
        | "EFFECT_SWAGGER" | "EFFECT_ATTRACT" -> randomAdjustment80 1
        | "EFFECT_SAFEGUARD" when atMostHalf target -> randomAdjustment80 1
        | "EFFECT_MAGNITUDE" | "EFFECT_EARTHQUAKE" when targetIsCharging target (Set.ofList [ "EFFECT_DIG" ]) && isFaster user target -> -2, rng
        | "EFFECT_BATON_PASS" when not (hasSuperEffectiveMove target user) -> 1, rng
        | "EFFECT_PURSUIT" when atMostQuarter target -> randomAdjustment50 -2
        | "EFFECT_PURSUIT" -> randomAdjustment80 1
        | "EFFECT_RAPID_SPIN" when user.Volatile.Trapped.IsSome || user.Volatile.LeechSeed || context.EnemySide.Spikes > 0 -> randomAdjustment80 -2
        | "EFFECT_HIDDEN_POWER" ->
            let effectiveness = Damage.effectivenessTimesTen move target
            if effectiveness < 10 then 1, rng elif effectiveness > 10 then -1, rng else 0, rng
        | "EFFECT_RAIN_DANCE" when target.Species.Type1 = TypeChart.value "WATER" || target.Species.Type2 = TypeChart.value "WATER" -> 3, rng
        | "EFFECT_SUNNY_DAY" when target.Species.Type1 = TypeChart.value "FIRE" || target.Species.Type2 = TypeChart.value "FIRE" -> 3, rng
        | "EFFECT_BELLY_DRUM" when user.AtkStage >= 3 || atMostHalf user -> 5, rng
        | "EFFECT_BELLY_DRUM" when not (atFullHealth user) -> 1, rng
        | "EFFECT_PSYCH_UP" when (statStages user |> List.sum) >= (statStages target |> List.sum) -> 1, rng
        | "EFFECT_TWISTER" | "EFFECT_GUST" when targetIsCharging target (Set.ofList [ "EFFECT_FLY" ]) && isFaster user target -> -2, rng
        | "EFFECT_FUTURE_SIGHT" when targetIsCharging target (Set.ofList [ "EFFECT_FLY"; "EFFECT_DIG" ]) && isFaster user target -> -2, rng
        | "EFFECT_STOMP" when target.Volatile.Minimized -> randomAdjustment80 -1
        | "EFFECT_SOLARBEAM" when context.WeatherType = Some "SUN" -> randomAdjustment 80 -2
        | "EFFECT_SOLARBEAM" when context.WeatherType = Some "RAIN" -> randomAdjustment 90 2
        | "EFFECT_THUNDER" when context.WeatherType = Some "SUN" -> randomAdjustment 90 1
        | _ -> 0, rng

    let private applySmart (context: AiContext) (user: BattleMon) (target: BattleMon) (scores: int array) (rng: Rng) =
        let mutable nextRng = rng
        user.Moves
        |> List.iteri (fun index move ->
            let adjustment, afterAdjustment = smartAdjustment context user target move nextRng
            nextRng <- afterAdjustment
            scores.[index] <- scores.[index] + adjustment)
        nextRng

    let private applyOpportunist (user: BattleMon) (scores: int array) (rng: Rng) =
        if not (atMostHalf user) then rng
        else
            let mutable nextRng = rng
            let discourage =
                if atMostQuarter user then true
                else
                    let selected, afterRoll = chance50 nextRng
                    nextRng <- afterRoll
                    selected
            if discourage then
                user.Moves |> List.iteri (fun index move -> if Set.contains move.Name stallMoves then scores.[index] <- scores.[index] + 1)
            nextRng

    let private applyAggressive (user: BattleMon) (target: BattleMon) (scores: int array) =
        let damages = user.Moves |> List.map (estimatedDamage user target)
        let strongest = damages |> List.max
        if strongest > 0 then
            user.Moves
            |> List.iteri (fun index move ->
                if damages.[index] > 0 && damages.[index] < strongest && not (Set.contains move.Effect recklessEffects) then
                    scores.[index] <- scores.[index] + 1)

    let private applyCautious (context: AiContext) (user: BattleMon) (scores: int array) (rng: Rng) =
        if context.EnemyTurnsTaken = 0 then rng
        else
            let mutable nextRng = rng
            user.Moves
            |> List.iteri (fun index move ->
                if Set.contains move.Name residualMoves then
                    let discourage, afterRoll = chancePercent 90 nextRng
                    nextRng <- afterRoll
                    if discourage then scores.[index] <- scores.[index] + 1)
            nextRng

    let private applyStatus (user: BattleMon) (target: BattleMon) (scores: int array) =
        let poisonType = TypeChart.value "POISON"
        user.Moves
        |> List.iteri (fun index move ->
            let poisonImmune =
                (move.Effect = "EFFECT_TOXIC" || move.Effect = "EFFECT_POISON")
                && (target.Species.Type1 = poisonType || target.Species.Type2 = poisonType)
            let typeImmune =
                (move.Effect = "EFFECT_SLEEP" || move.Effect = "EFFECT_PARALYZE" || move.Power > 0)
                && Damage.effectivenessTimesTen move target = 0
            if poisonImmune || typeImmune then scores.[index] <- scores.[index] + 10)

    let private applyRisky (user: BattleMon) (target: BattleMon) (scores: int array) (rng: Rng) =
        let mutable nextRng = rng
        user.Moves
        |> List.iteri (fun index move ->
            if move.Power > 0 then
                let riskyAtFullHealth = Set.contains move.Effect riskyEffects && atFullHealth user
                if not riskyAtFullHealth then
                    let adjustment = if estimatedDamage user target move >= target.Hp then -5 else 0
                    scores.[index] <- scores.[index] + adjustment)
        nextRng

    let private sourceScores (profile: TrainerProfile) (context: AiContext) (user: BattleMon) (target: BattleMon) (rng: Rng) =
        let hasTrackedPp = not user.Pp.IsEmpty
        let available =
            user.Moves
            |> List.mapi (fun index _ -> index)
            |> List.filter (fun index -> not hasTrackedPp || (index < user.Pp.Length && user.Pp.[index] > 0))
        if available.IsEmpty then None, rng
        else
            let scores =
                user.Moves
                |> List.mapi (fun index _ -> if user.Volatile.DisabledMoveIndex = Some index then 80 else 20)
                |> List.toArray
            let mutable nextRng = rng
            for flag in sourceLayerOrder do
                if List.contains flag profile.MoveFlags then
                    match flag with
                    | "AI_BASIC" -> applyBasic context user target scores
                    | "AI_SETUP" -> nextRng <- applySetup context user scores nextRng
                    | "AI_TYPES" -> applyTypes user target scores
                    | "AI_OFFENSIVE" -> applyOffensive user scores
                    | "AI_SMART" -> nextRng <- applySmart context user target scores nextRng
                    | "AI_OPPORTUNIST" -> nextRng <- applyOpportunist user scores nextRng
                    | "AI_AGGRESSIVE" -> applyAggressive user target scores
                    | "AI_CAUTIOUS" -> nextRng <- applyCautious context user scores nextRng
                    | "AI_STATUS" -> applyStatus user target scores
                    | "AI_RISKY" -> nextRng <- applyRisky user target scores nextRng
                    | _ -> ()
            Some(scores, available), nextRng

    let chooseMoveWithProfileAndRng (profile: TrainerProfile) (context: AiContext) (user: BattleMon) (target: BattleMon) (rng: Rng) =
        match sourceScores profile context user target rng with
        | None, nextRng -> None, nextRng
        | Some(scores, available), nextRng ->
            let lowestScore = available |> List.map (fun index -> scores.[index]) |> List.min
            let tied = available |> List.filter (fun index -> scores.[index] = lowestScore)
            let rec chooseTied tiedRng =
                let roll, afterRoll = Rng.next tiedRng
                let index = roll &&& 3
                if List.contains index tied then index, afterRoll
                else chooseTied afterRoll
            let index, finalRng = if tied.Length = 1 then tied.Head, nextRng else chooseTied nextRng
            Some(user.Moves.[index], index), finalRng

    let scoreMoveWithProfile profile enemyTurnsTaken (user: BattleMon) (target: BattleMon) (move: MoveData) =
        let probe = { user with Moves = [ move ]; Pp = [ move.Pp ] }
        match sourceScores profile (defaultContext enemyTurnsTaken) probe target (Rng.create 0u) with
        | Some(scores, _), _ -> scores.[0]
        | None, _ -> 80

    let scoreMove user target move = scoreMoveWithProfile emptyProfile 0 user target move

    let chooseMoveWithProfile (profile: TrainerProfile) enemyTurnsTaken (user: BattleMon) (target: BattleMon) =
        chooseMoveWithProfileAndRng profile (defaultContext enemyTurnsTaken) user target (Rng.create 0u)
        |> fst

    let chooseMove (user: BattleMon) (target: BattleMon) = chooseMoveWithProfile emptyProfile 0 user target

    let private canSwitch (active: BattleMon) =
        not (BattleMon.isFainted active)
        && active.Volatile.Trapped.IsNone
        && not active.Volatile.CantEscape
        && active.Volatile.Charging.IsNone
        && not active.Volatile.Recharge
        && active.Volatile.Rampage.IsNone
        && active.Volatile.BideTurns.IsNone

    let private switchPolicy (profile: TrainerProfile) =
        if List.contains "SWITCH_OFTEN" profile.ItemSwitchFlags then Some "SWITCH_OFTEN"
        elif List.contains "SWITCH_RARELY" profile.ItemSwitchFlags then Some "SWITCH_RARELY"
        elif List.contains "SWITCH_SOMETIMES" profile.ItemSwitchFlags then Some "SWITCH_SOMETIMES"
        else None

    let private switchThreshold policy tier =
        match policy, tier with
        | "SWITCH_OFTEN", 0x10 -> percentThreshold 50 + 1
        | "SWITCH_OFTEN", 0x20 -> percentThreshold 79 - 1
        | "SWITCH_OFTEN", _ -> percentThreshold 4
        | "SWITCH_RARELY", 0x10 -> percentThreshold 8
        | "SWITCH_RARELY", 0x20 -> percentThreshold 12
        | "SWITCH_RARELY", _ -> percentThreshold 79 - 1
        | "SWITCH_SOMETIMES", 0x10 -> percentThreshold 20 - 1
        | "SWITCH_SOMETIMES", 0x20 -> percentThreshold 50 + 1
        | "SWITCH_SOMETIMES", _ -> percentThreshold 20 - 1
        | _ -> 0

    let private sourceSwitchCandidate (context: AiContext) (active: BattleMon) (target: BattleMon) (team: BattleMon list) =
        let playerLastMove = target.Volatile.LastCounterMove
        let eligibleBench =
            team
            |> List.mapi (fun index mon -> index, mon)
            |> List.filter (fun (index, mon) -> index > 0 && not (BattleMon.isFainted mon) && not (atMostQuarter mon))
        let hasSuperEffectiveMove (mon: BattleMon) = mon.Moves |> List.exists (fun move -> move.Power > 0 && Damage.effectivenessTimesTen move target > 10)
        let resistsLastMove (mon: BattleMon) =
            match playerLastMove with
            | Some move -> Damage.effectivenessTimesTen move mon < 10
            | None -> false
        let resistsPlayerType (mon: BattleMon) =
            TypeChart.multiplier target.Species.Type1 mon.Species.Type1 < TypeChart.Neutral
            || TypeChart.multiplier target.Species.Type1 mon.Species.Type2 < TypeChart.Neutral
        let perishCandidate =
            eligibleBench
            |> List.tryFind (fun (_, mon) -> hasSuperEffectiveMove mon && (resistsLastMove mon || resistsPlayerType mon))
        let activeWalled =
            active.Moves
            |> List.filter (fun move -> move.Power > 0)
            |> List.forall (fun move -> Damage.effectivenessTimesTen move target = 0)
        let playerPressuresActive =
            match playerLastMove with
            | Some move -> Damage.effectivenessTimesTen move active > 10
            | None -> target.Moves |> List.exists (fun move -> move.Power > 0 && Damage.effectivenessTimesTen move active > 10)
        if context.EnemySide.PerishCounter = Some 1 then
            perishCandidate |> Option.map (fun (index, _) -> index, 0x30)
        elif not (atMostQuarter active || activeWalled || playerPressuresActive) then None
        else
            eligibleBench
            |> List.map (fun (index, mon) ->
                let hasSuperEffectiveMove = hasSuperEffectiveMove mon
                let resistsLastMove = resistsLastMove mon
                let tier = if hasSuperEffectiveMove && resistsLastMove then 0x10 elif hasSuperEffectiveMove || resistsLastMove then 0x20 else 0x30
                index, tier, hasSuperEffectiveMove, resistsLastMove)
            |> List.sortByDescending (fun (_, tier, hasSuperEffectiveMove, resistsLastMove) -> hasSuperEffectiveMove, resistsLastMove, -tier)
            |> List.tryHead
            |> Option.map (fun (index, tier, _, _) -> index, tier)

    let chooseSwitchWithProfileAndRng (profile: TrainerProfile) (context: AiContext) (active: BattleMon) (target: BattleMon) (team: BattleMon list) (rng: Rng) =
        match switchPolicy profile, sourceSwitchCandidate context active target team with
        | Some policy, Some(index, tier) when canSwitch active && not target.Volatile.CantEscape ->
            let shouldSwitch, nextRng = rollBelow (switchThreshold policy tier) rng
            (if shouldSwitch then Some index else None), nextRng
        | _ -> None, rng

    let chooseSwitchWithProfile (profile: TrainerProfile) enemyTurnsTaken (active: BattleMon) (target: BattleMon) (team: BattleMon list) =
        chooseSwitchWithProfileAndRng profile (defaultContext enemyTurnsTaken) active target team (Rng.create 0u)
        |> fst

    let chooseSwitch (active: BattleMon) (target: BattleMon) (team: BattleMon list) = chooseSwitchWithProfile emptyProfile 0 active target team

    let private sourceItemPriority =
        [ "FULL_RESTORE"; "MAX_POTION"; "HYPER_POTION"; "SUPER_POTION"; "POTION"
          "X_ACCURACY"; "FULL_HEAL"; "GUARD_SPEC"; "DIRE_HIT"; "X_ATTACK"
          "X_DEFEND"; "X_SPEED"; "X_SPECIAL" ]

    let private sourceHighestLevel (team: BattleMon list) = team |> List.map (fun mon -> mon.Level) |> List.max

    let private clearAiItemVolatile (enemy: BattleMon) =
        { enemy with
            Volatile =
                { enemy.Volatile with
                    BideTurns = None
                    BideDamage = 0
                    Rage = false
                    RageCounter = 0
                    ProtectCount = 0 } }

    let private applyTrainerItem item (enemy: BattleMon) =
        let updated =
            match item with
            | "FULL_RESTORE" -> Some { enemy with Hp = enemy.MaxHp; Status = Healthy; Volatile = { enemy.Volatile with Confusion = None } }
            | "MAX_POTION" -> Some { enemy with Hp = enemy.MaxHp }
            | "HYPER_POTION" -> Some { enemy with Hp = min enemy.MaxHp (enemy.Hp + 200) }
            | "SUPER_POTION" -> Some { enemy with Hp = min enemy.MaxHp (enemy.Hp + 50) }
            | "POTION" -> Some { enemy with Hp = min enemy.MaxHp (enemy.Hp + 20) }
            | "FULL_HEAL" -> Some { enemy with Status = Healthy }
            | "X_ACCURACY" -> Some { enemy with Volatile = { enemy.Volatile with XAccuracy = true } }
            | "GUARD_SPEC" -> Some { enemy with Volatile = { enemy.Volatile with Mist = true } }
            | "DIRE_HIT" -> Some { enemy with Volatile = { enemy.Volatile with FocusEnergy = true } }
            | "X_ATTACK" -> Some { enemy with AtkStage = min 6 (enemy.AtkStage + 1) }
            | "X_DEFEND" -> Some { enemy with DefStage = min 6 (enemy.DefStage + 1) }
            | "X_SPEED" -> Some { enemy with SpdStage = min 6 (enemy.SpdStage + 1) }
            | "X_SPECIAL" -> Some { enemy with SpAtkStage = min 6 (enemy.SpAtkStage + 1) }
            | _ -> None
        updated |> Option.map clearAiItemVolatile

    let private hasItemFlag flag (profile: TrainerProfile) = List.contains flag profile.ItemSwitchFlags

    let private canUseStatusItem (profile: TrainerProfile) (enemy: BattleMon) (rng: Rng) =
        match enemy.Status with
        | Healthy -> false, rng
        | BadPoison counter when hasItemFlag "CONTEXT_USE" profile ->
            if counter < 4 then false, rng else chance50 rng
        | Freeze
        | Sleep _ when hasItemFlag "CONTEXT_USE" profile -> true, rng
        | _ when hasItemFlag "CONTEXT_USE" profile -> false, rng
        | _ when hasItemFlag "ALWAYS_USE" profile -> true, rng
        | _ -> chance20 rng

    let private canUseHealingItem (profile: TrainerProfile) (enemy: BattleMon) (rng: Rng) =
        if enemy.Hp >= enemy.MaxHp || not (atMostHalf enemy) then false, rng
        elif hasItemFlag "CONTEXT_USE" profile then
            if atMostQuarter enemy then true, rng else chance20 rng
        elif hasItemFlag "UNKNOWN_USE" profile then
            if not (atMostQuarter enemy) then false, rng
            else
                let wouldDecline, nextRng = chance20 rng
                not wouldDecline, nextRng
        elif atMostQuarter enemy then true, rng
        else chance50 rng

    let private canUseXItem (profile: TrainerProfile) enemyTurnsTaken (rng: Rng) =
        if enemyTurnsTaken = 0 then
            if hasItemFlag "ALWAYS_USE" profile then true, rng
            else
                let rejected, afterFirstRoll = chance50 rng
                if rejected then false, afterFirstRoll
                elif hasItemFlag "CONTEXT_USE" profile then true, afterFirstRoll
                else
                    let rejectedAgain, finalRng = chance50 afterFirstRoll
                    not rejectedAgain, finalRng
        elif hasItemFlag "ALWAYS_USE" profile then chance20 rng
        else false, rng

    let tryUseItemWithRng (profile: TrainerProfile) (remainingItems: string list) (enemyTeam: BattleMon list) (enemy: BattleMon) enemyTurnsTaken (rng: Rng) =
        if remainingItems.IsEmpty || enemy.Level <> sourceHighestLevel enemyTeam then None, rng
        else
            let rec choose items nextRng =
                match items with
                | [] -> None, nextRng
                | item :: rest when not (List.contains item remainingItems) -> choose rest nextRng
                | item :: rest ->
                    let useItem, afterDecision =
                        match item with
                        | "FULL_HEAL" -> canUseStatusItem profile enemy nextRng
                        | "FULL_RESTORE" ->
                            let heal, afterHeal = canUseHealingItem profile enemy nextRng
                            if heal then true, afterHeal
                            elif hasItemFlag "CONTEXT_USE" profile then canUseStatusItem profile enemy afterHeal
                            else false, afterHeal
                        | "MAX_POTION" | "HYPER_POTION" | "SUPER_POTION" | "POTION" -> canUseHealingItem profile enemy nextRng
                        | _ -> canUseXItem profile enemyTurnsTaken nextRng
                    if useItem then
                        applyTrainerItem item enemy |> Option.map (fun updated -> { Item = item; Enemy = updated }), afterDecision
                    else
                        choose rest afterDecision
            choose sourceItemPriority rng

    let tryUseItem (profile: TrainerProfile) (remainingItems: string list) (enemyTeam: BattleMon list) (enemy: BattleMon) enemyTurnsTaken =
        tryUseItemWithRng profile remainingItems enemyTeam enemy enemyTurnsTaken (Rng.create 0u)
        |> fst
