namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// The seven entries in the GSC start menu.
/// (STATUS/QUIT are out of scope.)
type StartEntry =
    | Pokedex
    | Pokemon
    | Pack
    | Pokegear
    | Save
    | Option
    | Exit

/// The start menu layered over the overworld. Renders a bordered list box
/// anchored to the top-right of the screen; the overworld stays visible
/// beneath (the Game stack renders bottom-to-top and this scene draws only
/// its box in the right-side columns).
///
/// `openEntry` maps a menu selection to the stack transition to perform when A
/// is pressed; returning `Push child` pushes the child over this scene so it
/// can later pop back to the menu. `Exit` (and B/Start) always pop the menu
/// without consulting `openEntry`.
type StartMenuScene(content: Content, openEntry: StartEntry -> Transition, ?heldAtOpen: Buttons) =

    // GSC order: POKéDEX, POKéMON, PACK, POKéGEAR, SAVE, OPTION, EXIT.
    let entryLabels = [| "POKéDEX"; "POKéMON"; "PACK"; "POKéGEAR"; "SAVE"; "OPTION"; "EXIT" |]
    let entryDUs    = [| Pokedex;   Pokemon;   Pack;  Pokegear;   Save;  Option;  Exit   |]

    // All 7 entries always fit in the visible window — no scrolling needed.
    let mutable menu = MenuList.create entryLabels.Length entryLabels.Length true
    // Seed the edge detector with the buttons that were held when the menu opened
    // (Start, typically) so that press doesn't immediately re-fire and close it.
    let input = EdgeDetector(defaultArg heldAtOpen Buttons.none)
    let palette = TextRenderer.palette

    // Box geometry in 8-px tiles, mirroring the GSC start menu's
    // `menu_coords 10, 0, SCREEN_WIDTH - 1, SCREEN_HEIGHT - 1`: flush against the
    // right edge (cols 10-19) and the full 18-tile screen height (rows 0-17).
    // Entries are placed every 2 rows (GSC 1-D menu spacing) starting at the
    // first interior row below the top border.
    [<Literal>]
    let Left = 10

    [<Literal>]
    let Top = 0

    [<Literal>]
    let Width = 10

    [<Literal>]
    let Height = 18

    // First entry's row (one blank row under the top border) and the per-entry
    // row stride (GSC spaces menu items two tiles apart).
    [<Literal>]
    let FirstRow = 2

    [<Literal>]
    let RowStep = 2

    /// Current cursor position (0-based entry index). Exposed for unit tests.
    member _.Cursor = menu.Cursor

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)

            if edges.Up then
                menu <- MenuList.moveUp menu
                Stay
            elif edges.Down then
                menu <- MenuList.moveDown menu
                Stay
            elif edges.A then
                let entry = entryDUs.[menu.Cursor]
                match entry with
                | Exit -> Pop
                | _    -> openEntry entry
            elif edges.B || edges.Start then
                Pop
            else
                Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawBox fb content.Font palette Left Top Width Height

            // Entries are spaced two tiles apart (GSC 1-D menu spacing). The ▶
            // cursor sits at column Left+1; the label starts at Left+2.
            entryLabels
            |> Array.iteri (fun i label ->
                let row = Top + FirstRow + i * RowStep
                if i = menu.Cursor then
                    WindowRenderer.drawCursor fb content.Font palette (Left + 1) row
                WindowRenderer.drawString fb content.Font palette (Left + 2) row label)
