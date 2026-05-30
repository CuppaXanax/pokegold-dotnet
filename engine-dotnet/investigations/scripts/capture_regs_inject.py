"""Capture the per-frame WRAM register trace for an ARBITRARY song (oracle).

Like capture_both.py, but instead of booting to the title it forces the target
song with the same `call PlayMusic` injection as inject_capture.py — so the gate
(gate_seq.py) can verify our sequencer against ANY track, not just the title.

Writes a CSV with the same schema capture_regs2.py / dump_trace.fsx use.

Usage: python capture_regs_inject.py <rom> <out.csv> <song_id> [record_frames]
"""
import sys, csv, os
from pyboy import PyBoy

ROM = sys.argv[1]
OUT = sys.argv[2]
SONG = int(sys.argv[3])
RECORD = int(sys.argv[4]) if len(sys.argv) > 4 else 600

WMUSICID = 0xC19D
WCHANNEL1 = 0xC001
STRUCT = 0x32
OFF_FLAGS1 = 3
OFF_DUTY_CYCLE = 14
OFF_VOLUME_ENVELOPE = 15
OFF_FREQUENCY = 16
OFF_OCTAVE = 19
OFF_NOTE_DURATION = 21
PLAYMUSIC = 0x3D98

pb = PyBoy(ROM, window="null", sound_emulated=False, log_level="ERROR")
rf = pb.register_file


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


# boot to the attract opening (engine + main loop live), then inject the song.
for _ in range(800):
    pb.tick(1, False, False)
    if u16(WMUSICID) == 82:
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
    pb.tick(1, False, False)
if u16(WMUSICID) != SONG:
    print(f"FAILED to inject song {SONG}: wMusicID={u16(WMUSICID)}")
    pb.stop(); sys.exit(1)

# PlayMusic clears the channels; let the first notes latch before recording.
for _ in range(12):
    pb.tick(1, False, False)

cols = ["frame"]
for n in range(1, 5):
    cols += [f"on{n}", f"freq{n}", f"duty{n}", f"env{n}", f"oct{n}", f"dur{n}"]

rows = []
for fr in range(RECORD):
    pb.tick(1, False, False)
    if u16(WMUSICID) != SONG:
        print(f"song changed at frame {fr}; stopping early")
        break
    row = {"frame": fr}
    for n in range(1, 5):
        s = snapshot(n)
        for k in ("on", "freq", "duty", "env", "oct", "dur"):
            row[f"{k}{n}"] = s[k]
    rows.append(row)

pb.stop()

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", newline="") as fh:
    w = csv.DictWriter(fh, fieldnames=cols)
    w.writeheader()
    w.writerows(rows)
print(f"wrote {OUT} ({len(rows)} frames, song {SONG})")
