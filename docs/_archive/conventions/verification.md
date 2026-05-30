# Verification strategy for the C# reimplementation

Goal: turn the repo's existing "byte-identical build output" mindset into a runtime verification stack for the C# port. The asm repo already treats `make compare` + `roms.sha1` as the real proof of correctness; the C# port should treat reference-runtime snapshots the same way, with richer diagnostics because gameplay diverges long before a final save or screenshot does. (docs/recon/build-system.md:225-248)

Practical note: this is not a formal proof in the theorem-prover sense. It is a layered oracle strategy that makes divergence observable early, localizes it quickly, and keeps regression fixtures stable across future implementation work.

## 1. Verification target: what must match

The project-wide conventions already say the canonical state is raw bytes first, typed views second, with banking/timing explicit rather than hidden behind objects or frameworks. Verification should therefore compare raw memory domains plus the small set of controller/timing state that makes those bytes meaningful. (docs/conventions/memory-model.md:7-37,166-204,206-227,381-402,425-432; docs/conventions/translation-patterns.md:1337-1345)

### Canonical comparison domains

| Domain | Compare | Why |
|---|---|---|
| Logic state | WRAM0, WRAM bank 1, HRAM, selected I/O mirrors/bank state | Most gameplay state, RNG, menus, scripts, battle state, party data, joypad mirrors, and scheduling flags live here. (docs/recon/memory-map.md:13-15,29-49) |
| Video state | VRAM bank 0/1, OAM, palette buffers/register mirrors | Gold's visible state is timing-shaped and not reducible to "final tilemap only." (docs/recon/hazards.md:72-99; docs/recon/memory-map.md:48-52,69-94) |
| Timing state | VBlank counter, bank selectors, interrupt state, RTC latch/status, volatile registers needed for determinism | Frame order, stale carry RNG, DMA gating, RTC behavior, and interrupt-sensitive logic depend on this state. (docs/recon/execution-flow.md:143-176; docs/recon/hazards.md:42-68,86-99,111-132) |
| Save/battery state | All 32 KiB SRAM + opaque RTC state blob | Save correctness is defined by exact SRAM layout/checksum boundaries, not by deserialized objects. (docs/recon/data-formats.md:541-603; docs/recon/memory-map.md:41-43,61-67; docs/conventions/platform-interface.md:87-125) |
| Framebuffer | Final 160x144 resolved frame | Needed because STAT/LY effects can produce correct-looking VRAM bytes at frame end while the actually displayed frame was wrong. (docs/recon/hazards.md:79-91; docs/conventions/platform-interface.md:17-25,39-43) |
| Audio control state | Audio WRAM block, wave RAM, `rAUD*`/PCM-related register state | Audio is register-shaped internally and advances once per frame. Compare state, not just human listening impressions. (docs/recon/memory-map.md:84-89; docs/conventions/platform-interface.md:19-25,47-66) |

### Recommended hash domains

Do **not** start with one monolithic "whole emulator hash." Use separate canonical hashes per frame:

1. `logicHash` = WRAM0 + WRAM1 + HRAM + stable controller/bank state
2. `videoHash` = VRAM0 + VRAM1 + OAM + palette state
3. `timingHash` = interrupt/timer/RTC/MBC state needed for determinism
4. `audioHash` = audio WRAM + wave RAM + sound registers
5. `saveHash` = SRAM + RTC blob when a scenario touches persistence

**CONTENTIOUS:** use SHA-256 for committed/canonical fixtures because it is ubiquitous and easy to reproduce in .NET. If per-frame throughput becomes a bottleneck, future code can add a faster non-canonical cache hash locally, but fixture files and CI output should stay on one boring, portable algorithm.

## 2. Verification approaches A-F

### A. Lockstep execution against a reference emulator

**Verdict:** make this the primary oracle, but start at **VBlank/frame boundaries**, not per-instruction whole-game lockstep.

Gold already has a documented stable frame boundary: `VBlank_Normal` increments the VBlank counter, advances RNG, copies scroll/window state, performs at most one high-priority VRAM job, serves tile requests, optionally runs OAM DMA, clears `wVBlankOccurred`, polls joypad, runs sound, then returns through the common epilogue. That is the natural checkpoint for frame-accurate comparison. (docs/recon/execution-flow.md:145-176)

