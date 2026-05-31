"""Derive the empirical 'stick-frame' rule from the oracle.

For each music channel, walk frames and classify every note boundary:
a boundary is the frame where dur resets UP (new note loaded). A 'stick frame'
is the frame immediately before, where env (or duty/oct) changes but freq/dur
hold one extra frame. Report, per boundary, whether a stick occurred and which
field(s) changed on the stick frame vs the load frame.
"""
import csv, sys

path = sys.argv[1] if len(sys.argv) > 1 else "investigations/trace/title_oracle.csv"
rows = list(csv.DictReader(open(path)))

def col(r, name): return int(r[name])

for ch in (1, 2, 3):
    print(f"\n=== ch{ch} ===")
    prev = None
    for i, r in enumerate(rows):
        cur = dict(freq=col(r, f"freq{ch}"), env=col(r, f"env{ch}"),
                   dur=col(r, f"dur{ch}"), duty=col(r, f"duty{ch}"),
                   oct=col(r, f"oct{ch}"), on=col(r, f"on{ch}"))
        if prev is not None:
            # boundary = dur increases (new note loaded)
            if cur["dur"] > prev["dur"] + 0:  # dur went up
                if cur["dur"] > prev["dur"]:
                    changed = [k for k in ("freq", "env", "duty", "oct")
                               if cur[k] != prev[k]]
                    # was there a stick? look back: did env change one frame earlier
                    # while freq held?
                    stick = ""
                    if i >= 2:
                        pp = dict(freq=col(rows[i-2], f"freq{ch}"),
                                  env=col(rows[i-2], f"env{ch}"),
                                  dur=col(rows[i-2], f"dur{ch}"))
                        # prev is the stick candidate: env changed vs pp but freq same & dur held(<2)
                        if prev["env"] != pp["env"] and prev["freq"] == pp["freq"] and prev["dur"] <= 1:
                            stick = f"  <-- STICK at f{i-1} (env {pp['env']}->{prev['env']}, freq held {prev['freq']})"
                    print(f"f{i:3} load: freq {prev['freq']}->{cur['freq']} dur->{cur['dur']} "
                          f"env {prev['env']}->{cur['env']} changed={changed}{stick}")
        prev = cur
