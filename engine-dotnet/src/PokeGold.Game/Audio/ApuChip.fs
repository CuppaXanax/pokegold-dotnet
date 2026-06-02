namespace PokeGold.Game.Audio

open System

/// A faithful, register-level F# port of PyBoy's `core/sound.py` — the project's
/// ground-truth audio oracle. Unlike the older `Apu.fs` (which point-samples duty
/// patterns but relies on the sequencer to pre-compute volume), this models the
/// FULL hardware: a 512 Hz DIV-APU frame sequencer that clocks each channel's
/// volume envelope (64 Hz), length counter (256 Hz) and CH1 frequency sweep
/// (128 Hz) internally, exactly like the chip. It is driven purely by register
/// writes (`WriteReg offset value`, offsets 0..47 == $FF10..$FF3F) and triggers,
/// so feeding it the real driver's register stream reproduces PyBoy sample-for-sample.
///
/// Reference: PyBoy `pyboy/core/sound.py` (Sound, ToneChannel, SweepChannel,
/// WaveChannel, NoiseChannel). Timebase constants match PyBoy exactly.
module internal ApuChipConstants =
    [<Literal>]
    let FRAME_CYCLES = 70224
    [<Literal>]
    let CYCLES_512HZ = 8192

    /// The four pulse duty patterns (PyBoy ToneChannel.wavetables): 12.5/25/50/75%.
    let dutyTable =
        [| [| 0; 0; 0; 0; 0; 0; 0; 1 |]
           [| 1; 0; 0; 0; 0; 0; 0; 1 |]
           [| 1; 0; 0; 0; 0; 1; 1; 1 |]
           [| 0; 1; 1; 1; 1; 1; 1; 0 |] |]

    let divTable = [| 8; 16; 32; 48; 64; 80; 96; 112 |]

open ApuChipConstants

