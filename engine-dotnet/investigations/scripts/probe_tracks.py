"""Probe per-channel SOUND_CHANNEL_ON + CHANNEL_TRACKS (and NR51) for an injected song.
Definitively reveals how the real GSC driver builds NR51 (wSoundOutput) per frame.

usage: python probe_tracks.py <rom> <songId> <nframes>
"""
import sys
from pyboy import PyBoy

ROM = sys.argv[1]
SONG = int(sys.argv[2])
REC = int(sys.argv[3]) if len(sys.argv) > 3 else 120
WMUSICID = 0xC19D
PLAYMUSIC = 0x3D98
WCH1 = 0xC001
STRUCT = 0x32
OFF_FLAGS1 = 3
OFF_TRACKS = 24

pb = PyBoy(ROM, window="null", sound_emulated=True, sound_sample_rate=44100, log_level="ERROR")
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
    print(f"FAILED wMusicID={mid()}"); pb.stop(); sys.exit(1)
for _ in range(45):
    pb.tick(1, False, True)

agg = {}
for fr in range(REC):
    pb.tick(1, False, True)
    row = []
    for n in range(4):
        b = WCH1 + n * STRUCT
        on = pb.memory[b + OFF_FLAGS1] & 1
        trk = pb.memory[b + OFF_TRACKS]
        row.append((on, trk))
    nr51 = pb.memory[0xFF25]
    key = tuple(row) + (nr51,)
    agg[key] = agg.get(key, 0) + 1
pb.stop()

print("ch(on,tracks) for ch1..ch4  | NR51 | count")
for key, cnt in sorted(agg.items(), key=lambda kv: -kv[1]):
    chans = key[:4]; nr51 = key[4]
    s = "  ".join(f"ch{i+1}({on},{trk:02X})" for i, (on, trk) in enumerate(chans))
    print(f"{s} | {nr51:02X} | {cnt}")
