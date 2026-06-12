namespace PokeGold.Game.Battle

open PokeGold.Game.Data

/// Non-volatile status condition (constants/pokemon_data_constants.asm).
/// A mon has exactly one (or Healthy). Sleep carries a counter (1-7 turns),
/// BadPoison (Toxic) carries a ramp counter that grows each end-of-turn tick.
/// Behavior is NOT implemented here -- see M13.3.
type StatusCondition =
    | Healthy
    | Sleep of turnsLeft: int
    | Poison
    | BadPoison of counter: int
    | Burn
    | Freeze
    | Paralysis

/// Explicit per-battler gender used by Attract. Unknown is conservative when
/// no party/wild gender data has been threaded into the battle state yet.
type Gender =
    | Male
    | Female
    | Genderless
    | Unknown

/// Per-battle volatile status flags that reset on switch-out.
/// M13.0 defined the data shape; M13.4 added Mist/CantEscape and implements
/// confusion, flinch, leech seed, trap/wrap, substitute, focus energy, mist,
/// and mean look. M13.7 will implement charging/recharge/rampage.
type VolatileStatus =
    { /// Confusion turns remaining; None = not confused.
      /// Set by EFFECT_CONFUSE (2-5 turns). Each pre-move gate decrements;
      /// at 0 the mon snaps out. While confused, 50% chance of self-hit.
      Confusion: int option
      /// Set when a move with a flinch secondary fires; cleared each turn.
      /// Flinch only blocks if the flincher moved first that turn.
      Flinch: bool
      /// Leech Seed is active on this mon. End-of-turn: drain MaxHP/8 (min 1),
      /// heal the other side by that amount.
      LeechSeed: bool
      /// Substitute HP remaining; None = no substitute.
      /// Absorbs damage and blocks incoming status + stat drops while up.
      Substitute: int option
      /// Trapped/wrapped turns remaining; None = not trapped.
      /// Internal counter 3-6 (2-5 damaging turns). End-of-turn: decrement;
      /// at 0 release, else chip MaxHP/16 (min 1). Prevents fleeing.
      Trapped: int option
      /// Focus Energy is active (crit stage +1). Read by CriticalHit.critStage.
      FocusEnergy: bool
      /// Charging a two-turn move (e.g. Fly, Dig); None = not charging.
      /// Stores the countdown for the second-turn execution window.
      Charging: int option
      /// The move that is currently charging; cleared once it resolves.
      ChargingMove: MoveData option
      /// Must recharge this turn (e.g. after Hyper Beam).
      Recharge: bool
      /// Rampage (Thrash/Petal Dance) turns remaining; None = not rampaging.
      Rampage: int option
      /// Rage mode: this mon's damage counter rises each time it is hit.
      Rage: bool
      /// Rage damage counter, incremented when a raging mon is hit.
      RageCounter: int
      /// Future Sight countdown and move for the end-of-turn payoff.
      FutureSightCounter: int option
      FutureSightMove: MoveData option
      /// Mist is active: blocks opponent's stat-lowering moves.
      /// M13.6 stage helpers cover Attack/Defense/Speed, SpAttack/SpDefense,
      /// Accuracy, and Evasion.
      Mist: bool
      /// Mean Look / Spider Web: prevents fleeing. Cleared on switch-out.
      /// Extension point for M13.7 (multi-turn family) and M14 (switching).
      CantEscape: bool
      /// Nightmare is active on this mon; it chips sleeping targets for MaxHP/4.
      Nightmare: bool
      /// Curse is active on this mon; it chips cursed targets for MaxHP/4 each turn.
      Curse: bool
      /// Protect is active for one turn.
      Protect: bool
      /// Endure is active for one turn.
      Endure: bool
      /// Consecutive Protect/Endure uses; shared by both moves.
      ProtectCount: int
      /// Destiny Bond will faint the attacker when this mon faints.
      DestinyBond: bool
      /// Encore countdown on this mon; 0 means inactive.
      EncoreTimer: int option
      /// Disable countdown on this mon; 0 means inactive.
      DisableTimer: int option
      /// The move index disabled by Disable.
      DisabledMoveIndex: int option
      /// Lock On guarantees the next move hits.
      LockOn: bool
      /// Foresight bypasses Ghost/Evasion interaction.
      Foresight: bool
      /// Attract infatuation: the mon is attracted to its opponent and has a
      /// 50% chance to fail its move on the pre-move gate.
      Attracted: bool
      /// Bide lock-in turns remaining; damage taken while active is released at
      /// double power when the counter expires.
      BideTurns: int option
      BideDamage: int
      /// Defense Curl substatus, used to double Rollout's damage ramp.
      Curled: bool
      /// Minimize substatus, used by Stomp's double-damage check.
      Minimized: bool }

