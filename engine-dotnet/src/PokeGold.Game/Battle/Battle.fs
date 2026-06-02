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
    /// Gate order (M13.3 — non-volatile only; M13.4 adds flinch/confusion):
    ///   1. Sleep — decrement counter; wake at 0, else "fast asleep!" (no RNG draw)
    ///   2. Freeze — "frozen solid!"; Flame Wheel / Sacred Fire self-defrost (no RNG)
    ///   3. Paralysis — 25% full-para (1 RNG draw: roll < 64 = can't act)
    ///   (Flinch, Confusion, Attract — M13.4)
    let private preMoveStatusCheck (user: BattleMon) (_foe: BattleMon) (rng: Rng)
        : bool * BattleMon * string list * Rng =

        // 1. Sleep gate
        match user.Status with
        | Sleep turnsLeft ->
            let remaining = turnsLeft - 1
            if remaining = 0 then
                let user' = { user with Status = Healthy }
                // Mon wakes up; it CAN act this turn (faithful to GSC: woke_up then
                // continues through the remaining gates).
                (true, user', [ $"{user.Species.Name} woke up!" ], rng)
            else
                let user' = { user with Status = Sleep remaining }
                (false, user', [ $"{user.Species.Name} is fast asleep!" ], rng)
        | _ ->

        // 2. Freeze gate
        match user.Status with
        | Freeze ->
            // Flame Wheel and Sacred Fire self-defrost (effect_commands.asm l.209-213).
            // The move has already been selected; we check the user's chosen move via
            // the move that will be passed to executeMove. However, preMoveStatusCheck
            // receives only user/foe/rng — the move is resolved by the caller. We use
            // a simplified approach: frozen always blocks. The self-defrost for
            // Flame Wheel / Sacred Fire is a documented hook for M13.6/M13.7.
            // (The end-of-turn HandleDefrost gives the 10% random thaw.)
            (false, user, [ $"{user.Species.Name} is frozen solid!" ], rng)
        | _ ->

        // 3. Paralysis full-para gate (25% = 64/256)
        match user.Status with
        | Paralysis ->
            let roll, rng' = Rng.next rng
            if roll < 64 then
                (false, user, [ $"{user.Species.Name} is fully paralyzed!" ], rng')
            else
                (true, user, [], rng')
        | _ ->

        (true, user, [], rng)

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
              IsStruggle = isStruggle }

        let ctx =
            Effects.forMove move
            |> List.fold (fun (c: MoveContext) cmd ->
                Effects.applyCtx c cmd
            ) ctx

        ctx.User, ctx.Foe, ctx.Messages, ctx.Rng

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
    /// We don't track "just got frozen" since freeze infliction is M13.6;
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

    let private betweenTurns (player: BattleMon) (enemy: BattleMon) (rng: Rng)
        : BattleMon * BattleMon * string list * Rng =
        let mutable p = player
        let mutable e = enemy
        let mutable r = rng
        let mutable msgs: string list = []

        // Slots 1-5: stubs (future sight, weather, wrap, perish, items)

        // Slot 6: Defrost (10% thaw). Player then enemy.
        let p', pDefMsgs, r' = applyDefrost p r
        p <- p'; r <- r'; msgs <- msgs @ pDefMsgs
        let e', eDefMsgs, r' = applyDefrost e r
        e <- e'; r <- r'; msgs <- msgs @ eDefMsgs

        // Slot 7: Poison/Toxic tick. Player then enemy.
        let p', pPsnMsgs = applyResidual p
        p <- p'; msgs <- msgs @ pPsnMsgs
        let e', ePsnMsgs = applyResidual e
        e <- e'; msgs <- msgs @ ePsnMsgs

        // Slots 8+: Burn is handled by applyResidual above (same path as poison
        // in the disassembly — ResidualDamage checks PSN|BRN together).

        // Slots 9-15: stubs (leech seed, nightmare, curse, safeguard, screens,
        // encore, disable — M13.4/M13.8)

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

                // Phase: pre-move status gates
                let canAct, user, gateMsgs, rng' = preMoveStatusCheck user foe rng
                rng <- rng'
                msgs <- msgs @ gateMsgs

                if not canAct then
                    if playerIsUser then player <- user else enemy <- user
                    true
                else

                // Phase: execute move
                let user, foe, moveMsgs, rng' = executeMove user foe move isStruggle rng
                rng <- rng'
                msgs <- msgs @ moveMsgs

                // Phase: deduct PP (Struggle does not consume PP —
                // effect_commands.asm l.974: cp STRUGGLE; ret z)
                let user =
                    if isStruggle then user
                    else BattleMon.deductPp mvIndex user

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

    /// The player flees the battle (always succeeds in the slice).
    let run (s: BattleState) : BattleState =
        if isOver s then
            s
        else
            { s with
                Messages = [ "Got away safely!" ]
                Outcome = Some Ran }
