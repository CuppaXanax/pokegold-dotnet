namespace PokeGold.Game.Audio

open System

/// Which of the three Game Boy hardware voice types an APU channel is.
type internal ApuKind =
    | ApuPulse
    | ApuWave
    | ApuNoise

/// One hardware voice's live state inside the APU. The sequencer (the GSC sound
/// *driver*, in Synth.fs) writes the register-level parameters each frame; the
/// period timer / waveframe / LFSR free-run across frames exactly like the real
/// hardware counters (clocked in CPU cycles).
type internal ApuVoice =
    { Kind: ApuKind
      mutable DacOn: bool
      mutable PanL: bool
      mutable PanR: bool
      mutable Period: int            // channel period in CPU cycles (per waveframe tick)
      mutable PeriodTimer: int       // CPU-cycle countdown to the next waveframe tick
      mutable WaveFrame: int         // duty step 0..7 (pulse) / sample step 0..31 (wave)
      mutable Duty: int              // 0..3 (pulse)
      mutable Vol: int               // 0..15 envelope volume (pulse/noise)
      mutable WaveBytes: int[]       // 16 bytes of wave RAM (wave)
      mutable VolShift: int          // NR32 output shift 0/1/2 or >=4 = mute (wave)
      mutable Lfsr: int              // 15-bit LFSR shift register (noise)
      mutable LfsrFeed: int }        // feedback mask: 0x4000 (15-bit) or 0x4040 (7-bit)

