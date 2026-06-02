namespace PokeGold.Game.Battle

open PokeGold.Game.Data

/// The Gen-2 damage calculation, a faithful re-expression of
/// `engine/battle/effect_commands.asm` (`DamageCalc` → `Stab` → `DamageVariation`).
/// Every step truncates toward zero, in the original order, so worked examples
/// reproduce the disassembly exactly. Critical hit and the 85–100% damage roll
/// are passed in explicitly to keep the function pure and deterministic.
module Damage =

    [<Literal>]
    let MinRoll = 217
    [<Literal>]
    let MaxRoll = 255

    [<Literal>]
    let private DamageCap = 997
    [<Literal>]
    let private MinDamage = 2

    /// The combined type effectiveness of a move against a defender, scaled by
    /// ten per matching type (e.g. 40 = ×4, 10 = ×1, 0 = immune). Distinct
    /// defender types each contribute, matching the matchup-table scan.
    let effectivenessTimesTen (move: MoveData) (defender: BattleMon) : int =
        let s = defender.Species
        let types = if s.Type1 = s.Type2 then [ s.Type1 ] else [ s.Type1; s.Type2 ]
        types |> List.fold (fun acc t -> acc * TypeChart.multiplier move.Type t / 10) 10

    /// Damage dealt by `attacker`'s `move` against `defender`. `crit` doubles the
    /// pre-modifier total and (as in Gen 2) ignores stat-stage modifiers; `roll`
    /// is the 217..255 spread divisor numerator. `isStruggle` skips STAB
    /// (effect_commands.asm l.1221: cp STRUGGLE; ret z).
    let calc (attacker: BattleMon) (defender: BattleMon) (move: MoveData) (crit: bool) (roll: int) (isStruggle: bool) : int =
        let physical = TypeChart.isPhysical move.Type

        // Critical hits use unmodified stats; otherwise stat stages apply.
        let atk =
            match crit, physical with
            | true, true -> attacker.Attack
            | true, false -> attacker.SpAttack
            | false, true -> BattleMon.effectiveAttack attacker
            | false, false -> BattleMon.effectiveSpAttack attacker

        let def =
            match crit, physical with
            | true, true -> defender.Defense
            | true, false -> defender.SpDefense
            | false, true -> BattleMon.effectiveDefense defender
            | false, false -> BattleMon.effectiveSpDefense defender

        // Base: (((2*Level/5 + 2) * Power * Attack) / Defense) / 50, truncating.
        let mutable d = 2 * attacker.Level / 5 + 2
        d <- d * move.Power
        d <- d * atk
        d <- d / def
        d <- d / 50

        // Critical hit doubles, then the running total is capped and floored.
        if crit then d <- d * 2
        d <- min d DamageCap + MinDamage

        // STAB: ×1.5 as damage + damage/2. Struggle skips STAB
        // (effect_commands.asm l.1221: cp STRUGGLE; ret z).
        if not isStruggle && (move.Type = attacker.Species.Type1 || move.Type = attacker.Species.Type2) then
            d <- d + d / 2

        // Type effectiveness: ×(multiplier/10) per distinct defender type.
        let s = defender.Species
        let defTypes = if s.Type1 = s.Type2 then [ s.Type1 ] else [ s.Type1; s.Type2 ]
        for t in defTypes do
            d <- d * TypeChart.multiplier move.Type t / 10

        // Random spread: ×roll/255 (roll in 217..255).
        d <- d * roll / 255
        d
