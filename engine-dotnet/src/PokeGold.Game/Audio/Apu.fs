namespace PokeGold.Game.Audio

open System

/// Which of the three Game Boy hardware voice types an APU channel is.
type internal ApuKind =
    | ApuPulse
    | ApuWave
    | ApuNoise

/// One hardware voice's live state inside the APU. The sequencer (the GSC sound
/// *driver*, in Synth.fs) writes the register-level parameters each frame; the
/// oscillator phase / LFSR persist across frames like the real hardware counters.
type internal ApuVoice =
    { Kind: ApuKind
      mutable DacOn: bool
      mutable PanL: bool
      mutable PanR: bool
      mutable Period: int          // 11-bit period register (pulse/wave)
      mutable Duty: int            // 0..3 (pulse)
      mutable Vol: int             // 0..15 envelope volume (pulse/noise)
      mutable WaveTable: int[]     // 32 nibbles (wave)
      mutable WaveMult: float      // NR32 output level 0 / .25 / .5 / 1 (wave)
      mutable NoiseFreq: float     // LFSR clock Hz (noise)
      mutable Width7: bool         // 7-bit LFSR mode (noise)
      mutable Phase: float         // oscillator phase 0..1 (pulse/wave)
      mutable NoiseAcc: float      // LFSR clock accumulator (noise)
      mutable Lfsr: int }          // 15-bit LFSR (noise)