/// A faithful, bit-level model of the Game Boy / Game Boy Color APU (sound chip),
/// ported directly from PyBoy's `core/sound.py` — the project's ground-truth
/// reference oracle.
///
/// This is the audio *backend* — the counterpart to the sound *driver* that the
/// sequencer in `Synth.fs` ports from the disassembly. The driver computes, each
/// 60 Hz frame, the register-level intent (period, duty, volume, panning, noise
/// polynomial, wave RAM); this turns those into PCM exactly the way PyBoy's chip
/// model does.
///
/// Synthesis model (PyBoy-identical): each channel runs a period timer measured in
/// CPU cycles. Every output sample advances the chip by `cyclesPerSample` CPU
/// cycles; whenever a channel's timer underflows, its waveframe (pulse/wave) or
/// LFSR (noise) advances. The chip is then *point-sampled* — each channel's
/// instantaneous 0..15 DAC value is read, the enabled-per-side channels are summed
/// and clamped to 0..127, exactly as PyBoy's `Sound.sample()` does. This naive
/// point-sampling (rather than band-limited area-averaging) reproduces the
/// reference's characteristic high-harmonic content — which is the whole point of
/// the bit-faithful path.
///
/// Reference: PyBoy `pyboy/core/sound.py` (Sound, ToneChannel, SweepChannel,
/// WaveChannel, NoiseChannel).
type internal Apu(sampleRate: int, kinds: ApuKind[]) =

    // CPU cycles advanced per output sample. Uses the physically correct CPU clock
    // (4194304 Hz) so the APU stays consistent with the sequencer's frame clock:
    // Synth's samplesPerFrame * cyclesPerSample == 70224 cycles/frame exactly. (PyBoy
    // instead approximates 60 fps -> 70224/(sr//60) = 95.543, a ~0.45% slowdown; we
    // keep the true rate so our APU and our proven sequencer share one timebase.)
    // Fractional cycles are accumulated so the integer tick count stays exact.
    let cyclesPerSample = 4194304.0 / float sampleRate
    let mutable cycleAcc = 0.0

    // The four pulse duty patterns (PyBoy ToneChannel.wavetables), each an 8-step
    // bit pattern read by waveframe. 12.5% / 25% / 50% / 75%.
    let dutyTable =
        [| [| 0; 0; 0; 0; 0; 0; 0; 1 |]
           [| 1; 0; 0; 0; 0; 0; 0; 1 |]
           [| 1; 0; 0; 0; 0; 1; 1; 1 |]
           [| 0; 1; 1; 1; 1; 1; 1; 0 |] |]

    let divTable = [| 8; 16; 32; 48; 64; 80; 96; 112 |]

    let voices =
        kinds
        |> Array.map (fun k ->
            { Kind = k
              DacOn = false
              PanL = true
              PanR = true
              Period = 0
              PeriodTimer = 0
              WaveFrame = 0
              Duty = 2
              Vol = 0
              WaveBytes = Array.zeroCreate 16
              VolShift = 0
              Lfsr = 0x7FFF
              LfsrFeed = 0x4000 })

    // --- DC-blocking high-pass filter (per stereo side). One-pole; placed at a
    // near-DC corner because the PyBoy reference applies NO analog filter (the gate
    // compares the raw DAC sum, mean-subtracted) — so this only removes the channel
    // mix's static DC offset for clean host playback without tilting the low band.
    // Overridable via POKEGOLD_APU_HPF_HZ. ---
    let hpfHz =
        match Environment.GetEnvironmentVariable "POKEGOLD_APU_HPF_HZ" with
        | null | "" -> 0.4
        | s -> (match Double.TryParse s with | true, v -> v | _ -> 0.4)
    let chargeFactor = 1.0 - (2.0 * Math.PI * hpfHz / float sampleRate)
    let mutable capL = 0.0
    let mutable capR = 0.0

    /// Advance one voice's free-running counter by `cycles` CPU cycles, stepping its
    /// waveframe (pulse/wave) or LFSR (noise) on each period underflow — PyBoy
    /// `ToneChannel/WaveChannel/NoiseChannel.tick`.
    let tick (v: ApuVoice) (cycles: int) =
        if v.Period > 0 then
            v.PeriodTimer <- v.PeriodTimer - cycles
            match v.Kind with
            | ApuPulse ->
                while v.PeriodTimer <= 0 do
                    v.PeriodTimer <- v.PeriodTimer + v.Period
                    v.WaveFrame <- (v.WaveFrame + 1) &&& 7
            | ApuWave ->
                while v.PeriodTimer <= 0 do
                    v.PeriodTimer <- v.PeriodTimer + v.Period
                    v.WaveFrame <- (v.WaveFrame + 1) &&& 31
            | ApuNoise ->
                while v.PeriodTimer <= 0 do
                    v.PeriodTimer <- v.PeriodTimer + v.Period
                    let tap = v.Lfsr
                    v.Lfsr <- v.Lfsr >>> 1
                    let bit = (tap ^^^ v.Lfsr) &&& 1
                    if bit <> 0 then v.Lfsr <- v.Lfsr ||| v.LfsrFeed
                    else v.Lfsr <- v.Lfsr &&& ~~~v.LfsrFeed

    /// Point-sample one voice's instantaneous DAC value (0..15) — PyBoy `sample()`.
    let sample (v: ApuVoice) : int =
        if not v.DacOn then 0
        else
            match v.Kind with
            | ApuPulse -> v.Vol * dutyTable.[v.Duty &&& 3].[v.WaveFrame &&& 7]
            | ApuWave ->
                let mutable s = v.WaveBytes.[(v.WaveFrame >>> 1) &&& 15]
                if v.WaveFrame &&& 1 = 1 then s <- s >>> 4
                s <- s &&& 0x0F
                if v.VolShift >= 4 then 0 else s >>> v.VolShift
            | ApuNoise -> if v.Lfsr &&& 1 = 0 then v.Vol else 0

    member _.VoiceCount = voices.Length

    member _.SetPulse(i, dacOn, period, duty, vol, panL, panR) =
        let v = voices.[i]
        v.DacOn <- dacOn
        // PyBoy ToneChannel: period (CPU cycles) = 4 * (0x800 - sound_period).
        v.Period <- 4 * (0x800 - (period &&& 0x7FF))
        v.Duty <- duty
        v.Vol <- vol
        v.PanL <- panL
        v.PanR <- panR

    member _.SetWave(i, dacOn, period, table: int[], mult, panL, panR) =
        let v = voices.[i]
        v.DacOn <- dacOn
        // PyBoy WaveChannel: period (CPU cycles) = 2 * (0x800 - sound_period).
        v.Period <- 2 * (0x800 - (period &&& 0x7FF))
        // Pack the 32 nibbles (high-nibble-first ROM order) into 16 wave-RAM bytes,
        // then read them with PyBoy's exact nibble extraction in `sample`.
        for k in 0 .. 15 do
            v.WaveBytes.[k] <- ((table.[2 * k] &&& 0xF) <<< 4) ||| (table.[2 * k + 1] &&& 0xF)
        // NR32 level (1.0/0.5/0.25/0.0 from the driver) -> PyBoy volume shift.
        v.VolShift <-
            if mult >= 0.99 then 0
            elif mult >= 0.49 then 1
            elif mult >= 0.24 then 2
            else 4
        v.PanL <- panL
        v.PanR <- panR

    member _.SetNoise(i, dacOn, nr43: int, vol, panL, panR) =
        let v = voices.[i]
        let clkPow = (nr43 >>> 4) &&& 0xF
        let regWidth = (nr43 >>> 3) &&& 1
        let clkDiv = nr43 &&& 0x7
        // PyBoy NoiseChannel: DIVTABLE[clkdiv] << clkpow CPU cycles; 7-bit width
        // feeds bit 6 as well as bit 14 (mask 0x4040 vs 0x4000).
        let newPeriod = divTable.[clkDiv] <<< clkPow
        let newFeed = if regWidth = 1 then 0x4040 else 0x4000
        // Re-trigger the LFSR on a fresh drum hit (dac rising or polynomial change),
        // mirroring PyBoy's trigger (shiftregister = 0x7FFF).
        if (dacOn && not v.DacOn) || newPeriod <> v.Period || newFeed <> v.LfsrFeed then
            v.Lfsr <- 0x7FFF
        v.DacOn <- dacOn
        v.Period <- newPeriod
        v.LfsrFeed <- newFeed
        v.Vol <- vol
        v.PanL <- panL
        v.PanR <- panR

    /// Generate one output stereo sample. Advances the chip by `cyclesPerSample`
    /// CPU cycles, point-samples every voice, sums the enabled-per-side channels
    /// (clamped 0..127 like PyBoy `Sound.sample`), DC-blocks, scales, and adds into
    /// `buffer` at `idx`/`idx+1`.
    member _.RenderOne(buffer: float32[], idx: int, gain: float) =
        cycleAcc <- cycleAcc + cyclesPerSample
        let step = int cycleAcc
        cycleAcc <- cycleAcc - float step
        if step > 0 then
            for v in voices do
                tick v step

        let mutable sumL = 0
        let mutable sumR = 0
        for v in voices do
            let s = sample v
            if v.PanL then sumL <- sumL + s
            if v.PanR then sumR <- sumR + s
        let sumL = if sumL > 127 then 127 else sumL
        let sumR = if sumR > 127 then 127 else sumR

        let fl = float sumL
        let fr = float sumR
        let hl = fl - capL
        capL <- fl - hl * chargeFactor
        let hr = fr - capR
        capR <- fr - hr * chargeFactor

        // The summed sides span 0..127 (AC component ~±30 typical); scale to keep
        // headroom while staying audible. Gate is scale-invariant; this is cosmetic.
        let scale = gain / 40.0
        let clamp (x: float) = if x > 1.0 then 1.0 elif x < -1.0 then -1.0 else x
        buffer.[idx] <- buffer.[idx] + float32 (clamp (hl * scale))
        buffer.[idx + 1] <- buffer.[idx + 1] + float32 (clamp (hr * scale))
