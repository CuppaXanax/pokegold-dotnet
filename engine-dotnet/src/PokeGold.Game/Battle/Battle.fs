namespace PokeGold.Game.Battle

open PokeGold.Game.Data

/// How a battle ended, from the player's perspective.
type Outcome =
    | Win
    | Lose
    | Ran

/// A tiny deterministic RNG (a linear congruential generator) so battles are
/// reproducible and seedable. Yields bytes in 0..255, matching the hardware's
/// `BattleRandom`.
type Rng = { State: uint32 }

module Rng =
    let create (seed: uint32) : Rng = { State = seed }

    let next (r: Rng) : int * Rng =
        let s = r.State * 1103515245u + 12345u
        int ((s >>> 16) &&& 0xFFu), { State = s }

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
      Messages: string list }

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

    /// Stage-0 critical hit chance from `data/battle/critical_hit_chances.asm`
    /// (`1 out_of 15` ~ 17/256).
    [<Literal>]
    let private CritThreshold = 17

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

    /// Pre-move status gate. Returns (canAct, updatedUser, messages).
    /// Currently always allows the move (stub). M13.3 will add sleep/freeze/
    /// paralysis/confusion/flinch checks here.
    let private preMoveStatusCheck (user: BattleMon) (_foe: BattleMon) (_rng: Rng)
        : bool * BattleMon * string list * Rng =
        // Extension point: M13.3 inserts sleep wake check, freeze thaw,
        // paralysis full-para, M13.4 inserts confusion self-hit, flinch.
        (true, user, [], _rng)

    // -- Phase: roll hit (crit + damage spread) ------------------------------

    let private rollHit (rng: Rng) : bool * int * Rng =
        let critByte, rng = Rng.next rng
        let spread, rng = Rng.next rng
        let crit = critByte < CritThreshold
        let roll = Damage.MinRoll + spread % (Damage.MaxRoll - Damage.MinRoll + 1)
        crit, roll, rng

    // -- Phase: execute move -------------------------------------------------

    /// Execute one mon's move against the other using the MoveContext pattern.
    /// Effect commands fold over the context, accumulating state changes and
    /// messages. Returns updated (user, foe, messages, rng).
    let private executeMove (user: BattleMon) (foe: BattleMon) (move: MoveData) (rng: Rng) =
        let crit, roll, rng = rollHit rng
        let intro = $"{user.Species.Name} used {move.Name}!"

        let ctx : MoveContext =
            { User = user
              Foe = foe
              Move = move
              Crit = crit
              Roll = roll
              Rng = rng
              Messages = [ intro ] }

        let ctx =
            Effects.forMove move
            |> List.fold (fun (c: MoveContext) cmd ->
                let u', f', notes = Effects.apply c.User c.Foe c.Move c.Crit c.Roll cmd
                { c with User = u'; Foe = f'; Messages = c.Messages @ notes }
            ) ctx

        ctx.User, ctx.Foe, ctx.Messages, ctx.Rng

    // -- Phase: end-of-turn residuals (between turns) ------------------------

    /// End-of-turn residual effects, called after both sides have acted and
    /// mid-turn faint checks have passed. Returns updated (player, enemy, msgs).
    ///
    /// EXTENSION POINT: Later slices insert effects here in the canonical
    /// order from HandleBetweenTurnEffects (effect_commands.asm):
    ///   1. Future Sight countdown
    ///   2. Weather (sandstorm chip, sun/rain timer)
    ///   3. Wrap/bind/clamp chip
    ///   4. Perish Song countdown
    ///   5. Leech Seed drain
    ///   6. Poison/Toxic tick
    ///   7. Burn tick
    ///   8. Nightmare
    ///   9. Curse chip
    ///  10. Safeguard timer
    ///  11. Reflect/Light Screen timer
    ///  12. Encore timer
    ///  13. Disable timer
    /// Each is a pure function (player, enemy, rng) -> (player, enemy, msgs, rng).
    /// The list below is executed in order; later slices append to it.
    let private betweenTurns (player: BattleMon) (enemy: BattleMon) (rng: Rng)
        : BattleMon * BattleMon * string list * Rng =
        // Stub: no residuals yet. M13.3/M13.4/M13.8 will populate this.
        (player, enemy, [], rng)

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

    let private enemyMove (enemy: BattleMon) : MoveData =
        match enemy.Moves |> List.tryFind (fun m -> m.Power > 0) with
        | Some m -> m
        | None -> List.head enemy.Moves

    // -- Orchestrator --------------------------------------------------------

    /// The player selects a move (by index into their move list). This resolves a
    /// whole turn: both sides act in speed order, faints are checked between
    /// actions, end-of-turn residuals run, and the outcome is set if the battle ends.
    let chooseMove (index: int) (s: BattleState) : BattleState =
        if isOver s then
            s
        else

        let playerMv = s.Player.Moves.[index]
        let enemyMv = enemyMove s.Enemy

        // Faster mon acts first; ties favour the player.
        let playerFirst =
            BattleMon.effectiveSpeed s.Player >= BattleMon.effectiveSpeed s.Enemy

        let mutable player = s.Player
        let mutable enemy = s.Enemy
        let mutable rng = s.Rng
        let mutable msgs: string list = []
        let mutable outcome: Outcome option = None

        // Run one side's action (pre-move gate -> execute -> mid-turn faint check).
        let act (playerIsUser: bool) : bool =
            if outcome.IsSome then
                false
            else
                let user, foe, move =
                    if playerIsUser then player, enemy, playerMv else enemy, player, enemyMv

                // Phase: pre-move status gates
                let canAct, user, gateMsgs, rng' = preMoveStatusCheck user foe rng
                rng <- rng'
                msgs <- msgs @ gateMsgs

                if not canAct then
                    if playerIsUser then player <- user else enemy <- user
                    true
                else

                // Phase: execute move
                let user, foe, moveMsgs, rng' = executeMove user foe move rng
                rng <- rng'
                msgs <- msgs @ moveMsgs

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
