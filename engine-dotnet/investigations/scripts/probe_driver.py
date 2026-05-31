"""Instrument the GSC sound driver inside PyBoy to confirm the 'stick frame'.

Hooks the key ch1 driver routines (ParseMusic, .readnote, Music_VolumeEnvelope,
SetNoteDuration) and logs, per frame, which fired. This reveals whether at a
volume_envelope boundary the command and the note load happen on the SAME frame
(one ParseMusic call, as the asm reads) or on TWO consecutive frames (a real
stick frame), and exactly when CHANNEL_FREQUENCY / NOTE_DURATION change.
"""
import sys
from pyboy import PyBoy

ROM = sys.argv[1] if len(sys.argv) > 1 else r"N:\dev\scratch\slop-ware\pokegold.worktrees\agents-fun-compiler-high-level-port\pokegold.gbc"
TARGET = 1
BANK = 0x3a

# rom-bank addresses (banked region 0x4000-0x7fff)
A_PARSEMUSIC   = 0x45e1
A_READNOTE     = 0x45f1
A_VOLENV       = 0x4991
A_SETNOTEDUR   = 0x4a8d
A_PARSECMD     = 0x470f

WMUSICID = 0xC19D
WCHANNEL1 = 0xC001
OFF_VOLUME_ENVELOPE = 15
OFF_FREQUENCY = 16
OFF_NOTE_DURATION = 21

pb = PyBoy(ROM, window="null", sound_emulated=True, sound_sample_rate=44100)

events = []  # list of strings for the current frame

WCURCHANNEL = 0xC199

def mk(tag):
    def cb(ctx):
        ch = pb.memory[WCURCHANNEL]
        events.append(f"{tag}@ch{ch}")
    return cb

# Only fire for ch1: wCurChannel == 0. We can read it inside the callback.
WCURCHANNEL = 0xC2BE  # may differ; we will resolve by reading sym at runtime instead

def u8(a): return pb.memory[a]
def u16(a): return pb.memory[a] | (pb.memory[a+1] << 8)

for (addr, tag) in [(A_PARSEMUSIC,"PM"),(A_READNOTE,"NOTE"),(A_VOLENV,"VENV"),(A_SETNOTEDUR,"SND"),(A_PARSECMD,"CMD")]:
    pb.hook_register(BANK, addr, mk(tag), None)

print("booting...")
reached = False
for f in range(1500):
    if f % 24 == 0:
        pb.button("start")
    pb.tick(1, False, True)
    if u16(WMUSICID) == TARGET:
        reached = True
        break
if not reached:
    print("no title"); pb.stop(); sys.exit(1)
for _ in range(45):
    pb.tick(1, False, True)

b = WCHANNEL1  # ch1
prev = None
for fr in range(300):
    events.clear()
    pb.tick(1, False, True)
    env = u8(b+OFF_VOLUME_ENVELOPE)
    freq = u16(b+OFF_FREQUENCY)
    dur = u8(b+OFF_NOTE_DURATION)
    cur = (freq, env, dur)
    # Only print frames of interest: near the known boundaries or when events fired
    if events or (140 <= fr <= 150) or (214 <= fr <= 222) or (286 <= fr <= 292):
        print(f"fr={fr:3} freq={freq:4} env={env:3} dur={dur:3}  events={events}")
    prev = cur

pb.stop()
