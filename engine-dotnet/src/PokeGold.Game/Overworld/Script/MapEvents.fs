namespace PokeGold.Game.Overworld.Script

open System
open System.Globalization

/// A warp tile: stepping (or facing-and-confirming) onto `(X, Y)` sends the player
/// to warp id `DestWarp` on map `DestMap`. (`def_warp_events` / `warp_event x, y,
/// destMap, destWarpId`.)
type WarpEvent =
    { X: int
      Y: int
      DestMap: string
      DestWarp: int }

/// A coordinate trigger: standing on `(X, Y)` while this map's scene id equals
/// `Scene` runs `Script` once. (`def_coord_events` / `coord_event x, y, scene,
/// script`.) `Scene` is a `SCENE_*` constant name (resolved against the map's
/// scene list at integration time).
type CoordEvent =
    { X: int
      Y: int
      Scene: string
      Script: string }

/// A background/sign event: pressing A while facing `(X, Y)` runs `Script`.
/// `Kind` is a `BGEVENT_*` constant (READ for a sign, ITEM for a hidden item,
/// etc.). (`def_bg_events` / `bg_event x, y, type, script`.)
type BgEvent =
    { X: int
      Y: int
      Kind: string
      Script: string }

/// An overworld object (NPC, item ball, immovable sprite). Position `(X, Y)` is in
/// the same logical map-tile space as the other events (the assembler's `+4`
/// border offset is applied at render time, not here). `EventFlag = None` means
/// "always present"; otherwise the object only appears while that `EVENT_*` flag
/// is set. (`def_object_events` / `object_event x, y, SPRITE, MOVEDATA, radX, radY,
/// h1, h2, palette, OBJECTTYPE, sight, script, eventFlag`.)
type ObjectEvent =
    { X: int
      Y: int
      Sprite: string
      Movement: string
      RadiusX: int
      RadiusY: int
      Hour1: int
      Hour2: int
      Palette: string
      Type: string
      Sight: int
      Script: string
      EventFlag: string option }

/// All four event tables parsed from a map's `.asm`. Mirrors the four
/// `def_*_events` blocks at the foot of every map file.
type MapEvents =
    { Warps: WarpEvent[]
      Coords: CoordEvent[]
      Bgs: BgEvent[]
      Objects: ObjectEvent[] }

