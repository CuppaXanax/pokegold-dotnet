"""APU port validation — STAGE 1 (reference).

Validates our F# `ApuChip` is a faithful port of PyBoy's `core/sound.py` by an EXACT
oracle: instantiate PyBoy's OWN sound.py source in pure Python, feed it a synthetic but
feature-covering register-write script, and dump:
  - writes.csv      : frame,offset,value  (the identical script our ApuChip will replay)
  - expected.bin    : int16 interleaved stereo samples PyBoy's sound.py produces

Then `apuchip_replay.fsx` feeds writes.csv through ApuChip -> ours.bin, and
`apuchip_cmp.py` asserts sample-for-sample identity.

The installed PyBoy is Cython-compiled, but the .py SOURCE is on disk; we exec it with
stubbed deps so we test against the exact installed algorithm with no sequencer/capture
confounds.
"""
import sys, os, types, math, struct, csv

SR = 48000
NFRAMES = 180  # 3 s
OUTDIR = "investigations/trace"
os.makedirs(OUTDIR, exist_ok=True)

# --- Load PyBoy's sound.py source as a pure-python module (stub its deps). ---
import pyboy as _pb_pkg
SOUND_SRC = os.path.join(os.path.dirname(_pb_pkg.__file__), "core", "sound.py")

class _DummyLogger:
    def debug(self, *a, **k): pass
    def error(self, *a, **k): pass
    def critical(self, *a, **k): pass

stub_pyboy = types.ModuleType("pyboy")
stub_logging = types.ModuleType("pyboy.logging")
stub_logging.get_logger = lambda name: _DummyLogger()
stub_pyboy.logging = stub_logging
stub_utils = types.ModuleType("pyboy.utils")
stub_utils.PyBoyAssertException = Exception
stub_utils.cython_compiled = False
stub_utils.FRAME_CYCLES = 70224
stub_utils.MAX_CYCLES = 1 << 31
stub_utils.double_to_uint64_ceil = lambda v: math.ceil(v)
sys.modules["pyboy"] = stub_pyboy
sys.modules["pyboy.logging"] = stub_logging
sys.modules["pyboy.utils"] = stub_utils

ns = {"__name__": "pyboy_sound_ref"}
with open(SOUND_SRC, "r", encoding="utf-8") as fh:
    exec(compile(fh.read(), SOUND_SRC, "exec"), ns)
Sound = ns["Sound"]

# --- Build a synthetic, feature-covering per-frame register-write script. ---
# Offsets: 0=NR10..4=NR14, 5..9=NR2x, 10..14=NR3x(wave), 15..19=NR4x(noise),
#          20=NR50, 21=NR51, 22=NR52, 32..47=wave RAM.
writes = [[] for _ in range(NFRAMES)]

def w(frame, off, val):
    writes[frame].append((off, int(val) & 0xFF))

# Frame 0: power on, master vol, full panning.
w(0, 22, 0x80)      # NR52 power on
w(0, 20, 0x77)      # NR50 max both sides
w(0, 21, 0xFF)      # NR51 all channels both sides

# Wave RAM: a ramp (covers nibble read + volume shift).
for i in range(16):
    w(0, 32 + i, (i * 0x11) & 0xFF)

# A little melody: retrigger pulse1 (with sweep), pulse2, wave each ~30 frames;
# fire a noise "drum" every 15 frames. Envelopes set so decay is exercised.
pulse1_notes = [0x6C1, 0x710, 0x759, 0x710]   # 11-bit periods
pulse2_notes = [0x700, 0x740, 0x780, 0x740]
wave_notes   = [0x600, 0x680, 0x700, 0x680]
for k in range(NFRAMES // 30):
    f = k * 30
    p1 = pulse1_notes[k % len(pulse1_notes)]
    p2 = pulse2_notes[k % len(pulse2_notes)]
    p3 = wave_notes[k % len(wave_notes)]
    # CH1 pulse+sweep: NR10 sweep, NR11 duty=2/len, NR12 env vol=12 decay pace=3, NR13/14 freq+trigger
    w(f, 0, 0x35)                 # NR10: pace=3, dir=down, shift=5
    w(f, 1, (2 << 6) | 0x00)      # NR11 duty 50%
    w(f, 2, (12 << 4) | 0x03)     # NR12 init vol 12, decrease, pace 3
    w(f, 3, p1 & 0xFF)            # NR13 freq lo
    w(f, 4, 0x80 | ((p1 >> 8) & 7))  # NR14 trigger + freq hi (length disabled)
    # CH2 pulse: duty 1, env vol 15 decay pace 2
    w(f, 6, (1 << 6))
    w(f, 7, (15 << 4) | 0x02)
    w(f, 8, p2 & 0xFF)
    w(f, 9, 0x80 | ((p2 >> 8) & 7))
    # CH3 wave: DAC on, vol code 1 (100%), freq + trigger
    w(f, 10, 0x80)                # NR30 DAC power
    w(f, 11, 0x00)               # NR31 length
    w(f, 12, (1 << 5))           # NR32 volume code 1
    w(f, 13, p3 & 0xFF)
    w(f, 14, 0x80 | ((p3 >> 8) & 7))

for k in range(NFRAMES // 15):
    f = k * 15
    # CH4 noise drum: env vol 13 decay pace 2, NR43 polynomial, trigger
    w(f, 16, (13 << 4) | 0x02)   # NR42
    w(f, 17, 0x51)               # NR43 clkpow=5, width=0, div=1
    w(f, 19, 0x80)               # NR44 trigger

# --- Render with PyBoy's pure-python Sound. ---
snd = Sound(100, True, SR, True)
cum = 0
samples = []
for f in range(NFRAMES):
    for (off, val) in writes[f]:
        snd.set(off, val)
    cum += stub_utils.FRAME_CYCLES
    snd.tick(cum)
    head = snd.audiobuffer_head
    samples.extend(snd.audiobuffer[:head])
    snd.clear_buffer()

# --- Dump writes.csv and expected.bin (int16 interleaved, raw 0..127 values). ---
with open(f"{OUTDIR}/apuchip_writes.csv", "w", newline="") as fh:
    wr = csv.writer(fh)
    wr.writerow(["frame", "offset", "value"])
    for f in range(NFRAMES):
        for (off, val) in writes[f]:
            wr.writerow([f, off, val])

with open(f"{OUTDIR}/apuchip_expected.bin", "wb") as fh:
    fh.write(struct.pack(f"<{len(samples)}h", *samples))

print(f"frames={NFRAMES} sr={SR} samples(interleaved)={len(samples)}")
print(f"  -> {OUTDIR}/apuchip_writes.csv")
print(f"  -> {OUTDIR}/apuchip_expected.bin")
