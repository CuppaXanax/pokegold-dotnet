namespace PokeGold.Game.Battle

open PokeGold.Game.Data

// ---------------------------------------------------------------------------
//  CriticalHit — crit-stage computation, threshold table, high-crit moves
//
//  Faithful to `engine/battle/effect_commands.asm` `BattleCommand_Critical`
//  and `data/battle/critical_hit_chances.asm`.
// ---------------------------------------------------------------------------

module CriticalHit =

    /// Per-stage crit thresholds from `data/battle/critical_hit_chances.asm`.
    /// Index = crit stage (0..6). The `out_of` macro expands to `$100 / n`,
    /// i.e. 256 / n (integer). A BattleRandom byte < threshold → crit.
    ///   stage 0: 1 out_of 15 = 17
    ///   stage 1: 1 out_of 8  = 32
    ///   stage 2: 1 out_of 4  = 64
    ///   stage 3: 1 out_of 3  = 85
    ///   stage 4+: 1 out_of 2 = 128
    let thresholds : int[] =
        [| 17; 32; 64; 85; 128; 128; 128 |]

    /// Moves that receive +2 crit stage, from `data/moves/critical_hit_moves.asm`.
    /// The disassembly's `CriticalHitMoves` list is checked with `IsInArray`
    /// against the move animation byte (= move constant).
    let private highCritMoves =
        Set.ofList
            [ "KARATE_CHOP"
              "RAZOR_WIND"
              "RAZOR_LEAF"
              "CRABHAMMER"
              "SLASH"
              "AEROBLAST"
              "CROSS_CHOP" ]

    /// True when the move is in the `CriticalHitMoves` table (high-crit-ratio).
    let isHighCritMove (move: MoveData) : bool =
        highCritMoves.Contains move.Name

    /// Compute the crit stage (0..6) for an attack, faithful to
    /// `BattleCommand_Critical` in `engine/battle/effect_commands.asm`.
    ///
    /// The hardware accumulates c = 0, then:
    ///   +1 if Focus Energy (SUBSTATUS_FOCUS_ENERGY)
    ///   +2 if move is in CriticalHitMoves
    ///   (Chansey+Lucky Punch / Farfetch'd+Stick / Scope Lens omitted — item scope)
    ///
    /// The stage indexes into `thresholds` (max index = 6).
    let critStage (focusEnergy: bool) (move: MoveData) : int =
        let mutable c = 0
        if focusEnergy then c <- c + 1
        if isHighCritMove move then c <- c + 2
        min c (thresholds.Length - 1)

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
    /// pre-modifier total. On a crit, stat stages are ignored only when they
    /// would hurt the attacker (faithful to `CheckDamageStatsCritical` in
    /// `engine/battle/effect_commands.asm` l.2658-2702):
    ///   - if defStage >= atkStage: use unmodified (base) stats for both
    ///   - if defStage <  atkStage: use stage-modified stats for both
    ///   - no crit: always use stage-modified stats
    /// `roll` is the 217..255 spread divisor numerator. `isStruggle` skips STAB
    /// (effect_commands.asm l.1221: cp STRUGGLE; ret z).
    let calc (attacker: BattleMon) (defender: BattleMon) (move: MoveData) (crit: bool) (roll: int) (isStruggle: bool) : int =
        let physical = TypeChart.isPhysical move.Type

        // Determine whether to use boosted (stage-modified) stats.
        // CheckDamageStatsCritical: carry set (use boosted) when:
        //   - not a crit (wCriticalHit == 0), OR
        //   - crit AND defender's def level < attacker's atk level
        let useBoosted =
            if not crit then true
            else
                let atkStage, defStage =
                    if physical then attacker.AtkStage, defender.DefStage
                    else attacker.SpAtkStage, defender.SpDefStage
                defStage < atkStage

        let atk =
            match useBoosted, physical with
            | true, true -> BattleMon.effectiveAttack attacker
            | true, false -> BattleMon.effectiveSpAttack attacker
            | false, true -> attacker.Attack
            | false, false -> attacker.SpAttack

        let def =
            match useBoosted, physical with
            | true, true -> BattleMon.effectiveDefense defender
            | true, false -> BattleMon.effectiveSpDefense defender
            | false, true -> defender.Defense
            | false, false -> defender.SpDefense

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
