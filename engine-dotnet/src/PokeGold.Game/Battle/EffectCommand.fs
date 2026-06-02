namespace PokeGold.Game.Battle

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
