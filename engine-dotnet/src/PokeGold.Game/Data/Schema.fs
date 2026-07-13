namespace PokeGold.Game.Data

/// The record schema for the generated game-data tables. These types are shared
/// by the build-time generator's output (`Data/Generated/*.Generated.fs`) and
/// the thin runtime accessors (`TypeChart`/`Species`/`Moves`).

/// A species' base stats, decoded from `data/pokemon/base_stats/`. Only the
/// fields the battle slice needs are kept: the six base stats and the (up to
/// two) types. Stat order matches the disassembly's byte order
/// (HP, Attack, Defense, Speed, Sp.Atk, Sp.Def).
type BaseStats =
    { Dex: int
      Name: string
      Hp: int
      Attack: int
      Defense: int
      Speed: int
      SpAttack: int
      SpDefense: int
      Type1: int
      Type2: int
      CatchRate: int
      BaseExp: int
      Item1: string option
      Item2: string option
      GenderRatio: int
      GrowthRate: int }

/// A single evolution entry for a species.
type EvolutionEntry =
    { Method: string
      Param: string
      Param2: string
      Target: string }

/// A level-up learnset entry: at what level a move is learned.
type LearnsetEntry =
    { Level: int
      Move: string }

/// Combined evolution + learnset data for one species.
type EvosAttacks =
    { Species: string
      Evolutions: EvolutionEntry list
      Learnset: LearnsetEntry list }

/// A move's 7-byte data record from `data/moves/moves.asm`. The effect is kept
/// as its constant name (e.g. "EFFECT_NORMAL_HIT") and mapped to an effect
/// command sequence by the battle layer; the type is resolved to its numeric id.
type MoveData =
    { Name: string
      Effect: string
      Power: int
      Type: int
      Accuracy: int
      Pp: int
      EffectChance: int }

/// Which in-game bag pocket an item belongs to.
type Pocket = Item | Ball | KeyItem | TmHm

/// An item's full metadata record, decoded from the disassembly tables.
type ItemData =
    { Id: string
      Name: string
      Price: int
      Pocket: Pocket
      CantSelect: bool
      CantToss: bool
      HeldEffect: string
      Param: int
      FieldMenu: string
      BattleMenu: string
      Description: string }

/// A Pokédex entry for one species (Gold version text).
type DexEntry =
    { Num: int
      Name: string
      Category: string
      HeightDm: int
      WeightHg: int
      Description: string }

/// A single wild encounter slot: a species at a level.
type WildSlot =
    { Level: int
      Species: string }

/// Per-map wild encounter data.
type WildEncounterTable =
    { Map: string
      GrassRate: int * int * int
      GrassMorn: WildSlot list
      GrassDay: WildSlot list
      GrassNite: WildSlot list
      WaterRate: int
      Water: WildSlot list }

/// Source layout selected by the TRAINERTYPE_* byte in trainer party data.
[<RequireQualifiedAccess>]
type TrainerPartyType =
    | Normal
    | Moves
    | Item
    | ItemMoves

/// A single Pokémon in a trainer's party. ExplicitMoves preserves all four
/// source slots for TRAINERTYPE_MOVES/ITEM_MOVES, including NO_MOVE entries.
type TrainerMon =
    { Species: string
      Level: int
      HeldItem: string option
      ExplicitMoves: string list }

/// A trainer's full data.
type TrainerData = {
  Group: string
  Id: int
  Name: string
  PartyType: TrainerPartyType
  Party: TrainerMon list
  BaseReward: int
  /// Packed Attack/Defense/Speed/Special DVs shared by this trainer class.
  Dvs: int
  /// Source trainer-class AI move-scoring layers.
  AiMoveFlags: string list
  /// Source trainer-class item-use and switching policy flags.
  AiItemSwitchFlags: string list
  /// Source trainer-class AI item slots, excluding NO_ITEM.
  AiItems: string list
}