/// Pulse channel (PyBoy ToneChannel). `hasSweep` makes it CH1 (PyBoy SweepChannel).
type internal PulseChip(hasSweep: bool) =
    // Register-derived state.
    let mutable waveDuty = 0
    let mutable envelopeVolume = 0
    let mutable envelopeDirection = 0
    let mutable envelopePace = 0
    let mutable soundPeriod = 0
    let mutable lengthEnable = 0
    // Sweep (CH1 only).
    let mutable sweepPace = 0
    let mutable sweepDirection = 0
    let mutable sweepMagnitude = 0
    let mutable sweeptimer = 0
    let mutable sweepenable = false
    let mutable shadow = 0
    // Internal counters.
    let mutable enable = 0
    let mutable lengthtimer = 64
    let mutable envelopetimer = 0
    let mutable periodtimer = 0
    let mutable period = 4
    let mutable waveframe = 0
    let mutable volume = 0

    member _.Enable = enable

    /// PyBoy SweepChannel.sweep(save): returns true only when a new period was saved.
    member private _.DoSweep(save: bool) : bool =
        let newper =
            if sweepDirection = 0 then shadow + (shadow >>> sweepMagnitude)
            else shadow - (shadow >>> sweepMagnitude)
        if newper >= 0x800 then
            enable <- 0
            false
        elif save && sweepMagnitude <> 0 then
            soundPeriod <- newper
            shadow <- newper
            period <- 4 * (0x800 - soundPeriod)
            true
        else
            false

    member this.Trigger() =
        enable <- 0x02
        lengthtimer <- (if lengthtimer = 0 then 64 else lengthtimer)
        periodtimer <- period
        envelopetimer <- envelopePace
        volume <- envelopeVolume
        if hasSweep then
            if enable <> 0 then enable <- 0x01
            shadow <- soundPeriod
            sweeptimer <- sweepPace
            sweepenable <- (sweepPace <> 0 || sweepMagnitude <> 0)
            if sweepMagnitude <> 0 then this.DoSweep(false) |> ignore
        // DAC off (init vol 0 + decrease) immediately disables.
        if envelopeDirection = 0 && envelopeVolume = 0 then enable <- 0

    /// `reg` 0..4 == NRx0..NRx4. `forceLength` mirrors PyBoy's monochrome power-off path.
    member this.SetReg(reg: int, value: int, forceLength: bool) =
        match reg with
        | 0 ->
            if hasSweep then
                sweepPace <- (value >>> 4) &&& 0x07
                sweepDirection <- (value >>> 3) &&& 0x01
                sweepMagnitude <- value &&& 0x07
        | 1 ->
            if not forceLength then waveDuty <- (value >>> 6) &&& 0x03
            let initLen = value &&& 0x3F
            lengthtimer <- 64 - initLen
        | 2 ->
            envelopeVolume <- (value >>> 4) &&& 0x0F
            envelopeDirection <- (value >>> 3) &&& 0x01
            envelopePace <- value &&& 0x07
            if envelopeVolume = 0 && envelopeDirection = 0 then enable <- 0
        | 3 ->
            soundPeriod <- (soundPeriod &&& 0x700) ||| value
            period <- 4 * (0x800 - soundPeriod)
        | 4 ->
            lengthEnable <- (value >>> 6) &&& 0x01
            soundPeriod <- ((value <<< 8) &&& 0x0700) ||| (soundPeriod &&& 0xFF)
            period <- 4 * (0x800 - soundPeriod)
            if value &&& 0x80 <> 0 then this.Trigger()
        | _ -> ()

    member _.Tick(cycles: int) =
        if period > 0 then
            periodtimer <- periodtimer - cycles
            while periodtimer <= 0 do
                periodtimer <- periodtimer + period
                waveframe <- (waveframe + 1) % 8

    member _.TickLength() =
        if lengthEnable <> 0 && lengthtimer > 0 then
            lengthtimer <- lengthtimer - 1
            if lengthtimer = 0 then enable <- 0

    member _.TickEnvelope() =
        if envelopetimer <> 0 then
            envelopetimer <- envelopetimer - 1
            if envelopetimer = 0 then
                let newvolume = volume + (if envelopeDirection <> 0 then 1 else -1)
                if newvolume < 0 || newvolume > 15 then envelopetimer <- 0
                else
                    envelopetimer <- envelopePace
                    volume <- newvolume

    member this.TickSweep() =
        if hasSweep && sweepenable && sweepPace <> 0 then
            sweeptimer <- sweeptimer - 1
            if sweeptimer = 0 then
                if this.DoSweep(true) then
                    sweeptimer <- sweepPace
                    this.DoSweep(false) |> ignore

    member _.Sample() : int =
        if enable <> 0 then volume * dutyTable.[waveDuty].[waveframe]
        else 0


