"""Capture a naturally-playing song from pokegold.gbc (no input).

Boots to a target wMusicID, then records N frames of real-hardware audio to WAV.
"""
import sys, wave
import numpy as np
from pyboy import PyBoy

ROM = sys.argv[1]
OUT = sys.argv[2]
TARGET = int(sys.argv[3])             # wMusicID to wait for
REC = int(sys.argv[4]) if len(sys.argv) > 4 else 480
RATE = 44100
WMUSICID = 0xC19D

pb = PyBoy(ROM, window="null", sound_emulated=True, sound_sample_rate=RATE, log_level="ERROR")

def mid():
    return pb.memory[WMUSICID] | (pb.memory[WMUSICID + 1] << 8)

for f in range(4000):
    pb.tick(1, False, True)
    if mid() == TARGET:
        break
else:
    print(f"target {TARGET} never reached (last={mid()})"); pb.stop(); sys.exit(1)

# settle a few frames past the song start so the intro attack is past the boundary
for _ in range(20):
    pb.tick(1, False, True)

print(f"recording {REC} frames at wMusicID={mid()}")
chunks = []
for _ in range(REC):
    pb.tick(1, False, True)
    chunks.append(np.array(pb.sound.ndarray, copy=True))
pb.stop()

audio = np.concatenate(chunks, axis=0).astype(np.int16) * 256
with wave.open(OUT, "wb") as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(RATE)
    w.writeframes(audio.tobytes())
print(f"wrote {OUT}: {audio.shape[0]/RATE:.2f}s peak={int(np.max(np.abs(audio)))}")