/// Parses a map `.asm`'s `def_*_events` tables into [`MapEvents`](#). Reads the
/// disassembly source directly (like [`ScriptParser`](ScriptParser.fs)); operands
/// that are constants in source (`SPRITE_*`, `SCENE_*`, `BGEVENT_*`, map ids, event
/// flags, script labels) are kept as strings and resolved later, while the plain
/// numbers (coordinates, radii, hours, sight) are parsed to `int`.
module MapEventParser =

    /// Parse a RGBDS integer literal (`$hex`, `%binary`, `&octal`, or decimal,
    /// including a leading `-`).
    let private parseInt (s: string) : int =
        let t = s.Trim()

        if t.StartsWith "$" then Convert.ToInt32(t.Substring 1, 16)
        elif t.StartsWith "%" then Convert.ToInt32(t.Substring 1, 2)
        elif t.StartsWith "&" then Convert.ToInt32(t.Substring 1, 8)
        else Int32.Parse(t, CultureInfo.InvariantCulture)

    /// Parse an integer operand, defaulting to 0 if it is symbolic.
    let private intArg (s: string) : int =
        try parseInt s with _ -> 0

    /// Strip a trailing `; comment` and surrounding whitespace.
    let private stripComment (line: string) : string =
        let i = line.IndexOf ';'
        (if i >= 0 then line.Substring(0, i) else line).Trim()

    /// Split a body line into its mnemonic and comma-separated, trimmed args.
    let private splitLine (body: string) : string * string list =
        let ws = body.IndexOfAny([| ' '; '\t' |])

        if ws < 0 then
            body, []
        else
            let args =
                body.Substring(ws + 1).Split(',')
                |> Array.map (fun a -> a.Trim())
                |> Array.filter (fun a -> a <> "")
                |> Array.toList

            body.Substring(0, ws).Trim(), args

    /// An `eventFlag` operand: `-1` (always present) becomes `None`.
    let private flagOpt (s: string) : string option =
        if s = "-1" then None else Some s

    /// Parse a map `.asm`'s text into its four event tables. A line outside the
    /// `def_*_events` blocks (script code, text, headers) is ignored; only the
    /// event-table macros emit records.
    let parseText (text: string) : MapEvents =
        let warps = ResizeArray<WarpEvent>()
        let coords = ResizeArray<CoordEvent>()
        let bgs = ResizeArray<BgEvent>()
        let objects = ResizeArray<ObjectEvent>()

        for raw in text.Replace("\r\n", "\n").Split('\n') do
            let body = stripComment raw

            if body <> "" then
                let mn, a = splitLine body
                let arg n = List.tryItem n a |> Option.defaultValue ""
                let i n = intArg (arg n)

                match mn with
                | "warp_event" ->
                    warps.Add
                        { X = i 0
                          Y = i 1
                          DestMap = arg 2
                          DestWarp = i 3 }
                | "coord_event" ->
                    coords.Add
                        { X = i 0
                          Y = i 1
                          Scene = arg 2
                          Script = arg 3 }
                | "bg_event" ->
                    bgs.Add
                        { X = i 0
                          Y = i 1
                          Kind = arg 2
                          Script = arg 3 }
                | "object_event" ->
                    objects.Add
                        { X = i 0
                          Y = i 1
                          Sprite = arg 2
                          Movement = arg 3
                          RadiusX = i 4
                          RadiusY = i 5
                          Hour1 = i 6
                          Hour2 = i 7
                          Palette = arg 8
                          Type = arg 9
                          Sight = i 10
                          Script = arg 11
                          EventFlag = flagOpt (arg 12) }
                | _ -> ()

        { Warps = warps.ToArray()
          Coords = coords.ToArray()
          Bgs = bgs.ToArray()
          Objects = objects.ToArray() }

    /// Parse a map `.asm`'s event tables from a repo-relative path.
    let parseFile (relativePath: string) : MapEvents =
        parseText (PokeGold.Game.Core.Assets.readText relativePath)

/// Queries over a map's parsed [`MapEvents`](#): visibility (gated on the world's
/// event flags) and per-cell lookups the integration layer (M9.4) uses to decide
/// what a step or an A-press triggers.
module MapEvents =

    /// A map with no events (for maps whose `.asm` isn't wired up yet).
    let empty: MapEvents =
        { Warps = [||]
          Coords = [||]
          Bgs = [||]
          Objects = [||] }

    /// Is this object currently present, given the world's event flags? An object
    /// with no `EventFlag` is always present; otherwise it appears only while its
    /// flag is set.
    let objectVisible (world: World) (o: ObjectEvent) : bool =
        match o.EventFlag with
        | None -> true
        | Some flag -> World.hasEvent flag world

    /// The objects currently present in the world (visibility-filtered).
    let visibleObjects (world: World) (events: MapEvents) : ObjectEvent[] =
        events.Objects |> Array.filter (objectVisible world)

    /// The warp on cell `(x, y)`, if any.
    let warpAt (x: int) (y: int) (events: MapEvents) : WarpEvent option =
        events.Warps |> Array.tryFind (fun w -> w.X = x && w.Y = y)

    /// The coordinate trigger on cell `(x, y)`, if any.
    let coordAt (x: int) (y: int) (events: MapEvents) : CoordEvent option =
        events.Coords |> Array.tryFind (fun c -> c.X = x && c.Y = y)

    /// The sign/bg event on cell `(x, y)`, if any.
    let bgAt (x: int) (y: int) (events: MapEvents) : BgEvent option =
        events.Bgs |> Array.tryFind (fun b -> b.X = x && b.Y = y)

    /// The visible object standing on cell `(x, y)`, if any — used to resolve the
    /// NPC the player is facing when they press A.
    let objectAt (world: World) (x: int) (y: int) (events: MapEvents) : ObjectEvent option =
        visibleObjects world events |> Array.tryFind (fun o -> o.X = x && o.Y = y)
