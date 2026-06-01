namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Player
open PokeGold.Game.Ui

/// Internal state-machine modes for PCBoxScene.
type PCBoxMode =
    | Browsing
    | ActionMenu of actions: string array * menu: MenuList * monIndex: int
    | DepositWait
    | ReleaseWait  of monIndex: int
    | ShowMsg      of msg: string

/// Bill's PC box-storage scene. Shows the contents of the current box
/// (≤20 Pokémon) as a scrollable list with a cursor. Actions available:
///   • Up/Down   — navigate entries
///   • Left/Right — cycle among the 14 boxes (wrapping)
///   • A on a mon — action submenu: WITHDRAW / STATS / RELEASE / CANCEL
///   • A on DEPOSIT — pick a party mon to store in this box (via PartyScene picker)
///   • A on CHANGE BOX — same as pressing Right
///   • A on CANCEL / B — exit back to the caller
/// Rejection messages (last mon / party full / box full) are shown inline.
/// All state mutations go through `onChange` so the caller (OverworldScene)
/// can persist back to SaveData.
///
///   content   — loaded content (font)
///   player    — player state when the scene opens
///   onChange  — called with the updated PlayerState on every mutation
type PCBoxScene(content: Content, player: PlayerState, onChange: PlayerState -> unit) =

    let palette = TextRenderer.palette

    // ── Layout constants (tile units; screen = 20 × 18) ──────────────────────

    // Main list window: full width, rows 0-15 (height 16, interior rows 1-14).
    [<Literal>]
    let ListLeft   = 0
    [<Literal>]
    let ListTop    = 0
    [<Literal>]
    let ListWidth  = 20
    [<Literal>]
    let ListHeight = 16

    // Interior: row 1 = header, rows 2-14 = scrollable list (13 visible entries).
    [<Literal>]
    let HeaderRow     = 1
    [<Literal>]
    let ListStartRow  = 2
    [<Literal>]
    let VisibleEntries = 12

    // Info bar at rows 16-17: no border, just plain text lines.
    [<Literal>]
    let InfoRow1 = 16
    [<Literal>]
    let InfoRow2 = 17

    // Action submenu overlay: top-right, cols 11-19 (width 9), from row 0.
    [<Literal>]
    let ActionLeft  = 11
    [<Literal>]
    let ActionTop   = 0
    [<Literal>]
    let ActionWidth = 9

    // ── Mutable state ────────────────────────────────────────────────────────

    let mutable currentPlayer   = player
    let mutable mode            = Browsing : PCBoxMode
    let mutable depositPartyIdx = -1    // set by PartyScene picker callback
    let mutable yesNoResult     = 0     // set by YesNoScene callback
    let input = EdgeDetector()

    // ── Pure helpers ─────────────────────────────────────────────────────────

    let truncate (n: int) (s: string) =
        if s.Length <= n then s else s.[..n-1]

    /// Current active box.
    let currentBox () = currentPlayer.Pc.Boxes.[currentPlayer.Pc.CurrentBox]

    /// Total menu entries for the current box content: mons + DEPOSIT + CHANGE BOX + CANCEL.
    let entryCount () = (currentBox ()).Mons.Length + 3

    let depositIdx   () = (currentBox ()).Mons.Length
    let changeBoxIdx () = (currentBox ()).Mons.Length + 1
    let cancelIdx    () = (currentBox ()).Mons.Length + 2

    /// Rebuild a MenuList for the current box, clamping the cursor to valid range.
    let makeMenu (hint: int) : MenuList =
        let n = max 3 (entryCount ())
        MenuList.create n VisibleEntries false |> MenuList.moveTo (min hint (n - 1))

    let mutable menu = makeMenu 0

    /// Label for each menu entry (parallel to menu indices).
    let entryLabels () : string array =
        let box = currentBox ()
        [| for mon in box.Mons do
               yield sprintf "%-10s Lv.%-3d" (truncate 10 mon.Nickname) mon.Level
           yield "DEPOSIT"
           yield "CHANGE BOX"
           yield "CANCEL" |]

    // ── Rendering helpers ────────────────────────────────────────────────────

    let renderListWindow (fb: Framebuffer) =
        WindowRenderer.drawBox fb content.Font palette ListLeft ListTop ListWidth ListHeight

    let renderHeader (fb: Framebuffer) =
        let pc       = currentPlayer.Pc
        let boxNum   = pc.CurrentBox + 1
        let boxName  = truncate 8 pc.Boxes.[pc.CurrentBox].Name
        let partyLen = currentPlayer.Party.Length
        WindowRenderer.drawString fb content.Font palette (ListLeft + 1) HeaderRow
            (sprintf "BOX %-2d %-8s  P:%d/6" boxNum boxName partyLen)

    let renderEntries (fb: Framebuffer) =
        let labels = entryLabels ()
        let vis    = min menu.Visible (menu.Count - menu.Top)
        let slice  = labels |> Array.skip menu.Top |> Array.truncate vis
        WindowRenderer.drawList
            fb content.Font palette
            (ListLeft + 1) ListStartRow
            (slice |> Array.toSeq)
            (menu.Cursor - menu.Top)

    // ── Public API (test surface) ────────────────────────────────────────────

    /// Current scene mode.
    member _.Mode = mode

    /// Most recent PlayerState (reflects all PC mutations).
    member _.CurrentPlayer = currentPlayer

    // ── Cycle to next/previous box ───────────────────────────────────────────

    member private _.ChangeBox(delta: int) =
        let numBoxes = Storage.numBoxes
        let newIdx   = (currentPlayer.Pc.CurrentBox + delta + numBoxes) % numBoxes
        currentPlayer <- BoxOps.switchBox newIdx currentPlayer
        onChange currentPlayer
        menu <- makeMenu 0

    // ── Scene interface ──────────────────────────────────────────────────────

    interface Scene with
        member this.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)

            match mode with

            // ── Browsing: navigate box entries ────────────────────────────
            | Browsing ->
                if edges.Up then
                    menu <- MenuList.moveUp menu
                    Stay
                elif edges.Down then
                    menu <- MenuList.moveDown menu
                    Stay
                elif edges.Left then
                    this.ChangeBox(-1)
                    Stay
                elif edges.Right then
                    this.ChangeBox(+1)
                    Stay
                elif edges.A then
                    let cur = menu.Cursor
                    if cur = cancelIdx () then
                        Pop
                    elif cur = changeBoxIdx () then
                        this.ChangeBox(+1)
                        Stay
                    elif cur = depositIdx () then
                        if currentPlayer.Party.IsEmpty then
                            mode <- ShowMsg "Your party is empty!"
                            Stay
                        else
                            depositPartyIdx <- -1
                            mode <- DepositWait
                            Push(PartyScene(content, currentPlayer, (fun _ -> ()), fun idx ->
                                depositPartyIdx <- idx
                                Pop) :> Scene)
                    else
                        // A on a box mon — open action submenu
                        let box  = currentBox ()
                        let mon  = List.item cur box.Mons
                        let actions =
                            [| "WITHDRAW"; "STATS"; "RELEASE"; "CANCEL" |]
                        let am = MenuList.create actions.Length actions.Length true
                        mode <- ActionMenu(actions, am, cur)
                        Stay
                elif edges.B then
                    Pop
                else
                    Stay

            // ── ActionMenu: WITHDRAW / STATS / RELEASE / CANCEL ──────────
            | ActionMenu(actions, am, monIdx) ->
                let am' =
                    if   edges.Up   then MenuList.moveUp   am
                    elif edges.Down then MenuList.moveDown am
                    else am
                mode <- ActionMenu(actions, am', monIdx)

                if edges.A then
                    let chosen = actions.[am'.Cursor]
                    match chosen with

                    | "CANCEL" ->
                        mode <- Browsing
                        Stay

                    | "WITHDRAW" ->
                        let boxIdx = currentPlayer.Pc.CurrentBox
                        match BoxOps.withdraw boxIdx monIdx currentPlayer with
                        | Ok p ->
                            currentPlayer <- p
                            onChange p
                            menu <- makeMenu (min menu.Cursor (cancelIdx () - 1))
                            mode <- ShowMsg "Withdrew POKeMON!"
                        | Error e ->
                            mode <- ShowMsg e
                        Stay

                    | "STATS" ->
                        mode <- Browsing
                        let mon = List.item monIdx (currentBox ()).Mons
                        Push(SummaryScene(content, mon) :> Scene)

                    | "RELEASE" ->
                        mode <- ReleaseWait monIdx
                        Push(YesNoScene(content.Font, fun r -> yesNoResult <- r) :> Scene)

                    | _ ->
                        mode <- Browsing
                        Stay

                elif edges.B then
                    mode <- Browsing
                    Stay
                else
                    Stay

            // ── DepositWait: PartyScene just popped; process result ───────
            | DepositWait ->
                let idx = depositPartyIdx
                depositPartyIdx <- -1
                if idx < 0 then
                    // Cancelled (B in PartyScene).
                    mode <- Browsing
                else
                    let nick =
                        if idx < currentPlayer.Party.Length
                        then (List.item idx currentPlayer.Party).Nickname
                        else "?"
                    match BoxOps.deposit idx currentPlayer with
                    | Ok p ->
                        currentPlayer <- p
                        onChange p
                        menu <- makeMenu 0
                        mode <- ShowMsg(sprintf "%s deposited!" (truncate 10 nick))
                    | Error e ->
                        mode <- ShowMsg e
                Stay

            // ── ReleaseWait: YesNoScene just popped; process result ───────
            | ReleaseWait monIdx ->
                let r = yesNoResult
                yesNoResult <- 0
                if r = 1 then
                    let boxIdx = currentPlayer.Pc.CurrentBox
                    let p      = BoxOps.release boxIdx monIdx currentPlayer
                    currentPlayer <- p
                    onChange p
                    menu <- makeMenu (min menu.Cursor (cancelIdx () - 1))
                    mode <- ShowMsg "POKeMON released!"
                else
                    mode <- Browsing
                Stay

            // ── ShowMsg: press A or B to return to Browsing ──────────────
            | ShowMsg _ ->
                if edges.A || edges.B then
                    mode <- Browsing
                Stay

        member _.Render(fb: Framebuffer) =
            renderListWindow fb
            renderHeader fb
            renderEntries fb

            // Info bar (no border — two plain text lines).
            match mode with
            | ShowMsg msg ->
                WindowRenderer.drawString fb content.Font palette (ListLeft + 1) InfoRow1
                    (truncate 18 msg)
                WindowRenderer.drawString fb content.Font palette (ListLeft + 1) InfoRow2
                    "Press A or B"
            | Browsing ->
                WindowRenderer.drawString fb content.Font palette (ListLeft + 1) InfoRow1
                    "L/R:Change Box"
                WindowRenderer.drawString fb content.Font palette (ListLeft + 1) InfoRow2
                    "A:Select  B:Exit"
            | _ -> ()

            // Action submenu overlay.
            match mode with
            | ActionMenu(actions, am, _) ->
                let h = 2 + actions.Length
                WindowRenderer.drawBox fb content.Font palette ActionLeft ActionTop ActionWidth h
                WindowRenderer.drawList
                    fb content.Font palette
                    (ActionLeft + 1) (ActionTop + 1)
                    (actions |> Array.toSeq)
                    am.Cursor
            | _ -> ()
