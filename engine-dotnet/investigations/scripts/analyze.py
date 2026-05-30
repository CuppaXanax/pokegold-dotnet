import wave, numpy as np, glob, os

def load(path):
    with wave.open(path,'rb') as w:
        n=w.getnframes(); sr=w.getframerate()
        a=np.frombuffer(w.readframes(n),dtype=np.int16).astype(np.float64)/32768.0
    return a, sr

for path in sorted(glob.glob('render_*.wav')):
    a,sr=load(path)
    rms=np.sqrt(np.mean(a**2))
    peak=np.max(np.abs(a))
    dc=np.mean(a)
    # spectral centroid + high-frequency energy ratio (>8kHz vs total)
    seg=a[sr*1:sr*4]  # steady window
    if len(seg)<1024:
        seg=a
    win=np.hanning(len(seg))
    sp=np.abs(np.fft.rfft(seg*win))
    fr=np.fft.rfftfreq(len(seg),1/sr)
    p=sp**2
    centroid=np.sum(fr*p)/np.sum(p) if np.sum(p)>0 else 0
    hf=np.sum(p[fr>8000])/np.sum(p) if np.sum(p)>0 else 0
    # peak frequencies
    top=fr[np.argsort(p)[-6:]][::-1]
    print(f"{path:18s} rms={rms:.4f} peak={peak:.3f} dc={dc:+.4f} centroid={centroid:6.0f}Hz hf>8k={hf*100:4.1f}%  topHz={np.round(top).astype(int)}")
