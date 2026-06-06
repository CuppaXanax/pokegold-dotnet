namespace PokeGold.Game.Battle

open PokeGold.Game.Data

module Catch =
    /// Ball multiplier (out of 255 for integer math).
    let ballMultiplier (ball: string) : int =
        match ball with
        | "MASTER_BALL" -> 255
        | "ULTRA_BALL" -> 2
        | "GREAT_BALL" -> 3  // 1.5x = 3/2, handled in formula
        | _ -> 1  // POKE_BALL and others

    /// Attempt to catch a wild mon. Returns (caught: bool, wobbles: int).
    let tryCatch (ball: string) (mon: BattleMon) (rng: Rng) : bool * int * Rng =
        if ball = "MASTER_BALL" then (true, 3, rng)
        else
            let catchRate = mon.Species.CatchRate
            let ballMod = ballMultiplier ball
            let hp3 = 3 * mon.MaxHp
            let a = max 1 ((hp3 - 2 * mon.Hp) * catchRate * ballMod / hp3)
            if a >= 255 then (true, 3, rng)
            else
                // 4 wobble checks
                let mutable caught = true
                let mutable wobbles = 0
                let mutable r = rng
                for _ in 1..4 do
                    if caught then
                        let roll, r' = Rng.next r
                        r <- r'
                        if roll < a then wobbles <- wobbles + 1
                        else caught <- false
                (caught, wobbles, r)