### What to compare at frame lockstep

Compare, at minimum:

- WRAM0 + WRAM bank 1
- HRAM
- VRAM bank 0 + bank 1
- OAM
- palette state / palette buffers
- selected I/O state: scroll/window regs, LCD mode-affecting state, interrupt flags, timer state, sound regs, current ROM/SRAM/VRAM bank, RTC latch/status
- SRAM only in scenarios that open/save/load battery-backed state

Use full byte diffs on mismatch, but persist frame hashes in fixtures so normal CI stays cheap.

### Granularity recommendation

- **Default:** per completed VBlank/frame
- **Escalation:** per routine entry/exit for hot routines
- **Last resort:** per instruction or per scanline window, but only for short captured traces when debugging flag/timing bugs

**CONTENTIOUS:** do not make per-instruction whole-game lockstep the first milestone. The port is not a CPU emulator, and forcing the entire test harness to pretend it is one will slow progress without improving early confidence proportionally.

### Pros

- Best general-purpose bug detector
- Catches cross-subsystem interactions that unit tests miss
- Natural home for deterministic replay and glitch verification

### Cons

- Needs a reference emulator integration layer
- Raw per-frame byte dumps are bulky without hashing/compression
- Per-instruction/scanline modes are expensive and should be targeted, not universal

### B. Memory hashing per frame

**Verdict:** mandatory for routine CI.

This is the runtime analog of `make compare`. The current asm repo already proves final artifacts with hashes; the C# port should prove frame checkpoints the same way. (docs/recon/build-system.md:225-248)

### What to hash

Hash the canonical domains from section 1, not just WRAM. Hashing WRAM+HRAM alone is a good early milestone, but the final target must also cover video, timing, audio-control state, and save state when relevant.

### Recommended fixture shape

```text
frame, inputMask, logicHash, videoHash, timingHash, audioHash?, saveHash?
```

Store full dumps only for:

- first/last frame of a scenario
- known milestone frames
- divergence artifacts produced by CI/nightly runs

### Pros

- Cheap enough for every commit
- Easy to diff and archive
- Naturally supports replay fixtures and nightly long runs

### Cons

- A hash tells you that something diverged, not where
- Weak if it omits a critical domain
- Still needs a reference data generator

### C. Recorded input replay (TAS-style)

**Verdict:** mandatory for integration/regression testing.

Use frame-indexed input logs as the universal scenario format. Gold already samples input once per VBlank and mirrors it into the joypad state used by game logic, so frame-indexed input is the correct abstraction. (docs/recon/execution-flow.md:163-166,211-214; docs/recon/memory-map.md:33-35)

### Recommendation

Define an internal fixture format first:

- `input.bin`: 1 byte button mask per emulated frame
- `manifest.json`: ROM SHA1, symbol-file SHA1, emulator/version, scenario name, checkpoint list
- optional screenshots/saves/full dumps

**UNCLEAR:** there are no in-tree movie fixtures or replay tools today; the repo currently has only build/hash verification, not a gameplay test harness. Build an internal format first, then add importers for BizHawk/Gambatte/BGB-style movies later if useful. (docs/recon/build-system.md:246-248)

### Where recordings should come from

1. Generate authoritative recordings with the chosen reference emulator
2. Commit short deterministic scenarios into the repo
3. Keep long exploratory runs/nightly artifacts out of git unless they are tiny hash logs
4. Optionally import community TAS inputs later, but do not make third-party movie formats the core test ABI

### Pros

- Best reusable regression test format
- Naturally composes with frame hashes and screenshots
- Easy for future Claude instances to extend

### Cons

- Needs fixture authoring/generation pipeline
- Final-state-only replays are too weak unless paired with periodic hashes/snapshots

### D. Per-routine unit tests against captured ROM state

**Verdict:** mandatory for high-risk subsystems.

Some bug classes are easier to isolate at routine granularity than through full replay. That is especially true for stale-carry RNG, EXP math, AI scoring, save checksum logic, DV inheritance, and packed-PP behavior. The recon docs repeatedly call out carry-flag arithmetic, mixed endianness, and layout-sensitive routines as hot spots. (docs/recon/hazards.md:42-68,163-189; docs/recon/data-formats.md:53-127,541-603)

