namespace PokeGold.Game.Render

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Text

/// Draws a speech text box at the bottom of the screen, matching the GSC layout
/// (`SpeechTextbox`/`TextboxBorder` in `home/text.asm`): a 20×6 box at the bottom
/// six tile rows, a box-drawing border, and two interior text lines.
module TextRenderer =

    // Screen geometry in tiles (Game Boy: 20×18).
    [<Literal>]
    let private ScreenW = 20

    [<Literal>]
    let private BoxTop = 12 // TEXTBOX_Y = SCREEN_HEIGHT(18) - TEXTBOX_HEIGHT(6)

    [<Literal>]
    let private BoxH = 6 // TEXTBOX_HEIGHT

    [<Literal>]
    let private InnerX = 1 // TEXTBOX_INNERX

    [<Literal>]
    let private FirstTextRow = 14 // TEXTBOX_INNERY; second line is +2 (row 16)

    /// Black-on-white text palette (font glyphs are index 0 = bg, 3 = fg).
    let palette: Palette =
        Palette.ofColors
            [ Palette.rgb555 31 31 31 // 0: white
              Palette.rgb555 21 21 21 // 1
              Palette.rgb555 10 10 10 // 2
              Palette.rgb555 0 0 0 ] // 3: black

    let private drawCode (fb: Framebuffer) (font: Font) (col: int) (row: int) (code: byte) =
        Graphics.drawTile fb palette (col * 8) (row * 8) (Font.glyph font code)

    /// Draw the box border (top/middle/bottom rows) using the box-drawing glyphs.
    let private drawBorder (fb: Framebuffer) (font: Font) =
        WindowRenderer.drawBox fb font palette 0 BoxTop ScreenW BoxH

    /// Draw a finished or in-progress text box into the framebuffer.
    let draw (fb: Framebuffer) (font: Font) (state: TextBoxState) =
        drawBorder fb font

        state.Lines
        |> Array.iteri (fun lineIdx line ->
            let screenRow = FirstTextRow + lineIdx * 2

            line
            |> Array.iteri (fun i code -> drawCode fb font (InnerX + i) screenRow code))
