"""Characterize per-note-onset timing between ours and the oracle on ch1.

Aligns at a fixed offset k (ours[f+k] ~ oracle[f]) and lists every frame where
freq1 changes in EITHER trace, showing the onset-frame delta. Reveals whether our
timing is uniform, bounded ±1 jitter, or cumulatively drifting.
"""
import sys, csv

def load(path):
    with open(path) as fh:
        return list(csv.DictReader(fh))

ours = load(sys.argv[1])
oracle = load(sys.argv[2])
k = int(sys.argv[3]) if len(sys.argv) > 3 else 45
ch = sys.argv[4] if len(sys.argv) > 4 else "1"

def onsets(rows, field):
    out = []
    prev = None
    for i, r in enumerate(rows):
        v = r[field]
        if v != prev:
            out.append((i, v))
            prev = v
    return out

fr = f"freq{ch}"
o_on = onsets(oracle, fr)
u_on = onsets(ours, fr)

print(f"oracle freq{ch} onsets: {len(o_on)}; ours: {len(u_on)}; k={k}")
print("oracle_f  oracle_freq | nearest ours onset (ours_f-k)  delta")
# For each oracle onset, find the ours onset with the same freq value nearest in aligned time.
ui = 0
maxdelta = 0
for (of, ofreq) in o_on:
    # aligned target frame in ours = of + k
    # find ours onset matching freq value closest to of+k
    best = None
    for (uf, ufreq) in u_on:
        if ufreq == ofreq:
            aligned = uf - k
            if best is None or abs(aligned - of) < abs(best[0] - of):
                best = (aligned, uf, ufreq)
    if best is None:
        print(f"{of:6}  {ofreq:>6} | NO MATCH")
        continue
    delta = best[0] - of
    maxdelta = max(maxdelta, abs(delta))
    flag = "" if delta == 0 else ("  <== " + ("LATE" if delta > 0 else "EARLY"))
    print(f"{of:6}  {ofreq:>6} | ours_f={best[1]:6} aligned={best[0]:6}  delta={delta:+d}{flag}")

print(f"max |delta| = {maxdelta}")
