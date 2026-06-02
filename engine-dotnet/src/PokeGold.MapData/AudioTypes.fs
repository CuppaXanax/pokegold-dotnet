namespace PokeGold.Game.Audio

/// Shared, I/O-free audio data types + the GSC sound-script parser. These live in
/// the `PokeGold.MapData` project (not the game) so the build-time generator
/// (`PokeGold.DataGen`) and the runtime (`PokeGold.Game`) both reference the same
/// types and parse logic without a circular reference - exactly how the map DUs and
/// parsers are shared. The namespace stays `PokeGold.Game.Audio` so the runtime sees
/// these types unchanged.

/// A `volume_envelope`/`note_type`/`square_note`/`noise_note` argument pair, kept
/// verbatim as the two macro arguments (`volume_envelope X, Y`). The two values
/// mean different things per channel, so we store the raw pair and decode at the
/// use site:
///  - Pulse/noise: `Volume` (0..15) is the starting volume; `Sweep` is a signed
///    fade - positive fades *out* over `|Sweep|` engine steps, negative fades in,
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

/// The GSC audio script language as a typed command stream (macros/scripts/audio.asm).
/// One channel of a song or SFX is an array of these; control-flow targets that are
/// addresses in the ROM become *command indices* once the parser resolves labels.
/// Commands the high-level synth doesn't model (the various `unknownmusic`/`musicNN`
/// hooks) parse to `NoOp` so unknown data never derails playback.
type SoundCommand =
    /// A pitched note: pitch 1..12 (C..B), length in 16ths (1..16). On the noise
    /// channel the pitch selects a drum from the active kit instead.
    | Note of pitch: int * length: int
    /// A silent note of the given length (16ths).
    | Rest of length: int
    /// An SFX square note: explicit length, envelope, and GB frequency register.
    | SquareNote of length: int * env: Envelope * freq: int
    /// An SFX noise note: explicit length, envelope, and GB noise polynomial byte.
    | NoiseNoteCmd of NoiseNote
    | Octave of int
    /// Base note length (frames per 16th) and an optional envelope (note_type).
    | NoteType of length: int * env: Envelope option
    | Transpose of octaves: int * pitches: int
    | Tempo of int
    | TempoRelative of int
    | DutyCycle of int
    | DutyCyclePattern of int * int * int * int
    | VolumeEnvelope of Envelope
    | PitchSweep of Envelope
    | Vibrato of delay: int * extent: int * rate: int
    | PitchSlide of duration: int * octave: int * pitch: int
    | PitchOffset of int
    | Volume of left: int * right: int
    | StereoPanning of left: bool * right: bool
    | ForceStereoPanning of left: bool * right: bool
    | ToggleNoise of kit: int option
    | ToggleSfx
    | SetCondition of int
    /// Control flow; the int targets are resolved command indices, not addresses.
    | SoundCall of target: int
    | SoundRet
    | SoundLoop of count: int * target: int
    | SoundJump of target: int
    | SoundJumpIf of condition: int * target: int
    /// A recognized but unmodeled command (kept so timing/structure stay intact).
    | NoOp

/// A parsed, playable song or sound effect: a shared command stream (one address
/// space, like the ROM) plus each channel's hardware id and entry point into it.
type Song =
    { ChannelCount: int
      /// (hardwareChannelId 1..8, entry command index) per channel.
      Channels: (int * int)[]
      Commands: SoundCommand[] }
