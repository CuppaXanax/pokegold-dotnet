namespace PokeGold.Game.Overworld

open PokeGold.Game.Core

/// The player-animation system: maps a player's facing and walk phase to the
/// sprite frame index and horizontal flip to draw.
module Animation =

    /// Choose the sprite frame index and horizontal flip for the player's current
    /// facing and walk phase.
    ///
    /// GSC drives the legs from a free-running step-frame counter (`OBJECT_STEP_FRAME`),
    /// cycling a 4-phase animation — **neutral, stride, neutral, stride** — where the
    /// even phases are the standing pose and the odd phases are a stride (the second
    /// stride mirrored to swap the lead foot, per `data/sprites/facings.asm`). Because
    /// the counter free-runs and isn't reset between tiles, each walked tile shows a
    /// neutral pose then a stride and the lead foot alternates tile to tile.
    ///
    /// Our walk tile is 16 frames (GSC's is 8), so a phase lasts 8 frames here —
    /// `AnimFrame >> 3`, two phases per tile, matching `SetFacingStepAction`'s
    /// `>> 2` at our 2× frame scale. A wall-bump marches at half that speed
    /// (`SetFacingBumpAction` shifts by one more bit → `AnimFrame >> 4`). A ledge hop
    /// holds a single stride for the whole leap, alternating the foot per hop.
    let frameAndFlip (p: PlayerState) : int * bool =
        let phase =
            match p.Motion with
            | Walking -> (p.AnimFrame >>> 3) &&& 3
            | Bumping -> (p.AnimFrame >>> 4) &&& 3
            | Hopping -> 1 ||| ((p.StepCount &&& 1) <<< 1) // hold a stride; foot per hop
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
