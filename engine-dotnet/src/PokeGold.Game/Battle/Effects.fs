namespace PokeGold.Game.Battle

open PokeGold.Game.Data

/// One of the five battle stats a stage modifier can target.
type Stat =
    | Attack
    | Defense
    | Speed
    | SpAttack
    | SpDefense

/// The battle-effect command language: a small DU mirroring the disassembly's
/// per-move effect scripts (`engine/battle/move_effects/…`). A move's effect
/// constant maps to a sequence of these, which the turn loop interprets. The
/// slice covers the damaging hit plus single-stage stat drops/raises.
type EffectCommand =
    | Damage
    | LowerTargetStat of Stat
    | RaiseUserStat of Stat

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
        | "EFFECT_ATTACK_DOWN" -> [ LowerTargetStat Attack ]
        | "EFFECT_DEFENSE_DOWN" -> [ LowerTargetStat Defense ]
        | "EFFECT_SPEED_DOWN" -> [ LowerTargetStat Speed ]
        | "EFFECT_ATTACK_UP" -> [ RaiseUserStat Attack ]
        | "EFFECT_DEFENSE_UP" -> [ RaiseUserStat Defense ]
        | _ -> if move.Power > 0 then [ Damage ] else []

    /// Apply one effect command. Returns the (possibly updated) attacker and
    /// defender plus the messages to show. `crit` and `roll` are resolved by the
    /// caller so this stays pure.
    let apply
        (attacker: BattleMon)
        (defender: BattleMon)
        (move: MoveData)
        (crit: bool)
        (roll: int)
        (cmd: EffectCommand)
        : BattleMon * BattleMon * string list =
        match cmd with
        | Damage ->
            let dmg = Damage.calc attacker defender move crit roll
            let defender = { defender with Hp = max 0 (defender.Hp - dmg) }

            let notes =
                [ if crit then "A critical hit!"
                  match Damage.effectivenessTimesTen move defender with
                  | 0 -> $"It doesn't affect {defender.Species.Name}..."
                  | e when e > 10 -> "It's super effective!"
                  | e when e < 10 -> "It's not very effective..."
                  | _ -> () ]

            attacker, defender, notes

        | LowerTargetStat s ->
            if stage s defender <= -6 then
                attacker, defender, [ $"{defender.Species.Name}'s {statName s} won't go lower!" ]
            else
                let defender = shiftStage s -1 defender
                attacker, defender, [ $"{defender.Species.Name}'s {statName s} fell!" ]

        | RaiseUserStat s ->
            if stage s attacker >= 6 then
                attacker, defender, [ $"{attacker.Species.Name}'s {statName s} won't go higher!" ]
            else
                let attacker = shiftStage s 1 attacker
                attacker, defender, [ $"{attacker.Species.Name}'s {statName s} rose!" ]