module VolatileStatus =
    /// Neutral/empty volatile status -- no flags set.
    let empty : VolatileStatus =
        { Confusion = None
          Flinch = false
          LeechSeed = false
          Substitute = None
          Trapped = None
          FocusEnergy = false
          Charging = None
          ChargingMove = None
          Recharge = false
          Rampage = None
          Rage = false
          RageCounter = 0
          FutureSightCounter = None
          FutureSightMove = None
          Mist = false
          CantEscape = false
          Nightmare = false
          Curse = false
          Protect = false
          Endure = false
          ProtectCount = 0
          DestinyBond = false
          EncoreTimer = None
          DisableTimer = None
          DisabledMoveIndex = None
          LockOn = false
          Foresight = false
          Attracted = false
          BideTurns = None
          BideDamage = 0
          Curled = false
          Minimized = false }

/// A combatant in a battle: a species at a level with derived stats, current HP,
/// a move set, and per-stat stage modifiers (-6..+6). Everything is immutable;
/// the turn loop produces new `BattleMon` values rather than mutating.
type BattleMon =
    { Species: BaseStats
      Level: int
      MaxHp: int
      Hp: int
      Attack: int
      Defense: int
      Speed: int
      SpAttack: int
      SpDefense: int
      Moves: MoveData list
      /// Current PP for each move, parallel to Moves. Init from MoveData.Pp.
      Pp: int list
      /// Held item constant from the party model, if any.
      HeldItem: string option
      /// Non-volatile status condition.
      Status: StatusCondition
      AtkStage: int
      DefStage: int
      SpdStage: int
      SpAtkStage: int
      SpDefStage: int
      /// Accuracy stage (-6..+6). Used by M13.1 hit check.
      AccStage: int
      /// Evasion stage (-6..+6). Used by M13.1 hit check.
      EvaStage: int
      /// Explicit battle gender used by Attract. Defaults to Unknown.
      Gender: Gender
      /// Per-battle volatile status flags. See VolatileStatus type.
      Volatile: VolatileStatus }

