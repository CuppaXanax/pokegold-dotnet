namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// Internal state of the save confirmation flow.
type private SavePhase =
    /// Showing the inline YES/NO prompt; waiting for player input.
    | Confirming
    /// The "saved" text box was pushed; pop ourselves once it dismisses.
    | WaitingToPop

/// GSC-style save confirmation scene.
///
/// Flow:
///   1. Show an inline YES/NO box ("SAVE the game?") in the top-right corner.
///   2. YES  → call `onSave()`, push a "<name> saved the game!" text box, then
///             pop ourselves when the box is dismissed (returns to overworld).
///   3. NO/B → pop immediately, writing nothing.
///
/// `onSave` is provided by the caller (typically OverworldScene) and performs the
/// actual capture + write, keeping OverworldScene the single owner of that seam.
type SaveMenuScene(content: Content, playerName: string, onSave: unit -> unit) =

    let mutable phase = Confirming
    /// true = YES cursor (default, matching GSC), false = NO.
    let mutable yes = true
    let input = EdgeDetector()

    // Box geometry in 8-px tiles; screen is 20 × 18 tiles.
    // Placed in the top-right corner (same anchor as YesNoScene) but 3 tiles wider
    // to accommodate the "SAVE the game?" prompt line.
    [<Literal>]
    let Left = 9

    [<Literal>]
    let Top = 0

    [<Literal>]
    let Width = 11

    [<Literal>]
    let Height = 6

    let palette = TextRenderer.palette

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            match phase with
            | WaitingToPop ->
                // The text box that followed onSave() has just been dismissed.
                // Pop ourselves too so control returns to the overworld.
                Pop
            | Confirming ->
                let edges = input.Update(buttons)

                if edges.Up || edges.Down then
                    yes <- not yes
                    Stay
                elif edges.A then
                    if yes then
                        onSave ()
                        phase <- WaitingToPop
                        let msg = playerName + " saved<LINE>the game!<DONE>"
                        Push(TextBoxScene.Of(content, msg) :> Scene)
                    else
                        Pop
                elif edges.B then
                    Pop
                else
                    Stay

        member _.Render(fb: Framebuffer) =
            match phase with
            | Confirming ->
                WindowRenderer.drawBox fb content.Font palette Left Top Width Height
                WindowRenderer.drawString fb content.Font palette (Left + 1) (Top + 1) "SAVE the game?"
                WindowRenderer.drawString fb content.Font palette (Left + 2) (Top + 3) "YES"
                WindowRenderer.drawString fb content.Font palette (Left + 2) (Top + 4) "NO"
                let cursorRow = if yes then Top + 3 else Top + 4
                WindowRenderer.drawCursor fb content.Font palette (Left + 1) cursorRow
            | WaitingToPop ->
                ()
