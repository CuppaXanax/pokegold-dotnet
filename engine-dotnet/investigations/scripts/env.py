import wave, numpy as np

def load(path):
    with wave.open(path,'rb') as w:
        n=w.getnframes(); sr=w.getframerate()
        a=np.frombuffer(w.readframes(n),dtype=np.int16).astype(np.float64)/32768.0
    return a, sr

for ch in ['ch4','ch1','ch3']:
    a,sr=load(f'listen_{ch}.wav')
    # short-time RMS over 20ms windows
    win=int(sr*0.02)
    nseg=len(a)//win
    env=np.array([np.sqrt(np.mean(a[i*win:(i+1)*win]**2)) for i in range(nseg)])
    # how much does amplitude vary? silence fraction (below 10% of peak)
    pk=env.max()
    silent=np.mean(env < 0.1*pk)*100
    # count distinct "hits": rising edges above threshold
    above=env>0.3*pk
    hits=np.sum((~above[:-1])&above[1:])
    print(f"{ch}: segs={nseg} peakEnv={pk:.3f} silentFrac={silent:4.1f}%  hits/8s~{hits}  env[0:25]={np.round(env[:25],2)}")
