namespace PokeGold.Game.Overworld.Script

open System
open System.Globalization

/// A connection from one map to an adjacent map. (`connection dir, MapName,
/// MAP_CONST, offset` in `data/maps/attributes.asm`.) `Offset` is the alignment
/// offset along the shared edge (x for east/west, y for north/south); the full
/// border-streaming geometry is derived from it at integration time (M10.3).
type Connection =
    { Direction: string
      Map: string
      MapConst: string
      Offset: int }

/// Static, per-map metadata baked from the disassembly's map tables
/// (`constants/map_constants.asm` for dimensions/group, `data/maps/maps.asm` for
/// tileset/environment/music/palette, `data/maps/attributes.asm` for the
/// name↔const link, border block and connections). `Name` is the map's PascalCase
/// id (its `maps/<Name>.asm` file and the runtime `MapId`); `Const` is the
/// `MAP_*` constant warps target.
type MapMeta =
    { Name: string
      Const: string
      Group: string
      WidthBlocks: int
      HeightBlocks: int
      Tileset: string
      Environment: string
      Landmark: string
      Music: string
      Palette: string
      BorderBlock: int
      Connections: Connection[] }

/// Build-time parsers for the three map metadata tables. All pure (text in,
/// values out); `PokeGold.DataGen` feeds them the `.asm` contents and bakes the
/// joined result. The runtime never calls these.
module MapMetaParser =

    let private parseInt (s: string) : int =
        let t = s.Trim()

        if t.StartsWith "$" then Convert.ToInt32(t.Substring 1, 16)
        elif t.StartsWith "%" then Convert.ToInt32(t.Substring 1, 2)
        elif t.StartsWith "&" then Convert.ToInt32(t.Substring 1, 8)
        else Int32.Parse(t, CultureInfo.InvariantCulture)

    let private intArg (s: string) : int =
        try parseInt s with _ -> 0

    let private stripComment (line: string) : string =
        let i = line.IndexOf ';'
        (if i >= 0 then line.Substring(0, i) else line).Trim()

    /// Split a body line into mnemonic + comma-separated trimmed args.
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

    /// `constants/map_constants.asm` → `MAP_CONST -> (group, widthBlocks,
    /// heightBlocks)`. Tracks the enclosing `newgroup` for each `map_const`.
    let parseConstants (text: string) : Map<string, string * int * int> =
        let result = System.Collections.Generic.Dictionary<string, string * int * int>()
        let mutable group = ""

        for raw in text.Replace("\r\n", "\n").Split('\n') do
            let body = stripComment raw

            if body <> "" then
                let mn, a = splitLine body
                let arg n = List.tryItem n a |> Option.defaultValue ""

                match mn with
                | "newgroup" -> group <- arg 0
                | "map_const" ->
                    let name = arg 0
                    if name <> "" then
                        result.[name] <- (group, intArg (arg 1), intArg (arg 2))
                | _ -> ()

        result |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

    /// `data/maps/maps.asm` → `PascalName -> (tileset, environment, landmark,
    /// music, palette)`. The `map` macro args are
    /// `name, tileset, env, landmark, music, phoneFlag, palette, fishgroup`.
    let parseMaps (text: string) : Map<string, string * string * string * string * string> =
        let result = System.Collections.Generic.Dictionary<string, string * string * string * string * string>()

        for raw in text.Replace("\r\n", "\n").Split('\n') do
            let body = stripComment raw

            if body <> "" then
                let mn, a = splitLine body
                let arg n = List.tryItem n a |> Option.defaultValue ""

                if mn = "map" then
                    let name = arg 0
                    if name <> "" then
                        result.[name] <- (arg 1, arg 2, arg 3, arg 4, arg 6)

        result |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

    /// `data/maps/attributes.asm` → one record per `map_attributes` line, with the
    /// `connection` lines that follow it gathered into `Connections`. Carries the
    /// authoritative PascalName↔Const link and the border block.
    let parseAttributes (text: string) : (string * string * int * Connection list) list =
        // (name, const, border, reversed-connections)
        let results = ResizeArray<string * string * int * ResizeArray<Connection>>()

        for raw in text.Replace("\r\n", "\n").Split('\n') do
            let body = stripComment raw

            if body <> "" then
                let mn, a = splitLine body
                let arg n = List.tryItem n a |> Option.defaultValue ""

                match mn with
                | "map_attributes" -> results.Add(arg 0, arg 1, intArg (arg 2), ResizeArray<Connection>())
                | "connection" when results.Count > 0 ->
                    let _, _, _, conns = results.[results.Count - 1]
                    conns.Add
                        { Direction = arg 0
                          Map = arg 1
                          MapConst = arg 2
                          Offset = intArg (arg 3) }
                | _ -> ()

        [ for (name, c, border, conns) in results -> name, c, border, List.ofSeq conns ]

    /// Join the three tables into one `MapMeta` per map (keyed by the attributes
    /// table, which lists every real map). A map missing from `maps.asm` or
    /// `map_constants.asm` falls back to empty/zero fields rather than failing, so
    /// generation is robust to partial tables.
    let join (constants: string) (maps: string) (attributes: string) : MapMeta list =
        let consts = parseConstants constants
        let mapTable = parseMaps maps

        [ for (name, c, border, conns) in parseAttributes attributes do
              let group, wb, hb =
                  match consts.TryFind c with
                  | Some v -> v
                  | None -> "", 0, 0

              let tileset, env, landmark, music, palette =
                  match mapTable.TryFind name with
                  | Some v -> v
                  | None -> "", "", "", "", ""

              yield
                  { Name = name
                    Const = c
                    Group = group
                    WidthBlocks = wb
                    HeightBlocks = hb
                    Tileset = tileset
                    Environment = env
                    Landmark = landmark
                    Music = music
                    Palette = palette
                    BorderBlock = border
                    Connections = Array.ofList conns } ]
