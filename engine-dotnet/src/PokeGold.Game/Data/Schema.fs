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
      Type2: int }

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
