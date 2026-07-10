namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Battle
open PokeGold.Game.Player
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// Derived stats for a persistent PartyMon — exposed for unit testing.
type SummaryStats =
    { Atk: int
      Def: int
      Spd: int
      SpA: int
      SpD: int
      MaxHp: int }

/// Pure helpers for the summary scene — extracted so tests can call them directly.
module Summary =

    let private speciesOf (speciesId: int) =
        Species.all |> Map.tryPick (fun _ s -> if s.Dex = speciesId then Some s else None)

    /// Derive all stats from base stats, packed DVs, and five stat-exp words.
    let statsOf (mon: PartyMon) : SummaryStats =
        match speciesOf mon.SpeciesId with
        | Some s ->
            let stats = BattleMon.calculateStats s mon.Level mon.Dvs mon.StatExp
            { Atk = stats.Attack
              Def = stats.Defense
              Spd = stats.Speed
              SpA = stats.SpAttack
              SpD = stats.SpDefense
              MaxHp = stats.MaxHp }
        | None ->
            { Atk = 5; Def = 5; Spd = 5; SpA = 5; SpD = 5; MaxHp = 1 }

/// A three-page Pokémon summary Scene (Info / Stats / Moves).
///
///   Page 0 – Info:  dex no., species name, nickname, level, OT/ID, held item, status.
///   Page 1 – Stats: HP (cur/max), Atk/Def/Spd/SpA/SpD (derived), Exp.
///   Page 2 – Moves: up to 4 moves with name and current/max PP.
///
/// Left / Right navigate between pages (wrapping); B pops the scene.
///
/// // later: type icons + front sprite
type SummaryScene(content: Content, mon: PartyMon) =

    let mutable pageIndex = 0
    let pageCount         = 3
    let input             = EdgeDetector()
    let palette           = TextRenderer.palette

    // Layout constants (20 × 18 tiles = full GBC screen).
    [<Literal>]
    let BoxLeft   = 0
    [<Literal>]
    let BoxTop    = 0
    [<Literal>]
    let BoxWidth  = 20
    [<Literal>]
    let BoxHeight = 18

    // ── Cached derived values ─────────────────────────────────────────────────

    let stats = Summary.statsOf mon

    let dexEntry = Dex.byNum |> Map.tryFind mon.SpeciesId

    let speciesName =
        dexEntry |> Option.map (fun e -> e.Name) |> Option.defaultValue (string mon.SpeciesId)

    let dexNumStr =
        dexEntry |> Option.map (fun e -> sprintf "#%03d" e.Num) |> Option.defaultValue "#???"

    let heldItemName =
        mon.HeldItem
        |> Option.bind  (fun id -> Items.byId |> Map.tryFind id |> Option.map (fun d -> d.Name))
        |> Option.orElse (mon.HeldItem)
        |> Option.defaultValue "None"

    // ── Rendering helpers ─────────────────────────────────────────────────────

    let draw (fb: Framebuffer) (col: int) (row: int) (s: string) =
        WindowRenderer.drawString fb content.Font palette col row s

    let truncate (n: int) (s: string) =
        if s.Length <= n then s else s.[..n-1]

    let renderFrame (fb: Framebuffer) =
        WindowRenderer.drawBox fb content.Font palette BoxLeft BoxTop BoxWidth BoxHeight

    let renderPageIndicator (fb: Framebuffer) =
        let indicator = sprintf "%d/%d" (pageIndex + 1) pageCount
        draw fb (BoxLeft + BoxWidth - 1 - indicator.Length) (BoxTop + BoxHeight - 1) indicator

    let renderInfoPage (fb: Framebuffer) =
        let col = BoxLeft + 2
        draw fb col (BoxTop + 1) (sprintf "%-5s %-10s" dexNumStr (truncate 10 speciesName))
        draw fb col (BoxTop + 2) (sprintf "%-10s" (truncate 10 mon.Nickname))
        draw fb col (BoxTop + 3) (sprintf "Lv.%-3d" mon.Level)
        draw fb col (BoxTop + 4) (sprintf "OT: %-7s" (truncate 7 mon.OtName))
        draw fb col (BoxTop + 5) (sprintf "ID: %-5d" mon.OtId)
        draw fb col (BoxTop + 6) (sprintf "Item: %-8s" (truncate 8 heldItemName))
        draw fb col (BoxTop + 7) (sprintf "Stat: %-3s" (if mon.Status = "" then "OK" else truncate 3 mon.Status))

    let renderStatsPage (fb: Framebuffer) =
        let col = BoxLeft + 2
        draw fb col (BoxTop + 1) (sprintf "HP  %3d/%-3d" mon.Hp stats.MaxHp)
        draw fb col (BoxTop + 2) (sprintf "Atk %-5d" stats.Atk)
        draw fb col (BoxTop + 3) (sprintf "Def %-5d" stats.Def)
        draw fb col (BoxTop + 4) (sprintf "Spd %-5d" stats.Spd)
        draw fb col (BoxTop + 5) (sprintf "SpA %-5d" stats.SpA)
        draw fb col (BoxTop + 6) (sprintf "SpD %-5d" stats.SpD)
        draw fb col (BoxTop + 7) (sprintf "Exp %-7d" mon.Exp)

    let renderMovesPage (fb: Framebuffer) =
        let col = BoxLeft + 2
        draw fb col (BoxTop + 1) "MOVES"
        if mon.Moves.IsEmpty then
            draw fb col (BoxTop + 3) "(no moves)"
        else
            mon.Moves
            |> List.truncate 4
            |> List.iteri (fun i (moveId, curPp) ->
                let row = BoxTop + 3 + i
                // Try lookup by 1-based GSC move index first; fall back to constant-name string.
                let moveOpt =
                    Moves.tryByIndex moveId
                    |> Option.orElse (Moves.all |> Map.tryFind (string moveId))
                match moveOpt with
                | Some m ->
                    draw fb col row (sprintf "%-13s %2d/%-2d" (truncate 13 m.Name) curPp m.Pp)
                | None ->
                    draw fb col row (sprintf "Move #%-4d  %2d" moveId curPp))

    // ── Public API ────────────────────────────────────────────────────────────

    /// Current page index (0 = Info, 1 = Stats, 2 = Moves).
    member _.Page = pageIndex

    // ── Scene interface ───────────────────────────────────────────────────────

    interface Scene with

        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)
            if edges.Right then
                pageIndex <- (pageIndex + 1) % pageCount
                Stay
            elif edges.Left then
                pageIndex <- (pageIndex + pageCount - 1) % pageCount
                Stay
            elif edges.B then
                Pop
            else
                Stay

        member _.Render(fb: Framebuffer) =
            renderFrame fb
            renderPageIndicator fb
            match pageIndex with
            | 0 -> renderInfoPage fb
            | 1 -> renderStatsPage fb
            | _ -> renderMovesPage fb
