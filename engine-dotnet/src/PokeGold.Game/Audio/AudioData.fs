namespace PokeGold.Game.Audio

open System
open System.Globalization
open PokeGold.Game.Core

/// A `volume_envelope`/`note_type`/`square_note`/`noise_note` argument pair, kept
/// verbatim as the two macro arguments (`volume_envelope X, Y`). The two values
/// mean different things per channel, so we store the raw pair and decode at the
/// use site:
///  - Pulse/noise: `Volume` (0..15) is the starting volume; `Sweep` is a signed
///    fade — positive fades *out* over `|Sweep|` engine steps, negative fades in,
///    0 holds. (The macro encodes a negative sweep as the NRx2 increase bit.)
///  - Wave (ch3): `Sweep`'s low nibble selects the waveform (0..9) and `Volume`'s
///    low two bits select the NR32 output level (0=mute,1=100%,2=50%,3=25%).
type Envelope = { Volume: int; Sweep: int }

module Envelope =
    /// Keep the two script arguments verbatim (volume, signed sweep).
    let ofArgs (volume: int) (sweep: int) : Envelope = { Volume = volume; Sweep = sweep }

    let silent = { Volume = 0; Sweep = 0 }

    // ---- Pulse/noise interpretation (NRx2 volume envelope) --------------------

    /// Starting volume 0..15.
    let initialVolume (e: Envelope) : int = e.Volume
    /// True if the note fades *in* (negative sweep = NRx2 increase bit).
    let increase (e: Envelope) : bool = e.Sweep < 0
    /// Envelope step period 0..7 (0 = hold).
    let period (e: Envelope) : int = abs e.Sweep

    // ---- Wave (ch3) interpretation -------------------------------------------

    /// Which of the 10 waveforms this selects (the sweep arg's low nibble).
    let waveformIndex (e: Envelope) : int = e.Sweep &&& 0xF
    /// The NR32 output-level scale: the volume arg's low two bits pick
    /// mute/100%/50%/25% (engine: `(byte & $f0) << 1` into NR32 bits 6-5).
    let waveVolume (e: Envelope) : float =
        match e.Volume &&& 3 with
        | 1 -> 1.0
        | 2 -> 0.5
        | 3 -> 0.25
        | _ -> 0.0

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

    /// The 11-bit GB frequency register value for a note at the given GSC octave
    /// (1..8, as written in the .asm) and pitch (1..12), faithful to engine.asm
    /// GetFrequency. The note macro stores the octave inverted (engine octave =
    /// 8 - written), so the table value is arithmetic-shifted right by
    /// (7 - engineOctave) = (written - 1). The table holds 16-bit "negative"
    /// frequencies ($f8xx..$fdxx); GSC sign-propagates them (sra/rr) before keeping
    /// the low 11 bits, so we sign-extend to match.
    let notePeriod (octave: int) (pitch: int) : int =
        if pitch <= 0 || pitch >= frequencyTable.Length then 0
        else
            let raw = frequencyTable.[pitch]
            let signed = if raw >= 0x8000 then raw - 0x10000 else raw
            let shift = octave - 1
            let shifted = if shift >= 0 then signed >>> shift else signed <<< (-shift)
            shifted &&& 0x7FF

    /// Audible Hz of a pulse channel playing the given 11-bit period register
    /// value: the GB square formula `131072 / (2048 - period)`.
    let periodToHz (period: int) : float =
        if period <= 0 || period >= 2048 then 0.0 else 131072.0 / float (2048 - period)

    /// Frequency in Hz of a note at the given GSC octave (1..8) and pitch (1..12)
    /// on a pulse channel.
    let noteFrequency (octave: int) (pitch: int) : float =
        periodToHz (notePeriod octave pitch)

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
            if body.StartsWith "Drumkit" && body.EndsWith ":" && body <> "Drumkits:" then
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
