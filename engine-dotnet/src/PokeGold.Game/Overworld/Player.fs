namespace PokeGold.Game.Overworld

open PokeGold.Game.Core

/// What the player is doing this frame. Faithful to GSC's overworld micro-states:
/// pressing a direction you already face *steps*; pressing one you don't face
/// *turns in place* (rotate, no translation); a blocked press *bumps* (the walk
/// animation plays in place plus a sound hook). Only Walking and Hopping move the
/// player between cells — Turning and Bumping hold position.
type Motion =
    | Standing
    | Walking
    | Hopping
    | Turning
    | Bumping

/// The player's position and animation state on the overworld grid. Movement is
/// modeled on a 16-px cell grid with smooth interpolation during a step; this
/// record is immutable and advanced by the pure `Movement.step` system, which
/// makes the state trivial to test and to serialize for save/load later.
type PlayerState =
    { /// Logical cell the player occupies (or is moving into), 16-px grid.
      CellX: int
      CellY: int
      /// The direction the player faces.
      Facing: Direction
      /// What the player is doing this frame (idle / step / hop / turn / bump).
      Motion: Motion
      /// Cell the current step started from (for interpolation).
      SrcX: int
      SrcY: int
      /// Frames elapsed in the current motion (0 .. its duration).
      Progress: int
      /// Completed steps so far (drives the walk-cycle phase).
      StepCount: int
      /// Set true for exactly the one frame a wall-bump begins, so a future audio
      /// system can play the bump SFX. Transient; always false at rest.
      Bumped: bool }

    /// True while the player is translating between cells (a walk or a hop).
    member this.Moving =
        match this.Motion with
        | Walking
        | Hopping -> true
        | _ -> false

    /// True while the current motion is a 2-cell ledge hop.
    member this.Hopping = (this.Motion = Motion.Hopping)

    /// True whenever the player is mid-motion and not free to take fresh input.
    member this.Busy = (this.Motion <> Standing)

module Player =

    /// Frames one full grid step takes.
    [<Literal>]
    let StepFrames = 16

    /// Frames a ledge hop takes — twice a step, since it covers two cells at the
    /// same pixel speed as a walk.
    [<Literal>]
    let HopFrames = 32

    /// Frames a turn-in-place takes — about half a step, mirroring GSC's quick
    /// pivot (a 4-frame turn against an 8-frame walk).
    [<Literal>]
    let TurnFrames = 8

    /// Frames a wall-bump's in-place walk cycle runs before it can repeat (and
    /// re-pulse the SFX hook), matching one normal step.
    [<Literal>]
    let BumpFrames = StepFrames

    /// GSC's 16-entry ledge-hop vertical offset table (px, negative = up). The
    /// sprite rises to a −12 px apex, holds there, then lands flat — replacing a
    /// symmetric sine so the arc matches the original (engine/overworld
    /// map_objects.asm `.y_offsets`).
    let private HopYOffsets =
        [| -4; -6; -8; -10; -11; -12; -12; -12; -11; -10; -9; -8; -6; -4; 0; 0 |]

    /// Pixel size of one collision cell.
    [<Literal>]
    let CellPixels = 16

    /// Frames the player's current motion runs for.
    let stepDuration (p: PlayerState) : int =
        match p.Motion with
        | Hopping -> HopFrames
        | Turning -> TurnFrames
        | Bumping -> BumpFrames
        | _ -> StepFrames

    /// A player standing at cell (cx, cy) facing down, not moving.
    let create (cx: int) (cy: int) : PlayerState =
        { CellX = cx
          CellY = cy
          Facing = Down
          Motion = Standing
          SrcX = cx
          SrcY = cy
          Progress = 0
          StepCount = 0
          Bumped = false }

    /// Sprite top-left in world pixels, interpolated during a step. A ledge hop
    /// adds GSC's tabled vertical arc so the sprite lifts to a −12 px apex, holds,
    /// then lands flat.
    let worldPixel (p: PlayerState) : int * int =
        if p.Moving then
            let t = float p.Progress / float (stepDuration p)
            let px = float (p.SrcX * CellPixels) + float ((p.CellX - p.SrcX) * CellPixels) * t
            let py = float (p.SrcY * CellPixels) + float ((p.CellY - p.SrcY) * CellPixels) * t

            let arc =
                if p.Hopping then
                    let idx = min 15 (p.Progress * 16 / HopFrames)
                    float (-HopYOffsets.[idx])
                else
                    0.0

            int (round px), int (round (py - arc))
        else
            p.CellX * CellPixels, p.CellY * CellPixels