type internal WaveChip(cgb: bool) =
    let wavetable = Array.create 16 0xFF
    let mutable dacpow = 0
    let mutable volreg = 0
    let mutable soundPeriod = 0
    let mutable lengthEnable = 0
    let mutable enable = 0
    let mutable lengthtimer = 256
    let mutable periodtimer = 0
    let mutable period = 4
    let mutable waveframe = 0
    let mutable volumeshift = 0

    member _.Enable = enable

    member _.Trigger() =
        enable <- (if dacpow <> 0 then 0x04 else 0)
        lengthtimer <- (if lengthtimer = 0 then 256 else lengthtimer)
        periodtimer <- period

    member this.SetReg(reg: int, value: int) =
        match reg with
        | 0 ->
            dacpow <- (value >>> 7) &&& 0x01
            if dacpow = 0 then enable <- 0
        | 1 ->
            lengthtimer <- 256 - value
        | 2 ->
            volreg <- (value >>> 5) &&& 0x03
            volumeshift <- (if volreg > 0 then volreg - 1 else 4)
        | 3 ->
            soundPeriod <- (soundPeriod &&& 0x700) + value
            period <- 2 * (0x800 - soundPeriod)
        | 4 ->
            lengthEnable <- (value >>> 6) &&& 0x01
            soundPeriod <- ((value <<< 8) &&& 0x0700) + (soundPeriod &&& 0xFF)
            period <- 2 * (0x800 - soundPeriod)
            if value &&& 0x80 <> 0 then this.Trigger()
        | _ -> ()

    /// Wave RAM write: PyBoy routes to live waveframe slot when the channel is active
    /// on CGB; otherwise to the addressed byte. The driver writes wave RAM while the
    /// channel is off, so the addressed path is what matters here.
    member _.SetWaveByte(offset: int, value: int) =
        if enable <> 0 then
            if cgb then wavetable.[waveframe % 16] <- value
        else
            wavetable.[offset] <- value

    member _.Tick(cycles: int) =
        if period > 0 then
            periodtimer <- periodtimer - cycles
            while periodtimer <= 0 do
                periodtimer <- periodtimer + period
                waveframe <- (waveframe + 1) % 32

    member _.TickLength() =
        if lengthEnable <> 0 && lengthtimer > 0 then
            lengthtimer <- lengthtimer - 1
            if lengthtimer = 0 then enable <- 0

    member _.Sample() : int =
        if enable <> 0 && dacpow <> 0 then
            let mutable s = wavetable.[waveframe / 2]
            if waveframe % 2 = 1 then s <- s >>> 4
            s <- s &&& 0x0F
            s >>> volumeshift
        else 0


type internal NoiseChip() =
    let mutable envelopeVolume = 0
    let mutable envelopeDirection = 0
    let mutable envelopePace = 0
    let mutable clkpow = 0
    let mutable regwid = 0
    let mutable clkdiv = 0
    let mutable lengthEnable = 0
    let mutable enable = 0
    let mutable lengthtimer = 64
    let mutable periodtimer = 0
    let mutable envelopetimer = 0
    let mutable period = 8
    let mutable shiftregister = 1
    let mutable lfsrfeed = 0x4000
    let mutable volume = 0

    member _.Enable = enable

    member _.Trigger() =
        enable <- 0x08
        lengthtimer <- (if lengthtimer = 0 then 64 else lengthtimer)
        periodtimer <- period
        envelopetimer <- envelopePace
        volume <- envelopeVolume
        shiftregister <- 0x7FFF
        if envelopeDirection = 0 && envelopeVolume = 0 then enable <- 0

    member this.SetReg(reg: int, value: int) =
        match reg with
        | 0 -> ()
        | 1 -> lengthtimer <- 64 - (value &&& 0x3F)
        | 2 ->
            envelopeVolume <- (value >>> 4) &&& 0x0F
            envelopeDirection <- (value >>> 3) &&& 0x01
            envelopePace <- value &&& 0x07
            if envelopeVolume = 0 && envelopeDirection = 0 then enable <- 0
        | 3 ->
            clkpow <- (value >>> 4) &&& 0x0F
            regwid <- (value >>> 3) &&& 0x01
            clkdiv <- value &&& 0x07
            period <- divTable.[clkdiv] <<< clkpow
            lfsrfeed <- (if regwid <> 0 then 0x4040 else 0x4000)
        | 4 ->
            lengthEnable <- (value >>> 6) &&& 0x01
            if value &&& 0x80 <> 0 then this.Trigger()
        | _ -> ()

    member _.Tick(cycles: int) =
        if period > 0 then
            periodtimer <- periodtimer - cycles
            while periodtimer <= 0 do
                periodtimer <- periodtimer + period
                let mutable tap = shiftregister
                shiftregister <- shiftregister >>> 1
                tap <- tap ^^^ shiftregister
                if tap &&& 0x01 <> 0 then shiftregister <- shiftregister ||| lfsrfeed
                else shiftregister <- shiftregister &&& ~~~lfsrfeed

    member _.TickLength() =
        if lengthEnable <> 0 && lengthtimer > 0 then
            lengthtimer <- lengthtimer - 1
            if lengthtimer = 0 then enable <- 0

    member _.TickEnvelope() =
        if envelopetimer <> 0 then
            envelopetimer <- envelopetimer - 1
            if envelopetimer = 0 then
                let newvolume = volume + (if envelopeDirection <> 0 then 1 else -1)
                if newvolume < 0 || newvolume > 15 then envelopetimer <- 0
                else
                    envelopetimer <- envelopePace
                    volume <- newvolume

    member _.Sample() : int =
        if enable <> 0 then (if shiftregister &&& 0x01 = 0 then volume else 0)
        else 0


