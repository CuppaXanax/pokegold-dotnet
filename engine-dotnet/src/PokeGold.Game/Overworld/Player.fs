namespace PokeGold.Game.Overworld

open PokeGold.Game.Core

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
      /// True while a grid step is in progress.
      Moving: bool
      /// Cell the current step started from (for interpolation).
      SrcX: int
      SrcY: int
      /// Frames elapsed in the current step (0..StepFrames).
      Progress: int
      /// Completed steps so far (drives the walk-cycle phase).
      StepCount: int }

module Player =

    /// Frames one full grid step takes.
    [<Literal>]
    let StepFrames = 16

    /// Pixel size of one collision cell.
    [<Literal>]
    let CellPixels = 16

    /// A player standing at cell (cx, cy) facing down, not moving.
    let create (cx: int) (cy: int) : PlayerState =
        { CellX = cx
          CellY = cy
          Facing = Down
          Moving = false
          SrcX = cx
          SrcY = cy
          Progress = 0
          StepCount = 0 }

    /// Sprite top-left in world pixels, interpolated during a step.
    let worldPixel (p: PlayerState) : int * int =
        if p.Moving then
            let t = float p.Progress / float StepFrames
            let px = float (p.SrcX * CellPixels) + float ((p.CellX - p.SrcX) * CellPixels) * t
            let py = float (p.SrcY * CellPixels) + float ((p.CellY - p.SrcY) * CellPixels) * t
            int (round px), int (round py)
        else
            p.CellX * CellPixels, p.CellY * CellPixels
