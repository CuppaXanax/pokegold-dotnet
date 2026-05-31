namespace PokeGold.Game.Ui

open PokeGold.Game.Core

/// Pure geometry helpers for bordered box layouts. `WindowRenderer` calls these
/// to determine which tile code to draw at each position — keeping all math
/// testable without a framebuffer or font.
module Window =

    /// The tile code (Charmap byte) to draw at relative position (rx, ry) within
    /// a box whose tile dimensions are `width × height`.
    ///
    ///   rx = 0           → left edge
    ///   rx = width  - 1  → right edge
    ///   ry = 0           → top edge
    ///   ry = height - 1  → bottom edge
    ///   corners take priority; interior → Space.
    let boxGlyph (width: int) (height: int) (rx: int) (ry: int) : byte =
        let top    = ry = 0
        let bottom = ry = height - 1
        let left   = rx = 0
        let right  = rx = width  - 1
        if   top    && left  then Charmap.BoxTopLeft
        elif top    && right then Charmap.BoxTopRight
        elif bottom && left  then Charmap.BoxBottomLeft
        elif bottom && right then Charmap.BoxBottomRight
        elif top    || bottom then Charmap.BoxHoriz
        elif left   || right  then Charmap.BoxVert
        else                       Charmap.Space
