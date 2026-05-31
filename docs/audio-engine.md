# Porting the Pokémon Gold Audio Engine: A Post-Mortem

> How a high-level F# reimplementation of the GSC sound driver went from
> "recognizable but wrong" to byte-faithful — and the false trails that cost
> the most time getting there.

## TL;DR

The audio port is **two separate subsystems** that both had to be right before
anything sounded correct:

1. **The sequencer/driver** — parses the song's `.asm` command stream and, every
   ~60 Hz frame, computes note durations, frequencies, envelopes, and drum
   sub-notes. This is a faithful re-implementation of `audio/engine.asm`.
2. **The APU (sound chip)** — turns register-level parameters into PCM samples.

For a long time the sequencer was *mostly* correct and the APU was *spectrally*
correct, yet the music still sounded "slightly slow / staggered / smeared." The
breakthrough came from realizing both layers had bugs that **masked each other**,
and that we could only find them with a **falsifiable per-frame oracle** built
against a real emulator (PyBoy 2.7.0) running the actual ROM.

The two fixes that closed the gap:

- **APU rewrite** to a cycle-accurate register-level chip (`ApuChip.fs`, ported
  from PyBoy's `sound.py`) driven purely by `NRxx` register writes + per-note
  triggers — so the *hardware* owns envelope/length/sweep decay.
- **The global-tempo drum bug**: GSC's `tempo` command is **global** (writes to
  all four channels), but we applied it per-channel, so channels without their
  own `tempo` ran up to **1.41× too slow**.

After both: all 45 title drum hits match the emulator's frame timing
byte-for-byte, and Azalea Town (the user's hardest test case) loops perfectly by
ear.

---

## 1. The problem space

This is an **SM64-decomp-style port**, not an emulator. We don't run Game Boy
opcodes; we re-implement the game's *audio driver* in F# and feed it the original
song data. That means we inherit every subtlety of how the GSC driver schedules
notes — and if our model of that scheduling is off by even a fixed-point rounding
step, the music drifts.

The driver's core timing math (faithfully reproduced in `Synth.fs::noteFrames`,
lines ~300–304):

```
low      = (NoteLength * length) & 0xFF      // note_type sets NoteLength
full     = Tempo * low + Modifier             // 16-bit fixed-point accumulate
Modifier = full & 0xFF                         // fractional carry kept per channel
frames   = full >> 8                           // integer frame count for this note
```

`Tempo` is a 16-bit value (default `0x100` = 256). The `Modifier` carry is what
makes note lengths average out correctly over time — it is **stateful per
channel**, which becomes important below.

---

## 2. Why this was so hard: the layers masked each other

Two independent error sources produced *similar-sounding* symptoms:

| Symptom (by ear)            | Could be caused by…                                  |
| --------------------------- | ---------------------------------------------------- |
| "Slightly slow / dragging"  | tempo math **or** envelope decay too slow            |
| "Staggered / stuttering"    | onset timing **or** smeared transients               |
| "Muddy / underwater"        | pitch instability **or** spectral synthesis artifacts |
| "Percussion wrong"          | drum sub-note timing **or** noise-channel synthesis  |

When you can't tell *which* layer is at fault, every fix is a guess, and a guess
that improves one layer while the other still dominates sounds like "nothing
changed." That is exactly what happened across multiple iterations — real
synthesis improvements were inaudible because the **drums were 41% too slow** and
swamped the percept.

**Lesson:** when two subsystems compose into one observable output, you cannot
debug by ear. You need to measure each layer in isolation against ground truth.

---

## 3. The oracle: falsifiable per-frame measurement against PyBoy

The single most valuable tool was a set of scripts that boot the **real ROM** in
PyBoy and read the GSC driver's own **WRAM state** every frame. The driver writes
its working values to known addresses (from `pokegold.sym`):

| WRAM symbol               | Addr     | Meaning                                  |
| ------------------------- | -------- | ---------------------------------------- |
| `wChannelN…Frequency`     | —        | base note frequency (pre-vibrato)        |
| `wChannelN…NoteDuration`  | —        | frames remaining on current note         |
| `wChannel4MusicAddress`   | `0xC09D` | ch4 script pointer (drum advance signal) |
| `wChannel4NoteDuration`   | `0xC0AC` | ch4 frames-left                          |
| `wNoiseSampleAddress`     | `0xC1A0` | current drum sub-note pointer            |
| `wNoiseSampleDelay`       | `0xC1A2` | frames until next drum sub-note          |
| `wMusicNoiseSampleSet`    | `0xC1A4` | active drum kit                          |

Key scripts (in `engine-dotnet/investigations/scripts/`):

- **`capture_freq_wram.py`** — the melody oracle. Per frame, dumps each channel's
  `CHANNEL_FREQUENCY` and `NOTE_DURATION`. A **note onset** = the frame where
  `NOTE_DURATION` *resets* (not where frequency changes — that misses same-pitch
  re-triggers).
- **`capture_drums.py`** — the percussion oracle. A **drum hit** = the frame where
  the ch4 music-script pointer (`0xC09D`) advances while `wNoiseSampleAddress` is
  nonzero.
