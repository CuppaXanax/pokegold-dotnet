"""Capture the GSC sound driver's per-frame channel state from real hardware.

Boots pokegold.gbc in PyBoy, advances to the title screen (deterministic), then
each frame reads the four music channels' WRAM structs and records the driver's
*register-write intent* — the exact values it hands the APU. This is the golden
oracle for Gate 1 (sequencer verification): plain readable RAM, no write-only
hardware register problem.

Output: a CSV (one row per frame) with, per channel n in 1..4:
  onN      bit0 of CHANNEL_NOTE_FLAGS (NOTE_ON) — channel actively sounding
  freqN    CHANNEL_FREQUENCY (11-bit period x; pulse f = 131072/(2048-x))
  dutyN    CHANNEL_DUTY_CYCLE
  envN     CHANNEL_VOLUME_ENVELOPE (NRx2 byte: vol<<4 | dir<<3 | period)
  octN     CHANNEL_OCTAVE
  durN     CHANNEL_NOTE_DURATION (high-byte frames remaining on current note)

Usage: python capture_regs.py [rom] [out.csv] [record_frames]
"""
import sys, csv
from pyboy import PyBoy

ROM = sys.argv[1] if len(sys.argv) > 1 else r"N:\dev\scratch\slop-ware\pokegold.worktrees\agents-fun-compiler-high-level-port\pokegold.gbc"
OUT = sys.argv[2] if len(sys.argv) > 2 else "investigations/trace/title_oracle.csv"
RECORD_FRAMES = int(sys.argv[3]) if len(sys.argv) > 3 else 360  # ~6s @ ~60fps

WMUSICID = 0xC19D
WCHANNEL1 = 0xC001
STRUCT = 0x32

# channel_struct member offsets (constants/audio_constants.asm)
OFF_NOTE_FLAGS = 12
OFF_DUTY_CYCLE = 14
OFF_VOLUME_ENVELOPE = 15
OFF_FREQUENCY = 16  # word
OFF_OCTAVE = 19
OFF_NOTE_DURATION = 21

pb = PyBoy(ROM, window="null", sound_emulated=False)


def u8(a):
    return pb.memory[a]


def u16(a):
    return pb.memory[a] | (pb.memory[a + 1] << 8)


def music_id():
    return u16(WMUSICID)


def chan_base(n):  # n in 1..4
    return WCHANNEL1 + (n - 1) * STRUCT


def snapshot_channel(n):
    b = chan_base(n)
    return {
        "on": u8(b + OFF_NOTE_FLAGS) & 1,
        "freq": u16(b + OFF_FREQUENCY),
        "duty": u8(b + OFF_DUTY_CYCLE),
        "env": u8(b + OFF_VOLUME_ENVELOPE),
        "oct": u8(b + OFF_OCTAVE),
        "dur": u8(b + OFF_NOTE_DURATION),
    }


# Boot + mash START/A to reach the title screen and start its music.
print("booting to title music...")
last = -1
for f in range(1200):
    if f % 30 == 0:
        pb.button("start")
    if f % 30 == 15:
        pb.button("a")
    pb.tick(1, False, False)
    mid = music_id()
    if mid != last:
        print(f"  frame {f}: wMusicID={mid}")
        last = mid
    if mid != 0 and f > 180:
        break

mid = music_id()
print(f"recording {RECORD_FRAMES} frames, wMusicID={mid}")

cols = ["frame"]
for n in range(1, 5):
    cols += [f"on{n}", f"freq{n}", f"duty{n}", f"env{n}", f"oct{n}", f"dur{n}"]

rows = []
for fr in range(RECORD_FRAMES):
    pb.tick(1, False, False)
    row = {"frame": fr}
    for n in range(1, 5):
        s = snapshot_channel(n)
        row[f"on{n}"] = s["on"]
        row[f"freq{n}"] = s["freq"]
        row[f"duty{n}"] = s["duty"]
        row[f"env{n}"] = s["env"]
        row[f"oct{n}"] = s["oct"]
        row[f"dur{n}"] = s["dur"]
    rows.append(row)

pb.stop()

import os
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", newline="") as fh:
    w = csv.DictWriter(fh, fieldnames=cols)
    w.writeheader()
    w.writerows(rows)

# brief summary: count distinct notes (NOTE_DURATION resets) per channel
print(f"wrote {OUT}: {len(rows)} frames, wMusicID={mid}")
for n in range(1, 5):
    onframes = sum(1 for r in rows if r[f"on{n}"])
    freqs = sorted({r[f"freq{n}"] for r in rows if r[f"on{n}"]})
    print(f"  ch{n}: {onframes} on-frames, {len(freqs)} distinct freqs")
