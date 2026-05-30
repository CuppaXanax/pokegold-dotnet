"""Gate 2 — APU isolation via steady-chord spectral comparison.

Inputs (all from the aligned capture_both.py run + our render):
  trace/title_oracle.csv          register trace (hardware)
  trace/title_align.txt           hardware frame -> PCM sample map
  wav/ref_title_aligned.wav       hardware PCM (frame 0 = oracle frame 0)
  wav/our_title.wav               our APU PCM (frame 0 = song start)

Because Gate 1 proved our register stream is byte-identical to hardware (offset
k=45: oracle frame f == our frame f+k), any difference in the rendered audio on a
window where the registers are constant is caused ONLY by the APU (synthesis) stage.

This script finds the longest steady window (all of freq1/2/3 constant and on),
extracts that window from both PCMs, and compares their normalized magnitude
spectra: per-partial levels and a harshness metric (fraction of energy above 8 kHz).

Usage: python compare_chord.py [k]
"""
import sys, csv, wave
import numpy as np

K = int(sys.argv[1]) if len(sys.argv) > 1 else 45
RATE = 44100
OUR_SPF = 44100.0 / (4194304.0 / 70224.0)  # our samples-per-frame (~738.36)


def load_wav(path):
    w = wave.open(path)
    n = w.getnframes()
    a = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float64)
    return a.reshape(-1, 2)  # (n,2) L,R kept separate to avoid panning downmix bias


def load_align(path):
    rows = {}
    with open(path) as fh:
        next(fh)  # rate line
        for line in fh:
            fr, start, cnt = line.split()
            rows[int(fr)] = (int(start), int(cnt))
    return rows


orac = list(csv.DictReader(open("investigations/trace/title_oracle.csv")))
align = load_align("investigations/trace/title_align.txt")
ref = load_wav("investigations/wav/ref_title_aligned.wav")
ours = load_wav("investigations/wav/our_title.wav")


def steady_runs(rows):
    runs = []
    a = 0
    for f in range(1, len(rows)):
        same = all(rows[f][f"freq{n}"] == rows[a][f"freq{n}"] for n in (1, 2, 3)) and \
            all(int(rows[f][f"on{n}"]) for n in (1, 2, 3))
        if not same:
            runs.append((a, f - 1))
            a = f
    runs.append((a, len(rows) - 1))
    return runs


runs = steady_runs(orac)
# longest steady run, but trim 3 frames at each end to avoid attack/release transients
best = max(runs, key=lambda ab: ab[1] - ab[0])
a, b = best
a += 3
b -= 3
if b - a < 6:
    print("no long-enough steady window found:", best); sys.exit(2)

periods = [int(orac[a][f"freq{n}"]) for n in (1, 2, 3)]
def pulse_hz(p): return 0.0 if p <= 0 or p >= 2048 else 131072.0 / (2048 - p)
def wave_hz(p): return 0.0 if p <= 0 or p >= 2048 else 65536.0 / (2048 - p)
funds = [pulse_hz(periods[0]), pulse_hz(periods[1]), wave_hz(periods[2])]

print(f"steady window: oracle frames {a}..{b} ({b-a+1} frames)")
print(f"periods (ch1,2,3) = {periods}  fundamentals = "
      + ", ".join(f"{h:.1f}Hz" for h in funds))

# Hardware PCM window from the align map.
hw_start = align[a][0]
hw_end = align[b][0] + align[b][1]
ref_win = ref[hw_start:hw_end]

# Our PCM window: oracle frame f == our frame f+K.
our_a = int(round((a + K) * OUR_SPF))
our_b = int(round((b + 1 + K) * OUR_SPF))
our_win = ours[our_a:our_b]

L = min(len(ref_win), len(our_win))
L = 1 << int(np.floor(np.log2(L)))  # power-of-two for a clean FFT
ref_win = ref_win[:L]
our_win = our_win[:L]
print(f"comparison window: {L} samples ({L/RATE*1000:.0f} ms)")


def spectrum(x):
    x = x - x.mean()
    w = np.hanning(len(x))
    X = np.abs(np.fft.rfft(x * w))
    f = np.fft.rfftfreq(len(x), 1.0 / RATE)
    return f, X


def amp_at(f, X, hz, halfwidth=15.0):
    if hz <= 0:
        return 0.0
    sel = (f >= hz - halfwidth) & (f <= hz + halfwidth)
    return X[sel].max() if sel.any() else 0.0


def hi_frac(f, X, cut=8000.0):
    e = X ** 2
    return e[f >= cut].sum() / (e.sum() + 1e-12)


def centroid(f, X):
    return (f * X).sum() / (X.sum() + 1e-12)


# Panning (titlescreen): ch1=LEFT, ch3=RIGHT, ch2=center(both).
# Compare each stereo side independently so side-panned channels aren't halved
# by a mono downmix. Within a side the two fundamentals don't overlap, so their
# ratio is a clean, normalization-free measure of channel balance + timbre.
sides = {
    "LEFT  (ch1 + ch2)": (0, [("ch1", funds[0]), ("ch2", funds[1])]),
    "RIGHT (ch3 + ch2)": (1, [("ch3", funds[2]), ("ch2", funds[1])]),
}

for label, (col, chans) in sides.items():
    fr, Xr = spectrum(ref_win[:, col])
    fo, Xo = spectrum(our_win[:, col])
    print(f"\n=== {label} ===")
    print(f"{'partial':>20} | {'hardware':>10} | {'ours':>10} | {'ratio o/h':>9}")
    for name, fund in chans:
        for h in range(1, 6):
            hz = fund * h
            if hz <= 0 or hz > RATE / 2:
                break
            ar = amp_at(fr, Xr, hz)
            ao = amp_at(fo, Xo, hz)
            rat = ao / ar if ar > 1e-6 else float("nan")
            print(f"  {name} h{h} {hz:7.0f}Hz | {ar:10.1f} | {ao:10.1f} | {rat:9.2f}")
    # Per-side fundamental balance (the two channels' fundamentals, ratio of ratios).
    (n1, f1), (n2, f2) = chans
    hr = amp_at(fr, Xr, f1) / (amp_at(fr, Xr, f2) + 1e-9)
    orr = amp_at(fo, Xo, f1) / (amp_at(fo, Xo, f2) + 1e-9)
    print(f"  balance {n1}/{n2} fundamental:  hardware={hr:.3f}  ours={orr:.3f}"
          + ("   <-- MISMATCH" if abs(np.log2((orr+1e-9)/(hr+1e-9))) > 0.6 else ""))
    print(f"  centroid: hw={centroid(fr,Xr):.0f}Hz ours={centroid(fo,Xo):.0f}Hz"
          f"  | >8kHz energy: hw={hi_frac(fr,Xr):.4f} ours={hi_frac(fo,Xo):.4f}")
