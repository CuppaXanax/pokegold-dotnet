namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// Sub-mode for the Pokédex scene.
type DexMode =
    | DexList
    | DexDetail of num: int

/// Pure helpers for the Pokédex scene — extracted so tests can call them directly
/// without touching the framebuffer.
module Pokedex =

    /// Total dex entries (Generation I + II).
    let entryCount = 251

    /// Number of distinct species the player has seen.
    let seenCount (player: PlayerState) = player.DexSeen.Count

    /// Number of distinct species the player owns.
    let ownCount (player: PlayerState) = player.DexOwn.Count

    /// Row label for dex entry `num` (1-based) given player state.
    ///
    /// Format: "#NNN M NAME_____"
    ///   M = '*' for owned, ' ' for seen-not-owned or unseen.
    ///   NAME is the species name (up to 9 chars) when seen, or "---------" when unseen.
    let rowLabel (player: PlayerState) (num: int) : string =
        let entry = Dex.byNum |> Map.tryFind num
        let rawName =
            match entry with
            | Some e -> e.Name
            | None   -> sprintf "??%d" num
        let isOwned = player.DexOwn.Contains num
        let isSeen  = player.DexSeen.Contains num
        let marker  = if isOwned then "*" else " "
        let displayName =
            let n = if rawName.Length > 9 then rawName.[..8] else rawName
            if isSeen then sprintf "%-9s" n
            else           "---------"
        sprintf "#%03d %s %s" num marker displayName

    /// Height in feet and inches from the packed encoding (feet*100 + inches).
    /// The disassembly stores height as `dw <feet>*100+<inches>`, so e.g.
    /// 204 → 2'04" and 104 → 1'04".
    let heightLabel (packed: int) : string =
        let feet   = packed / 100
        let inches = packed % 100
        sprintf "%d'%02d\"" feet inches

    /// Weight in pounds from tenths-of-a-pound storage.
    /// The disassembly stores weight as a `dw` tenths-of-a-pound value,
    /// so e.g. 150 → 15.0 lb and 2000 → 200.0 lb.
    let weightLabel (tenthsLb: int) : string =
        let lbs = float tenthsLb / 10.0
        sprintf "%.1f lb" lbs

    /// Split a GSC-encoded description string into display lines, stripping
    /// control tokens (<LINE>, <NEXT>, <DONE>, <LF>).
    let descLines (desc: string) : string list =
        let seps = [| "<LINE>"; "<NEXT>"; "<DONE>"; "<LF>"; "\n" |]
        desc.Split(seps, System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList
        |> List.filter (fun s -> s.Trim() <> "")

/// The Pokédex scene: a scrolling 251-entry list showing seen/own markers,
/// SEEN and OWN running counters, and a full-detail view for owned species.
///
///   content — loaded content (font)
///   player  — read-only PlayerState (DexSeen/DexOwn are never mutated here)
///
/// List mode:
///   Up/Down  – scroll the 251-entry list (7 visible rows, no wrap).
///   A        – open detail for an owned or seen entry; unseen entries do nothing.
///   B        – pop the scene (return to start menu → overworld).
///
/// Detail mode:
///   B        – return to list mode.
///   A        – does nothing (returns Stay).
///
/// // later: dex sprite + cry
type PokedexScene(content: Content, player: PlayerState) =

    let mutable mode : DexMode = DexList
    let mutable menu = MenuList.create Pokedex.entryCount 7 false
    let input   = EdgeDetector()
    let palette = TextRenderer.palette

    // Full GBC screen (20 × 18 tiles); border at rows/cols 0 and 17/19.
    [<Literal>]
    let BoxLeft   = 0
    [<Literal>]
    let BoxTop    = 0
    [<Literal>]
    let BoxWidth  = 20
    [<Literal>]
    let BoxHeight = 18

    // List layout: ▶ cursor glyph at col ListLeft, text at ListLeft+1.
    // Visible window = 7 rows starting at ListTop.
    [<Literal>]
    let ListLeft = 1
    [<Literal>]
    let ListTop  = 2

    // ── Rendering helpers ─────────────────────────────────────────────────────

    let draw (fb: Framebuffer) (col: int) (row: int) (s: string) =
        WindowRenderer.drawString fb content.Font palette col row s

    let truncate (n: int) (s: string) =
        if s.Length <= n then s else s.[..n - 1]

    let renderList (fb: Framebuffer) =
        draw fb (BoxLeft + 2) (BoxTop + 1) "POKéDEX"

        // 7-row visible window: generate labels for entries menu.Top+1 .. menu.Top+7.
        let labels =
            [| for i in 0 .. 6 do
                   let num = menu.Top + i + 1   // dex numbers are 1-based
                   if num <= Pokedex.entryCount then
                       yield Pokedex.rowLabel player num |]
        WindowRenderer.drawList
            fb content.Font palette
            ListLeft ListTop
            (labels |> Array.toSeq)
            (menu.Cursor - menu.Top)

        // SEEN / OWN counters below the list.
        draw fb (BoxLeft + 2) (BoxTop + 10) (sprintf "SEEN %3d" (Pokedex.seenCount player))
        draw fb (BoxLeft + 2) (BoxTop + 11) (sprintf "OWN  %3d" (Pokedex.ownCount  player))

    let renderDetail (fb: Framebuffer) (num: int) =
        match Dex.byNum |> Map.tryFind num with
        | None -> ()
        | Some entry ->
            let isOwned = player.DexOwn.Contains num

            // Row 1: dex number + species name.
            draw fb (BoxLeft + 2) (BoxTop + 1)
                (sprintf "#%03d %-9s" entry.Num (truncate 9 entry.Name))

            if isOwned then
                // Row 3: category (e.g. "SEED POKéMON").
                draw fb (BoxLeft + 1) (BoxTop + 3)
                    (truncate 18 (sprintf "%s POKéMON" (truncate 9 entry.Category)))

                // Rows 5-6: height and weight.
                draw fb (BoxLeft + 2) (BoxTop + 5)
                    (sprintf "HT %-8s" (Pokedex.heightLabel entry.HeightDm))
                draw fb (BoxLeft + 2) (BoxTop + 6)
                    (sprintf "WT %-8s" (Pokedex.weightLabel entry.WeightHg))

                // Rows 8-11: description (up to 4 split lines).
                let lines = Pokedex.descLines entry.Description
                lines
                |> List.truncate 4
                |> List.iteri (fun i line ->
                    draw fb (BoxLeft + 1) (BoxTop + 8 + i) (truncate 18 line))
            else
                // Seen-not-owned: name already shown; show placeholder stats.
                draw fb (BoxLeft + 2) (BoxTop + 3) "???"

            draw fb (BoxLeft + 2) (BoxTop + 16) "B:BACK"

    // ── Public API (for unit tests) ───────────────────────────────────────────

    /// Current sub-mode (DexList or DexDetail).
    member _.Mode = mode

    /// 0-based cursor index within the 251-entry list.
    member _.Cursor = menu.Cursor

    /// Underlying MenuList state (for scroll / window invariant tests).
    member _.MenuState = menu

    // ── Scene interface ───────────────────────────────────────────────────────

    interface Scene with

        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)
            match mode with

            | DexList ->
                if edges.Up then
                    menu <- MenuList.moveUp menu
                    Stay
                elif edges.Down then
                    menu <- MenuList.moveDown menu
                    Stay
                elif edges.A then
                    let num = menu.Cursor + 1   // 1-based dex number
                    if player.DexOwn.Contains num || player.DexSeen.Contains num then
                        mode <- DexDetail num
                        Stay
                    else
                        Stay   // unseen: A does nothing
                elif edges.B then
                    Pop
                else
                    Stay

            | DexDetail _ ->
                if edges.B then
                    mode <- DexList
                    Stay
                else
                    Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawBox fb content.Font palette BoxLeft BoxTop BoxWidth BoxHeight
            match mode with
            | DexList       -> renderList fb
            | DexDetail num -> renderDetail fb num
