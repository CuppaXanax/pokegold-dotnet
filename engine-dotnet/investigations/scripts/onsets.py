import sys, wave, numpy as np

def load_mono(path):
    w = wave.open(path, 'rb')
    n = w.getnframes(); sr = w.getframerate()
    d = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float64)
    if w.getnchannels() == 2:
        d = d.reshape(-1, 2).mean(axis=1)
    w.close()
    return d, sr

def onsets(path, hop=256, win=1024, thresh=1.6):
    d, sr = load_mono(path)
    # short-time energy
    nfr = (len(d) - win) // hop
    e = np.empty(nfr)
    for i in range(nfr):
        seg = d[i*hop:i*hop+win]
        e[i] = np.sqrt((seg*seg).mean())
    # spectral-flux-like: positive energy difference
    de = np.diff(e, prepend=e[0])
    # onset when energy rises sharply relative to local level
    on = []
    for i in range(1, nfr):
        if de[i] > 0 and e[i] > thresh*e[i-1] + 20 and (not on or (i-on[-1])*hop/sr > 0.04):
            on.append(i)
    times = np.array(on)*hop/sr
    return times, sr

for path in sys.argv[1:]:
    t, sr = onsets(path)
    ioi = np.diff(t)
    print(f"\n{path}: {len(t)} onsets")
    print("  onset times (s):", " ".join(f"{x:.3f}" for x in t[:40]))
    print("  IOIs (frames@60):", " ".join(f"{x*60:.1f}" for x in ioi[:40]))
