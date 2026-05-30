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
      /// Frames elapsed in the current step (0..StepFrames, or 0..HopFrames).
      Progress: int
      /// Completed steps so far (drives the walk-cycle phase).
      StepCount: int
      /// True while the current step is a 2-cell ledge hop (longer, arced).
      Hopping: bool }

module Player =

    /// Frames one full grid step takes.
    [<Literal>]
    let StepFrames = 16

    /// Frames a ledge hop takes — twice a step, since it covers two cells at the
    /// same pixel speed as a walk.
    [<Literal>]
    let HopFrames = 32

    /// Peak height (px) of the ledge-hop arc.
    [<Literal>]
    let HopArc = 8.0

    /// Pixel size of one collision cell.
    [<Literal>]
    let CellPixels = 16

    /// Frames the player's current step (walk or hop) runs for.
    let stepDuration (p: PlayerState) : int =
        if p.Hopping then HopFrames else StepFrames

    /// A player standing at cell (cx, cy) facing down, not moving.
    let create (cx: int) (cy: int) : PlayerState =
        { CellX = cx
          CellY = cy
          Facing = Down
          Moving = false
          SrcX = cx
          SrcY = cy
          Progress = 0
          StepCount = 0
          Hopping = false }

    /// Sprite top-left in world pixels, interpolated during a step. A ledge hop
    /// adds a `sin`-shaped vertical arc so the sprite lifts then lands.
    let worldPixel (p: PlayerState) : int * int =
        if p.Moving then
            let t = float p.Progress / float (stepDuration p)
            let px = float (p.SrcX * CellPixels) + float ((p.CellX - p.SrcX) * CellPixels) * t
            let py = float (p.SrcY * CellPixels) + float ((p.CellY - p.SrcY) * CellPixels) * t
            let arc = if p.Hopping then sin (System.Math.PI * t) * HopArc else 0.0
            int (round px), int (round (py - arc))
        else
            p.CellX * CellPixels, p.CellY * CellPixels
