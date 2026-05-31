namespace PokeGold.Game.Ui

open PokeGold.Game.Core

/// Rising-edge state for a single frame: true = button was just pressed this frame.
[<Struct>]
type EdgeState =
    { Up: bool
      Down: bool
      Left: bool
      Right: bool
      A: bool
      B: bool
      Start: bool
      Select: bool }

/// Tracks the previous button state to compute rising edges (pressed-this-frame).
/// Extracted from `YesNoScene` so every menu can reuse the same edge detection
/// without duplicating the `prev` latch and `pressed cur was` idiom.
///
/// `initial` seeds the previous-frame latch. Pass the buttons that were held at
/// the moment a scene was opened so the press that *opened* the scene doesn't
/// register as a fresh rising edge on its first frame (e.g. holding Start to open
/// the start menu must not immediately close it). Defaults to `Buttons.none`.
type EdgeDetector(?initial: Buttons) =
    let mutable prev = defaultArg initial Buttons.none

    /// Call once per frame with the current button state; returns which buttons
    /// were newly pressed (rising edge) this frame. Updates the internal latch.
    member _.Update(cur: Buttons) : EdgeState =
        let pressed c w = c && not w

        let edges =
            { Up = pressed cur.Up prev.Up
              Down = pressed cur.Down prev.Down
              Left = pressed cur.Left prev.Left
              Right = pressed cur.Right prev.Right
              A = pressed cur.A prev.A
              B = pressed cur.B prev.B
              Start = pressed cur.Start prev.Start
              Select = pressed cur.Select prev.Select }

        prev <- cur
        edges
