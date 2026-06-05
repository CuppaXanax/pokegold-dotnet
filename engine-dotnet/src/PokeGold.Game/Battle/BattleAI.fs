namespace PokeGold.Game.Battle

open PokeGold.Game.Data

/// Basic Gen-2 style battle AI: score each available move and pick the highest.
/// Source: engine/battle/ai/scoring.asm (simplified)
module BattleAI =

    /// Score a single move for the enemy to use against the player.
    /// Higher score = better move choice.
    let scoreMove (user: BattleMon) (target: BattleMon) (move: MoveData) : int =
        let mutable score = 100

        if move.Power > 0 then
            score <- score + move.Power / 2

            if move.Type = user.Species.Type1 || move.Type = user.Species.Type2 then
                score <- score + 20

            let eff = Damage.effectivenessTimesTen move target
            if eff > 10 then score <- score + 40
            elif eff = 0 then score <- score - 200
            elif eff < 10 then score <- score - 20
        else
            match move.Effect with
            | "EFFECT_SLEEP" | "EFFECT_PARALYZE" | "EFFECT_TOXIC" | "EFFECT_CONFUSE" ->
                if target.Status = Healthy && target.Volatile.Confusion.IsNone then
                    score <- score + 30
                else
                    score <- score - 50
            | _ ->
                score <- score + 10

        if move.Power > 0 && target.Hp > 0 then
            let estDmg = move.Power * 2
            if estDmg > target.Hp * 3 then
                score <- score - 10

        score

    /// Pick the best move index for the enemy, given their available moves.
    /// Returns (MoveData, moveIndex) or None if must Struggle.
    let chooseMove (user: BattleMon) (target: BattleMon) : (MoveData * int) option =
        if user.Moves.IsEmpty then None
        elif not user.Pp.IsEmpty && user.Pp |> List.forall (fun pp -> pp = 0) then None
        else
            let indexed = user.Moves |> List.mapi (fun i m -> (m, i))
            let usable =
                indexed
                |> List.filter (fun (_, i) -> i < user.Pp.Length && user.Pp.[i] > 0)
            let candidates = if usable.IsEmpty then indexed else usable

            candidates
            |> List.map (fun (m, i) -> (m, i, scoreMove user target m))
            |> List.sortByDescending (fun (_, _, s) -> s)
            |> List.tryHead
            |> Option.map (fun (m, i, _) -> (m, i))
