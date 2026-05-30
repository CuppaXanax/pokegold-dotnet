namespace PokeGold.Game.Audio

open System.Collections.Generic
open PokeGold.Game.Audio.AudioData

/// Which GB hardware voice a channel drives. Channel ids 1/5 → Pulse1, 2/6 →
/// Pulse2, 3/7 → Wave, 4/8 → Noise (5..8 are the SFX-priority aliases).
type VoiceKind =
    | Pulse1
    | Pulse2
    | Wave
    | Noise

module VoiceKind =
    let ofChannelId (id: int) : VoiceKind =
        match (id - 1) % 4 with
        | 0 -> Pulse1
        | 1 -> Pulse2
        | 2 -> Wave
        | _ -> Noise

/// Per-channel sequencer + voice runtime. One per channel in a playing song.
///
/// Pitch is carried as the live 11-bit GB *period* register value (`BasePeriod`),
/// not Hz, because vibrato, pitch slides, pitch offsets and the hardware sweep all
/// modulate that register. Each frame we fold those into an effective period and
/// convert to Hz once for the sampler.
type internal Chan =
    { Kind: VoiceKind
      mutable Pc: int
      mutable Active: bool
      CallStack: Stack<int>
      Loops: Dictionary<int, int>
      // Sequencer state set by control commands:
      mutable Octave: int
      mutable NoteLength: int
      mutable Tempo: int
      mutable Env: Envelope
      mutable Drumkit: int
      mutable TransposeOct: int
      mutable TransposePitch: int
      mutable Modifier: int
      mutable FramesLeft: int
      // Live pitch (period register) + the sampler's view of it:
      mutable BasePeriod: int
      mutable Freq: float
      mutable Phase: float
      // Volume envelope runtime (pulse/noise):
      mutable EnvVol: float
      mutable EnvCounter: int
      mutable EnvCur: Envelope
      mutable On: bool
      mutable PanL: float
      mutable PanR: float
      // Pulse duty + rotating duty pattern:
      mutable Duty: int
      mutable DutyLoop: bool
      mutable DutyPattern: int
      // Wave voice:
      mutable WaveTable: int[]
      mutable WaveVol: float
      // Noise LFSR + drum sub-sequence cursor:
      mutable Lfsr: int
      mutable NoiseAcc: float
      mutable NoiseFreq: float
      mutable NoiseWidth7: bool
      mutable DrumNotes: NoiseNote[]
      mutable DrumIdx: int
      mutable DrumSubLeft: int
      // Vibrato:
      mutable VibDelay: int
      mutable VibDelayCount: int
      mutable VibExtent: int
      mutable VibRate: int
      mutable VibRateCount: int
      mutable VibDir: bool
      // Pitch slide (per-note glide of BasePeriod toward a target):
      mutable SlideActive: bool
      mutable SlidePendingTarget: int
      mutable SlidePendingDur: int
      mutable SlideTarget: int
      mutable SlideAmount: int
      mutable SlideFrac: int
      mutable SlideAccum: int
      mutable SlideUp: bool
      // Constant pitch offset (added every frame):
      mutable PitchOffset: int
      // Hardware pitch sweep (ch1): period +/-= period>>shift every N frames.
      mutable SweepShift: int
      mutable SweepDown: bool
      mutable SweepPeriodFrames: int
      mutable SweepCount: int }

