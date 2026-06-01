namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Player
open PokeGold.Game.Ui

/// Internal state-machine modes for PlayerPCScene.
/// Case names are prefixed Pc* / PcMail* to avoid FS0041 ambiguity
/// with the existing PCBoxMode, MartMode, and PartyMode DU cases.
type PlayerPCMode =
    | PcItemMain
    | PcItemPick   of action: string * pickMenu: MenuList
    | PcItemMsg    of msg: string
    | PcMailBrowse of mailMenu: MenuList
    | PcMailRead   of index: int

/// The Player's PC scene — dispatcher for item storage and the mailbox.
/// Entries: WITHDRAW ITEM, DEPOSIT ITEM, TOSS ITEM, MAIL BOX, LOG OFF.
///
/// Item flows (1 at a time, inline result message; no sub-scene push):
///   WITHDRAW ITEM — pick from PC stash → move to bag
///   DEPOSIT ITEM  — pick from bag (Items/Balls/TmHm; not KeyItems — GSC rule) → move to stash
///   TOSS ITEM     — pick from PC stash → remove (silently clamped)
///   MAIL BOX      — browse mailbox; A on an entry reads it
///   LOG OFF / B   → Pop
///
///   content   — loaded content (font)
///   player    — player state when the scene opens
///   onChange  — called with the updated PlayerState on every mutation
type PlayerPCScene(content: Content, player: PlayerState, onChange: PlayerState -> unit) =

    let palette = TextRenderer.palette

    // ── Layout constants (tile units; screen = 20 × 18) ──────────────────────

    [<Literal>]
    let ListLeft   = 0
    [<Literal>]
    let ListTop    = 0
    [<Literal>]
    let ListWidth  = 20
    [<Literal>]
    let ListHeight = 14

    // Interior rows 1-12; list starts at row 1 (no separate header row needed).
    [<Literal>]
    let ListStartRow   = 1
    [<Literal>]
    let VisibleEntries = 11

    // Info bar below the box (plain text, no additional border).
    [<Literal>]
    let InfoRow1 = 14
    [<Literal>]
    let InfoRow2 = 15

    // ── Top-level menu entries ────────────────────────────────────────────────

    let mainLabels = [| "WITHDRAW ITEM"; "DEPOSIT ITEM"; "TOSS ITEM"; "MAIL BOX"; "LOG OFF" |]
    let logOffIdx  = mainLabels.Length - 1

    // ── Mutable state ─────────────────────────────────────────────────────────

    let mutable currentPlayer = player
    let mutable mode          = PcItemMain : PlayerPCMode
    let mutable mainMenu      = MenuList.create mainLabels.Length mainLabels.Length true
    let input                 = EdgeDetector()

    // ── Pure helpers ──────────────────────────────────────────────────────────

    let truncate (n: int) (s: string) =
        if s.Length <= n then s else s.[..n-1]

    /// Items the player can deposit: Items + Balls + TmHm (NOT KeyItems — GSC rule).
    let depositableItems () : (string * int) list =
        currentPlayer.Bag.Items @ currentPlayer.Bag.Balls @ currentPlayer.Bag.TmHm

    /// Source item list for a given action.
    let sourceItems (action: string) : (string * int) list =
        match action with
        | "DEPOSIT" -> depositableItems ()
        | _         -> currentPlayer.Pc.PcItems

    let makePickMenu (count: int) (hint: int) : MenuList =
        MenuList.create (max 1 count) VisibleEntries false
        |> MenuList.moveTo (min hint (max 0 (count - 1)))

    // ── Rendering helpers ─────────────────────────────────────────────────────

    let renderListWindow (fb: Framebuffer) =
        WindowRenderer.drawBox fb content.Font palette ListLeft ListTop ListWidth ListHeight

    let renderMainMenu (fb: Framebuffer) =
        WindowRenderer.drawList
            fb content.Font palette
            (ListLeft + 1) ListStartRow
            (mainLabels |> Array.toSeq)
            mainMenu.Cursor

    let renderPickList (fb: Framebuffer) (items: (string * int) list) (pm: MenuList) =
        let vis   = min pm.Visible (pm.Count - pm.Top)
        let slice = items |> List.skip pm.Top |> List.truncate vis
        let labels = slice |> List.map (fun (id, q) -> sprintf "%-12s %3d" (truncate 12 id) q)
        WindowRenderer.drawList
            fb content.Font palette
            (ListLeft + 1) ListStartRow
            (labels |> List.toSeq)
            (pm.Cursor - pm.Top)

    let renderMailList (fb: Framebuffer) (mm: MenuList) =
        let mailbox = currentPlayer.Pc.Mailbox
        if not mailbox.IsEmpty then
            let vis    = min mm.Visible (mm.Count - mm.Top)
            let slice  = mailbox |> List.skip mm.Top |> List.truncate vis
            let labels = slice |> List.mapi (fun i m ->
                sprintf "%d %-14s" (mm.Top + i + 1) (truncate 14 m.Author))
            WindowRenderer.drawList
                fb content.Font palette
                (ListLeft + 1) ListStartRow
                (labels |> List.toSeq)
                (mm.Cursor - mm.Top)

    // ── Public API (test surface) ─────────────────────────────────────────────

    member _.Mode          = mode
    member _.CurrentPlayer = currentPlayer

    // ── Scene interface ───────────────────────────────────────────────────────

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)

            match mode with

            // ── Main top menu ─────────────────────────────────────────────
            | PcItemMain ->
                mainMenu <-
                    if   edges.Up   then MenuList.moveUp   mainMenu
                    elif edges.Down then MenuList.moveDown mainMenu
                    else mainMenu

                if edges.A then
                    let cur = mainMenu.Cursor
                    if cur = logOffIdx then
                        Pop
                    elif cur = 3 then
                        // MAIL BOX
                        let mail = currentPlayer.Pc.Mailbox
                        if mail.IsEmpty then
                            mode <- PcItemMsg "No mail."
                        else
                            mode <- PcMailBrowse(MenuList.create mail.Length VisibleEntries false)
                        Stay
                    else
                        let action =
                            match cur with
                            | 0 -> "WITHDRAW"
                            | 1 -> "DEPOSIT"
                            | _ -> "TOSS"
                        let items = sourceItems action
                        if items.IsEmpty then
                            mode <- PcItemMsg "No items."
                        else
                            mode <- PcItemPick(action, makePickMenu items.Length 0)
                        Stay
                elif edges.B then
                    Pop
                else
                    Stay

            // ── Item picker ───────────────────────────────────────────────
            | PcItemPick(action, pm) ->
                let pm' =
                    if   edges.Up   then MenuList.moveUp   pm
                    elif edges.Down then MenuList.moveDown pm
                    else pm
                mode <- PcItemPick(action, pm')

                if edges.A then
                    let items = sourceItems action
                    if pm'.Cursor >= items.Length then
                        mode <- PcItemMsg "No items."
                    else
                        let (itemId, _) = List.item pm'.Cursor items
                        let result =
                            match action with
                            | "WITHDRAW" -> PcItemOps.withdrawItem itemId 1 currentPlayer
                            | "DEPOSIT"  -> PcItemOps.depositItem  itemId 1 currentPlayer
                            | _          -> Ok (PcItemOps.tossItem  itemId 1 currentPlayer)
                        match result with
                        | Ok p ->
                            currentPlayer <- p
                            onChange p
                            let msg =
                                match action with
                                | "WITHDRAW" -> sprintf "Got %s!"       (truncate 12 itemId)
                                | "DEPOSIT"  -> sprintf "Put away %s!"  (truncate 12 itemId)
                                | _          -> sprintf "Tossed %s."    (truncate 12 itemId)
                            mode <- PcItemMsg msg
                        | Error e ->
                            mode <- PcItemMsg e
                    Stay
                elif edges.B then
                    mode <- PcItemMain
                    Stay
                else
                    Stay

            // ── Result message ────────────────────────────────────────────
            | PcItemMsg _ ->
                if edges.A || edges.B then
                    mode <- PcItemMain
                Stay

            // ── Mailbox browse ────────────────────────────────────────────
            | PcMailBrowse mm ->
                let mm' =
                    if   edges.Up   then MenuList.moveUp   mm
                    elif edges.Down then MenuList.moveDown mm
                    else mm
                mode <- PcMailBrowse mm'

                if edges.A then
                    let idx = mm'.Cursor
                    if idx < currentPlayer.Pc.Mailbox.Length then
                        mode <- PcMailRead idx
                    Stay
                elif edges.B then
                    mode <- PcItemMain
                    Stay
                else
                    Stay

            // ── Mail read ─────────────────────────────────────────────────
            | PcMailRead _ ->
                if edges.A || edges.B then
                    let mail = currentPlayer.Pc.Mailbox
                    let mm   = MenuList.create (max 1 mail.Length) VisibleEntries false
                    mode <- PcMailBrowse mm
                Stay

        member _.Render(fb: Framebuffer) =
            renderListWindow fb

            match mode with
            | PcItemMain ->
                renderMainMenu fb
                WindowRenderer.drawString fb content.Font palette (ListLeft + 1) InfoRow1
                    "A:Select  B:Exit"

            | PcItemPick(action, pm) ->
                let items = sourceItems action
                renderPickList fb items pm
                WindowRenderer.drawString fb content.Font palette (ListLeft + 1) InfoRow1
                    (sprintf "%s: A=pick B=back" (truncate 15 action))

            | PcItemMsg msg ->
                renderMainMenu fb
                WindowRenderer.drawString fb content.Font palette (ListLeft + 1) InfoRow1
                    (truncate 18 msg)
                WindowRenderer.drawString fb content.Font palette (ListLeft + 1) InfoRow2
                    "Press A or B"

            | PcMailBrowse mm ->
                renderMailList fb mm
                WindowRenderer.drawString fb content.Font palette (ListLeft + 1) InfoRow1
                    "A:Read  B:Exit"

            | PcMailRead idx ->
                let mail = currentPlayer.Pc.Mailbox
                if idx < mail.Length then
                    let m = mail.[idx]
                    WindowRenderer.drawString fb content.Font palette (ListLeft + 1) (ListStartRow + 1)
                        (sprintf "From: %s" (truncate 12 m.Author))
                    WindowRenderer.drawString fb content.Font palette (ListLeft + 1) (ListStartRow + 2)
                        (truncate 18 m.Body)
                WindowRenderer.drawString fb content.Font palette (ListLeft + 1) InfoRow1
                    "Press A or B"
