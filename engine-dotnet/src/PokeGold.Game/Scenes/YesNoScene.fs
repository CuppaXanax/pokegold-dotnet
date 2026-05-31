namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render

/// A minimal YES/NO choice menu (GSC `yesorno`). Renders a small bordered box in
/// the top-right corner; Up/Down move the cursor between the two options, A
/// confirms, B cancels (= No). On a decision it reports the result through
/// `onResult` (1 = yes, 0 = no) and pops itself off the stack, so the running
/// script can resume with the choice. A full menu system is M11; this is the one
/// two-option prompt the overworld scripts need.
type YesNoScene(font: Font, onResult: int -> unit) =
    /// Cursor position: true = YES (the default, like GSC), false = NO.
    let mutable yes = true
    let mutable prev = Buttons.none

    // Box geometry in 8-px tiles: a 6×6 box anchored to the top-right.
    [<Literal>]
    let Left = 13

    [<Literal>]
    let Top = 0

    [<Literal>]
    let Width = 6

    [<Literal>]
    let Height = 6

    let palette = TextRenderer.palette

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let pressed cur was = cur && not was
            let up = pressed buttons.Up prev.Up
            let down = pressed buttons.Down prev.Down
            let a = pressed buttons.A prev.A
            let b = pressed buttons.B prev.B
            prev <- buttons

            if up || down then
                yes <- not yes
                Stay
            elif a then
                onResult(if yes then 1 else 0)
                Pop
            elif b then
                onResult 0
                Pop
            else
                Stay

        member _.Render(fb: Framebuffer) =
            let draw col row (code: byte) =
                Graphics.drawTile fb palette (col * 8) (row * 8) (Font.glyph font code)

            let drawStr col row (s: string) =
                Charmap.encode s |> Array.iteri (fun i code -> draw (col + i) row code)

            // Border.
            for ry in 0 .. Height - 1 do
                let row = Top + ry

                let l, m, r =
                    if ry = 0 then Charmap.BoxTopLeft, Charmap.BoxHoriz, Charmap.BoxTopRight
                    elif ry = Height - 1 then Charmap.BoxBottomLeft, Charmap.BoxHoriz, Charmap.BoxBottomRight
                    else Charmap.BoxVert, Charmap.Space, Charmap.BoxVert

                draw Left row l

                for cx in 1 .. Width - 2 do
                    draw (Left + cx) row m

                draw (Left + Width - 1) row r

            // Options and the cursor on the selected row.
            drawStr (Left + 2) (Top + 1) "YES"
            drawStr (Left + 2) (Top + 3) "NO"
            draw (Left + 1) (Top + (if yes then 1 else 3)) 0xEDuy // "▶"
