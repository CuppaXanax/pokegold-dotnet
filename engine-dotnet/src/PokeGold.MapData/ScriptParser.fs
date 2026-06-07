namespace PokeGold.Game.Overworld.Script

open System
open System.Globalization

/// Parses a map's `.asm` into a `ScriptProgram` (command stream + label map).
///
/// Like the audio `SongParser`, this reads the disassembly source directly
/// ("the source is the spec") rather than assembled bytes. A map file mixes
/// script code, text data, and the `def_*_events` tables; this parser emits a
/// command only for recognised script mnemonics and otherwise treats a line as
/// non-emitting (labels, text/data directives, event-table macros, headers).
/// Recognised opcodes outside the M9 slice become `Unsupported`, so a whole file
/// parses without loss and coverage can be measured.
///
/// Label handling mirrors RGBDS: a line ending in `:` is a label; a label
/// beginning with `.` is local and is qualified with the most recent global
/// label (`AScript` + `.Foo` -> `AScript.Foo`). Jump/call/branch targets are
/// qualified the same way before being stored, so the VM can resolve them
/// against `Labels` regardless of where they were written.
module ScriptParser =

    /// Parse a RGBDS integer literal (`$hex`, `%binary`, `&octal`, or decimal).
    let private parseInt (s: string) : int =
        let t = s.Trim()

        if t.StartsWith "$" then Convert.ToInt32(t.Substring 1, 16)
        elif t.StartsWith "%" then Convert.ToInt32(t.Substring 1, 2)
        elif t.StartsWith "&" then Convert.ToInt32(t.Substring 1, 8)
        else Int32.Parse(t, CultureInfo.InvariantCulture)

    /// Parse an integer operand, defaulting to 0 if it is symbolic (a constant we
    /// don't resolve here). Numeric script operands (values, counts, coordinates)
    /// are concrete in source, so this only falls back for genuinely odd input.
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
            let mn = body.Substring(0, ws).Trim()

            let args =
                body.Substring(ws + 1).Split(',')
                |> Array.map (fun a -> a.Trim())
                |> Array.filter (fun a -> a <> "")
                |> Array.toList

            mn, args

    /// Qualify a local label/target (`.Foo`) with the enclosing global label.
    let private qualify (lastGlobal: string) (name: string) : string =
        if name.StartsWith "." then lastGlobal + name else name

    /// Translate one mnemonic + args into a command, with targets/labels already
    /// qualified by `g` (the current global label). Returns `None` for lines that
    /// don't emit a script command (text/data/event-table/header directives).
    let private toCommand (g: string) (mn: string) (args: string list) : ScriptCommand option =
        let arg n = List.tryItem n args |> Option.defaultValue ""
        let lbl n = qualify g (arg n)
        let i n = intArg (arg n)

        match mn with
        // control flow
        | "scall" | "farscall" -> Some(Scall(lbl 0))
        | "sjump" | "farsjump" | "sdefer" -> Some(Sjump(lbl 0))
        | "jumpstd" -> Some(Jumpstd(arg 0))
        | "callstd" -> Some(Callstd(arg 0))
        | "iffalse" -> Some(Iffalse(lbl 0))
        | "iftrue" -> Some(Iftrue(lbl 0))
        | "ifequal" -> Some(Ifequal(i 0, lbl 1))
        | "ifnotequal" -> Some(Ifnotequal(i 0, lbl 1))
        | "ifgreater" -> Some(Ifgreater(i 0, lbl 1))
        | "ifless" -> Some(Ifless(i 0, lbl 1))
        // variables
        | "setval" -> Some(Setval(i 0))
        | "addval" -> Some(Addval(i 0))
        | "readvar" -> Some(Readvar(arg 0))
        | "writevar" -> Some(Writevar(arg 0))
        | "loadvar" -> Some(Unsupported("loadvar", args))
        | "loadmem" -> Some(Unsupported("loadmem", args))
        | "readmem" -> Some(Unsupported("readmem", args))
        | "writemem" -> Some(Unsupported("writemem", args))
        | "random" -> Some(Unsupported("random", args))
        | "loadmenu" -> Some(Unsupported("loadmenu", args))
        | "verticalmenu" -> Some(Unsupported("verticalmenu", args))
        | "closewindow" -> Some(Unsupported("closewindow", args))
        | "pokepic" -> Some(Unsupported("pokepic", args))
        | "closepokepic" -> Some(Unsupported("closepokepic", args))
        | "_2dmenu" -> Some(Unsupported("_2dmenu", args))
        | "itemnotify" -> Some(Unsupported("itemnotify", args))
        | "prompt" -> Some(Unsupported("prompt", args))
        | "elevator" -> Some(Unsupported("elevator", args))
        | "giveegg" -> Some(Unsupported("giveegg", args))
        | "catchtutorial" -> Some(Unsupported("catchtutorial", args))
        | "givepokemail" -> Some(Unsupported("givepokemail", args))
        | "checkpokemail" -> Some(Unsupported("checkpokemail", args))
        | "addcellnum" -> Some(Unsupported("addcellnum", args))
        | "describedecoration" -> Some(Unsupported("describedecoration", args))
        | "stonetable" -> Some(Unsupported("stonetable", args))
        | "cmdqueue" -> Some(Unsupported("cmdqueue", args))
        | "writecmdqueue" -> Some(Unsupported("writecmdqueue", args))
        | "conditional_event" -> Some(Unsupported("conditional_event", args))
        | "endifjustbattled" -> Some(Unsupported("endifjustbattled", args))
        | "checkmoney" -> Some(Unsupported("checkmoney", args))
        | "takemoney" -> Some(Unsupported("takemoney", args))
        | "givemoney" -> Some(Unsupported("givemoney", args))
        | "checkcoins" -> Some(Unsupported("checkcoins", args))
        | "takecoins" -> Some(Unsupported("takecoins", args))
        | "givecoins" -> Some(Unsupported("givecoins", args))
        | "checkver" -> Some(Unsupported("checkver", args))
        | "checktime" -> Some(Unsupported("checktime", args))
        // event flags
        | "checkevent" -> Some(Checkevent(arg 0))
        | "clearevent" -> Some(Clearevent(arg 0))
        | "setevent" -> Some(Setevent(arg 0))
        // engine flags
        | "checkflag" -> Some(Checkflag(arg 0))
        | "clearflag" -> Some(Clearflag(arg 0))
        | "setflag" -> Some(Setflag(arg 0))
        // map scene
        | "checkmapscene" -> Some(Checkmapscene(arg 0))
        | "setmapscene" -> Some(Setmapscene(arg 0, i 1))
        | "checkscene" -> Some Checkscene
        | "setscene" -> Some(Setscene(i 0))
        // items (qty defaults to 1 when the source omits it)
        | "giveitem" -> Some(Giveitem(arg 0, (if args.Length > 1 then i 1 else 1)))
        | "takeitem" -> Some(Takeitem(arg 0, (if args.Length > 1 then i 1 else 1)))
        | "checkitem" -> Some(Checkitem(arg 0))
        | "verbosegiveitem" -> Some(Verbosegiveitem(arg 0, (if args.Length > 1 then i 1 else 1)))
        // text & ui
        | "opentext" -> Some Opentext
        | "closetext" -> Some Closetext
        | "writetext" -> Some(Writetext(arg 0))
        | "jumptext" -> Some(Jumptext(arg 0))
        | "jumptextfaceplayer" -> Some(Jumptextfaceplayer(arg 0))
        | "waitbutton" -> Some Waitbutton
        | "promptbutton" -> Some Promptbutton
        | "yesorno" -> Some Yesorno
        // battle
        | "loadwildmon" -> Some(Loadwildmon(arg 0, i 1))
        | "trade" -> Some(Unsupported("trade", args))
        | "givepoke" ->
            let item = if args.Length > 2 then Some(arg 2) else None
            Some(Givepoke(arg 0, i 1, item))
        | "checkpoke" -> Some(Checkpoke(arg 0))
        | "loadtrainer" -> Some(Loadtrainer(arg 0, arg 1))
        | "startbattle" -> Some Startbattle
        | "reloadmapafterbattle" -> Some Reloadmapafterbattle
        | "winlosstext" -> Some(Winlosstext(arg 0, arg 1))
        | "setlasttalked" -> Some(Setlasttalked(arg 0))
        // movement & objects
        | "applymovement" -> Some(Applymovement(arg 0, arg 1))
        | "applymovementlasttalked" -> Some(Applymovement("LAST_TALKED", arg 0))
        | "faceplayer" -> Some Faceplayer
        | "faceobject" -> Some(Faceobject(arg 0, arg 1))
        | "disappear" -> Some(Disappear(arg 0))
        | "appear" -> Some(Appear(arg 0))
        | "turnobject" -> Some(Turnobject(arg 0, arg 1))
        // object manipulation / cosmetic / timing (kept as Unsupported no-ops)
        | "moveobject" -> Some(Unsupported("moveobject", args))
        | "follow" -> Some(Unsupported("follow", args))
        | "stopfollow" -> Some(Unsupported("stopfollow", args))
        | "variablesprite" -> Some(Unsupported("variablesprite", args))
        | "fix_facing" -> Some(Unsupported("fix_facing", args))
        | "remove_fixed_facing" -> Some(Unsupported("remove_fixed_facing", args))
        | "writeobjectxy" -> Some(Unsupported("writeobjectxy", args))
        | "pause" -> Some(Unsupported("pause", args))
        | "showemote" -> Some(Unsupported("showemote", args))
        | "earthquake" -> Some(Unsupported("earthquake", args))
        | "doorstate" -> Some(Unsupported("doorstate", args))
        | "ugdoor" -> Some(Unsupported("ugdoor", args))
        | "dontrestartmapmusic" -> Some(Unsupported("dontrestartmapmusic", args))
        | "playmapmusic" -> Some(Unsupported("playmapmusic", args))
        | "musicfadeout" -> Some(Unsupported("musicfadeout", args))
        | "newloadmap" -> Some(Unsupported("newloadmap", args))
        | "warpcheck" -> Some(Unsupported("warpcheck", args))
        | "blackoutmod" -> Some(Unsupported("blackoutmod", args))
        | "reanchormap" -> Some(Unsupported("reanchormap", args))
        // audio
        | "playmusic" -> Some(Playmusic(arg 0))
        | "playsound" -> Some(Playsound(arg 0))
        | "waitsfx" -> Some Waitsfx
        | "cry" -> Some(Cry(arg 0))
        // special functions
        | "special" -> Some(Special(arg 0))
        | "gettrainername" -> Some(Unsupported("gettrainername", args))
        | "getitemname" -> Some(Unsupported("getitemname", args))
        | "getmonname" -> Some(Unsupported("getmonname", args))
        | "getstring" -> Some(Unsupported("getstring", args))
        | "getnum" -> Some(Unsupported("getnum", args))
        | "text_ram" -> Some(Unsupported("text_ram", args))
        // mart
        | "pokemart" -> Some(Pokemart(arg 0, arg 1))
        // map & warp
        | "halloffame" -> Some(Unsupported(mn, args))
        | "credits" -> Some(Unsupported(mn, args))
        | "callback" -> Some(Unsupported(mn, args))
        | "warp" -> Some(Warp(arg 0, i 1, i 2))
        | "warpfacing" -> Some(Warpfacing(arg 0, arg 1, i 2, i 3))
        | "reloadmap" -> Some Reloadmap
        | "refreshmap" -> Some Refreshmap
        | "changeblock" -> Some(Changeblock(i 0, i 1, i 2))
        // terminators
        | "endcallback" -> Some End
        | "end" -> Some End
        | "endall" -> Some EndAll
        | _ -> None

    /// The non-script directives a map file is full of: data, headers, the event
    /// tables, and char/object-constant scaffolding. These never emit a command
    /// (so a label in front of them just points at the next real script command).
    let private nonScript =
        set
            [ "db"; "dw"; "dn"; "dl"; "ds"; "dba"; "dab"; "bigdt"
              "text"; "text_far"; "line"; "cont"; "next"; "para"; "done"; "page"
              "text_start"; "raw"; "ascii"; "sound"; "interpret_data"
              "object_const_def"; "const"; "const_def"; "const_skip"; "const_value"
              "map_def"; "map_attributes"; "map_header"; "connection"
              "def_scene_scripts"; "scene_script"
              "def_callbacks"
              "def_warp_events"; "warp_event"
              "def_coord_events"; "coord_event"
              "def_bg_events"; "bg_event"
              "def_object_events"; "object_event" ]

    /// Decide what a stripped, non-empty line is: a label, a script command, an
    /// explicitly-known non-script directive, or an unrecognised mnemonic (which
    /// becomes `Unsupported` only if it isn't obviously data — i.e. it looks like
    /// a script opcode we haven't modelled). Returns either a label name or a
    /// command (or nothing for non-emitting lines). Labels are returned raw and
    /// qualified by the caller.
    type private Line =
        | LLabel of string
        | LCommand of ScriptCommand
        | LSkip

    let private classify (g: string) (body: string) : Line =
        // A label is a single token (no internal whitespace, no following mnemonic).
        let singleToken = not (body.Contains " ") && not (body.Contains "\t")
        // Map files write labels with a trailing ':'. RGBDS also allows a *local*
        // label (`.foo`) to omit the colon, which `engine/events/std_scripts.asm`
        // does throughout (e.g. `.ok` under `PokecenterNurseScript`). Recognize both
        // forms so branch/jump targets — qualified the same way — resolve; otherwise
        // a colon-less local label is mis-parsed as an `Unsupported` command and the
        // `sjump .ok` / `iftrue .morn` that target it fall through, running every
        // branch in sequence (the nurse reciting all her time-of-day lines).
        if singleToken && body.EndsWith ":" then
            LLabel(body.Substring(0, body.Length - 1))
        elif singleToken && body.StartsWith "." then
            LLabel body
        else
            let mn, args = splitLine body

            match toCommand g mn args with
            | Some cmd -> LCommand cmd
            | None ->
                if nonScript.Contains mn || mn.EndsWith ":" then
                    LSkip
                else
                    // An unmodelled mnemonic that isn't known data: record it as a
                    // script opcode we haven't covered yet (drives the M9.6 pass).
                    LCommand(Unsupported(mn, args))

    /// Parse the text of a map `.asm` file into a `ScriptProgram`.
    let parseText (text: string) : ScriptProgram =
        let commands = ResizeArray<ScriptCommand>()
        let labels = System.Collections.Generic.Dictionary<string, int>()
        let mutable lastGlobal = ""

        for raw in text.Replace("\r\n", "\n").Split('\n') do
            let body = stripComment raw

            if body <> "" then
                match classify lastGlobal body with
                | LLabel name ->
                    let qualified = qualify lastGlobal name
                    if not (name.StartsWith ".") then lastGlobal <- name
                    // First definition wins; ignore accidental duplicates.
                    if not (labels.ContainsKey qualified) then
                        labels.[qualified] <- commands.Count
                | LCommand cmd ->
                    // Expand the `trainer` macro (macros/scripts/maps.asm l.142-153) inline
                    // rather than emitting an Unsupported no-op. The macro is pure data in
                    // the disassembly; the engine's TalkToTrainerScript/AlreadyBeatenTrainer
                    // scripts (engine/events/trainer_scripts.asm) drive the actual dialog
                    // branching. We re-express the same branching as inline script commands
                    // so the VM produces the correct first-encounter / already-beaten flow
                    // without needing the `trainerflagaction`/`scripttalkafter` opcodes.
                    //
                    // Args: trainer GROUP, ID, FLAG, SEEN_TEXT, BEATEN_TEXT, LOSS_TEXT, AFTER_SCRIPT
                    //
                    // Expansion (equivalent of TalkToTrainerScript):
                    //   faceplayer
                    //   checkevent FLAG
                    //   iftrue AFTER_SCRIPT   ;; already beaten → jump to after-battle script
                    //   opentext
                    //   writetext SEEN_TEXT
                    //   waitbutton
                    //   closetext
                    //   loadtrainer GROUP, ID
                    //   startbattle
                    //   reloadmapafterbattle
                    //   setevent FLAG
                    //   end                   ;; first encounter ends here (never reaches AFTER_SCRIPT)
                    //
                    // The AFTER_SCRIPT label (e.g. `.Script`) follows the trainer data in the
                    // source and typically starts with `endifjustbattled` (already a no-op in
                    // the VM), so the "talk again" path sees the after-battle dialog while
                    // the "just won" path ended above.
                    match cmd with
                    | Unsupported("trainer", args) when args.Length >= 7 ->
                        let group = args.[0]
                        let id = args.[1]
                        let flag = args.[2]
                        let seenText = args.[3]
                        let afterLabel = qualify lastGlobal args.[6]
                        commands.AddRange(
                            [| Faceplayer
                               Checkevent flag
                               Iftrue afterLabel
                               Opentext
                               Writetext seenText
                               Waitbutton
                               Closetext
                               Loadtrainer(group, id)
                               Startbattle
                               Reloadmapafterbattle
                               Setevent flag
                               End |])
                    | Unsupported("itemball", args) when args.Length >= 1 ->
                        let qty = if args.Length > 1 then intArg args.[1] else 1
                        commands.AddRange([| Verbosegiveitem(args.[0], qty); End |])
                    | Unsupported("hiddenitem", args) when args.Length >= 1 ->
                        commands.AddRange([| Verbosegiveitem(args.[0], 1); End |])
                    | Unsupported("fruittree", _) ->
                        commands.AddRange([| Verbosegiveitem("BERRY", 1); End |])
                    | _ -> commands.Add cmd
                | LSkip -> ()

        { Commands = commands.ToArray()
          Labels = labels |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq }
