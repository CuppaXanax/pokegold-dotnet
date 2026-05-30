"""Objective spectral comparison of two WAVs (reference vs ours).

Reports per-octave-band energy distribution and spectral centroid, so we can see
where our synth has excess energy (aliasing/harshness) vs the real hardware.
"""
import sys, wave
import numpy as np

def load_mono(path):
    with wave.open(path, "rb") as w:
        n = w.getnframes(); ch = w.getnchannels(); sr = w.getframerate()
        raw = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float64)
    if ch == 2:
        raw = raw.reshape(-1, 2).mean(axis=1)
    return raw, sr

def highpass(x, sr, fc=50.0):
    # one-pole high-pass to remove DC/sub-bass (GB hardware high-passes the DAC)
    rc = 1.0 / (2*np.pi*fc)
    a = rc / (rc + 1.0/sr)
    y = np.empty_like(x)
    prev_x = 0.0; prev_y = 0.0
    for i in range(len(x)):
        y[i] = a*(prev_y + x[i] - prev_x)
        prev_x = x[i]; prev_y = y[i]
    return y

def avg_spectrum(x, sr, nfft=4096):
    # Welch-style averaged magnitude spectrum, normalized to unit total power.
    win = np.hanning(nfft)
    step = nfft // 2
    acc = np.zeros(nfft // 2 + 1)
    cnt = 0
    for i in range(0, len(x) - nfft, step):
        seg = x[i:i+nfft] * win
        mag = np.abs(np.fft.rfft(seg))**2
        acc += mag; cnt += 1
    acc /= max(cnt, 1)
    acc /= acc.sum() + 1e-12
    freqs = np.fft.rfftfreq(nfft, 1.0/sr)
    return freqs, acc

def bands(freqs, spec):
    edges = [0, 250, 500, 1000, 2000, 4000, 8000, 16000, 22050]
    out = []
    for a, b in zip(edges[:-1], edges[1:]):
        m = (freqs >= a) & (freqs < b)
        out.append((a, b, spec[m].sum()))
    return out

ref, sr1 = load_mono(sys.argv[1])
ours, sr2 = load_mono(sys.argv[2])
assert sr1 == sr2, (sr1, sr2)
sr = sr1

print(f"DC offset: ref={ref.mean():8.1f}  ours={ours.mean():8.1f}  (int16 units)")
# Remove DC / sub-bass that the real GB hardware high-passes away
ref = highpass(ref, sr)
ours = highpass(ours, sr)

# RMS-normalize both
ref /= (np.sqrt(np.mean(ref**2)) + 1e-9)
ours /= (np.sqrt(np.mean(ours**2)) + 1e-9)

fr, sref = avg_spectrum(ref, sr)
fo, sours = avg_spectrum(ours, sr)

cen_ref = (fr * sref).sum()
cen_ours = (fo * sours).sum()

print(f"{'band (Hz)':>14} | {'ref %':>7} | {'ours %':>7} | {'ratio':>6}")
print("-"*46)
for (a, b, er), (_, _, eo) in zip(bands(fr, sref), bands(fo, sours)):
    ratio = (eo + 1e-9) / (er + 1e-9)
    print(f"{a:6d}-{b:5d} | {100*er:6.2f}% | {100*eo:6.2f}% | {ratio:5.2f}x")
print("-"*46)
print(f"spectral centroid:  ref={cen_ref:7.1f} Hz   ours={cen_ours:7.1f} Hz   ({cen_ours/cen_ref:.2f}x)")
hf_ref = sref[fr >= 6000].sum(); hf_ours = sours[fo >= 6000].sum()
print(f"HF energy (>=6kHz): ref={100*hf_ref:5.2f}%   ours={100*hf_ours:5.2f}%   ({(hf_ours+1e-9)/(hf_ref+1e-9):.2f}x)")