### How to capture reference data

Use the reference emulator plus symbol addresses from the `.sym` file produced by the existing build. The asm build already emits `.sym` and `.map`, so the capture pipeline can resolve function entry points and relevant RAM symbols without hardcoding raw addresses. (docs/recon/build-system.md:83-99)

### Good candidates

- `Random` / `RandomRange`
- experience-at-level / EXP gain paths
- damage calculation and stat stage application
- AI move scoring and AI item use
- save checksum / verify checksum
- RTC normalization helpers
- breeding DV inheritance
- text command termination/dispatch edge cases

### Pros

- Fast and localized
- Excellent first line of defense for arithmetic/layout bugs
- Easier to diagnose than a 2,000-frame replay mismatch

### Cons

- Misses cross-frame ordering bugs by itself
- Needs careful fixture capture at exact entry/exit points

### E. Screenshot / framebuffer comparison

**Verdict:** important complement, not a substitute.

The platform boundary already recommends a final resolved framebuffer interface because the original engine depends on scanline-time LCD behavior and per-line overrides. That means screenshot comparison is the right rendering oracle at the host boundary. (docs/conventions/platform-interface.md:17-25,27-46)

### Recommendation

Use pixel-perfect framebuffer comparisons for:

- boot/logo/title milestones
- battle intro/UI scenes
- map transition scenes
- credits / Hall of Fame scenes
- specific LCD-effect-heavy scenes

Produce a diff image on failure.

### Pros

- Catches visible rendering bugs immediately
- Validates the actual platform-facing output
- Very useful for milestone demos

### Cons

- Cannot prove gameplay logic or save correctness
- Can miss invisible memory divergence
- Can be noisy if used before the renderer is stable

### F. Save file compatibility

**Verdict:** mandatory once saving exists.

The save format is explicitly byte-shaped: `sGameData` is checksummed, `sOptions` and check sentinels are outside the checksum, the active box mirror lives outside the main checksum, and numbered boxes/backups are split across banks. That means compatibility testing must be byte-level and bidirectional. (docs/recon/data-formats.md:541-603; docs/recon/memory-map.md:41-43)

### Required tests

1. Load original-ROM save in C#
2. Save in C#, then reload in original ROM/reference emulator
3. Round-trip original save through C# with no gameplay changes and verify expected unchanged bytes remain unchanged
4. Explicitly cover active box, numbered boxes, backup blocks, Hall of Fame, and RTC status paths

### Pros

- Directly validates one of the most layout-sensitive subsystems
- Catches many corruption bugs and serialization "cleanups"

### Cons

- Only meaningful after save/RTC code exists
- Needs curated fixture saves for edge cases

## 3. Recommended layered strategy

### Minimum viable verification for the first useful milestone

For the earliest playable target, implement this stack in order:

1. **Reference replay harness** with frame-indexed input
2. **Per-frame `logicHash` + `videoHash`** at the VBlank boundary
3. **Framebuffer compare** at milestone checkpoints
4. **Routine tests** for RNG and one or two arithmetic/layout hotspots

That is the smallest stack that can catch the most likely early mistakes: wrong frame order, wrong memory layout, wrong RNG advancement, and obviously wrong rendering. It also matches the repo's existing preference for bytes-first correctness over subjective playability. (docs/recon/execution-flow.md:145-176; docs/conventions/memory-model.md:7-37; docs/conventions/translation-patterns.md:1337-1345)

### Gold standard final strategy

Use all six approaches together:

- SameBoy-backed frame lockstep as the top-level oracle
- domain hashes on every replayed frame
- targeted routine fixtures for arithmetic/AI/RTC/text/save hot spots
- framebuffer comparison for visible scenes
- bidirectional save compatibility
- curated glitch replay/catalog tests

### What each layer is best at

| Layer | Best at catching |
|---|---|
| Routine fixtures | Arithmetic, flags, mixed-endian layout bugs |
| Frame hashes | Cross-subsystem state drift, regressions on ordinary commits |
| Lockstep byte diff | First exact divergence frame and region |
| Screenshot compare | Rendering and LCD-effect failures |
| Save compatibility | Serialization/checksum/box-layout mistakes |
| Glitch catalog | "Helpful cleanup" regressions that remove original bugs |

