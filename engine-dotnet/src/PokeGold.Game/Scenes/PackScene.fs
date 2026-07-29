namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Player
open PokeGold.Game.Ui

/// Internal state machine sub-mode for the Pack scene.
type PackMode =
    | Browsing
    | ActionMenu of menu: MenuList * actions: string array * itemId: string * itemQty: int
    | TossQty   of itemId: string * maxQty: int * tossQty: int
    | WaitToss  of itemId: string * tossQty: int
    | InfoMsg   of msg: string

/// The bag/pack menu, showing the four GSC pockets (ITEM, BALL, KEY ITEM, TM/HM).
/// Left/Right switch pocket; Up/Down scroll the list; A opens the item action
/// submenu; B/CANCEL closes the scene. TOSS is fully implemented with a quantity
/// selector (Up/Down = ±1, Left/Right = ±10) and a YES/NO confirmation via
/// YesNoScene. GIVE pushes PartyScene in picker mode — on selection the item is
/// transferred to the target mon's held slot (and any prior held item is returned).
/// USE supports party-targeting field items plus world-aware key-item callbacks.
///
///   content   — loaded content (font)
///   player    — initial PlayerState to display
///   onChange  — callback invoked with a new PlayerState whenever the bag changes
type PackScene(content: Content, player: PlayerState, onChange: PlayerState -> unit, ?onFishingRod: string -> unit, ?onEscapeRope: unit -> unit) =

    let pocketNames = [| "ITEM"; "BALL"; "KEY ITEM"; "TM/HM" |]
    let palette     = TextRenderer.palette

    // ── Screen geometry (8-px tile units; screen = 20 × 18 tiles) ────────────

    // Main item-list box: full width, rows 0-10 (interior rows 1-9).
    [<Literal>]
    let MainLeft   = 0
    [<Literal>]
    let MainTop    = 0
    [<Literal>]
    let MainWidth  = 20
    [<Literal>]
    let MainHeight = 11

    // Description box: full width, rows 11-17 (interior rows 12-16).
    [<Literal>]
    let DescLeft   = 0
    [<Literal>]
    let DescTop    = 11
    [<Literal>]
    let DescWidth  = 20
    [<Literal>]
    let DescHeight = 7

    // Action-submenu overlay: cols 11-19 (width 9), anchored to top of screen.
    // "CANCEL" (6 chars) + cursor-glyph (1) + borders (2) = 9 wide, fits exactly.
    [<Literal>]
    let ActionLeft  = 11
    [<Literal>]
    let ActionTop   = 0
    [<Literal>]
    let ActionWidth = 9

    // Quantity-selector overlay: centred, rows 3-7.
    [<Literal>]
    let QtyLeft   = 2
    [<Literal>]
    let QtyTop    = 3
    [<Literal>]
    let QtyWidth  = 16
    [<Literal>]
    let QtyHeight = 5

    // Visible item rows inside the main box (rows MainTop+2 .. MainTop+2+V-1).
    [<Literal>]
    let VisibleItems = 7

    // ── Mutable state ─────────────────────────────────────────────────────────

    let mutable currentPlayer = player
    let mutable pocketIdx     = 0
    let mutable mode          = Browsing : PackMode
    let mutable yesNoResult   = 0
    let input = EdgeDetector()
    let onFishingRod = defaultArg onFishingRod (fun _ -> ())
    let onEscapeRope = defaultArg onEscapeRope (fun () -> ())

    // ── Pure helpers ──────────────────────────────────────────────────────────

    let pocketItems (pi: int) (bag: Bag) : (string * int) list =
        match pi with
        | 0 -> bag.Items
        | 1 -> bag.Balls
        | 2 -> bag.KeyItems
        | _ -> bag.TmHm

    let currentPocket () =
        match pocketIdx with
        | 0 -> Pocket.Item
        | 1 -> Pocket.Ball
        | 2 -> Pocket.KeyItem
        | _ -> Pocket.TmHm

    let makeMenu (pi: int) (bag: Bag) (hint: int) : MenuList =
        let n = pocketItems pi bag |> List.length
        // +1 for the CANCEL row at the end of every pocket list
        MenuList.create (max 1 (n + 1)) VisibleItems false |> MenuList.moveTo hint

    // Defined after makeMenu so the initialiser can call it.
    let mutable menus : MenuList array =
        Array.init 4 (fun i -> makeMenu i player.Bag 0)

    // Rebuild all pocket menus after a bag mutation, clamping cursors.
    let rebuildMenus (bag: Bag) =
        menus <- Array.init 4 (fun i -> makeMenu i bag menus.[i].Cursor)

    let itemName (id: string) =
        ItemsData.byId |> Map.tryFind id |> Option.map (fun d -> d.Name) |> Option.defaultValue id

    let itemDesc (id: string) =
        ItemsData.byId |> Map.tryFind id |> Option.map (fun d -> d.Description) |> Option.defaultValue ""

    let commit (newPlayer: PlayerState) =
        currentPlayer <- newPlayer
        rebuildMenus newPlayer.Bag
        onChange newPlayer

    let continueRareCandyEvolution slotIdx =
        let mon = currentPlayer.Party.[slotIdx]
        match Evolution.tryFind (LevelUp Day) mon with
        | None -> Pop
        | Some candidate ->
            Replace(
                EvolutionScene(
                    content.Font,
                    mon.Nickname,
                    candidate.Target,
                    fun decision ->
                        match decision with
                        | CancelEvolution -> ()
                        | AcceptEvolution ->
                            PackUseGive.applyEvolution "RARE_CANDY" slotIdx candidate currentPlayer
                            |> commit) :> Scene)

    /// Build the action list for an item. Returns at least ["TOSS"; "CANCEL"].
    let buildActions (id: string) (pocket: Pocket) : string array =
        let data      = ItemsData.byId |> Map.tryFind id
        let fieldMenu = data |> Option.map (fun d -> d.FieldMenu) |> Option.defaultValue "ITEMMENU_NOUSE"
        [
            // USE: items/key-items with a field-menu action; TMs (teach).
            match pocket with
            | Pocket.Item | Pocket.KeyItem ->
                if id = "ESCAPE_ROPE" || PackUseGive.isFishingRod id || (fieldMenu <> "" && fieldMenu <> "ITEMMENU_NOUSE") then yield "USE"
            | Pocket.TmHm  -> yield "USE"
            | Pocket.Ball  -> ()
            // GIVE: item pocket only (key items and TMs/HMs cannot be held).
            match pocket with
            | Pocket.Item -> yield "GIVE"
            | _           -> ()
            yield "TOSS"
            yield "CANCEL"
        ] |> List.toArray

    let truncate (n: int) (s: string) = if s.Length <= n then s else s.[..n-1]

    let descLines (desc: string) =
        let seps  = [| "<LINE>"; "<NEXT>"; "<LF>"; "\n" |]
        let parts = desc.Split(seps, System.StringSplitOptions.RemoveEmptyEntries)
        let l1    = if parts.Length > 0 then truncate 18 parts.[0] else ""
        let l2    = if parts.Length > 1 then truncate 18 parts.[1] else ""
        l1, l2

    // ── Rendering helpers ─────────────────────────────────────────────────────

    let renderMainBox (fb: Framebuffer) =
        WindowRenderer.drawBox fb content.Font palette MainLeft MainTop MainWidth MainHeight
        // Pocket name in the first interior row.
        WindowRenderer.drawString fb content.Font palette (MainLeft + 2) (MainTop + 1) pocketNames.[pocketIdx]

    let renderItems (fb: Framebuffer) =
        let items = pocketItems pocketIdx currentPlayer.Bag
        let menu  = menus.[pocketIdx]
        let vis   = min menu.Visible (menu.Count - menu.Top)
        let labels =
            [| for i in 0 .. vis - 1 do
                   let idx = menu.Top + i
                   if idx < items.Length then
                       let (id, qty) = items.[idx]
                       yield sprintf "%s x%d" (itemName id) qty
                   else
                       yield "CANCEL" |]
        WindowRenderer.drawList
            fb content.Font palette
            (MainLeft + 1) (MainTop + 2)
            (labels |> Array.toSeq)
            (menu.Cursor - menu.Top)

    let renderDescBox (fb: Framebuffer) (overridePair: (string * string) option) =
        WindowRenderer.drawBox fb content.Font palette DescLeft DescTop DescWidth DescHeight
        let items = pocketItems pocketIdx currentPlayer.Bag
        let menu  = menus.[pocketIdx]
        let (l1, l2) =
            match overridePair with
            | Some p -> p
            | None ->
                if menu.Cursor < items.Length then
                    let (id, _) = items.[menu.Cursor]
                    descLines (itemDesc id)
                else
                    "", ""   // CANCEL row: no description
        if l1 <> "" then
            WindowRenderer.drawString fb content.Font palette (DescLeft + 1) (DescTop + 2) (truncate 18 l1)
        if l2 <> "" then
            WindowRenderer.drawString fb content.Font palette (DescLeft + 1) (DescTop + 3) (truncate 18 l2)

    // ── Public API (exposed for unit tests) ───────────────────────────────────

    /// Current pocket index: 0=ITEM, 1=BALL, 2=KEY ITEM, 3=TM/HM.
    member _.PocketIndex = pocketIdx

    /// Cursor row within the active pocket (0-based; last row = CANCEL).
    member _.Cursor = menus.[pocketIdx].Cursor

    /// Most recent PlayerState (reflects bag mutations from confirmed tosses).
    member _.CurrentPlayer = currentPlayer

    // ── Scene interface ────────────────────────────────────────────────────────

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)
            let items = pocketItems pocketIdx currentPlayer.Bag

            match mode with

            // ── Browsing: navigate pockets and items ──────────────────────────
            | Browsing ->
                if edges.Left then
                    pocketIdx <- (pocketIdx + 3) % 4
                    Stay
                elif edges.Right then
                    pocketIdx <- (pocketIdx + 1) % 4
                    Stay
                elif edges.Up then
                    menus.[pocketIdx] <- MenuList.moveUp menus.[pocketIdx]
                    Stay
                elif edges.Down then
                    menus.[pocketIdx] <- MenuList.moveDown menus.[pocketIdx]
                    Stay
                elif edges.A then
                    let cursor = menus.[pocketIdx].Cursor
                    if cursor >= items.Length then
                        Pop  // CANCEL row → close Pack
                    else
                        let (id, qty) = items.[cursor]
                        let actions   = buildActions id (currentPocket ())
                        let am        = MenuList.create actions.Length actions.Length true
                        mode <- ActionMenu(am, actions, id, qty)
                        Stay
                elif edges.B then
                    Pop
                else
                    Stay

            // ── ActionMenu: USE / GIVE / TOSS / CANCEL ────────────────────────
            | ActionMenu(am, actions, id, qty) ->
                let am' =
                    if   edges.Up   then MenuList.moveUp   am
                    elif edges.Down then MenuList.moveDown am
                    else am
                mode <- ActionMenu(am', actions, id, qty)

                if edges.A then
                    let chosen = actions.[am'.Cursor]
                    match chosen with
                    | "CANCEL" ->
                        mode <- Browsing
                        Stay
                    | "USE" ->
                        mode <- Browsing
                        if PackUseGive.isFishingRod id then
                            onFishingRod id
                            Pop
                        elif id = "ESCAPE_ROPE" then
                            onEscapeRope ()
                            Pop
                        elif PackUseGive.isRepel id then
                            match PackUseGive.applyRepel id currentPlayer with
                            | Some newPlayer ->
                                currentPlayer <- newPlayer
                                rebuildMenus newPlayer.Bag
                                onChange newPlayer
                                Pop
                            | None ->
                                Pop
                        elif PackUseGive.isFullRestore id then
                            Push(
                                PartyScene(content, currentPlayer, onChange,
                                    fun slotIdx ->
                                        match PackUseGive.applyFullRestore slotIdx currentPlayer with
                                        | Some newPlayer ->
                                            currentPlayer <- newPlayer
                                            rebuildMenus newPlayer.Bag
                                            onChange newPlayer
                                            Pop
                                        | None -> Pop) :> Scene)
                        elif PackUseGive.isStatusCure id then
                            Push(
                                PartyScene(content, currentPlayer, onChange,
                                    fun slotIdx ->
                                        match PackUseGive.applyStatusCure id slotIdx currentPlayer with
                                        | Some newPlayer ->
                                            currentPlayer <- newPlayer
                                            rebuildMenus newPlayer.Bag
                                            onChange newPlayer
                                            Pop
                                        | None -> Pop) :> Scene)
                        elif PackUseGive.isHpHeal id then
                            // HP-restore item: push Party as a target picker.
                            Push(
                                PartyScene(content, currentPlayer, onChange,
                                    fun slotIdx ->
                                        match PackUseGive.applyHpHeal id slotIdx currentPlayer with
                                        | Some newPlayer ->
                                            currentPlayer <- newPlayer
                                            rebuildMenus newPlayer.Bag
                                            onChange newPlayer
                                            Pop
                                        | None ->
                                            // Mon already at full HP — don't consume. Pop back to Pack.
                                            Pop) :> Scene)
                        elif PackUseGive.isRareCandy id then
                            Push(
                                PartyScene(content, currentPlayer, onChange,
                                    fun slotIdx ->
                                        match PackUseGive.applyRareCandy slotIdx currentPlayer with
                                        | None -> Pop
                                        | Some(newPlayer, requests) ->
                                            commit newPlayer
                                            match requests with
                                            | [] -> continueRareCandyEvolution slotIdx
                                            | request :: _ ->
                                                // Field Rare Candy resolves one replacement prompt; subsequent
                                                // same-level requests are intentionally not queued in this UI flow.
                                                match Moves.tryByIndex request.MoveId with
                                                | None -> continueRareCandyEvolution slotIdx
                                                | Some move ->
                                                    let mon = currentPlayer.Party.[slotIdx]
                                                    Replace(
                                                        LearnMoveScene(
                                                            content.Font,
                                                            mon.Nickname,
                                                            move.Name,
                                                            mon.Moves,
                                                            (fun _ -> ()),
                                                            onDecisionTransition = fun decision ->
                                                                match decision with
                                                                | DeclineMove -> ()
                                                                | ReplaceMove moveIndex ->
                                                                    let current = currentPlayer.Party.[slotIdx]
                                                                    let moves =
                                                                        current.Moves
                                                                        |> List.mapi (fun i existing ->
                                                                            if i = moveIndex then request.MoveId, move.Pp else existing)
                                                                    commit { currentPlayer with Party = currentPlayer.Party |> List.mapi (fun i existing -> if i = slotIdx then { current with Moves = moves } else existing) }
                                                                continueRareCandyEvolution slotIdx) :> Scene)) :> Scene)
                        elif PackUseGive.isVitamin id then
                            Push(
                                PartyScene(content, currentPlayer, onChange,
                                    fun slotIdx ->
                                        match PackUseGive.applyVitamin id slotIdx currentPlayer with
                                        | Some newPlayer -> commit newPlayer; Pop
                                        | None -> Pop) :> Scene)
                        elif PackUseGive.isEther id || PackUseGive.isPpUp id then
                            Push(
                                PartyScene(
                                    content,
                                    currentPlayer,
                                    onChange,
                                    onMoveSelect = fun slotIdx moveIdx ->
                                        let result =
                                            if PackUseGive.isEther id then PackUseGive.applyEther id slotIdx moveIdx currentPlayer
                                            else PackUseGive.applyPpUp id slotIdx moveIdx currentPlayer
                                        match result with
                                        | Some newPlayer -> commit newPlayer; Pop
                                        | None -> Pop) :> Scene)
                        elif PackUseGive.isElixer id then
                            Push(
                                PartyScene(content, currentPlayer, onChange,
                                    fun slotIdx ->
                                        match PackUseGive.applyElixer id slotIdx currentPlayer with
                                        | Some newPlayer -> commit newPlayer; Pop
                                        | None -> Pop) :> Scene)
                        elif PackUseGive.isRevive id then
                            Push(
                                PartyScene(content, currentPlayer, onChange,
                                    fun slotIdx ->
                                        match PackUseGive.applyRevive id slotIdx currentPlayer with
                                        | Some newPlayer -> commit newPlayer; Pop
                                        | None -> Pop) :> Scene)
                        elif PackUseGive.isEvolutionStone id then
                            Push(
                                PartyScene(content, currentPlayer, onChange,
                                    fun slotIdx ->
                                        match PackUseGive.prepareEvolution id slotIdx currentPlayer with
                                        | None -> Pop
                                        | Some candidate ->
                                            let mon = currentPlayer.Party.[slotIdx]
                                            currentPlayer <- PackUseGive.consumeEvolutionStone id currentPlayer
                                            rebuildMenus currentPlayer.Bag
                                            onChange currentPlayer
                                            Replace(
                                                EvolutionScene(
                                                    content.Font,
                                                    mon.Nickname,
                                                    candidate.Target,
                                                    fun decision ->
                                                        match decision with
                                                        | CancelEvolution -> ()
                                                        | AcceptEvolution ->
                                                            let newPlayer = PackUseGive.applyEvolution id slotIdx candidate currentPlayer
                                                            currentPlayer <- newPlayer
                                                            rebuildMenus newPlayer.Bag
                                                            onChange newPlayer) :> Scene)) :> Scene)
                        elif PackUseGive.isTmHm id then
                            Push(
                                PartyScene(content, currentPlayer, onChange,
                                    fun slotIdx ->
                                        let mon = currentPlayer.Party.[slotIdx]
                                        let commit taughtMon =
                                            let bag = if TmHm.isHmItem id then currentPlayer.Bag else Bag.remove id 1 currentPlayer.Bag
                                            let party = currentPlayer.Party |> List.mapi (fun i existing -> if i = slotIdx then taughtMon else existing)
                                            let newPlayer = { currentPlayer with Party = party; Bag = bag }
                                            currentPlayer <- newPlayer
                                            rebuildMenus newPlayer.Bag
                                            onChange newPlayer

                                        match TmHm.prepare id mon with
                                        | LearnedImmediately taughtMon ->
                                            commit taughtMon
                                            Pop
                                        | NeedsReplacement moveId ->
                                            let move = MovesData.byIndex.[moveId]
                                            Replace(
                                                LearnMoveScene(
                                                    content.Font,
                                                    mon.Nickname,
                                                    move.Name,
                                                    mon.Moves,
                                                    fun decision ->
                                                        match decision with
                                                        | DeclineMove -> ()
                                                        | ReplaceMove index -> commit (TmHm.replaceMove moveId index mon)) :> Scene)
                                        | UnknownTmHm
                                        | Incompatible
                                        | AlreadyKnows ->
                                            Pop) :> Scene)
                        else
                            // Deferred field-use category.
                            Push(TextBoxScene.Of(content, "Can't use that here yet.<DONE>") :> Scene)
                    | "GIVE" ->
                        mode <- Browsing
                        // Push Party as a target picker; on select, give the item.
                        Push(
                            PartyScene(content, currentPlayer, onChange,
                                fun slotIdx ->
                                    let newPlayer = PackUseGive.applyGive id slotIdx currentPlayer
                                    currentPlayer <- newPlayer
                                    rebuildMenus newPlayer.Bag
                                    onChange newPlayer
                                    Pop) :> Scene)
                    | "TOSS" ->
                        let cantToss =
                            ItemsData.byId
                            |> Map.tryFind id
                            |> Option.map (fun d -> d.CantToss)
                            |> Option.defaultValue false
                        if cantToss then
                            mode <- InfoMsg(sprintf "Can't toss %s!" (itemName id))
                            Stay
                        else
                            mode <- TossQty(id, qty, 1)
                            Stay
                    | _ ->
                        mode <- Browsing
                        Stay
                elif edges.B then
                    mode <- Browsing
                    Stay
                else
                    Stay

            // ── TossQty: choose how many to discard ───────────────────────────
            | TossQty(id, maxQty, tossQty) ->
                let delta =
                    if   edges.Up    then  1
                    elif edges.Down  then -1
                    elif edges.Right then  10
                    elif edges.Left  then -10
                    else 0
                let newQty = max 1 (min maxQty (tossQty + delta))
                mode <- TossQty(id, maxQty, newQty)
                if edges.A then
                    mode <- WaitToss(id, newQty)
                    Push(YesNoScene(content.Font, fun r -> yesNoResult <- r) :> Scene)
                elif edges.B then
                    mode <- Browsing
                    Stay
                else
                    Stay

            // ── WaitToss: YesNoScene just popped; consume the callback result ─
            | WaitToss(id, qty) ->
                let r = yesNoResult
                yesNoResult <- 0
                mode <- Browsing
                if r = 1 then
                    let newBag = Bag.remove id qty currentPlayer.Bag
                    currentPlayer <- { currentPlayer with Bag = newBag }
                    onChange currentPlayer
                    rebuildMenus newBag
                Stay

            // ── InfoMsg: CantToss refusal or similar notice ───────────────────
            | InfoMsg _ ->
                if edges.A || edges.B then
                    mode <- Browsing
                Stay

        member _.Render(fb: Framebuffer) =
            renderMainBox fb
            renderItems fb

            match mode with
            | Browsing | WaitToss _ ->
                renderDescBox fb None

            | ActionMenu(am, actions, _, _) ->
                renderDescBox fb None
                // Action submenu overlaid in the top-right of the main box.
                let h = 2 + actions.Length
                WindowRenderer.drawBox fb content.Font palette ActionLeft ActionTop ActionWidth h
                WindowRenderer.drawList
                    fb content.Font palette
                    (ActionLeft + 1) (ActionTop + 1)
                    (actions |> Array.toSeq)
                    am.Cursor

            | TossQty(id, _, tossQty) ->
                renderDescBox fb None
                // Quantity selector overlay, centred in the main-box area.
                WindowRenderer.drawBox fb content.Font palette QtyLeft QtyTop QtyWidth QtyHeight
                WindowRenderer.drawString fb content.Font palette (QtyLeft + 1) (QtyTop + 1) "TOSS HOW MANY?"
                let qtyLabel = sprintf "%s x%d" (itemName id) tossQty
                WindowRenderer.drawString fb content.Font palette (QtyLeft + 2) (QtyTop + 2) (truncate 14 qtyLabel)
                WindowRenderer.drawString fb content.Font palette (QtyLeft + 1) (QtyTop + 3) "Up/Dn:1  L/R:10"

            | InfoMsg msg ->
                renderDescBox fb (Some (truncate 18 msg, "Press A or B"))
