"""Trustworthy per-frame note capture from PyBoy WRAM (NOT open-bus registers).

The GSC driver stores the computed 11-bit period in CHANNEL_FREQUENCY (wChannelNFreq)
and the remaining note frames in CHANNEL_NOTE_DURATION every frame, BEFORE it writes
the (write-only) NRx3/NRx4 hardware registers. Reading these WRAM words gives the real
note pitch + timing with zero open-bus garbage.

Boots to the title song naturally (presses Start through the attract), settles, then
records ch1..4 {freq, noteDuration, on} for N frames.

usage: capture_freq_wram.py <rom> <songId> <nframes> <out.csv> [--inject]
  default boots naturally and waits for wMusicID == songId.
"""
import sys, csv
from pyboy import PyBoy

ROM = sys.argv[1] if len(sys.argv) > 1 else "pokegold.gbc"
SONG = int(sys.argv[2]) if len(sys.argv) > 2 else 1
NFRAMES = int(sys.argv[3]) if len(sys.argv) > 3 else 360
OUT = sys.argv[4] if len(sys.argv) > 4 else "investigations/trace/ref_title_freq.csv"

WMUSICID = 0xC19D
WCH1 = 0xC001
STRIDE = 0x32
OFF_FREQ = 0x10   # CHANNEL_FREQUENCY (0xC011 - 0xC001)
OFF_DUR = 0x15    # CHANNEL_NOTE_DURATION (0xC016 - 0xC001)
OFF_FLAGS1 = 0x03 # CHANNEL_FLAGS1 (0xC004 - 0xC001)

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

# settle a few frames so the first note is loaded
for _ in range(2):
    pb.tick(1, False, True)

def chan(n):
    base = WCH1 + n * STRIDE
    freq = pb.memory[base + OFF_FREQ] | (pb.memory[base + OFF_FREQ + 1] << 8)
    dur = pb.memory[base + OFF_DUR]
    on = (pb.memory[base + OFF_FLAGS1] >> 0) & 1
    return freq & 0x7FF, dur, on

rows = []
for fr in range(NFRAMES):
    rec = [fr]
    for n in range(4):
        f, d, o = chan(n)
        rec += [f, d, o]
    rows.append(rec)
    pb.tick(1, False, True)
pb.stop()

with open(OUT, "w", newline="") as fh:
    w = csv.writer(fh)
    hdr = ["frame"]
    for n in range(1, 5):
        hdr += [f"f{n}", f"d{n}", f"on{n}"]
    w.writerow(hdr)
    w.writerows(rows)
print(f"wrote {OUT} ({NFRAMES} frames)")
# quick summary: note-onset frames for ch1 (freq changes while on)
prev = None
onsets = []
for r in rows:
    key = (r[1], r[3])  # f1, on1
    if key != prev and r[3] == 1:
        onsets.append(r[0])
    prev = key
print("ch1 onset frames (first 24):", onsets[:24])
