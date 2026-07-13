namespace PokeGold.DataGen

open System.Text
open PokeGold.Game.Overworld.Script

/// Renders the parsed `GeneratedMap` values as compilable F# literal source. The
/// emitted module lives in `PokeGold.Game.Data` and constructs the DUs/records
/// from `PokeGold.Game.Overworld.Script` (defined in the shared `PokeGold.MapData`
/// project, compiled into the game). Each map is its own top-level `let` so the F#
/// compiler sees many small initialisers rather than one giant expression.
module EmitMaps =

    /// An escaped F# string literal.
    let private str (s: string) : string =
        let e = s.Replace("\\", "\\\\").Replace("\"", "\\\"")
        "\"" + e + "\""

    let private strOpt (o: string option) : string =
        match o with
        | Some s -> sprintf "Some %s" (str s)
        | None -> "None"

    let private intOpt (o: int option) : string =
        match o with
        | Some i -> sprintf "Some %d" i
        | None -> "None"

    /// One script command rendered as its DU constructor.
    let private cmd (c: ScriptCommand) : string =
        match c with
        | Scall t -> sprintf "Scall %s" (str t)
        | Sjump t -> sprintf "Sjump %s" (str t)
        | Jumpstd t -> sprintf "Jumpstd %s" (str t)
        | Callstd t -> sprintf "Callstd %s" (str t)
        | Iffalse t -> sprintf "Iffalse %s" (str t)
        | Iftrue t -> sprintf "Iftrue %s" (str t)
        | Ifequal (v, t) -> sprintf "Ifequal(%d, %s)" v (str t)
        | Ifnotequal (v, t) -> sprintf "Ifnotequal(%d, %s)" v (str t)
        | Ifgreater (v, t) -> sprintf "Ifgreater(%d, %s)" v (str t)
        | Ifless (v, t) -> sprintf "Ifless(%d, %s)" v (str t)
        | Setval v -> sprintf "Setval %d" v
        | Addval v -> sprintf "Addval %d" v
        | Readvar v -> sprintf "Readvar %s" (str v)
        | Writevar v -> sprintf "Writevar %s" (str v)
        | Loadvar(v, value) -> sprintf "Loadvar(%s, %d)" (str v) value
        | Loadmem(addr, value) -> sprintf "Loadmem(%s, %d)" (str addr) value
        | Readmem addr -> sprintf "Readmem %s" (str addr)
        | Writemem addr -> sprintf "Writemem %s" (str addr)
        | Random limit -> sprintf "Random %d" limit
        | Checkevent f -> sprintf "Checkevent %s" (str f)
        | Clearevent f -> sprintf "Clearevent %s" (str f)
        | Setevent f -> sprintf "Setevent %s" (str f)
        | Checkflag f -> sprintf "Checkflag %s" (str f)
        | Clearflag f -> sprintf "Clearflag %s" (str f)
        | Setflag f -> sprintf "Setflag %s" (str f)
        | Checkmapscene m -> sprintf "Checkmapscene %s" (str m)
        | Setmapscene (m, s) -> sprintf "Setmapscene(%s, %d)" (str m) s
        | Checkscene -> "Checkscene"
        | Setscene s -> sprintf "Setscene %d" s
        | Giveitem (it, q) -> sprintf "Giveitem(%s, %d)" (str it) q
        | Takeitem (it, q) -> sprintf "Takeitem(%s, %d)" (str it) q
        | Checkitem it -> sprintf "Checkitem %s" (str it)
        | Verbosegiveitem (it, q) -> sprintf "Verbosegiveitem(%s, %d)" (str it) q
        | Opentext -> "Opentext"
        | Closetext -> "Closetext"
        | Writetext t -> sprintf "Writetext %s" (str t)
        | Jumptext t -> sprintf "Jumptext %s" (str t)
        | Jumptextfaceplayer t -> sprintf "Jumptextfaceplayer %s" (str t)
        | Waitbutton -> "Waitbutton"
        | Promptbutton -> "Promptbutton"
        | Yesorno -> "Yesorno"
        | Loadmenu m -> sprintf "Loadmenu %s" (str m)
        | Verticalmenu -> "Verticalmenu"
        | Closewindow -> "Closewindow"
        | Pokepic s -> sprintf "Pokepic %s" (str s)
        | Closepokepic -> "Closepokepic"
        | TwoDMenu -> "TwoDMenu"
        | Itemnotify -> "Itemnotify"
        | Prompt -> "Prompt"
        | Elevator args -> sprintf "Elevator [%s]" (args |> List.map str |> String.concat "; ")
        | Loadwildmon (s, l) -> sprintf "Loadwildmon(%s, %d)" (str s) l
        | Givepoke (s, l, item) -> sprintf "Givepoke(%s, %d, %s)" (str s) l (strOpt item)
        | Checkpoke s -> sprintf "Checkpoke %s" (str s)
        | Giveegg(s, l) -> sprintf "Giveegg(%s, %d)" (str s) l
        | Catchtutorial -> "Catchtutorial"
        | Trade tradeId -> sprintf "Trade %s" (str tradeId)
        | Givepokemail args -> sprintf "Givepokemail [%s]" (args |> List.map str |> String.concat "; ")
        | Checkpokemail args -> sprintf "Checkpokemail [%s]" (args |> List.map str |> String.concat "; ")
        | Loadtrainer (g, i) -> sprintf "Loadtrainer(%s, %s)" (str g) (str i)
        | Startbattle -> "Startbattle"
        | Reloadmapafterbattle -> "Reloadmapafterbattle"
        | Winlosstext (w, l) -> sprintf "Winlosstext(%s, %s)" (str w) (str l)
        | Setlasttalked o -> sprintf "Setlasttalked %s" (str o)
        | Applymovement (o, m) -> sprintf "Applymovement(%s, %s)" (str o) (str m)
        | Faceplayer -> "Faceplayer"
        | Faceobject (a, b) -> sprintf "Faceobject(%s, %s)" (str a) (str b)
        | Disappear o -> sprintf "Disappear %s" (str o)
        | Appear o -> sprintf "Appear %s" (str o)
        | Turnobject (o, f) -> sprintf "Turnobject(%s, %s)" (str o) (str f)
        | Moveobject(o, x, y) -> sprintf "Moveobject(%s, %d, %d)" (str o) x y
        | Follow(leader, follower) -> sprintf "Follow(%s, %s)" (str leader) (str follower)
        | Stopfollow -> "Stopfollow"
        | Variablesprite(sprite, replacement) -> sprintf "Variablesprite(%s, %s)" (str sprite) (str replacement)
        | Writeobjectxy o -> sprintf "Writeobjectxy %s" (str o)
        | Pause frames -> sprintf "Pause %d" frames
        | Showemote(emote, obj, frames) -> sprintf "Showemote(%s, %s, %d)" (str emote) (str obj) frames
        | Earthquake frames -> sprintf "Earthquake(%s)" (intOpt frames)
        | Playmusic s -> sprintf "Playmusic %s" (str s)
        | Playsound s -> sprintf "Playsound %s" (str s)
        | Waitsfx -> "Waitsfx"
        | Cry s -> sprintf "Cry %s" (str s)
        | Warp (m, x, y) -> sprintf "Warp(%s, %d, %d)" (str m) x y
        | Warpfacing (f, m, x, y) -> sprintf "Warpfacing(%s, %s, %d, %d)" (str f) (str m) x y
        | Reloadmap -> "Reloadmap"
        | Refreshmap -> "Refreshmap"
        | Changeblock (x, y, blockId) -> sprintf "Changeblock(%d, %d, %d)" x y blockId
        | Doorstate(door, state) -> sprintf "Doorstate(%s, %s)" (intOpt door) (state |> strOpt)
        | Ugdoor args -> sprintf "Ugdoor [%s]" (args |> List.map str |> String.concat "; ")
        | Dontrestartmapmusic -> "Dontrestartmapmusic"
        | Playmapmusic -> "Playmapmusic"
        | Musicfadeout -> "Musicfadeout"
        | Newloadmap -> "Newloadmap"
        | Warpcheck -> "Warpcheck"
        | Blackoutmod m -> sprintf "Blackoutmod %s" (str m)
        | Reanchormap -> "Reanchormap"
        | End -> "End"
        | EndAll -> "EndAll"
        | Halloffame -> "Halloffame"
        | Credits -> "Credits"
        | Special n -> sprintf "Special %s" (str n)
        | Pokemart(mt, m) -> sprintf "Pokemart(%s, %s)" (str mt) (str m)
        | Addcellnum phone -> sprintf "Addcellnum %s" (str phone)
        | Checkcellnum phone -> sprintf "Checkcellnum %s" (str phone)
        | Checkphonecall -> "Checkphonecall"
        | Checkjustbattled -> "Checkjustbattled"
        | Askforphonenumber phone -> sprintf "Askforphonenumber %s" (str phone)
        | Checkmoney args -> sprintf "Checkmoney [%s]" (args |> List.map str |> String.concat "; ")
        | Takemoney args -> sprintf "Takemoney [%s]" (args |> List.map str |> String.concat "; ")
        | Givemoney args -> sprintf "Givemoney [%s]" (args |> List.map str |> String.concat "; ")
        | Checkcoins amount -> sprintf "Checkcoins(%s)" (intOpt amount)
        | Takecoins amount -> sprintf "Takecoins(%s)" (intOpt amount)
        | Givecoins amount -> sprintf "Givecoins(%s)" (intOpt amount)
        | Checkver -> "Checkver"
        | Checktime time -> sprintf "Checktime %s" (str time)
        | ConditionalEvent args -> sprintf "ConditionalEvent [%s]" (args |> List.map str |> String.concat "; ")
        | Endifjustbattled -> "Endifjustbattled"
        | Gettrainername(buffer, group, trainer) -> sprintf "Gettrainername(%s, %s, %s)" (str buffer) (str group) (str trainer)
        | Getitemname(buffer, item) -> sprintf "Getitemname(%s, %s)" (str buffer) (str item)
        | Getmonname(buffer, species) -> sprintf "Getmonname(%s, %s)" (str buffer) (str species)
        | Getstring(buffer, value) -> sprintf "Getstring(%s, %s)" (str buffer) (str value)
        | Getnum(buffer, var) -> sprintf "Getnum(%s, %s)" (str buffer) (str var)
        | Getcurlandmarkname buffer -> sprintf "Getcurlandmarkname %s" (str buffer)
        | TextRam value -> sprintf "TextRam %s" (str value)
        | Describedecoration args -> sprintf "Describedecoration [%s]" (args |> List.map str |> String.concat "; ")
        | Stonetable args -> sprintf "Stonetable [%s]" (args |> List.map str |> String.concat "; ")
        | Cmdqueue args -> sprintf "Cmdqueue [%s]" (args |> List.map str |> String.concat "; ")
        | Writecmdqueue args -> sprintf "Writecmdqueue [%s]" (args |> List.map str |> String.concat "; ")
        | MenuCoords args -> sprintf "MenuCoords [%s]" (args |> List.map str |> String.concat "; ")
        | Specialphonecall call -> sprintf "Specialphonecall %s" (str call)
        | TeleportFrom -> "TeleportFrom"
        | TreeShake -> "TreeShake"
        | Elevfloor args -> sprintf "Elevfloor [%s]" (args |> List.map str |> String.concat "; ")
        | Unsupported (n, args) ->
            let a = args |> List.map str |> String.concat "; "
            sprintf "Unsupported(%s, [%s])" (str n) a

    /// One movement command rendered as its DU constructor.
    let private moveCmd (c: MovementCmd) : string =
        match c with
        | MoveStep d -> sprintf "MoveStep %d" d
        | MoveBigStep d -> sprintf "MoveBigStep %d" d
        | MoveSlowStep d -> sprintf "MoveSlowStep %d" d
        | MoveTurnStep d -> sprintf "MoveTurnStep %d" d
        | MoveSlideStep d -> sprintf "MoveSlideStep %d" d
        | MoveJumpStep d -> sprintf "MoveJumpStep %d" d
        | MoveTurnHead d -> sprintf "MoveTurnHead %d" d
        | MoveStepSleep n -> sprintf "MoveStepSleep %d" n
        | MoveStepEnd -> "MoveStepEnd"
        | MoveUnsupported n -> sprintf "MoveUnsupported %s" (str n)

    /// A map's movement scripts rendered as a `Map<string, MovementCmd[]>` literal.
    let private movements (m: Map<string, MovementCmd[]>) : string =
        m
        |> Map.toArray
        |> Array.map (fun (k, cs) -> sprintf "(%s, [| %s |])" (str k) (cs |> Array.map moveCmd |> String.concat "; "))
        |> String.concat "; "

    /// `key, value` pairs of a string→string map, as F# tuple literals.
    let private pairs (m: Map<string, string>) : string =
        m
        |> Map.toArray
        |> Array.map (fun (k, v) -> sprintf "(%s, %s)" (str k) (str v))
        |> String.concat "; "

    let private intPairs (m: Map<string, int>) : string =
        m
        |> Map.toArray
        |> Array.map (fun (k, v) -> sprintf "(%s, %d)" (str k) v)
        |> String.concat "; "

    let private scriptProgram (p: ScriptProgram) : string =
        let cmds = p.Commands |> Array.map cmd |> String.concat "; "
        sprintf "{ Commands = [| %s |]; Labels = Map.ofArray [| %s |] }" cmds (intPairs p.Labels)

    let private warp (w: WarpEvent) : string =
        sprintf "{ X = %d; Y = %d; DestMap = %s; DestWarp = %d }" w.X w.Y (str w.DestMap) w.DestWarp

    let private coord (c: CoordEvent) : string =
        sprintf "{ X = %d; Y = %d; Scene = %s; Script = %s }" c.X c.Y (str c.Scene) (str c.Script)

    let private bg (b: BgEvent) : string =
        sprintf "{ X = %d; Y = %d; Kind = %s; Script = %s }" b.X b.Y (str b.Kind) (str b.Script)

    let private objectEvent (o: ObjectEvent) : string =
        sprintf
            "{ X = %d; Y = %d; Sprite = %s; Movement = %s; RadiusX = %d; RadiusY = %d; Hour1 = %d; Hour2 = %d; Palette = %s; Type = %s; Sight = %d; Script = %s; EventFlag = %s }"
            o.X o.Y (str o.Sprite) (str o.Movement) o.RadiusX o.RadiusY o.Hour1 o.Hour2 (str o.Palette) (str o.Type)
            o.Sight (str o.Script) (strOpt o.EventFlag)

    let private arr (render: 'a -> string) (xs: 'a[]) : string =
        xs |> Array.map render |> String.concat "; "

    let private callback (c: MapCallback) : string =
        sprintf "{ Kind = %s; Label = %s }" (str c.Kind) (str c.Label)

    let private mapEvents (e: MapEvents) : string =
        sprintf
            "{ Warps = [| %s |]; Coords = [| %s |]; Bgs = [| %s |]; Objects = [| %s |]; Scenes = [| %s |]; SceneLabels = [| %s |]; Callbacks = [| %s |] }"
            (arr warp e.Warps) (arr coord e.Coords) (arr bg e.Bgs) (arr objectEvent e.Objects)
            (e.Scenes |> Array.map str |> String.concat "; ")
            (e.SceneLabels |> Array.map str |> String.concat "; ")
            (arr callback e.Callbacks)

    let private connection (c: Connection) : string =
        sprintf "{ Direction = %s; Map = %s; MapConst = %s; Offset = %d }" (str c.Direction) (str c.Map) (str c.MapConst) c.Offset

    let private mapMeta (m: MapMeta) : string =
        sprintf
            "{ Name = %s; Const = %s; Group = %s; WidthBlocks = %d; HeightBlocks = %d; Tileset = %s; Environment = %s; Landmark = %s; Music = %s; Palette = %s; BorderBlock = %d; Blocks = %s; Connections = [| %s |] }"
            (str m.Name) (str m.Const) (str m.Group) m.WidthBlocks m.HeightBlocks (str m.Tileset) (str m.Environment)
            (str m.Landmark) (str m.Music) (str m.Palette) m.BorderBlock (str m.Blocks) (arr connection m.Connections)

    /// The full generated `Maps.Generated.fs` source.
    let render (maps: GeneratedMap list) (spawnPoints: (string * string * int * int) list) : string =
        let sb = StringBuilder()

        sb.Append("// <auto-generated>\n") |> ignore
        sb.Append("//   Produced by PokeGold.DataGen from the disassembly map tables.\n") |> ignore
        sb.Append("//   Do not edit by hand; regenerated on build. See engine-dotnet/tools/PokeGold.DataGen.\n") |> ignore
        sb.Append("// </auto-generated>\n\n") |> ignore
        sb.AppendLine("namespace PokeGold.Game.Data") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("open PokeGold.Game.Overworld.Script") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("/// Generated static data for every map: metadata, event tables, script") |> ignore
        sb.AppendLine("/// programs, and resolved text — baked from the .asm at build time.") |> ignore
        sb.AppendLine("module MapsData =") |> ignore
        sb.AppendLine() |> ignore

        for g in maps do
            sb.AppendLine(sprintf "    let private m_%s : GeneratedMap =" g.Meta.Name) |> ignore
            sb.AppendLine(sprintf "        { Meta = %s" (mapMeta g.Meta)) |> ignore
            sb.AppendLine(sprintf "          Events = %s" (mapEvents g.Events)) |> ignore
            sb.AppendLine(sprintf "          Script = %s" (scriptProgram g.Script)) |> ignore
            sb.AppendLine(sprintf "          Text = Map.ofArray [| %s |]" (pairs g.Text)) |> ignore
            sb.AppendLine(sprintf "          Movements = Map.ofArray [| %s |]" (movements g.Movements)) |> ignore
            sb.AppendLine(sprintf "          ObjectConsts = [| %s |] }" (g.ObjectConsts |> Array.map str |> String.concat "; ")) |> ignore
            sb.AppendLine() |> ignore

        sb.AppendLine("    /// Every map's static data, keyed by its PascalCase id (its maps/<id>.asm).") |> ignore
        sb.AppendLine("    let all : Map<string, GeneratedMap> =") |> ignore
        sb.AppendLine("        Map.ofArray [|") |> ignore
        for g in maps do
            sb.AppendLine(sprintf "            (%s, m_%s)" (str g.Meta.Name) g.Meta.Name) |> ignore
        sb.AppendLine("        |]") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("    /// The map with the given id, if it exists.") |> ignore
        sb.AppendLine("    let byName (name: string) : GeneratedMap option = Map.tryFind name all") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("    /// Source-defined whiteout destinations, keyed by map constant.") |> ignore
        sb.AppendLine("    let spawnPoints : Map<string, string * int * int> =") |> ignore
        sb.AppendLine("        Map.ofArray [|") |> ignore
        for mapConst, runtimeName, x, y in spawnPoints do
            sb.AppendLine(sprintf "            (%s, (%s, %d, %d))" (str mapConst) (str runtimeName) x y) |> ignore
        sb.AppendLine("        |]") |> ignore

        sb.ToString()

    /// The generated `StdScripts.Generated.fs` source: the shared standard-script
    /// program (`engine/events/std_scripts.asm`) plus its resolved text
    /// (`data/text/std_text.asm`), baked once for `jumpstd`/`callstd` to resolve
    /// against at runtime.
    let renderStdScripts (prog: ScriptProgram) (text: Map<string, string>) : string =
        let sb = StringBuilder()

        sb.Append("// <auto-generated>\n") |> ignore
        sb.Append("//   Produced by PokeGold.DataGen from engine/events/std_scripts.asm + data/text/std_text.asm.\n") |> ignore
        sb.Append("//   Do not edit by hand; regenerated on build. See engine-dotnet/tools/PokeGold.DataGen.\n") |> ignore
        sb.Append("// </auto-generated>\n\n") |> ignore
        sb.AppendLine("namespace PokeGold.Game.Data") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("open PokeGold.Game.Overworld.Script") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("/// Generated shared *standard* scripts (jumpstd/callstd targets) and their") |> ignore
        sb.AppendLine("/// resolved text, baked from the .asm at build time.") |> ignore
        sb.AppendLine("module StdScriptsData =") |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("    /// Every standard script in one program, addressed by label.") |> ignore
        sb.AppendLine(sprintf "    let program : ScriptProgram = %s" (scriptProgram prog)) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("    /// Standard-script text labels resolved to M5 token strings.") |> ignore
        sb.AppendLine(sprintf "    let text : Map<string, string> = Map.ofArray [| %s |]" (pairs text)) |> ignore

        sb.ToString()
