"""Single-number APU spectral-shape score vs hardware (lower = better).

Reuses the steady window from the aligned capture. For each stereo side and each
non-overlapping partial (h1,h2,h3,h5 — h4 is the 75%-duty null, skipped), computes
log2(ours/hardware). A free overall gain is removed (subtract the mean log-ratio),
so the score is the STD of the residual log-ratios = how well the harmonic BALANCE
(timbre) matches, independent of loudness. Prints `SCORE <std>`.
"""
import sys, csv, wave
import numpy as np

K = 45
RATE = 44100
OUR_SPF = 44100.0 / (4194304.0 / 70224.0)


def load_wav(p):
    w = wave.open(p)
    a = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64)
    return a.reshape(-1, 2)


def load_align(p):
    rows = {}
    with open(p) as fh:
        next(fh)
        for line in fh:
            fr, s, c = line.split()
            rows[int(fr)] = (int(s), int(c))
    return rows


orac = list(csv.DictReader(open("investigations/trace/title_oracle.csv")))
align = load_align("investigations/trace/title_align.txt")
ref = load_wav("investigations/wav/ref_title_aligned.wav")
ours = load_wav(sys.argv[1] if len(sys.argv) > 1 else "investigations/wav/our_title.wav")

# longest steady run (freq1/2/3 constant & on), trimmed 3 frames each end
runs, a0 = [], 0
for f in range(1, len(orac)):
    same = all(orac[f][f"freq{n}"] == orac[a0][f"freq{n}"] for n in (1, 2, 3)) and \
        all(int(orac[f][f"on{n}"]) for n in (1, 2, 3))
    if not same:
        runs.append((a0, f - 1)); a0 = f
runs.append((a0, len(orac) - 1))
a, b = max(runs, key=lambda ab: ab[1] - ab[0]); a += 3; b -= 3

P = [int(orac[a][f"freq{n}"]) for n in (1, 2, 3)]
pulse = lambda p: 131072.0 / (2048 - p)
wav = lambda p: 65536.0 / (2048 - p)
funds = [pulse(P[0]), pulse(P[1]), wav(P[2])]

hw = ref[align[a][0]:align[b][0] + align[b][1]]
oa = int(round((a + K) * OUR_SPF)); ob = int(round((b + 1 + K) * OUR_SPF))
ow = ours[oa:ob]
L = 1 << int(np.floor(np.log2(min(len(hw), len(ow)))))
hw, ow = hw[:L], ow[:L]


def spec(x):
    x = x - x.mean()
    X = np.abs(np.fft.rfft(x * np.hanning(len(x))))
    f = np.fft.rfftfreq(len(x), 1.0 / RATE)
    return f, X


def amp(f, X, hz, hw_=15.0):
    sel = (f >= hz - hw_) & (f <= hz + hw_)
    return X[sel].max() if sel.any() else 0.0


sides = [(0, funds[0]), (0, funds[1]), (1, funds[2]), (1, funds[1])]
logr = []
for col, fund in sides:
    fr, Xr = spec(hw[:, col]); fo, Xo = spec(ow[:, col])
    for h in (1, 2, 3, 5):
        hz = fund * h
        ar, ao = amp(fr, Xr, hz), amp(fo, Xo, hz)
        if ar > 1e3 and ao > 1e3:
            logr.append(np.log2(ao / ar))
logr = np.array(logr)
print(f"SCORE {logr.std():.4f}   (n={len(logr)}, mean_gain_log2={logr.mean():+.2f})")
