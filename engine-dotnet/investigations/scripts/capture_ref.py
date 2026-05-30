"""Capture real-hardware audio from pokegold.gbc via PyBoy (headless, deterministic).

Boots the ROM, advances through the intro to the title screen, then records the
sound engine's output to a WAV. Reads wMusicID (0xC19D) to report which song.
"""
import sys, wave
import numpy as np
from pyboy import PyBoy

ROM = sys.argv[1] if len(sys.argv) > 1 else r"N:\dev\scratch\slop-ware\pokegold.worktrees\agents-fun-compiler-high-level-port\pokegold.gbc"
OUT = sys.argv[2] if len(sys.argv) > 2 else "ref_capture.wav"
RECORD_FRAMES = int(sys.argv[3]) if len(sys.argv) > 3 else 600  # 10s @60fps
RATE = 44100
WMUSICID = 0xC19D

pb = PyBoy(ROM, window="null", sound_emulated=True, sound_sample_rate=RATE)

def music_id():
    return pb.memory[WMUSICID] | (pb.memory[WMUSICID + 1] << 8)

# Boot + mash START/A to reach the title screen and start its music.
print("booting...")
last = -1
for f in range(1200):
    if f % 30 == 0:
        pb.button("start")
    if f % 30 == 15:
        pb.button("a")
    pb.tick(1, False, True)
    mid = music_id()
    if mid != last:
        print(f"  frame {f}: wMusicID={mid}")
        last = mid
    if mid != 0 and f > 180:
        # let it settle a bit then start recording
        break

mid = music_id()
print(f"recording {RECORD_FRAMES} frames, wMusicID={mid}")
chunks = []
for f in range(RECORD_FRAMES):
    pb.tick(1, False, True)
    chunks.append(np.array(pb.sound.ndarray, copy=True))

pb.stop()

audio = np.concatenate(chunks, axis=0).astype(np.int16) * 256  # int8 -> int16
with wave.open(OUT, "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(RATE)
    w.writeframes(audio.tobytes())

peak = int(np.max(np.abs(audio))) if audio.size else 0
print(f"wrote {OUT}: {audio.shape[0]} frames ({audio.shape[0]/RATE:.2f}s), peak={peak}, wMusicID={mid}")
