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
