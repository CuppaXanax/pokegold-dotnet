# Phase 1 Reconnaissance Summary

This document synthesizes eight parallel research reports into a coherent picture of the `pret/pokegold` disassembly. Read this first, then dive into individual reports for details.

## What this repository is

This is a **byte-accurate disassembly** of Pokémon Gold Version (UE/international English, game ID `AAUE`) for Game Boy Color. It also builds Pokémon Silver (`AAXE`), debug variants, and Virtual Console patches from the same source. The build produces a 2 MiB ROM matching SHA1 `d8b8a3600a465308c9953dfa04f0081c05bdcb94` for Gold. The cartridge uses **MBC3+TIMER+RAM+BATTERY** (real-time clock + 32KB SRAM + battery backup).

**UNCLEAR:** The repo does not explicitly label itself as targeting v1.0 vs v1.1 of the retail ROM. The SHA1 hash is the ground truth for which exact revision this matches.

## Codebase at a glance

| Metric | Value |
|--------|-------|
| Total ASM files | ~450+ across home/, engine/, data/, audio/, maps/, ram/ |
| ROM banks | 128 ($00-$7F), 16KB each = 2 MiB |
| WRAM | ~6KB across WRAM0 ($C000-$CFFF) + WRAM1 ($D000-$DFFF) |
| HRAM | ~127 bytes ($FF80-$FFFE), includes OAM DMA stub |
| SRAM | 4 banks × 8KB = 32KB (save data, PC boxes, backup saves) |
| Build toolchain | RGBDS 1.0.1 + 7 custom C tools |
| Script DSLs | 7 bytecode languages (events, text, movement, audio, battle commands, battle anims, OAM anims) |

## Key architectural findings

### 1. The game is built around VBlank as the global heartbeat
The main game loop runs logic, then halts until VBlank. VBlank runs a **strict priority pipeline**: BG map buffer → palette upload → BG map redraw → tile requests → OAM DMA → joypad poll → sound engine tick. Only ONE of the high-priority VRAM jobs runs per frame. Different game modes (overworld, cutscene, serial, credits) swap the VBlank handler via `hVBlank`. *(execution-flow.md §6)*

### 2. Seven bytecode interpreters, not just one
The game has seven distinct script DSLs, each with its own opcode table and interpreter:
- **Event scripts** (162 opcodes, `ScriptCommandTable`) — map interactions, NPC dialogue, game flow
- **Text scripts** (TX_* commands + inline tokens) — text rendering and formatting
- **Movement scripts** (direction-encoded opcodes) — NPC/player movement sequences
- **Audio scripts** (note bytes + $D0-$FF commands) — music and SFX
- **Battle effect scripts** ($01-$AF commands) — move effect sequences
- **Battle animation scripts** (frame waits + $D0-$FF commands) — visual effects
- **OAM animation scripts** (frame data + sentinel commands) — sprite animations

These are the heart of the game's content. Translating them faithfully is a major workstream. *(decomp-conventions.md §7)*

### 3. Memory layout is the program's skeleton
The ~2800-line `wram.asm` defines the entire game state. Key regions:
- **Audio RAM** ($C000-$C198): 8 channel structs + mixer state
- **Battle overlay** ($CAA0-$CBD6): move structs, battler state, AI fields
- **Party data** ($D986-$DAB0): 6 Pokémon × 48 bytes + OT/nickname arrays
- **Map/overworld state** ($D1FD-$D952): objects, connections, tileset data, player position
- **HRAM** ($FF80-$FFFE): DMA stub, joypad mirrors, RNG state, scroll registers, math scratch

All data is stored in specific byte layouts with specific endianness (some big-endian, some little-endian, some mixed). Party Pokémon use big-endian stats but little-endian OT IDs. *(memory-map.md, data-formats.md §3)*

### 4. Bank switching is pervasive but well-patterned
The game uses `rst $10` (Bankswitch) and `rst $08` (FarCall) as the standard bank-switching mechanisms. `farcall`/`callfar` macros save the current bank, switch to the target, call, and restore. Some hot paths (audio, text, battle) bypass the macro and switch banks inline for performance. The `predef` system provides indexed dispatch to banked routines. *(execution-flow.md §11-12)*

### 5. Data formats are fixed-width and byte-exact
All major data structures have documented fixed widths:
- Base stats: 32 bytes/species
- Move data: 7 bytes/move  
- Box Pokémon: 32 bytes, Party Pokémon: 48 bytes (extends box with live stats)
- Map headers: 9 bytes + 12 bytes/connection
- Trainer parties: variable, 4 format variants (normal/moves/item/item+moves)
- Wild encounters: 47 bytes/grass table, 9 bytes/water table

Endianness is mixed and must be preserved exactly. *(data-formats.md §1-8)*

## Critical translation hazards (ranked by difficulty)

These are the things that will be **hardest** to get right in C#:

### CRITICAL
1. **VRAM/LCD timing and scanline effects** — The game polls `rLY`/`rSTAT` and does per-scanline register writes for raster effects (title screen parallax, battle transitions). A "draw once per frame" renderer won't match. *(hazards.md §4.1-4.2)*
2. **OAM DMA trampoline** — A 10-byte routine copied to HRAM that runs while normal memory is inaccessible. Must be modeled, not just replaced with a memcpy. *(hazards.md §1)*
3. **RNG depends on CPU flags and hardware divider** — `Random` uses `adc`/`sbc` with the **incoming carry flag** from the caller/interrupted code, plus `rDIV` from hardware. Changing this changes every random outcome. *(hazards.md §3.1)*

