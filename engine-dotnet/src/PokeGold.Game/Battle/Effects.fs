namespace PokeGold.Game.Battle

open PokeGold.Game.Data

// ---------------------------------------------------------------------------
//  Effects.fs -- effect dispatch + interpreter
//
//  The `Stat` and `EffectCommand` DUs live in EffectCommand.fs so that
//  parallel family slices (M13.5-M13.8) can add cases with minimal merge
//  contention. This file owns `forMove` (mapping EFFECT_* strings to command
//  lists) and `apply` (interpreting one command).
// ---------------------------------------------------------------------------

module Effects =

    /// Read a mon's current stage for a stat.
    let stage (s: Stat) (m: BattleMon) : int =
        match s with
        | Attack -> m.AtkStage
        | Defense -> m.DefStage
        | Speed -> m.SpdStage
        | SpAttack -> m.SpAtkStage
        | SpDefense -> m.SpDefStage
        | Accuracy -> m.AccStage
        | Evasion -> m.EvaStage

    /// Return a mon with the given stat's stage shifted by `delta`, clamped to
    /// the −6..+6 range the hardware enforces.
    let shiftStage (s: Stat) (delta: int) (m: BattleMon) : BattleMon =
        let clamp v = max -6 (min 6 v)

        match s with
        | Attack -> { m with AtkStage = clamp (m.AtkStage + delta) }
        | Defense -> { m with DefStage = clamp (m.DefStage + delta) }
        | Speed -> { m with SpdStage = clamp (m.SpdStage + delta) }
        | SpAttack -> { m with SpAtkStage = clamp (m.SpAtkStage + delta) }
        | SpDefense -> { m with SpDefStage = clamp (m.SpDefStage + delta) }
        | Accuracy -> { m with AccStage = clamp (m.AccStage + delta) }
        | Evasion -> { m with EvaStage = clamp (m.EvaStage + delta) }

    let private statName =
        function
        | Attack -> "ATTACK"
        | Defense -> "DEFENSE"
        | Speed -> "SPEED"
        | SpAttack -> "SPECIAL ATK"
        | SpDefense -> "SPECIAL DEF"
        | Accuracy -> "ACCURACY"
        | Evasion -> "EVASION"

    let private oppositeGender (user: BattleMon) (foe: BattleMon) : bool =
        match user.Gender, foe.Gender with
        | Male, Female -> true
        | Female, Male -> true
        | _ -> false

    /// Map a move's effect constant to its command sequence. Damaging moves
    /// with no special effect are a single `Damage`; the recognised stat moves
    /// drop the target's stat. Unknown effects fall back to `Damage` when the
    /// move has power, otherwise do nothing.

    let forMove (move: MoveData) : EffectCommand list =
        match move.Effect with
        | "EFFECT_NORMAL_HIT" -> [ Damage ]
        | "EFFECT_RECOIL_HIT" -> [ Damage; Recoil ]
        | "EFFECT_ATTACK_DOWN" -> [ LowerTargetStat Attack ]
        | "EFFECT_DEFENSE_DOWN" -> [ LowerTargetStat Defense ]
        | "EFFECT_SPEED_DOWN" -> [ LowerTargetStat Speed ]
        | "EFFECT_SP_ATK_DOWN" -> [ LowerTargetStat SpAttack ]
        | "EFFECT_SP_DEF_DOWN" -> [ LowerTargetStat SpDefense ]
        | "EFFECT_ACCURACY_DOWN" -> [ LowerTargetStat Accuracy ]
        | "EFFECT_EVASION_DOWN" -> [ LowerTargetStat Evasion ]
        | "EFFECT_ATTACK_UP" -> [ RaiseUserStat Attack ]
        | "EFFECT_DEFENSE_UP" -> [ RaiseUserStat Defense ]
        | "EFFECT_SP_ATK_UP" -> [ RaiseUserStat SpAttack ]
        | "EFFECT_SP_DEF_UP" -> [ RaiseUserStat SpDefense ]
        | "EFFECT_ACCURACY_UP" -> [ RaiseUserStat Accuracy ]
        | "EFFECT_EVASION_UP" -> [ RaiseUserStat Evasion ]
        | "EFFECT_ATTACK_DOWN_2" -> [ LowerTargetStat Attack; LowerTargetStat Attack ]
        | "EFFECT_DEFENSE_DOWN_2" -> [ LowerTargetStat Defense; LowerTargetStat Defense ]
        | "EFFECT_SPEED_DOWN_2" -> [ LowerTargetStat Speed; LowerTargetStat Speed ]
        | "EFFECT_SP_ATK_DOWN_2" -> [ LowerTargetStat SpAttack; LowerTargetStat SpAttack ]
        | "EFFECT_SP_DEF_DOWN_2" -> [ LowerTargetStat SpDefense; LowerTargetStat SpDefense ]
        | "EFFECT_ATTACK_UP_2" -> [ RaiseUserStat Attack; RaiseUserStat Attack ]
        | "EFFECT_DEFENSE_UP_2" -> [ RaiseUserStat Defense; RaiseUserStat Defense ]
        | "EFFECT_SPEED_UP_2" -> [ RaiseUserStat Speed; RaiseUserStat Speed ]
        | "EFFECT_SP_ATK_UP_2" -> [ RaiseUserStat SpAttack; RaiseUserStat SpAttack ]
        | "EFFECT_SP_DEF_UP_2" -> [ RaiseUserStat SpDefense; RaiseUserStat SpDefense ]
        | "EFFECT_SLEEP" -> [ InflictSleep ]
        | "EFFECT_POISON" -> [ InflictPoison ]
        | "EFFECT_BURN" -> [ InflictBurn ]
        | "EFFECT_FREEZE" -> [ InflictFreeze ]
        | "EFFECT_TOXIC" -> [ InflictToxic ]
        | "EFFECT_PARALYZE" -> [ InflictParalyze ]
        | "EFFECT_CONFUSE" -> [ InflictConfuse ]
        | "EFFECT_LEECH_SEED" -> [ ApplyLeechSeed ]
        | "EFFECT_TRAP_TARGET" -> [ Damage; TrapTarget ]
        | "EFFECT_SUBSTITUTE" -> [ CreateSubstitute ]
        | "EFFECT_MIST" -> [ SetMist ]
        | "EFFECT_FOCUS_ENERGY" -> [ SetFocusEnergy ]
        | "EFFECT_MEAN_LOOK" -> [ SetMeanLook ]
        | "EFFECT_FLINCH_HIT" -> [ Damage; EffectChance SetFlinch ]
        | "EFFECT_CONFUSE_HIT" -> [ Damage; EffectChance InflictConfuse ]
        | "EFFECT_POISON_HIT" -> [ Damage; EffectChance InflictPoison ]
        | "EFFECT_BURN_HIT" -> [ Damage; EffectChance InflictBurn ]
        | "EFFECT_PARALYZE_HIT" -> [ Damage; EffectChance InflictParalyze ]
        | "EFFECT_FREEZE_HIT" -> [ Damage; EffectChance InflictFreeze ]
        | "EFFECT_ATTACK_DOWN_HIT" -> [ Damage; EffectChance (LowerTargetStat Attack) ]
        | "EFFECT_DEFENSE_DOWN_HIT" -> [ Damage; EffectChance (LowerTargetStat Defense) ]
        | "EFFECT_SPEED_DOWN_HIT" -> [ Damage; EffectChance (LowerTargetStat Speed) ]
        | "EFFECT_SP_ATK_DOWN_HIT" -> [ Damage; EffectChance (LowerTargetStat SpAttack) ]
        | "EFFECT_SP_DEF_DOWN_HIT" -> [ Damage; EffectChance (LowerTargetStat SpDefense) ]
        | "EFFECT_ACCURACY_DOWN_HIT" -> [ Damage; EffectChance (LowerTargetStat Accuracy) ]
        | "EFFECT_EVASION_DOWN_HIT" -> [ Damage; EffectChance (LowerTargetStat Evasion) ]
        | "EFFECT_ATTACK_UP_HIT" -> [ Damage; EffectChance (RaiseUserStat Attack) ]
        | "EFFECT_DEFENSE_UP_HIT" -> [ Damage; EffectChance (RaiseUserStat Defense) ]
        | "EFFECT_SP_ATK_UP_HIT" -> [ Damage; EffectChance (RaiseUserStat SpAttack) ]
        | "EFFECT_SP_DEF_UP_HIT" -> [ Damage; EffectChance (RaiseUserStat SpDefense) ]
        | "EFFECT_ACCURACY_UP_HIT" -> [ Damage; EffectChance (RaiseUserStat Accuracy) ]
        | "EFFECT_EVASION_UP_HIT" -> [ Damage; EffectChance (RaiseUserStat Evasion) ]
        | "EFFECT_ATTRACT" -> [ InflictAttract ]
        // --- M13.5: damage-shaping & fixed damage family ---
        | "EFFECT_LEVEL_DAMAGE"   -> [ LevelDamage ]
        | "EFFECT_PSYWAVE"        -> [ PsywaveDamage ]
        | "EFFECT_SUPER_FANG"     -> [ SuperFangDamage ]
        | "EFFECT_STATIC_DAMAGE"  -> [ StaticDamage ]
        | "EFFECT_OHKO"           -> [ OhkoDamage ]
        | "EFFECT_FALSE_SWIPE"    -> [ FalseSwipeDamage ]
        | "EFFECT_REVERSAL"       -> [ ReversalDamage ]
        | "EFFECT_RETURN"         -> [ ReturnDamage ]
        | "EFFECT_FRUSTRATION"    -> [ FrustrationDamage ]
        | "EFFECT_PRESENT"        -> [ PresentDamage ]
        | "EFFECT_MAGNITUDE"      -> [ MagnitudeDamage ]
        | "EFFECT_HIDDEN_POWER"   -> [ HiddenPowerDamage ]
        | "EFFECT_FURY_CUTTER"    -> [ FuryCutterDamage ]
        | "EFFECT_ROLLOUT"        -> [ RolloutDamage ]
        | "EFFECT_TRIPLE_KICK"    -> [ TripleKickDamage ]
        | "EFFECT_BEAT_UP"        -> [ BeatUpDamage ]
        | "EFFECT_LEECH_HIT"     -> [ DrainDamage ]
        | "EFFECT_DREAM_EATER"   -> [ DreamEaterDamage ]
        | "EFFECT_SELFDESTRUCT"  -> [ SelfdestructDamage ]
        | "EFFECT_JUMP_KICK"     -> [ JumpKickDamage ]
        | "EFFECT_PAY_DAY"       -> [ PayDayDamage ]
        | "EFFECT_RAPID_SPIN"    -> [ RapidSpinDamage ]
        | "EFFECT_THIEF"         -> [ ThiefDamage ]
        | "EFFECT_RAGE"          -> [ RageDamage ]
        | "EFFECT_MULTI_HIT"     -> [ MultiHitDamage ]
        | "EFFECT_DOUBLE_HIT"    -> [ DoubleHitDamage ]
        | "EFFECT_POISON_MULTI_HIT" -> [ PoisonMultiHitDamage ]
        | "EFFECT_GUST"          -> [ ConditionalDoubleDamage ]
        | "EFFECT_TWISTER"       -> [ ConditionalDoubleDamage ]
        | "EFFECT_STOMP"         -> [ ConditionalDoubleDamage ]
        | "EFFECT_EARTHQUAKE"    -> [ ConditionalDoubleDamage ]
        | _ -> if move.Power > 0 then [ Damage ] else []

    /// Apply one effect command to a MoveContext. Returns the updated context
    /// with user/foe/messages/lastDamage modified as needed.
    let rec applyCtx (ctx: MoveContext) (cmd: EffectCommand) : MoveContext =
        match cmd with
        | Damage ->
            let dmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            // Substitute absorbs damage (effect_commands.asm CheckSubstitute).
            let foe, subBroke =
                match ctx.Foe.Volatile.Substitute with
                | Some subHp ->
                    let remaining = subHp - dmg
                    if remaining <= 0 then
                        let vol = { ctx.Foe.Volatile with Substitute = None }
                        { ctx.Foe with Volatile = vol }, true
                    else
                        let vol = { ctx.Foe.Volatile with Substitute = Some remaining }
                        { ctx.Foe with Volatile = vol }, false
                | None ->
                    { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }, false

            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen ctx.Move foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> ()
                  if subBroke then $"{foe.Species.Name}'s substitute faded!" ]

            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | Recoil ->
            // Recoil = 1/4 of damage dealt, min 1 HP.
            // effect_commands.asm BattleCommand_Recoil: srl b; rr c; srl b; rr c
            let recoil = max 1 (ctx.LastDamage / 4)
            let user = { ctx.User with Hp = max 0 (ctx.User.Hp - recoil) }
            let notes = [ $"{ctx.User.Species.Name}'s hit with recoil!" ]
            { ctx with User = user; Messages = ctx.Messages @ notes }

        | LowerTargetStat s ->
            // Blocked by Mist (move_effects/mist.asm).
            if ctx.Foe.Volatile.Mist then
                { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} is protected by mist!" ] }
            // Blocked by Substitute.
            elif ctx.Foe.Volatile.Substitute.IsSome then
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            elif stage s ctx.Foe <= -6 then
                { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name}'s {statName s} won't go lower!" ] }
            else
                let foe = shiftStage s -1 ctx.Foe
                { ctx with Foe = foe; Messages = ctx.Messages @ [ $"{foe.Species.Name}'s {statName s} fell!" ] }

        | RaiseUserStat s ->
            if stage s ctx.User >= 6 then
                { ctx with Messages = ctx.Messages @ [ $"{ctx.User.Species.Name}'s {statName s} won't go higher!" ] }
            else
                let user = shiftStage s 1 ctx.User
                { ctx with User = user; Messages = ctx.Messages @ [ $"{user.Species.Name}'s {statName s} rose!" ] }

        | InflictSleep ->
            // BattleCommand_SleepTarget (effect_commands.asm l.3552).
            // Fails if: foe already asleep, foe already has any status, attack missed.
            // Blocked by substitute.
            match ctx.Foe.Volatile.Substitute with
            | Some _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            | None ->
            match ctx.Foe.Status with
            | Sleep _ ->
                { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} is already asleep!" ] }
            | Healthy ->
                // Random sleep counter: 2-7 turns, faithful to the rejection loop.
                // BattleRandom AND SLP_MASK (0-7); reject 0 and 7; inc -> 2-7.
                let rec sleepLoop rng =
                    let v, rng' = Rng.next rng
                    let masked = v &&& 7
                    if masked = 0 || masked = 7 then sleepLoop rng'
                    else (masked + 1), rng'
                let turns, rng' = sleepLoop ctx.Rng
                let foe = { ctx.Foe with Status = Sleep turns }
                { ctx with Foe = foe; Rng = rng'; Messages = ctx.Messages @ [ $"{foe.Species.Name} fell asleep!" ] }
            | _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }

        | InflictPoison ->
            // BattleCommand_Poison (effect_commands.asm l.3672).
            // Immune: Poison-type, already has a status.
            // Blocked by substitute.
            match ctx.Foe.Volatile.Substitute with
            | Some _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            | None ->
            let poisonType = TypeChart.value "POISON"
            if ctx.Foe.Species.Type1 = poisonType || ctx.Foe.Species.Type2 = poisonType then
                { ctx with Messages = ctx.Messages @ [ $"It doesn't affect {ctx.Foe.Species.Name}..." ] }
            elif ctx.Foe.Status <> Healthy then
                match ctx.Foe.Status with
                | Poison | BadPoison _ ->
                    { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} is already poisoned!" ] }
                | _ ->
                    { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            else
                let foe = { ctx.Foe with Status = Poison }
                { ctx with Foe = foe; Messages = ctx.Messages @ [ $"{foe.Species.Name} was poisoned!" ] }

        | InflictToxic ->
            // BattleCommand_Poison with EFFECT_TOXIC path (effect_commands.asm l.3735).
            // Same immunities as Poison, but sets BadPoison with counter starting at 0.
            // Blocked by substitute.
            match ctx.Foe.Volatile.Substitute with
            | Some _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            | None ->
            let poisonType = TypeChart.value "POISON"
            if ctx.Foe.Species.Type1 = poisonType || ctx.Foe.Species.Type2 = poisonType then
                { ctx with Messages = ctx.Messages @ [ $"It doesn't affect {ctx.Foe.Species.Name}..." ] }
            elif ctx.Foe.Status <> Healthy then
                match ctx.Foe.Status with
                | Poison | BadPoison _ ->
                    { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} is already poisoned!" ] }
                | _ ->
                    { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            else
                let foe = { ctx.Foe with Status = BadPoison 0 }
                { ctx with Foe = foe; Messages = ctx.Messages @ [ $"{foe.Species.Name} was badly poisoned!" ] }

        | InflictParalyze ->
            // BattleCommand_Paralyze (effect_commands.asm l.5788).
            // Gen 2: NO electric-type immunity for paralysis.
            // Fails if: already paralyzed, already has any status.
            // Blocked by substitute.
            match ctx.Foe.Volatile.Substitute with
            | Some _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            | None ->
            match ctx.Foe.Status with
            | Paralysis ->
                { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} is already paralyzed!" ] }
            | Healthy ->
                let foe = { ctx.Foe with Status = Paralysis }
                { ctx with Foe = foe; Messages = ctx.Messages @ [ $"{foe.Species.Name} is paralyzed! It may be unable to move!" ] }
            | _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }

        | InflictBurn ->
            match ctx.Foe.Volatile.Substitute with
            | Some _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            | None ->
            match ctx.Foe.Status with
            | Burn ->
                { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} is already burned!" ] }
            | Healthy ->
                let foe = { ctx.Foe with Status = Burn }
                { ctx with Foe = foe; Messages = ctx.Messages @ [ $"{foe.Species.Name} was burned!" ] }
            | _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }

        | InflictFreeze ->
            match ctx.Foe.Volatile.Substitute with
            | Some _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            | None ->
            match ctx.Foe.Status with
            | Freeze ->
                { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} is already frozen solid!" ] }
            | Healthy ->
                let foe = { ctx.Foe with Status = Freeze }
                { ctx with Foe = foe; Messages = ctx.Messages @ [ $"{foe.Species.Name} was frozen solid!" ] }
            | _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }

        | InflictAttract ->
            if ctx.Foe.Volatile.Substitute.IsSome then
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            elif not (oppositeGender ctx.User ctx.Foe) then
                { ctx with Messages = ctx.Messages @ [ $"It doesn't affect {ctx.Foe.Species.Name}..." ] }
            elif ctx.Foe.Volatile.Attracted then
                { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} is already infatuated!" ] }
            else
                let vol = { ctx.Foe.Volatile with Attracted = true }
                let foe = { ctx.Foe with Volatile = vol }
                { ctx with Foe = foe; Messages = ctx.Messages @ [ "It fell for the charm!" ] }

        // --- M13.4: volatile status commands ---

        | InflictConfuse ->
            // BattleCommand_Confuse (effect_commands.asm l.5707).
            // Blocked by substitute. Fails if already confused.
            match ctx.Foe.Volatile.Substitute with
            | Some _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            | None ->
            if ctx.Foe.Volatile.Confusion.IsSome then
                { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} is already confused!" ] }
            else
                // 2-5 turns: BattleRandom AND %11 + 2
                let roll, rng' = Rng.next ctx.Rng
                let turns = (roll &&& 3) + 2
                let vol = { ctx.Foe.Volatile with Confusion = Some turns }
                let foe = { ctx.Foe with Volatile = vol }
                { ctx with Foe = foe; Rng = rng'; Messages = ctx.Messages @ [ $"{foe.Species.Name} became confused!" ] }

        | EffectChance cmd ->
            let roll, rng' = Rng.next ctx.Rng
            if roll < ctx.Move.EffectChance then
                applyCtx { ctx with Rng = rng' } cmd
            else
                { ctx with Rng = rng' }

        | SetFlinch ->
            // BattleCommand_FlinchTarget (effect_commands.asm l.5314).
            // Blocked by substitute, frozen/sleeping target, or if user went second.
            // The "went first" check is handled in preMoveStatusCheck;
            // here we only set the flag. The pre-move gate ignores Flinch if the
            // flinched mon moved first (see preMoveStatusCheck).
            match ctx.Foe.Volatile.Substitute with
            | Some _ -> ctx
            | None ->
            match ctx.Foe.Status with
            | Freeze | Sleep _ -> ctx
            | _ ->
                let vol = { ctx.Foe.Volatile with Flinch = true }
                let foe = { ctx.Foe with Volatile = vol }
                { ctx with Foe = foe }

        | ApplyLeechSeed ->
            // BattleCommand_LeechSeed (move_effects/leech_seed.asm).
            // Blocked by substitute, Grass-type, already seeded.
            match ctx.Foe.Volatile.Substitute with
            | Some _ ->
                { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} evaded the attack!" ] }
            | None ->
            let grassType = TypeChart.value "GRASS"
            if ctx.Foe.Species.Type1 = grassType || ctx.Foe.Species.Type2 = grassType then
                { ctx with Messages = ctx.Messages @ [ $"It doesn't affect {ctx.Foe.Species.Name}..." ] }
            elif ctx.Foe.Volatile.LeechSeed then
                { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} evaded the attack!" ] }
            else
                let vol = { ctx.Foe.Volatile with LeechSeed = true }
                let foe = { ctx.Foe with Volatile = vol }
                { ctx with Foe = foe; Messages = ctx.Messages @ [ $"{foe.Species.Name} was seeded!" ] }

        | TrapTarget ->
            // BattleCommand_TrapTarget (effect_commands.asm l.5569).
            // Blocked by substitute. Fails if already trapped.
            // Counter: BattleRandom AND %11 + 3 = 3-6 internal turns (2-5 chip turns).
            match ctx.Foe.Volatile.Substitute with
            | Some _ -> ctx
            | None ->
            if ctx.Foe.Volatile.Trapped.IsSome then ctx
            else
                let roll, rng' = Rng.next ctx.Rng
                let turns = (roll &&& 3) + 3
                let vol = { ctx.Foe.Volatile with Trapped = Some turns }
                let foe = { ctx.Foe with Volatile = vol }
                { ctx with Foe = foe; Rng = rng'; Messages = ctx.Messages @ [ $"{foe.Species.Name} was trapped!" ] }

        | CreateSubstitute ->
            // BattleCommand_Substitute (move_effects/substitute.asm).
            // Cost = MaxHP / 4. Fails if already has sub or HP <= cost.
            if ctx.User.Volatile.Substitute.IsSome then
                { ctx with Messages = ctx.Messages @ [ $"{ctx.User.Species.Name} already has a substitute!" ] }
            else
                let cost = ctx.User.MaxHp / 4
                if ctx.User.Hp <= cost then
                    { ctx with Messages = ctx.Messages @ [ "It's too weak to make a substitute!" ] }
                else
                    let user = { ctx.User with Hp = ctx.User.Hp - cost
                                               Volatile = { ctx.User.Volatile with Substitute = Some cost } }
                    // Creating a substitute breaks existing trap (faithful to disassembly l.45-56).
                    let user =
                        { user with Volatile = { user.Volatile with Trapped = None } }
                    { ctx with User = user; Messages = ctx.Messages @ [ $"{user.Species.Name} made a substitute!" ] }

        | SetMist ->
            // BattleCommand_Mist (move_effects/mist.asm).
            if ctx.User.Volatile.Mist then
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            else
                let vol = { ctx.User.Volatile with Mist = true }
                let user = { ctx.User with Volatile = vol }
                { ctx with User = user; Messages = ctx.Messages @ [ $"{user.Species.Name}'s shrouded in mist!" ] }

        | SetFocusEnergy ->
            // BattleCommand_FocusEnergy (move_effects/focus_energy.asm).
            if ctx.User.Volatile.FocusEnergy then
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            else
                let vol = { ctx.User.Volatile with FocusEnergy = true }
                let user = { ctx.User with Volatile = vol }
                { ctx with User = user; Messages = ctx.Messages @ [ $"{user.Species.Name} is getting pumped!" ] }

        | SetMeanLook ->
            // BattleCommand_MeanLook (move_effects/mean_look.asm).
            // Sets CantEscape on the target. Blocked by substitute.
            match ctx.Foe.Volatile.Substitute with
            | Some _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            | None ->
            if ctx.Foe.Volatile.CantEscape then
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }
            else
                let vol = { ctx.Foe.Volatile with CantEscape = true }
                let foe = { ctx.Foe with Volatile = vol }
                { ctx with Foe = foe; Messages = ctx.Messages @ [ $"{foe.Species.Name} can't escape now!" ] }

        // --- M13.5: damage-shaping & fixed damage family ---

        | LevelDamage ->
            // BattleCommand_ConstantDamage EFFECT_LEVEL_DAMAGE path (effect_commands.asm l.3131-3144).
            // Damage = user's level, bypasses normal damage calc entirely.
            let dmg = ctx.User.Level
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            { ctx with Foe = foe; Messages = ctx.Messages; LastDamage = dmg }

        | PsywaveDamage ->
            // BattleCommand_ConstantDamage EFFECT_PSYWAVE path (effect_commands.asm l.3163-3176).
            // max = level + level/2 = floor(level * 1.5).
            // Rejection-sample: draw byte, reject 0 and >= max, keep first valid.
            // RNG draw(s): 1+ bytes at this position.
            let maxVal = ctx.User.Level + ctx.User.Level / 2
            let rec loop rng =
                let v, rng' = Rng.next rng
                if v = 0 || v >= maxVal then loop rng'
                else v, rng'
            let dmg, rng' = loop ctx.Rng
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            { ctx with Foe = foe; Rng = rng'; LastDamage = dmg }

        | SuperFangDamage ->
            // BattleCommand_ConstantDamage EFFECT_SUPER_FANG path (effect_commands.asm l.3178-3199).
            // Damage = target HP / 2, min 1.
            let dmg = max 1 (ctx.Foe.Hp / 2)
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            { ctx with Foe = foe; LastDamage = dmg }

        | StaticDamage ->
            // BattleCommand_ConstantDamage fallthrough (effect_commands.asm l.3157-3161).
            // Damage = move's Power field (Sonicboom=20, Dragon Rage=40).
            let dmg = ctx.Move.Power
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            { ctx with Foe = foe; LastDamage = dmg }

        | OhkoDamage ->
            // BattleCommand_OHKO (effect_commands.asm l.5377-5419).
            // Fails if target level >= user level (set missed). On success, damage = 65535.
            // Accuracy: (userLevel - targetLevel) * 2 + moveAccuracy (as 0-255 byte).
            // Then normal CheckHit with that modified accuracy.
            if ctx.Foe.Level >= ctx.User.Level then
                { ctx with Messages = ctx.Messages @ [ $"{ctx.User.Species.Name}'s attack missed!" ] }
            else
                // Compute modified accuracy byte.
                let diff = ctx.User.Level - ctx.Foe.Level
                let accByte = ctx.Move.Accuracy * 255 / 100
                let modAcc = min 255 (diff * 2 + accByte)
                if modAcc >= 255 then
                    // Always hits.
                    let foe = { ctx.Foe with Hp = 0 }
                    { ctx with Foe = foe; LastDamage = ctx.Foe.Hp; Messages = ctx.Messages @ [ "It's a one-hit KO!" ] }
                else
                    let roll, rng' = Rng.next ctx.Rng
                    if roll < modAcc then
                        let foe = { ctx.Foe with Hp = 0 }
                        { ctx with Foe = foe; Rng = rng'; LastDamage = ctx.Foe.Hp; Messages = ctx.Messages @ [ "It's a one-hit KO!" ] }
                    else
                        { ctx with Rng = rng'; Messages = ctx.Messages @ [ $"{ctx.User.Species.Name}'s attack missed!" ] }

        | FalseSwipeDamage ->
            // BattleCommand_FalseSwipe (move_effects/false_swipe.asm).
            // Normal damage calc, but capped to leave target at >= 1 HP.
            let rawDmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            let dmg =
                if rawDmg >= ctx.Foe.Hp then max 0 (ctx.Foe.Hp - 1)
                else rawDmg
            let foe, subBroke =
                match ctx.Foe.Volatile.Substitute with
                | Some subHp ->
                    let remaining = subHp - dmg
                    if remaining <= 0 then
                        let vol = { ctx.Foe.Volatile with Substitute = None }
                        { ctx.Foe with Volatile = vol }, true
                    else
                        let vol = { ctx.Foe.Volatile with Substitute = Some remaining }
                        { ctx.Foe with Volatile = vol }, false
                | None ->
                    { ctx.Foe with Hp = max 1 (ctx.Foe.Hp - dmg) }, false
            let notes =
                [ if ctx.Crit && dmg < rawDmg then () // crit cancelled by false swipe cap
                  elif ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen ctx.Move foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> ()
                  if subBroke then $"{foe.Species.Name}'s substitute faded!" ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | ReversalDamage ->
            // BattleCommand_ConstantDamage EFFECT_REVERSAL path (effect_commands.asm l.3207-3287).
            // ratio = hp * 48 / maxHp. Lookup FlailReversalPower table.
            // HP_BAR_LENGTH_PX = 48. Thresholds: 1,4,9,16,32,48.
            let ratio = ctx.User.Hp * 48 / ctx.User.MaxHp
            let power =
                if ratio <= 1 then 200
                elif ratio <= 4 then 150
                elif ratio <= 9 then 100
                elif ratio <= 16 then 80
                elif ratio <= 32 then 40
                else 20
            let m = { ctx.Move with Power = power }
            let dmg = Damage.calc ctx.User ctx.Foe m ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen m foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> () ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | ReturnDamage ->
            // engine/battle/move_effects: power = max(1, friendship * 10 / 25).
            // With friendship=0, power=1 (min 1 to do something).
            let power = max 1 (ctx.Friendship * 10 / 25)
            let m = { ctx.Move with Power = power }
            let dmg = Damage.calc ctx.User ctx.Foe m ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen m foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> () ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | FrustrationDamage ->
            // power = max(1, (255 - friendship) * 10 / 25).
            let power = max 1 ((255 - ctx.Friendship) * 10 / 25)
            let m = { ctx.Move with Power = power }
            let dmg = Damage.calc ctx.User ctx.Foe m ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen m foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> () ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | PresentDamage ->
            // BattleCommand_Present (move_effects/present.asm).
            // RNG draw: 1 byte. Thresholds: <=102 → power 40, <=179 → power 80,
            // <=204 → power 120, else heal target 25% maxHP.
            // (40 percent = 102, 70 percent + 1 = 179, 80 percent = 204)
            let roll, rng' = Rng.next ctx.Rng
            if roll <= 204 then
                let power =
                    if roll <= 102 then 40
                    elif roll <= 179 then 80
                    else 120
                let m = { ctx.Move with Power = power }
                let dmg = Damage.calc ctx.User ctx.Foe m ctx.Crit ctx.Roll ctx.IsStruggle
                let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
                let notes =
                    [ if ctx.Crit then "A critical hit!"
                      if not ctx.IsStruggle then
                          match Damage.effectivenessTimesTen m foe with
                          | 0 -> $"It doesn't affect {foe.Species.Name}..."
                          | e when e > 10 -> "It's super effective!"
                          | e when e < 10 -> "It's not very effective..."
                          | _ -> () ]
                { ctx with Foe = foe; Rng = rng'; Messages = ctx.Messages @ notes; LastDamage = dmg }
            else
                // Heal target by 25% max HP.
                let heal = max 1 (ctx.Foe.MaxHp / 4)
                let foe = { ctx.Foe with Hp = min ctx.Foe.MaxHp (ctx.Foe.Hp + heal) }
                { ctx with Foe = foe; Rng = rng'; Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} regained health!" ] }

        | MagnitudeDamage ->
            // BattleCommand_GetMagnitude (move_effects/magnitude.asm).
            // RNG draw: 1 byte. Threshold table (data/moves/magnitude_power.asm):
            //   <=13  → mag 4, power 10  |  <=38  → mag 5, power 30
            //   <=89  → mag 6, power 50  |  <=166 → mag 7, power 70
            //   <=217 → mag 8, power 90  |  <=242 → mag 9, power 110
            //   <=255 → mag 10, power 150
            let roll, rng' = Rng.next ctx.Rng
            let mag, power =
                if roll <= 13 then 4, 10
                elif roll <= 38 then 5, 30
                elif roll <= 89 then 6, 50
                elif roll <= 166 then 7, 70
                elif roll <= 217 then 8, 90
                elif roll <= 242 then 9, 110
                else 10, 150
            let m = { ctx.Move with Power = power }
            let dmg = Damage.calc ctx.User ctx.Foe m ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let notes =
                [ $"Magnitude {mag}!"
                  if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen m foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> () ]
            { ctx with Foe = foe; Rng = rng'; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | HiddenPowerDamage ->
            // HiddenPowerDamage (engine/battle/hidden_power.asm).
            // Type and power from DVs. Our model uses DV=0 for all stats.
            // With all DVs=0: atkDV=0, defDV=0, spdDV=0, spcDV=0.
            // Power: ((atkDV&8)<<0 | (defDV&8)>>1 | (spdDV&8)>>2 | (spcDV&8)>>3) * 5
            //        + (spcDV & 3) -> /2 + 31 = 31.
            // Type: ((defDV & 3) | ((atkDV & 3) << 2)) + 1 = 1 (FIGHTING).
            // Skip Normal (inc), skip Bird/unused types checks.
            let power = 31
            let hpType = TypeChart.value "FIGHTING"
            let m = { ctx.Move with Power = power; Type = hpType }
            let dmg = Damage.calc ctx.User ctx.Foe m ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  match Damage.effectivenessTimesTen m foe with
                  | 0 -> $"It doesn't affect {foe.Species.Name}..."
                  | e when e > 10 -> "It's super effective!"
                  | e when e < 10 -> "It's not very effective..."
                  | _ -> () ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | FuryCutterDamage ->
            // BattleCommand_FuryCutter (move_effects/fury_cutter.asm).
            // Power doubles per consecutive hit, max 5 turns (16x).
            // Counter is 1-indexed: first use = count 1.
            let count = min 5 (ctx.FuryCutterCount + 1)
            let mutable power = ctx.Move.Power
            for _ in 2 .. count do
                power <- power * 2
            let m = { ctx.Move with Power = power }
            let dmg = Damage.calc ctx.User ctx.Foe m ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen m foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> () ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg; FuryCutterCount = count }

        | RolloutDamage ->
            // BattleCommand_RolloutPower (move_effects/rollout.asm).
            // Power doubles each turn (count 1-5). Defense Curl adds +1 to the
            // doubling exponent. Lock-in turn management is M13.7.
            let count = min 5 (ctx.RolloutCount + 1)
            let doublings = if ctx.DefenseCurlUsed then count else count - 1
            let mutable power = ctx.Move.Power
            for _ in 1 .. doublings do
                power <- power * 2
            let m = { ctx.Move with Power = power }
            let dmg = Damage.calc ctx.User ctx.Foe m ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen m foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> () ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg; RolloutCount = count }

        | TripleKickDamage ->
            // BattleCommand_TripleKick + BattleCommand_KickCounter (move_effects/triple_kick.asm).
            // 3 hits at escalating power: kick 1 = base, kick 2 = 2*base, kick 3 = 3*base.
            // Each hit does its own damage calc (but we use the same crit/roll for simplicity).
            let basePower = ctx.Move.Power
            let mutable totalDmg = 0
            let mutable foe = ctx.Foe
            let mutable notes: string list = []
            for kick in 1 .. 3 do
                let m = { ctx.Move with Power = basePower * kick }
                let dmg = Damage.calc ctx.User foe m ctx.Crit ctx.Roll ctx.IsStruggle
                foe <- { foe with Hp = max 0 (foe.Hp - dmg) }
                totalDmg <- totalDmg + dmg
            notes <- [ $"Hit 3 time(s)!" ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = totalDmg }

        | BeatUpDamage ->
            // BattleCommand_BeatUp (move_effects/beat_up.asm).
            // In wild battles, only the active mon participates (1 hit).
            // Damage = (level * 2 / 5 + 2) * basePower * userBaseAtk / foeBaseDef / 50 + 2.
            // Simplified: one hit using the normal damage formula with base power.
            let dmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            { ctx with Foe = foe; Messages = ctx.Messages; LastDamage = dmg }

        | DrainDamage ->
            // BattleCommand_DrainTarget / SapHealth (effect_commands.asm l.3797-3870).
            // Normal damage, then heal user by half damage dealt (min 1).
            let dmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            let foe, subBroke =
                match ctx.Foe.Volatile.Substitute with
                | Some subHp ->
                    let remaining = subHp - dmg
                    if remaining <= 0 then
                        let vol = { ctx.Foe.Volatile with Substitute = None }
                        { ctx.Foe with Volatile = vol }, true
                    else
                        let vol = { ctx.Foe.Volatile with Substitute = Some remaining }
                        { ctx.Foe with Volatile = vol }, false
                | None ->
                    { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }, false
            let heal = max 1 (dmg / 2)
            let user = { ctx.User with Hp = min ctx.User.MaxHp (ctx.User.Hp + heal) }
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen ctx.Move foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> ()
                  if subBroke then $"{foe.Species.Name}'s substitute faded!"
                  $"{ctx.User.Species.Name} sucked health from {foe.Species.Name}!" ]
            { ctx with User = user; Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | DreamEaterDamage ->
            // BattleCommand_EatDream (effect_commands.asm l.3797).
            // Only works if target is sleeping. Otherwise fails.
            match ctx.Foe.Status with
            | Sleep _ ->
                let dmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
                let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
                let heal = max 1 (dmg / 2)
                let user = { ctx.User with Hp = min ctx.User.MaxHp (ctx.User.Hp + heal) }
                let notes =
                    [ if ctx.Crit then "A critical hit!"
                      $"{ctx.Foe.Species.Name}'s dream was eaten!" ]
                { ctx with User = user; Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }
            | _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }

        | SelfdestructDamage ->
            // BattleCommand_Selfdestruct + BattleCommand_DamageCalc selfdestruct path.
            // Halve target defense during damage calc (effect_commands.asm l.2905-2912).
            // User faints after (HP = 0).
            let halvedDefFoe =
                let d = max 1 (ctx.Foe.Defense / 2)
                let sd = max 1 (ctx.Foe.SpDefense / 2)
                { ctx.Foe with Defense = d; SpDefense = sd }
            let dmg = Damage.calc ctx.User halvedDefFoe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let user = { ctx.User with Hp = 0 }
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen ctx.Move foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> () ]
            { ctx with User = user; Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | JumpKickDamage ->
            // EFFECT_JUMP_KICK (Hi Jump Kick / Jump Kick).
            // Normal damage on hit. On miss, crash damage = 1/8 user's max HP (min 1).
            // The miss is already handled by executeMove (which doesn't call applyCtx
            // on miss), so here we just do normal damage. The crash-on-miss is
            // handled in Battle.fs executeMove for this effect.
            let dmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            let foe, subBroke =
                match ctx.Foe.Volatile.Substitute with
                | Some subHp ->
                    let remaining = subHp - dmg
                    if remaining <= 0 then
                        let vol = { ctx.Foe.Volatile with Substitute = None }
                        { ctx.Foe with Volatile = vol }, true
                    else
                        let vol = { ctx.Foe.Volatile with Substitute = Some remaining }
                        { ctx.Foe with Volatile = vol }, false
                | None ->
                    { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }, false
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen ctx.Move foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> ()
                  if subBroke then $"{foe.Species.Name}'s substitute faded!" ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | PayDayDamage ->
            // BattleCommand_PayDay (move_effects/pay_day.asm).
            // Normal damage + scatter coins message. Money = level * 2 (not tracked here).
            let dmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen ctx.Move foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> ()
                  "Coins scattered everywhere!" ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | RapidSpinDamage ->
            // BattleCommand_RapidSpin (move_effects/rapid_spin.asm).
            // Normal damage + clear leech seed, trap, spikes on user.
            let dmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let mutable user = ctx.User
            let mutable clearMsgs: string list = []
            if user.Volatile.LeechSeed then
                user <- { user with Volatile = { user.Volatile with LeechSeed = false } }
                clearMsgs <- clearMsgs @ [ $"{user.Species.Name} shed Leech Seed!" ]
            if user.Volatile.Trapped.IsSome then
                user <- { user with Volatile = { user.Volatile with Trapped = None } }
                clearMsgs <- clearMsgs @ [ $"{user.Species.Name} was freed from the trap!" ]
            // Spikes clear would go here (M13.8 field hazards).
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen ctx.Move foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> () ]
            { ctx with User = user; Foe = foe; Messages = ctx.Messages @ notes @ clearMsgs; LastDamage = dmg }

        | ThiefDamage ->
            // BattleCommand_Thief (move_effects/thief.asm).
            // Normal damage + steal item (item model stub: just message).
            let dmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen ctx.Move foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> ()
                  // Item steal stub: M13.5 documents this as needing in-battle item model.
                  // "Stole <item>!" would go here when items are implemented.
                  ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | RageDamage ->
            // BattleCommand_Rage (move_effects/rage.asm).
            // Normal damage + set rage flag. The atk-up-on-hit mechanic is M13.7 turn-state.
            let dmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen ctx.Move foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> ()
                  // M13.7 hook: set SUBSTATUS_RAGE for atk-up-on-hit mechanic.
                  ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | MultiHitDamage ->
            // BattleCommand_EndLoop EFFECT_MULTI_HIT path (effect_commands.asm l.5228-5241).
            // Hit count: draw byte & 3. If < 2, use value + 1.
            // If >= 2, draw again & 3, then + 1.
            // Distribution: 2 hits = 3/8, 3 hits = 3/8, 4 hits = 1/8, 5 hits = 1/8.
            let r1, rng1 = Rng.next ctx.Rng
            let masked1 = r1 &&& 3
            let hits, rng' =
                if masked1 < 2 then
                    masked1 + 1, rng1
                else
                    let r2, rng2 = Rng.next rng1
                    (r2 &&& 3) + 1, rng2
            // BUT the disassembly does: first draw & 3, if < 2 use that + 1 (=1 or 2).
            // Wait - from the code: inc a at l.5236 makes it count+1 which goes to [de] as
            // rollout count, which is then decremented. The actual hit count is count+1
            // since [bc] = count+1+1... Let me re-read.
            // Actually: .got_number_hits: inc a -> ld [de], a -> inc a -> ld [bc], a
            // So [de] = drawn+1, [bc] = drawn+2. Then .in_loop decrements [de] until 0.
            // Total hits = [bc] = [de]+1 = drawn+2.
            // Wait, .double_hit: ld [de],a; inc a; ld [bc],a; jr .loop_back_to_critical
            // So the first hit is already happening. The counter [de] is the REMAINING hits.
            // After the first hit, EndLoop enters .in_loop, decrements [de], loops if != 0.
            // So total hits = [de] + 1 = a + 1 where a was what got stored in [de].
            // For EFFECT_MULTI_HIT: a = .got_number_hits value.
            // If masked1 < 2: a = masked1, so hits = masked1 + 1 = 1 or 2.
            // If masked1 >= 2: a = (r2 & 3), so hits = (r2 & 3) + 1 = 1,2,3,4.
            // But wait: .got_number_hits does inc a FIRST, then .double_hit stores to [de].
            // So: for masked1 < 2: .got_number_hits: inc a → masked1+1, .double_hit stores
            //   masked1+1 in [de], inc a → masked1+2 in [bc].
            // Total hits = [de]+1 = masked1+1+1 = masked1+2.
            // For masked1 >= 2: second draw & 3 → r2m. Falls to .got_number_hits:
            //   inc a → r2m+1 → .double_hit: [de]=r2m+1, [bc]=r2m+2.
            // Total hits = r2m+1+1 = r2m+2.
            //
            // So: masked1=0 → 2 hits, masked1=1 → 3 hits.
            //     masked1>=2 → r2m+2 hits where r2m = 0..3 → 2,3,4,5 hits.
            // P(2) = 1/4 + 1/2*1/4 = 3/8, P(3) = 1/4 + 1/2*1/4 = 3/8,
            // P(4) = 1/2*1/4 = 1/8, P(5) = 1/2*1/4 = 1/8. Correct!
            let hits, rng' =
                let r1, rng1 = Rng.next ctx.Rng
                let m1 = r1 &&& 3
                if m1 < 2 then
                    m1 + 2, rng1
                else
                    let r2, rng2 = Rng.next rng1
                    (r2 &&& 3) + 2, rng2
            let mutable totalDmg = 0
            let mutable foe = ctx.Foe
            for _ in 1 .. hits do
                let dmg = Damage.calc ctx.User foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
                foe <- { foe with Hp = max 0 (foe.Hp - dmg) }
                totalDmg <- totalDmg + dmg
            let notes = [ $"Hit {hits} time(s)!" ]
            { ctx with Foe = foe; Rng = rng'; Messages = ctx.Messages @ notes; LastDamage = totalDmg }

        | DoubleHitDamage ->
            // BattleCommand_EndLoop EFFECT_DOUBLE_HIT path: always 2 hits.
            let mutable totalDmg = 0
            let mutable foe = ctx.Foe
            for _ in 1 .. 2 do
                let dmg = Damage.calc ctx.User foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
                foe <- { foe with Hp = max 0 (foe.Hp - dmg) }
                totalDmg <- totalDmg + dmg
            let notes = [ "Hit 2 time(s)!" ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = totalDmg }

        | PoisonMultiHitDamage ->
            // BattleCommand_EndLoop EFFECT_POISON_MULTI_HIT path (Twineedle): always 2 hits.
            // + 20% poison chance per hit (reuses InflictPoison logic).
            // The poison secondary rolls are per-hit. We roll after all hits.
            let mutable totalDmg = 0
            let mutable foe = ctx.Foe
            for _ in 1 .. 2 do
                let dmg = Damage.calc ctx.User foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
                foe <- { foe with Hp = max 0 (foe.Hp - dmg) }
                totalDmg <- totalDmg + dmg
            let mutable rng = ctx.Rng
            let mutable poisonMsgs: string list = []
            // Try poison after hits (20% = EffectChance on the move = 20).
            // Two chances (one per hit), faithful to Twineedle's secondary.
            let poisonType = TypeChart.value "POISON"
            let canPoison =
                foe.Status = Healthy
                && foe.Species.Type1 <> poisonType && foe.Species.Type2 <> poisonType
                && foe.Volatile.Substitute.IsNone
            for _ in 1 .. 2 do
                if canPoison && foe.Status = Healthy then
                    let roll, rng' = Rng.next rng
                    rng <- rng'
                    // 20% = effectChance 20; roll < 20*255/100 ≈ 51
                    if roll < 51 then
                        foe <- { foe with Status = Poison }
                        poisonMsgs <- [ $"{foe.Species.Name} was poisoned!" ]
            let notes = [ "Hit 2 time(s)!" ]
            { ctx with Foe = foe; Rng = rng; Messages = ctx.Messages @ notes @ poisonMsgs; LastDamage = totalDmg }

        | ConditionalDoubleDamage ->
            // EFFECT_GUST / EFFECT_TWISTER / EFFECT_STOMP / EFFECT_EARTHQUAKE.
            // Normal damaging hit. Hook for 2x vs Fly/Dig/Minimize (M13.7).
            // Flinch secondary for Twister/Stomp is M13.6.
            let dmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            let foe, subBroke =
                match ctx.Foe.Volatile.Substitute with
                | Some subHp ->
                    let remaining = subHp - dmg
                    if remaining <= 0 then
                        let vol = { ctx.Foe.Volatile with Substitute = None }
                        { ctx.Foe with Volatile = vol }, true
                    else
                        let vol = { ctx.Foe.Volatile with Substitute = Some remaining }
                        { ctx.Foe with Volatile = vol }, false
                | None ->
                    { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }, false
            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen ctx.Move foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> ()
                  if subBroke then $"{foe.Species.Name}'s substitute faded!" ]
            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

    /// Legacy apply: wraps applyCtx for callers that don't need the full context.
    let apply
        (attacker: BattleMon)
        (defender: BattleMon)
        (move: MoveData)
        (crit: bool)
        (roll: int)
        (cmd: EffectCommand)
        : BattleMon * BattleMon * string list =
        let ctx : MoveContext =
            { User = attacker; Foe = defender; Move = move; Crit = crit; Roll = roll
              Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
              FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0 }
        let ctx' = applyCtx ctx cmd
        ctx'.User, ctx'.Foe, ctx'.Messages
