namespace PokeGold.Game.Audio

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
