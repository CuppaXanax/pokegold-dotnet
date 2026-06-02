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

    let private statName =
        function
        | Attack -> "ATTACK"
        | Defense -> "DEFENSE"
        | Speed -> "SPEED"
        | SpAttack -> "SPECIAL ATK"
        | SpDefense -> "SPECIAL DEF"

    /// Map a move's effect constant to its command sequence. Damaging moves with
    /// no special effect are a single `Damage`; the recognised stat moves drop
    /// the target's stat. Unknown effects fall back to `Damage` when the move
    /// has power, otherwise do nothing.
    let forMove (move: MoveData) : EffectCommand list =
        match move.Effect with
        | "EFFECT_NORMAL_HIT" -> [ Damage ]
        | "EFFECT_RECOIL_HIT" -> [ Damage; Recoil ]
        | "EFFECT_ATTACK_DOWN" -> [ LowerTargetStat Attack ]
        | "EFFECT_DEFENSE_DOWN" -> [ LowerTargetStat Defense ]
        | "EFFECT_SPEED_DOWN" -> [ LowerTargetStat Speed ]
        | "EFFECT_ATTACK_UP" -> [ RaiseUserStat Attack ]
        | "EFFECT_DEFENSE_UP" -> [ RaiseUserStat Defense ]
        | "EFFECT_SLEEP" -> [ InflictSleep ]
        | "EFFECT_POISON" -> [ InflictPoison ]
        | "EFFECT_TOXIC" -> [ InflictToxic ]
        | "EFFECT_PARALYZE" -> [ InflictParalyze ]
        | "EFFECT_CONFUSE" -> [ InflictConfuse ]
        | "EFFECT_LEECH_SEED" -> [ ApplyLeechSeed ]
        | "EFFECT_TRAP_TARGET" -> [ Damage; TrapTarget ]
        | "EFFECT_SUBSTITUTE" -> [ CreateSubstitute ]
        | "EFFECT_MIST" -> [ SetMist ]
        | "EFFECT_FOCUS_ENERGY" -> [ SetFocusEnergy ]
        | "EFFECT_MEAN_LOOK" -> [ SetMeanLook ]
        // EFFECT_FLINCH_HIT is a secondary-on-hit effect (M13.6 wires the
        // chance roll); here we map it as Damage + SetFlinch so the command
        // exists and can be tested. M13.6 will gate SetFlinch behind EffectChance.
        | "EFFECT_FLINCH_HIT" -> [ Damage; SetFlinch ]
        | _ -> if move.Power > 0 then [ Damage ] else []

    /// Apply one effect command to a MoveContext. Returns the updated context
    /// with user/foe/messages/lastDamage modified as needed.
    let applyCtx (ctx: MoveContext) (cmd: EffectCommand) : MoveContext =
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

        | SetFlinch ->
            // BattleCommand_FlinchTarget (effect_commands.asm l.5314).
            // Blocked by substitute, frozen/sleeping target, or if user went second.
            // The "went first" check is handled by the caller (M13.6 secondary system);
            // here we just set the flag. The pre-move gate ignores Flinch if the
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
              Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false }
        let ctx' = applyCtx ctx cmd
        ctx'.User, ctx'.Foe, ctx'.Messages