### Recommended implementation order

1. Internal replay format + reference harness
2. Per-frame VBlank hashing (`logicHash`, `videoHash` first)
3. First short replay: boot to title
4. RNG / EXP / checksum routine fixtures
5. Screenshot diff at title + one battle scene
6. Save load/save round-trip tests
7. Full glitch catalog
8. Longer nightly replays
9. Narrow instruction/scanline trace tooling for hard timing bugs only

## 4. Bug class coverage

| Bug class | Primary catch | Secondary catch | Why |
|---|---|---|---|
| Wrong arithmetic result (EXP, damage, stats) | D | A, B, C | Routine fixtures localize math bugs fastest; replays/hashes catch any that slip through. |
| Wrong RNG sequence (stale carry, `rDIV` timing) | D, A | B, C | RNG needs both routine-level capture and frame-timing validation. (docs/recon/hazards.md:44-55; docs/recon/memory-map.md:29-31) |
| Wrong control flow (flag-dependent branches) | D, A | B | Flag bugs often show up first in isolated fixtures, then in frame drift. |
| Wrong memory layout (struct size, offset, endianness) | D, B | F, A | Mixed-endian/layout-sensitive data is easiest to catch with byte-exact before/after routine fixtures and save tests. (docs/recon/data-formats.md:53-127,541-603; docs/conventions/memory-model.md:381-402) |
| Wrong frame timing (VBlank order, VRAM/OAM timing) | A | B, E | The VBlank schedule is explicitly ordered and timing-sensitive. (docs/recon/execution-flow.md:145-176; docs/recon/hazards.md:72-99) |
| Wrong rendering (tile composition, palettes, scanline effects) | E, A | B | Framebuffer diff catches visible output; lockstep/hashes explain why. |
| Wrong audio (channel state, note timing) | A, B | C | Compare audio-control state and wave RAM during replay; add PCM spot checks later if needed. |
| Wrong save format (checksum, offsets, active box) | F | D, B | Save compatibility is the direct oracle; checksum/unit fixtures catch local logic. |
| Missing glitch | Catalog tests | A, C, D, F | Every glitch should be encoded as a named regression, not left to incidental replay coverage. |

## 5. Glitch regression catalog

General rule for all rows: **pass** means the named checkpoints produce the same bytes/framebuffer/save outcome as the reference run; **fail** on first mismatch or on any protective "cleanup" that suppresses the original bug.

| Order | Glitch | Trigger fixture / inputs | State to verify | Best approach |
|---:|---|---|---|---|
| 1 | RNG manipulation | Short deterministic overworld replay with frame delays before a known RNG consumer | `hRandomAdd`, `hRandomSub`, selected timing state, downstream outcome | C + D + B |
| 2 | Coin Case glitch | Reference fixture in Coin Case context; use item once through the real menu/text path | Text engine control-flow outcome, relevant stack/text-state bytes, resulting WRAM/HRAM state | D + A |
| 3 | Bad Clone glitch | Box-change save flow interrupted at scripted cut points | `sBox`, numbered box SRAM, `sChecksum`, `sBackupChecksum`, active box after reload | C + F + A |
| 4 | Save corruption exploits | No-save Hall of Fame and other edge save flows | Core save blocks vs active box mirror, backup blocks, reload result | F + C + D |
| 5 | Trainer AI exploits | Deterministic battle fixtures for Conversion2, Mean Look/Toxic, bad item-read cases | AI chosen move/item and battle WRAM state after decision | D + C |
| 6 | Celebi Egg glitch | Pre-seeded visible `EGG` slot whose hidden species byte is Celebi; advance to hatch | `wPartySpecies` slot, hidden species byte in party struct, hatch result | D + C |
| 7 | Experience underflow | Level-1 Medium-Slow fixture receives minimal EXP | EXP bytes, resulting level, derived stat bytes | D |
| 8 | Stat recalculation glitch | Give stat EXP without triggering recalc; compare party view vs boxed/temp path | Cached party stats vs recalculated temp/boxed stats | D + C |
| 9 | Berry / RTC rollover glitch | Advance RTC across day 139 -> 140 -> 141 and reload/continue | RTC state, daily timers, fruit-tree/daily-event bytes | D + C + F |
| 10 | Map connection bugs | Surf from shoreline directly across a map connection | `wMapGroup`, `wMapNumber`, `wXCoord`, `wYCoord`, map-loading state after scripted step | C + B |
| 11 | Wrong pocket TMs | Corrupted bag fixture with TM/HM ID in wrong pocket, then open/use through normal UI | Pocket arrays, TM/HM array, UI/use behavior outcome | D + C |
| 12 | DV/stat inheritance quirks | Fixed-parent breeding fixture with known DVs | Egg DV bytes, compatibility result, downstream shiny/stat effects if relevant | D |
| 13 | Text buffer overflow | Long-name/text substitution fixture such as `PresentFailedText` | `wMonOrItemNameBuffer`, `wStringBuffer*`, tilemap/text output damage | D + E |
| 14 | Move PP overflow / PP Up bug | Disabled move with 0 visible PP but PP Up bits set | Packed PP byte, move-availability decision, Struggle path | D |
| 15 | Type matchup errors | AI-facing battle fixture where `CheckTypeMatchup` misuse changes move choice | AI decision state, not just final damage | D + C |

