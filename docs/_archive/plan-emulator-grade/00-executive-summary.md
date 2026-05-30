# Executive Summary: Pokémon Gold C# Reimplementation Plan

## What this document is

This is the top-level planning document for a byte-accurate C# reimplementation of Pokémon Gold (Game Boy Color) using Sokol.NET as the initial platform backend. It synthesizes eight parallel reconnaissance reports and six sequential design documents produced from the `pret/pokegold` disassembly. Read this first; drill into individual documents for detail.

## The codebase in one paragraph

Pokémon Gold is a 2 MiB ROM across 128 banks, built with RGBDS 1.0.1, targeting MBC3+TIMER+RAM+BATTERY hardware. The game logic lives in ~450 ASM files organized into `home/` (always-banked utilities), `engine/` (subsystem logic), `data/` (tables), `audio/` (music/SFX), and `maps/` (world data). It runs seven distinct bytecode interpreters (events, text, movement, audio, battle commands, battle animations, OAM animations), uses VBlank as its global heartbeat and scheduler, and stores all state in ~6KB of WRAM with specific byte layouts and mixed endianness. Fifteen documented glitches depend on exact memory layout, save ordering, RNG behavior, and text engine stack semantics — all must be preserved.

## Architecture in brief

- **Memory**: Separate physical backing stores per hardware region (ROM, VRAM, SRAM, WRAM, HRAM, OAM) with a bus facade for address decoding. Typed `Span<byte>`-backed views provide named accessors. No detached object graphs — bytes are the source of truth. *(docs/conventions/memory-model.md)*
- **Translation**: A shared `CpuMath` helper layer handles 8-bit flag arithmetic, BCD, carry chains, and rotates. Bank-aware dispatch (`FarCall`, `Predef`) is preserved even though ROM is flat in host memory. Bytecode interpreters are faithfully translated as byte-stream interpreters. *(docs/conventions/translation-patterns.md)*
- **Platform boundary**: The game core submits a final 160×144 RGB555 framebuffer and PCM audio samples. The platform provides raw button state, battery persistence, a clock, and an optional serial endpoint. The game owns the frame loop — the platform is a servant, not a framework. *(docs/conventions/platform-interface.md)*
- **Verification**: Primary oracle is SameBoy-backed lockstep comparison at VBlank boundaries, with per-domain hashes (logic, video, timing, audio, save), routine-level fixtures, screenshot diffs, save compatibility tests, and a 15-glitch regression catalog. *(docs/conventions/verification.md)*
- **Structure**: 9-project .NET solution: Core (translated game), Platform (interfaces), Platform.Sokol (backend), Hosting (frame pacing/ROM discovery), App (thin exe), Verification + SameBoy (harness), Tests.Unit + Tests.Integration. *(docs/plan/repo-structure.md)*
- **Milestones**: 18 ordered milestones from scaffolding to credits roll, with parallelization bands after the core architecture stabilizes. *(docs/plan/milestones.md)*

## The 5 most important decisions for your review

### 1. This project will build emulator-grade hardware components, not just translate game logic

**The honest truth:** A byte-accurate reimplementation of Pokémon Gold requires faithfully modeling several Game Boy hardware subsystems: the PPU (pixel pipeline with scanline timing, STAT interrupts, VRAM access windows), the APU (4-channel audio synthesis), the timer/DIV system (drives RNG), the interrupt controller (IF/IE/IME, priority, delayed `ei`), MBC3 banking + RTC, and OAM DMA. These live inside `Pokegold.Core`, not in the platform layer.

The plan already accounts for this (core owns PPU composition and APU synthesis), but it should be stated plainly: the "core" is a selective Game Boy emulator for the hardware-shaped subsystems, plus a hand-translated port for the game logic that runs on top.

**Decision needed:** Are you comfortable with this scope? The alternative is to weaken "byte-accurate" to "behavior-equivalent at gameplay checkpoints" — which is significantly easier but loses glitch preservation and frame-perfect verification.

### 2. Single flat flag model vs per-routine flag handling

The translation patterns document proposes a shared `CpuMath` layer with a tiny flag model (Zero, Carry, HalfCarry, Subtract). This is necessary for correctness — the RNG consumes stale carry, time math uses `ccf`, many routines return status via carry, and `daa` needs the full flag set.

**Decision needed:** Should every translated routine use this shared flag model (consistent but verbose), or should translators choose per-routine whether to hand-port flags vs use the model (flexible but risks inconsistency)? The rubber-duck critique recommends a strict **translation ABI** where each routine declares its flag/register contract.

### 3. Separate memory arrays + bus vs single flat array

The memory model uses separate physical arrays per hardware region (ROM, VRAM, SRAM, WRAM0, WRAMX, HRAM, OAM) plus a bus facade. This correctly models bank switching and timing-sensitive access. The alternative (one `byte[65536]` plus bank shadows) is simpler but loses physical bank identity.

**Decision needed:** The separate-arrays approach is recommended and the plan proceeds with it. Flag if you'd prefer the flat approach — it changes the memory model significantly.

### 4. Verification granularity: frame-level vs sub-frame

