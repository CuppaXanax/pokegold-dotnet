namespace PokeGold.Game.Overworld

open PokeGold.Game.Core

/// The player-animation system: maps a player's facing and walk phase to the
/// sprite frame index and horizontal flip to draw.
module Animation =

    /// Choose the sprite frame index and horizontal flip for the player's current
    /// facing and walk phase. Bumping reuses the walk cycle (legs move in place);
    /// turning just shows the standing pose of the new facing. A bump counts as a
    /// step of cadence (see Movement.step), so the foot alternates once per cycle —
    /// at walk speed, not double.
    let frameAndFlip (p: PlayerState) : int * bool =
        let walking =
            match p.Motion with
            | Walking
            | Hopping
            | Bumping -> true
            | _ -> false

        let foot = walking && p.StepCount % 2 = 1

        // Side walking can't alternate the foot by mirroring (that would flip the
        // facing), so it steps between the walk-side and stand-side poses instead.
        let sideFrame = if walking && p.StepCount % 2 = 0 then 5 else 2

        match p.Facing with
        | Down -> (if walking then 3 else 0), foot
        | Up -> (if walking then 4 else 1), foot
        | Left -> sideFrame, false
        | Right -> sideFrame, true
