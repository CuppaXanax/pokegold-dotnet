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
        | _ -> if move.Power > 0 then [ Damage ] else []

    /// Apply one effect command to a MoveContext. Returns the updated context
    /// with user/foe/messages/lastDamage modified as needed.
    let applyCtx (ctx: MoveContext) (cmd: EffectCommand) : MoveContext =
        match cmd with
        | Damage ->
            let dmg = Damage.calc ctx.User ctx.Foe ctx.Move ctx.Crit ctx.Roll ctx.IsStruggle
            let foe = { ctx.Foe with Hp = max 0 (ctx.Foe.Hp - dmg) }

            let notes =
                [ if ctx.Crit then "A critical hit!"
                  if not ctx.IsStruggle then
                      match Damage.effectivenessTimesTen ctx.Move foe with
                      | 0 -> $"It doesn't affect {foe.Species.Name}..."
                      | e when e > 10 -> "It's super effective!"
                      | e when e < 10 -> "It's not very effective..."
                      | _ -> () ]

            { ctx with Foe = foe; Messages = ctx.Messages @ notes; LastDamage = dmg }

        | Recoil ->
            // Recoil = 1/4 of damage dealt, min 1 HP.
            // effect_commands.asm BattleCommand_Recoil: srl b; rr c; srl b; rr c
            let recoil = max 1 (ctx.LastDamage / 4)
            let user = { ctx.User with Hp = max 0 (ctx.User.Hp - recoil) }
            let notes = [ $"{ctx.User.Species.Name}'s hit with recoil!" ]
            { ctx with User = user; Messages = ctx.Messages @ notes }

        | LowerTargetStat s ->
            if stage s ctx.Foe <= -6 then
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
            match ctx.Foe.Status with
            | Paralysis ->
                { ctx with Messages = ctx.Messages @ [ $"{ctx.Foe.Species.Name} is already paralyzed!" ] }
            | Healthy ->
                let foe = { ctx.Foe with Status = Paralysis }
                { ctx with Foe = foe; Messages = ctx.Messages @ [ $"{foe.Species.Name} is paralyzed! It may be unable to move!" ] }
            | _ ->
                { ctx with Messages = ctx.Messages @ [ "But it failed!" ] }

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
