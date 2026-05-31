namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// A minimal YES/NO choice menu (GSC `yesorno`). Renders a small bordered box in
/// the top-right corner; Up/Down move the cursor between the two options, A
/// confirms, B cancels (= No). On a decision it reports the result through
/// `onResult` (1 = yes, 0 = no) and pops itself off the stack, so the running
/// script can resume with the choice. A full menu system is M11; this is the one
/// two-option prompt the overworld scripts need.
type YesNoScene(font: Font, onResult: int -> unit) =
    /// Cursor position: true = YES (the default, like GSC), false = NO.
    let mutable yes = true
    let input = EdgeDetector()

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
            let edges = input.Update(buttons)

            if edges.Up || edges.Down then
                yes <- not yes
                Stay
            elif edges.A then
                onResult(if yes then 1 else 0)
                Pop
            elif edges.B then
                onResult 0
                Pop
            else
                Stay

        member _.Render(fb: Framebuffer) =
            let drawCode col row (code: byte) =
                Graphics.drawTile fb palette (col * 8) (row * 8) (Font.glyph font code)

            WindowRenderer.drawBox fb font palette Left Top Width Height

            // Options and the cursor on the selected row.
            WindowRenderer.drawString fb font palette (Left + 2) (Top + 1) "YES"
            WindowRenderer.drawString fb font palette (Left + 2) (Top + 3) "NO"
            drawCode (Left + 1) (Top + (if yes then 1 else 3)) 0xEDuy // "▶"
