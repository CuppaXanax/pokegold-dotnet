"""Gate 2 helper — capture hardware PCM and the register trace from ONE PyBoy run.

Identical boot/lock logic to capture_regs2.py, but with sound emulation on: each
frame we record both the four channels' WRAM register state AND the APU's PCM
output. Because they come from the same run they are sample-accurate aligned, so we
can feed the (proven-faithful) register trace into our APU and compare its PCM to
this hardware PCM on a steady note — isolating the synthesis stage.

Outputs:
  trace/title_oracle.csv   (same schema as capture_regs2.py)
  wav/ref_title_aligned.wav (stereo 44100, the hardware PCM for the recorded frames)
  trace/title_align.txt     (frames-per-sample bookkeeping for the renderer)

Usage: python capture_both.py [rom] [record_frames]
"""
import sys, csv, os, wave
import numpy as np
from pyboy import PyBoy

ROM = sys.argv[1] if len(sys.argv) > 1 else r"N:\dev\scratch\slop-ware\pokegold.worktrees\agents-fun-compiler-high-level-port\pokegold.gbc"
RECORD_FRAMES = int(sys.argv[2]) if len(sys.argv) > 2 else 360
TARGET = 1  # MUSIC_TITLE
RATE = 44100

CSV_OUT = "investigations/trace/title_oracle.csv"
WAV_OUT = "investigations/wav/ref_title_aligned.wav"
ALIGN_OUT = "investigations/trace/title_align.txt"

WMUSICID = 0xC19D
WCHANNEL1 = 0xC001
STRUCT = 0x32
OFF_FLAGS1 = 3
OFF_DUTY_CYCLE = 14
OFF_VOLUME_ENVELOPE = 15
OFF_FREQUENCY = 16
OFF_OCTAVE = 19
OFF_NOTE_DURATION = 21

pb = PyBoy(ROM, window="null", sound_emulated=True, sound_sample_rate=RATE)


def u8(a):
    return pb.memory[a]


def u16(a):
    return pb.memory[a] | (pb.memory[a + 1] << 8)


def snapshot(n):
    b = WCHANNEL1 + (n - 1) * STRUCT
    return {
        "on": u8(b + OFF_FLAGS1) & 1,
        "freq": u16(b + OFF_FREQUENCY),
        "duty": u8(b + OFF_DUTY_CYCLE),
        "env": u8(b + OFF_VOLUME_ENVELOPE),
        "oct": u8(b + OFF_OCTAVE),
        "dur": u8(b + OFF_NOTE_DURATION),
    }


print(f"booting; locking onto wMusicID={TARGET} ...")
reached = False
for f in range(1500):
    if f % 24 == 0:
        pb.button("start")
    pb.tick(1, False, True)
    if u16(WMUSICID) == TARGET:
        reached = True
        break
if not reached:
    print("never reached title music")
    pb.stop(); sys.exit(1)

for _ in range(45):
    pb.tick(1, False, True)
if u16(WMUSICID) != TARGET:
    print("song changed during settle"); pb.stop(); sys.exit(1)

cols = ["frame"]
for n in range(1, 5):
    cols += [f"on{n}", f"freq{n}", f"duty{n}", f"env{n}", f"oct{n}", f"dur{n}"]

rows = []
pcm_chunks = []
frame_sample_counts = []
for fr in range(RECORD_FRAMES):
    pb.tick(1, False, True)
    if u16(WMUSICID) != TARGET:
        print(f"song changed at frame {fr}; stopping"); break
    row = {"frame": fr}
    for n in range(1, 5):
        s = snapshot(n)
        for k in ("on", "freq", "duty", "env", "oct", "dur"):
            row[f"{k}{n}"] = s[k]
    rows.append(row)
    chunk = np.array(pb.sound.ndarray, copy=True)  # (samples, 2) int8-ish
    pcm_chunks.append(chunk)
    frame_sample_counts.append(chunk.shape[0])

pb.stop()

os.makedirs(os.path.dirname(CSV_OUT), exist_ok=True)
os.makedirs(os.path.dirname(WAV_OUT), exist_ok=True)
with open(CSV_OUT, "w", newline="") as fh:
    w = csv.DictWriter(fh, fieldnames=cols)
    w.writeheader()
    w.writerows(rows)

audio = np.concatenate(pcm_chunks, axis=0).astype(np.int16) * 256
with wave.open(WAV_OUT, "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(RATE)
    w.writeframes(audio.tobytes())

# samples-per-frame varies slightly; record cumulative so the renderer can map a
# game frame -> a hardware PCM sample index for windowed comparison.
with open(ALIGN_OUT, "w") as fh:
    fh.write(f"rate {RATE}\n")
    cum = 0
    for i, c in enumerate(frame_sample_counts):
        fh.write(f"{i} {cum} {c}\n")
        cum += c

print(f"wrote {CSV_OUT} ({len(rows)} frames)")
print(f"wrote {WAV_OUT} ({audio.shape[0]} samples, {audio.shape[0]/RATE:.2f}s)")
print(f"mean samples/frame = {np.mean(frame_sample_counts):.1f}")
