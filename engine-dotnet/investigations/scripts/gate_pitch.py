"""Pitch-sequence gate: are we playing the right NOTES in the right ORDER?

Strips out all timing — duration, capture lead-in offset, tempo-carry jitter — and
compares only the ordered list of distinct note periods per channel (rests collapsed).
This answers the melody question directly and is immune to the alignment problems that
defeat the frame-by-frame and event-duration gates.

Reports the longest common prefix and an LCS-style match ratio per channel.
"""
import sys, csv

def load(path):
    return list(csv.DictReader(open(path)))

def pitch_seq(rows, ch):
    """Ordered list of note periods; consecutive duplicates and rests collapsed."""
    seq = []
    last = None
    for r in rows:
        on = int(r[f"on{ch}"])
        per = int(r[f"freq{ch}"])
        cur = per if (on and per > 0) else 0   # 0 == rest
        if cur != last:
            if cur != 0:
                seq.append(cur)
            last = cur
    return seq

def common_prefix(a, b):
    n = 0
    for x, y in zip(a, b):
        if x != y:
            break
        n += 1
    return n

def lcs_len(a, b):
    # classic DP; sequences are short (tens-hundreds of notes)
    m, n = len(a), len(b)
    dp = [0] * (n + 1)
    for i in range(1, m + 1):
        prev = 0
        ai = a[i - 1]
        for j in range(1, n + 1):
            tmp = dp[j]
            dp[j] = prev + 1 if ai == b[j - 1] else max(dp[j], dp[j - 1])
            prev = tmp
    return dp[n]

def main():
    ours = load(sys.argv[1])
    orac = load(sys.argv[2])
    nch = int(sys.argv[3]) if len(sys.argv) > 3 else 4
    print(f"Pitch-sequence gate  ours={sys.argv[1]}  oracle={sys.argv[2]}")
    overall = True
    for ch in range(1, nch + 1):
        oseq = pitch_seq(orac, ch)
        useq = pitch_seq(ours, ch)
        # A channel with no pitched notes (the noise/drum channel) has no melody to
        # compare — its correctness is verified byte-for-byte by gate_seq instead.
        if not oseq and not useq:
            print(f"  ch{ch}: N/A   (no pitched notes — drum/noise channel; see gate_seq)")
            continue
        # The oracle capture starts mid-song; find where ours first matches the
        # oracle's opening note, then compare from there.
        best_skip, best_lcs = 0, -1
        head = oseq[:8]
        for skip in range(max(1, len(useq))):
            if head and skip < len(useq) and useq[skip] == head[0]:
                lcs = lcs_len(oseq, useq[skip:])
                if lcs > best_lcs:
                    best_lcs, best_skip = lcs, skip
        if best_lcs < 0:
            best_lcs = lcs_len(oseq, useq); best_skip = 0
        cp = common_prefix(oseq, useq[best_skip:])
        denom = min(len(oseq), len(useq) - best_skip) or 1
        ratio = best_lcs / denom
        status = "PASS" if ratio >= 0.95 else "FAIL"
        if ratio < 0.95:
            overall = False
        print(f"  ch{ch}: {status}  oracle {len(oseq)} notes, ours {len(useq)} "
              f"(skip {best_skip}); common-prefix {cp}, LCS {best_lcs}/{denom} = {ratio:.2f}")
        if cp < denom:
            i = cp
            o = oseq[i] if i < len(oseq) else None
            u = useq[best_skip + i] if best_skip + i < len(useq) else None
            print(f"        first divergence at note {i}: oracle={o} ours={u}")
    print("RESULT:", "PASS" if overall else "FAIL")
    sys.exit(0 if overall else 1)

if __name__ == "__main__":
    main()
