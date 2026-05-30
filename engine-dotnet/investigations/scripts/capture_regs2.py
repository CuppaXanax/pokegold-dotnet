"""Capture the GSC sound driver's per-frame channel state from real hardware.

Boots pokegold.gbc in PyBoy, advances to the TITLE screen theme (MUSIC_TITLE=1),
stops all input so the song plays undisturbed, then each frame records the four
music channels' WRAM struct state — the driver's *register-write intent*, the
exact values it hands the APU. This is the golden oracle for Gate 1 (sequencer
verification): plain readable RAM, immune to the write-only hardware-register
problem.

Output CSV (one row per frame) with, per channel n in 1..4:
  onN    CHANNEL_FLAGS1 bit0 (SOUND_CHANNEL_ON) — channel actively sounding
  freqN  CHANNEL_FREQUENCY (11-bit period x; pulse f = 131072/(2048-x))
  dutyN  CHANNEL_DUTY_CYCLE
  envN   CHANNEL_VOLUME_ENVELOPE (NRx2 byte: vol<<4 | dir<<3 | period)
  octN   CHANNEL_OCTAVE
  durN   CHANNEL_NOTE_DURATION (high-byte frames remaining on current note)

Usage: python capture_regs.py [rom] [out.csv] [record_frames] [target_music_id]
"""
import sys, csv, os
from pyboy import PyBoy

ROM = sys.argv[1] if len(sys.argv) > 1 else r"N:\dev\scratch\slop-ware\pokegold.worktrees\agents-fun-compiler-high-level-port\pokegold.gbc"
OUT = sys.argv[2] if len(sys.argv) > 2 else "investigations/trace/title_oracle.csv"
RECORD_FRAMES = int(sys.argv[3]) if len(sys.argv) > 3 else 360
TARGET = int(sys.argv[4]) if len(sys.argv) > 4 else 1  # MUSIC_TITLE

WMUSICID = 0xC19D
WCHANNEL1 = 0xC001
STRUCT = 0x32

OFF_FLAGS1 = 3            # bit0 = SOUND_CHANNEL_ON
OFF_DUTY_CYCLE = 14
OFF_VOLUME_ENVELOPE = 15
OFF_FREQUENCY = 16       # word
OFF_OCTAVE = 19
OFF_NOTE_DURATION = 21

pb = PyBoy(ROM, window="null", sound_emulated=False)


def u8(a):
    return pb.memory[a]


def u16(a):
    return pb.memory[a] | (pb.memory[a + 1] << 8)


def music_id():
    return u16(WMUSICID)


def snapshot_channel(n):
    b = WCHANNEL1 + (n - 1) * STRUCT
    return {
        "on": u8(b + OFF_FLAGS1) & 1,
        "freq": u16(b + OFF_FREQUENCY),
        "duty": u8(b + OFF_DUTY_CYCLE),
        "env": u8(b + OFF_VOLUME_ENVELOPE),
        "oct": u8(b + OFF_OCTAVE),
        "dur": u8(b + OFF_NOTE_DURATION),
    }


# Phase 1: press start to skip the intro cutscene until the TITLE theme starts,
# then STOP input so we don't navigate into the menu (which swaps the song).
print(f"booting; locking onto wMusicID={TARGET} ...")
reached = False
for f in range(1500):
    if not reached and f % 24 == 0:
        pb.button("start")
    pb.tick(1, False, False)
    if music_id() == TARGET:
        reached = True
        reached_frame = f
        break

if not reached:
    print(f"!! never reached wMusicID={TARGET}; current={music_id()}")
    pb.stop()
    sys.exit(1)

# Phase 2: settle (no input) and confirm the song is stable.
for _ in range(45):
    pb.tick(1, False, False)
mid = music_id()
print(f"reached target at frame {reached_frame}; after settle wMusicID={mid}")
if mid != TARGET:
    print(f"!! song changed during settle to {mid}; aborting")
    pb.stop()
    sys.exit(1)

# Phase 3: record.
cols = ["frame"]
for n in range(1, 5):
    cols += [f"on{n}", f"freq{n}", f"duty{n}", f"env{n}", f"oct{n}", f"dur{n}"]

rows = []
for fr in range(RECORD_FRAMES):
    pb.tick(1, False, False)
    if music_id() != TARGET:
        print(f"!! song changed at record frame {fr} to {music_id()}; stopping early")
        break
    row = {"frame": fr}
    for n in range(1, 5):
        s = snapshot_channel(n)
        for k in ("on", "freq", "duty", "env", "oct", "dur"):
            row[f"{k}{n}"] = s[k]
    rows.append(row)

pb.stop()

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", newline="") as fh:
    w = csv.DictWriter(fh, fieldnames=cols)
    w.writeheader()
    w.writerows(rows)

print(f"wrote {OUT}: {len(rows)} frames, wMusicID={TARGET}")
for n in range(1, 5):
    onframes = sum(1 for r in rows if r[f"on{n}"])
    freqs = sorted({r[f"freq{n}"] for r in rows if r[f"on{n}"] and r[f"freq{n}"]})
    print(f"  ch{n}: {onframes} on-frames, {len(freqs)} distinct freqs")