/// The whole chip: 4 channels + NR51 panning + NR52 power, with the absolute-cycle
/// frame-sequencer + sampling loop ported from PyBoy `Sound.tick`.
type internal ApuChip(sampleRate: int, cgb: bool) =
    do if sampleRate % 60 <> 0 then
        invalidArg "sampleRate" "APU sample rate must divide 60"

    let samplesPerFrame = sampleRate / 60
    let cyclesPerSample = float FRAME_CYCLES / float samplesPerFrame

    let sweepCh = PulseChip(true)
    let toneCh = PulseChip(false)
    let waveCh = WaveChip(cgb)
    let noiseCh = NoiseChip()

    let mutable poweron = 0
    // NR51 panning bits.
    let mutable sweepL = false
    let mutable sweepR = false
    let mutable toneL = false
    let mutable toneR = false
    let mutable waveL = false
    let mutable waveR = false
    let mutable noiseL = false
    let mutable noiseR = false

    // Absolute-cycle bookkeeping (PyBoy Sound.tick). `cycles` is an integer accumulator
    // like PyBoy; the sample/512Hz targets are fractional boundaries it crosses.
    let mutable cycles = 0
    let mutable cyclesTargetSample = cyclesPerSample
    let mutable cyclesTarget512 = float CYCLES_512HZ
    let mutable divApu = 0

    // --- Output stage for host/engine rendering (NOT used by the bit-exact isolation
    // gate, which reads the raw 0..127 sum via `Advance`). A near-DC one-pole high-pass
    // removes the static DC pedestal of the summed 0..127 DAC so playback is click-free;
    // the PyBoy reference applies no analog filter, so the corner is placed far below the
    // audio band (it does not tilt the spectrum the gate measures). Mirrors the old
    // `Apu.fs` output so rendered WAV levels are unchanged. Overridable via
    // POKEGOLD_APU_HPF_HZ. ---
    let hpfHz =
        match Environment.GetEnvironmentVariable "POKEGOLD_APU_HPF_HZ" with
        | null | "" -> 0.4
        | s -> (match Double.TryParse s with | true, v -> v | _ -> 0.4)
    let chargeFactor = 1.0 - (2.0 * Math.PI * hpfHz / float (samplesPerFrame * 60))
    let mutable capL = 0.0

    // Debug-only shadow of the last value written to each control register, indexed so
    // that index == offset == (address - 0xFF10) for 0..22 (FF10..FF26) and wave RAM at
    // 32..47 (FF30..FF3F). Lets a harness snapshot the driver's held register state each
    // frame and diff it, byte-for-byte, against PyBoy's `pb.memory[0xFF10..]` capture.
    let regShadow = Array.zeroCreate<int> 48

    let tickChannels (c: int) =
        if poweron <> 0 then
            sweepCh.Tick c
            toneCh.Tick c
            waveCh.Tick c
            noiseCh.Tick c

    member _.SampleRate = samplesPerFrame * 60

    /// Debug-only: the driver's last-written value for each register (index == offset).
    member _.RegShadow = regShadow

    /// Mirror PyBoy `Sound.set(offset, value)`: offset 0..21 control regs, 32..47 wave RAM.
    member this.WriteReg(offset: int, value: int) =
        if offset >= 0 && offset < 48 then regShadow.[offset] <- value &&& 0xFF
        if offset < 20 && (poweron <> 0 || ((not cgb) && offset % 5 = 1)) then
            let force = (poweron = 0) && (not cgb)
            match offset / 5 with
            | 0 -> sweepCh.SetReg(offset % 5, value, force)
            | 1 -> toneCh.SetReg(offset % 5, value, force)
            | 2 -> waveCh.SetReg(offset % 5, value)
            | 3 -> noiseCh.SetReg(offset % 5, value)
            | _ -> ()
        elif offset = 20 && poweron <> 0 then ()        // NR50: master volume (unmodelled, like PyBoy)
        elif offset = 21 && poweron <> 0 then           // NR51: panning
            noiseL <- value &&& 0b1000_0000 <> 0
            waveL  <- value &&& 0b0100_0000 <> 0
            toneL  <- value &&& 0b0010_0000 <> 0
            sweepL <- value &&& 0b0001_0000 <> 0
            noiseR <- value &&& 0b0000_1000 <> 0
            waveR  <- value &&& 0b0000_0100 <> 0
            toneR  <- value &&& 0b0000_0010 <> 0
            sweepR <- value &&& 0b0000_0001 <> 0
        elif offset = 22 then                           // NR52: power
            if value &&& 0x80 = 0 then
                // Power off: PyBoy zeroes regs 0..21. Clear panning + reset channel regs.
                noiseL <- false; waveL <- false; toneL <- false; sweepL <- false
                noiseR <- false; waveR <- false; toneR <- false; sweepR <- false
                for r in 0 .. 4 do
                    sweepCh.SetReg(r, 0, false)
                    toneCh.SetReg(r, 0, false)
                    waveCh.SetReg(r, 0)
                    noiseCh.SetReg(r, 0)
                poweron <- 0
            else
                poweron <- 0x80
        elif offset >= 32 && offset < 48 then
            waveCh.SetWaveByte(offset - 32, value)
        else ()

    /// One mixed stereo output pair (each side 0..127, like PyBoy).
    member private _.MixSample() : struct (int * int) =
        if poweron = 0 then struct (0, 0)
        else
            let sw = sweepCh.Sample()
            let to_ = toneCh.Sample()
            let wv = waveCh.Sample()
            let no = noiseCh.Sample()
            let mutable l = 0
            let mutable r = 0
            if sweepL then l <- l + sw
            if toneL then l <- l + to_
            if waveL then l <- l + wv
            if noiseL then l <- l + no
            if sweepR then r <- r + sw
            if toneR then r <- r + to_
            if waveR then r <- r + wv
            if noiseR then r <- r + no
            let clamp v = if v > 127 then 127 elif v < 0 then 0 else v
            struct (clamp l, clamp r)

    /// One mono output sample (0..127): the summed DAC of all four channels with
    /// NR51 panning BYPASSED. GSC's stereo_panning writes are gated by the wOptions
    /// STEREO bit, and the game's default sound option is MONO, so both hardware
    /// output terminals receive the same full mix. This is the engine playback path;
    /// the bit-exact isolation gate keeps using `MixSample` (which honours NR51).
    member private _.MixMono() : int =
        if poweron = 0 then 0
        else
            let s = sweepCh.Sample() + toneCh.Sample() + waveCh.Sample() + noiseCh.Sample()
            if s > 127 then 127 elif s < 0 then 0 else s

    /// Advance the chip by `step` CPU cycles, running the 512 Hz DIV-APU sequencer
    /// (length 256 Hz / sweep 128 Hz / envelope 64 Hz) and every channel's period
    /// timer — the shared primitive behind both `Advance` (gate) and `RenderOne`
    /// (engine). Ordering matches PyBoy `Sound.tick` exactly.
    member private _.TickCycles(step: int) =
        let oldDiv = divApu
        while float cycles >= cyclesTarget512 do
            divApu <- divApu + 1
            cyclesTarget512 <- cyclesTarget512 + float CYCLES_512HZ
        let divTicks = divApu - oldDiv
        if poweron <> 0 then
            tickChannels step
            for _ in 1 .. divTicks do
                if divApu % 2 = 0 then
                    sweepCh.TickLength(); toneCh.TickLength(); waveCh.TickLength(); noiseCh.TickLength()
                if divApu % 4 = 0 then sweepCh.TickSweep()
                if divApu % 8 = 0 then
                    sweepCh.TickEnvelope(); toneCh.TickEnvelope(); noiseCh.TickEnvelope()
        cycles <- cycles + step

    /// Advance the chip by `nCycles` CPU cycles, emitting each produced stereo sample
    /// (L,R each 0..127) to `emit`. Faithful to PyBoy `Sound.tick`'s sample/512Hz loop.
    member this.Advance(nCycles: int, emit: int -> int -> unit) =
        let mutable remaining = nCycles
        while remaining > 0 do
            let step = max 0 (min (int (ceil cyclesTargetSample) - cycles) remaining)
            this.TickCycles step
            while float cycles >= cyclesTargetSample do
                let struct (l, r) = this.MixSample()
                emit l r
                cyclesTargetSample <- cyclesTargetSample + cyclesPerSample
            remaining <- remaining - step

    /// Produce exactly one output stereo sample for the engine: advance to the next
    /// sample boundary (one `TickCycles` step, identical to one `Advance` iteration),
    /// point-sample the mix, DC-block and scale it, and ADD into `buffer` at
    /// `idx`/`idx+1` (so multiple players mix). `gain` scales this voice's output.
    member this.RenderOne(buffer: float32[], idx: int, gain: float) =
        let step = max 0 (int (ceil cyclesTargetSample) - cycles)
        this.TickCycles step
        let m = this.MixMono()
        cyclesTargetSample <- cyclesTargetSample + cyclesPerSample
        // Mono default: both output terminals carry the same summed mix (NR51 bypassed).
        let fm = float m
        let h = fm - capL
        capL <- fm - h * chargeFactor
        // The summed mix spans 0..127 (AC component ~±30 typical); scale for headroom.
        // Scale-invariant gate; this only sets host playback level (matches old Apu.fs).
        let scale = gain / 40.0
        let clamp (x: float) = if x > 1.0 then 1.0 elif x < -1.0 then -1.0 else x
        let v = float32 (clamp (h * scale))
        buffer.[idx] <- buffer.[idx] + v
        buffer.[idx + 1] <- buffer.[idx + 1] + v


