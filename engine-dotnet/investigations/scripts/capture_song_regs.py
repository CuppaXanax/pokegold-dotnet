"""Play an arbitrary song via PlayMusic injection and capture PyBoy's real per-frame
APU register snapshot (pb.memory[0xFF10..0xFF26] + wave RAM) to a CSV — the oracle for
the register-level driver diff (regdiff.py). Mirrors capture_apu.py's CSV format.

usage: python capture_song_regs.py <rom> <out.csv> <songId> <nframes>
"""
import sys, csv
import numpy as np
from pyboy import PyBoy

ROM = sys.argv[1]
OUT = sys.argv[2]
SONG = int(sys.argv[3])
REC = int(sys.argv[4]) if len(sys.argv) > 4 else 600
RATE = 44100
WMUSICID = 0xC19D
PLAYMUSIC = 0x3D98
REG_LO, REG_HI = 0xFF10, 0xFF26
WAVE_LO, WAVE_HI = 0xFF30, 0xFF3F

pb = PyBoy(ROM, window="null", sound_emulated=True, sound_sample_rate=RATE, log_level="ERROR")
rf = pb.register_file

def mid():
    return pb.memory[WMUSICID] | (pb.memory[WMUSICID + 1] << 8)

for _ in range(800):
    pb.tick(1, False, True)
    if mid() == 82:
        break

pc0, sp0 = rf.PC, rf.SP
sp = (sp0 - 2) & 0xFFFF
pb.memory[sp] = pc0 & 0xFF
pb.memory[sp + 1] = (pc0 >> 8) & 0xFF
rf.SP = sp
rf.D = 0
rf.E = SONG
rf.PC = PLAYMUSIC

for _ in range(8):
    pb.tick(1, False, True)
if mid() != SONG:
    print(f"FAILED: wMusicID={mid()} != {SONG}"); pb.stop(); sys.exit(1)

# settle so the driver has loaded its first notes (match capture_apu.py's 45-frame settle)
for _ in range(45):
    pb.tick(1, False, True)

reg_names = [f"r{a:04X}" for a in range(REG_LO, REG_HI + 1)] + [f"w{a:04X}" for a in range(WAVE_LO, WAVE_HI + 1)]
rows = []
for fr in range(REC):
    pb.tick(1, False, True)
    regs = [pb.memory[a] for a in range(REG_LO, REG_HI + 1)] + [pb.memory[a] for a in range(WAVE_LO, WAVE_HI + 1)]
    rows.append([fr] + regs)
pb.stop()

with open(OUT, "w", newline="") as f:
    wr = csv.writer(f); wr.writerow(["frame"] + reg_names); wr.writerows(rows)
print(f"wrote {OUT}: {len(rows)} frames, song={SONG}")
