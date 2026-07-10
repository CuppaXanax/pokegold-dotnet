namespace PokeGold.Game.Battle

open PokeGold.Game.Data

/// How a battle ended, from the player's perspective.
type Outcome =
    | Win
    | Lose
    | Ran

type TrainerBattleContext =
    { Group: string
      Id: string
      Name: string
      ClassName: string
      WinText: string option
      LossText: string option
      BaseReward: int option }

type BattleKind =
    | WildBattle
    | TrainerBattle of TrainerBattleContext

/// The full state of a wild battle. Immutable: each turn produces a new state.
/// `Messages` is the queue of lines the battle scene reveals one at a time;
/// `Outcome` is set once the battle resolves.
type BattleState =
    { Player: BattleMon
      Enemy: BattleMon
      PlayerTeam: BattleMon list
      EnemyTeam: BattleMon list
      Kind: BattleKind
      Messages: string list
      Outcome: Outcome option
      Rng: Rng
      WeatherTimer: int option
      WeatherType: string option
      PlayerSide: SideState
      EnemySide: SideState }

module Battle =

    let private trainerLabel (ctx: TrainerBattleContext) =
        let name = if System.String.IsNullOrWhiteSpace ctx.Name || ctx.Name = "?" then "???" else ctx.Name
        if ctx.ClassName = "" then name else $"{ctx.ClassName} {name}"

    let private trainerClassLabel (ctx: TrainerBattleContext) =
        if System.String.IsNullOrWhiteSpace ctx.ClassName then "TRAINER" else ctx.ClassName

    let private openingMessages kind (enemy: BattleMon) =
        match kind with
        | WildBattle -> [ $"Wild {enemy.Species.Name} appeared!" ]
        | TrainerBattle ctx -> [ $"{trainerLabel ctx} wants to battle!"; $"{trainerClassLabel ctx} sent out {enemy.Species.Name}!" ]

    let private faintedEnemyText kind (enemy: BattleMon) =
        match kind with
        | WildBattle -> $"Wild {enemy.Species.Name} fainted!"
        | TrainerBattle _ -> $"{enemy.Species.Name} fainted!"

    let private sentOutEnemyText kind (enemy: BattleMon) =
        match kind with
        | WildBattle -> $"{enemy.Species.Name} appeared!"
        | TrainerBattle ctx -> $"{trainerClassLabel ctx} sent out {enemy.Species.Name}!"

    /// Start a wild battle between the player's mon and a wild one.
    let createWild (player: BattleMon) (enemy: BattleMon) (seed: uint32) : BattleState =
        { Player = player
          Enemy = enemy
          PlayerTeam = [ player ]
          EnemyTeam = [ enemy ]
          Kind = WildBattle
          Messages = openingMessages WildBattle enemy
          Outcome = None
          Rng = Rng.create seed
          WeatherTimer = None
          WeatherType = None
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty }

    let private createTeamWith kind (playerTeam: BattleMon list) (enemyTeam: BattleMon list) (seed: uint32) : BattleState =
        let player = List.head playerTeam
        let enemy = List.head enemyTeam
        { Player = player
          Enemy = enemy
          PlayerTeam = playerTeam
          EnemyTeam = enemyTeam
          Kind = kind
          Messages = openingMessages kind enemy
          Outcome = None
          Rng = Rng.create seed
          WeatherTimer = None
          WeatherType = None
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty }

    /// Start a wild team battle using the player and enemy teams in their current order.
    let createTeam (playerTeam: BattleMon list) (enemyTeam: BattleMon list) (seed: uint32) : BattleState =
        createTeamWith WildBattle playerTeam enemyTeam seed

    let createTrainer (context: TrainerBattleContext) (playerTeam: BattleMon list) (enemyTeam: BattleMon list) (seed: uint32) : BattleState =
        createTeamWith (TrainerBattle context) playerTeam enemyTeam seed

    /// Back-compatible wrapper for older tests and demo callers.
    let create (player: BattleMon) (enemy: BattleMon) (seed: uint32) : BattleState =
        createWild player enemy seed

    let isOver (s: BattleState) : bool = s.Outcome.IsSome

    let isTrainerBattle (s: BattleState) =
        match s.Kind with
        | TrainerBattle _ -> true
        | WildBattle -> false

    let private heldItemData (m: BattleMon) =
        m.HeldItem |> Option.bind (fun itemId -> Items.byId |> Map.tryFind itemId)

    let private heldEffect effect (m: BattleMon) =
        heldItemData m |> Option.exists (fun item -> item.HeldEffect = effect)

    let private heldParam effect (m: BattleMon) =
        heldItemData m
        |> Option.filter (fun item -> item.HeldEffect = effect)
        |> Option.map (fun item -> item.Param)

    // -----------------------------------------------------------------------
    //  Turn phases (faithful to effect_commands.asm DoMove/DoTurn ordering)
    //
    //  select -> pre-move status gates -> execute move -> secondary effects
    //         -> faint check -> end-of-turn residuals -> end-of-turn faint check
    //
    //  Each phase is a small pure function. The orchestrator (chooseMove) calls
    //  them in order. Later slices fill in the stubs.
    // -----------------------------------------------------------------------

    // -- Phase: pre-move status gates ----------------------------------------

    /// Pre-move status gate, faithful to `CheckTurn` / `BattleCommand_CheckTurn`
    /// in `engine/battle/effect_commands.asm` (l.121-342).
    ///
    /// Gate order (M13.3 non-volatile + M13.4/M13.6 volatile):
    ///   1. Sleep -- decrement counter; wake at 0, else "fast asleep!" (no RNG draw)
    ///   2. Freeze -- "frozen solid!"; Flame Wheel / Sacred Fire self-defrost (no RNG)
    ///   3. Flinch -- flinched can't move (no RNG draw). Cleared after check.
    ///   4. Paralysis -- 25% full-para (1 RNG draw: roll < 64 = can't act)
    ///   5. Confusion -- dec counter; at 0 snap out; else 50% self-hit (1 RNG draw)
    ///   6. Attract -- 50% chance to fail the turn when the mon is infatuated
    ///
    /// Returns: (canAct, confusionSelfHit, updatedUser, messages, rng)
    /// When confusionSelfHit = true, the caller must apply a 40-power typeless
    /// physical hit from the user to itself (using user's own atk/def).
    let private preMoveStatusCheck (user: BattleMon) (_foe: BattleMon) (selectedMove: MoveData) (selectedMoveIndex: int) (userMovedFirst: bool) (rng: Rng)
        : bool * bool * BattleMon * string list * Rng =

        // 1. Sleep gate
        match user.Status with
        | Sleep turnsLeft ->
            let remaining = turnsLeft - 1
            if selectedMove.Effect = "EFFECT_SLEEP_TALK" && remaining > 0 then
                let user' = { user with Status = Sleep remaining }
                (true, false, user', [ $"{user.Species.Name} is fast asleep!" ], rng)
            elif remaining = 0 then
                let user' = { user with Status = Healthy }
                (true, false, user', [ $"{user.Species.Name} woke up!" ], rng)
            else
                let user' = { user with Status = Sleep remaining }
                (false, false, user', [ $"{user.Species.Name} is fast asleep!" ], rng)
        | _ ->

        // 2. Freeze gate
        match user.Status with
        | Freeze when selectedMove.Name = "FLAME_WHEEL" || selectedMove.Name = "SACRED_FIRE" ->
            (true, false, user, [], rng)
        | Freeze ->
            (false, false, user, [ $"{user.Species.Name} is frozen solid!" ], rng)
        | _ ->

        // 3. Flinch gate (effect_commands.asm l.223-233).
        // Flinch only matters if the flinch flag was set by the opponent who
        // moved first. The pre-move gate receives the move-order context so it
        // can skip the flinch block when the user moved first this turn.
        let user, flinchBlock =
            if user.Volatile.Flinch then
                let cleared = { user with Volatile = { user.Volatile with Flinch = false } }
                if not userMovedFirst then (cleared, true)
                else (cleared, false)
            else (user, false)
        if flinchBlock then
            (false, false, user, [ $"{user.Species.Name} flinched!" ], rng)
        else
            let user, disableMessages =
                match user.Volatile.DisableTimer with
                | Some turns ->
                    let remaining = turns - 1
                    if remaining <= 0 then
                        let vol = { user.Volatile with DisableTimer = None; DisabledMoveIndex = None }
                        { user with Volatile = vol }, [ "Disabled no more!" ]
                    else
                        let vol = { user.Volatile with DisableTimer = Some remaining }
                        { user with Volatile = vol }, []
                | None -> user, []

            // 4. Recharge gate: Hyper Beam-style moves force a skipped turn.
            if user.Volatile.Recharge then
                let user' = { user with Volatile = { user.Volatile with Recharge = false } }
                (false, false, user', disableMessages @ [ $"{user.Species.Name} must recharge!" ], rng)
            else
                // 5. Paralysis full-para gate (25% = 64/256)
                let mutable canAct = true
                let mutable selfHit = false
                let mutable messages: string list = disableMessages
                let mutable rng' = rng

                match user.Status with
                | Paralysis ->
                    let roll, nextRng = Rng.next rng'
                    rng' <- nextRng
                    if roll < 64 then
                        canAct <- false
                        messages <- messages @ [ $"{user.Species.Name} is fully paralyzed!" ]
                | _ -> ()

                if not canAct then
                    (false, selfHit, user, messages, rng')
                else
                    // 6. Confusion gate (effect_commands.asm l.253-288).
                    let canAct, selfHit, user, messages, rng' =
                        match user.Volatile.Confusion with
                        | Some turns ->
                            let remaining = turns - 1
                            if remaining = 0 then
                                let vol = { user.Volatile with Confusion = None }
                                let user' = { user with Volatile = vol }
                                (true, false, user', messages @ [ $"{user.Species.Name} snapped out of confusion!" ], rng')
                            else
                                let vol = { user.Volatile with Confusion = Some remaining }
                                let user' = { user with Volatile = vol }
                                let msgs = messages @ [ $"{user.Species.Name} is confused!" ]
                                let roll, nextRng = Rng.next rng'
                                if roll < 128 then
                                    (false, true, user', msgs @ [ $"{user.Species.Name} hurt itself in its confusion!" ], nextRng)
                                else
                                    (true, false, user', msgs, nextRng)
                        | None ->
                            (true, false, user, messages, rng')

                    // 7. Attract gate.
                    let canAct, selfHit, user, messages, rng' =
                        if canAct && user.Volatile.Attracted then
                            let roll, rng' = Rng.next rng'
                            if roll < 128 then
                                (false, selfHit, user, messages @ [ $"{user.Species.Name} is immobilized by attraction!" ], rng')
                            else
                                (true, selfHit, user, messages, rng')
                        else
                            (canAct, selfHit, user, messages, rng')

                    if canAct && user.Volatile.DisabledMoveIndex = Some selectedMoveIndex then
                        (false, selfHit, user, messages @ [ $"{selectedMove.Name} is disabled!" ], rng')
                    else
                        (canAct, selfHit, user, messages, rng')

    // -- Phase: confusion self-hit -------------------------------------------

    /// 40-power typeless physical self-hit (HitSelfInConfusion,
    /// effect_commands.asm l.2861). Uses the user's own Attack vs own Defense,
    /// no crit, no type effectiveness, no STAB, max roll (255).
    /// Faithful to HitSelfInConfusion + BattleCommand_DamageCalc with the
    /// confusion-specific overrides (typeless = type 0xFF, wCriticalHit = 0).
    let private confusionSelfHitDamage (user: BattleMon) : int =
        let atk =
            let raw = BattleMon.effectiveAttack user
            if user.Status = Burn then max 1 (raw / 2) else raw
        let def = BattleMon.effectiveDefense user
        // (((2*Level/5 + 2) * 40 * Atk) / Def) / 50 + 2
        let mutable d = 2 * user.Level / 5 + 2
        d <- d * 40
        d <- d * atk
        d <- d / def
        d <- d / 50
        // No crit doubling. Cap + min damage floor.
        d <- min 997 d + 2
        // No STAB, no type effectiveness. Max roll (faithful: confusion
        // self-hit uses a fixed spread; the disassembly calls DamageCalc
        // which does roll, but confusion doesn't re-roll — we use 255).
        d <- d * 255 / 255
        d

    // -- Phase: accuracy / miss check (CheckHit) ------------------------------

    /// Accuracy/miss check, faithful to `BattleCommand_CheckHit` in
    /// `engine/battle/effect_commands.asm`. Returns `(hit, rng')`.
    let private checkHit (user: BattleMon) (foe: BattleMon) (move: MoveData) (rng: Rng) (weatherType: string option)
        : bool * Rng =
        if move.Effect = "EFFECT_ALWAYS_HIT" then
            (true, rng)
        elif move.Effect = "EFFECT_THUNDER" && weatherType = Some "RAIN" then
            (true, rng)
        else

        let accByte =
            if move.Effect = "EFFECT_THUNDER" && weatherType = Some "SUN" then
                128
            else
                move.Accuracy * 255 / 100

        let modifiedAcc =
            let staged = BattleMon.applyAccEvaStages accByte user.AccStage foe.EvaStage
            match heldParam "HELD_BRIGHTPOWDER" foe with
            | Some pct -> max 1 (staged * (100 - pct) / 100)
            | None -> staged

        if modifiedAcc >= 255 then
            (true, rng)
        else

        let roll, rng' = Rng.next rng
        (roll < modifiedAcc, rng')

    // -- Phase: roll hit (crit + damage spread) ------------------------------

    let private rollHit (critStage: int) (rng: Rng) : bool * int * Rng =
        let critByte, rng = Rng.next rng
        let spread, rng = Rng.next rng
        let threshold = CriticalHit.thresholds.[min critStage (CriticalHit.thresholds.Length - 1)]
        let crit = critByte < threshold
        let roll = Damage.MinRoll + spread % (Damage.MaxRoll - Damage.MinRoll + 1)
        crit, roll, rng

    let private critStageFor (user: BattleMon) (move: MoveData) =
        let itemBoost = if heldEffect "HELD_CRITICAL_UP" user then 1 else 0
        min (CriticalHit.thresholds.Length - 1) (CriticalHit.critStage user.Volatile.FocusEnergy move + itemBoost)

    // -- Phase: execute move -------------------------------------------------

    /// Execute one mon's move against the other using the MoveContext pattern.
    /// Effect commands fold over the context, accumulating state changes and
    /// messages. Returns updated (user, foe, messages, rng).
    ///
    /// RNG draw order per move (faithful to GSC command sequence):
    ///   1. Accuracy roll  — checkHit (skipped for EFFECT_ALWAYS_HIT / acc $FF)
    ///   2. Crit roll      — rollHit  (skipped on miss)
    ///   3. Spread roll    — rollHit  (skipped on miss)
    let private executeMove (user: BattleMon) (foe: BattleMon) (move: MoveData) (isStruggle: bool) (rng: Rng) (userIsPlayer: bool) (targetIsSwitching: bool) (battle: BattleState)
        : BattleMon * BattleMon * string list * Rng * SideState * SideState * int option * string option * int * bool =
        let intro = $"{user.Species.Name} used {move.Name}!"

        let runBeatUp rng =
            let rawTeam = if userIsPlayer then battle.PlayerTeam else battle.EnemyTeam
            let team = rawTeam |> List.mapi (fun i mon -> if i = 0 then user else mon)
            let mutable foe = foe
            let mutable rng = rng
            let mutable msgs = [ intro ]
            let mutable totalDamage = 0
            let mutable hitAtLeastOnce = false

            for participant in team do
                if not (BattleMon.isFainted foe) && participant.Hp > 0 && participant.Status = Healthy then
                    let hit, rngAfterHit = checkHit user foe move rng battle.WeatherType
                    rng <- rngAfterHit
                    if hit then
                        let crit, roll, rngAfterRoll = rollHit (critStageFor user move) rng
                        rng <- rngAfterRoll
                        let foeAfterHit, dmg, hitMsgs = Effects.applyBeatUpHit participant foe move crit roll
                        hitAtLeastOnce <- dmg > 0 || hitAtLeastOnce
                        totalDamage <- totalDamage + dmg
                        msgs <- msgs @ [ $"{participant.Species.Name} attacked!" ]
                        if crit then msgs <- msgs @ [ "A critical hit!" ]
                        msgs <- msgs @ hitMsgs
                        foe <-
                            if foe.Volatile.Rage && dmg > 0 then
                                { foeAfterHit with Volatile = { foeAfterHit.Volatile with RageCounter = min 255 (foeAfterHit.Volatile.RageCounter + 1) } }
                            else
                                foeAfterHit

            if not hitAtLeastOnce then
                msgs <- msgs @ [ "But it failed!" ]

            user, foe, msgs, rng, battle.PlayerSide, battle.EnemySide, battle.WeatherTimer, battle.WeatherType, totalDamage, hitAtLeastOnce

        let runHit crit roll rng =
            let ctx : MoveContext =
                { User = user
                  Foe = foe
                  Move = move
                  Crit = crit
                  Roll = roll
                  Rng = rng
                  Messages = [ intro ]
                  LastDamage = 0
                  IsStruggle = isStruggle
                  FuryCutterCount = 0
                  RolloutCount = 0
                  DefenseCurlUsed = user.Volatile.Curled
                  Friendship = 0
                  UserIsPlayer = userIsPlayer
                  PlayerSide = battle.PlayerSide
                  EnemySide = battle.EnemySide
                  WeatherTimer = battle.WeatherTimer
                  WeatherType = battle.WeatherType }

            let ctx =
                Effects.forMove move
                |> List.fold (fun (c: MoveContext) cmd ->
                    Effects.applyCtxWith targetIsSwitching c cmd
                ) ctx

            let foe =
                if ctx.Foe.Volatile.Rage && ctx.LastDamage > 0 then
                    { ctx.Foe with Volatile = { ctx.Foe.Volatile with RageCounter = min 255 (ctx.Foe.Volatile.RageCounter + 1) } }
                else
                    ctx.Foe

            let ctx = { ctx with Foe = foe }
            ctx.User, ctx.Foe, ctx.Messages, ctx.Rng, ctx.PlayerSide, ctx.EnemySide, ctx.WeatherTimer, ctx.WeatherType, ctx.LastDamage, true

        if move.Effect = "EFFECT_BEAT_UP" && not isStruggle then
            runBeatUp rng
        elif move.Effect = "EFFECT_FUTURE_SIGHT" && not isStruggle then
            runHit false Damage.MaxRoll rng
        elif move.Effect = "EFFECT_OHKO" && not isStruggle then
            runHit false Damage.MaxRoll rng
        elif move.Effect = "EFFECT_TRANSFORM" && not isStruggle then
            runHit false Damage.MaxRoll rng
        elif move.Effect = "EFFECT_CONVERSION" && not isStruggle then
            runHit false Damage.MaxRoll rng
        elif move.Effect = "EFFECT_METRONOME" && not isStruggle then
            runHit false Damage.MaxRoll rng
        elif move.Effect = "EFFECT_MIRROR_MOVE" && not isStruggle then
            runHit false Damage.MaxRoll rng
        elif move.Effect = "EFFECT_MIMIC" && not isStruggle then
            let hit, rng = checkHit user foe move rng battle.WeatherType
            if hit then
                runHit false Damage.MaxRoll rng
            else
                let msgs = [ intro; $"{user.Species.Name}'s attack missed!" ]
                (user, foe, msgs, rng, battle.PlayerSide, battle.EnemySide, battle.WeatherTimer, battle.WeatherType, 0, false)
        elif move.Effect = "EFFECT_CONVERSION2" && not isStruggle then
            let hit, rng = checkHit user foe move rng battle.WeatherType
            if hit then
                runHit false Damage.MaxRoll rng
            else
                let msgs = [ intro; $"{user.Species.Name}'s attack missed!" ]
                (user, foe, msgs, rng, battle.PlayerSide, battle.EnemySide, battle.WeatherTimer, battle.WeatherType, 0, false)
        elif move.Effect = "EFFECT_JUMP_KICK" && not isStruggle then
            let crit, roll, rng = rollHit (critStageFor user move) rng
            let hit, rng = checkHit user foe move rng battle.WeatherType
            if hit then
                runHit crit roll rng
            else
                let msgs = [ intro; $"{user.Species.Name}'s attack missed!" ]
                let crash =
                    if Damage.effectivenessTimesTen move foe = 0 then 0
                    else max 1 ((Damage.calc user foe move crit roll isStruggle) / 8)
                let user = { user with Hp = max 0 (user.Hp - crash) }
                let msgs =
                    if crash > 0 then msgs @ [ $"{user.Species.Name} kept going and crashed!" ]
                    else msgs
                (user, foe, msgs, rng, battle.PlayerSide, battle.EnemySide, battle.WeatherTimer, battle.WeatherType, 0, false)
        else
            // Struggle always hits (effect_commands.asm: EFFECT_ALWAYS_HIT path).
            let hit, rng =
                if isStruggle then (true, rng)
                else checkHit user foe move rng battle.WeatherType

            if not hit then
                let msgs = [ intro; $"{user.Species.Name}'s attack missed!" ]
                (user, foe, msgs, rng, battle.PlayerSide, battle.EnemySide, battle.WeatherTimer, battle.WeatherType, 0, false)
            else
                let crit, roll, rng = rollHit (critStageFor user move) rng
                runHit crit roll rng

    // -- Phase: end-of-turn residuals (between turns) ------------------------

    /// End-of-turn residual effects, called after both sides have acted and
    /// mid-turn faint checks have passed. Returns updated (player, enemy, msgs, rng).
    ///
    /// Faithful order from HandleBetweenTurnEffects (core.asm l.205) and
    /// ResidualDamage (core.asm l.949). In the disassembly, ResidualDamage runs
    /// per-side after each move; here we consolidate into betweenTurns for both
    /// sides (player then enemy) to keep the code structure clean.
    ///
    /// Slot ordering:
    ///   1. Future Sight countdown              — stub (M13.7)
    ///   2. Weather (sandstorm chip, timer)      — stub (M13.8)
    ///   3. Wrap/Bind/Clamp chip                 — stub (M13.4)
    ///   4. Perish Song countdown                — stub (M13.8)
    ///   5. Leftovers / items                    — LEFTOVERS filled
    ///   6. Defrost (10% random thaw per turn)   — FILLED (M13.3)
    ///   7. Poison/Toxic tick (player then enemy) — FILLED (M13.3)
    ///   8. Burn tick (player then enemy)         — FILLED (M13.3)
    ///   9. Leech Seed drain                     — stub (M13.4)
    ///  10. Nightmare                            — stub (M13.4)
    ///  11. Curse chip                           — stub (M13.4)
    ///  12. Safeguard timer                      — stub (M13.8)
    ///  13. Reflect/Light Screen timer           — stub (M13.8)
    ///  14. Encore timer                         — stub (M13.4)
    ///  15. Disable timer                        — stub (M13.4)

    /// Apply one mon's poison/toxic/burn residual. Returns (mon, msgs).
    /// Poison: MaxHP/8 (min 1). core.asm GetEighthMaxHP.
    /// Toxic: MaxHP/16 * counter (counter increments each tick). core.asm l.989.
    /// Burn: MaxHP/8 (min 1). core.asm same path as poison.
    let private applyResidual (m: BattleMon) : BattleMon * string list =
        if BattleMon.isFainted m then (m, [])
        else
        match m.Status with
        | Poison ->
            let dmg = max 1 (m.MaxHp / 8)
            let m' = { m with Hp = max 0 (m.Hp - dmg) }
            (m', [ $"{m.Species.Name} is hurt by poison!" ])
        | BadPoison counter ->
            // Increment counter (starts at 0, first tick uses counter=1).
            let n = counter + 1
            let tick = max 1 (m.MaxHp / 16)
            let dmg = tick * n
            let m' = { m with Hp = max 0 (m.Hp - dmg); Status = BadPoison n }
            (m', [ $"{m.Species.Name} is hurt by poison!" ])
        | Burn ->
            let dmg = max 1 (m.MaxHp / 8)
            let m' = { m with Hp = max 0 (m.Hp - dmg) }
            (m', [ $"{m.Species.Name} is hurt by its burn!" ])
        | _ -> (m, [])

    /// 10% random thaw (HandleDefrost, core.asm l.1468-1498).
    /// `BattleRandom; cp 10 percent` -> roll < 25 = thaw.
    /// Only fires if the mon was NOT just frozen this turn (wPlayerJustGotFrozen).
    /// We don't track "just got frozen" because freeze infliction is now
    /// modeled as a status effect; the gate is conservative and keeps the
    /// turn-order logic pure.
    /// for now, always eligible.
    let private applyDefrost (m: BattleMon) (rng: Rng) : BattleMon * string list * Rng =
        match m.Status with
        | Freeze ->
            let roll, rng' = Rng.next rng
            if roll < 25 then
                let m' = { m with Status = Healthy }
                (m', [ $"{m.Species.Name} was defrosted!" ], rng')
            else
                (m, [], rng')
        | _ -> (m, [], rng)

    let private applyHeldItemResidual (m: BattleMon) : BattleMon * string list =
        if BattleMon.isFainted m then (m, [])
        else
            match m.HeldItem with
            | Some itemId ->
                match Items.byId |> Map.tryFind itemId with
                | Some item ->
                    let consumeWith (updated: BattleMon) =
                        { updated with HeldItem = None }, [ $"{m.Species.Name} ate {item.Name}!" ]

                    match item.HeldEffect, m.Status, m.Volatile.Confusion with
                    | "HELD_HEAL_POISON", Poison, _
                    | "HELD_HEAL_POISON", BadPoison _, _ ->
                        consumeWith { m with Status = Healthy }
                    | "HELD_HEAL_PARALYZE", Paralysis, _ ->
                        consumeWith { m with Status = Healthy }
                    | "HELD_HEAL_BURN", Burn, _ ->
                        consumeWith { m with Status = Healthy }
                    | "HELD_HEAL_FREEZE", Freeze, _ ->
                        consumeWith { m with Status = Healthy }
                    | "HELD_HEAL_SLEEP", Sleep _, _ ->
                        consumeWith { m with Status = Healthy }
                    | "HELD_HEAL_CONFUSION", _, Some _ ->
                        consumeWith { m with Volatile = { m.Volatile with Confusion = None } }
                    | "HELD_HEAL_STATUS", status, _ when status <> Healthy ->
                        consumeWith { m with Status = Healthy }
                    | "HELD_HEAL_STATUS", _, Some _ ->
                        consumeWith { m with Volatile = { m.Volatile with Confusion = None } }
                    | _ ->
                        match item.HeldEffect with
                        | "HELD_LEFTOVERS" when m.Hp < m.MaxHp ->
                            let heal = max 1 (m.MaxHp / 16)
                            let healed = min m.MaxHp (m.Hp + heal)
                            { m with Hp = healed }, [ $"{m.Species.Name} restored HP with {item.Name}!" ]
                        | "HELD_BERRY" when m.Hp <= m.MaxHp / 2 ->
                            let heal = max 1 item.Param
                            let healed = min m.MaxHp (m.Hp + heal)
                            { m with Hp = healed; HeldItem = None }, [ $"{m.Species.Name} ate {item.Name}!" ]
                        | _ -> m, []
                | _ -> m, []
            | None -> m, []

    /// Future Sight payoff: countdown at end-of-turn and damage the foe on expiry.
    let private applyFutureSight (m: BattleMon) (other: BattleMon) (rng: Rng) (weatherType: string option) : BattleMon * BattleMon * string list * Rng =
        match m.Volatile.FutureSightCounter, m.Volatile.FutureSightMove, m.Volatile.FutureSightDamage with
        | Some turns, Some move, Some storedDamage ->
            let remaining = turns - 1
            if remaining = 1 then
                let vol = { m.Volatile with FutureSightCounter = None; FutureSightMove = None; FutureSightDamage = None }
                let m = { m with Volatile = vol }
                let hit, rng = checkHit m other move rng weatherType
                if hit then
                    let spread, rng = Rng.next rng
                    let roll = Damage.MinRoll + spread % (Damage.MaxRoll - Damage.MinRoll + 1)
                    let dmg = storedDamage * roll / Damage.MaxRoll
                    let other' = { other with Hp = max 0 (other.Hp - dmg) }
                    (m, other', [ $"{other'.Species.Name} was hit by Future Sight!" ], rng)
                else
                    (m, other, [ $"{m.Species.Name}'s Future Sight missed!" ], rng)
            elif remaining > 0 then
                let vol = { m.Volatile with FutureSightCounter = Some remaining }
                ({ m with Volatile = vol }, other, [], rng)
            else
                let vol = { m.Volatile with FutureSightCounter = None; FutureSightMove = None; FutureSightDamage = None }
                ({ m with Volatile = vol }, other, [], rng)
        | _ -> (m, other, [], rng)

    /// Wrap/Bind/Clamp chip (HandleWrap, core.asm l.1153).
    /// Decrement trap counter; at 0 release, else chip MaxHP/16 (min 1).
    /// If the mon has a substitute, wrap is suppressed (faithful to l.1183).
    let private applyWrap (m: BattleMon) : BattleMon * string list =
        if BattleMon.isFainted m then (m, [])
        else
        match m.Volatile.Trapped with
        | None -> (m, [])
        | Some _ when m.Volatile.Substitute.IsSome ->
            // Substitute suppresses wrap chip (core.asm l.1183).
            (m, [])
        | Some turns ->
            let remaining = turns - 1
            if remaining = 0 then
                let vol = { m.Volatile with Trapped = None }
                let m' = { m with Volatile = vol }
                (m', [ $"{m.Species.Name} was released!" ])
            else
                let chip = max 1 (m.MaxHp / 16)
                let vol = { m.Volatile with Trapped = Some remaining }
                let m' = { m with Hp = max 0 (m.Hp - chip); Volatile = vol }
                (m', [ $"{m.Species.Name} is hurt by the trap!" ])

    /// Leech Seed drain (ResidualDamage, core.asm l.1008-1029).
    /// Drains MaxHP/8 (min 1) from the seeded mon and heals the other side.
    /// Returns (seeded, other, msgs).
    let private applyLeechSeed (seeded: BattleMon) (other: BattleMon) : BattleMon * BattleMon * string list =
        if BattleMon.isFainted seeded then (seeded, other, [])
        else
        if not seeded.Volatile.LeechSeed then (seeded, other, [])
        else
            let drain = max 1 (seeded.MaxHp / 8)
            let actualDrain = min seeded.Hp drain
            let seeded' = { seeded with Hp = max 0 (seeded.Hp - drain) }
            let healed = min other.MaxHp (other.Hp + actualDrain)
            let other' = { other with Hp = healed }
            (seeded', other', [ $"{seeded.Species.Name}'s health is sapped by Leech Seed!" ])

    let private applyRampage (m: BattleMon) (rng: Rng) : BattleMon * string list * Rng =
        match m.Volatile.Rampage with
        | Some turns when turns > 1 ->
            let vol = { m.Volatile with Rampage = Some (turns - 1) }
            ({ m with Volatile = vol }, [ $"{m.Species.Name} is still rampaging!" ], rng)
        | Some _ ->
            let roll, rng = Rng.next rng
            let vol = { m.Volatile with Rampage = None; Confusion = Some (2 + (roll &&& 1)) }
            ({ m with Volatile = vol }, [ $"{m.Species.Name} became confused after rampaging!" ], rng)
        | None -> (m, [], rng)

    let private applyWeather (m: BattleMon) : BattleMon * string list =
        if BattleMon.isFainted m then (m, [])
        else
            let immune = m.Species.Type1 = TypeChart.value "ROCK" || m.Species.Type1 = TypeChart.value "GROUND" || m.Species.Type1 = TypeChart.value "STEEL" || m.Species.Type2 = TypeChart.value "ROCK" || m.Species.Type2 = TypeChart.value "GROUND" || m.Species.Type2 = TypeChart.value "STEEL"
            if immune then (m, [])
            else
                let chip = max 1 (m.MaxHp / 8)
                ({ m with Hp = max 0 (m.Hp - chip) }, [ $"{m.Species.Name} is buffeted by the sandstorm!" ])

    let private betweenTurns (player: BattleMon) (enemy: BattleMon) (rng: Rng) (weatherTimer: int option) (weatherType: string option) (playerSide: SideState) (enemySide: SideState)
        : BattleMon * BattleMon * SideState * SideState * int option * string option * string list * Rng =
        let mutable p = player
        let mutable e = enemy
        let mutable r = rng
        let mutable msgs: string list = []
        let mutable wt = weatherTimer
        let mutable wtType = weatherType
        let mutable ps = playerSide
        let mutable es = enemySide

        // Clear one-turn flags at the start of the turn.
        p <- { p with Volatile = { p.Volatile with Protect = false; Endure = false } }
        e <- { e with Volatile = { e.Volatile with Protect = false; Endure = false } }

        // Slot 1: Future Sight countdown.
        let p', e', futureMsgs, r' = applyFutureSight p e r wtType
        p <- p'; e <- e'; r <- r'; msgs <- msgs @ futureMsgs
        let e', p', futureMsgs, r' = applyFutureSight e p r wtType
        p <- p'; e <- e'; r <- r'; msgs <- msgs @ futureMsgs

        // Slot 2: Weather (sandstorm chip, timer).
        if wt.IsSome && wt.Value > 0 then
            let p', pWeatherMsgs = applyWeather p
            p <- p'; msgs <- msgs @ pWeatherMsgs
            let e', eWeatherMsgs = applyWeather e
            e <- e'; msgs <- msgs @ eWeatherMsgs
            wt <- Some (wt.Value - 1)
            if wt.Value = 0 then
                wt <- None
                wtType <- None

        // Slot 3: Wrap/Bind/Clamp chip.
        let p', pWrapMsgs = applyWrap p
        p <- p'; msgs <- msgs @ pWrapMsgs
        let e', eWrapMsgs = applyWrap e
        e <- e'; msgs <- msgs @ eWrapMsgs

        // Slot 4: Perish Song countdown.
        let pPer = if ps.PerishCounter.IsSome then Some (ps.PerishCounter.Value - 1) else None
        let ePer = if es.PerishCounter.IsSome then Some (es.PerishCounter.Value - 1) else None
        if ps.PerishCounter.IsSome then
            if ps.PerishCounter.Value <= 1 then
                p <- { p with Hp = 0 }
                msgs <- msgs @ [ $"{p.Species.Name} fainted from Perish Song!" ]
            else
                msgs <- msgs @ [ $"{p.Species.Name} is fading from Perish Song!" ]
        if es.PerishCounter.IsSome then
            if es.PerishCounter.Value <= 1 then
                e <- { e with Hp = 0 }
                msgs <- msgs @ [ $"{e.Species.Name} fainted from Perish Song!" ]
            else
                msgs <- msgs @ [ $"{e.Species.Name} is fading from Perish Song!" ]
        ps <- { ps with PerishCounter = if pPer.IsSome && pPer.Value > 0 then pPer else None }
        es <- { es with PerishCounter = if ePer.IsSome && ePer.Value > 0 then ePer else None }

        // Slot 5: Leftovers / items.
        let p', pItemMsgs = applyHeldItemResidual p
        p <- p'; msgs <- msgs @ pItemMsgs
        let e', eItemMsgs = applyHeldItemResidual e
        e <- e'; msgs <- msgs @ eItemMsgs

        // Slot 6: Defrost.
        let p', pDefMsgs, r' = applyDefrost p r
        p <- p'; r <- r'; msgs <- msgs @ pDefMsgs
        let e', eDefMsgs, r' = applyDefrost e r
        e <- e'; r <- r'; msgs <- msgs @ eDefMsgs

        // Slot 7: Poison/Toxic/Burn tick.
        let p', pPsnMsgs = applyResidual p
        p <- p'; msgs <- msgs @ pPsnMsgs
        let e', ePsnMsgs = applyResidual e
        e <- e'; msgs <- msgs @ ePsnMsgs

        // Slot 8: Leech Seed drain.
        let p', e', pSeedMsgs = applyLeechSeed p e
        p <- p'; e <- e'; msgs <- msgs @ pSeedMsgs
        let e', p', eSeedMsgs = applyLeechSeed e p
        p <- p'; e <- e'; msgs <- msgs @ eSeedMsgs

        // Slot 9: Rampage auto-confuse.
        let p', pRampMsgs, r' = applyRampage p r
        p <- p'; r <- r'; msgs <- msgs @ pRampMsgs
        let e', eRampMsgs, r' = applyRampage e r
        e <- e'; r <- r'; msgs <- msgs @ eRampMsgs

        // Slot 10: Nightmare — chips sleeping targets MaxHP/4 per turn.
        if p.Volatile.Nightmare then
            match p.Status with
            | Sleep _ ->
                let dmg = max 1 (p.MaxHp / 4)
                p <- { p with Hp = max 0 (p.Hp - dmg) }
                msgs <- msgs @ [ $"{p.Species.Name} is suffering from Nightmare!" ]
            | _ -> ()
        if e.Volatile.Nightmare then
            match e.Status with
            | Sleep _ ->
                let dmg = max 1 (e.MaxHp / 4)
                e <- { e with Hp = max 0 (e.Hp - dmg) }
                msgs <- msgs @ [ $"{e.Species.Name} is suffering from Nightmare!" ]
            | _ -> ()

        // Slot 11: Curse chip.
        if p.Volatile.Curse then
            let dmg = max 1 (p.MaxHp / 4)
            p <- { p with Hp = max 0 (p.Hp - dmg) }
            msgs <- msgs @ [ $"{p.Species.Name} is hurt by Curse!" ]
        if e.Volatile.Curse then
            let dmg = max 1 (e.MaxHp / 4)
            e <- { e with Hp = max 0 (e.Hp - dmg) }
            msgs <- msgs @ [ $"{e.Species.Name} is hurt by Curse!" ]

        // Slot 12: Safeguard timer.
        let psSafeguard = if ps.SafeguardTimer.IsSome then Some (ps.SafeguardTimer.Value - 1) |> Option.filter (fun n -> n > 0) else None
        let esSafeguard = if es.SafeguardTimer.IsSome then Some (es.SafeguardTimer.Value - 1) |> Option.filter (fun n -> n > 0) else None
        ps <- { ps with SafeguardTimer = psSafeguard }
        es <- { es with SafeguardTimer = esSafeguard }

        // Slot 13: Reflect/Light Screen timer.
        let psReflect = if ps.ReflectTimer.IsSome then Some (ps.ReflectTimer.Value - 1) |> Option.filter (fun n -> n > 0) else None
        let psLightScreen = if ps.LightScreenTimer.IsSome then Some (ps.LightScreenTimer.Value - 1) |> Option.filter (fun n -> n > 0) else None
        let esReflect = if es.ReflectTimer.IsSome then Some (es.ReflectTimer.Value - 1) |> Option.filter (fun n -> n > 0) else None
        let esLightScreen = if es.LightScreenTimer.IsSome then Some (es.LightScreenTimer.Value - 1) |> Option.filter (fun n -> n > 0) else None
        ps <- { ps with ReflectTimer = psReflect; LightScreenTimer = psLightScreen }
        es <- { es with ReflectTimer = esReflect; LightScreenTimer = esLightScreen }

        // Slot 14: Encore timer.
        let pEncore = if p.Volatile.EncoreTimer.IsSome then Some (p.Volatile.EncoreTimer.Value - 1) |> Option.filter (fun n -> n > 0) else None
        let eEncore = if e.Volatile.EncoreTimer.IsSome then Some (e.Volatile.EncoreTimer.Value - 1) |> Option.filter (fun n -> n > 0) else None
        p <- { p with Volatile = { p.Volatile with EncoreTimer = pEncore; EncoreMoveIndex = if pEncore.IsSome then p.Volatile.EncoreMoveIndex else None } }
        e <- { e with Volatile = { e.Volatile with EncoreTimer = eEncore; EncoreMoveIndex = if eEncore.IsSome then e.Volatile.EncoreMoveIndex else None } }

        (p, e, ps, es, wt, wtType, msgs, r)

    // -- Phase: faint check --------------------------------------------------

    /// Check if either side has fainted and produce the appropriate outcome
    /// and messages. Returns (outcome option, messages, enemyFainted, playerFainted).
    let private faintCheck (state: BattleState) : Outcome option * string list * bool * bool =
        let enemyFainted = BattleMon.isFainted state.Enemy
        let playerFainted = BattleMon.isFainted state.Player

        if enemyFainted then
            let survivingEnemy = state.EnemyTeam |> List.filter (fun m -> not (BattleMon.isFainted m))
            if survivingEnemy.IsEmpty then
                (Some Win, [ faintedEnemyText state.Kind state.Enemy; "You won!" ], true, false)
            else
                (None, [ faintedEnemyText state.Kind state.Enemy ], true, false)
        elif playerFainted then
            let survivingPlayer = state.PlayerTeam |> List.filter (fun m -> not (BattleMon.isFainted m))
            if survivingPlayer.IsEmpty then
                (Some Lose, [ $"{state.Player.Species.Name} fainted!"; "You lost!" ], false, true)
            else
                (None, [ $"{state.Player.Species.Name} fainted!" ], false, true)
        else
            (None, [], false, false)

    // -- Enemy AI ------------------------------------------------------------

    /// Pick the enemy's move. When all PP is exhausted, returns None (Struggle).
    let private enemyMoveChoice (enemy: BattleMon) (player: BattleMon) : (MoveData * int) option =
        BattleAI.chooseMove enemy player

    // -- Orchestrator --------------------------------------------------------

    let private chargingMoveOf (m: BattleMon) : MoveData option =
        if m.Volatile.Charging.IsSome then m.Volatile.ChargingMove else None

    let private nextMon (state: BattleState) : BattleState * string list =
        if BattleMon.isFainted state.Enemy then
            let next = state.EnemyTeam |> List.filter (fun m -> not (BattleMon.isFainted m)) |> List.tryHead
            match next with
            | Some mon ->
                let team = mon :: (state.EnemyTeam |> List.filter (fun m -> m <> mon))
                ({ state with Enemy = mon; EnemyTeam = team }, [ sentOutEnemyText state.Kind mon ])
            | None -> (state, [])
        elif BattleMon.isFainted state.Player then
            let next = state.PlayerTeam |> List.filter (fun m -> not (BattleMon.isFainted m)) |> List.tryHead
            match next with
            | Some mon ->
                let team = mon :: (state.PlayerTeam |> List.filter (fun m -> m <> mon))
                ({ state with Player = mon; PlayerTeam = team }, [ $"Go, {mon.Species.Name}!" ])
            | None -> (state, [])
        else
            (state, [])

    let private isChargingEffect (move: MoveData) : bool =
        [ "EFFECT_FLY"; "EFFECT_DIG"; "EFFECT_CHARGE"; "EFFECT_SOLAR_BEAM";
          "EFFECT_SKULL_BASH"; "EFFECT_SKY_ATTACK"; "EFFECT_RAZOR_WIND" ]
        |> List.contains move.Effect

    let private restoreHeldPp (m: BattleMon) =
        match heldParam "HELD_RESTORE_PP" m with
        | None -> m, []
        | Some _ ->
            let target =
                List.zip [ 0 .. m.Pp.Length - 1 ] m.Pp
                |> List.tryFind (fun (i, pp) -> pp = 0 && i < m.Moves.Length && m.Moves.[i].Pp > 0)

            match target with
            | None -> m, []
            | Some(i, pp) ->
                let maxPp = m.Moves.[i].Pp
                let restored = min maxPp (pp + 5)
                let pp' = m.Pp |> List.mapi (fun j current -> if i = j then restored else current)
                { m with Pp = pp'; HeldItem = None }, [ $"{m.Species.Name}'s held item restored PP!" ]

    let private clearSwitchVolatile (m: BattleMon) =
        { m with Volatile = VolatileStatus.empty }

    let private batonPassTo (source: BattleMon) (target: BattleMon) =
        let nightmare =
            match target.Status with
            | Sleep _ -> source.Volatile.Nightmare
            | _ -> false
        let volatile =
            { source.Volatile with
                Nightmare = nightmare
                DisableTimer = None
                DisabledMoveIndex = None
                Attracted = false
                Transformed = false
                EncoreTimer = None
                EncoreMoveIndex = None
                LastMove = None
                Trapped = None }
        { target with
            AtkStage = source.AtkStage
            DefStage = source.DefStage
            SpdStage = source.SpdStage
            SpAtkStage = source.SpAtkStage
            SpDefStage = source.SpDefStage
            AccStage = source.AccStage
            EvaStage = source.EvaStage
            Volatile = volatile }

    let private resetBatonPassOpponentStatus (m: BattleMon) =
        { m with Volatile = { m.Volatile with Attracted = false; Trapped = None } }

    let private switchTeamTo (teamIndex: int) (active: BattleMon) (team: BattleMon list) (incoming: BattleMon -> BattleMon -> BattleMon) =
        if teamIndex <= 0 || teamIndex >= team.Length then
            None
        else
            let target = team.[teamIndex]
            if BattleMon.isFainted target then
                None
            else
                let switchedIn = incoming active target
                let switchedOut = clearSwitchVolatile active
                let team' =
                    team
                    |> List.mapi (fun i mon ->
                        if i = 0 then switchedIn
                        elif i = teamIndex then switchedOut
                        else mon)
                Some(switchedIn, team')

    let private firstHealthyBench (team: BattleMon list) =
        team
        |> List.mapi (fun i mon -> i, mon)
        |> List.tryFind (fun (i, mon) -> i > 0 && not (BattleMon.isFainted mon))
        |> Option.map fst

    let private randomHealthyBench (team: BattleMon list) (rng: Rng) =
        if firstHealthyBench team |> Option.isNone then
            None, rng
        else
            let rec loop rng =
                let roll, rng' = Rng.next rng
                let index = roll &&& 7
                if index > 0 && index < team.Length && not (BattleMon.isFainted team.[index]) then
                    Some index, rng'
                else
                    loop rng'
            loop rng

    /// The player selects a move (by index into their move list). This resolves a
    /// whole turn: both sides act in speed order, faints are checked between
    /// actions, end-of-turn residuals run, and the outcome is set if the battle ends.
    let chooseMove (index: int) (s: BattleState) : BattleState =
        if isOver s then
            s
        else

        // Determine player's move: Struggle if all PP exhausted, else the selected move.
        let struggle = Moves.byName "STRUGGLE"
        let playerStruggle = BattleMon.mustStruggle s.Player
        let playerMv, playerMvIndex =
            if playerStruggle then struggle, -1
            else s.Player.Moves.[index], index

        let mutable preEnemy = s.Enemy
        let mutable preEnemyTeam = s.EnemyTeam
        let enemySwitched, enemySwitchMsgs =
            match BattleAI.chooseSwitch preEnemy s.Player preEnemyTeam with
            | Some switchIndex ->
                match switchTeamTo switchIndex preEnemy preEnemyTeam (fun _ target -> clearSwitchVolatile target) with
                | Some(incoming, team') ->
                    let msgs = [ $"Enemy withdrew {preEnemy.Species.Name}!"; $"Enemy sent out {incoming.Species.Name}!" ]
                    preEnemy <- incoming
                    preEnemyTeam <- team'
                    true, msgs
                | None -> false, []
            | None -> false, []

        // Enemy move selection.
        let enemyChoice = if enemySwitched then None else enemyMoveChoice preEnemy s.Player
        let enemyStruggle = (not enemySwitched) && enemyChoice.IsNone
        let enemyMv, enemyMvIndex =
            if enemySwitched then Moves.byName "SPLASH", -1
            else
                match enemyChoice with
                | Some (m, i) -> m, i
                | None -> struggle, -1

        // Struggle messages (before moves execute).
        let struggleMsgs =
            [ if playerStruggle then $"{s.Player.Species.Name} has no moves left!"
              if enemyStruggle then $"{s.Enemy.Species.Name} has no moves left!" ]

        let usesProtectCounter (move: MoveData) =
            move.Effect = "EFFECT_PROTECT" || move.Effect = "EFFECT_ENDURE"

        let resetProtectCountIfNeeded (move: MoveData) (mon: BattleMon) =
            if usesProtectCounter move then mon
            else { mon with Volatile = { mon.Volatile with ProtectCount = 0 } }

        let mutable player = resetProtectCountIfNeeded playerMv s.Player
        let mutable enemy = resetProtectCountIfNeeded enemyMv preEnemy
        let mutable playerTeam = s.PlayerTeam |> List.mapi (fun i m -> if i = 0 then player else m)
        let mutable enemyTeam = preEnemyTeam |> List.mapi (fun i m -> if i = 0 then enemy else m)
        let mutable rng = s.Rng
        let mutable weatherTimer = s.WeatherTimer
        let mutable weatherType = s.WeatherType
        let mutable playerSide = s.PlayerSide
        let mutable enemySide = s.EnemySide
        let mutable msgs: string list = enemySwitchMsgs @ struggleMsgs
        let mutable outcome: Outcome option = None
        let mutable skipPlayerAction = false
        let mutable skipEnemyAction = false
        let mutable playerDamageTaken = 0
        let mutable enemyDamageTaken = 0
        let mutable playerLastCounterMove = player.Volatile.LastCounterMove
        let mutable enemyLastCounterMove = enemy.Volatile.LastCounterMove
        let mutable playerLastMove = player.Volatile.LastMove
        let mutable enemyLastMove = enemy.Volatile.LastMove
        let mutable forcedPlayerMoveIndex: int option = None
        let mutable forcedEnemyMoveIndex: int option = None

        let priorityOf (move: MoveData) =
            if move.Effect = "EFFECT_PRIORITY_HIT" then 1 else 0

        let quickClawCheck (mon: BattleMon) (rng: Rng) =
            match heldParam "HELD_QUICK_CLAW" mon with
            | Some chance ->
                let roll, rng' = Rng.next rng
                roll < chance, rng'
            | None -> false, rng

        let playerFirst =
            if enemySwitched then
                true
            else
                let playerPriority = priorityOf playerMv
                let enemyPriority = priorityOf enemyMv

                if playerPriority <> enemyPriority then
                    playerPriority > enemyPriority
                else
                    match heldEffect "HELD_QUICK_CLAW" player, heldEffect "HELD_QUICK_CLAW" enemy with
                    | true, false ->
                        let activated, rng' = quickClawCheck player rng
                        rng <- rng'
                        if activated then msgs <- msgs @ [ $"{player.Species.Name}'s QUICK CLAW let it move first!" ]
                        if activated then true else BattleMon.effectiveSpeed player >= BattleMon.effectiveSpeed enemy
                    | false, true ->
                        let activated, rng' = quickClawCheck enemy rng
                        rng <- rng'
                        if activated then msgs <- msgs @ [ $"{enemy.Species.Name}'s QUICK CLAW let it move first!" ]
                        if activated then false else BattleMon.effectiveSpeed player >= BattleMon.effectiveSpeed enemy
                    | _ ->
                        BattleMon.effectiveSpeed player >= BattleMon.effectiveSpeed enemy

        // Run one side's action (pre-move gate -> execute -> PP deduct -> mid-turn faint check).
        let act (playerIsUser: bool) : bool =
            if outcome.IsSome then
                false
            elif playerIsUser && skipPlayerAction then
                skipPlayerAction <- false
                false
            elif (not playerIsUser) && skipEnemyAction then
                skipEnemyAction <- false
                false
            else
                let user, foe, selectedMove, selectedMoveIndex, isStruggle =
                    if playerIsUser then player, enemy, playerMv, playerMvIndex, playerStruggle
                    else enemy, player, enemyMv, enemyMvIndex, enemyStruggle

                let forcedMoveIndex =
                    match if playerIsUser then forcedPlayerMoveIndex else forcedEnemyMoveIndex with
                    | Some index -> Some index
                    | None when user.Volatile.Rampage.IsSome ->
                        user.Volatile.LastMove
                        |> Option.bind (fun lastMove -> user.Moves |> List.tryFindIndex (fun candidate -> candidate.Name = lastMove.Name))
                    | None when user.Volatile.EncoreTimer.IsSome -> user.Volatile.EncoreMoveIndex
                    | None -> None

                let move, mvIndex =
                    match forcedMoveIndex with
                    | Some index when index >= 0 && index < user.Moves.Length -> user.Moves.[index], index
                    | _ -> selectedMove, selectedMoveIndex

                let storedCharge = chargingMoveOf user
                let chargeTurn = storedCharge.IsSome
                let moveToUse = if chargeTurn then storedCharge.Value else move
                let mvIndexToUse =
                    if chargeTurn then user.Moves |> List.findIndex (fun m -> m.Name = storedCharge.Value.Name) else mvIndex
                let targetIsSwitching = playerIsUser && enemySwitched

                // Did this user move first this turn?
                let userMovedFirst =
                    if playerIsUser then playerFirst else not playerFirst

                // Phase: pre-move status gates
                let canAct, selfHit, user, gateMsgs, rng' = preMoveStatusCheck user foe moveToUse mvIndexToUse userMovedFirst rng
                rng <- rng'
                msgs <- msgs @ gateMsgs

                if selfHit then
                    // Confusion self-hit: 40-power typeless physical, user hits itself.
                    let dmg = confusionSelfHitDamage user
                    let user = { user with Hp = max 0 (user.Hp - dmg) }
                    if playerIsUser then
                        player <- user
                        playerTeam <- playerTeam |> List.mapi (fun i m -> if i = 0 then user else m)
                    else
                        enemy <- user
                        enemyTeam <- enemyTeam |> List.mapi (fun i m -> if i = 0 then user else m)
                    // Check if self-hit caused a faint.
                    let faintOutcome, faintMsgs, enemyFainted, playerFainted =
                        faintCheck { Player = player; Enemy = enemy; PlayerTeam = playerTeam; EnemyTeam = enemyTeam; Kind = s.Kind; Messages = s.Messages; Outcome = s.Outcome; Rng = rng; WeatherTimer = weatherTimer; WeatherType = weatherType; PlayerSide = playerSide; EnemySide = enemySide }
                    msgs <- msgs @ faintMsgs
                    match faintOutcome with
                    | Some o -> outcome <- Some o; false
                    | None when enemyFainted || playerFainted ->
                        let switched, switchMsgs = nextMon { Player = player; Enemy = enemy; PlayerTeam = playerTeam; EnemyTeam = enemyTeam; Kind = s.Kind; Messages = s.Messages; Outcome = s.Outcome; Rng = rng; WeatherTimer = weatherTimer; WeatherType = weatherType; PlayerSide = playerSide; EnemySide = enemySide }
                        player <- switched.Player
                        enemy <- switched.Enemy
                        playerTeam <- switched.PlayerTeam
                        enemyTeam <- switched.EnemyTeam
                        msgs <- msgs @ switchMsgs
                        true
                    | None -> true
                elif not canAct then
                    if playerIsUser then player <- user else enemy <- user
                    true
                else
                    match user.Volatile.BideTurns with
                    | Some turns ->
                        let user, foe, bideMsgs =
                            if turns > 1 then
                                let vol = { user.Volatile with BideTurns = Some (turns - 1) }
                                { user with Volatile = vol }, foe, [ $"{user.Species.Name} is storing energy!" ]
                            else
                                let dmg = min 65535 (user.Volatile.BideDamage * 2)
                                let foe = if dmg > 0 then { foe with Hp = max 0 (foe.Hp - dmg) } else foe
                                let vol = { user.Volatile with BideTurns = None; BideDamage = 0 }
                                let messages =
                                    if dmg > 0 then [ $"{user.Species.Name} unleashed energy!" ]
                                    else [ $"{user.Species.Name} unleashed energy!"; "But it failed!" ]
                                { user with Volatile = vol }, foe, messages

                        msgs <- msgs @ bideMsgs
                        if playerIsUser then
                            player <- user
                            enemy <- foe
                            playerTeam <- playerTeam |> List.mapi (fun i m -> if i = 0 then user else m)
                            enemyTeam <- enemyTeam |> List.mapi (fun i m -> if i = 0 then foe else m)
                        else
                            enemy <- user
                            player <- foe
                            playerTeam <- playerTeam |> List.mapi (fun i m -> if i = 0 then foe else m)
                            enemyTeam <- enemyTeam |> List.mapi (fun i m -> if i = 0 then user else m)

                        let faintOutcome, faintMsgs, enemyFainted, playerFainted =
                            faintCheck { Player = player; Enemy = enemy; PlayerTeam = playerTeam; EnemyTeam = enemyTeam; Kind = s.Kind; Messages = s.Messages; Outcome = s.Outcome; Rng = rng; WeatherTimer = weatherTimer; WeatherType = weatherType; PlayerSide = playerSide; EnemySide = enemySide }
                        msgs <- msgs @ faintMsgs
                        match faintOutcome with
                        | Some o -> outcome <- Some o; false
                        | None when enemyFainted || playerFainted ->
                            let switched, switchMsgs = nextMon { Player = player; Enemy = enemy; PlayerTeam = playerTeam; EnemyTeam = enemyTeam; Kind = s.Kind; Messages = s.Messages; Outcome = s.Outcome; Rng = rng; WeatherTimer = weatherTimer; WeatherType = weatherType; PlayerSide = playerSide; EnemySide = enemySide }
                            player <- switched.Player
                            enemy <- switched.Enemy
                            playerTeam <- switched.PlayerTeam
                            enemyTeam <- switched.EnemyTeam
                            msgs <- msgs @ switchMsgs
                            true
                        | None -> true

                    | None ->
                        // First turn of a charging move: set the charge flag and skip the action.
                        if not chargeTurn && isChargingEffect move && user.Volatile.Charging.IsNone then
                            let user' = { user with Volatile = { user.Volatile with Charging = Some 1; ChargingMove = Some move } }
                            if playerIsUser then player <- user' else enemy <- user'
                            msgs <- msgs @ [ $"{user'.Species.Name} is charging up!" ]
                            true
                        else
                            // ProtectChance fails if the opponent already moved.
                            let opponentWentFirst = not userMovedFirst
                            let user, foe, moveMsgs, rng', playerSide', enemySide', weatherTimer', weatherType', lastDamage, hit =
                                if moveToUse.Effect = "EFFECT_SKETCH" then
                                    let lastOppMove =
                                        if playerIsUser then enemyLastCounterMove else playerLastCounterMove
                                    let clearLast user =
                                        { user with Volatile = { user.Volatile with LastMove = None; LastCounterMove = None } }
                                    let sketchResult =
                                        match lastOppMove with
                                        | Some copied when foe.Volatile.Substitute.IsNone && not foe.Volatile.Transformed && copied.Name <> "STRUGGLE" && not (user.Moves |> List.exists (fun known -> known.Name = copied.Name)) ->
                                            user.Moves
                                            |> List.tryFindIndex (fun move -> move.Name = moveToUse.Name)
                                            |> Option.map (fun index -> index, copied)
                                        | _ -> None

                                    match sketchResult with
                                    | Some(index, copied) ->
                                        let cleared = clearLast user
                                        let user =
                                            { cleared with
                                                Moves = user.Moves |> List.mapi (fun i move -> if i = index then copied else move)
                                                Pp = user.Pp |> List.mapi (fun i pp -> if i = index then copied.Pp else pp) }
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; $"{user.Species.Name} sketched {copied.Name}!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, true
                                    | None ->
                                        clearLast user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "It didn't affect the target!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                elif moveToUse.Effect = "EFFECT_FORCE_SWITCH" then
                                    let hit, rng = checkHit user foe moveToUse rng weatherType
                                    if not hit then
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; $"{user.Species.Name}'s attack missed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                    else
                                        let targetTeam = if playerIsUser then enemyTeam else playerTeam
                                        if firstHealthyBench targetTeam |> Option.isSome then
                                            if opponentWentFirst then
                                                user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; $"{foe.Species.Name} was blown away!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, true
                                            else
                                                user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "But it failed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                        else
                                            let rec drawBelow limit rng =
                                                let roll, rng' = Rng.next rng
                                                if roll < limit then roll, rng' else drawBelow limit rng'
                                            let forceSucceeds, rng =
                                                if user.Level >= foe.Level then true, rng
                                                else
                                                    let roll, rng = drawBelow (user.Level + foe.Level + 1) rng
                                                    roll >= foe.Level / 4, rng
                                            if forceSucceeds then
                                                user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; $"{foe.Species.Name} fled in fear!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, true
                                            else
                                                user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "But it failed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                elif moveToUse.Effect = "EFFECT_BATON_PASS" then
                                    let team = if playerIsUser then playerTeam else enemyTeam
                                    if firstHealthyBench team |> Option.isSome then
                                        executeMove user foe moveToUse isStruggle rng playerIsUser targetIsSwitching s
                                    else
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "But it failed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                elif moveToUse.Effect = "EFFECT_TELEPORT" then
                                    let rec drawBelow limit rng =
                                        let roll, rng' = Rng.next rng
                                        if roll < limit then roll, rng' else drawBelow limit rng'
                                    let teleportSucceeds, rng =
                                        if user.Volatile.CantEscape then false, rng
                                        elif not playerIsUser then true, rng
                                        elif user.Level >= foe.Level then true, rng
                                        else
                                            let roll, rng = drawBelow (user.Level + foe.Level + 1) rng
                                            roll >= foe.Level / 4, rng

                                    if teleportSucceeds then
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; $"{user.Species.Name} fled from battle!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, true
                                    else
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "But it failed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                elif moveToUse.Effect = "EFFECT_SPITE" then
                                    let hit, rng = checkHit user foe moveToUse rng weatherType
                                    if not hit then
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; $"{user.Species.Name}'s attack missed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                    else
                                        let lastOppMove =
                                            if playerIsUser then enemyLastCounterMove else playerLastCounterMove
                                        let spiteResult =
                                            match lastOppMove with
                                            | Some lastMove when lastMove.Name <> "STRUGGLE" ->
                                                foe.Moves
                                                |> List.tryFindIndex (fun move -> move.Name = lastMove.Name)
                                                |> Option.bind (fun index ->
                                                    if index < foe.Pp.Length && foe.Pp.[index] > 0 then Some(index, lastMove) else None)
                                            | _ -> None

                                        match spiteResult with
                                        | Some(index, spiteMove) ->
                                            let roll, rng = Rng.next rng
                                            let amount = min foe.Pp.[index] ((roll &&& 3) + 2)
                                            let foe =
                                                { foe with Pp = foe.Pp |> List.mapi (fun i pp -> if i = index then pp - amount else pp) }
                                            user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; $"{spiteMove.Name}'s PP was reduced by {amount}!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, true
                                        | None ->
                                            user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "It didn't affect the target!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                elif moveToUse.Effect = "EFFECT_ENCORE" then
                                    let hit, rng = checkHit user foe moveToUse rng weatherType
                                    if not hit then
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; $"{user.Species.Name}'s attack missed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                    else
                                        let lastOppMove =
                                            if playerIsUser then enemyLastMove else playerLastMove
                                        let encoreResult =
                                            match lastOppMove with
                                            | Some lastMove when foe.Volatile.EncoreTimer.IsNone && lastMove.Name <> "STRUGGLE" && lastMove.Effect <> "EFFECT_ENCORE" && lastMove.Effect <> "EFFECT_MIRROR_MOVE" ->
                                                foe.Moves
                                                |> List.tryFindIndex (fun candidate -> candidate.Name = lastMove.Name)
                                                |> Option.bind (fun idx ->
                                                    if idx < foe.Pp.Length && foe.Pp.[idx] > 0 then Some(idx, lastMove) else None)
                                            | _ -> None

                                        match encoreResult with
                                        | Some(index, encoredMove) ->
                                            let roll, rng = Rng.next rng
                                            let duration = (roll &&& 3) + 3
                                            let foe = { foe with Volatile = { foe.Volatile with EncoreTimer = Some duration; EncoreMoveIndex = Some index } }
                                            if not opponentWentFirst then
                                                if playerIsUser then forcedEnemyMoveIndex <- Some index else forcedPlayerMoveIndex <- Some index
                                            user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; $"{encoredMove.Name} got an encore!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, true
                                        | None ->
                                            user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "It didn't affect the target!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                elif moveToUse.Effect = "EFFECT_DISABLE" then
                                    let hit, rng = checkHit user foe moveToUse rng weatherType
                                    if not hit then
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; $"{user.Species.Name}'s attack missed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                    else
                                        let lastOppMove =
                                            if playerIsUser then enemyLastCounterMove else playerLastCounterMove
                                        let disableResult =
                                            match lastOppMove with
                                            | Some lastMove when foe.Volatile.DisableTimer.IsNone && lastMove.Name <> "STRUGGLE" ->
                                                foe.Moves
                                                |> List.tryFindIndex (fun candidate -> candidate.Name = lastMove.Name)
                                                |> Option.bind (fun idx ->
                                                    if idx < foe.Pp.Length && foe.Pp.[idx] > 0 then Some(idx, lastMove) else None)
                                            | _ -> None

                                        match disableResult with
                                        | Some(index, disabledMove) ->
                                            let rec nonzero rng =
                                                let roll, rng' = Rng.next rng
                                                let count = roll &&& 7
                                                if count = 0 then nonzero rng' else count, rng'
                                            let count, rng = nonzero rng
                                            let duration = count + 1
                                            let foe = { foe with Volatile = { foe.Volatile with DisableTimer = Some duration; DisabledMoveIndex = Some index } }
                                            user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; $"{disabledMove.Name} was disabled!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, true
                                        | None ->
                                            user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "But it failed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                elif moveToUse.Effect = "EFFECT_COUNTER" then
                                    let lastOppMove =
                                        if playerIsUser then enemyLastCounterMove else playerLastCounterMove
                                    let damageTaken =
                                        if playerIsUser then playerDamageTaken else enemyDamageTaken
                                    let counterWorks =
                                        match lastOppMove with
                                        | Some lastMove ->
                                            opponentWentFirst
                                            && lastMove.Effect <> "EFFECT_COUNTER"
                                            && lastMove.Power > 0
                                            && TypeChart.isPhysical lastMove.Type
                                            && damageTaken > 0
                                            && Damage.effectivenessTimesTen moveToUse foe <> 0
                                        | None -> false

                                    if counterWorks then
                                        let dmg = min 65535 (damageTaken * 2)
                                        let foe = { foe with Hp = max 0 (foe.Hp - dmg) }
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "Countered the attack!" ], rng, playerSide, enemySide, weatherTimer, weatherType, dmg, true
                                    else
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "But it failed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                elif moveToUse.Effect = "EFFECT_MIRROR_COAT" then
                                    let lastOppMove =
                                        if playerIsUser then enemyLastCounterMove else playerLastCounterMove
                                    let damageTaken =
                                        if playerIsUser then playerDamageTaken else enemyDamageTaken
                                    let mirrorCoatWorks =
                                        match lastOppMove with
                                        | Some lastMove ->
                                            opponentWentFirst
                                            && lastMove.Effect <> "EFFECT_MIRROR_COAT"
                                            && lastMove.Power > 0
                                            && not (TypeChart.isPhysical lastMove.Type)
                                            && damageTaken > 0
                                            && Damage.effectivenessTimesTen moveToUse foe <> 0
                                        | None -> false

                                    if mirrorCoatWorks then
                                        let dmg = min 65535 (damageTaken * 2)
                                        let foe = { foe with Hp = max 0 (foe.Hp - dmg) }
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "Mirror Coated the attack!" ], rng, playerSide, enemySide, weatherTimer, weatherType, dmg, true
                                    else
                                        user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "But it failed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                elif usesProtectCounter moveToUse && opponentWentFirst then
                                    let user = { user with Volatile = { user.Volatile with ProtectCount = 0 } }
                                    user, foe, [ $"{user.Species.Name} used {moveToUse.Name}!"; "But it failed!" ], rng, playerSide, enemySide, weatherTimer, weatherType, 0, false
                                else
                                    executeMove user foe moveToUse isStruggle rng playerIsUser targetIsSwitching s
                            rng <- rng'
                            playerSide <- playerSide'
                            enemySide <- enemySide'
                            weatherTimer <- weatherTimer'
                            weatherType <- weatherType'
                            msgs <- msgs @ moveMsgs

                            let foe =
                                if lastDamage > 0 && foe.Volatile.BideTurns.IsSome then
                                    { foe with Volatile = { foe.Volatile with BideDamage = foe.Volatile.BideDamage + lastDamage } }
                                else
                                    foe

                            if hit then
                                match moveToUse.Effect with
                                | "EFFECT_SKETCH"
                                | "EFFECT_MIMIC"
                                | "EFFECT_TRANSFORM" -> ()
                                | "EFFECT_SLEEP_TALK"
                                | "EFFECT_MIRROR_MOVE"
                                | "EFFECT_METRONOME" ->
                                    match user.Volatile.LastCounterMove with
                                    | Some called ->
                                        if playerIsUser then
                                            playerLastCounterMove <- Some called
                                            playerLastMove <- Some called
                                            enemyDamageTaken <- lastDamage
                                        else
                                            enemyLastCounterMove <- Some called
                                            enemyLastMove <- Some called
                                            playerDamageTaken <- lastDamage
                                    | None -> ()
                                | _ ->
                                    if playerIsUser then
                                        playerLastCounterMove <- Some moveToUse
                                        playerLastMove <- Some moveToUse
                                        enemyDamageTaken <- lastDamage
                                    else
                                        enemyLastCounterMove <- Some moveToUse
                                        enemyLastMove <- Some moveToUse
                                        playerDamageTaken <- lastDamage

                            let user =
                                if hit && moveToUse.Effect <> "EFFECT_SKETCH" && moveToUse.Effect <> "EFFECT_MIMIC" && moveToUse.Effect <> "EFFECT_SLEEP_TALK" && moveToUse.Effect <> "EFFECT_MIRROR_MOVE" && moveToUse.Effect <> "EFFECT_METRONOME" && moveToUse.Effect <> "EFFECT_TRANSFORM" then
                                    { user with Volatile = { user.Volatile with LastCounterMove = Some moveToUse; LastMove = Some moveToUse } }
                                else
                                    user

                            // Clear the charge window after the second-turn execution.
                            let user =
                                if chargeTurn then
                                    { user with Volatile = { user.Volatile with Charging = None; ChargingMove = None } }
                                else user

                            // Phase: deduct PP (Struggle does not consume PP --
                            // effect_commands.asm l.974: cp STRUGGLE; ret z)
                            let user =
                                let mimicSucceeded =
                                    hit
                                    && moveToUse.Effect = "EFFECT_MIMIC"
                                    && mvIndexToUse < user.Moves.Length
                                    && user.Moves.[mvIndexToUse].Name <> moveToUse.Name

                                if isStruggle || (moveToUse.Effect = "EFFECT_SKETCH" && hit) || mimicSucceeded || (moveToUse.Effect = "EFFECT_TRANSFORM" && user.Volatile.Transformed) then user
                                else BattleMon.deductPp mvIndexToUse user
                            let user, ppMsgs = restoreHeldPp user
                            msgs <- msgs @ ppMsgs

                            let applyDefaultAssignment () =
                                if playerIsUser then
                                    player <- user
                                    enemy <- foe
                                    playerTeam <- playerTeam |> List.mapi (fun i m -> if i = 0 then user else m)
                                    enemyTeam <- enemyTeam |> List.mapi (fun i m -> if i = 0 then foe else m)
                                else
                                    enemy <- user
                                    player <- foe
                                    playerTeam <- playerTeam |> List.mapi (fun i m -> if i = 0 then foe else m)
                                    enemyTeam <- enemyTeam |> List.mapi (fun i m -> if i = 0 then user else m)

                            match moveToUse.Effect, hit with
                            | "EFFECT_TELEPORT", true ->
                                applyDefaultAssignment ()
                                outcome <- Some Ran
                            | "EFFECT_BATON_PASS", true ->
                                if playerIsUser then
                                    let updatedTeam = playerTeam |> List.mapi (fun i m -> if i = 0 then user else m)
                                    match firstHealthyBench updatedTeam |> Option.bind (fun idx -> switchTeamTo idx user updatedTeam batonPassTo) with
                                    | Some(incoming, team') ->
                                        player <- incoming
                                        playerTeam <- team'
                                        enemy <- resetBatonPassOpponentStatus foe
                                        enemyTeam <- enemyTeam |> List.mapi (fun i m -> if i = 0 then enemy else m)
                                        msgs <- msgs @ [ $"Go, {incoming.Species.Name}!" ]
                                    | None ->
                                        msgs <- msgs @ [ "But it failed!" ]
                                        applyDefaultAssignment ()
                                else
                                    let updatedTeam = enemyTeam |> List.mapi (fun i m -> if i = 0 then user else m)
                                    match firstHealthyBench updatedTeam |> Option.bind (fun idx -> switchTeamTo idx user updatedTeam batonPassTo) with
                                    | Some(incoming, team') ->
                                        enemy <- incoming
                                        enemyTeam <- team'
                                        player <- resetBatonPassOpponentStatus foe
                                        playerTeam <- playerTeam |> List.mapi (fun i m -> if i = 0 then player else m)
                                        msgs <- msgs @ [ $"{incoming.Species.Name} was sent out!" ]
                                    | None ->
                                        msgs <- msgs @ [ "But it failed!" ]
                                        applyDefaultAssignment ()
                            | "EFFECT_FORCE_SWITCH", true ->
                                if playerIsUser then
                                    let updatedEnemyTeam = enemyTeam |> List.mapi (fun i m -> if i = 0 then foe else m)
                                    let switchIndex, rng' = randomHealthyBench updatedEnemyTeam rng
                                    rng <- rng'
                                    match switchIndex |> Option.bind (fun idx -> switchTeamTo idx foe updatedEnemyTeam (fun _ target -> clearSwitchVolatile target)) with
                                    | Some(incoming, team') ->
                                        player <- user
                                        playerTeam <- playerTeam |> List.mapi (fun i m -> if i = 0 then user else m)
                                        enemy <- incoming
                                        enemyTeam <- team'
                                        skipEnemyAction <- true
                                        msgs <- msgs @ [ $"{incoming.Species.Name} was dragged out!" ]
                                    | None ->
                                        applyDefaultAssignment ()
                                        outcome <- Some Ran
                                else
                                    let updatedPlayerTeam = playerTeam |> List.mapi (fun i m -> if i = 0 then foe else m)
                                    let switchIndex, rng' = randomHealthyBench updatedPlayerTeam rng
                                    rng <- rng'
                                    match switchIndex |> Option.bind (fun idx -> switchTeamTo idx foe updatedPlayerTeam (fun _ target -> clearSwitchVolatile target)) with
                                    | Some(incoming, team') ->
                                        enemy <- user
                                        enemyTeam <- enemyTeam |> List.mapi (fun i m -> if i = 0 then user else m)
                                        player <- incoming
                                        playerTeam <- team'
                                        skipPlayerAction <- true
                                        msgs <- msgs @ [ $"Go, {incoming.Species.Name}!" ]
                                    | None ->
                                        applyDefaultAssignment ()
                                        msgs <- msgs @ [ "But it failed!" ]
                            | _ ->
                                applyDefaultAssignment ()

                            // Phase: mid-turn faint check
                            let faintOutcome, faintMsgs, enemyFainted, playerFainted =
                                faintCheck { Player = player; Enemy = enemy; PlayerTeam = playerTeam; EnemyTeam = enemyTeam; Kind = s.Kind; Messages = s.Messages; Outcome = s.Outcome; Rng = rng; WeatherTimer = weatherTimer; WeatherType = weatherType; PlayerSide = playerSide; EnemySide = enemySide }
                            msgs <- msgs @ faintMsgs
                            match faintOutcome with
                            | Some o ->
                                outcome <- Some o
                                false
                            | None when enemyFainted || playerFainted ->
                                let switched, switchMsgs = nextMon { Player = player; Enemy = enemy; PlayerTeam = playerTeam; EnemyTeam = enemyTeam; Kind = s.Kind; Messages = s.Messages; Outcome = s.Outcome; Rng = rng; WeatherTimer = weatherTimer; WeatherType = weatherType; PlayerSide = playerSide; EnemySide = enemySide }
                                player <- switched.Player
                                enemy <- switched.Enemy
                                playerTeam <- switched.PlayerTeam
                                enemyTeam <- switched.EnemyTeam
                                msgs <- msgs @ switchMsgs
                                true
                            | None -> true

        let order =
            if enemySwitched then [ true ]
            elif playerFirst then [ true; false ]
            else [ false; true ]
        order |> List.iter (fun who -> act who |> ignore)

        // Phase: end-of-turn residuals (only if nobody fainted mid-turn)
        if outcome.IsNone then
            let p, e, playerSide', enemySide', weatherTimer', weatherType', residualMsgs, rng' = betweenTurns player enemy rng weatherTimer weatherType playerSide enemySide
            player <- p
            enemy <- e
            playerTeam <- playerTeam |> List.mapi (fun i m -> if i = 0 then p else m)
            enemyTeam <- enemyTeam |> List.mapi (fun i m -> if i = 0 then e else m)
            playerSide <- playerSide'
            enemySide <- enemySide'
            weatherTimer <- weatherTimer'
            weatherType <- weatherType'
            rng <- rng'
            msgs <- msgs @ residualMsgs

            // Phase: end-of-turn faint check
            let faintOutcome, faintMsgs, _, _ =
                faintCheck { Player = player; Enemy = enemy; PlayerTeam = playerTeam; EnemyTeam = enemyTeam; Kind = s.Kind; Messages = s.Messages; Outcome = s.Outcome; Rng = rng; WeatherTimer = weatherTimer; WeatherType = weatherType; PlayerSide = playerSide; EnemySide = enemySide }
            msgs <- msgs @ faintMsgs
            match faintOutcome with
            | Some o -> outcome <- Some o
            | None ->
                let switched, switchMsgs =
                    nextMon { Player = player; Enemy = enemy; PlayerTeam = playerTeam; EnemyTeam = enemyTeam; Kind = s.Kind; Messages = s.Messages; Outcome = s.Outcome; Rng = rng; WeatherTimer = weatherTimer; WeatherType = weatherType; PlayerSide = playerSide; EnemySide = enemySide }
                player <- switched.Player
                enemy <- switched.Enemy
                playerTeam <- switched.PlayerTeam
                enemyTeam <- switched.EnemyTeam
                msgs <- msgs @ switchMsgs

        { s with
            Player = player
            Enemy = enemy
            PlayerTeam = playerTeam
            EnemyTeam = enemyTeam
            Messages = msgs
            Outcome = outcome
            Rng = rng
            WeatherTimer = weatherTimer
            WeatherType = weatherType
            PlayerSide = playerSide
            EnemySide = enemySide }

    /// The player flees the battle. Blocked if the player is trapped (Wrap/Bind)
    /// or locked in by Mean Look / Spider Web.
    let run (s: BattleState) : BattleState =
        if isOver s then
            s
        elif isTrainerBattle s then
            { s with Messages = [ "No! There's no running from a trainer battle!" ] }
        elif heldEffect "HELD_ESCAPE" s.Player then
            { s with
                Messages = [ "Got away safely!" ]
                Outcome = Some Ran }
        elif s.Player.Volatile.Trapped.IsSome then
            { s with Messages = [ $"{s.Player.Species.Name} is trapped and can't escape!" ] }
        elif s.Player.Volatile.CantEscape then
            { s with Messages = [ $"{s.Player.Species.Name} can't escape!" ] }
        else
            { s with
                Messages = [ "Got away safely!" ]
                Outcome = Some Ran }

    /// Switch the active player mon to a different team member by index.
    /// The switched-in mon inherits no volatile status (fresh entry).
    let switchMon (teamIndex: int) (s: BattleState) : BattleState =
        if isOver s then s
        elif teamIndex < 0 || teamIndex >= s.PlayerTeam.Length then s
        else
            let target = s.PlayerTeam.[teamIndex]
            if BattleMon.isFainted target then s
            elif target = s.Player then s
            else
                // Swap active mon with the target in the team list
                let team =
                    s.PlayerTeam |> List.mapi (fun i m ->
                        if i = 0 then target
                        elif m = target then s.Player
                        else m)
                { s with
                    Player = target
                    PlayerTeam = team
                    Messages = [ $"Come back, {s.Player.Species.Name}!"; $"Go, {target.Species.Name}!" ] }
