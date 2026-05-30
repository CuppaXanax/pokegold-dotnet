namespace PokeGold.Game.Overworld

open PokeGold.Game.Core

/// The player-animation system: maps a player's facing and walk phase to the
/// sprite frame index and horizontal flip to draw.
module Animation =

    /// Choose the sprite frame index and horizontal flip for the player's current
    /// facing and walk phase. Bumping reuses the walk cycle (legs move in place);
    /// turning just shows the standing pose of the new facing.
    let frameAndFlip (p: PlayerState) : int * bool =
        let walking =
            match p.Motion with
            | Walking
            | Hopping
            | Bumping -> true
            | _ -> false

        // Walk/hop alternate the lead foot per completed step; a bump has no
        // completed steps, so it toggles within its own cycle to stay animated.
        let phase =
            match p.Motion with
            | Bumping -> (p.Progress * 2 / Player.BumpFrames) % 2
            | _ -> p.StepCount % 2

        let foot = walking && phase = 1

        // Side walking can't alternate the foot by mirroring (that would flip the
        // facing), so it steps between the walk-side and stand-side poses instead.
        let sideFrame = if walking && phase = 0 then 5 else 2

        match p.Facing with
        | Down -> (if walking then 3 else 0), foot
        | Up -> (if walking then 4 else 1), foot
        | Left -> sideFrame, false
        | Right -> sideFrame, true
