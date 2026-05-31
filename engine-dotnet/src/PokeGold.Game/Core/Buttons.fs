namespace PokeGold.Game.Core

/// The Game Boy button set for one frame of input. The host fills this from the
/// keyboard (or a gamepad) and passes it to the game core each tick; the core
/// has no knowledge of the physical input device.
[<Struct>]
type Buttons =
    { Up: bool
      Down: bool
      Left: bool
      Right: bool
      A: bool
      B: bool
      Start: bool
      Select: bool }

module Buttons =

    /// No buttons held.
    let none =
        { Up = false
          Down = false
          Left = false
          Right = false
          A = false
          B = false
          Start = false
          Select = false }

    /// Bitwise AND of two button frames: a button is set only if held in both.
    let intersect (a: Buttons) (b: Buttons) : Buttons =
        { Up = a.Up && b.Up
          Down = a.Down && b.Down
          Left = a.Left && b.Left
          Right = a.Right && b.Right
          A = a.A && b.A
          B = a.B && b.B
          Start = a.Start && b.Start
          Select = a.Select && b.Select }

    /// `a` with every button that is set in `mask` cleared (a AND NOT mask).
    let except (a: Buttons) (mask: Buttons) : Buttons =
        { Up = a.Up && not mask.Up
          Down = a.Down && not mask.Down
          Left = a.Left && not mask.Left
          Right = a.Right && not mask.Right
          A = a.A && not mask.A
          B = a.B && not mask.B
          Start = a.Start && not mask.Start
          Select = a.Select && not mask.Select }
