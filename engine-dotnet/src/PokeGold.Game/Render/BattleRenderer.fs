namespace PokeGold.Game.Render

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Battle

/// Draws the battle screen: the two combatants' name/level/HP panels and, in the
/// bottom box, either a message line or the move menu. Deliberately schematic
/// (no mon portraits yet) but laid out like GSC — enemy panel top-left, player
/// panel lower-right, command box along the bottom.
module BattleRenderer =

    [<Literal>]
    let private ScreenW = 20

    [<Literal>]
    let private BoxTop = 12

    [<Literal>]
    let private BoxH = 6

    // Encode a single character defensively: anything the charmap lacks renders
    // as a space rather than throwing.
    let private codeOf (ch: char) : byte =
        try
            match Charmap.encode (string ch) with
            | [||] -> Charmap.Space
            | arr -> arr.[0]
        with _ ->
            Charmap.Space

    let private drawText (fb: Framebuffer) (font: Font) (col: int) (row: int) (s: string) =
        s
        |> Seq.iteri (fun i ch ->
            Graphics.drawTile fb TextRenderer.palette ((col + i) * 8) (row * 8) (Font.glyph font (codeOf ch)))

    // A horizontal HP bar in pixels: filled green proportional to HP, dark when
    // empty, turning yellow/red as it drains — like the real gauge.
    let private drawHpBar (fb: Framebuffer) (px: int) (py: int) (widthPx: int) (cur: int) (maxHp: int) =
        let cur = System.Math.Clamp(cur, 0, maxHp)
        let frac = if maxHp <= 0 then 0.0 else float cur / float maxHp
        let filled = int (float widthPx * frac + 0.5)

        let r, g, b =
            if frac > 0.5 then 0uy, 200uy, 80uy
            elif frac > 0.2 then 230uy, 200uy, 0uy
            else 230uy, 40uy, 40uy

        for x in 0 .. widthPx - 1 do
            for y in 0 .. 3 do
                if x < filled then fb.SetPixel(px + x, py + y, r, g, b, 255uy)
                else fb.SetPixel(px + x, py + y, 70uy, 70uy, 70uy, 255uy)

    /// Draw the upper battle field: both combatants' panels.
    let drawField (fb: Framebuffer) (font: Font) (state: BattleState) =
        fb.Clear(248uy, 248uy, 248uy, 255uy)

        let e = state.Enemy
        drawText fb font 1 1 e.Species.Name
        drawText fb font 1 2 $"Lv{e.Level}"
        drawHpBar fb (1 * 8) (3 * 8 + 2) 64 e.Hp e.MaxHp

        let p = state.Player
        drawText fb font 10 8 p.Species.Name
        drawText fb font 10 9 $"Lv{p.Level}"
        drawHpBar fb (10 * 8) (10 * 8 + 2) 64 p.Hp p.MaxHp
        drawText fb font 10 11 $"{p.Hp}/{p.MaxHp}"

    // Draw a bordered box across the bottom six rows (the command/message box).
    let private drawBox (fb: Framebuffer) (font: Font) =
        let put col row code =
            Graphics.drawTile fb TextRenderer.palette (col * 8) (row * 8) (Font.glyph font code)

        let last = ScreenW - 1

        for row in 0 .. BoxH - 1 do
            let sr = BoxTop + row

            let left, mid, right =
                if row = 0 then Charmap.BoxTopLeft, Charmap.BoxHoriz, Charmap.BoxTopRight
                elif row = BoxH - 1 then Charmap.BoxBottomLeft, Charmap.BoxHoriz, Charmap.BoxBottomRight
                else Charmap.BoxVert, Charmap.Space, Charmap.BoxVert

            put 0 sr left
            for col in 1 .. last - 1 do put col sr mid
            put last sr right

    /// Draw the move-selection menu in the bottom box with a cursor and PP display.
    let drawMenu (fb: Framebuffer) (font: Font) (moves: MoveData list) (pp: int list) (cursor: int) =
        drawBox fb font

        moves
        |> List.iteri (fun i m ->
            let row = 13 + i
            // Cursor: a small filled triangle-ish block to the move's left.
            if i = cursor then
                for y in 0..6 do
                    for x in 0..4 do
                        if x <= y && x <= 6 - y then
                            fb.SetPixel(2 * 8 + x, row * 8 + 1 + y, 0uy, 0uy, 0uy, 255uy)

            drawText fb font 3 row m.Name)

        // PP display for the currently selected move (right side of box).
        if cursor >= 0 && cursor < moves.Length && cursor < pp.Length then
            let curPp = pp.[cursor]
            let maxPp = moves.[cursor].Pp
            let ppStr = $"PP {curPp}/{maxPp}"
            drawText fb font (ScreenW - 1 - ppStr.Length) (13 + moves.Length) ppStr

    /// Draw a single message line in the bottom box.
    let drawMessage (fb: Framebuffer) (font: Font) (line: string) =
        drawBox fb font
        drawText fb font 1 14 line