/// Public replay seam for the isolation gate: render a captured/synthesised per-frame
/// register-write log through `ApuChip`, with ZERO sequencer involvement. CSV rows are
/// `frame,offset,value` (header skipped). Returns interleaved stereo float32 holding the
/// RAW per-side DAC sum (0..127 each), exactly as PyBoy's `Sound.sample()` produces — so
/// it can be compared sample-for-sample against PyBoy's own `sound.py`.
module ApuReplay =
    open System.IO

    let renderLog (csvPath: string) (sampleRate: int) (numFrames: int) (cgb: bool) : float32[] =
        let writes = Array.init numFrames (fun _ -> ResizeArray<int * int>())
        let mutable first = true
        for line in File.ReadLines csvPath do
            if first then first <- false
            elif line.Length > 0 then
                let parts = line.Split(',')
                let f = int parts.[0]
                if f >= 0 && f < numFrames then
                    writes.[f].Add(int parts.[1], int parts.[2])
        let apu = ApuChip(sampleRate, cgb)
        let outBuf = ResizeArray<float32>(numFrames * (sampleRate / 60) * 2)
        let emit (l: int) (r: int) =
            outBuf.Add(float32 l)
            outBuf.Add(float32 r)
        for f in 0 .. numFrames - 1 do
            for (off, v) in writes.[f] do
                apu.WriteReg(off, v)
            apu.Advance(ApuChipConstants.FRAME_CYCLES, emit)
        outBuf.ToArray()
