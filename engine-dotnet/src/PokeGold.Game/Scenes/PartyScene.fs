namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Player
open PokeGold.Game.Ui

/// Internal sub-mode for PartyScene.
type PartyMode =
    | Browsing
    | ActionMenu of menu: MenuList * actions: string array * slotIdx: int
    | SwitchPick of firstSlot: int
    | ItemMsg    of msg: string

/// The party Pokémon menu — lists up to 6 party mons, each showing nickname,
/// level, current/max HP and a 6-tile text HP bar. Up/Down move the ▶ cursor;
/// A opens the action submenu; B pops back to the caller.
///
/// Default (menu) mode: A → action submenu (STATS / SWITCH / ITEM / field-moves / CANCEL).
/// - STATS   → push a summary placeholder (M11.5: replace with SummaryScene).
/// - SWITCH  → enter a "pick second slot" sub-mode; confirming swaps the two
///             party entries and invokes `onChange` so the reorder persists.
/// - ITEM    → take a held item back into the bag (clear HeldItem); or show a
///             brief "nothing held" notice. Giving an item is driven from Pack
///             (M11.3 Pack use/give can push this in picker mode).
/// - Field moves detected in the mon's move set dispatch to an M17 gate stub
///   that shows "Can't use that here yet." (no crash).
/// - CANCEL / B closes the submenu.
///
/// Optional picker mode: when `onSelect` is supplied, pressing A on a slot
/// calls `onSelect slotIndex` directly instead of opening the action submenu.
/// M11.3 Pack use/give can push this in picker mode to choose a party target.
///
///   content   — loaded content (font)
///   player    — initial PlayerState to display
///   onChange  — callback invoked with the updated PlayerState on any mutation
///   onSelect  — optional picker-mode seam (M11.3+): replaces action submenu
type PartyScene(content: Content, player: PlayerState, onChange: PlayerState -> unit, ?onSelect: int -> Transition) =

    let mutable currentPlayer = player
    let mutable mode          = Browsing : PartyMode
    let input   = EdgeDetector()
    let palette = TextRenderer.palette

    // ── Field-move detection (M17 gate stubs) ────────────────────────────────
    // GSC HM / field-move constant names used to populate the action submenu.
    // M17: field-move dispatch will route these to the overworld-use system.
    let fieldMoveNameSet =
        Set.ofList
            [ "CUT"; "FLY"; "SURF"; "STRENGTH"; "FLASH"
              "WHIRLPOOL"; "WATERFALL"; "ROCK_SMASH"; "HEADBUTT"; "SWEET_SCENT" ]

    /// Move names from `mon.Moves` that are field/HM moves.
    /// Move IDs are stored as ints; looked up via `Moves.all` (keyed by name).
    let fieldMovesOf (mon: PartyMon) : string list =
        mon.Moves
        |> List.choose (fun (moveId, _) ->
            Moves.all
            |> Map.tryFind (string moveId)
            |> Option.bind (fun m ->
                if Set.contains m.Name fieldMoveNameSet then Some m.Name else None))

    // ── Layout constants ──────────────────────────────────────────────────────
    // Screen: 20 × 18 tiles.  Party box: full width.
    // Each slot occupies 2 interior rows (name/level row + HP-bar row).
    // CANCEL takes 1 row.  Max 6 slots → height = 2 border + 12 + 1 = 15.

    [<Literal>]
    let BoxLeft  = 0
    [<Literal>]
    let BoxTop   = 0
    [<Literal>]
    let BoxWidth = 20

    // Action-submenu overlay: cols 11..19 (width 9), top of screen.
    // "CANCEL" (6 chars) + cursor glyph (1) + borders (2) = 9 wide.
    [<Literal>]
    let ActionLeft  = 11
    [<Literal>]
    let ActionTop   = 0
    [<Literal>]
    let ActionWidth = 9

    // ── Menu state ────────────────────────────────────────────────────────────

    let slotCount () = currentPlayer.Party.Length

    /// One cursor entry per party slot plus CANCEL; wrapping enabled.
    let makeMenu (hint: int) : MenuList =
        let n = max 1 (slotCount () + 1)
        MenuList.create n n true |> MenuList.moveTo hint

    let mutable menu = makeMenu 0

    // ── Pure helpers ──────────────────────────────────────────────────────────

    let truncate (n: int) (s: string) =
        if s.Length <= n then s else s.[..n-1]

    /// Build the action list for a party slot, appending any detected field moves.
    let buildActions (mon: PartyMon) : string array =
        [|  yield "STATS"
            yield "SWITCH"
            yield "ITEM"
            for fm in fieldMovesOf mon do   // M17: field-move dispatch
                yield fm
            yield "CANCEL" |]

    let swapPartySlots (i: int) (j: int) (party: Party) : Party =
        let arr = party |> List.toArray
        let tmp  = arr.[i]
        arr.[i] <- arr.[j]
        arr.[j] <- tmp
        arr |> Array.toList

    // ── Rendering helpers ─────────────────────────────────────────────────────

    let renderBox (fb: Framebuffer) =
        let n = slotCount ()
        let h = min 15 (max 4 (2 + n * 2 + 1))   // border + slot rows + CANCEL
        WindowRenderer.drawBox fb content.Font palette BoxLeft BoxTop BoxWidth h

    let renderSlots (fb: Framebuffer) =
        let party = currentPlayer.Party
        let n     = party.Length
        for i in 0 .. n - 1 do
            let mon  = List.item i party
            let row0 = BoxTop + 1 + i * 2   // name/level row
            let row1 = row0 + 1             // HP-bar row

            // ▶ cursor on the name row of the selected slot.
            if menu.Cursor = i then
                WindowRenderer.drawCursor fb content.Font palette (BoxLeft + 1) row0

            // Row 0: NICKNAME  Lv.NN  [status]
            let nick    = truncate 10 mon.Nickname
            let lvStr   = sprintf "Lv.%-3d" mon.Level
            let stsStr  = if mon.Status <> "" then truncate 3 mon.Status else "   "
            WindowRenderer.drawString fb content.Font palette (BoxLeft + 2) row0
                (sprintf "%-10s %s %s" nick lvStr stsStr)

            // Row 1: CUR/MAX  [======  ]
            // 6-char text bar: one '=' per ~8 filled pixels (0-48).
            let fillPx   = HpBar.fill mon.Hp mon.MaxHp
            let barTiles = min 6 (fillPx * 6 / HpBar.BarPx)
            let barStr   = sprintf "[%s%s]" (String.replicate barTiles "=") (String.replicate (6 - barTiles) " ")
            let hpStr    = sprintf "%3d/%-3d" mon.Hp mon.MaxHp
            WindowRenderer.drawString fb content.Font palette (BoxLeft + 2) row1
                (sprintf "HP %s %s" hpStr barStr)

        // CANCEL row at the bottom.
        let cancelRow = BoxTop + 1 + n * 2
        if menu.Cursor = n then
            WindowRenderer.drawCursor fb content.Font palette (BoxLeft + 1) cancelRow
        WindowRenderer.drawString fb content.Font palette (BoxLeft + 2) cancelRow "CANCEL"

    // ── Public API ────────────────────────────────────────────────────────────

    /// Current cursor position (0-based; last entry = CANCEL).
    member _.Cursor = menu.Cursor

    /// Most-recent PlayerState (reflects party mutations from switch/item-take).
    member _.CurrentPlayer = currentPlayer

    // ── Scene interface ───────────────────────────────────────────────────────

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)
            let party = currentPlayer.Party

            match mode with

            // ── Browsing: navigate party slots ────────────────────────────────
            | Browsing ->
                if edges.Up then
                    menu <- MenuList.moveUp menu
                    Stay
                elif edges.Down then
                    menu <- MenuList.moveDown menu
                    Stay
                elif edges.A then
                    let cursor = menu.Cursor
                    if cursor >= party.Length then
                        Pop   // CANCEL row selected
                    else
                        match onSelect with
                        | Some picker ->
                            // Picker mode: A selects the slot directly and returns
                            // the Transition from the picker callback.
                            // M11.3 Pack use/give can push this in picker mode.
                            picker cursor
                        | None ->
                            let mon     = List.item cursor party
                            let actions = buildActions mon
                            let am      = MenuList.create actions.Length actions.Length true
                            mode <- ActionMenu(am, actions, cursor)
                            Stay
                elif edges.B then
                    Pop
                else
                    Stay

            // ── ActionMenu: STATS / SWITCH / ITEM / field-moves / CANCEL ─────
            | ActionMenu(am, actions, slotIdx) ->
                let am' =
                    if   edges.Up   then MenuList.moveUp   am
                    elif edges.Down then MenuList.moveDown am
                    else am
                mode <- ActionMenu(am', actions, slotIdx)

                if edges.A then
                    let chosen = actions.[am'.Cursor]
                    match chosen with

                    | "CANCEL" ->
                        mode <- Browsing
                        Stay

                    | "STATS" ->
                        mode <- Browsing
                        let mon = List.item slotIdx party
                        // M11.5: replace with SummaryScene
                        Push(TextBoxScene.Of(content, sprintf "%s's summary<DONE>" mon.Nickname) :> Scene)

                    | "SWITCH" ->
                        mode <- SwitchPick slotIdx
                        Stay

                    | "ITEM" ->
                        let mon = List.item slotIdx party
                        match mon.HeldItem with
                        | Some itemId ->
                            // Take the held item back into the bag; clear HeldItem.
                            // Seam: giving an item from the bag is driven from Pack (M11.3+).
                            let newBag   = Bag.add itemId 1 currentPlayer.Bag
                            let newMon   = { mon with HeldItem = None }
                            let newParty = currentPlayer.Party |> List.mapi (fun i m -> if i = slotIdx then newMon else m)
                            currentPlayer <- { currentPlayer with Party = newParty; Bag = newBag }
                            onChange currentPlayer
                            mode <- ItemMsg(sprintf "Took %s." itemId)
                            Stay
                        | None ->
                            mode <- ItemMsg(sprintf "%s has nothing." (truncate 12 mon.Nickname))
                            Stay

                    | _ ->
                        // Field move or unrecognised action.
                        // M17: field-move dispatch
                        mode <- Browsing
                        Push(TextBoxScene.Of(content, "Can't use that here yet.<DONE>") :> Scene)

                elif edges.B then
                    mode <- Browsing
                    Stay
                else
                    Stay

            // ── SwitchPick: choose the second slot to swap ───────────────────
            | SwitchPick firstSlot ->
                if edges.Up then
                    menu <- MenuList.moveUp menu
                    Stay
                elif edges.Down then
                    menu <- MenuList.moveDown menu
                    Stay
                elif edges.A then
                    let secondSlot = menu.Cursor
                    if secondSlot >= party.Length then
                        mode <- Browsing   // CANCEL row: abort switch
                        Stay
                    elif secondSlot = firstSlot then
                        mode <- Browsing   // same slot: no-op
                        Stay
                    else
                        let newParty = swapPartySlots firstSlot secondSlot currentPlayer.Party
                        currentPlayer <- { currentPlayer with Party = newParty }
                        onChange currentPlayer
                        menu <- makeMenu secondSlot
                        mode <- Browsing
                        Stay
                elif edges.B then
                    mode <- Browsing
                    Stay
                else
                    Stay

            // ── ItemMsg: brief notice after an item action ───────────────────
            | ItemMsg _ ->
                if edges.A || edges.B then
                    mode <- Browsing
                Stay

        member _.Render(fb: Framebuffer) =
            renderBox fb
            renderSlots fb

            match mode with
            | ActionMenu(am, actions, _) ->
                let h = 2 + actions.Length
                WindowRenderer.drawBox fb content.Font palette ActionLeft ActionTop ActionWidth h
                WindowRenderer.drawList
                    fb content.Font palette
                    (ActionLeft + 1) (ActionTop + 1)
                    (actions |> Array.toSeq)
                    am.Cursor

            | SwitchPick _ ->
                // Show a hint at the bottom of the box during switch-pick mode.
                let n   = currentPlayer.Party.Length
                let row = BoxTop + 1 + n * 2
                WindowRenderer.drawString fb content.Font palette (BoxLeft + 2) row "Pick partner  "

            | ItemMsg msg ->
                let n   = currentPlayer.Party.Length
                let row = BoxTop + 1 + n * 2
                WindowRenderer.drawString fb content.Font palette (BoxLeft + 2) row (truncate 17 msg)

            | Browsing -> ()
