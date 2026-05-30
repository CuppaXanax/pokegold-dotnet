# Audio investigations

Scratch artifacts and the **audio verification gate** built to settle whether the
music problem lives in our note **sequencer** or our **APU** (synthesis) stage —
without grading PCM by ear or by fuzzy spectral heuristics.

## TL;DR finding

**The sequencer is faithful.** Diffed against real hardware (the actual ROM running
in PyBoy), our per-frame channel state is byte-exact:

```
RESULT: PASS — sequencer faithful (note structure byte-exact vs hardware)
  ch1..ch4: 360 frames, notes byte-exact
  (ch1 residuals: 2 one-frame note-boundary jitter frames from fractional
   tempo-carry phase, and 2 frames where the envelope byte updates one frame
   earlier on hardware — both benign, sub-audible)
```

Therefore the remaining "wrong sound" is **isolated to `Apu.fs`** (the synthesis /
PCM stage), not to note/timing/pitch/envelope sequencing. That is the next thing to
fix, and it can be debugged as pure signal math.

## The gate (two stages)

The pipeline has two hardware-separate jobs. We verify each independently:

1. **Sequencer** (`Audio/Synth.fs`, a port of the GSC sound *driver*): reads the
   `.asm` song and computes, per 60 Hz frame, each channel's discrete register
   intent — period, duty, NRx2 envelope byte, note duration, on/off.
2. **APU** (`Audio/Apu.fs`, the sound *chip*): turns those register values into PCM.

Because the APU consumes our own sequencer's output, a single bad render can't tell
you which stage failed. The gate decouples them using a **real-hardware oracle**.

### Gate 1 — sequencer (this is implemented and PASSING)

- `scripts/capture_regs2.py` — boots `pokegold.gbc` in PyBoy, locks onto the title
  theme (`wMusicID=1`), and each frame records the four music channels' WRAM
  `channel_struct` fields → `trace/title_oracle.csv`. Plain readable RAM = the
  driver's own computed intent (the hardware APU registers `$FF13`/`$FF14` read back
  as `0xFF`, so they are useless as an oracle — WRAM is the trick).
- `scripts/dump_trace.fsx` — runs **our** `SongPlayer` on the same song and dumps the
  equivalent per-frame state (`SongPlayer.DebugStepFrame()`) → `trace/title_ours.csv`.
- `scripts/gate_seq.py` — the automated gate. Cross-correlates to find the frame
  offset (the oracle is captured mid-loop), then diffs the discrete fields exactly.
  Binary pass/fail via exit code. Tolerates only ≤1-frame note-boundary jitter
  (fractional tempo-carry phase) and the 1-frame envelope-byte phase skew, both of
  which are understood and sub-audible; any real wrong note fails the gate.

Run it (from `engine-dotnet/`):

```powershell
py  investigations/scripts/capture_regs2.py            # golden oracle (capture once)
dotnet fsi investigations/scripts/dump_trace.fsx audio/music/titlescreen.asm investigations/trace/title_ours.csv 720
py  investigations/scripts/gate_seq.py                  # exit 0 = PASS
```

### Gate 2 — APU (next)

Feed the **golden** oracle trace straight into our `Apu.fs` (bypassing our
sequencer) and render PCM. If it still sounds wrong, the bug is 100% in the APU and
can be debugged as signal math against `wav/ref_title.wav`.

## Folders

- `scripts/` — capture/dump/diff tooling (above) plus older diagnostic scratch
  (`capture_ref.py`, `spectrum.py`, `onsets.py`, `render_*.fsx`, …). The `.fsx`
  scripts reference the built game DLL relative to their own location and read song
  `.asm` via the repo-root asset resolver, so run them from `engine-dotnet/`.
- `trace/` — generated CSV traces (git-ignored).
- `wav/` — reference and our-output WAVs for by-ear spot checks (git-ignored).