- **`dump_trace.fsx`** — dumps *our* engine's per-frame `SeqSnapshot` to the same
  CSV shape, so the two can be diffed directly.

This gave us a **falsifiable gate**: two integer sequences of onset frames. They
either match or they don't. No ears, no opinions.

### Hard-won PyBoy gotchas

- Use the **real Python** at
  `%LOCALAPPDATA%\Programs\Python\Python313\python.exe` — bare `python` on this
  machine hits the Windows Store stub.
- PyBoy 2.7.0 is compiled Cython: `sound.set/tick` are C-only and **can't** be
  monkeypatched. WRAM reads via `pb.memory` are the **only** viable oracle.
- **Natural boot only.** Injecting state to jump to a song contaminates
  `wMusicNoiseSampleSet` and other driver globals. Let the ROM boot to the song
  naturally and count frames.
- `pb.tick(1, False, True)` advances exactly one frame headless.

---

## 4. The false trails (and why they were false)

Documenting these because each one *looked* like a smoking gun and each one cost
real time.

### 4.1 "The onset stutter / extra re-triggers"

An early detector flagged our melody as having extra note onsets — apparent
stutter. **False.** The detector was keying on base-frequency changes, which
both miss same-pitch re-triggers and double-count vibrato wobble. Re-built on
`NOTE_DURATION` resets, the truth emerged: ch3 onsets were
`[72,96,144,192,216,240,264,312]` vs ref `[71,95,143,191,215,239,263,311,359]` —
**every onset exactly ref+1, intervals identical.** Frame-perfect pacing with a
constant +1 *capture* offset. The "stutter" was a measurement artifact.

### 4.2 "The +2 pitch offset"

Our ch1 emitted frequency 1603 where ref WRAM showed 1601 — looked like a
+2 pitch bug. **False.** `titlescreen.asm` ch1 has `pitch_offset 2`. Per
`engine.asm::HandleTrackVibrato`, the engine adds `CHANNEL_PITCH_OFFSET` to a
*separate working register* (`wCurTrackFrequency`, which is what's written to
hardware), **not** to `CHANNEL_FREQUENCY` (the WRAM base we were reading). So ref
WRAM=1601 (pure base), hardware plays 1603, ours emits 1603 — **correct.** We were
comparing our hardware output against ref's pre-offset base.

**Lesson:** know *which* register your oracle reads. WRAM base ≠ hardware output
when vibrato/pitch-offset are in play.

### 4.3 The register-diff oracle (`apu_regs.csv`)

Tried diffing raw APU register reads frame-by-frame. **Garbage for write-only
registers** — the Game Boy returns open-bus / OR-masked values when you *read*
write-only `NRxx` registers, so the "diff" was noise. Reverted to reading the
driver's WRAM working values, which are real.

---

## 5. The two real bugs

### 5.1 The APU: spectral synthesis → register-level chip

