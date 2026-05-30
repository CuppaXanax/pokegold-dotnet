"""Force a specific song via a PlayMusic call-injection, then capture it.

Boots to the attract spin-loop, injects `call PlayMusic` (DE=song id) by pushing
the current PC as the return address, and verifies wMusicID latches to the target.
"""
import sys, wave
import numpy as np
from pyboy import PyBoy

ROM = sys.argv[1]
OUT = sys.argv[2]
SONG = int(sys.argv[3])
REC = int(sys.argv[4]) if len(sys.argv) > 4 else 480
SOLO = int(sys.argv[5]) if len(sys.argv) > 5 else 0  # 1..4 to solo a channel, 0 = full mix
RATE = 44100
WMUSICID = 0xC19D
PLAYMUSIC = 0x3D98
NR51 = 0xFF25  # sound panning: bits 7-4 = left S4..S1, 3-0 = right S4..S1
SOLO_MASK = ((1 << (SOLO - 1)) | (1 << (SOLO + 3))) if SOLO else 0xFF

pb = PyBoy(ROM, window="null", sound_emulated=True, sound_sample_rate=RATE, log_level="ERROR")
rf = pb.register_file

def mid():
    return pb.memory[WMUSICID] | (pb.memory[WMUSICID + 1] << 8)

# boot to the opening (engine + main loop running)
for _ in range(800):
    pb.tick(1, False, True)
    if mid() == 82:
        break

# inject: push current PC as return addr, jump to PlayMusic with DE = song id
pc0, sp0 = rf.PC, rf.SP
sp = (sp0 - 2) & 0xFFFF
pb.memory[sp] = pc0 & 0xFF
pb.memory[sp + 1] = (pc0 >> 8) & 0xFF
rf.SP = sp
rf.D = 0
rf.E = SONG
rf.PC = PLAYMUSIC

for i in range(8):
    pb.tick(1, False, True)
    print(f"  after inject frame {i}: wMusicID={mid()}")

if mid() != SONG:
    print(f"FAILED: wMusicID={mid()} != {SONG}")
    pb.stop(); sys.exit(1)

for _ in range(20):
    pb.tick(1, False, True)
print(f"recording {REC} frames at wMusicID={mid()}")
chunks = []
for _ in range(REC):
    if SOLO:
        pb.memory[NR51] = SOLO_MASK
    pb.tick(1, False, True)
    if SOLO:
        pb.memory[NR51] = SOLO_MASK
    chunks.append(np.array(pb.sound.ndarray, copy=True))
pb.stop()

audio = np.concatenate(chunks, axis=0).astype(np.int16) * 256
with wave.open(OUT, "wb") as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(RATE)
    w.writeframes(audio.tobytes())
print(f"wrote {OUT}: {audio.shape[0]/RATE:.2f}s peak={int(np.max(np.abs(audio)))}")
