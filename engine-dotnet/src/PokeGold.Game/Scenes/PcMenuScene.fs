namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Player
open PokeGold.Game.Ui

/// The Pokémon Center PC dispatcher menu — the scene that appears when the
/// player interacts with the PC in a Pokémon Center. Offers:
///   BILL'S PC    → push PCBoxScene (Pokémon storage)
///   PLAYER'S PC  → stub for M12.4 (PC item storage / mailbox)
///   LOG OFF      → Pop (back to overworld)
///   B            → Pop
///
/// M12.4 can extend this by adding `PLAYER'S PC` to the labels array; the
/// index-based dispatch below (0=Bill's, 1=Player's, last=LOG OFF) makes the
/// addition straightforward with no structural changes.
///
///   content   — loaded content (font)
///   player    — player state when the PC is opened
///   onChange  — called with the updated PlayerState on any mutation
type PcMenuScene(content: Content, player: PlayerState, onChange: PlayerState -> unit) =

    let palette = TextRenderer.palette

    let mutable currentPlayer = player
    let input  = EdgeDetector()

    // Menu entries — M12.4 inserts "PLAYER'S PC" at index 1 when ready.
    let labels = [| "BILL'S PC"; "PLAYER'S PC"; "LOG OFF" |]
    let mutable menu = MenuList.create labels.Length labels.Length true

    // ── Screen geometry ──────────────────────────────────────────────────────

    [<Literal>]
    let BoxLeft  = 0
    [<Literal>]
    let BoxTop   = 0
    [<Literal>]
    let BoxWidth = 16

    // ── Public API (test surface) ────────────────────────────────────────────

    member _.Cursor = menu.Cursor

    member _.CurrentPlayer = currentPlayer

    // ── Scene interface ──────────────────────────────────────────────────────

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)

            menu <-
                if   edges.Up   then MenuList.moveUp   menu
                elif edges.Down then MenuList.moveDown menu
                else menu

            if edges.A then
                match menu.Cursor with
                | 0 ->
                    // BILL'S PC → push box storage scene.
                    Push(PCBoxScene(content, currentPlayer, fun p -> currentPlayer <- p; onChange p) :> Scene)
                | 1 ->
                    // PLAYER'S PC — stub; M12.4 wires PC item storage / mailbox.
                    Push(TextBoxScene.Of(content, "PLAYER'S PC — coming in M12.4!<DONE>") :> Scene)
                | _ ->
                    Pop  // LOG OFF
            elif edges.B then
                Pop
            else
                Stay

        member _.Render(fb: Framebuffer) =
            let h = 2 + labels.Length
            WindowRenderer.drawBox fb content.Font palette BoxLeft BoxTop BoxWidth h
            WindowRenderer.drawList
                fb content.Font palette
                (BoxLeft + 1) (BoxTop + 1)
                (labels |> Array.toSeq)
                menu.Cursor