/// A faithful, high-level model of the Game Boy / Game Boy Color APU (sound chip).
///
/// This is the audio *backend* — the counterpart to the sound *driver* that the
/// sequencer in `Synth.fs` ports from the disassembly. The driver computes, each
/// 60 Hz frame, what it would write to the hardware sound registers (period, duty,
/// volume, panning, noise polynomial, wave RAM); this turns those into PCM exactly
/// the way the chip does: square/wave/noise generators feeding 4-bit DACs, summed
/// through the NR51 stereo mixer and an analog high-pass filter.
///
/// Anti-aliasing: the generators run oversampled with analytic area-averaging of
/// each square/wave edge (so transitions land at their true sub-sample time rather
/// than snapping to the output grid), then a windowed-sinc FIR decimates to the
/// output rate. This removes the harsh aliasing of a naive per-sample square wave.
///
/// References: Pan Docs (Audio_details, Audio_Registers), gbdev wiki "Gameboy sound
/// hardware", blargg's band-limited synthesis notes.
type internal Apu(sampleRate: int, kinds: ApuKind[]) =

    // Oversampling factor for the internal generators. 16x at 44.1 kHz = 705.6 kHz,
    // comfortably above the highest GB content (wave steps, sweep SFX) so the FIR
    // can band-limit cleanly.
    let os = 16
    let osRate = float (sampleRate * os)

    let voices =
        kinds
        |> Array.map (fun k ->
            { Kind = k
              DacOn = false
              PanL = true
              PanR = true
              Period = 0
              Duty = 2
              Vol = 0
              WaveTable = Array.zeroCreate 32
              WaveMult = 0.0
              NoiseFreq = 0.0
              Width7 = false
              Phase = 0.0
              NoiseAcc = 0.0
              Lfsr = 0x7FFF })

    // The four pulse duty cycles as their *fraction* of a period spent high. The
    // GB's 8-step duty patterns are phase-rotated versions of these; rotation is
    // inaudible, so a duty fraction reproduces the same harmonic content. (Pan Docs
    // notes 25% and 75% are audibly identical for this reason.)
    let dutyFrac = [| 0.125; 0.25; 0.5; 0.75 |]

    // --- FIR decimation filter (windowed sinc low-pass at the oversampled rate) ---
    let taps = 8 * os + 1
    let coeffs =
        let m = float (taps - 1)
        let fc = 0.45 * float sampleRate / osRate   // cutoff in cycles/oversample
        let raw =
            Array.init taps (fun i ->
                let x = float i - m / 2.0
                let sinc =
                    if x = 0.0 then 2.0 * fc
                    else sin (2.0 * Math.PI * fc * x) / (Math.PI * x)
                let w = 0.5 - 0.5 * cos (2.0 * Math.PI * float i / m)   // Hann window
                sinc * w)
        let sum = Array.sum raw
        raw |> Array.map (fun v -> v / sum)         // unity DC gain
    let histL = Array.zeroCreate<float> taps
    let histR = Array.zeroCreate<float> taps
    let mutable histPos = 0

    // --- Analog DC-blocking high-pass filter (per stereo side). CGB charge factor
    // adapted from the 4.19 MHz hardware rate to our output rate. Removes the large
    // DC offset that idle/quiet channels inject through their DACs. ---
    let chargeFactor = Math.Pow(0.998943, 4194304.0 / float sampleRate)
    let mutable capL = 0.0
    let mutable capR = 0.0

    /// Fraction of the sub-step `[phase, phase+dp)` (wrapped into 0..1) that lies in
    /// the high region `[0, duty)` — analytic area-averaging of a duty-cycle pulse.
    let areaHigh (phase: float) (dp: float) (duty: float) : float =
        if dp <= 0.0 then (if phase < duty then 1.0 else 0.0)
        else
            let overlap a b = max 0.0 (min b duty - max a 0.0)
            let p2 = phase + dp
            let total =
                if p2 <= 1.0 then overlap phase p2
                else overlap phase 1.0 + overlap 0.0 (p2 - 1.0)
            total / dp

    /// One oversampled analog sample (-1..1) from a single voice, advancing its
    /// oscillator/LFSR by one oversample step. A disabled DAC contributes silence.
    let voiceSub (v: ApuVoice) : float =
        if not v.DacOn then 0.0
        else
            match v.Kind with
            | ApuPulse ->
                let f =
                    if v.Period <= 0 || v.Period >= 2048 then 0.0
                    else 131072.0 / float (2048 - v.Period)
                let dp = f / osRate
                let frac = areaHigh v.Phase dp dutyFrac.[v.Duty &&& 3]
                v.Phase <- (v.Phase + dp) % 1.0
                (frac * float v.Vol) / 7.5 - 1.0
            | ApuWave ->
                let f =
                    if v.Period <= 0 || v.Period >= 2048 then 0.0
                    else 65536.0 / float (2048 - v.Period)
                let dp = f / osRate
                let idx = int (v.Phase * 32.0) &&& 31
                v.Phase <- (v.Phase + dp) % 1.0
                (float v.WaveTable.[idx] * v.WaveMult) / 7.5 - 1.0
            | ApuNoise ->
                v.NoiseAcc <- v.NoiseAcc + v.NoiseFreq / osRate
                while v.NoiseAcc >= 1.0 do
                    v.NoiseAcc <- v.NoiseAcc - 1.0
                    let x = (v.Lfsr ^^^ (v.Lfsr >>> 1)) &&& 1
                    v.Lfsr <- (v.Lfsr >>> 1) ||| (x <<< 14)
                    if v.Width7 then v.Lfsr <- (v.Lfsr &&& ~~~0x40) ||| (x <<< 6)
                let bit = (~~~v.Lfsr) &&& 1
                let digital = if bit = 1 then float v.Vol else 0.0
                digital / 7.5 - 1.0

    let pushHist (l: float) (r: float) =
        histL.[histPos] <- l
        histR.[histPos] <- r
        histPos <- (histPos + 1) % taps

    let firDot (h: float[]) : float =
        let mutable acc = 0.0
        let mutable k = (histPos - 1 + taps) % taps
        for j in 0 .. taps - 1 do
            acc <- acc + coeffs.[j] * h.[k]
            k <- (k - 1 + taps) % taps
        acc

    member _.VoiceCount = voices.Length

    member _.SetPulse(i, dacOn, period, duty, vol, panL, panR) =
        let v = voices.[i]
        v.DacOn <- dacOn
        v.Period <- period
        v.Duty <- duty
        v.Vol <- vol
        v.PanL <- panL
        v.PanR <- panR

    member _.SetWave(i, dacOn, period, table: int[], mult, panL, panR) =
        let v = voices.[i]
        v.DacOn <- dacOn
        v.Period <- period
        v.WaveTable <- table
        v.WaveMult <- mult
        v.PanL <- panL
        v.PanR <- panR

    member _.SetNoise(i, dacOn, freq, width7, vol, panL, panR) =
        let v = voices.[i]
        v.DacOn <- dacOn
        v.NoiseFreq <- freq
        v.Width7 <- width7
        v.Vol <- vol
        v.PanL <- panL
        v.PanR <- panR

    /// Generate one output stereo sample. Runs `os` oversampled steps of every
    /// voice through the DAC + NR51 mixer into the FIR history, decimates, then
    /// applies the analog high-pass. Adds into `buffer` at `idx`/`idx+1`.
    member _.RenderOne(buffer: float32[], idx: int, gain: float) =
        for _ in 1 .. os do
            let mutable l = 0.0
            let mutable r = 0.0
            for v in voices do
                let s = voiceSub v
                if v.PanL then l <- l + s
                if v.PanR then r <- r + s
            pushHist l r

        let fl = firDot histL
        let fr = firDot histR

        let hl = fl - capL
        capL <- fl - hl * chargeFactor
        let hr = fr - capR
        capR <- fr - hr * chargeFactor

        // Four summed channels span roughly ±4 pre-filter; scale to keep headroom.
        let scale = 0.25 * gain
        buffer.[idx] <- buffer.[idx] + float32 (hl * scale)
        buffer.[idx + 1] <- buffer.[idx + 1] + float32 (hr * scale)
