namespace PokeGold.Game.Ui

/// Pure scrolling-list cursor state machine.
/// Invariants (maintained by all operations):
///   - When Count > 0: 0 ≤ Cursor < Count
///   - When Count > 0: Top ≤ Cursor < Top + Visible
///   - When Count = 0: Cursor = 0, Top = 0
type MenuList =
    { Count: int
      Cursor: int
      Top: int
      Visible: int
      Wrap: bool }

module MenuList =

    /// Create a new menu list. `count` is the total number of items; `visible`
    /// is how many rows are shown at once; `wrap` controls whether navigation
    /// wraps around at the ends.
    let create (count: int) (visible: int) (wrap: bool) : MenuList =
        { Count = max 0 count
          Cursor = 0
          Top = 0
          Visible = max 1 visible
          Wrap = wrap }

    // Re-clamp Top so that Top ≤ Cursor < Top + Visible, keeping Top ≥ 0.
    let private fixTop (ml: MenuList) : MenuList =
        if ml.Count = 0 then
            { ml with Cursor = 0; Top = 0 }
        else
            let cursor = max 0 (min (ml.Count - 1) ml.Cursor)
            let top =
                if cursor < ml.Top then cursor
                elif cursor >= ml.Top + ml.Visible then cursor - ml.Visible + 1
                else ml.Top
            { ml with Cursor = cursor; Top = max 0 top }

    /// Move the cursor up by one row. Clamps at 0 when Wrap = false; wraps to
    /// Count - 1 when Wrap = true.
    let moveUp (ml: MenuList) : MenuList =
        if ml.Count = 0 then ml
        else
            let cursor =
                if ml.Cursor = 0 then (if ml.Wrap then ml.Count - 1 else 0)
                else ml.Cursor - 1
            fixTop { ml with Cursor = cursor }

    /// Move the cursor down by one row. Clamps at Count - 1 when Wrap = false;
    /// wraps to 0 when Wrap = true.
    let moveDown (ml: MenuList) : MenuList =
        if ml.Count = 0 then ml
        else
            let cursor =
                if ml.Cursor = ml.Count - 1 then (if ml.Wrap then 0 else ml.Count - 1)
                else ml.Cursor + 1
            fixTop { ml with Cursor = cursor }

    /// Jump the cursor directly to index `i` (clamped to the valid range).
    let moveTo (i: int) (ml: MenuList) : MenuList =
        fixTop { ml with Cursor = i }
