"""E1 capture: PyBoy's ACTUAL audio output + per-frame APU register snapshots.

Boots to the title song, settles, then for N frames records:
  - PyBoy's emulated audio (pb.sound.ndarray, 48 kHz stereo int8) -> ref_title_48k.wav
  - the raw APU registers $FF10-$FF26 and wave RAM $FF30-$FF3F -> apu_regs.csv

This is the oracle for E1: we will feed the SAME register state into our Apu.fs and
compare the resulting waveform to ref_title_48k.wav in the TIME domain.
"""
import sys, csv, wave, struct
import numpy as np
from pyboy import PyBoy

ROM = sys.argv[1] if len(sys.argv) > 1 else r"N:\dev\scratch\slop-ware\pokegold.worktrees\agents-fun-compiler-high-level-port\pokegold.gbc"
NFRAMES = int(sys.argv[2]) if len(sys.argv) > 2 else 600
OUTDIR = "investigations/trace"
WMUSICID = 0xC19D

pb = PyBoy(ROM, window="null", sound_emulated=True)
SR = pb.sound.sample_rate

# boot to title
reached = False
for f in range(1500):
    if f % 24 == 0:
        pb.button("start")
    pb.tick(1, False, True)
    if pb.memory[WMUSICID] == 1:
        reached = True
        break
if not reached:
    print("ERROR: title song never started"); pb.stop(); sys.exit(1)
for _ in range(45):
    pb.tick(1, False, True)

REG_LO, REG_HI = 0xFF10, 0xFF26
WAVE_LO, WAVE_HI = 0xFF30, 0xFF3F
reg_names = [f"r{a:04X}" for a in range(REG_LO, REG_HI + 1)] + [f"w{a:04X}" for a in range(WAVE_LO, WAVE_HI + 1)]

audio = []
rows = []
for fr in range(NFRAMES):
    pb.tick(1, False, True)
    head = pb.sound.raw_buffer_head            # mono-sample count (L+R)
    nd = pb.sound.ndarray[: head // 2]          # (frames, 2) int8
    audio.append(nd.copy())
    regs = [pb.memory[a] for a in range(REG_LO, REG_HI + 1)] + [pb.memory[a] for a in range(WAVE_LO, WAVE_HI + 1)]
    rows.append([fr] + regs)

pb.stop()

aud = np.concatenate(audio, axis=0).astype(np.int16)  # int8 range -128..127
# write 16-bit wav (scale int8 -> int16)
with wave.open(f"{OUTDIR}/ref_title_48k.wav", "wb") as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((aud * 256).astype("<i2").tobytes())

with open(f"{OUTDIR}/apu_regs.csv", "w", newline="") as f:
    wr = csv.writer(f); wr.writerow(["frame"] + reg_names); wr.writerows(rows)

print(f"captured {len(rows)} frames, {aud.shape[0]} stereo samples @ {SR} Hz")
print(f"  -> {OUTDIR}/ref_title_48k.wav")
print(f"  -> {OUTDIR}/apu_regs.csv")
