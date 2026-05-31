"""APU port validation — STAGE 3 (compare).

Asserts our ApuChip output is sample-for-sample identical to PyBoy's own sound.py for
the identical register-write script. Binary PASS/FAIL with a committed threshold.

PASS criterion (committed BEFORE running): >=99.9% of samples bit-identical AND max
absolute deviation <=1 LSB. (Both are pure integer algorithms; we expect 100%/0, but
allow a 1-in-1000 single-LSB slack for any benign cycle-phase rounding at sample
boundaries. Anything beyond that is a real port bug.)
"""
import sys, struct
import numpy as np

OUTDIR = "investigations/trace"

def load_bin(path):
    with open(path, "rb") as fh:
        raw = fh.read()
    return np.array(struct.unpack(f"<{len(raw)//2}h", raw), dtype=np.int32)

exp = load_bin(f"{OUTDIR}/apuchip_expected.bin")
our = load_bin(f"{OUTDIR}/apuchip_ours.bin")

n = min(len(exp), len(our))
if len(exp) != len(our):
    print(f"WARN length mismatch: expected={len(exp)} ours={len(our)} (comparing first {n})")
exp = exp[:n]; our = our[:n]

diff = np.abs(exp - our)
identical = int(np.sum(diff == 0))
frac_identical = identical / n
maxdev = int(diff.max())
# cosine for context
cos = float(np.dot(exp, our) / (np.linalg.norm(exp) * np.linalg.norm(our) + 1e-9))

print(f"samples compared        = {n}")
print(f"bit-identical fraction  = {frac_identical:.6f}   [target >=0.999]")
print(f"max abs deviation (LSB) = {maxdev}              [target <=1]")
print(f"cosine (context)        = {cos:.6f}")

if maxdev > 1:
    # show first few divergences for debugging
    idx = np.where(diff > 1)[0][:10]
    print("first divergences (sample_index, expected, ours):")
    for i in idx:
        print(f"  {i}: {exp[i]} vs {our[i]}")

ok = frac_identical >= 0.999 and maxdev <= 1
print("VERDICT:", "PASS — ApuChip is a faithful port of PyBoy sound.py" if ok else "FAIL — port diverges from oracle")
sys.exit(0 if ok else 1)
