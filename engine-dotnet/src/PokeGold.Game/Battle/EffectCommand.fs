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
    | Accuracy
    | Evasion

/// Shared battle-side timer/flag state used by M13.8 effects.
type SideState =
    { PerishCounter: int option
      SafeguardTimer: int option
      ReflectTimer: int option
      LightScreenTimer: int option
      Spikes: int }

    static member Empty =
        { PerishCounter = None
          SafeguardTimer = None
          ReflectTimer = None
          LightScreenTimer = None
          Spikes = 0 }

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
    /// Put the target to sleep (EFFECT_SLEEP). Counter 2-7 turns via Rng.
    | InflictSleep
    /// Poison the target (EFFECT_POISON). Blocked if target is Poison-type or
    /// already has a status condition.
    | InflictPoison
    /// Badly poison the target (EFFECT_TOXIC). Uses a ramping counter.
    | InflictToxic
    /// Paralyze the target (EFFECT_PARALYZE). Blocked if target already statused.
    /// Gen 2 has NO electric-type paralysis immunity.
    | InflictParalyze
    /// Burn the target (EFFECT_BURN / EFFECT_BURN_HIT). Blocked if already statused.
    | InflictBurn
    /// Freeze the target (EFFECT_FREEZE / EFFECT_FREEZE_HIT). Blocked if already statused.
    | InflictFreeze
    /// Infatuate the target when explicit battle genders are opposite.
    | InflictAttract
    // --- M13.4: volatile status commands ---
    /// Confuse the target (EFFECT_CONFUSE). 2-5 turns via Rng (rand & 3 + 2).
    /// Blocked if target already confused or has a substitute.
    | InflictConfuse
    /// Set Flinch on the target (EFFECT_FLINCH_HIT secondary).
    /// Only effective if the user moved first. The pre-move gate checks the flag.
    | SetFlinch
    /// Execute a secondary effect only when the move's effect-chance roll succeeds.
    | EffectChance of EffectCommand
    /// Seed the target with Leech Seed (EFFECT_LEECH_SEED).
    /// Blocked if target is Grass-type or already seeded.
    | ApplyLeechSeed
    /// Trap the target for 3-6 internal turns (EFFECT_TRAP_TARGET).
    /// Blocked if target already trapped or has a substitute.
    | TrapTarget
    /// Create a substitute (EFFECT_SUBSTITUTE). Costs MaxHP/4 from user.
    /// Fails if user HP <= MaxHP/4 or already has a substitute.
    | CreateSubstitute
    /// Set Mist on the user (EFFECT_MIST). Blocks stat-lowering moves.
    | SetMist
    /// Set Focus Energy on the user (EFFECT_FOCUS_ENERGY). Crit stage +1.
    | SetFocusEnergy
    /// Prevent the target from fleeing (EFFECT_MEAN_LOOK).
    | SetMeanLook
    /// Start Sandstorm weather (EFFECT_SANDSTORM).
    | SetSandstorm
    /// Set Perish Song counters on both sides (EFFECT_PERISH_SONG).
    | SetPerishSong
    /// Set Safeguard on the user side (EFFECT_SAFEGUARD).
    | SetSafeguard
    /// Set Reflect on the user side (EFFECT_REFLECT).
    | SetReflect
    /// Set Light Screen on the user side (EFFECT_LIGHT_SCREEN).
    | SetLightScreen
    /// Set entry hazards on the foe side (EFFECT_SPIKES).
    | SetSpikes
    /// Set Nightmare on the target (EFFECT_NIGHTMARE).
    | SetNightmare
    /// Apply Curse to the target or user (EFFECT_CURSE).
    | SetCurse
    // --- M13.9: remaining move-effect commands ---
    | HealUser
    | WeatherHeal
    | SetRainDance
    | SetSunnyDay
    | Swagger
    | ResetStats
    | Protect
    | Endure
    | BellyDrum
    | PsychUp
    | DestinyBond
    | PainSplit
    | AllUpHit
    | SnoreDamage
    | SetEncore
    | SetDisable
    | DefrostFoe
    | ReducePP
    | CounterDamage
    | MirrorCoatDamage
    | HealBellEffect
    | SetLockOn
    | SetForesight

    // -----------------------------------------------------------------------
    //  M13.5: damage-shaping & fixed damage family
    // -----------------------------------------------------------------------

    /// EFFECT_LEVEL_DAMAGE: damage = user's level (Seismic Toss, Night Shade).
    | LevelDamage
    /// EFFECT_PSYWAVE: damage = random 1 .. floor(level * 1.5), rejection-sampled.
    | PsywaveDamage
    /// EFFECT_SUPER_FANG: damage = target current HP / 2, min 1.
    | SuperFangDamage
    /// EFFECT_STATIC_DAMAGE: damage = move.Power (Sonicboom=20, Dragon Rage=40).
    | StaticDamage
    /// EFFECT_OHKO: one-hit KO (Horn Drill, Fissure, Guillotine).
    /// Gen-2 accuracy = attacker level - target level + move accuracy (in %).
    /// Fails outright if target level >= attacker level.
    | OhkoDamage
    /// EFFECT_FALSE_SWIPE: normal damage, but capped to leave target at >= 1 HP.
    | FalseSwipeDamage
    /// EFFECT_REVERSAL / EFFECT_FLAIL: power based on user HP-ratio table.
    | ReversalDamage
    /// EFFECT_RETURN: power = friendship / 2.5 (our model uses 0 friendship).
    | ReturnDamage
    /// EFFECT_FRUSTRATION: power = (255 - friendship) / 2.5.
    | FrustrationDamage
    /// EFFECT_PRESENT: random tiers (40/80/120 damage or heal 25% target HP).
    /// RNG draw: 1 byte → thresholds 102/179/204/255.
    | PresentDamage
    /// EFFECT_MAGNITUDE: random magnitude 4-10, each with a power.
    /// RNG draw: 1 byte → threshold table.
    | MagnitudeDamage
    /// EFFECT_HIDDEN_POWER: type+power from DVs. DVs=0 in our model →
    /// power=31, type=FIGHTING (faithful computation).
    | HiddenPowerDamage
    /// EFFECT_FURY_CUTTER: power doubles each consecutive hit (max 16x).
    /// `hitCount` in MoveContext tracks consecutive uses; reset on miss.
    | FuryCutterDamage
    /// EFFECT_ROLLOUT: 5-turn doubling lock-in. Defense Curl doubles further.
    /// Power ramp implemented here; lock-in turn management is a M13.7 hand-off.
    /// `hitCount` in MoveContext tracks the rollout turn (1-5).
    | RolloutDamage
    /// EFFECT_TRIPLE_KICK: 3 hits at escalating power (base, 2x, 3x).
    | TripleKickDamage
    /// EFFECT_BEAT_UP: one hit per healthy party member. Simplified to a single
    /// hit (wild battles have 1 party member) with base power = level-based.
    | BeatUpDamage

    /// EFFECT_LEECH_HIT: deal damage, heal user by half dealt (min 1).
    | DrainDamage
    /// EFFECT_DREAM_EATER: drain, but only works vs sleeping target.
    | DreamEaterDamage
    /// EFFECT_SELFDESTRUCT: user faints. Damage calc halves target defense.
    | SelfdestructDamage
    /// EFFECT_JUMP_KICK: on miss, user takes crash damage = 1/8 max HP (min 1).
    /// On hit, normal damage.
    | JumpKickDamage
    /// EFFECT_PAY_DAY: normal damage + "Coins scattered everywhere!" message.
    | PayDayDamage
    /// EFFECT_RAPID_SPIN: damage + clear leech seed/trap on user.
    | RapidSpinDamage
    /// EFFECT_THIEF: damage + steal item message (item model is stub).
    | ThiefDamage
    /// EFFECT_RAGE: damage + set rage flag (atk-up-on-hit is M13.7 turn-state).
    | RageDamage
    /// Begin a two-turn charging move on the user's turn.
    | BeginCharging
    /// Set recharge on a Hyper Beam-style move.
    | BeginRecharge
    /// Lock the user into a rampage move for the next turns.
    | BeginRampage
    /// Seed Future Sight on the user for an end-of-turn payoff.
    | BeginFutureSight

    /// EFFECT_MULTI_HIT: 2-5 hits with 3/8, 3/8, 1/8, 1/8 distribution.
    /// The hit count is determined by RNG and stored in MoveContext.MultiHitCount.
    | MultiHitDamage
    /// EFFECT_DOUBLE_HIT: always 2 hits.
    | DoubleHitDamage
    /// EFFECT_POISON_MULTI_HIT (Twineedle): 2 hits + 20% poison chance per hit.
    | PoisonMultiHitDamage

    /// EFFECT_GUST/TWISTER/STOMP/EARTHQUAKE: normal damaging hit.
    /// Hook for 2x-vs-Fly/Dig/Minimize (M13.7); flinch secondary (M13.6).
    | ConditionalDoubleDamage

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
      IsStruggle: bool
      /// M13.5: Fury Cutter consecutive hit counter (0 = first use).
      /// Persisted across turns by the caller for EFFECT_FURY_CUTTER.
      FuryCutterCount: int
      /// M13.5: Rollout turn counter (0 = first use, max 4).
      /// Persisted across turns by the caller for EFFECT_ROLLOUT.
      RolloutCount: int
      /// M13.5: Defense Curl active flag for Rollout power doubling.
      DefenseCurlUsed: bool
      /// M13.5: Friendship value (0-255) for Return/Frustration.
      Friendship: int
      /// True when the user is the player's mon (used for side-state effects).
      UserIsPlayer: bool
      /// The current side state for the player team.
      PlayerSide: SideState
      /// The current side state for the enemy team.
      EnemySide: SideState
      /// Weather timer for the battle. None = clear.
      WeatherTimer: int option
      /// Weather type for the battle. None = clear, Some "RAIN"/"SUN"/"SAND".
      WeatherType: string option }
