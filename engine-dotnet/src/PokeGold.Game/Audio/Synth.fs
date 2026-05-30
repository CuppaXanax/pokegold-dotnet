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
type internal Chan =
    { Kind: VoiceKind
      mutable Pc: int
      mutable Active: bool
      CallStack: Stack<int>
      Loops: Dictionary<int, int>
      mutable Octave: int
      mutable NoteLength: int
      mutable Tempo: int
      mutable Duty: int
      mutable Env: Envelope
      mutable Drumkit: int
      mutable TransposeOct: int
      mutable Modifier: int
      mutable FramesLeft: int
      // Live voice parameters used by the sampler:
      mutable Freq: float
      mutable Phase: float
      mutable EnvVol: float
      mutable EnvCounter: int
      mutable EnvCur: Envelope
      mutable On: bool
      mutable PanL: float
      mutable PanR: float
      mutable Wave: int[]
      // Noise LFSR runtime:
      mutable Lfsr: int
      mutable NoiseAcc: float
      mutable NoiseFreq: float
      mutable NoiseWidth7: bool }

/// Software synthesizer that sequences one parsed `Song` on a ~60 Hz frame clock
/// and renders it to PCM. Faithful to the engine's timing math (note duration from
/// tempo × note-length, free-running envelopes) while re-expressing the four GB
/// channels as ordinary oscillators. One `SongPlayer` plays one song; the
/// `AudioEngine` mixes a looping music player with transient SFX players.
type SongPlayer(song: Song, loop: bool, sampleRate: int) =

    // ~59.7 Hz on hardware; 60 is imperceptibly close and keeps the math clean.
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
          Duty = 2
          Env = { InitialVolume = 15; Increase = false; Period = 0 }
          Drumkit = 0
          TransposeOct = 0
          Modifier = 0
          FramesLeft = 0
          Freq = 0.0
          Phase = 0.0
          EnvVol = 0.0
          EnvCounter = 0
          EnvCur = Envelope.silent
          On = false
          PanL = 1.0
          PanR = 1.0
          Wave = wave0
          Lfsr = 0x7FFF
          NoiseAcc = 0.0
          NoiseFreq = 0.0
          NoiseWidth7 = false }

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

    /// Begin sounding a note: latch frequency/envelope and retrigger the envelope.
    let startNote (c: Chan) (freqHz: float) (env: Envelope) (frames: int) =
        c.FramesLeft <- max 1 frames
        c.Freq <- freqHz
        c.EnvCur <- env
        c.EnvVol <- float env.InitialVolume
        c.EnvCounter <- 0
        c.On <- freqHz > 0.0 || c.Kind = Noise

    /// Compute a note's duration in frames from tempo × (note-length × 16ths),
    /// carrying the fractional remainder forward (engine SetNoteDuration).
    let noteFrames (c: Chan) (lengthParam: int) : int =
        let low = (c.NoteLength * (max 1 lengthParam)) &&& 0xFF
        let full = c.Tempo * low + c.Modifier
        c.Modifier <- full &&& 0xFF
        full >>> 8

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
                    let idx = pitch - 1
                    let drum = if idx >= 0 && idx < kit.Length then kit.[idx] else []
                    let frames = noteFrames c length
                    match drum with
                    | first :: _ ->
                        let f, w7 = noiseParams first.Freq
                        c.NoiseFreq <- f
                        c.NoiseWidth7 <- w7
                        startNote c 0.0 first.Env frames
                    | [] -> startNote c 0.0 Envelope.silent frames
                else
                    let frames = noteFrames c length
                    let hz = AudioData.noteFrequency (c.Octave + c.TransposeOct) pitch
                    startNote c hz c.Env frames
            | Rest length ->
                let frames = noteFrames c length
                c.FramesLeft <- max 1 frames
                c.On <- false
            | SquareNote (length, env, freq) ->
                let frames = noteFrames c length
                let p = freq &&& 0x7FF
                let hz = if p >= 2048 then 0.0 else 131072.0 / float (2048 - p)
                startNote c hz env frames
            | NoiseNoteCmd n ->
                let frames = noteFrames c n.Length
                let f, w7 = noiseParams n.Freq
                c.NoiseFreq <- f
                c.NoiseWidth7 <- w7
                startNote c 0.0 n.Env frames
            | Octave o -> c.Octave <- o; advance c (guard - 1)
            | NoteType (len, env) ->
                c.NoteLength <- len
                env |> Option.iter (fun e -> c.Env <- e)
                advance c (guard - 1)
            | Transpose (oct, _) -> c.TransposeOct <- oct; advance c (guard - 1)
            | Tempo t -> c.Tempo <- t; c.Modifier <- 0; advance c (guard - 1)
            | TempoRelative d -> c.Tempo <- c.Tempo + d; advance c (guard - 1)
            | DutyCycle d -> c.Duty <- d &&& 3; advance c (guard - 1)
            | DutyCyclePattern (a, _, _, _) -> c.Duty <- a &&& 3; advance c (guard - 1)
            | VolumeEnvelope e -> c.Env <- e; advance c (guard - 1)
            | ToggleNoise kit ->
                kit |> Option.iter (fun k -> c.Drumkit <- k)
                advance c (guard - 1)
            | StereoPanning (l, r)
            | ForceStereoPanning (l, r) ->
                c.PanL <- (if l then 1.0 else 0.0)
                c.PanR <- (if r then 1.0 else 0.0)
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
            // Effects/hooks not modeled at the sample level: keep timing intact.
            | SetCondition _ | Vibrato _ | PitchSlide _ | PitchSweep _ | PitchOffset _
            | Volume _ | ToggleSfx | NoOp -> advance c (guard - 1)

    /// Advance every channel by one 60 Hz frame.
    let stepFrame () =
        for c in chans do
            if c.Active then
                if c.FramesLeft > 0 then
                    c.FramesLeft <- c.FramesLeft - 1
                    if c.On && c.EnvCur.Period > 0 then
                        c.EnvCounter <- c.EnvCounter + 1
                        if c.EnvCounter >= c.EnvCur.Period then
                            c.EnvCounter <- 0
                            c.EnvVol <-
                                if c.EnvCur.Increase then min 15.0 (c.EnvVol + 1.0)
                                else max 0.0 (c.EnvVol - 1.0)
                if c.FramesLeft <= 0 then advance c 1024

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
        if not c.On || c.EnvVol <= 0.0 then 0.0
        else
            let amp = c.EnvVol / 15.0
            match c.Kind with
            | Pulse1
            | Pulse2 ->
                let duty = [| 0.125; 0.25; 0.5; 0.75 |].[c.Duty]
                if c.Phase < duty then amp else -amp
            | Wave ->
                let i = int (c.Phase * 32.0) &&& 31
                (float c.Wave.[i] / 7.5 - 1.0) * amp
            | Noise ->
                let bit = (~~~c.Lfsr) &&& 1
                (if bit = 1 then amp else -amp)

    /// Advance a voice's oscillator phase by one output sample.
    let advancePhase (c: Chan) =
        match c.Kind with
        | Noise ->
            c.NoiseAcc <- c.NoiseAcc + c.NoiseFreq / float sampleRate
            while c.NoiseAcc >= 1.0 do
                c.NoiseAcc <- c.NoiseAcc - 1.0
                let x = (c.Lfsr ^^^ (c.Lfsr >>> 1)) &&& 1
                c.Lfsr <- (c.Lfsr >>> 1) ||| (x <<< 14)
                if c.NoiseWidth7 then c.Lfsr <- (c.Lfsr &&& ~~~0x40) ||| (x <<< 6)
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