module BattleMon =

    /// Stage → (numerator, denominator), the `data/battle/stat_multipliers.asm`
    /// ratios for stages −6..+6. Indexed by `stage + 6`.
    let private stageRatios =
        [| (25, 100); (28, 100); (33, 100); (40, 100); (50, 100); (66, 100)
           (1, 1)
           (15, 10); (2, 1); (25, 10); (3, 1); (35, 10); (4, 1) |]

    /// Accuracy/evasion stage ratios from `data/battle/accuracy_multipliers.asm`.
    /// Stages −6..+6, indexed by `stage + 6`. These DIFFER from the 5-stat
    /// `stageRatios` above (the accuracy table grows faster).
    let private accEvaStageRatios =
        [| (33, 100); (36, 100); (43, 100); (50, 100); (60, 100); (75, 100)
           (1, 1)
           (133, 100); (166, 100); (2, 1); (233, 100); (133, 50); (3, 1) |]

    /// Apply accuracy/evasion stage modifiers to a raw accuracy byte, faithfully
    /// reproducing the `.StatModifiers` two-pass loop in `effect_commands.asm`.
    /// First multiplies by the user's accuracy stage ratio, then by the inverted
    /// foe's evasion stage ratio. Intermediate results are clamped to minimum 1;
    /// the final result is capped at 255.
    let applyAccEvaStages (accByte: int) (userAccStage: int) (foeEvaStage: int) : int =
        // Pass 1: user's accuracy stage (table index = accStage + 6)
        let accIdx = max 0 (min 12 (userAccStage + 6))
        let accNum, accDen = accEvaStageRatios.[accIdx]
        let mutable acc = accByte * accNum / accDen
        acc <- max 1 acc

        // Pass 2: inverted foe's evasion stage (table index = 6 - evaStage)
        // In the hardware, evasion level is subtracted from MAX_STAT_LEVEL+1 (14),
        // which effectively mirrors the table: +6 evasion → lowest ratio (33/100),
        // −6 evasion → highest ratio (3/1).
        let evaIdx = max 0 (min 12 (6 - foeEvaStage))
        let evaNum, evaDen = accEvaStageRatios.[evaIdx]
        acc <- acc * evaNum / evaDen
        acc <- max 1 acc

        min 255 acc

    [<Literal>]
    let MaxStatValue = 999

    /// Apply a stat stage (−6..+6) to a base stat value, as the battle engine
    /// does: multiply by the stage ratio with integer truncation, clamp to 999.
    let applyStage (stage: int) (stat: int) : int =
        let stage = max -6 (min 6 stage)
        let num, den = stageRatios.[stage + 6]
        min MaxStatValue (stat * num / den)

    let effectiveAttack (m: BattleMon) = applyStage m.AtkStage m.Attack
    let effectiveDefense (m: BattleMon) =
        let value = applyStage m.DefStage m.Defense
        if m.Species.Name = "DITTO" && m.HeldItem = Some "METAL_POWDER" then
            min MaxStatValue (value * 2)
        else value
    let effectiveSpAttack (m: BattleMon) = applyStage m.SpAtkStage m.SpAttack
    let effectiveSpDefense (m: BattleMon) =
        let value = applyStage m.SpDefStage m.SpDefense
        if m.Species.Name = "DITTO" && m.HeldItem = Some "METAL_POWDER" then
            min MaxStatValue (value * 2)
        else value
    /// Effective Speed, faithful to the GSC engine: stage-modified then quartered
    /// if paralysed (PAR). `ApplyPrzEffectOnSpeed` in core.asm halves twice.
    let effectiveSpeed (m: BattleMon) =
        let spd = applyStage m.SpdStage m.Speed
        match m.Status with
        | Paralysis -> max 1 (spd / 4)
        | _ -> spd

    let isFainted (m: BattleMon) = m.Hp <= 0

    /// True when the move at `index` has PP remaining and can be selected.
    let canUseMove (index: int) (m: BattleMon) : bool =
        index >= 0 && index < m.Pp.Length && m.Pp.[index] > 0

    /// True when every move is at 0 PP (the mon must Struggle).
    /// Returns false if the mon has no moves.
    let mustStruggle (m: BattleMon) : bool =
        not m.Pp.IsEmpty && m.Pp |> List.forall (fun pp -> pp = 0)

    /// Max PP for the move at `index` (from the MoveData).
    let maxPp (index: int) (m: BattleMon) : int =
        m.Moves.[index].Pp

    /// Return a new BattleMon with the PP at `index` decremented by 1 (min 0).
    /// No-op if index is out of range.
    let deductPp (index: int) (m: BattleMon) : BattleMon =
        if index < 0 || index >= m.Pp.Length then m
        else
            let pp' = m.Pp |> List.mapi (fun i pp -> if i = index then max 0 (pp - 1) else pp)
            { m with Pp = pp' }

    // The Gen-2 stat formula with DV = 0 and stat experience = 0 (see
    // engine/pokemon/move_mon.asm CalcMonStatC): the (2*Base*Level/100) core
    // plus Level + 10 for HP, or + 5 for every other stat.
    
    /// Gen-2 stat formula core (DV=0, stat exp=0).
    let statCore baseStat level = 2 * baseStat * level / 100

    /// HP stat formula: core + level + 10.
    let calcHp baseStat level = statCore baseStat level + level + 10

    /// Non-HP stat formula: core + 5.
    let calcStat baseStat level = statCore baseStat level + 5

    /// Build a battler from species data at a level with the given moves,
    /// starting at full HP with neutral stat stages, healthy status, full PP,
    /// and empty volatile flags.
    let ofSpecies (species: BaseStats) (level: int) (moves: MoveData list) : BattleMon =
        let maxHp = calcHp species.Hp level

        { Species = species
          Level = level
          MaxHp = maxHp
          Hp = maxHp
          Attack = calcStat species.Attack level
          Defense = calcStat species.Defense level
          Speed = calcStat species.Speed level
          SpAttack = calcStat species.SpAttack level
          SpDefense = calcStat species.SpDefense level
          Moves = moves
          Pp = moves |> List.map (fun m -> m.Pp)
          HeldItem = None
          Status = Healthy
          AtkStage = 0
          DefStage = 0
          SpdStage = 0
          SpAtkStage = 0
          SpDefStage = 0
          AccStage = 0
          EvaStage = 0
          Gender = Unknown
          Volatile = VolatileStatus.empty }
