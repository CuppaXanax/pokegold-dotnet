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
    /// (`1 out_of 15` ≈ 17/256).
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

    // Roll a critical hit and the 85–100% damage spread for one hit.
    let private rollHit (rng: Rng) : bool * int * Rng =
        let critByte, rng = Rng.next rng
        let spread, rng = Rng.next rng
        let crit = critByte < CritThreshold
        let roll = Damage.MinRoll + spread % (Damage.MaxRoll - Damage.MinRoll + 1)
        crit, roll, rng

    // Execute one mon's move against the other, returning the updated attacker,
    // defender, the lines to show, and the advanced RNG.
    let private executeMove (user: BattleMon) (foe: BattleMon) (move: MoveData) (rng: Rng) =
        let crit, roll, rng = rollHit rng
        let intro = $"{user.Species.Name} used {move.Name}!"

        let mutable u = user
        let mutable f = foe
        let mutable msgs = [ intro ]

        for cmd in Effects.forMove move do
            let u', f', notes = Effects.apply u f move crit roll cmd
            u <- u'
            f <- f'
            msgs <- msgs @ notes

        u, f, msgs, rng

    // The enemy AI for the slice: use the first damaging move, else the first.
    let private enemyMove (enemy: BattleMon) : MoveData =
        match enemy.Moves |> List.tryFind (fun m -> m.Power > 0) with
        | Some m -> m
        | None -> List.head enemy.Moves

    /// The player selects a move (by index into their move list). This resolves a
    /// whole turn: both sides act in speed order, faints are checked between
    /// actions, and the outcome is set if the battle ends.
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
        let mutable msgs = []
        let mutable outcome = None

        // Run one side's action; returns false if the battle ended.
        let act (playerIsUser: bool) : bool =
            if outcome.IsSome then
                false
            else
                let user, foe, move =
                    if playerIsUser then player, enemy, playerMv else enemy, player, enemyMv

                let user, foe, lines, rng' = executeMove user foe move rng
                rng <- rng'
                msgs <- msgs @ lines

                if playerIsUser then
                    player <- user
                    enemy <- foe
                else
                    enemy <- user
                    player <- foe

                if BattleMon.isFainted enemy then
                    msgs <- msgs @ [ $"Wild {enemy.Species.Name} fainted!"; "You won!" ]
                    outcome <- Some Win
                    false
                elif BattleMon.isFainted player then
                    msgs <- msgs @ [ $"{player.Species.Name} fainted!"; "You lost!" ]
                    outcome <- Some Lose
                    false
                else
                    true

        let order = if playerFirst then [ true; false ] else [ false; true ]
        order |> List.iter (fun who -> act who |> ignore)

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