### Why this order

- Items 1-4 build the shared harness: timing, save/state snapshots, and interruptible replay
- Items 5-9 cover the most project-threatening gameplay/RTC correctness bugs
- Items 10-15 are still required, but depend less on foundational harness work

The source glitch catalog should be treated as a required backlog, not optional trivia. The repo explicitly treats glitches as load-bearing behavior. (docs/recon/glitches.md:7-25; docs/conventions/translation-patterns.md:1341-1345)

## 6. Reference emulator selection

### Recommendation

Use **SameBoy as the primary reference emulator**.

### Why SameBoy

- strongest fit for byte-accuracy goals
- widely regarded as highly accurate on DMG/CGB timing
- explicit library build target makes automation practical
- permissive license is friendlier for an embedded verification sidecar than GPL-style alternatives
- cross-platform and CI-friendly

### Secondary tools

- **Gambatte:** keep as a secondary cross-check and possible TAS-import source, but do not make it the first embedded oracle. Its ecosystem value is high for TAS/speedrunning, but it is a worse fit for a bundled, automation-first C# verification harness.
- **BGB:** keep as a manual debugger of last resort. It is excellent for investigation, but not the default CI oracle because it is Windows-centric and not an obvious library-first dependency.

### Integration shape

Build a thin native capture shim around the chosen emulator and call it from .NET. The shim should expose only what the verifier needs:

- load ROM + optional save/RTC state
- set current-frame input mask
- run until next VBlank boundary
- read named memory domains / controller state
- dump final framebuffer
- optionally set temporary breakpoints for per-routine capture

**UNCLEAR:** whether direct SameBoy P/Invoke is pleasant enough from C# or whether a small C wrapper is cleaner. Assume a wrapper unless proven otherwise.

## 7. Test-data pipeline

### 7.1 Generate reference data from the exact repo target

Always build the canonical ROM from this repo first and record its hash in every fixture manifest. The repo already documents the retail Gold SHA1 and treats `make compare` as ground truth. (docs/recon/build-system.md:122-145,225-248)

Suggested manifest fields:

```json
{
  "game": "pokegold",
  "romSha1": "d8b8a3600a465308c9953dfa04f0081c05bdcb94",
  "symSha1": "...",
  "sourceCommit": "...",
  "referenceEmulator": "SameBoy",
  "referenceVersion": "...",
  "scenario": "boot-to-title"
}
```

### 7.2 Suggested fixture layout

```text
tests/fixtures/verification/<scenario>/
  manifest.json
  input.bin
  frames.csv
  screenshots/
  saves/
  checkpoints/
```

Recommended contents:

- `manifest.json` - ROM hash, symbol hash, source commit, emulator version, scenario metadata
- `input.bin` - 1 byte per frame
- `frames.csv` - frame index + domain hashes
- `screenshots/*.png` - only milestone or diff-friendly frames
- `saves/*.sav` + RTC blob - for save compatibility scenarios
- `checkpoints/*.bin.zst` - sparse full dumps for hard bugs; do not commit every frame dump

