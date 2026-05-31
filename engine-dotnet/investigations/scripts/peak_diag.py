"""Cheap decisive diagnostic: do OURS and the PyBoy REF play the same FREQUENCIES?

For matched time windows, find each signal's dominant spectral peaks (mono mix).
- Peaks ALIGN but harmonic balance differs  -> pure TIMBRE gap (APU synthesis).
- Peaks SHIFTED by a constant ratio          -> octave/pitch/tempo error.
- REF has strong peaks OURS lacks (or vice versa) -> missing/extra channel.

Loads ref_title_48k.wav (48k) and ours_title_44k.wav (44.1k), resamples ours->48k,
and after a global best-lag alignment prints the top peaks per window side by side.
"""
import wave, numpy as np

def load(path):
    with wave.open(path, "rb") as w:
        sr = w.getframerate(); n = w.getnframes(); ch = w.getnchannels()
        a = np.frombuffer(w.readframes(n), dtype="<i2").astype(np.float64)
    a = a.reshape(-1, ch).mean(axis=1)  # mono
    return a, sr

ref, sr_ref = load("investigations/trace/ref_title_48k.wav")
our, sr_our = load("investigations/trace/ours_title_44k.wav")

# resample ours to ref rate by linear interp
t_our = np.arange(len(our)) / sr_our
t_new = np.arange(0, t_our[-1], 1.0 / sr_ref)
our_rs = np.interp(t_new, t_our, our)
sr = sr_ref

# normalize
ref = ref / (np.abs(ref).max() + 1e-9)
our_rs = our_rs / (np.abs(our_rs).max() + 1e-9)

# global best lag via FFT cross-correlation on a 2s chunk
L = min(len(ref), len(our_rs), sr * 2)
a = ref[:L] - ref[:L].mean(); b = our_rs[:L] - our_rs[:L].mean()
nfft = 1 << int(np.ceil(np.log2(2*L)))
XC = np.fft.irfft(np.fft.rfft(a, nfft) * np.conj(np.fft.rfft(b, nfft)), nfft)
XC = np.concatenate([XC[-(L-1):], XC[:L]])
lag = XC.argmax() - (L - 1)
print(f"global best lag = {lag} samples ({lag/sr*1000:.1f} ms), peakcorr={XC.max()/(np.linalg.norm(a)*np.linalg.norm(b)+1e-9):.3f}")

if lag > 0:
    ref_al = ref[lag:]; our_al = our_rs
else:
    ref_al = ref; our_al = our_rs[-lag:]
n = min(len(ref_al), len(our_al))
ref_al = ref_al[:n]; our_al = our_al[:n]

def top_peaks(x, sr, k=8, fmax=8000):
    win = np.hanning(len(x))
    X = np.abs(np.fft.rfft(x * win))
    f = np.fft.rfftfreq(len(x), 1/sr)
    m = f <= fmax
    X = X[m]; f = f[m]
    # local maxima
    idx = []
    for i in range(2, len(X)-2):
        if X[i] > X[i-1] and X[i] >= X[i+1] and X[i] > X.max()*0.06:
            idx.append(i)
    idx = sorted(idx, key=lambda i: -X[i])[:k]
    idx = sorted(idx, key=lambda i: f[i])
    return [(round(f[i],1), round(float(X[i]/X.max()),2)) for i in idx]

WIN = sr  # 1s windows
for w in range(0, min(n, sr*5), WIN):
    rseg = ref_al[w:w+WIN]; oseg = our_al[w:w+WIN]
    if len(rseg) < WIN: break
    print(f"\n--- window {w//sr}s ---")
    print("  REF peaks :", top_peaks(rseg, sr))
    print("  OURS peaks:", top_peaks(oseg, sr))
