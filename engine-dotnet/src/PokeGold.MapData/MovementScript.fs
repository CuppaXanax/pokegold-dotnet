namespace PokeGold.Game.Overworld.Script

open System

/// One command of an overworld **movement script** — the actor-animation language
/// run by `applymovement` (`macros/scripts/movement.asm`). Directional commands
/// carry a direction index `0=Down 1=Up 2=Left 3=Right` (GSC's `DOWN/UP/LEFT/RIGHT`
/// operand). Only the behaviours the high-level port animates are modelled; every
/// other movement macro is kept verbatim as `MovementUnsupported name` so the M10.9
/// coverage sweep can still account for it. The record types live in the shared
/// `PokeGold.MapData` project so the same parse backs the build-time generator and
/// the runtime consumer.
type MovementCmd =
    /// `step DIR` — walk one tile (normal speed).
    | MoveStep of dir: int
    /// `big_step DIR` — walk one tile, fast.
    | MoveBigStep of dir: int
    /// `slow_step DIR` — walk one tile, slow.
    | MoveSlowStep of dir: int
    /// `turn_step DIR` — turn, then walk one tile.
    | MoveTurnStep of dir: int
    /// `slide_step`/`slow_slide_step`/`fast_slide_step DIR` — glide one tile.
    | MoveSlideStep of dir: int
    /// `jump_step`/`slow_jump_step`/`fast_jump_step DIR` — hop one tile.
    | MoveJumpStep of dir: int
    /// `turn_head DIR` — face a direction without moving.
    | MoveTurnHead of dir: int
    /// `step_sleep N` — hold position for `N` frames.
    | MoveStepSleep of frames: int
    /// `step_end`/`step_wait_end` — the script terminates.
    | MoveStepEnd
    /// Any other movement macro (control/teleport/emote/…) — modelled as a no-op
    /// but preserved by name for coverage accounting.
    | MoveUnsupported of name: string

/// Parses the `applymovement` actor scripts and per-map object-constant blocks out
/// of a map `.asm`. Like the other `PokeGold.MapData` parsers it is pure text-in,
/// value-out, shared by the runtime tests and the build-time generator.
module MovementParser =

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

    /// Parse a `DOWN`/`UP`/`LEFT`/`RIGHT` operand to a direction index.
    let private dirIndex (s: string) : int =
        match s.Trim() with
        | "UP" -> 1
        | "LEFT" -> 2
        | "RIGHT" -> 3
        | _ -> 0

    /// Parse a small integer operand (decimal or `$hex`), defaulting to 0.
    let private intArg (s: string) : int =
        let t = s.Trim()

        try
            if t.StartsWith "$" then Convert.ToInt32(t.Substring 1, 16) else Int32.Parse t
        with _ ->
            0

    /// Every mnemonic the movement-script language defines (`macros/scripts/
    /// movement.asm`). Recognising the full set lets the block scanner tell a
    /// movement script apart from ordinary map-script code.
    let private movementMnemonics: Set<string> =
        set
            [ "turn_head"; "turn_step"; "slow_step"; "step"; "big_step"
              "slow_slide_step"; "slide_step"; "fast_slide_step"; "turn_away"; "turn_in"; "turn_waterfall"
              "slow_jump_step"; "jump_step"; "fast_jump_step"
              "remove_sliding"; "set_sliding"; "remove_fixed_facing"; "fix_facing"; "show_object"; "hide_object"
              "step_sleep"; "step_end"; "step_wait_end"; "remove_object"; "step_loop"; "step_stop"
              "teleport_from"; "teleport_to"; "skyfall"; "step_dig"; "step_bump"; "fish_got_bite"; "fish_cast_rod"
              "hide_emote"; "show_emote"; "step_shake"; "tree_shake"; "rock_smash"; "return_dig" ]

    /// Decode a single movement-macro line, or `None` if it isn't one.
    let private cmdOf (mn: string) (args: string list) : MovementCmd option =
        let a0 () = List.tryItem 0 args |> Option.defaultValue ""

        match mn with
        | "step" -> Some(MoveStep(dirIndex (a0 ())))
        | "big_step" -> Some(MoveBigStep(dirIndex (a0 ())))
        | "slow_step" -> Some(MoveSlowStep(dirIndex (a0 ())))
        | "turn_step" -> Some(MoveTurnStep(dirIndex (a0 ())))
        | "slow_slide_step"
        | "slide_step"
        | "fast_slide_step" -> Some(MoveSlideStep(dirIndex (a0 ())))
        | "slow_jump_step"
        | "jump_step"
        | "fast_jump_step" -> Some(MoveJumpStep(dirIndex (a0 ())))
        | "turn_head" -> Some(MoveTurnHead(dirIndex (a0 ())))
        | "step_sleep" -> Some(MoveStepSleep(intArg (a0 ())))
        | "step_end"
        | "step_wait_end" -> Some MoveStepEnd
        | m when Set.contains m movementMnemonics -> Some(MoveUnsupported m)
        | _ -> None

    /// `step_end`-equivalent commands that terminate a movement block.
    let private terminalMnemonics: Set<string> =
        set [ "step_end"; "step_wait_end"; "step_loop"; "step_stop" ]

    /// Is a body line a bare global label (`Foo:`)?
    let private labelOf (body: string) : string option =
        if body.EndsWith ":" && not (body.Contains " ") && not (body.Contains "\t") && not (body.StartsWith ".") then
            Some(body.TrimEnd(':'))
        else
            None

    /// Extract every movement script in a map `.asm`: a global label immediately
    /// followed by one or more movement macros, up to its terminating `step_end`.
    /// Labels whose body isn't movement code (ordinary scripts) are ignored.
    let parseMovements (text: string) : Map<string, MovementCmd[]> =
        let result = System.Collections.Generic.Dictionary<string, MovementCmd[]>()
        let acc = ResizeArray<MovementCmd>()
        let mutable label: string option = None

        let flush () =
            match label with
            | Some l when acc.Count > 0 -> result.[l] <- acc.ToArray()
            | _ -> ()

            acc.Clear()
            label <- None

        for raw in text.Replace("\r\n", "\n").Split('\n') do
            let body = stripComment raw

            if body <> "" then
                match labelOf body with
                | Some l ->
                    flush ()
                    label <- Some l
                | None ->
                    let mn, args = splitLine body

                    match cmdOf mn args with
                    | Some c when label.IsSome ->
                        acc.Add c

                        if Set.contains mn terminalMnemonics then
                            flush ()
                    | _ ->
                        // A non-movement line ends any tentative block (the label
                        // turned out to be an ordinary map script).
                        flush ()

        flush ()

        result |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

    /// The map's object-constant names in declaration order (`object_const_def` /
    /// `const NAME`). The i-th name is the i-th `object_event`, so `applymovement`'s
    /// symbolic actor operand resolves to an object index by position.
    let parseObjectConsts (text: string) : string[] =
        let acc = ResizeArray<string>()
        let mutable inBlock = false

        for raw in text.Replace("\r\n", "\n").Split('\n') do
            let body = stripComment raw

            if body = "object_const_def" then
                inBlock <- true
            elif inBlock && body <> "" then
                let mn, args = splitLine body

                if mn = "const" && not (List.isEmpty args) then
                    acc.Add(List.head args)
                else
                    inBlock <- false

        acc.ToArray()
