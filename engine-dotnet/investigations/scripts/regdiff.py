"""Falsifiable per-frame APU register diff: our driver vs PyBoy's real capture.

Compares our `dump_regs.fsx` CSV against PyBoy's `apu_regs.csv` (pb.memory[0xFF10..]).
Only registers whose hardware *read-back* equals the written byte are meaningful
(write-only period/length/trigger regs read as 0xFF/masked, so they are reported but
flagged). The decisive percussion fingerprint is NR43 (rFF22, fully readable poly byte).

Finds the constant frame offset (PyBoy capture starts mid-boot) that best aligns the
readable registers, then reports:
  - per-register match fraction over the aligned overlap
  - how NR43 match degrades over time (reveals tempo drift, falsifiably)

usage: python regdiff.py <ours.csv> <ref_apu_regs.csv>
"""
import sys, csv
import numpy as np

# addr (0xFF__) -> (name, readable?)  readable == raw written byte equals memory read-back
READABLE = {
    0x12: ("NR12 ch1env", True), 0x17: ("NR22 ch2env", True),
    0x11: ("NR11 ch1duty", True), 0x16: ("NR21 ch2duty", True),
    0x21: ("NR42 ch4env", True), 0x22: ("NR43 ch4poly", True),
    0x25: ("NR51 pan", True),    0x10: ("NR10 sweep", True),
}
# write-only / masked (reported but not used for alignment / pass-judgement)
WRITEONLY = {0x13, 0x14, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x20, 0x23, 0x24, 0x26}

def load(path):
    with open(path, newline="") as f:
        r = csv.reader(f); hdr = next(r)
        rows = [[int(x) for x in row] for row in r if row]
    cols = {name: i for i, name in enumerate(hdr)}
    return hdr, cols, np.array(rows, dtype=int)

def col(cols, addr):
    return cols.get(f"r{0xFF00|addr:04X}")  # rFF12 etc

def main():
    ours_p, ref_p = sys.argv[1], sys.argv[2]
    _, oc, ours = load(ours_p)
    _, rc, ref = load(ref_p)
    no, nr = ours.shape[0], ref.shape[0]

    # readable-register column indices
    rd_addrs = [a for a in READABLE]
    def vec(data, cols, addr):
        c = col(cols, addr)
        return data[:, c] if c is not None else None

    # --- find best constant frame offset on combined readable registers ---
    maxoff = min(250, nr - 50)
    win = min(200, no, nr - maxoff)
    best, best_off = -1.0, 0
    for off in range(0, maxoff):
        tot = match = 0
        for a in rd_addrs:
            ov = vec(ours, oc, a); rv = vec(ref, rc, a)
            if ov is None or rv is None: continue
            o = ov[:win]; r = rv[off:off+win]
            m = min(len(o), len(r))
            match += int(np.sum(o[:m] == r[:m])); tot += m
        frac = match / max(tot, 1)
        if frac > best:
            best, best_off = frac, off
    print(f"best frame offset = {best_off}  (readable-reg match {best*100:.1f}% over first {win} frames)\n")

    off = best_off
    overlap = min(no, nr - off)
    print(f"aligned overlap = {overlap} frames\n")
    print(f"{'reg':16} {'match%':>7}  note")
    for addr in sorted(set(list(READABLE) + list(WRITEONLY))):
        ov = vec(ours, oc, addr); rv = vec(ref, rc, addr)
        if ov is None or rv is None: continue
        o = ov[:overlap]; r = rv[off:off+overlap]
        m = min(len(o), len(r))
        frac = np.mean(o[:m] == r[:m]) * 100
        if addr in READABLE:
            name = READABLE[addr][0]; tag = ""
        else:
            name = f"FF{addr:02X}"; tag = "(write-only/masked - ignore)"
        print(f"{name:16} {frac:6.1f}%  {tag}")

    # --- NR43 (percussion) match over time, in 100-frame buckets: reveals tempo drift ---
    a = 0x22
    ov = vec(ours, oc, a); rv = vec(ref, rc, a)
    if ov is not None and rv is not None:
        o = ov[:overlap]; r = rv[off:off+overlap]
        m = min(len(o), len(r)); o, r = o[:m], r[:m]
        print(f"\nNR43 (ch4 poly / percussion) match over time, buckets of 100 frames:")
        for s in range(0, m, 100):
            e = min(s+100, m)
            print(f"  frames {s:4d}-{e:4d}: {np.mean(o[s:e]==r[s:e])*100:5.1f}%")
        # also count distinct nonzero NR43 values (drum vocabulary)
        print(f"  distinct nonzero NR43 ours={len(set(o[o>0]))} ref={len(set(r[r>0]))}")

if __name__ == "__main__":
    main()
