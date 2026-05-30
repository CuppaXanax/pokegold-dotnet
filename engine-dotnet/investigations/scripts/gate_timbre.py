"""APU timbre gate — MONO full-mix, apples-to-apples (the only valid APU metric).

WHY MONO: the hardware title reference is mono (L==R byte-identical; confirmed by a
ch1-solo capture whose L and R are identical, i.e. NR51 panning is inactive because
the game runs the attract/title in the MONO sound-option state). That invalidates
every per-stereo-side script (score_apu.py / compare_chord.py / harmonic_rolloff.py):
they assumed ref LEFT == ch1+ch2, but ref LEFT is actually the full mix ch1+ch2+ch3.
This gate compares the MONO SUM of both renders over the title's longest constant-
register window, where Gate 1 has proven our channel registers are byte-identical to
hardware — so any difference here is PURELY APU synthesis (DAC/mix/filter), not the
sequencer.

WHAT IT REPORTS (all gate-able numbers):
  - crest factor (peak/RMS): time-domain "spikiness". Ours peaks negative-skewed from
    two coincident 75%-duty pulse troughs.
  - spectral centroid / rolloff85 (Hz): perceived brightness.
  - band-energy distribution (% of total): WHERE the spectra differ.

KNOWN RESIDUAL (documented, not a sequencer bug): ours carries a high-harmonic deficit
(2-22 kHz) and a low-mid excess vs the PyBoy reference. The FIR explains only the
>15 kHz part; the 2-11 kHz part is most consistent with ALIASING in the PyBoy reference
(the wave channel's sharp 32-step edges at ~146 Hz fold high harmonics back below
Nyquist) that our oversampled + band-limited APU faithfully avoids. Closing it fully
means deciding whether to match PyBoy's aliased brightness or stay analog-faithful.

Usage: py gate_timbre.py [tol_pct]   (default band tolerance 2.5 percentage points)
Exit code 0 = within tolerance on every band, else 1.
"""
import csv
import sys
import wave
import numpy as np

K = 45                                   # frame offset ours->oracle (title)
RATE = 44100
OUR_SPF = 44100.0 / (4194304.0 / 70224.0)
BANDS = [(0, 300), (300, 800), (800, 2000), (2000, 5000), (5000, 11000), (11000, 22050)]
TRACE = "investigations/trace/title_oracle.csv"
ALIGN = "investigations/trace/title_align.txt"
REF = "investigations/wav/ref_title_aligned.wav"
OURS = "investigations/wav/our_title.wav"


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


def steady_window():
    """Longest run of constant ch1/ch2/ch3 registers (trim 3 frames each edge)."""
    orac = list(csv.DictReader(open(TRACE)))
    runs, a0 = [], 0
    for f in range(1, len(orac)):
        same = all(orac[f][f"freq{n}"] == orac[a0][f"freq{n}"] for n in (1, 2, 3)) and \
            all(int(orac[f][f"on{n}"]) for n in (1, 2, 3))
        if not same:
            runs.append((a0, f - 1)); a0 = f
    runs.append((a0, len(orac) - 1))
    a, b = max(runs, key=lambda ab: ab[1] - ab[0])
    return a + 3, b - 3


def mono_window():
    align = load_align(ALIGN)
    a, b = steady_window()
    ref, ours = load_wav(REF), load_wav(OURS)
    hw = ref[align[a][0]:align[b][0] + align[b][1]]
    oa = int(round((a + K) * OUR_SPF))
    ob = int(round((b + 1 + K) * OUR_SPF))
    ow = ours[oa:ob]
    L = min(len(hw), len(ow))
    return hw[:L, 0] + hw[:L, 1], ow[:L, 0] + ow[:L, 1]


def shape(x):
    x = x - x.mean()
    rms = np.sqrt(np.mean(x ** 2))
    peak = np.max(np.abs(x))
    crest = peak / rms if rms > 0 else 0.0
    w = np.hanning(len(x))
    X = np.abs(np.fft.rfft(x * w))
    fr = np.fft.rfftfreq(len(x), 1.0 / RATE)
    e = X ** 2
    centroid = float(np.sum(fr * e) / np.sum(e))
    cum = np.cumsum(e) / np.sum(e)
    rolloff = float(fr[np.searchsorted(cum, 0.85)])
    bandpct = [float(e[(fr >= lo) & (fr < hi)].sum() / e.sum() * 100.0) for lo, hi in BANDS]
    return crest, centroid, rolloff, bandpct


def main():
    tol = float(sys.argv[1]) if len(sys.argv) > 1 else 2.5
    hw, ow = mono_window()
    hc, hcen, hroll, hb = shape(hw)
    oc, ocen, oroll, ob = shape(ow)
    print(f"window {len(hw)} samples ({len(hw)/RATE:.2f}s)  tol +/-{tol}pp")
    print(f"{'metric':>10} {'hw':>8} {'ours':>8}")
    print(f"{'crest':>10} {hc:8.2f} {oc:8.2f}")
    print(f"{'centroid':>10} {hcen:8.0f} {ocen:8.0f}")
    print(f"{'rolloff85':>10} {hroll:8.0f} {oroll:8.0f}")
    print(f"\n{'band Hz':>14} {'hw%':>7} {'ours%':>7} {'delta':>7}")
    worst = 0.0
    for (lo, hi), h, o in zip(BANDS, hb, ob):
        d = o - h
        worst = max(worst, abs(d))
        print(f"{lo:6d}-{hi:<7d} {h:7.1f} {o:7.1f} {d:+7.1f}")
    ok = worst <= tol
    print(f"\nworst band delta {worst:.1f}pp -> {'PASS' if ok else 'FAIL'}")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
