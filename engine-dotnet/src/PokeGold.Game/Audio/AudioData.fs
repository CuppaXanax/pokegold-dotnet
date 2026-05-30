namespace PokeGold.Game.Audio

open System
open System.Globalization
open PokeGold.Game.Core

/// A volume envelope, as written in the audio script (`volume_envelope`,
/// `note_type`, `square_note`/`noise_note`). The script encodes the sweep with a
/// signed second argument: a positive value fades the note *out* over `Period`
/// engine steps, a negative value fades it *in*. `Period = 0` holds the volume.
type Envelope =
    { InitialVolume: int // 0..15
      Increase: bool
      Period: int } // 0..7

module Envelope =
    /// Decode an envelope from the script's two arguments (volume, signed sweep).
    let ofArgs (volume: int) (sweep: int) : Envelope =
        { InitialVolume = volume
          Increase = sweep < 0
          Period = abs sweep }

    let silent = { InitialVolume = 0; Increase = false; Period = 0 }

/// One drum voice in a drumkit: a short noise note (length in 16ths, an envelope,
/// and the raw GB noise polynomial byte that sets its timbre/pitch).
type NoiseNote =
    { Length: int
      Env: Envelope
      Freq: int }

/// Static audio tables ported from the disassembly: the note-frequency table
/// (audio/notes.asm), the wave-channel sample shapes (audio/wave_samples.asm), and
/// the drumkits (audio/drumkits.asm). All are read once from the source `.asm` and
/// cached, in keeping with the engine reading repo assets in place.
module AudioData =

    /// Parse a RGBDS integer literal: `$hex`, `%binary`, or decimal. The argument
    /// may carry a trailing comment, already stripped by the caller.
    let parseInt (s: string) : int =
        let t = s.Trim()
        if t.StartsWith "$" then Convert.ToInt32(t.Substring 1, 16)
        elif t.StartsWith "%" then Convert.ToInt32(t.Substring 1, 2)
        else Int32.Parse(t, CultureInfo.InvariantCulture)

    /// Strip a trailing `; comment` and surrounding whitespace from a line.
    let stripComment (line: string) : string =
        let i = line.IndexOf ';'
        (if i >= 0 then line.Substring(0, i) else line).Trim()

    /// Split a `mnemonic arg, arg, ...` line into its mnemonic and trimmed args.
    let splitLine (line: string) : string * string list =
        let body = stripComment line
        if body = "" then "", []
        else
            // Label lines end in ':' and carry no mnemonic.
            let ws = body.IndexOfAny([| ' '; '\t' |])
            if ws < 0 then body, []
            else
                let mn = body.Substring(0, ws).Trim()
                let rest = body.Substring(ws + 1)
                let args = rest.Split(',') |> Array.map (fun a -> a.Trim()) |> Array.toList
                mn, (args |> List.filter (fun a -> a <> ""))

    // ---- audio/notes.asm : FrequencyTable -------------------------------------

    /// The GB frequency register values per pitch (index 0 unused; 1..12 = C..B,
    /// with a second octave for transposition overflow). Read from notes.asm.
    let frequencyTable : int[] =
        Assets.readText "audio/notes.asm"
        |> fun text -> text.Replace("\r", "").Split('\n')
        |> Array.choose (fun line ->
            let mn, args = splitLine line
            if mn = "dw" && not args.IsEmpty then Some(parseInt args.Head) else None)

    /// Frequency in Hz of a note at the given GSC octave (1..8) and pitch (1..12),
    /// faithful to engine.asm GetFrequency: take the table value for the pitch,
    /// shift right by (7 - octave), keep 11 bits, then apply the GB square formula
    /// `131072 / (2048 - period)`.
    let noteFrequency (octave: int) (pitch: int) : float =
        if pitch <= 0 || pitch >= frequencyTable.Length then 0.0
        else
            let raw = frequencyTable.[pitch]
            let shift = 7 - octave
            let shifted = if shift >= 0 then raw >>> shift else raw <<< (-shift)
            let period = shifted &&& 0x7FF
            if period >= 2048 then 0.0 else 131072.0 / float (2048 - period)

    // ---- audio/wave_samples.asm : 32-step 4-bit waveforms ----------------------

    /// The wave-channel sample shapes: each is 32 nibbles (0..15). Read from
    /// wave_samples.asm (`dn` packs the nibbles).
    let waveSamples : int[][] =
        Assets.readText "audio/wave_samples.asm"
        |> fun text -> text.Replace("\r", "").Split('\n')
        |> Array.choose (fun line ->
            let mn, args = splitLine line
            if mn = "dn" then Some(args |> List.map parseInt |> List.toArray) else None)
        |> Array.filter (fun a -> a.Length = 32)

    // ---- audio/drumkits.asm : kits of drum (noise) voices ----------------------

    /// The drumkits: `drumkits.[kit].[note]` is the list of noise notes that drum
    /// plays. Parsed from drumkits.asm by resolving each kit's drum labels to the
    /// `noise_note` sequences defined later in the file.
    let drumkits : NoiseNote list[][] =
        let lines =
            Assets.readText "audio/drumkits.asm"
            |> fun text -> text.Replace("\r", "").Split('\n')

        // First pass: collect each drum label's noise-note sequence.
        let samples = System.Collections.Generic.Dictionary<string, NoiseNote list>()
        let mutable curLabel = ""
        let cur = System.Collections.Generic.List<NoiseNote>()

        let flush () =
            if curLabel <> "" then samples.[curLabel] <- List.ofSeq cur

        for line in lines do
            let body = stripComment line
            if body.EndsWith ":" then
                let name = body.TrimEnd(':')
                if not (name.StartsWith "Drumkit") && name <> "Drumkits" then
                    flush ()
                    curLabel <- name
                    cur.Clear()
            else
                let mn, args = splitLine line
                if mn = "noise_note" && args.Length >= 4 then
                    let a = args |> List.map parseInt
                    cur.Add
                        { Length = a.[0]
                          Env = Envelope.ofArgs a.[1] a.[2]
                          Freq = a.[3] }
        flush ()

        // Second pass: each `Drumkit_:` lists drum labels via `dw`.
        let kits = System.Collections.Generic.List<NoiseNote list[]>()
        let mutable kit : System.Collections.Generic.List<NoiseNote list> = null

        for line in lines do
            let body = stripComment line
            if body.StartsWith "Drumkit" && body.EndsWith ":" then
                if kit <> null then kits.Add(kit.ToArray())
                kit <- System.Collections.Generic.List<NoiseNote list>()
            else
                let mn, args = splitLine line
                if mn = "dw" && kit <> null && not args.IsEmpty then
                    match samples.TryGetValue args.Head with
                    | true, v -> kit.Add v
                    | _ -> kit.Add []
        if kit <> null then kits.Add(kit.ToArray())
        kits.ToArray()
