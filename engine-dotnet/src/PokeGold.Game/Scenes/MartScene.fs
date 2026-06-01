namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Player
open PokeGold.Game.Ui

/// Internal state-machine modes for MartScene.
type MartMode =
    | TopMenu
    | Buying
    | BuyQty   of itemId: string * maxQty: int * qty: int
    | BuyWait  of itemId: string * qty: int
    | Selling
    | SellQty  of itemId: string * maxQty: int * qty: int
    | SellWait of itemId: string * qty: int
    | ShowMsg  of msg: string

/// The Poké Mart buy/sell scene. Models the GSC StandardMart flow:
/// a top menu (BUY / SELL / QUIT), a scrollable item list with prices,
/// a quantity selector (1-99, capped by affordability), a YES/NO confirm,
/// and a result message before returning to the top menu. B or QUIT pops
/// the scene back to the overworld.
///
///   content       — loaded content (font)
///   initialPlayer — player state at mart open time
///   martType      — MARTTYPE_STANDARD / BITTER / BARGAIN / PHARMACY (all treated as standard)
///   items         — ordered item constant list for this mart's inventory
///   onChange      — called with the updated PlayerState on each bag/money change
type MartScene(content: Content, initialPlayer: PlayerState, martType: string, items: string list, onChange: PlayerState -> unit) =

    let palette = TextRenderer.palette

    // ── Screen geometry (8-px tile units; screen = 20 × 18 tiles) ─────────────

    // Item list box: full width, rows 0-11 (interior rows 1-10).
    [<Literal>]
    let ListLeft   = 0
    [<Literal>]
    let ListTop    = 0
    [<Literal>]
    let ListWidth  = 20
    [<Literal>]
    let ListHeight = 12
    [<Literal>]
    let VisibleItems = 8   // rows ListTop+2 .. ListTop+9

    // Info/money box: full width, rows 12-17.
    [<Literal>]
    let InfoLeft   = 0
    [<Literal>]
    let InfoTop    = 12
    [<Literal>]
    let InfoWidth  = 20
    [<Literal>]
    let InfoHeight = 6

    // Top-menu overlay: top-right corner, cols 12-19 (width 8), rows 0..(2+3-1).
    [<Literal>]
    let TopMenuLeft  = 12
    [<Literal>]
    let TopMenuTop   = 0
    [<Literal>]
    let TopMenuWidth = 8   // "QUIT" (4) + cursor(1) + borders(2) = 7, round up

    // Quantity overlay: cols 2-17 (width 16), rows 3-7 (height 5).
    [<Literal>]
    let QtyLeft   = 2
    [<Literal>]
    let QtyTop    = 3
    [<Literal>]
    let QtyWidth  = 16
    [<Literal>]
    let QtyHeight = 5

    // ── Mutable state ──────────────────────────────────────────────────────────

    let mutable currentPlayer = initialPlayer
    let mutable mode: MartMode = TopMenu
    let mutable topMenu    = MenuList.create 3 3 true    // BUY / SELL / QUIT
    let mutable buyMenu    = MenuList.create (items.Length + 1) VisibleItems false
    let mutable sellMenu   = MenuList.create 1 VisibleItems false
    let mutable yesNoResult = 0
    let input = EdgeDetector()

    // ── Pure helpers ──────────────────────────────────────────────────────────

    let itemName (id: string) =
        ItemsData.byId |> Map.tryFind id |> Option.map (fun d -> d.Name) |> Option.defaultValue id

    let itemPrice (id: string) =
        ItemsData.byId |> Map.tryFind id |> Option.map (fun d -> d.Price) |> Option.defaultValue 0

    let truncate (n: int) (s: string) =
        if s.Length <= n then s else s.[..n-1]

    /// Sellable items in the player's bag (item + ball pockets, canSell filtered).
    let sellableItems (bag: Bag) : (string * int) list =
        (bag.Items @ bag.Balls)
        |> List.filter (fun (id, _) -> Mart.canSell id)

    /// Maximum affordable quantity at given price (1..99).
    let maxAffordable (price: int) : int =
        if price <= 0 then 99
        else min 99 (max 1 (currentPlayer.Money / price))

    let rebuildSellMenu (bag: Bag) =
        let n = sellableItems bag |> List.length
        sellMenu <- MenuList.create (max 1 (n + 1)) VisibleItems false

    let topMenuLabels = [| "BUY"; "SELL"; "QUIT" |]

    // ── Rendering helpers ─────────────────────────────────────────────────────

    let renderListBox (fb: Framebuffer) (title: string) =
        WindowRenderer.drawBox fb content.Font palette ListLeft ListTop ListWidth ListHeight
        WindowRenderer.drawString fb content.Font palette (ListLeft + 2) (ListTop + 1) title

    let renderBuyItems (fb: Framebuffer) =
        let vis = min buyMenu.Visible (buyMenu.Count - buyMenu.Top)
        let labels =
            [| for i in 0 .. vis - 1 do
                   let idx = buyMenu.Top + i
                   if idx < items.Length then
                       let id    = items.[idx]
                       let price = itemPrice id
                       let name  = truncate 9 (itemName id)
                       yield sprintf "%s P%d" name price
                   else
                       yield "CANCEL" |]
        WindowRenderer.drawList
            fb content.Font palette
            (ListLeft + 1) (ListTop + 2)
            (labels |> Array.toSeq)
            (buyMenu.Cursor - buyMenu.Top)

    let renderSellItems (fb: Framebuffer) =
        let sellable = sellableItems currentPlayer.Bag
        let vis = min sellMenu.Visible (sellMenu.Count - sellMenu.Top)
        let labels =
            [| for i in 0 .. vis - 1 do
                   let idx = sellMenu.Top + i
                   if idx < sellable.Length then
                       let (id, qty) = sellable.[idx]
                       let sellPrc   = Money.sellPrice (itemPrice id) 1
                       let name      = truncate 8 (itemName id)
                       yield sprintf "%s x%d P%d" name qty sellPrc
                   else
                       yield "CANCEL" |]
        WindowRenderer.drawList
            fb content.Font palette
            (ListLeft + 1) (ListTop + 2)
            (labels |> Array.toSeq)
            (sellMenu.Cursor - sellMenu.Top)

    let renderInfoBox (fb: Framebuffer) (line1: string) (line2: string) =
        WindowRenderer.drawBox fb content.Font palette InfoLeft InfoTop InfoWidth InfoHeight
        WindowRenderer.drawString fb content.Font palette (InfoLeft + 1) (InfoTop + 1) "MONEY"
        WindowRenderer.drawString fb content.Font palette (InfoLeft + 1) (InfoTop + 2) (sprintf "P%d" currentPlayer.Money)
        if line1 <> "" then
            WindowRenderer.drawString fb content.Font palette (InfoLeft + 1) (InfoTop + 4) (truncate 18 line1)
        if line2 <> "" then
            WindowRenderer.drawString fb content.Font palette (InfoLeft + 1) (InfoTop + 5) (truncate 18 line2)

    let renderQtyOverlay (fb: Framebuffer) (prompt: string) (label: string) =
        WindowRenderer.drawBox fb content.Font palette QtyLeft QtyTop QtyWidth QtyHeight
        WindowRenderer.drawString fb content.Font palette (QtyLeft + 1) (QtyTop + 1) (truncate 14 prompt)
        WindowRenderer.drawString fb content.Font palette (QtyLeft + 2) (QtyTop + 2) (truncate 12 label)
        WindowRenderer.drawString fb content.Font palette (QtyLeft + 1) (QtyTop + 3) "Up/Dn:1  L/R:10"

    // ── Public API (for unit tests) ───────────────────────────────────────────

    /// Current mode — allows tests to assert navigation state.
    member _.Mode = mode

    /// Most recent PlayerState (reflects buy/sell mutations).
    member _.CurrentPlayer = currentPlayer

    // ── Scene interface ───────────────────────────────────────────────────────

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)

            match mode with

            // ── Top menu: BUY / SELL / QUIT ────────────────────────────────
            | TopMenu ->
                topMenu <-
                    if edges.Up   then MenuList.moveUp   topMenu
                    elif edges.Down then MenuList.moveDown topMenu
                    else topMenu

                if edges.A then
                    match topMenu.Cursor with
                    | 0 -> // BUY
                        buyMenu <- MenuList.create (items.Length + 1) VisibleItems false
                        mode <- Buying
                        Stay
                    | 1 -> // SELL
                        rebuildSellMenu currentPlayer.Bag
                        mode <- Selling
                        Stay
                    | _ -> Pop // QUIT
                elif edges.B then Pop
                else Stay

            // ── Buying: browse mart item list ──────────────────────────────
            | Buying ->
                buyMenu <-
                    if edges.Up   then MenuList.moveUp   buyMenu
                    elif edges.Down then MenuList.moveDown buyMenu
                    else buyMenu

                if edges.A then
                    if buyMenu.Cursor >= items.Length then
                        mode <- TopMenu
                        Stay
                    else
                        let itemId = items.[buyMenu.Cursor]
                        let price  = itemPrice itemId
                        let maxQ   = maxAffordable price
                        mode <- BuyQty(itemId, maxQ, 1)
                        Stay
                elif edges.B then
                    mode <- TopMenu
                    Stay
                else Stay

            // ── BuyQty: choose how many to buy ─────────────────────────────
            | BuyQty(itemId, maxQty, qty) ->
                let delta =
                    if   edges.Up    then  1
                    elif edges.Down  then -1
                    elif edges.Right then  10
                    elif edges.Left  then -10
                    else 0
                let newQty = max 1 (min maxQty (qty + delta))
                mode <- BuyQty(itemId, maxQty, newQty)

                if edges.A then
                    mode <- BuyWait(itemId, newQty)
                    Push(YesNoScene(content.Font, fun r -> yesNoResult <- r) :> Scene)
                elif edges.B then
                    mode <- Buying
                    Stay
                else Stay

            // ── BuyWait: YesNoScene just popped; commit or cancel ──────────
            | BuyWait(itemId, qty) ->
                let r = yesNoResult
                yesNoResult <- 0
                let price = itemPrice itemId
                if r = 1 then
                    match Mart.buy itemId price qty currentPlayer.Money currentPlayer.Bag with
                    | Ok(newMoney, newBag) ->
                        currentPlayer <- { currentPlayer with Money = newMoney; Bag = newBag }
                        onChange currentPlayer
                        mode <- ShowMsg "Here you go!"
                    | Error Mart.CantAfford ->
                        mode <- ShowMsg "You can't afford it!"
                else
                    mode <- Buying
                Stay

            // ── Selling: browse sellable bag items ─────────────────────────
            | Selling ->
                let sellable = sellableItems currentPlayer.Bag
                sellMenu <-
                    if edges.Up   then MenuList.moveUp   sellMenu
                    elif edges.Down then MenuList.moveDown sellMenu
                    else sellMenu

                if edges.A then
                    if sellMenu.Cursor >= sellable.Length then
                        mode <- TopMenu
                        Stay
                    else
                        let (itemId, haveQty) = sellable.[sellMenu.Cursor]
                        mode <- SellQty(itemId, haveQty, 1)
                        Stay
                elif edges.B then
                    mode <- TopMenu
                    Stay
                else Stay

            // ── SellQty: choose how many to sell ───────────────────────────
            | SellQty(itemId, maxQty, qty) ->
                let delta =
                    if   edges.Up    then  1
                    elif edges.Down  then -1
                    elif edges.Right then  10
                    elif edges.Left  then -10
                    else 0
                let newQty = max 1 (min maxQty (qty + delta))
                mode <- SellQty(itemId, maxQty, newQty)

                if edges.A then
                    mode <- SellWait(itemId, newQty)
                    Push(YesNoScene(content.Font, fun r -> yesNoResult <- r) :> Scene)
                elif edges.B then
                    mode <- Selling
                    Stay
                else Stay

            // ── SellWait: YesNoScene popped; commit sell or cancel ─────────
            | SellWait(itemId, qty) ->
                let r = yesNoResult
                yesNoResult <- 0
                let price = itemPrice itemId
                if r = 1 then
                    match Mart.sell itemId price qty currentPlayer.Money currentPlayer.Bag with
                    | Ok(newMoney, newBag) ->
                        currentPlayer <- { currentPlayer with Money = newMoney; Bag = newBag }
                        onChange currentPlayer
                        let earned = Money.sellPrice price qty
                        mode <- ShowMsg(sprintf "Got P%d!" earned)
                    | Error Mart.CantSell ->
                        mode <- ShowMsg "Can't sell that!"
                    | Error Mart.NotInBag ->
                        mode <- ShowMsg "Don't have enough!"
                else
                    mode <- Selling
                Stay

            // ── ShowMsg: transaction result; A/B returns to top menu ───────
            | ShowMsg _ ->
                if edges.A || edges.B then
                    mode <- TopMenu
                Stay

        member _.Render(fb: Framebuffer) =
            match mode with

            | TopMenu ->
                // Top-menu overlay: BUY / SELL / QUIT in top-right corner.
                let tmH = 2 + topMenuLabels.Length
                WindowRenderer.drawBox fb content.Font palette TopMenuLeft TopMenuTop TopMenuWidth tmH
                WindowRenderer.drawList
                    fb content.Font palette
                    (TopMenuLeft + 1) (TopMenuTop + 1)
                    (topMenuLabels |> Array.toSeq)
                    topMenu.Cursor
                renderInfoBox fb "" ""

            | Buying | BuyWait _ ->
                renderListBox fb "BUY"
                renderBuyItems fb
                // Show selected item's unit price in the info area.
                let priceInfo =
                    if buyMenu.Cursor < items.Length then
                        let id = items.[buyMenu.Cursor]
                        sprintf "P%d each" (itemPrice id)
                    else ""
                renderInfoBox fb priceInfo ""

            | BuyQty(itemId, _, qty) ->
                renderListBox fb "BUY"
                renderBuyItems fb
                let price = itemPrice itemId
                let total = Money.buyTotal price qty
                renderQtyOverlay fb
                    (sprintf "HOW MANY? (max %d)" (maxAffordable price))
                    (sprintf "x%d  P%d" qty total)
                renderInfoBox fb (sprintf "P%d each" price) (sprintf "Total: P%d" total)

            | Selling | SellWait _ ->
                renderListBox fb "SELL"
                renderSellItems fb
                // Show selected item's sell price in info area.
                let sellInfo =
                    let sellable = sellableItems currentPlayer.Bag
                    if sellMenu.Cursor < sellable.Length then
                        let (id, _) = sellable.[sellMenu.Cursor]
                        sprintf "P%d each" (Money.sellPrice (itemPrice id) 1)
                    else ""
                renderInfoBox fb sellInfo ""

            | SellQty(itemId, _, qty) ->
                renderListBox fb "SELL"
                renderSellItems fb
                let price  = itemPrice itemId
                let earned = Money.sellPrice price qty
                renderQtyOverlay fb
                    "HOW MANY?"
                    (sprintf "x%d  earn P%d" qty earned)
                renderInfoBox fb (sprintf "Sell P%d each" (Money.sellPrice price 1)) (sprintf "Total: P%d" earned)

            | ShowMsg msg ->
                renderListBox fb "MART"
                renderInfoBox fb (truncate 18 msg) "Press A or B"
