namespace PokeGold.Game.Scenes

open System
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Render
open PokeGold.Game.Ui

type DexSearchState =
    { Type1: int
      Type2: int
      Cursor: int }

/// Sub-mode for the Pokédex scene.
type DexMode =
    | DexList
    | DexDetail of num: int
    | DexArea of num: int
    | DexSearch of DexSearchState
    | DexSearchResults of DexSearchState

/// Pure helpers for the Pokédex scene — extracted so tests can call them directly
/// without touching the framebuffer.
module Pokedex =

    /// Total dex entries (Generation I + II).
    let entryCount = 251

    /// Number of distinct species the player has seen.
    let seenCount (player: PlayerState) = player.DexSeen.Count

    /// Number of distinct species the player owns.
    let ownCount (player: PlayerState) = player.DexOwn.Count

    let private typeSearchOptions =
        [| ("----", None)
           ("NORMAL", Some 0)
           ("FIRE", Some 20)
           ("WATER", Some 21)
           ("GRASS", Some 22)
           ("ELECTRIC", Some 23)
           ("ICE", Some 25)
           ("FIGHTING", Some 1)
           ("POISON", Some 3)
           ("GROUND", Some 4)
           ("FLYING", Some 2)
           ("PSYCHIC", Some 24)
           ("BUG", Some 7)
           ("ROCK", Some 5)
           ("GHOST", Some 8)
           ("DRAGON", Some 26)
           ("DARK", Some 27)
           ("STEEL", Some 9) |]

    let searchTypeCount = typeSearchOptions.Length - 1

    let searchTypeLabel (index: int) : string =
        if index >= 0 && index < typeSearchOptions.Length then
            fst typeSearchOptions.[index]
        else
            fst typeSearchOptions.[0]

    let private searchTypeId (index: int) : int option =
        if index >= 0 && index < typeSearchOptions.Length then
            snd typeSearchOptions.[index]
        else
            None

    let nextSearchType (allowBlank: bool) (index: int) : int =
        let minIndex = if allowBlank then 0 else 1
        if index >= searchTypeCount then minIndex else index + 1

    let prevSearchType (allowBlank: bool) (index: int) : int =
        let minIndex = if allowBlank then 0 else 1
        if index <= minIndex then searchTypeCount else index - 1

    let private matchesType (typeId: int) (species: string) =
        match Species.all |> Map.tryFind species with
        | Some stats -> stats.Type1 = typeId || stats.Type2 = typeId
        | None -> false

    let searchResults (player: PlayerState) (type1: int) (type2: int) : int list =
        let requiredTypes =
            [ searchTypeId type1
              searchTypeId type2 ]
            |> List.choose id

        Dex.all
        |> Array.toList
        |> List.filter (fun entry ->
            player.DexOwn.Contains entry.Num
            && requiredTypes |> List.forall (fun typeId -> matchesType typeId entry.Name))
        |> List.map _.Num

    let private encounterSpecies (table: WildEncounterTable) =
        seq {
            yield! table.GrassMorn
            yield! table.GrassDay
            yield! table.GrassNite
            yield! table.Water
        }

    let areaLocations (species: string) : string list =
        WildEncounters.all
        |> Map.toList
        |> List.choose (fun (mapName, table) ->
            if encounterSpecies table |> Seq.exists (fun slot -> slot.Species = species) then
                Some mapName
            else
                None)
        |> List.distinct

    let areaLocationsForDexNum (num: int) : string list =
        match Dex.byNum |> Map.tryFind num with
        | Some entry -> areaLocations entry.Name
        | None -> []

    let mapLabel (mapName: string) =
        mapName.Replace("_", " ")

    let dexSpritePath (player: PlayerState) (num: int) : string =
        if not (player.DexSeen.Contains num || player.DexOwn.Contains num) then
            "gfx/pokedex/question_mark.png"
        else
            match Dex.byNum |> Map.tryFind num with
            | None -> "gfx/pokedex/question_mark.png"
            | Some entry ->
                let candidate = $"gfx/pokemon/{entry.Name.ToLowerInvariant()}/front_gold.png"
                if Assets.exists candidate then candidate else "gfx/pokedex/question_mark.png"

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
/// SEEN and OWN running counters, detail/area screens, and type search.
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
///   Left/Right – choose PAGE/AREA/CRY/PRNT action.
///   A          – AREA opens wild encounter nests; other actions remain inert.
///   B          – return to the screen that opened detail.
type PokedexScene(content: Content, player: PlayerState) =

    let mutable mode : DexMode = DexList
    let mutable menu = MenuList.create Pokedex.entryCount 7 false
    let mutable detailReturnMode : DexMode = DexList
    let mutable detailAction = 0
    let mutable searchResults : int list = []
    let mutable resultsMenu = MenuList.create 0 4 false
    let mutable searchMessage : string option = None
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

    let wrapIndex count value =
        if value < 0 then count - 1
        elif value >= count then 0
        else value

    let defaultSearchState =
        { Type1 = 1
          Type2 = 0
          Cursor = 0 }

    let beginSearch (state: DexSearchState) =
        let results = Pokedex.searchResults player state.Type1 state.Type2
        if results.IsEmpty then
            searchMessage <- Some "The specified type was not found."
            mode <- DexSearch state
        else
            searchMessage <- None
            searchResults <- results
            resultsMenu <- MenuList.create results.Length 4 false
            mode <- DexSearchResults state

    let drawDexPic (fb: Framebuffer) (num: int) =
        let pic = Image.loadTilesWithSize (Pokedex.dexSpritePath player num)
        let tilesWide = pic.Width / 8
        let picLeft = (BoxLeft + 13) * 8 + ((7 * 8 - pic.Width) / 2)
        let picTop = (BoxTop + 2) * 8 + ((7 * 8 - pic.Height) / 2)

        pic.Tiles
        |> Array.iteri (fun i tile ->
            let x = picLeft + (i % tilesWide) * 8
            let y = picTop + (i / tilesWide) * 8
            Graphics.drawTile fb palette x y tile)

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
            drawDexPic fb num

            // Row 1: dex number + species name.
            draw fb (BoxLeft + 2) (BoxTop + 1)
                (sprintf "#%03d %-9s" entry.Num (truncate 9 entry.Name))

            if isOwned then
                // Row 3: category (e.g. "SEED POKéMON").
                draw fb (BoxLeft + 1) (BoxTop + 3)
                    (truncate 11 (sprintf "%s POKéMON" (truncate 9 entry.Category)))

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
                    draw fb (BoxLeft + 1) (BoxTop + 9 + i) (truncate 18 line))
            else
                // Seen-not-owned: name already shown; show placeholder stats.
                draw fb (BoxLeft + 2) (BoxTop + 3) "???"

            let actions = [| "PAGE"; "AREA"; "CRY"; "PRNT" |]
            let mutable col = BoxLeft + 1
            actions
            |> Array.iteri (fun i label ->
                draw fb col (BoxTop + 16) (if i = detailAction then ">" + label else " " + label)
                col <- col + label.Length + 1)

    let renderSearch (fb: Framebuffer) (state: DexSearchState) =
        draw fb (BoxLeft + 2) (BoxTop + 1) "SEARCH TYPE"
        let rows = [| 4; 6; 13; 15 |]
        let labels =
            [| sprintf "TYPE1  %-8s" (Pokedex.searchTypeLabel state.Type1)
               sprintf "TYPE2  %-8s" (Pokedex.searchTypeLabel state.Type2)
               "BEGIN SEARCH"
               "CANCEL" |]

        labels
        |> Array.iteri (fun i label ->
            let cursor = if state.Cursor = i then ">" else " "
            draw fb (BoxLeft + 2) (BoxTop + rows.[i]) (cursor + label))

        match searchMessage with
        | Some msg ->
            draw fb (BoxLeft + 1) (BoxTop + 10) (truncate 18 msg)
        | None -> ()

    let renderSearchResults (fb: Framebuffer) (state: DexSearchState) =
        draw fb (BoxLeft + 1) (BoxTop + 1)
            (truncate 18 (sprintf "%s/%s"
                (Pokedex.searchTypeLabel state.Type1)
                (Pokedex.searchTypeLabel state.Type2)))

        let labels =
            [| for i in 0 .. resultsMenu.Visible - 1 do
                   let idx = resultsMenu.Top + i
                   if idx < searchResults.Length then
                       yield Pokedex.rowLabel player searchResults.[idx] |]

        WindowRenderer.drawList
            fb content.Font palette
            ListLeft (BoxTop + 4)
            (labels |> Array.toSeq)
            (resultsMenu.Cursor - resultsMenu.Top)

        draw fb (BoxLeft + 2) (BoxTop + 14) (sprintf "%d found" searchResults.Length)
        draw fb (BoxLeft + 2) (BoxTop + 16) "A:DATA B:BACK"

    let renderArea (fb: Framebuffer) (num: int) =
        match Dex.byNum |> Map.tryFind num with
        | None -> ()
        | Some entry ->
            draw fb (BoxLeft + 2) (BoxTop + 1) (truncate 16 (entry.Name + "'S NEST"))
            let locations = Pokedex.areaLocations entry.Name

            if locations.IsEmpty then
                draw fb (BoxLeft + 2) (BoxTop + 4) "AREA UNKNOWN"
            else
                locations
                |> List.truncate 10
                |> List.iteri (fun i mapName ->
                    draw fb (BoxLeft + 1) (BoxTop + 3 + i) (truncate 18 (Pokedex.mapLabel mapName)))

            draw fb (BoxLeft + 2) (BoxTop + 16) "A/B:BACK"

    // ── Public API (for unit tests) ───────────────────────────────────────────

    /// Current sub-mode (DexList or DexDetail).
    member _.Mode = mode

    /// 0-based cursor index within the 251-entry list.
    member _.Cursor = menu.Cursor

    /// Underlying MenuList state (for scroll / window invariant tests).
    member _.MenuState = menu

    member _.DetailAction = detailAction

    member _.SearchResults = searchResults

    member _.ResultsMenuState = resultsMenu

    member _.SearchMessage = searchMessage

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
                elif edges.Start then
                    searchMessage <- None
                    mode <- DexSearch defaultSearchState
                    Stay
                elif edges.A then
                    let num = menu.Cursor + 1   // 1-based dex number
                    if player.DexOwn.Contains num || player.DexSeen.Contains num then
                        detailReturnMode <- DexList
                        detailAction <- 0
                        mode <- DexDetail num
                        Stay
                    else
                        Stay   // unseen: A does nothing
                elif edges.B then
                    Pop
                else
                    Stay

            | DexDetail num ->
                if edges.B then
                    mode <- detailReturnMode
                    Stay
                elif edges.Left then
                    detailAction <- wrapIndex 4 (detailAction - 1)
                    Stay
                elif edges.Right then
                    detailAction <- wrapIndex 4 (detailAction + 1)
                    Stay
                elif edges.A then
                    if detailAction = 1 then
                        mode <- DexArea num
                    Stay
                else
                    Stay

            | DexArea num ->
                if edges.A || edges.B then
                    mode <- DexDetail num
                    Stay
                else
                    Stay

            | DexSearch state ->
                if edges.B || edges.Start then
                    mode <- DexList
                    searchMessage <- None
                    Stay
                elif edges.Up then
                    mode <- DexSearch { state with Cursor = wrapIndex 4 (state.Cursor - 1) }
                    searchMessage <- None
                    Stay
                elif edges.Down then
                    mode <- DexSearch { state with Cursor = wrapIndex 4 (state.Cursor + 1) }
                    searchMessage <- None
                    Stay
                elif edges.Left || edges.Right then
                    let step allowBlank value =
                        if edges.Left then Pokedex.prevSearchType allowBlank value
                        else Pokedex.nextSearchType allowBlank value

                    match state.Cursor with
                    | 0 ->
                        mode <- DexSearch { state with Type1 = step false state.Type1 }
                        searchMessage <- None
                    | 1 ->
                        mode <- DexSearch { state with Type2 = step true state.Type2 }
                        searchMessage <- None
                    | _ -> ()
                    Stay
                elif edges.A then
                    match state.Cursor with
                    | 0 ->
                        mode <- DexSearch { state with Type1 = Pokedex.nextSearchType false state.Type1 }
                        searchMessage <- None
                    | 1 ->
                        mode <- DexSearch { state with Type2 = Pokedex.nextSearchType true state.Type2 }
                        searchMessage <- None
                    | 2 -> beginSearch state
                    | _ ->
                        mode <- DexList
                        searchMessage <- None
                    Stay
                else
                    Stay

            | DexSearchResults state ->
                if edges.B then
                    mode <- DexSearch state
                    Stay
                elif edges.Up then
                    resultsMenu <- MenuList.moveUp resultsMenu
                    Stay
                elif edges.Down then
                    resultsMenu <- MenuList.moveDown resultsMenu
                    Stay
                elif edges.A && not searchResults.IsEmpty then
                    let num = searchResults.[resultsMenu.Cursor]
                    if player.DexSeen.Contains num then
                        detailReturnMode <- DexSearchResults state
                        detailAction <- 0
                        mode <- DexDetail num
                    Stay
                else
                    Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawBox fb content.Font palette BoxLeft BoxTop BoxWidth BoxHeight
            match mode with
            | DexList                -> renderList fb
            | DexDetail num          -> renderDetail fb num
            | DexArea num            -> renderArea fb num
            | DexSearch state        -> renderSearch fb state
            | DexSearchResults state -> renderSearchResults fb state
