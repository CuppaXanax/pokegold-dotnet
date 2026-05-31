"""E1 compare: our full-pipeline render vs PyBoy's audio, TIME-RESOLVED.

Metrics (committed before running):
  - mean per-STFT-frame spectral cosine (log-magnitude) after best global alignment
  - RMS-envelope Pearson r  (captures dynamics/transients/silence the band-gate missed)
PASS (faithful) = spec_cos >= 0.95 AND rms_r >= 0.95 ; < 0.90 = clearly broken.
"""
import sys, wave
import numpy as np

def load(path):
    with wave.open(path, "rb") as w:
        sr = w.getframerate(); n = w.getnframes(); ch = w.getnchannels()
        a = np.frombuffer(w.readframes(n), dtype="<i2").astype(np.float64)
    a = a.reshape(-1, ch)
    mono = a.mean(axis=1)
    return mono, sr

def resample(x, sr, target):
    if sr == target: return x
    t = np.linspace(0, len(x)/sr, int(len(x)/sr*target), endpoint=False)
    return np.interp(t, np.arange(len(x))/sr, x)

TARGET = 32000
ours, sro = load("investigations/trace/ours_title_44k.wav")
ref,  srr = load("investigations/trace/ref_title_48k.wav")
ours = resample(ours, sro, TARGET); ref = resample(ref, srr, TARGET)

# Apples-to-apples DC block. PyBoy's raw output is UNIPOLAR with a large, channel-
# activity-dependent DC pedestal (mean ~4900, range 0..9500); our render is already
# DC-blocked (bipolar, ~0 mean). DC is inaudible, so comparing a pedestal-laden signal
# against a centered one is invalid — the pedestal dominates the STFT's low bins and
# the alignment xcorr. Apply the SAME ~20 Hz one-pole high-pass to BOTH before metrics.
def dcblock(x, sr, hz=20.0):
    a = float(np.exp(-2.0 * np.pi * hz / sr))
    y = np.empty_like(x); prev_x = 0.0; prev_y = 0.0
    for i in range(len(x)):
        prev_y = a * prev_y + (x[i] - prev_x); prev_x = x[i]; y[i] = prev_y
    return y
ours = dcblock(ours, TARGET); ref = dcblock(ref, TARGET)

# normalize to unit RMS
def norm(x): 
    r = np.sqrt(np.mean(x**2)); return x / r if r > 0 else x
ours = norm(ours); ref = norm(ref)

# best global integer alignment via cross-correlation (search +-1.0s)
maxlag = int(1.0 * TARGET)
L = min(len(ours), len(ref))
a = ours[:L]; b = ref[:L]
best_lag, best_c = 0, -2
for lag in range(-maxlag, maxlag + 1, 8):
    if lag >= 0:
        x = a[lag:]; y = b[:len(x)]
    else:
        y = b[-lag:]; x = a[:len(y)]
    m = min(len(x), len(y))
    if m < TARGET: continue
    c = np.dot(x[:m], y[:m]) / m
    if c > best_c: best_c, best_lag = c, lag
lag = best_lag
if lag >= 0: a = ours[lag:]; b = ref[:len(a)]
else: b = ref[-lag:]; a = ours[:len(b)]
L = min(len(a), len(b)); a = a[:L]; b = b[:L]
print(f"alignment lag = {lag} samples ({lag/TARGET*1000:.1f} ms), waveform xcorr/sample = {best_c:.4f}")

# STFT log-magnitude spectral cosine per frame
def stft_logmag(x, nfft=1024, hop=256):
    win = np.hanning(nfft)
    frames = []
    for i in range(0, len(x) - nfft, hop):
        seg = x[i:i+nfft] * win
        mag = np.abs(np.fft.rfft(seg))
        frames.append(np.log1p(mag))
    return np.array(frames)
Sa = stft_logmag(a); Sb = stft_logmag(b)
n = min(len(Sa), len(Sb)); Sa, Sb = Sa[:n], Sb[:n]
cos = np.sum(Sa*Sb, axis=1) / (np.linalg.norm(Sa,axis=1)*np.linalg.norm(Sb,axis=1) + 1e-9)
spec_cos = float(np.mean(cos))
frac_bad = float(np.mean(cos < 0.7))

# RMS envelope (10 ms windows) Pearson
def rms_env(x, win=320):
    n = len(x)//win
    return np.sqrt(np.mean(x[:n*win].reshape(n,win)**2, axis=1))
ea, eb = rms_env(a), rms_env(b)
m = min(len(ea), len(eb)); ea, eb = ea[:m], eb[:m]
rms_r = float(np.corrcoef(ea, eb)[0,1])

print(f"spectral cosine (mean over time) = {spec_cos:.4f}   [target >=0.95]")
print(f"  fraction of frames cos<0.7     = {frac_bad:.3f}")
print(f"RMS-envelope Pearson r           = {rms_r:.4f}   [target >=0.95]")

# --- Disambiguate timbre vs timing: per-1s segment with LOCAL best-lag alignment.
# If locally-aligned spectral cosine is high but global RMS-r is low -> timing/tempo
# (sequencer) problem. If even local spectral is low -> timbre (APU) problem.
seg = TARGET // 4  # 0.25 s — fine resolution kills within-window note-timing confound
loc_cos = []
for s in range(0, L - 2*seg, seg):
    aw = a[s:s+seg]
    # search local lag +-50ms against ref
    ll = int(0.05*TARGET); bestc, bestl = -2, 0
    for lag in range(-ll, ll+1, 4):
        bs = s + lag
        if bs < 0 or bs+seg > len(b): continue
        bw = b[bs:bs+seg]
        c = np.dot(aw, bw)/seg
        if c > bestc: bestc, bestl = c, lag
    bw = b[s+bestl:s+bestl+seg]
    Sa2 = stft_logmag(aw); Sb2 = stft_logmag(bw)
    nn = min(len(Sa2), len(Sb2))
    cc = np.sum(Sa2[:nn]*Sb2[:nn],axis=1)/(np.linalg.norm(Sa2[:nn],axis=1)*np.linalg.norm(Sb2[:nn],axis=1)+1e-9)
    loc_cos.append(np.mean(cc))
loc_cos_mean = float(np.mean(loc_cos)) if loc_cos else 0.0
print(f"LOCAL-aligned spectral cosine    = {loc_cos_mean:.4f}  (removes tempo drift -> pure timbre)")
print(f"  per-second: {[round(x,3) for x in loc_cos]}")
verdict = "FAITHFUL" if (spec_cos>=0.95 and rms_r>=0.95) else ("BROKEN" if (spec_cos<0.90 or rms_r<0.90) else "MARGINAL")
print(f"VERDICT: {verdict}")