The original APU (`Apu.fs`) was a band-limited *spectral* synthesizer: each frame
the sequencer handed it frequency/volume/duty and it generated the waveform
directly. This is fundamentally the wrong model and produced exactly the
artifacts an external analysis (and the user's ears) flagged:

- **smeared transients** — no instantaneous attack
- **bleeding notes** — software envelope didn't cut sharply
- **no true silence** — a noise floor where the hardware drops to zero
- **"underwater" sustains** — phase artifacts on held notes

The fix: port PyBoy's `sound.py` to a **cycle-accurate register-level chip**
(`ApuChip.fs`, validated bit-identical to the PyBoy oracle). Crucially, the
sequencer now drives it **only through register writes** (`NRxx`) plus a per-note
**trigger** bit — exactly like the GSC driver writing `$FF10..$FF3F`. That means
the *chip* owns the hardware volume envelope, length counter, and sweep at their
real DIV-APU rates. The driver doesn't recompute decay; it just writes the
envelope byte and triggers, and the silicon model does the rest.

This is the "high-level port" philosophy applied correctly: re-implement the
driver, but talk to a faithful *model of the hardware*, not a shortcut.

Default routing notes: `NR52`=power-on, `NR51`=`0xFF` (both sides). GSC's
`stereo_panning` is gated by the `wOptions` STEREO bit, and the game's default is
**MONO**, so a centered render is the faithful default and matches the PyBoy mono
reference.

### 5.2 The global-tempo drum bug (the one that finally did it)

GSC's `tempo` command is **global**. In `engine.asm`,
`Music_Tempo → SetGlobalTempo` writes `CHANNEL_TEMPO` to **all four** music
channels and clears each channel's `NOTE_DURATION_MODIFIER`. Our engine applied
`tempo` **per-channel**.

Why this was invisible for melodies but fatal for drums: every melodic channel in
the title has its *own* `tempo` command, so per-channel handling happened to be
correct for them. But the **drum channel (ch4) has no `tempo` command at all** —
it's supposed to inherit ch1's `tempo 256 → 184 → 134`. Under per-channel
handling, ch4 stayed pinned at the default `256`.

The arithmetic, for `drum_note 1,2` at the point the title drums start (frame
672):

```
NoteLength = 12 (drum_speed), length = 2  →  low = 24
correct:  Tempo=184  →  184*24 >> 8 = 17 frames  ✓ (matches emulator)
our bug:  Tempo=256  →  256*24 >> 8 = 24 frames  ✗ (1.41× too slow)
```

That 24-vs-17 is the entire "percussion is wrong / staggered" complaint, across
every song with a mid-song tempo change on a channel another channel doesn't
mirror.

**The fix** (`Synth.fs`, ~442–455) makes both `tempo` and `tempo_relative`
global:

```fsharp
| Tempo t ->
    // GSC `tempo` is GLOBAL (SetGlobalTempo): writes the tempo to every
    // channel and clears each note-duration modifier carry.
    for ch in chans do
        ch.Tempo <- t
        ch.Modifier <- 0

| TempoRelative d ->
    // "set global tempo to the *current* channel's tempo +/- delta",
    // then apply to all channels.
    let t = c.Tempo + d
    for ch in chans do
        ch.Tempo <- t
        ch.Modifier <- 0
```

---

## 6. Verification

The drum oracle is the falsifiable gate. After the fix, re-dumping our title
drums and diffing onset intervals against `ref_title_drums.csv`:

```
ref intervals: 17,17,9,8,9,9,12,6,7,12,6,7,6,6,38,13,6,6,13,6,6,7,...
our intervals: 17,17,9,8,9,9,12,6,7,12,6,7,6,6,38,13,6,6,13,6,6,7,...   (identical)
per-hit diff:  +1 on all 45 hits  (constant startup capture offset only)
```

All 45 hits match byte-for-byte. Melody channels (ch1/2/3) confirmed
**not regressed** — global tempo now correctly propagates to channels that lack
their own `tempo`, while channels that have one are unchanged. `dotnet test`:
**86/86 green.** And the decisive test: the user reports Azalea Town now sounds
**perfect, loops and all, by ear**.

### Known residuals (not bugs)

- **Constant +1 startup offset** between our onsets and the oracle's — an artifact
  of where each capture begins relative to the boot/loop, not a timing error.
- **Overall volume** differs from the ref WAVs — uniform scalar gain difference
  between PyBoy's mixer headroom and our direct full-scale channel sum. A constant
  multiplier across the whole file is the signature of a capture-level gain
  mismatch, not a per-note envelope problem.

---

## 7. Methodology takeaways

1. **Compose-and-conquer is a trap.** When two subsystems feed one observable,
   improving one while the other dominates feels like "nothing changed." Isolate
   and measure each layer independently.
2. **Build a falsifiable oracle before guessing.** Two integer sequences that
   either match or don't beat hours of A/B listening. The WRAM per-frame capture
   was worth more than every ear test combined.
3. **Know exactly which register your oracle reads.** WRAM base frequency ≠
   hardware output once vibrato/pitch-offset apply. Comparing the wrong pair
   invents phantom bugs (§4.2).
4. **Don't read write-only hardware registers as ground truth** — open-bus reads
   are noise (§4.3). Read the driver's own working state instead.
5. **Port to a faithful hardware *model*, not a shortcut.** Spectral synthesis was
   "close" and fundamentally wrong; the register-level chip was the only thing that
   produced real transients and true silence.
6. **One global vs per-channel mistake can hide for ages** if it only affects the
   channels you weren't scrutinizing. The melody being perfect actively masked the
   drum bug.

---

## 8. File map

| File | Role |
| ---- | ---- |
| `engine-dotnet/src/PokeGold.Game/Audio/Synth.fs` | Sequencer/driver. `noteFrames` duration math; command loop; **global tempo fix** (~442–455). |
| `engine-dotnet/src/PokeGold.Game/Audio/ApuChip.fs` | Cycle-accurate register-level APU, ported from PyBoy `sound.py`. |
| `engine-dotnet/src/PokeGold.Game/Audio/Apu.fs` | Legacy spectral APU (superseded). |
| `engine-dotnet/investigations/scripts/capture_freq_wram.py` | Melody oracle (WRAM freq/duration per frame). |
| `engine-dotnet/investigations/scripts/capture_drums.py` | Drum oracle (ch4 script-advance per frame). |
| `engine-dotnet/investigations/scripts/dump_trace.fsx` | Dumps our engine's per-frame snapshot for diffing. |
| `engine-dotnet/investigations/scripts/render_song.fsx` | Renders a song to WAV for ear-check. |
| `audio/engine.asm` | GSC reference driver. `Music_Tempo`/`SetGlobalTempo`/`Tempo`; `HandleTrackVibrato`; `ReadNoiseSample`. |
| `audio/music/titlescreen.asm` | Title song; the global-tempo test case (ch4 has no `tempo`). |
| `pokegold.sym` | WRAM symbol → address map for the oracle scripts. |

---

*Reference commit: `audio: cycle-accurate APU core + fix global-tempo
drum-timing bug`.*
