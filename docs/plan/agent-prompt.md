# Kickoff prompt for iteration agents

Paste the block below as the opening prompt for any agent session working toward victory.
It deliberately defers all detail to `victory-plan.md` so the prompt never goes stale.

---

You are working on the Pokémon Gold F# port in this repository. Your single source of
truth is `docs/plan/victory-plan.md` — read it fully before doing anything else. It
contains the architecture map, build commands, conventions, and the ordered work items.

Work this loop, one item at a time:

1. Pick the **first incomplete item** in the plan's suggested order (Workstream A legs
   A1→A21 first, then B1→B12, then C, then D). An item is complete when its row in
   `victory-plan.md` is prefixed with ✅. Do not skip ahead; do not work two items at once.
2. Read the disassembly sources the item names (`maps/*.asm`, `engine/**`,
   `constants/**`) **before** writing code. The disassembly is the spec. Do not implement
   from general knowledge of Gen 2 — several "well-known facts" are wrong.
3. Implement the smallest correct change, following the named pattern to copy.
4. Run the full suite: `cd engine-dotnet && dotnet test tests/PokeGold.Tests`. All green
   or you are not done.
5. Update the bookkeeping in the same change: prefix the item's row in `victory-plan.md`
   with ✅, and move any `ConformanceLedgerTests.fs` entries you implemented (the ledger
   is enforced by tests, so a stale entry fails the build).
6. Commit: short imperative subject, explanatory body, one concern per commit,
   `Co-authored-by:` trailer for yourself.
7. Go to 1.

Hard rules — violating any of these is worse than making no progress:

- **Never weaken, delete, or work around a failing assertion to get green.** A failing
  leg test means the *runtime* has a bug; fix the engine. If you believe the test itself
  is wrong, prove it from the disassembly source in your write-up and fix it with the
  citation in the commit body.
- **Never edit `Data/Generated/*` by hand** — change the parser/DataGen and rebuild. If
  you change a macro expansion, update the pinned total in `CoverageSweepTests.fs` with a
  comment explaining the delta.
- **Never commit a ROM, save file, or build artifact.** `.gitignore` already covers them;
  do not "fix" that.
- **Do not build the solution** — `PokeGold.Host.Android` needs the Android SDK. Build
  `src/PokeGold.Game`, `src/PokeGold.Host`, and the tests individually.
- **No drive-by refactors.** Touch only what the work item needs. Purity/architecture
  rework is explicitly out of scope (see the vNext notes in the plan).
- **Stop conditions** — stop and write up findings (in the item's row of
  `victory-plan.md`, marked ⚠️ with a short note) instead of grinding, when: you have
  retried the same failure 3 times; the fix seems to require changing a core seam
  (`Script.fs` effect types, `SaveData` schema, scene-stack semantics); or two work items
  appear to contradict each other.

Start now: read `docs/plan/victory-plan.md`, state which item you are picking up and why
it is the first incomplete one, then begin.
