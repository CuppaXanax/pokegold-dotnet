"""Boot pokegold.gbc with NO input and log wMusicID over time (hex)."""
import sys
from pyboy import PyBoy

ROM = sys.argv[1]
pb = PyBoy(ROM, window="null", sound_emulated=True, sound_sample_rate=44100, log_level="ERROR")
WMUSICID = 0xC19D
last = None
for f in range(2400):
    pb.tick(1, False, True)
    mid = pb.memory[WMUSICID] | (pb.memory[WMUSICID + 1] << 8)
    if mid != last:
        print(f"frame {f:5d} ({f/60:5.1f}s): wMusicID={mid} (0x{mid:04x})")
        last = mid
pb.stop()
