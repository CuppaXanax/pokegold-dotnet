namespace PokeGold.Game.Battle

open PokeGold.Game.Data

// ---------------------------------------------------------------------------
//  EffectCommand.fs -- shared battle-effect types
//
//  This file owns the discriminated unions that the entire battle subsystem
//  references.  It is intentionally tiny so that the four parallel "effect
//  family" slices (M13.5-M13.8) can each add new DU cases with minimal merge
//  contention.
//
//  DISPATCH CONTRACT (for M13.5-M13.8 family slices):
//  1.  Add new cases to `EffectCommand` here.
//  2.  Map the EFFECT_* constant string to a command list in
//      `Effects.forMove` (Effects.fs).
//  3.  Handle the new case in `Effects.apply` (Effects.fs).
//  4.  Unknown/unhandled effects with Power > 0 fall back to [Damage];
//      status-only moves with Power = 0 fall back to [].
// ---------------------------------------------------------------------------

/// A tiny deterministic RNG (a linear congruential generator) so battles are
/// reproducible and seedable. Yields bytes in 0..255, matching the hardware's
/// `BattleRandom`.
type Rng = { State: uint32 }

module Rng =
    let create (seed: uint32) : Rng = { State = seed }

    let next (r: Rng) : int * Rng =
        let s = r.State * 1103515245u + 12345u
        int ((s >>> 16) &&& 0xFFu), { State = s }

/// One of the battle stats a stage modifier can target.
/// Later slices may extend stat helpers but the DU cases are stable.
type Stat =
    | Attack
    | Defense
    | Speed
    | SpAttack
    | SpDefense

/// The battle-effect command language: a small DU mirroring the disassembly's
/// per-move effect scripts (`engine/battle/move_effects/...`). A move's effect
/// constant maps to a sequence of these, which the turn loop interprets.
///
/// Family slices add new cases here; keep existing cases stable so parallel
/// work does not break the build.
type EffectCommand =
    | Damage
    | LowerTargetStat of Stat
    | RaiseUserStat of Stat
    /// Recoil: user takes 1/4 of the damage dealt (min 1 HP).
    /// Used by EFFECT_RECOIL_HIT (Struggle, Take Down, Double-Edge, etc.).
    | Recoil

/// Context threaded through effect-command execution for a single move.
/// Carries the user/foe/move/crit/roll/rng/messages so effect commands compose
/// cleanly via fold. Later slices may add fields (e.g. hitCount, lastDamage).
type MoveContext =
    { User: BattleMon
      Foe: BattleMon
      Move: MoveData
      Crit: bool
      Roll: int
      Rng: Rng
      Messages: string list
      /// Damage dealt by the most recent Damage command this turn.
      /// Used by Recoil to compute 1/4 recoil. Initialised to 0.
      LastDamage: int
      /// True when this move is Struggle (skips STAB, no PP deduction).
      IsStruggle: bool }
