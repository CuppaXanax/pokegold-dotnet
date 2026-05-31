"""DECISIVE experiment: capture PyBoy's REAL per-frame APU register WRITES + its
actual audio, frame-aligned, in ONE run.

Boots to the title song, settles, then for N frames:
  - wraps core Sound.set(offset, value) to log every (frame, offset, value) the real
    driver writes -> apuchip_writes_real.csv  (frame,offset,value)
  - records PyBoy's emulated audio (pb.sound.ndarray) -> ref_writes_48k.wav

The offset convention of PyBoy core Sound.set is IDENTICAL to our ApuChip.WriteReg
(0..22 = NR10..NR52, 32..47 = wave RAM), because ApuChip is a port of this exact file.
So apuchip_replay.fsx (ApuReplay.renderLog) can consume this CSV unchanged.

This isolates: ApuChip(PyBoy's real writes) vs PyBoy's actual audio. ZERO sequencer,
ZERO of our register emission -> tests our APU + the reference, nothing else.
"""
import sys, csv, wave
import numpy as np
from pyboy import PyBoy

ROM = sys.argv[1] if len(sys.argv) > 1 else r"N:\dev\scratch\slop-ware\pokegold.worktrees\agents-fun-compiler-high-level-port\pokegold.gbc"
NFRAMES = int(sys.argv[2]) if len(sys.argv) > 2 else 600
OUTDIR = "investigations/trace"
WMUSICID = 0xC19D

pb = PyBoy(ROM, window="null", sound_emulated=True)
SR = pb.sound.sample_rate
core = pb.sound.mb.sound  # the cython/py core APU whose .set we log

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

# --- wrap Sound.set at the CLASS level (works whether core/sound is pure-python) ---
cur_frame = [0]
writes = []
orig_set = type(core).set

def logging_set(self, offset, value):
    writes.append((cur_frame[0], int(offset), int(value)))
    return orig_set(self, offset, value)

try:
    type(core).set = logging_set
    patched = True
except (TypeError, AttributeError):
    patched = False

if not patched:
    print("ERROR: could not monkeypatch Sound.set (compiled cython?)"); pb.stop(); sys.exit(2)

# snapshot the CURRENT register state at frame 0 so the replay starts from the same
# chip state the real driver is already in mid-song (not a cold power-on).
init_writes = []
# NR52 power + NR51 panning + each channel's current regs + wave RAM
for off in range(0, 23):
    try:
        init_writes.append((0, off, int(core.get(off))))
    except Exception:
        pass
for off in range(32, 48):
    try:
        init_writes.append((0, off, int(core.get(off))))
    except Exception:
        pass

audio = []
for fr in range(NFRAMES):
    cur_frame[0] = fr
    pb.tick(1, False, True)
    head = pb.sound.raw_buffer_head
    nd = pb.sound.ndarray[: head // 2]
    audio.append(nd.copy())

type(core).set = orig_set
pb.stop()

aud = np.concatenate(audio, axis=0).astype(np.int16)
with wave.open(f"{OUTDIR}/ref_writes_48k.wav", "wb") as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((aud * 256).astype("<i2").tobytes())

# init regs first (frame 0, before any logged write), then the logged writes
with open(f"{OUTDIR}/apuchip_writes_real.csv", "w", newline="") as f:
    wr = csv.writer(f); wr.writerow(["frame", "offset", "value"])
    wr.writerows(init_writes)
    wr.writerows(writes)

print(f"captured {len(writes)} writes over {NFRAMES} frames, {aud.shape[0]} stereo samples @ {SR} Hz")
print(f"  init regs = {len(init_writes)} (frame-0 state snapshot)")
print(f"  -> {OUTDIR}/apuchip_writes_real.csv")
print(f"  -> {OUTDIR}/ref_writes_48k.wav")
