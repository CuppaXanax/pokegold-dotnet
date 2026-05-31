namespace PokeGold.Game.Render

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Ui

/// Shared rendering helpers for bordered boxes, encoded strings, and cursor
/// lists — consumed by all menu scenes. Drawing is done via `Graphics.drawTile`
/// directly into a `Framebuffer`; all geometry math lives in `Window` (pure).
module WindowRenderer =

    let private drawCode (fb: Framebuffer) (font: Font) (palette: Palette) (col: int) (row: int) (code: byte) =
        Graphics.drawTile fb palette (col * 8) (row * 8) (Font.glyph font code)

    /// Draw a bordered box (border glyphs + space-filled interior) at a tile
    /// rect.  Produces the same tiles as the inline border code in `YesNoScene`
    /// and `TextRenderer.drawBorder`.
    ///
    ///   left, top          — tile column/row of the top-left corner
    ///   width, height      — tile dimensions (must be ≥ 2 × 2 for a visible border)
    let drawBox (fb: Framebuffer) (font: Font) (palette: Palette) (left: int) (top: int) (width: int) (height: int) =
        for ry in 0 .. height - 1 do
            for rx in 0 .. width - 1 do
                let code = Window.boxGlyph width height rx ry
                drawCode fb font palette (left + rx) (top + ry) code

    /// Draw `s` (encoded via `Charmap`) starting at tile column `col`, row `row`.
    let drawString (fb: Framebuffer) (font: Font) (palette: Palette) (col: int) (row: int) (s: string) =
        Charmap.encode s
        |> Array.iteri (fun i code -> drawCode fb font palette (col + i) row code)

    /// Draw a vertical list of strings with the ▶ cursor glyph (0xED) on the
    /// selected row. `items` is the visible slice to render (call site is
    /// responsible for windowing via `MenuList`); `cursorIndex` is the index
    /// within `items` that should show the cursor.
    ///
    ///   left   — tile column of the cursor glyph; text starts at left + 1
    ///   top    — tile row of the first item
    let drawList (fb: Framebuffer) (font: Font) (palette: Palette) (left: int) (top: int) (items: string seq) (cursorIndex: int) =
        items
        |> Seq.iteri (fun i item ->
            let row = top + i
            if i = cursorIndex then
                drawCode fb font palette left row 0xEDuy // ▶
            drawString fb font palette (left + 1) row item)
