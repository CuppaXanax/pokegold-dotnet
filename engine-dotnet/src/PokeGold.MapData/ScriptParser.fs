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

    /// Small always-known constants used by script operands. Data generation passes
    /// the full disassembly constant table; this keeps parser-only tests and tiny
    /// snippets from regressing on the most common values.
    let private builtInConstants : Map<string, int> =
        Map.ofList
            [ "FALSE", 0
              "TRUE", 1
              "DOWN", 0
              "UP", 1
              "LEFT", 2
              "RIGHT", 3 ]

    let private tryParseInt (s: string) : int option =
        try Some(parseInt s) with _ -> None

    /// Parse the simple constant expressions RGBDS scripts use in numeric operand
    /// slots (`RIGHT`, `PARTY_LENGTH`, `NUM_POKEMON - 2 - 1`, etc.).
    let private intArg (strict: bool) (constants: Map<string, int>) (s: string) : int =
        let tokenValue token =
            match tryParseInt token with
            | Some value -> Some value
            | None -> Map.tryFind token constants

        let tokens =
            s.Trim().Replace("+", " + ").Replace("-", " - ").Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

        let mutable sign = 1
        let mutable value = 0
        let mutable sawTerm = false
        let mutable unresolved: string option = None

        for token in tokens do
            match token with
            | "+" -> sign <- 1
            | "-" -> sign <- -1
            | _ ->
                match tokenValue token with
                | Some term ->
                    value <- value + sign * term
                    sawTerm <- true
                    sign <- 1
                | None ->
                    if unresolved.IsNone then
                        unresolved <- Some token

        match unresolved with
        | Some token when strict -> failwithf "Unresolved numeric script constant '%s' in operand '%s'" token s
        | Some _ -> 0
        | None when sawTerm -> value
        | None -> 0

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

    /// Every mnemonic in the movement-script language. Movement blocks are parsed
    /// separately by `MovementParser`; they are data referenced by `applymovement`,
    /// not runnable overworld script commands.
    let private movementMnemonics: Set<string> =
        set
            [ "turn_head"; "turn_step"; "slow_step"; "step"; "big_step"
              "slow_slide_step"; "slide_step"; "fast_slide_step"; "turn_away"; "turn_in"; "turn_waterfall"
              "slow_jump_step"; "jump_step"; "fast_jump_step"
              "remove_sliding"; "set_sliding"; "remove_fixed_facing"; "fix_facing"; "show_object"; "hide_object"
              "step_sleep"; "step_end"; "step_wait_end"; "remove_object"; "step_loop"; "step_stop"
              "teleport_from"; "teleport_to"; "skyfall"; "step_dig"; "step_bump"; "fish_got_bite"; "fish_cast_rod"
              "hide_emote"; "show_emote"; "step_shake"; "tree_shake"; "rock_smash"; "return_dig" ]

    let private terminalMovementMnemonics: Set<string> =
        set [ "step_end"; "step_wait_end"; "step_loop"; "step_stop" ]

    let private globalLabelOf (body: string) : string option =
        if body.EndsWith ":" && not (body.Contains " ") && not (body.Contains "\t") && not (body.StartsWith ".") then
            Some(body.Substring(0, body.Length - 1))
        else
            None

    let private collectMovementLabels (text: string) : Set<string> =
        let labels = ResizeArray<string>()
        let mutable pendingLabel: string option = None

        for raw in text.Replace("\r\n", "\n").Split('\n') do
            let body = stripComment raw

            if body <> "" then
                match globalLabelOf body with
                | Some label -> pendingLabel <- Some label
                | None ->
                    let mn, _ = splitLine body

                    match pendingLabel with
                    | Some label when movementMnemonics.Contains mn ->
                        labels.Add label
                        pendingLabel <- None
                    | Some _ -> pendingLabel <- None
                    | None -> ()

        labels |> Set.ofSeq

    /// Qualify a local label/target (`.Foo`) with the enclosing global label.
    let private qualify (lastGlobal: string) (name: string) : string =
        if name.StartsWith "." then lastGlobal + name else name

    /// Translate one mnemonic + args into a command, with targets/labels already
    /// qualified by `g` (the current global label). Returns `None` for lines that
    /// don't emit a script command (text/data/event-table/header directives).
    let private toCommand (strict: bool) (constants: Map<string, int>) (g: string) (mn: string) (args: string list) : ScriptCommand option =
        let arg n = List.tryItem n args |> Option.defaultValue ""
        let lbl n = qualify g (arg n)
        let i n = intArg strict constants (arg n)

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
            [ "db"; "dw"; "dn"; "dl"; "ds"; "dba"; "dab"; "dbw"; "bigdt";
              "text"; "text_far"; "line"; "cont"; "next"; "para"; "done"; "page";
              "text_start"; "text_end"; "raw"; "ascii"; "sound"; "interpret_data";
              "DEF"; "INCLUDE"; "MACRO"; "ENDM"; "add_stdscript";
              "object_const_def"; "const"; "const_def"; "const_skip"; "const_value";
              "map_def"; "map_attributes"; "map_header"; "connection";
              "def_scene_scripts"; "scene_script"; "scene_const";
              "def_callbacks"; "callback";
              "def_warp_events"; "warp_event";
              "def_coord_events"; "coord_event";
              "def_bg_events"; "bg_event";
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

    let private classify (strict: bool) (constants: Map<string, int>) (g: string) (body: string) : Line =
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

            match toCommand strict constants g mn args with
            | Some cmd -> LCommand cmd
            | None ->
                if nonScript.Contains mn || mn.EndsWith ":" then
                    LSkip
                else
                    // An unmodelled mnemonic that isn't known data: record it as a
                    // script opcode we haven't covered yet (drives the M9.6 pass).
                    LCommand(Unsupported(mn, args))

    let private collectSceneConstants (text: string) : Map<string, int> =
        let scenes = ResizeArray<string>()

        for raw in text.Replace("\r\n", "\n").Split('\n') do
            let body = stripComment raw

            if body <> "" then
                let mn, args = splitLine body

                match mn with
                | "scene_script" when args.Length > 1 && args.[1] <> "" -> scenes.Add args.[1]
                | "scene_const" when args.Length > 0 && args.[0] <> "" -> scenes.Add args.[0]
                | _ -> ()

        scenes
        |> Seq.mapi (fun i name -> name, i)
        |> Seq.distinctBy fst
        |> Map.ofSeq

    let private constantsFor (extraConstants: Map<string, int>) (text: string) : Map<string, int> =
        let addAll source target =
            source |> Map.fold (fun acc key value -> Map.add key value acc) target

        builtInConstants
        |> addAll extraConstants
        |> addAll (collectSceneConstants text)

    let private parseTextInternal (strict: bool) (extraConstants: Map<string, int>) (text: string) : ScriptProgram =
        let commands = ResizeArray<ScriptCommand>()
        let labels = System.Collections.Generic.Dictionary<string, int>()
        let mutable lastGlobal = ""
        let constants = constantsFor extraConstants text
        let movementLabels = collectMovementLabels text

        let mutable inMacro = false
        let mutable inMovementBlock = false

        for raw in text.Replace("\r\n", "\n").Split('\n') do
            let body = stripComment raw

            if body <> "" then
                let mn, _ = splitLine body

                if inMacro then
                    if mn = "ENDM" then
                        inMacro <- false
                elif inMovementBlock then
                    if terminalMovementMnemonics.Contains mn then
                        inMovementBlock <- false
                elif mn = "MACRO" then
                    inMacro <- true
                elif
                    match globalLabelOf body with
                    | Some label when movementLabels.Contains label ->
                        inMovementBlock <- true
                        true
                    | _ -> false
                then
                    ()
                elif movementMnemonics.Contains mn then
                    ()
                else
                    match classify strict constants lastGlobal body with
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
                            let qty = if args.Length > 1 then intArg strict constants args.[1] else 1
                            commands.AddRange([| Verbosegiveitem(args.[0], qty); End |])
                        | Unsupported("hiddenitem", args) when args.Length >= 1 ->
                            commands.AddRange([| Verbosegiveitem(args.[0], 1); End |])
                        | Unsupported("fruittree", _) ->
                            commands.AddRange([| Verbosegiveitem("BERRY", 1); End |])
                        | _ -> commands.Add cmd
                    | LSkip -> ()

        { Commands = commands.ToArray()
          Labels = labels |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq }

    /// Parse a map `.asm` file with a build-time constant table. This is the path
    /// DataGen uses; unresolved symbolic numeric operands are fatal so bad scenes
    /// cannot be baked into generated source as `0`.
    let parseTextWithConstants (constants: Map<string, int>) (text: string) : ScriptProgram =
        parseTextInternal true constants text

    /// Parse the text of a map `.asm` file into a `ScriptProgram`.
    let parseText (text: string) : ScriptProgram =
        parseTextInternal false Map.empty text
