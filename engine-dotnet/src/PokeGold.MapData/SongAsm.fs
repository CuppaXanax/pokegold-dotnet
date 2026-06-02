namespace PokeGold.Game.Audio

open System
open System.Globalization
open System.Collections.Generic

/// Pure, I/O-free parser for the GSC audio script (`audio/music/*.asm`,
/// `audio/sfx.asm`): text in, `Song` values out. Shared by `PokeGold.DataGen`
/// (the build-time producer that bakes the parsed songs into F# literals) and -
/// only as a fallback - the runtime. Notes are written as pitch symbols
/// (`C_`..`B_`) and control flow as labels; both are resolved here so the synth
/// sees only numbers and command indices. Mirrors the map `ScriptParser` split.
module SongAsm =

    // ---- Self-contained RGBDS line helpers (no Game/Core dependency) ----------

    /// Parse a RGBDS integer literal: `$hex`, `%binary`, or decimal.
    let private parseInt (s: string) : int =
        let t = s.Trim()
        if t.StartsWith "$" then Convert.ToInt32(t.Substring 1, 16)
        elif t.StartsWith "%" then Convert.ToInt32(t.Substring 1, 2)
        else Int32.Parse(t, CultureInfo.InvariantCulture)

    /// Strip a trailing `; comment` and surrounding whitespace from a line.
    let private stripComment (line: string) : string =
        let i = line.IndexOf ';'
        (if i >= 0 then line.Substring(0, i) else line).Trim()

    /// Split a `mnemonic arg, arg, ...` line into its mnemonic and trimmed args.
    let private splitLine (line: string) : string * string list =
        let body = stripComment line
        if body = "" then "", []
        else
            let ws = body.IndexOfAny([| ' '; '\t' |])
            if ws < 0 then body, []
            else
                let mn = body.Substring(0, ws).Trim()
                let rest = body.Substring(ws + 1)
                let args = rest.Split(',') |> Array.map (fun a -> a.Trim()) |> Array.toList
                mn, (args |> List.filter (fun a -> a <> ""))

    /// Note symbol -> pitch index 1..12 (constants/audio_constants.asm).
    let private pitchOf (sym: string) : int =
        match sym with
        | "C_" -> 1 | "C#" -> 2 | "D_" -> 3 | "D#" -> 4
        | "E_" -> 5 | "F_" -> 6 | "F#" -> 7 | "G_" -> 8
        | "G#" -> 9 | "A_" -> 10 | "A#" -> 11 | "B_" -> 12
        | _ -> 0

    let private tryInt (s: string) : int option =
        try Some(parseInt s) with _ -> None

    let private boolArg (s: string) : bool =
        match s.Trim() with
        | "FALSE" | "0" -> false
        | _ -> true

    /// One entry the first pass produces; control-flow keeps its (already
    /// label-qualified) target string until the second pass resolves it to an index.
    type private Raw =
        | RCmd of SoundCommand
        | RCall of string
        | RRet
        | RLoop of int * string
        | RJump of string
        | RJumpIf of int * string

    /// A song header gathered while scanning: its label, channel count, and the
    /// (hardware id, channel label) pairs from its `channel` directives.
    type private Header =
        { Name: string
          mutable Count: int
          Channels: List<int * string> }

    /// Result of parsing a whole file: the shared command stream, the label->index
    /// map, and every song/SFX header found.
    type private ParsedFile =
        { Commands: SoundCommand[]
          Labels: IReadOnlyDictionary<string, int>
          Headers: Header list }

    let private qualify (lastGlobal: string) (name: string) : string =
        if name.StartsWith "." then lastGlobal + name else name

    /// Translate one mnemonic+args into a Raw entry, or None for non-emitting lines
    /// (labels, header directives, raw bytes). `lastGlobal` qualifies local labels.
    let private toRaw (lastGlobal: string) (mn: string) (args: string list) : Raw option =
        let i n = match List.tryItem n args with Some a -> (match tryInt a with Some v -> v | None -> 0) | None -> 0
        let env a b = Envelope.ofArgs (i a) (i b)

        match mn with
        | "note" ->
            let p = match List.tryItem 0 args with Some s -> pitchOf s | None -> 0
            Some(RCmd(Note(p, i 1)))
        | "drum_note" ->
            // On the noise channel the first arg is a numeric drum index, not a note symbol.
            Some(RCmd(Note(i 0, i 1)))
        | "rest" -> Some(RCmd(Rest(i 0)))
        | "square_note" -> Some(RCmd(SquareNote(i 0, env 1 2, i 3)))
        | "noise_note" -> Some(RCmd(NoiseNoteCmd { Length = i 0; Env = env 1 2; Freq = i 3 }))
        | "octave" -> Some(RCmd(Octave(i 0)))
        | "note_type" | "drum_speed" ->
            Some(RCmd(NoteType(i 0, (if args.Length >= 3 then Some(env 1 2) else None))))
        | "transpose" -> Some(RCmd(Transpose(i 0, i 1)))
        | "tempo" -> Some(RCmd(Tempo(i 0)))
        | "tempo_relative" -> Some(RCmd(TempoRelative(i 0)))
        | "duty_cycle" -> Some(RCmd(DutyCycle(i 0)))
        | "duty_cycle_pattern" -> Some(RCmd(DutyCyclePattern(i 0, i 1, i 2, i 3)))
        | "volume_envelope" -> Some(RCmd(VolumeEnvelope(env 0 1)))
        | "pitch_sweep" -> Some(RCmd(PitchSweep(env 0 1)))
        | "vibrato" -> Some(RCmd(Vibrato(i 0, i 1, (if args.Length >= 3 then i 2 else 0))))
        | "pitch_slide" -> Some(RCmd(PitchSlide(i 0, i 1, i 2)))
        | "pitch_offset" -> Some(RCmd(PitchOffset(i 0)))
        | "volume" -> Some(RCmd(Volume(i 0, (if args.Length > 1 then i 1 else i 0))))
        | "stereo_panning" -> Some(RCmd(StereoPanning(boolArg args.[0], boolArg args.[1])))
        | "force_stereo_panning" -> Some(RCmd(ForceStereoPanning(boolArg args.[0], boolArg args.[1])))
        | "toggle_noise" | "sfx_toggle_noise" ->
            Some(RCmd(ToggleNoise(if args.IsEmpty then None else Some(i 0))))
        | "toggle_sfx" -> Some(RCmd ToggleSfx)
        | "set_condition" -> Some(RCmd(SetCondition(i 0)))
        | "sound_call" -> Some(RCall(qualify lastGlobal args.[0]))
        | "sound_ret" -> Some RRet
        | "sound_loop" -> Some(RLoop(i 0, qualify lastGlobal args.[1]))
        | "sound_jump" -> Some(RJump(qualify lastGlobal args.[0]))
        | "sound_jump_if" -> Some(RJumpIf(i 0, qualify lastGlobal args.[1]))
        // Header/byte directives are handled by the scanner, not as commands.
        | "channel_count" | "channel" | "db" | "dw" | "dn" -> None
        // Recognized but unmodeled hooks keep structure without affecting timing.
        | _ -> Some(RCmd NoOp)

    let private parseFile (text: string) : ParsedFile =
        let lines = text.Replace("\r", "").Split('\n')
        let raws = List<Raw>()
        let labels = Dictionary<string, int>()
        let pending = List<string>()
        let headers = List<Header>()
        let mutable lastGlobal = ""
        let mutable header : Header option = None

        let bind () =
            // Bind any labels seen since the last command to the next command index.
            for l in pending do
                labels.[l] <- raws.Count
            pending.Clear()

        for line in lines do
            let body = stripComment line
            if body = "" then ()
            elif body.EndsWith ":" then
                let name = body.TrimEnd(':')
                if name.StartsWith "." then pending.Add(qualify lastGlobal name)
                else
                    lastGlobal <- name
                    pending.Add name
            else
                let mn, args = splitLine line
                match mn with
                | "channel_count" ->
                    let h = { Name = lastGlobal; Count = (match tryInt args.[0] with Some v -> v | None -> 1); Channels = List() }
                    header <- Some h
                    headers.Add h
                | "channel" ->
                    match header with
                    | Some h when args.Length >= 2 ->
                        h.Channels.Add(((match tryInt args.[0] with Some v -> v | None -> 0), args.[1]))
                    | _ -> ()
                | _ ->
                    match toRaw lastGlobal mn args with
                    | Some r ->
                        bind ()
                        raws.Add r
                    | None -> ()

        // Second pass: resolve control-flow label targets to command indices.
        let idx (name: string) = match labels.TryGetValue name with true, v -> v | _ -> 0
        let commands =
            raws
            |> Seq.map (fun r ->
                match r with
                | RCmd c -> c
                | RCall t -> SoundCall(idx t)
                | RRet -> SoundRet
                | RLoop (n, t) -> SoundLoop(n, idx t)
                | RJump t -> SoundJump(idx t)
                | RJumpIf (c, t) -> SoundJumpIf(c, idx t))
            |> Seq.toArray

        { Commands = commands; Labels = labels; Headers = List.ofSeq headers }

    let private toSong (pf: ParsedFile) (h: Header) : Song =
        { ChannelCount = h.Count
          Channels =
            h.Channels
            |> Seq.map (fun (id, label) ->
                id, (match pf.Labels.TryGetValue label with true, v -> v | _ -> 0))
            |> Seq.toArray
          Commands = pf.Commands }

    /// Parse a single-song file (e.g. a music track): the first header's song.
    /// Returns None if the file declares no song.
    let firstSong (text: string) : Song option =
        let pf = parseFile text
        match pf.Headers with
        | h :: _ -> Some(toSong pf h)
        | [] -> None

    /// Parse every header in a file (e.g. `audio/sfx.asm`) into `(name, song)`
    /// pairs. All returned songs share the one parsed command stream, exactly as
    /// the runtime sliced them, so callers can bake the stream once.
    let allSongs (text: string) : (string * Song) list =
        let pf = parseFile text
        pf.Headers |> List.map (fun h -> h.Name, toSong pf h)
