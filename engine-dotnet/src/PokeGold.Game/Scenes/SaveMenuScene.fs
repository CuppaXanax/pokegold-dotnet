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

    // GSC shows the prompt in the bottom speech textbox ("Would you like to /
    // save the game?") with a separate YES/NO box in the top-right corner. Box
    // geometry in 8-px tiles; screen is 20 × 18 tiles.

    // Bottom speech textbox: cols 0–19, rows 12–17 (matches TextRenderer).
    [<Literal>]
    let PromptLeft = 0

    [<Literal>]
    let PromptTop = 12

    [<Literal>]
    let PromptWidth = 20

    [<Literal>]
    let PromptHeight = 6

    // YES/NO box in the top-right corner (matches YesNoScene).
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
                // Prompt lives in the bottom speech textbox, two lines, exactly
                // like GSC's _WouldYouLikeToSaveTheGameText (no overflow).
                WindowRenderer.drawBox fb content.Font palette PromptLeft PromptTop PromptWidth PromptHeight
                WindowRenderer.drawString fb content.Font palette (PromptLeft + 1) (PromptTop + 2) "Would you like to"
                WindowRenderer.drawString fb content.Font palette (PromptLeft + 1) (PromptTop + 4) "save the game?"

                // YES/NO choice box in the top-right corner.
                WindowRenderer.drawBox fb content.Font palette Left Top Width Height
                WindowRenderer.drawString fb content.Font palette (Left + 2) (Top + 1) "YES"
                WindowRenderer.drawString fb content.Font palette (Left + 2) (Top + 3) "NO"
                let cursorRow = if yes then Top + 1 else Top + 3
                WindowRenderer.drawCursor fb content.Font palette (Left + 1) cursorRow
            | WaitingToPop ->
                ()