### 7.3 Capturing routine fixtures

Use the reference emulator with symbol-aware breakpoints:

1. run to routine entry
2. dump relevant registers/memory slice
3. single-step or run to routine exit
4. dump after-state
5. convert into a small unit-test fixture

The existing build emits `.sym`/`.map`; use that instead of scattering magic addresses through tests. (docs/recon/build-system.md:83-99)

### 7.4 Keeping fixtures in sync

Every fixture must pin:

- ROM SHA1
- symbol-file SHA1
- source commit or tag
- reference emulator + version
- generator script version

Fail fast if any of those drift. Regenerating fixtures against a different disassembly build without noticing is the runtime equivalent of comparing against the wrong `roms.sha1` file.

## 8. CI / CD integration

The repo currently has hash-based build verification but no dedicated gameplay/unit-test harness. Add runtime verification in layers rather than trying to ship one giant suite immediately. (docs/recon/build-system.md:225-248)

### Run on every commit / PR

- routine fixtures for RNG, EXP, checksum, one AI case
- short replays with frame hashes:
  - boot -> title
  - one overworld movement/menu scenario
  - one deterministic battle scenario
- a few framebuffer checkpoints
- save-load smoke test once save exists
- a handful of crafted glitch fixtures that do not require long playthroughs

### Run nightly

- longer replay sets through overworld/breeding/battle/credits
- full glitch catalog
- save cross-load matrix
- RTC rollover scenarios
- optional slower lockstep modes with sparse full byte dumps

### Run before release / major milestones

- widest replay corpus
- bidirectional save compatibility sweep
- golden screenshots for milestone scenes
- manual investigation of any remaining BGB/SameBoy disagreement cases

### Failure diagnostics must include

- first divergent frame/checkpoint
- which domain hash diverged
- first differing address/offset and nearby bytes
- symbolized name if available
- current input mask and recent input history
- current bank/timing state
- screenshot diff if video diverged
- save diff summary if persistence diverged

## 9. Milestone-specific verification

### Boot to title screen

Minimum acceptable stack:

- deterministic power-on replay to title
- per-frame `logicHash` + `videoHash`
- title-screen framebuffer compare
- smoke routine tests for RNG and joypad mirroring

Why this is enough: boot/title exercises init, memory clears, bank setup, OAM DMA setup, title rendering, the VBlank scheduler, joypad sampling, and sound tick order without needing save/battle complexity. (docs/recon/execution-flow.md:97-116,145-176; docs/recon/hazards.md:18-32,72-99)

### Complete a battle

Add:

- deterministic battle replay with hashes at least every frame, ideally every turn boundary plus frame hashes
- routine fixtures for damage/EXP/AI/packed PP
- battle scene framebuffer checkpoints
- audio-control-state comparison during battle intro and one turn

Why: battle amplifies stale carry, mixed-endian stats, AI bugs, and packed-byte semantics. (docs/recon/hazards.md:42-68,171-181; docs/recon/data-formats.md:53-96)

### Credits roll

Add:

- long replay covering Hall of Fame -> credits -> return flow
- save compatibility before and after the run
- full nightly glitch suite except any still waiting on unimplemented subsystems
- sparse full-dump checkpoints on major transitions

Why: this path touches long-run scheduling, special VBlank modes, save/Hall of Fame behavior, and long scenario stability. (docs/recon/execution-flow.md:137-140,173-177; docs/recon/glitches.md:363-389)

## Bottom line

Treat verification as **runtime `make compare`**:

- SameBoy-backed replays are the top-level oracle
- per-frame domain hashes are the day-to-day CI workhorse
- per-routine fixtures catch arithmetic/flag/layout bugs quickly
- framebuffer diff proves visible rendering
- save compatibility proves SRAM/RTC correctness
- every documented glitch becomes a named regression test

If a future implementation choice makes testing easier by hiding bytes, timing, or glitches, that choice is probably wrong for this project. The existing conventions already prefer explicit bytes, bank state, and scheduling behavior over simplification. (docs/conventions/memory-model.md:425-438; docs/conventions/translation-patterns.md:1337-1345)