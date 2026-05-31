"""Trustworthy per-frame DRUM (ch4) timing oracle from PyBoy WRAM.

GSC drums: each `drum_note` advances the ch4 music-script pointer (wChannel4MusicAddress,
0xC09D) and reloads ch4 NOTE_DURATION (wChannel4NoteDuration, 0xC0AC). The actual drum
sound is a kit sub-sample (wNoiseSampleAddress path), but the HIT TIMING is exactly one
hit per drum_note = one script-addr advance.

So the trustworthy drum-onset signal = the frame wChannel4MusicAddress advances (while ch4
is playing drums). We also record NOTE_DURATION, the noise sample addr/delay, and the
drumkit id for context.

Mirrors capture_freq_wram.py natural boot exactly (frame indices line up).

usage: capture_drums.py <rom> <songId> <nframes> <out.csv>
"""
import sys, csv
from pyboy import PyBoy

ROM = sys.argv[1] if len(sys.argv) > 1 else "pokegold.gbc"
SONG = int(sys.argv[2]) if len(sys.argv) > 2 else 1
NFRAMES = int(sys.argv[3]) if len(sys.argv) > 3 else 1100
OUT = sys.argv[4] if len(sys.argv) > 4 else "investigations/trace/ref_title_drums.csv"

WMUSICID = 0xC19D
CH4_SCRIPT = 0xC09D   # wChannel4MusicAddress (2 bytes)
CH4_DUR = 0xC0AC      # wChannel4NoteDuration
NOISE_ADDR = 0xC1A0
NOISE_DELAY = 0xC1A2
NOISE_SET = 0xC1A4
WCH4_FLAGS1 = 0xC09A  # bit0 = SOUND_CHANNEL_ON

pb = PyBoy(ROM, window="null", sound_emulated=True, log_level="ERROR")
mid = lambda: pb.memory[WMUSICID] | (pb.memory[WMUSICID + 1] << 8)

reached = False
for f in range(2000):
    if f % 24 == 0:
        pb.button("start")
    pb.tick(1, False, True)
    if mid() == SONG:
        reached = True
        break
if not reached:
    print(f"ERROR: song {SONG} never started (mid={mid()})")
    pb.stop(); sys.exit(1)

for _ in range(2):
    pb.tick(1, False, True)

rows = []
for fr in range(NFRAMES):
    script = pb.memory[CH4_SCRIPT] | (pb.memory[CH4_SCRIPT + 1] << 8)
    dur = pb.memory[CH4_DUR]
    naddr = pb.memory[NOISE_ADDR] | (pb.memory[NOISE_ADDR + 1] << 8)
    kit = pb.memory[NOISE_SET]
    on = pb.memory[WCH4_FLAGS1] & 1
    rows.append([fr, script, dur, naddr, kit, on])
    pb.tick(1, False, True)
pb.stop()

with open(OUT, "w", newline="") as fh:
    w = csv.writer(fh)
    w.writerow(["frame", "script", "dur", "noiseAddr", "kit", "on4"])
    w.writerows(rows)
print(f"wrote {OUT} ({NFRAMES} frames)")

# drum-hit onsets = frames where the ch4 script pointer advances while noise drums active
prev = None
onsets = []
for r in rows:
    if prev is not None and r[1] != prev and r[3] != 0:
        onsets.append(r[0])
    prev = r[1]
print("drum kit(s):", sorted(set(r[4] for r in rows)))
print(f"drum hits: {len(onsets)}  first: {onsets[0] if onsets else None}")
print("onset frames:", onsets)
print("intervals:", [onsets[i+1]-onsets[i] for i in range(len(onsets)-1)])