The verification strategy uses VBlank/frame boundaries as the primary comparison point. A rubber-duck review flagged that frame-level hashing catches *that* divergence happened but not *why*. For PPU/STAT/RNG/interrupt bugs, divergence may happen hundreds of operations before the frame boundary.

**Decision needed:** Should we invest early in sub-frame trace tooling (scanline traces, routine entry/exit captures, rDIV/interrupt logs) as foundational infrastructure? Or treat it as escalation-only tooling built when needed? The recommendation is to promote it to foundational — it will save significant debugging time.

### 5. Scope: single-player credits-path Gold, or broader?

The milestone plan targets a complete single-player Gold playthrough to Hall of Fame and credits. Link/trading/printer/Mystery Gift are explicitly deferred. Silver support is deferred (same codebase, mostly data differences).

**Decision needed:** Is "single-player credits-path Gold with glitch preservation" the right v1 scope? If link/serial parity is required for v1, it should be promoted now — serial timing is one of the hardest translation challenges.

## Open questions needing your input

1. **Gold v1.0 vs v1.1?** The repo doesn't explicitly label the revision. The SHA1 hash is definitive, but should we investigate which retail version this matches?

2. **Nintendo logo at boot?** The disassembly starts after the boot ROM. M06 is a product decision: skip it, show a custom splash, or emulate the boot ROM sequence?

3. **Data extraction strategy:** Should data tables (Pokémon stats, moves, maps, trainers, items, text) be hand-translated to C#, auto-extracted from the built ROM, or source-generated from the ASM? Automated extraction + hash validation is recommended for byte accuracy.

4. **Legal/asset policy:** The ROM is not committed (copyright). Should generated test fixtures avoid embedding large copyrighted data chunks? What's the policy for committed vs locally-generated assets?

5. **Performance budget:** NativeAOT + Sokol.NET is the target. Is there a minimum FPS or maximum memory budget? Does the PPU need to be fast enough for real-time, or is correctness-first acceptable with optimization later?

## What surprised us during planning

**Good surprises:**
- No self-modifying code found. No undocumented opcodes. No WRAM bank switching at runtime. These remove three major hazard classes.
- The pret team's organization is excellent — consistent naming, clear subsystem boundaries, well-annotated constants. The C# can mirror their structure closely.
- Only one HRAM executable trampoline (OAM DMA). No other code-in-RAM tricks.

**Bad surprises:**
- The RNG depends on stale CPU carry flags from the caller or interrupted instruction. This is the single hardest translation challenge — it couples the RNG to execution context in a way that has no clean C# equivalent.
- Seven separate bytecode interpreters, not one. Each needs faithful translation with opcode-level behavior preservation.
- Mixed endianness within a single struct (party Pokémon: big-endian stats, little-endian OT ID). Every multi-byte field access must be endian-aware.
- Several milestones initially rated "L" are likely XL once hardware modeling is factored in: CpuMath/flags, title screen (requires PPU), text engine (bytecode + glitch-sensitive), and audio engine (requires APU synthesis).

## Riskiest parts of the plan (honest assessment)

1. **RNG fidelity** — The stale-carry dependency means the RNG is coupled to instruction-level execution context. Getting this right for all callers across the entire codebase is the single highest-risk item. If this can't be solved cleanly, it may force a narrower compatibility target.

2. **PPU implementation** — The game uses per-scanline LCD effects (wLYOverrides, STAT interrupts). A correct PPU needs scanline-level timing, not just end-of-frame composition. This is emulator-grade work hiding inside "title screen" and "overworld" milestones.

3. **Scale of translation** — ~450 ASM files, 162 event script opcodes, hundreds of battle effect commands, thousands of map scripts. The individual translations are mostly straightforward, but the volume creates risk of subtle inconsistencies accumulating.

4. **Verification feedback loop** — If frame-level hashing is the only debugging tool, localizing divergence will be slow and frustrating. Sub-frame tooling investment pays for itself but adds to early milestone scope.

---

*Full documentation tree:*

| Document | Path | Size |
|----------|------|------|
| Phase 1 Summary | docs/recon/00-summary.md | 11KB |
| Build System | docs/recon/build-system.md | 22KB |
| Source Map | docs/recon/source-map.md | 348KB |
| Memory Map | docs/recon/memory-map.md | 193KB |
| Data Formats | docs/recon/data-formats.md | 35KB |
| Execution Flow | docs/recon/execution-flow.md | 28KB |
| Hazards | docs/recon/hazards.md | 14KB |
| Glitches | docs/recon/glitches.md | 24KB |
| Decomp Conventions | docs/recon/decomp-conventions.md | 33KB |
| Translation Patterns | docs/conventions/translation-patterns.md | 32KB |
| Memory Model | docs/conventions/memory-model.md | 24KB |
| Platform Interface | docs/conventions/platform-interface.md | 18KB |
| Verification Strategy | docs/conventions/verification.md | 26KB |
| Milestones | docs/plan/milestones.md | 22KB |
| Repo Structure | docs/plan/repo-structure.md | 23KB |
| **This Document** | **docs/plan/00-executive-summary.md** | — |
