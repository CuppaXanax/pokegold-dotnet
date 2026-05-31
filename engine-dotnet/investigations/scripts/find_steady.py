"""Find the longest constant-register window in apu_regs.csv and decode the voices."""
import csv
rows = list(csv.reader(open("investigations/trace/apu_regs.csv")))
hdr, data = rows[0], rows[1:]
def key(r): return tuple(r[1:])  # all regs (skip frame)
best_len, best_start, cur_start = 0, 0, 0
for i in range(1, len(data) + 1):
    if i == len(data) or key(data[i]) != key(data[cur_start]):
        if i - cur_start > best_len:
            best_len, best_start = i - cur_start, cur_start
        cur_start = i
print(f"longest constant window: frames {data[best_start][0]}..{data[best_start+best_len-1][0]} ({best_len} frames)")
idx = {n: k for k, n in enumerate(hdr)}
r = data[best_start]
def g(name): return int(r[idx[name]])
print("NR10-14 ch1:", [hex(g(f'r{a:04X}')) for a in range(0xFF10,0xFF15)])
print("NR21-24 ch2:", [hex(g(f'r{a:04X}')) for a in range(0xFF16,0xFF1A)])
print("NR30-34 ch3:", [hex(g(f'r{a:04X}')) for a in range(0xFF1A,0xFF1F)])
print("NR41-44 ch4:", [hex(g(f'r{a:04X}')) for a in range(0xFF20,0xFF24)])
print("NR50-52    :", [hex(g(f'r{a:04X}')) for a in range(0xFF24,0xFF27)])
print("wave RAM   :", [hex(g(f'w{a:04X}')) for a in range(0xFF30,0xFF40)])
# decode ch1/ch2 pulse
for ch,(n1,n2,n3,n4) in [("ch1",(0xFF11,0xFF12,0xFF13,0xFF14)),("ch2",(0xFF16,0xFF17,0xFF18,0xFF19))]:
    duty=g(f'r{n1:04X}')>>6; vol=g(f'r{n2:04X}')>>4; envper=g(f'r{n2:04X}')&7; envdir=(g(f'r{n2:04X}')>>3)&1
    period=((g(f'r{n4:04X}')&7)<<8)|g(f'r{n3:04X}')
    freq=131072/(2048-period) if period<2048 else 0
    print(f"{ch}: duty={duty} vol={vol} envper={envper} envdir={envdir} period={period} freq={freq:.1f}Hz")
n3,n4=0xFF1D,0xFF1E
period3=((g(f'r{n4:04X}')&7)<<8)|g(f'r{n3:04X}')
print(f"ch3 wave: period={period3} freq={65536/(2048-period3):.1f}Hz NR30={hex(g('rFF1A'))} NR32={hex(g('rFF1C'))}")
print(f"NR51 panning = {g('rFF25'):08b}")
