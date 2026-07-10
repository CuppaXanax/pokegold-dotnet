Complete the full plan in docs\plan\plan.md. The terminal goal: a 100%-completable Pokémon Gold — one continuous fresh-save run defeats Red, and one persistent save owns all 251 species. No debug warps, no state mutations, no Tackle-only trainers, no silent no-ops on required commands.

Execute epics in critical-path order:

## Epic 1 — Battle and Progression Integrity (BAT-001 → BAT-024)

Authentic trainer/wild parties from .asm source data. Stable party identity. Full Gen 2 stat calculations. Per-enemy EXP/stat-exp/money. All TM/HM/evolution methods. Move learning decisions. Blackout on loss that aborts victory-only scripts.

## Epic 2 — Overworld and Script Integrity (OVR-001 → OVR-011, SCR-001 → SCR-010, UI-001 → UI-003)

Complete all seven field moves as real map actions. Wire fishing rods, headbutt, and item use from menus to overworld. Implement required script command stubs. Fix battle-script control flow (win/loss/catch/run). Finish daycare, breeding foundations, contest, Game Corner, and side-system specials.

## Epic 3 — Continuous Fresh-Save Route (RTE-001 → RTE-004, Stories 3.2 → 3.22)

Convert staged A1–A21 component tests into one persistent StartNewGame checkpoint chain. Each checkpoint must earn its state through real input — no debug warp, set-event, set-flag, set-scene, seed-party, seed-item, or auto-win. Extend one checkpoint at a time: starter → Falkner → Bugsy → ... → Lance → Kanto badges → Red → credits.

## Epic 4 — All 251 Species (DEX-001 → DEX-024)

Runtime acquisition recipes for every species. Playable fishing, headbutt, breeding/hatching, roamers, contest catches, offline trade terminal with version imports and trade evolutions. One long-lived save reaches DexOwn = 251 without debug mutation. Oak/Diploma fires.

## Epic 5 — Release Quality (REL-001 → REL-015)

Burn down all RequiredFor100Percent stubs. Close sprite gaps. Replace generic battle-animation tints with visible primitives. Test all three starters, losses, retries, save interruptions, capacity edge cases. Align README and status docs. Validate clean-machine desktop build.

---

## Execution protocol

For each work item:
1. Read the relevant .asm source (the behavioral spec).
2. Write a failing test at the highest applicable verification layer.
3. Implement the smallest conformance fix.
4. Run `dotnet test .\tests\PokeGold.Tests`.
5. Commit with a focused message naming the work item (e.g., "BAT-005: stable party identity").
6. Update docs\plan\plan.md status (⬜ → ✅) only when the proving test passes.

If a work item is already done (test exists and passes without debug setup), mark it ✅ and move on. If partially done, finish it. If blocked by something upstream, note the block in plan.md and skip to the next unblocked item.

Do not ask for confirmation on local reads, edits, tests, or commits. Do ask before any destructive action, external write, or material scope expansion beyond this plan.

## Victory gate

The plan is complete when:
- `dotnet test` passes with a continuous StartNewGame → Red credits checkpoint chain
- A separate test reaches DexOwn = 251 from a post-game save using only runtime-playable channels
- Zero RequiredFor100Percent entries remain StubNoOp or Unknown
- docs\plan\plan.md shows all items ✅
