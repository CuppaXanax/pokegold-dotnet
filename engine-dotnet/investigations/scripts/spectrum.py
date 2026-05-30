import sys, wave, numpy as np

def load(path):
    w = wave.open(path, 'rb')
    n = w.getnframes(); sr = w.getframerate()
    d = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float64)
    if w.getnchannels() == 2:
        d = d.reshape(-1, 2).mean(axis=1)
    w.close()
    # one-pole DC high-pass (~50 Hz)
    a = 0.995
    y = np.zeros_like(d); px = 0.0; py = 0.0
    for i in range(len(d)):
        y[i] = a * (py + d[i] - px); px = d[i]; py = y[i]
    return y, sr

bands = [0,250,500,1000,2000,4000,8000,16000,22050]
for path in sys.argv[1:]:
    y, sr = load(path)
    spec = np.abs(np.fft.rfft(y * np.hanning(len(y))))
    freq = np.fft.rfftfreq(len(y), 1.0/sr)
    power = spec**2
    total = power.sum() + 1e-9
    print(f"\n{path}  rms={np.sqrt((y**2).mean()):.1f}")
    cent = (freq*power).sum()/total
    print(f"  centroid={cent:.0f} Hz")
    for lo,hi in zip(bands, bands[1:]):
        m = (freq>=lo)&(freq<hi)
        print(f"   {lo:5d}-{hi:5d}: {100*power[m].sum()/total:5.1f}%")
