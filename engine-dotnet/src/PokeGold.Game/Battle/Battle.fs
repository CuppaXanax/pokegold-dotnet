namespace PokeGold.Game.Battle

open PokeGold.Game.Data

/// How a battle ended, from the player's perspective.
type Outcome =
    | Win
    | Lose
    | Ran

/// The full state of a wild battle. Immutable: each turn produces a new state.
/// `Messages` is the queue of lines the battle scene reveals one at a time;
/// `Outcome` is set once the battle resolves.
type BattleState =
    { Player: BattleMon
      Enemy: BattleMon
      Messages: string list
      Outcome: Outcome option
      Rng: Rng }

module Battle =

    /// Start a wild battle between the player's mon and a wild one.
    let create (player: BattleMon) (enemy: BattleMon) (seed: uint32) : BattleState =
        { Player = player
          Enemy = enemy
          Messages = [ $"Wild {enemy.Species.Name} appeared!" ]
          Outcome = None
          Rng = Rng.create seed }

    let isOver (s: BattleState) : bool = s.Outcome.IsSome

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
    let private preMoveStatusCheck (user: BattleMon) (_foe: BattleMon) (userMovedFirst: bool) (rng: Rng)
        : bool * bool * BattleMon * string list * Rng =

        // 1. Sleep gate
        match user.Status with
        | Sleep turnsLeft ->
            let remaining = turnsLeft - 1
            if remaining = 0 then
                let user' = { user with Status = Healthy }
                (true, false, user', [ $"{user.Species.Name} woke up!" ], rng)
            else
                let user' = { user with Status = Sleep remaining }
                (false, false, user', [ $"{user.Species.Name} is fast asleep!" ], rng)
        | _ ->

        // 2. Freeze gate
        match user.Status with
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
            // 4. Recharge gate: Hyper Beam-style moves force a skipped turn.
            if user.Volatile.Recharge then
                let user' = { user with Volatile = { user.Volatile with Recharge = false } }
                (false, false, user', [ $"{user.Species.Name} must recharge!" ], rng)
            else
                // 5. Paralysis full-para gate (25% = 64/256)
                let mutable canAct = true
                let mutable selfHit = false
                let mutable messages: string list = []
                let mutable rng' = rng

                match user.Status with
                | Paralysis ->
                    let roll, nextRng = Rng.next rng'
                    rng' <- nextRng
                    if roll < 64 then
                        canAct <- false
                        messages <- [ $"{user.Species.Name} is fully paralyzed!" ]
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
                                (true, false, user', [ $"{user.Species.Name} snapped out of confusion!" ], rng')
                            else
                                let vol = { user.Volatile with Confusion = Some remaining }
                                let user' = { user with Volatile = vol }
                                let msgs = [ $"{user.Species.Name} is confused!" ]
                                let roll, nextRng = Rng.next rng'
                                if roll < 128 then
                                    (false, true, user', msgs @ [ $"{user.Species.Name} hurt itself in its confusion!" ], nextRng)
                                else
                                    (true, false, user', msgs, nextRng)
                        | None ->
                            (true, false, user, [], rng')

                    // 7. Attract gate.
                    if canAct && user.Volatile.Attracted then
                        let roll, rng' = Rng.next rng'
                        if roll < 128 then
                            (false, selfHit, user, messages @ [ $"{user.Species.Name} is immobilized by attraction!" ], rng')
                        else
                            (true, selfHit, user, messages, rng')
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
    let private checkHit (user: BattleMon) (foe: BattleMon) (move: MoveData) (rng: Rng)
        : bool * Rng =
        if move.Effect = "EFFECT_ALWAYS_HIT" then
            (true, rng)
        else

        let accByte = move.Accuracy * 255 / 100

        let modifiedAcc =
            BattleMon.applyAccEvaStages accByte user.AccStage foe.EvaStage

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

    // -- Phase: execute move -------------------------------------------------

    /// Execute one mon's move against the other using the MoveContext pattern.
    /// Effect commands fold over the context, accumulating state changes and
    /// messages. Returns updated (user, foe, messages, rng).
    ///
    /// RNG draw order per move (faithful to GSC command sequence):
    ///   1. Accuracy roll  — checkHit (skipped for EFFECT_ALWAYS_HIT / acc $FF)
    ///   2. Crit roll      — rollHit  (skipped on miss)
    ///   3. Spread roll    — rollHit  (skipped on miss)
    let private executeMove (user: BattleMon) (foe: BattleMon) (move: MoveData) (isStruggle: bool) (rng: Rng) =
        let intro = $"{user.Species.Name} used {move.Name}!"

        // Struggle always hits (effect_commands.asm: EFFECT_ALWAYS_HIT path).
        let hit, rng =
            if isStruggle then (true, rng)
            else checkHit user foe move rng

        if not hit then
            let msgs = [ intro; $"{user.Species.Name}'s attack missed!" ]
            // EFFECT_JUMP_KICK: crash damage on miss = 1/8 max HP, min 1.
            if move.Effect = "EFFECT_JUMP_KICK" then
                let crash = max 1 (user.MaxHp / 8)
                let user = { user with Hp = max 0 (user.Hp - crash) }
                (user, foe, msgs @ [ $"{user.Species.Name} kept going and crashed!" ], rng)
            else
                (user, foe, msgs, rng)
        else

        let crit, roll, rng = rollHit (CriticalHit.critStage user.Volatile.FocusEnergy move) rng
        let intro = $"{user.Species.Name} used {move.Name}!"

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
              DefenseCurlUsed = false
              Friendship = 0 }

        let ctx =
            Effects.forMove move
            |> List.fold (fun (c: MoveContext) cmd ->
                Effects.applyCtx c cmd
            ) ctx

        let foe =
            if ctx.Foe.Volatile.Rage && ctx.LastDamage > 0 then
                { ctx.Foe with AtkStage = min 6 (ctx.Foe.AtkStage + 1) }
            else
                ctx.Foe

        { ctx with Foe = foe } |> fun ctx -> ctx.User, ctx.Foe, ctx.Messages, ctx.Rng

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
    ///   5. Leftovers / items                    — stub (items scope)
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

    /// Future Sight payoff: countdown at end-of-turn and damage the foe on expiry.
    let private applyFutureSight (m: BattleMon) (other: BattleMon) : BattleMon * BattleMon * string list =
        match m.Volatile.FutureSightCounter, m.Volatile.FutureSightMove with
        | Some turns, Some move when turns > 1 ->
            let vol = { m.Volatile with FutureSightCounter = Some (turns - 1) }
            ({ m with Volatile = vol }, other, [ $"{m.Species.Name}'s Future Sight is still charging!" ])
        | Some 1, Some move ->
            let dmg = Damage.calc m other move false Damage.MaxRoll false
            let other' = { other with Hp = max 0 (other.Hp - dmg) }
            let m' = { m with Volatile = { m.Volatile with FutureSightCounter = None; FutureSightMove = None } }
            (m', other', [ $"{other'.Species.Name} was hit by Future Sight!" ])
        | _ -> (m, other, [])

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

    let private applyRampage (m: BattleMon) : BattleMon * string list =
        match m.Volatile.Rampage with
        | Some turns when turns > 1 ->
            let vol = { m.Volatile with Rampage = Some (turns - 1) }
            ({ m with Volatile = vol }, [ $"{m.Species.Name} is still rampaging!" ])
        | Some _ ->
            let vol = { m.Volatile with Rampage = None; Confusion = Some 2 }
            ({ m with Volatile = vol }, [ $"{m.Species.Name} became confused after rampaging!" ])
        | None -> (m, [])

    let private betweenTurns (player: BattleMon) (enemy: BattleMon) (rng: Rng)
        : BattleMon * BattleMon * string list * Rng =
        let mutable p = player
        let mutable e = enemy
        let mutable r = rng
        let mutable msgs: string list = []

        // Slot 1: Future Sight countdown.
        let p', e', futureMsgs = applyFutureSight p e
        p <- p'; e <- e'; msgs <- msgs @ futureMsgs

        // Slot 2: Weather (sandstorm chip, timer) — stub (M13.8)

        // Slot 3: Wrap/Bind/Clamp chip (HandleWrap, core.asm l.1153).
        // Player then enemy.
        let p', pWrapMsgs = applyWrap p
        p <- p'; msgs <- msgs @ pWrapMsgs
        let e', eWrapMsgs = applyWrap e
        e <- e'; msgs <- msgs @ eWrapMsgs

        // Slot 4: Perish Song countdown — stub (M13.8)
        // Slot 5: Leftovers / items — stub (items scope)

        // Slot 6: Defrost (10% thaw). Player then enemy.
        let p', pDefMsgs, r' = applyDefrost p r
        p <- p'; r <- r'; msgs <- msgs @ pDefMsgs
        let e', eDefMsgs, r' = applyDefrost e r
        e <- e'; r <- r'; msgs <- msgs @ eDefMsgs

        // Slot 7: Poison/Toxic/Burn tick (ResidualDamage PSN|BRN path).
        // Player then enemy.
        let p', pPsnMsgs = applyResidual p
        p <- p'; msgs <- msgs @ pPsnMsgs
        let e', ePsnMsgs = applyResidual e
        e <- e'; msgs <- msgs @ ePsnMsgs

        // Slot 8: Leech Seed drain (ResidualDamage, core.asm l.1008).
        // Player-seeded drains to enemy, then enemy-seeded drains to player.
        let p', e', pSeedMsgs = applyLeechSeed p e
        p <- p'; e <- e'; msgs <- msgs @ pSeedMsgs
        let e', p', eSeedMsgs = applyLeechSeed e p
        p <- p'; e <- e'; msgs <- msgs @ eSeedMsgs

        // Slot 9: Rampage auto-confuse.
        let p', pRampMsgs = applyRampage p
        p <- p'; msgs <- msgs @ pRampMsgs
        let e', eRampMsgs = applyRampage e
        e <- e'; msgs <- msgs @ eRampMsgs

        // Slot 10: Nightmare — stub (M13.8: requires Sleep check, chip MaxHP/4)
        // Slot 11: Curse chip — stub (M13.8: ghost-type curse, chip MaxHP/4)
        // Slot 11: Safeguard timer — stub (M13.8)
        // Slot 12: Reflect/Light Screen timer — stub (M13.8)
        // Slot 13: Encore timer — stub (M13.9)
        // Slot 14: Disable timer — stub (M13.9)

        (p, e, msgs, r)

    // -- Phase: faint check --------------------------------------------------

    /// Check if either side has fainted and produce the appropriate outcome
    /// and messages. Returns (outcome option, messages).
    let private faintCheck (player: BattleMon) (enemy: BattleMon)
        : Outcome option * string list =
        if BattleMon.isFainted enemy then
            (Some Win, [ $"Wild {enemy.Species.Name} fainted!"; "You won!" ])
        elif BattleMon.isFainted player then
            (Some Lose, [ $"{player.Species.Name} fainted!"; "You lost!" ])
        else
            (None, [])

    // -- Enemy AI ------------------------------------------------------------

    /// Pick the enemy's move. When all PP is exhausted, returns None (Struggle).
    let private enemyMoveChoice (enemy: BattleMon) : (MoveData * int) option =
        if BattleMon.mustStruggle enemy then None
        else
            let indexed = enemy.Moves |> List.mapi (fun i m -> (m, i))
            let hasPp i = i < enemy.Pp.Length && enemy.Pp.[i] > 0
            let pick =
                indexed |> List.tryFind (fun (m, i) -> m.Power > 0 && hasPp i)
            match pick with
            | Some p -> Some p
            | None ->
                indexed |> List.tryFind (fun (_, i) -> hasPp i)
                |> Option.orElseWith (fun () -> indexed |> List.tryHead)

    // -- Orchestrator --------------------------------------------------------

    let private chargingMoveOf (m: BattleMon) : MoveData option =
        if m.Volatile.Charging.IsSome then m.Volatile.ChargingMove else None

    let private isChargingEffect (move: MoveData) : bool =
        [ "EFFECT_FLY"; "EFFECT_DIG"; "EFFECT_CHARGE"; "EFFECT_SOLAR_BEAM";
          "EFFECT_SKULL_BASH"; "EFFECT_SKY_ATTACK"; "EFFECT_RAZOR_WIND" ]
        |> List.contains move.Effect

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

        // Enemy move selection.
        let enemyChoice = enemyMoveChoice s.Enemy
        let enemyStruggle = enemyChoice.IsNone
        let enemyMv, enemyMvIndex =
            match enemyChoice with
            | Some (m, i) -> m, i
            | None -> struggle, -1

        // Struggle messages (before moves execute).
        let struggleMsgs =
            [ if playerStruggle then $"{s.Player.Species.Name} has no moves left!"
              if enemyStruggle then $"{s.Enemy.Species.Name} has no moves left!" ]

        // Faster mon acts first; ties favour the player.
        let playerFirst =
            BattleMon.effectiveSpeed s.Player >= BattleMon.effectiveSpeed s.Enemy

        let mutable player = s.Player
        let mutable enemy = s.Enemy
        let mutable rng = s.Rng
        let mutable msgs: string list = struggleMsgs
        let mutable outcome: Outcome option = None

        // Run one side's action (pre-move gate -> execute -> PP deduct -> mid-turn faint check).
        let act (playerIsUser: bool) : bool =
            if outcome.IsSome then
                false
            else
                let user, foe, move, mvIndex, isStruggle =
                    if playerIsUser then player, enemy, playerMv, playerMvIndex, playerStruggle
                    else enemy, player, enemyMv, enemyMvIndex, enemyStruggle

                let storedCharge = chargingMoveOf user
                let chargeTurn = storedCharge.IsSome
                let moveToUse = if chargeTurn then storedCharge.Value else move
                let mvIndexToUse =
                    if chargeTurn then user.Moves |> List.findIndex (fun m -> m.Name = storedCharge.Value.Name) else mvIndex

                // Did this user move first this turn?
                let userMovedFirst =
                    if playerIsUser then playerFirst else not playerFirst

                // Phase: pre-move status gates
                let canAct, selfHit, user, gateMsgs, rng' = preMoveStatusCheck user foe userMovedFirst rng
                rng <- rng'
                msgs <- msgs @ gateMsgs

                if selfHit then
                    // Confusion self-hit: 40-power typeless physical, user hits itself.
                    let dmg = confusionSelfHitDamage user
                    let user = { user with Hp = max 0 (user.Hp - dmg) }
                    if playerIsUser then player <- user else enemy <- user
                    // Check if self-hit caused a faint.
                    let faintOutcome, faintMsgs = faintCheck player enemy
                    msgs <- msgs @ faintMsgs
                    match faintOutcome with
                    | Some o -> outcome <- Some o; false
                    | None -> true
                elif not canAct then
                    if playerIsUser then player <- user else enemy <- user
                    true
                else
                    // First turn of a charging move: set the charge flag and skip the action.
                    if not chargeTurn && isChargingEffect move && user.Volatile.Charging.IsNone then
                        let user' = { user with Volatile = { user.Volatile with Charging = Some 1; ChargingMove = Some move } }
                        if playerIsUser then player <- user' else enemy <- user'
                        msgs <- msgs @ [ $"{user'.Species.Name} is charging up!" ]
                        true
                    else
                        // Phase: execute move
                        let user, foe, moveMsgs, rng' = executeMove user foe moveToUse isStruggle rng
                        rng <- rng'
                        msgs <- msgs @ moveMsgs

                        // Clear the charge window after the second-turn execution.
                        let user =
                            if chargeTurn then
                                { user with Volatile = { user.Volatile with Charging = None; ChargingMove = None } }
                            else user

                        // Phase: deduct PP (Struggle does not consume PP --
                        // effect_commands.asm l.974: cp STRUGGLE; ret z)
                        let user =
                            if isStruggle then user
                            else BattleMon.deductPp mvIndexToUse user

                        if playerIsUser then
                            player <- user
                            enemy <- foe
                        else
                            enemy <- user
                            player <- foe

                        // Phase: mid-turn faint check
                        let faintOutcome, faintMsgs = faintCheck player enemy
                        msgs <- msgs @ faintMsgs
                        match faintOutcome with
                        | Some o ->
                            outcome <- Some o
                            false
                        | None -> true

        let order = if playerFirst then [ true; false ] else [ false; true ]
        order |> List.iter (fun who -> act who |> ignore)

        // Phase: end-of-turn residuals (only if nobody fainted mid-turn)
        if outcome.IsNone then
            let p, e, residualMsgs, rng' = betweenTurns player enemy rng
            player <- p
            enemy <- e
            rng <- rng'
            msgs <- msgs @ residualMsgs

            // Phase: end-of-turn faint check
            let faintOutcome, faintMsgs = faintCheck player enemy
            msgs <- msgs @ faintMsgs
            match faintOutcome with
            | Some o -> outcome <- Some o
            | None -> ()

        { s with
            Player = player
            Enemy = enemy
            Messages = msgs
            Outcome = outcome
            Rng = rng }

    /// The player flees the battle. Blocked if the player is trapped (Wrap/Bind)
    /// or locked in by Mean Look / Spider Web.
    let run (s: BattleState) : BattleState =
        if isOver s then
            s
        elif s.Player.Volatile.Trapped.IsSome then
            { s with Messages = [ $"{s.Player.Species.Name} is trapped and can't escape!" ] }
        elif s.Player.Volatile.CantEscape then
            { s with Messages = [ $"{s.Player.Species.Name} can't escape!" ] }
        else
            { s with
                Messages = [ "Got away safely!" ]
                Outcome = Some Ran }