/// Software synthesizer that sequences one parsed `Song` on a ~60 Hz frame clock
/// and renders it to PCM. Faithful to the engine's timing math (note duration from
/// tempo × note-length) and to its pitch/volume effects (vibrato, pitch slide,
/// pitch offset, sweep, rotating duty), re-expressing the four GB channels as
/// ordinary oscillators driven by the live period register.
type SongPlayer(song: Song, loop: bool, sampleRate: int) =

    let framesPerSecond = 60.0
    let samplesPerFrame = float sampleRate / framesPerSecond

    let wave0 =
        if AudioData.waveSamples.Length > 0 then AudioData.waveSamples.[0]
        else Array.replicate 32 8

    let mkChan (kind: VoiceKind) (entry: int) : Chan =
        { Kind = kind
          Pc = entry
          Active = true
          CallStack = Stack<int>()
          Loops = Dictionary<int, int>()
          Octave = 4
          NoteLength = 1
          Tempo = 0x100
          Env = Envelope.silent
          Drumkit = 0
          TransposeOct = 0
          TransposePitch = 0
          Modifier = 0
          FramesLeft = 0
          BasePeriod = 0
          Freq = 0.0
          Phase = 0.0
          EnvVol = 0.0
          EnvCounter = 0
          EnvCur = Envelope.silent
          On = false
          PanL = 1.0
          PanR = 1.0
          Duty = 2
          DutyLoop = false
          DutyPattern = 0
          WaveTable = wave0
          WaveVol = 1.0
          Lfsr = 0x7FFF
          NoiseAcc = 0.0
          NoiseFreq = 0.0
          NoiseWidth7 = false
          DrumNotes = [||]
          DrumIdx = 0
          DrumSubLeft = 0
          VibDelay = 0
          VibDelayCount = 0
          VibExtent = 0
          VibRate = 0
          VibRateCount = 0
          VibDir = false
          SlideActive = false
          SlidePendingTarget = -1
          SlidePendingDur = 0
          SlideTarget = 0
          SlideAmount = 0
          SlideFrac = 0
          SlideAccum = 0
          SlideUp = false
          PitchOffset = 0
          SweepShift = 0
          SweepDown = false
          SweepPeriodFrames = 0
          SweepCount = 0 }

    let chans =
        song.Channels
        |> Array.map (fun (id, entry) -> mkChan (VoiceKind.ofChannelId id) entry)

    let cmds = song.Commands

    // Decode a GB noise polynomial byte (NR43) into an LFSR clock frequency.
    let noiseParams (nr43: int) : float * bool =
        let s = (nr43 >>> 4) &&& 0xF
        let width7 = (nr43 &&& 0x8) <> 0
        let r = nr43 &&& 0x7
        let divisor = if r = 0 then 8.0 else float (r * 16)
        let freq = 524288.0 / divisor / (2.0 ** float (s + 1))
        freq, width7

    /// Compute a note's duration in frames from tempo × (note-length × 16ths),
    /// carrying the fractional remainder forward (engine SetNoteDuration).
    let noteFrames (c: Chan) (lengthParam: int) : int =
        let low = (c.NoteLength * (max 1 lengthParam)) &&& 0xFF
        let full = c.Tempo * low + c.Modifier
        c.Modifier <- full &&& 0xFF
        full >>> 8

    /// Retrigger the volume envelope at the start of a pitched note.
    let startEnv (c: Chan) (env: Envelope) =
        c.EnvCur <- env
        c.EnvVol <- float (Envelope.initialVolume env)
        c.EnvCounter <- 0

    /// Latch a fresh pulse/wave note: set its base period, envelope/instrument,
    /// reset the per-note vibrato delay, and arm any pending pitch slide.
    let startPitchedNote (c: Chan) (period: int) (frames: int) =
        c.FramesLeft <- max 1 frames
        c.BasePeriod <- period
        c.On <- period > 0
        c.VibDelayCount <- c.VibDelay
        match c.Kind with
        | Wave ->
            let idx = Envelope.waveformIndex c.Env
            c.WaveTable <-
                if idx >= 0 && idx < AudioData.waveSamples.Length then AudioData.waveSamples.[idx]
                else wave0
            c.WaveVol <- Envelope.waveVolume c.Env
            c.EnvVol <- 15.0
            c.EnvCur <- Envelope.silent
        | _ -> startEnv c c.Env
        // Arm a pitch slide that a preceding pitch_slide command requested.
        if c.SlidePendingTarget >= 0 then
            let target = c.SlidePendingTarget
            let dur = max 1 c.SlidePendingDur
            let dist = abs (target - period)
            c.SlideActive <- true
            c.SlideUp <- target > period
            c.SlideTarget <- target
            c.SlideAmount <- dist / dur
            c.SlideFrac <- dist % dur
            c.SlideAccum <- 0
            c.SlidePendingTarget <- -1
        else
            c.SlideActive <- false
        // Re-arm the hardware sweep counter for this note.
        c.SweepCount <- c.SweepPeriodFrames

    /// Load the drum's current sub-note (its envelope + NR43 frequency) and arm its
    /// frame counter, or silence the channel when the sequence is exhausted. A drum
    /// sub-note plays `(len & 0xF) + 1` frames (GSC ReadNoiseSample).
    let loadDrumSub (c: Chan) =
        if c.DrumIdx < c.DrumNotes.Length then
            let n = c.DrumNotes.[c.DrumIdx]
            c.DrumIdx <- c.DrumIdx + 1
            c.DrumSubLeft <- (n.Length &&& 0xF) + 1
            let f, w7 = noiseParams n.Freq
            c.NoiseFreq <- f
            c.NoiseWidth7 <- w7
            startEnv c n.Env
            c.On <- true
        else
            c.On <- false

    /// Start a noise drum: load the selected drum's sub-note sequence and trigger its
    /// first sub-note immediately (GSC clears wNoiseSampleDelay so the first sample is
    /// read on the same frame).
    let startDrum (c: Chan) (drum: NoiseNote list) (frames: int) =
        c.FramesLeft <- max 1 frames
        c.DrumNotes <- List.toArray drum
        c.DrumIdx <- 0
        c.DrumSubLeft <- 0
        if List.isEmpty drum then c.On <- false else loadDrumSub c

    /// Advance the noise drum sub-sequence by one frame (independent of the main
    /// note timer): when the current sub-note's frames run out, load the next, until
    /// the sequence is exhausted.
    let stepDrum (c: Chan) =
        if c.DrumSubLeft > 0 then c.DrumSubLeft <- c.DrumSubLeft - 1
        if c.DrumSubLeft <= 0 then loadDrumSub c

    /// Run commands for a channel until one consumes time (a note/rest) or the
    /// channel ends. Control commands are processed instantly.
    let rec advance (c: Chan) (guard: int) =
        if not c.Active || guard <= 0 then ()
        elif c.Pc < 0 || c.Pc >= cmds.Length then
            c.Active <- false
        else
            let cmd = cmds.[c.Pc]
            c.Pc <- c.Pc + 1

            match cmd with
            | Note (pitch, length) ->
                if c.Kind = Noise then
                    let kit =
                        if c.Drumkit < AudioData.drumkits.Length then AudioData.drumkits.[c.Drumkit]
                        else [||]
                    // GSC seeks Drumkit[pitch] directly (the drum instrument index);
                    // pitch 0 is a rest.
                    let drum = if pitch >= 1 && pitch < kit.Length then kit.[pitch] else []
                    if pitch = 0 then
                        c.FramesLeft <- max 1 (noteFrames c length)
                        c.On <- false
                    else
                        startDrum c drum (noteFrames c length)
                else
                    let p = AudioData.notePeriod (c.Octave - c.TransposeOct) (pitch + c.TransposePitch)
                    startPitchedNote c p (noteFrames c length)
            | Rest length ->
                c.FramesLeft <- max 1 (noteFrames c length)
                c.On <- false
                c.SlideActive <- false
            | SquareNote (length, env, freq) ->
                c.Env <- env
                startEnv c env
                startPitchedNote c (freq &&& 0x7FF) (noteFrames c length)
            | NoiseNoteCmd n ->
                let f, w7 = noiseParams n.Freq
                c.NoiseFreq <- f
                c.NoiseWidth7 <- w7
                startEnv c n.Env
                c.FramesLeft <- max 1 (noteFrames c n.Length)
                c.DrumNotes <- [||]
                c.On <- true
            | Octave o -> c.Octave <- o; advance c (guard - 1)
            | NoteType (len, env) ->
                c.NoteLength <- len
                env |> Option.iter (fun e -> c.Env <- e)
                advance c (guard - 1)
            | Transpose (oct, pitch) ->
                c.TransposeOct <- oct
                c.TransposePitch <- pitch
                advance c (guard - 1)
            | Tempo t -> c.Tempo <- t; c.Modifier <- 0; advance c (guard - 1)
            | TempoRelative d -> c.Tempo <- c.Tempo + d; advance c (guard - 1)
            | DutyCycle d ->
                c.Duty <- d &&& 3
                c.DutyLoop <- false
                advance c (guard - 1)
            | DutyCyclePattern (a, b, cc, d) ->
                // Pack the four 2-bit duties [a b c d]. GSC seeds the pattern with a
                // rotate-right-2 (rrca rrca) so the first per-frame rotate-left lands
                // back on duty a; the duty then cycles a,b,c,d,... one step per frame.
                let packed = ((a &&& 3) <<< 6) ||| ((b &&& 3) <<< 4) ||| ((cc &&& 3) <<< 2) ||| (d &&& 3)
                c.DutyPattern <- ((packed >>> 2) ||| (packed <<< 6)) &&& 0xFF
                c.DutyLoop <- true
                c.Duty <- (c.DutyPattern &&& 0xC0) >>> 6
                advance c (guard - 1)
            | VolumeEnvelope e -> c.Env <- e; advance c (guard - 1)
            | ToggleNoise kit ->
                kit |> Option.iter (fun k -> c.Drumkit <- k)
                advance c (guard - 1)
            | StereoPanning (l, r)
            | ForceStereoPanning (l, r) ->
                c.PanL <- (if l then 1.0 else 0.0)
                c.PanR <- (if r then 1.0 else 0.0)
                advance c (guard - 1)
            | Vibrato (delay, extent, rate) ->
                c.VibDelay <- delay
                c.VibDelayCount <- delay
                c.VibExtent <- extent
                c.VibRate <- rate
                c.VibRateCount <- rate
                c.VibDir <- false
                advance c (guard - 1)
            | PitchSlide (duration, octave, pitch) ->
                c.SlidePendingTarget <- AudioData.notePeriod octave pitch
                c.SlidePendingDur <- duration
                advance c (guard - 1)
            | PitchOffset off -> c.PitchOffset <- off; advance c (guard - 1)
            | PitchSweep env ->
                // pitch_sweep time, shift: a positive shift sweeps the pitch up, a
                // negative shift sweeps it down. Convert the 128 Hz pace to frames.
                let time = Envelope.initialVolume env
                c.SweepDown <- env.Sweep < 0
                c.SweepShift <- abs env.Sweep &&& 0x7
                c.SweepPeriodFrames <- if time = 0 then 0 else int (float time * framesPerSecond / 128.0 + 0.5)
                c.SweepCount <- c.SweepPeriodFrames
                advance c (guard - 1)
            | SoundCall target ->
                c.CallStack.Push c.Pc
                c.Pc <- target
                advance c (guard - 1)
            | SoundRet ->
                if c.CallStack.Count > 0 then c.Pc <- c.CallStack.Pop()
                else c.Active <- false
                advance c (guard - 1)
            | SoundLoop (count, target) ->
                if count = 0 then
                    c.Pc <- target
                else
                    let key = c.Pc - 1
                    let seen = match c.Loops.TryGetValue key with true, v -> v | _ -> 0
                    if seen + 1 < count then
                        c.Loops.[key] <- seen + 1
                        c.Pc <- target
                    else
                        c.Loops.Remove key |> ignore
                advance c (guard - 1)
            | SoundJump target -> c.Pc <- target; advance c (guard - 1)
            | SoundJumpIf (_, target) -> c.Pc <- target; advance c (guard - 1)
            | SetCondition _ | Volume _ | ToggleSfx | NoOp -> advance c (guard - 1)

    /// Move BasePeriod one frame along an active pitch slide; deactivate on reach.
    let stepSlide (c: Chan) =
        if c.SlideActive then
            c.SlideAccum <- c.SlideAccum + c.SlideFrac
            let mutable step = c.SlideAmount
            if c.SlideAccum >= 256 then
                c.SlideAccum <- c.SlideAccum - 256
                step <- step + 1
            if c.SlideUp then
                c.BasePeriod <- c.BasePeriod + step
                if c.BasePeriod >= c.SlideTarget then
                    c.BasePeriod <- c.SlideTarget
                    c.SlideActive <- false
            else
                c.BasePeriod <- c.BasePeriod - step
                if c.BasePeriod <= c.SlideTarget then
                    c.BasePeriod <- c.SlideTarget
                    c.SlideActive <- false

    /// Apply the hardware sweep to BasePeriod (ch1), every SweepPeriodFrames.
    let stepSweep (c: Chan) =
        if c.SweepPeriodFrames > 0 && c.SweepShift > 0 && c.On then
            c.SweepCount <- c.SweepCount - 1
            if c.SweepCount <= 0 then
                c.SweepCount <- c.SweepPeriodFrames
                let delta = c.BasePeriod >>> c.SweepShift
                c.BasePeriod <- if c.SweepDown then c.BasePeriod - delta else c.BasePeriod + delta

    /// This frame's vibrato perturbation (in period units). GSC only nudges the
    /// pitch on a *toggle* frame, alternating up/down, after the per-note delay.
    let vibratoOffset (c: Chan) : int =
        if c.VibExtent = 0 then 0
        elif c.VibDelayCount > 0 then
            c.VibDelayCount <- c.VibDelayCount - 1
            0
        elif c.VibRateCount <= 0 then
            c.VibRateCount <- c.VibRate
            c.VibDir <- not c.VibDir
            let half = max 1 (c.VibExtent / 2)
            if c.VibDir then half else -half
        else
            c.VibRateCount <- c.VibRateCount - 1
            0

    /// Advance every channel by one 60 Hz frame and recompute its sampler Hz.
    let stepFrame () =
        for c in chans do
            if c.Active then
                if c.FramesLeft > 0 then c.FramesLeft <- c.FramesLeft - 1

                // Volume envelope fade (pulse/noise).
                if c.On && c.Kind <> Wave && Envelope.period c.EnvCur > 0 then
                    c.EnvCounter <- c.EnvCounter + 1
                    if c.EnvCounter >= Envelope.period c.EnvCur then
                        c.EnvCounter <- 0
                        c.EnvVol <-
                            if Envelope.increase c.EnvCur then min 15.0 (c.EnvVol + 1.0)
                            else max 0.0 (c.EnvVol - 1.0)

                if c.Kind = Noise then stepDrum c

                stepSlide c
                stepSweep c

                if c.FramesLeft <= 0 then advance c 1024

                // GSC's HandleTrackVibrato rotates the duty-cycle pattern one step per
                // frame (rlca rlca) and takes the top 2 bits as this frame's duty — a
                // ~15 Hz PWM shimmer that brightens the pulse timbre.
                if c.DutyLoop then
                    c.DutyPattern <- ((c.DutyPattern <<< 2) ||| (c.DutyPattern >>> 6)) &&& 0xFF
                    c.Duty <- (c.DutyPattern &&& 0xC0) >>> 6

                // Fold per-frame pitch effects into an effective period → Hz.
                if c.Kind <> Noise then
                    let eff = c.BasePeriod + c.PitchOffset + vibratoOffset c
                    let clamped = max 1 (min 2047 eff)
                    c.Freq <- AudioData.periodToHz clamped

        if loop && Array.forall (fun (c: Chan) -> not c.Active) chans then
            song.Channels
            |> Array.iteri (fun i (_, entry) ->
                let c = chans.[i]
                c.Pc <- entry
                c.Active <- true
                c.CallStack.Clear()
                c.Loops.Clear())

    /// One voice's instantaneous sample in [-1, 1].
    let voiceSample (c: Chan) : float =
        if not c.On then 0.0
        else
            match c.Kind with
            | Pulse1
            | Pulse2 ->
                if c.EnvVol <= 0.0 then 0.0
                else
                    let amp = c.EnvVol / 15.0
                    let duty = [| 0.125; 0.25; 0.5; 0.75 |].[c.Duty]
                    if c.Phase < duty then amp else -amp
            | Wave ->
                if c.WaveVol <= 0.0 then 0.0
                else
                    let i = int (c.Phase * 32.0) &&& 31
                    (float c.WaveTable.[i] / 7.5 - 1.0) * c.WaveVol
            | Noise ->
                if c.EnvVol <= 0.0 then 0.0
                else
                    let amp = c.EnvVol / 15.0
                    let bit = (~~~c.Lfsr) &&& 1
                    if bit = 1 then amp else -amp

    /// Advance a voice's oscillator phase by one output sample. The wave channel
    /// runs an octave below a pulse channel for the same period register.
    let advancePhase (c: Chan) =
        match c.Kind with
        | Noise ->
            c.NoiseAcc <- c.NoiseAcc + c.NoiseFreq / float sampleRate
            while c.NoiseAcc >= 1.0 do
                c.NoiseAcc <- c.NoiseAcc - 1.0
                let x = (c.Lfsr ^^^ (c.Lfsr >>> 1)) &&& 1
                c.Lfsr <- (c.Lfsr >>> 1) ||| (x <<< 14)
                if c.NoiseWidth7 then c.Lfsr <- (c.Lfsr &&& ~~~0x40) ||| (x <<< 6)
        | Wave -> c.Phase <- (c.Phase + (c.Freq * 0.5) / float sampleRate) % 1.0
        | _ -> c.Phase <- (c.Phase + c.Freq / float sampleRate) % 1.0

    let mutable sampleAcc = 0.0

    /// True once every channel has run to completion (only meaningful when not
    /// looping); lets the engine retire a finished SFX.
    member _.Finished = (not loop) && Array.forall (fun (c: Chan) -> not c.Active) chans

    /// Render `nFrames` stereo sample-frames, *adding* into the interleaved buffer
    /// at `offset` (so multiple players mix). `gain` scales this song's output.
    member _.Render(buffer: float32[], offset: int, nFrames: int, gain: float) =
        for n in 0 .. nFrames - 1 do
            if sampleAcc <= 0.0 then
                stepFrame ()
                sampleAcc <- sampleAcc + samplesPerFrame

            let mutable l = 0.0
            let mutable r = 0.0
            for c in chans do
                if c.On then
                    let s = voiceSample c
                    l <- l + s * c.PanL
                    r <- r + s * c.PanR
                advancePhase c

            sampleAcc <- sampleAcc - 1.0
            let idx = offset + n * 2
            buffer.[idx] <- buffer.[idx] + float32 (l * gain)
            buffer.[idx + 1] <- buffer.[idx + 1] + float32 (r * gain)
