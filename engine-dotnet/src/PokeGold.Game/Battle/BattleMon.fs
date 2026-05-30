namespace PokeGold.Game.Battle

open PokeGold.Game.Data

/// A combatant in a battle: a species at a level with derived stats, current HP,
/// a move set, and per-stat stage modifiers (−6..+6). Everything is immutable;
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
      AtkStage: int
      DefStage: int
      SpdStage: int
      SpAtkStage: int
      SpDefStage: int }

module BattleMon =

    /// Stage → (numerator, denominator), the `data/battle/stat_multipliers.asm`
    /// ratios for stages −6..+6. Indexed by `stage + 6`.
    let private stageRatios =
        [| (25, 100); (28, 100); (33, 100); (40, 100); (50, 100); (66, 100)
           (1, 1)
           (15, 10); (2, 1); (25, 10); (3, 1); (35, 10); (4, 1) |]

    [<Literal>]
    let MaxStatValue = 999

    /// Apply a stat stage (−6..+6) to a base stat value, as the battle engine
    /// does: multiply by the stage ratio with integer truncation, clamp to 999.
    let applyStage (stage: int) (stat: int) : int =
        let stage = max -6 (min 6 stage)
        let num, den = stageRatios.[stage + 6]
        min MaxStatValue (stat * num / den)

    let effectiveAttack (m: BattleMon) = applyStage m.AtkStage m.Attack
    let effectiveDefense (m: BattleMon) = applyStage m.DefStage m.Defense
    let effectiveSpAttack (m: BattleMon) = applyStage m.SpAtkStage m.SpAttack
    let effectiveSpDefense (m: BattleMon) = applyStage m.SpDefStage m.SpDefense
    let effectiveSpeed (m: BattleMon) = applyStage m.SpdStage m.Speed

    let isFainted (m: BattleMon) = m.Hp <= 0

    // The Gen-2 stat formula with DV = 0 and stat experience = 0 (see
    // engine/pokemon/move_mon.asm CalcMonStatC): the (2*Base*Level/100) core
    // plus Level + 10 for HP, or + 5 for every other stat.
    let private core baseStat level = 2 * baseStat * level / 100

    let private hpStat baseStat level = core baseStat level + level + 10
    let private otherStat baseStat level = core baseStat level + 5

    /// Build a battler from species data at a level with the given moves,
    /// starting at full HP with neutral stat stages.
    let ofSpecies (species: BaseStats) (level: int) (moves: MoveData list) : BattleMon =
        let maxHp = hpStat species.Hp level

        { Species = species
          Level = level
          MaxHp = maxHp
          Hp = maxHp
          Attack = otherStat species.Attack level
          Defense = otherStat species.Defense level
          Speed = otherStat species.Speed level
          SpAttack = otherStat species.SpAttack level
          SpDefense = otherStat species.SpDefense level
          Moves = moves
          AtkStage = 0
          DefStage = 0
          SpdStage = 0
          SpAtkStage = 0
          SpDefStage = 0 }