### HARD
4. **Flag-register-dependent control flow** — Many routines use carry/zero from non-compare instructions (time math with `ccf`, carry-return APIs as scheduling protocol). *(hazards.md §3.2-3.3)*
5. **RTC/MBC3 state machine** — Latch semantics are edge-triggered, day overflow wraps at 140, HALT/CARRY bits require explicit handling. Not a simple DateTime. *(hazards.md §7)*
6. **Serial/link/printer timing** — Interrupt-driven protocol with VC-specific timing patches. Needs protocol simulation, not literal port. *(hazards.md §serial note)*

### MEDIUM
7. **BCD arithmetic** (`daa` instruction) — No C# equivalent; must be manually reimplemented. *(hazards.md §10)*
8. **Stack tricks** — `sp` repurposed as bulk-copy pointer; synthetic calls via `push`+`jp hl`. *(hazards.md §9)*
9. **Multi-byte carry-chain arithmetic** — Multiply/divide/experience math chains `adc`/`sbc` across multiple bytes. *(hazards.md §11)*

## Glitch preservation requirements

15 known glitches documented, 6 rated CRITICAL for preservation:

| Glitch | Root cause summary | Key subsystems |
|--------|-------------------|----------------|
| Celebi Egg | Hidden species in party struct survives hatch | Egg creation, party struct model |
| Coin Case ACE | `done` vs `text_end` text terminator mismatch | Text engine stack behavior |
| Bad Clone | Interruptible box-save ordering, unchecked SRAM mirror | Save system, checksum boundaries |
| Trainer AI exploits | Score-based move choice with documented bug branches | AI scoring system |
| Save corruption | Checksum doesn't cover active box; Hall of Fame save bug | Save/SRAM architecture |
| RNG manipulation | `rDIV`-driven RNG with VBlank advancement | RNG implementation, VBlank timing |

Full details with file:line citations in glitches.md. These become our regression test catalog.

## Pret disassembly conventions to respect

- **Naming**: `w`/`h`/`s`/`v` prefixes for WRAM/HRAM/SRAM/VRAM; PascalCase for code labels; ALL_CAPS for constants; `_F` suffix for bit indices; `_MASK` for bitmasks
- **Structure**: `::` for public/exported labels, `:` for file-local; `const_def`/`const` for auto-incrementing enums; `rsreset`/`rb`/`rw` for struct offsets
- **Annotations**: `; BUG:` for known defects, `; unreferenced` for dead code, `; unused` for reserved slots, `; LEGACY:` for compatibility shims
- **Conditional compilation**: `_GOLD`/`_SILVER` for version splits (mostly data/assets), `_DEBUG` for debug room, `_GOLD_VC`/`_SILVER_VC` for Virtual Console patches

*(decomp-conventions.md §1-8)*

## Cross-report contradictions and gaps

### No contradictions found
All eight reports are consistent with each other. The memory-map report and data-formats report agree on struct sizes and layouts. The execution-flow report and hazards report agree on VBlank ordering and timing constraints.

### Gaps to address in Phase 2

1. **Audio engine internals** — The source-map cataloged audio files and decomp-conventions documented the audio DSL, but no report deeply traced the audio engine's per-frame tick behavior or channel mixing logic. Phase 2 synthesis should treat audio as its own translation challenge.

2. **Gold vs Silver delta exhaustiveness** — Decomp-conventions documented representative Gold/Silver differences but noted the list is not exhaustive. For the C# reimplementation, we need to decide: Gold-only, or dual Gold+Silver?

3. **WRAM bank switching** — Hazards report found no runtime `rSVBK` writes but couldn't prove global absence. If WRAM bank switching is truly unused (default bank 1 always), this simplifies the memory model significantly.

4. **GBC speed switching** — Similarly, no evidence of double-speed mode usage was found, but absence wasn't proven exhaustively.

5. **Version identification** — The repo doesn't explicitly label itself as v1.0 vs v1.1. The SHA1 hash is definitive, but a human-readable label would help future contributors.

6. **Save format completeness** — Data-formats documented the save structure, but the exact byte-for-byte save file layout (which fields go into which SRAM backup fragments) needs more detail for verification testing.

## What this means for the C# reimplementation

The codebase is **large but well-structured**. The pret team has done excellent work organizing it into subsystems with clear naming and consistent patterns. Key implications:

1. **The 7 bytecode interpreters are the biggest translation workload** — Each needs a faithful C# interpreter that preserves opcode-level behavior.

2. **Memory must be modeled as bytes, not objects** — Too many glitches and behaviors depend on raw byte layout, overlapping memory regions, and specific endianness. A `byte[]` backing store with typed accessors is likely the right approach.

3. **The VBlank pipeline defines frame timing** — The C# game loop must reproduce the exact VBlank priority ordering, not just "update then render."

4. **Bank switching can likely be flattened** — Since we're not constrained to 16KB windows, the C# version can address all code/data directly. But `farcall` semantics (save/restore bank) must still be respected where carry flags or return values cross bank boundaries.

5. **Data tables can be loaded from the original ROM or from structured C# equivalents** — But every byte must match, so automated extraction + validation against the ROM is the safest path.

---

*Generated from Phase 1 reconnaissance. Individual reports:*
- *[build-system.md](build-system.md) — Build pipeline, toolchain, ROM targets*
- *[source-map.md](source-map.md) — File-by-file codebase map*
- *[memory-map.md](memory-map.md) — Full RAM/VRAM/SRAM/HRAM annotation*
- *[data-formats.md](data-formats.md) — Byte-exact data structure layouts*
- *[execution-flow.md](execution-flow.md) — Boot sequence through main loop*
- *[hazards.md](hazards.md) — Translation-critical hardware dependencies*
- *[glitches.md](glitches.md) — Glitch preservation catalog*
- *[decomp-conventions.md](decomp-conventions.md) — Pret naming/macro/organization conventions*
