namespace PokeGold.Game.Overworld

open PokeGold.Game.Core

/// The player-animation system: maps a player's facing and walk phase to the
/// sprite frame index and horizontal flip to draw.
module Animation =

    /// Choose the sprite frame index and horizontal flip for the player's current
    /// facing and walk phase.
    ///
    /// GSC drives the legs from a free-running step-frame counter, cycling a
    /// 4-phase animation — **stand, step, stand, step** — where the even phases are
    /// the neutral pose and the odd phases are a stride (the second stride mirrored
    /// to swap the lead foot). A walk advances a phase every 4 frames; a wall-bump
    /// is the same march at half speed (every 8). We read the phase straight off
    /// the motion's `Progress` so it matches `SetFacingStepAction` /
    /// `SetFacingBumpAction` (`OBJECT_STEP_FRAME >> 2` / `>> 3`).
    let frameAndFlip (p: PlayerState) : int * bool =
        let phase =
            match p.Motion with
            | Bumping -> (p.Progress >>> 3) &&& 3
            | Walking
            | Hopping -> (p.Progress >>> 2) &&& 3
            | _ -> 0 // Standing / Turning hold the neutral pose.

        // Odd phases (1, 3) are a stride; phase 3 mirrors to swap the lead foot.
        let stepping = phase &&& 1 = 1
        let footFlip = phase = 3

        match p.Facing with
        | Down -> (if stepping then 3 else 0), footFlip
        | Up -> (if stepping then 4 else 1), footFlip
        // Side strides can't mirror the foot (that would flip the facing), so they
        // toggle the walk-side and stand-side poses instead; the flip just selects
        // left vs right facing.
        | Left -> (if stepping then 5 else 2), false
        | Right -> (if stepping then 5 else 2), true
