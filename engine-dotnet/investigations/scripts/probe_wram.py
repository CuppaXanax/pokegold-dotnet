"""Probe: boot to title, then dump raw audio WRAM to find live channel state."""
import sys
from pyboy import PyBoy

ROM = r"N:\dev\scratch\slop-ware\pokegold.worktrees\agents-fun-compiler-high-level-port\pokegold.gbc"
pb = PyBoy(ROM, window="null", sound_emulated=False)

WMUSICID = 0xC19D
def u16(a): return pb.memory[a] | (pb.memory[a+1] << 8)

last = -1
for f in range(1500):
    if f % 30 == 0: pb.button("start")
    if f % 30 == 15: pb.button("a")
    pb.tick(1, False, False)
    mid = u16(WMUSICID)
    if mid != last:
        print(f"frame {f}: wMusicID={mid} (low={pb.memory[WMUSICID]}, hi={pb.memory[WMUSICID+1]})")
        last = mid
    if mid != 0 and f > 300:
        break

# advance a few more frames so music engine populates channels
for _ in range(20):
    pb.tick(1, False, False)

print("\n=== wChannel1..4 raw (0xC001 + n*0x32, 50 bytes each) ===")
for n in range(4):
    base = 0xC001 + n*0x32
    b = [pb.memory[base+i] for i in range(0x32)]
    print(f"ch{n+1} @ {hex(base)}: " + " ".join(f"{x:02x}" for x in b))

print("\n=== scan 0xC000..0xC200 for nonzero runs ===")
nz = [a for a in range(0xC000, 0xC200) if pb.memory[a] != 0]
print(f"{len(nz)} nonzero bytes; first 40 addrs:", [hex(a) for a in nz[:40]])

# Look at wSoundOutput / NR52 mirror and the actual hardware regs FF10-FF26
print("\n=== hardware audio regs FF10-FF26 ===")
print(" ".join(f"{pb.memory[0xFF00+i]:02x}" for i in range(0x10, 0x27)))
pb.stop()
