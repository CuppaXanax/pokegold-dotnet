namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// The six entries in the GSC start menu.
/// (POKéGEAR is deferred to M20; STATUS/QUIT are out of scope.)
type StartEntry =
    | Pokedex
    | Pokemon
    | Pack
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
type StartMenuScene(content: Content, openEntry: StartEntry -> Transition) =

    // GSC order: POKéDEX, POKéMON, PACK, SAVE, OPTION, EXIT.
    let entryLabels = [| "POKéDEX"; "POKéMON"; "PACK"; "SAVE"; "OPTION"; "EXIT" |]
    let entryDUs    = [| Pokedex;   Pokemon;   Pack;  Save;  Option;  Exit   |]

    // All 6 entries always fit in the visible window — no scrolling needed.
    let mutable menu = MenuList.create entryLabels.Length entryLabels.Length true
    let input = EdgeDetector()
    let palette = TextRenderer.palette

    // Box geometry in 8-px tiles.
    // Screen is 20×18 tiles. The box is flush against the right edge.
    // Longest entries ("POKéDEX" / "POKéMON") are 7 chars rendered via charmap;
    // the cursor glyph (▶) needs 1 more column, so interior is 8 tiles wide.
    // Box: 2 border + 8 interior = 10 wide. Left = 20 - 10 = 10.
    // Height: 2 border + 6 entries (1 row each) = 8.
    [<Literal>]
    let Left = 10

    [<Literal>]
    let Top = 0

    [<Literal>]
    let Width = 10

    [<Literal>]
    let Height = 8

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

            // All 6 entries are always visible (visible = count = 6, Top = 0).
            // drawList places the ▶ cursor at column Left+1 and text at Left+2.
            let visibleItems =
                entryLabels.[menu.Top .. menu.Top + menu.Visible - 1] |> Array.toSeq
            WindowRenderer.drawList fb content.Font palette (Left + 1) (Top + 1) visibleItems (menu.Cursor - menu.Top)
