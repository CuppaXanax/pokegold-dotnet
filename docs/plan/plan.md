# Execution Plan — Pokémon Gold in F#

This is the actionable plan: ordered milestones, each with concrete deliverables, measurable
acceptance criteria, dependencies, and risks. The vision and principles live in `README.md`.

Target: **Pokémon Gold** (international English, game ID `AAUE`, SHA1
`d8b8a3600a465308c9953dfa04f0081c05bdcb94`) reimplemented in **F#** on **MonoGame** (DesktopGL).
Reference source is the `pret/pokegold` disassembly in this repo and the analysis in `docs/recon/`.

---

## Decisions to resolve before/early (gate)

These shape multiple milestones. Each has a recommendation; confirm or override.

| # | Decision | Options | Recommendation |
|---|----------|---------|----------------|
| D1 | **Asset/data source** *(RESOLVED)* | The repo is complete source: 1,825 gfx files, 286 maps, all tables in `.asm`. No ROM is present or needed (`roms.sha1` is only a build-validation checksum). | **Parse the repo assets directly** — `.png`/`.2bpp`/`.pal` for graphics, `.blk` for maps, `.asm` `db`/`dw` rows for numeric tables. **No ROM build, no ROM input from players.** |
| D2 | **Versions** | Gold only / Gold + Silver | **Gold only** initially (Silver is mostly data deltas; revisit later) |
| D3 | **Frame timing** | 60.0 fps / 59.7275 fps (GB rate) | **59.7275** via fixed `TargetElapsedTime`; logic ticks once per frame |
| D4 | **Presentation** | integer scale / stretch | **Integer scale**, default 4× (640×576), nearest-neighbor, letterbox the rest |
| D5 | **Content pipeline** | MGCB content pipeline / load raw assets ourselves | **Raw**: we synthesize `Texture2D` from decoded 2bpp + palettes; skip MGCB entirely |
| D6 | **Save format** | GB SRAM-compatible / our own | **Our own** (versioned), no SRAM compatibility requirement |

> All decisions have working answers; D1 is resolved (parse repo assets directly — no ROM).
> Revisit D2–D6 only if something forces it.

## Asset & legal posture

This repository is a complete reverse-engineered **source** representation of the game: graphics
as `.png`/`.2bpp`, palettes as `.pal`, maps as `.blk`, and all data tables/text/scripts as `.asm`.
There is **no ROM in the repo, and none is required** to build or run this port — `roms.sha1` is
purely a checksum to verify that an RGBDS build of the disassembly reproduces the retail ROM
byte-for-byte. The F# port loads these source assets directly. **Players do not supply a ROM**,
exactly as with the SM64 decomp PC ports.

---

## Project layout (created in M0)

```
PokeGold.sln
src/
  PokeGold.Game/      F# library — data, scripts (DUs), systems, state. No MonoGame dependency.
  PokeGold.Host/      F# executable — MonoGame DesktopGL shell: window, loop, input, audio, present.
tools/
  PokeGold.DataGen/   F# tool — turns pret assets/ROM into PokeGold.Game data + resources (per D1).
tests/
  PokeGold.Tests/     F# tests — data spot-checks and interpreter unit tests.
```

Boundary rule: `PokeGold.Game` is platform-agnostic and produces a 160×144 indexed/RGBA framebuffer
and audio sample buffers each tick. `PokeGold.Host` owns all MonoGame types.

---

## Milestones

Bottom-up: something on screen early; each milestone exercises the next. Sizing is rough
(S/M/L/XL).

### M0 — Solution scaffold & shell  · S
- **Deliverables:** `PokeGold.sln`; `PokeGold.Game` (F# lib, empty); `PokeGold.Host` (F# exe)
  referencing `MonoGame.Framework.DesktopGL`; a `Game` subclass that creates a 160×144
  `RenderTarget2D`, clears it each frame, and presents it scaled per D4 with point sampling under a
  fixed timestep per D3.
- **Acceptance:** `dotnet run --project src/PokeGold.Host` opens a 640×576 window showing a solid
  cleared 160×144 buffer scaled with nearest-neighbor; stable fixed-step loop; clean exit. Builds
  and runs on Windows.
- **Depends on:** —
- **Risks:** F#+MonoGame project friction (templates are C#-first). *Mitigation:* reference the
  DesktopGL NuGet package directly from an F# exe; write the `Game` subclass in F#.

### M1 — Framebuffer & GB graphics primitives  · M
- **Deliverables (in `PokeGold.Game`):** `Framebuffer` (160×144 of palette indices → RGBA32);
  `Tile` decoder (16-byte 2bpp → 8×8 of 2-bit indices); `Palette` (4 × GBC RGB555 → RGBA); a tile
  blitter that draws an 8×8 tile at (x,y) with a palette into the framebuffer.
- **Acceptance:** unit test decodes a known 16-byte tile to the expected 8×8 index grid; a demo
  in the Host draws a hand-made tilesheet with a chosen palette and it appears correct on screen.
- **Depends on:** M0.
- **Risks:** RGB555→RGBA color conversion fidelity (GBC color curve). *Mitigation:* start with a
  straight 5→8-bit scale; refine later if colors look off.

### M2 — Asset/data pipeline, first slice  · L
- **Deliverables:** per D1/D5, `PokeGold.DataGen` (or a direct loader) that produces a loadable
  first slice: one tileset (`.2bpp` + `.pal`), one map's blockset + `.blk` + dimensions +
  collision/attributes, surfaced as `PokeGold.Game` values.
- **Acceptance:** the slice loads at runtime into typed F# values; counts/sizes spot-checked
  against the source files (e.g., tile count, map width×height, block count) in a test.
- **Depends on:** M1; decision D1.
- **Risks:** Gen-2 map model has several layers (tiles → blocks/metatiles → map). *Mitigation:*
  model exactly the layers the slice needs; document the block format (32×32px blocks of 8×8 tiles).

### M3 — Overworld map render  · M
- **Deliverables:** block/metatile model; map = grid of block IDs expanded to tiles; a camera; a
  renderer that composes the visible region into the framebuffer.
- **Acceptance:** a real map (e.g. the player's bedroom or New Bark Town) renders correctly,
  matching a screenshot from the original to tile accuracy.
- **Depends on:** M2.
- **Risks:** off-by-one in block expansion / camera edges. *Mitigation:* golden-image compare in a
  test against a captured reference frame.

### M4 — Player movement & collision  · M
- **Deliverables:** input mapping (`PokeGold.Host` keyboard → GB button set consumed by Game);
  grid-stepped player movement with walking animation; collision from block/tile attributes;
  camera follow.
- **Acceptance:** the player walks the map with smooth grid steps, correct facing/animation, and
  cannot pass solid tiles; camera tracks within map bounds.
- **Depends on:** M3.
- **Risks:** matching the original's grid-step cadence/feel. *Mitigation:* tune step duration in
  frames against video reference.

### M5 — Text engine  · L
- **Deliverables:** font tiles; text-box rendering; the **text script language** as a DU
  (`TX_*` commands + inline tokens) with an interpreter; typewriter output, line break, scroll,
  prompt/pause.
- **Acceptance:** triggering a real sign/NPC text from the slice map renders the correct string,
  honoring control codes (newline, scroll, wait-for-button).
- **Depends on:** M3 (render), M2 (text data).
- **Risks:** token/terminator subtleties (`done` vs `text_end`). *Mitigation:* enumerate the
  command set from the disassembly; unit-test the interpreter against known text streams.

### M6 — Battle vertical slice  · XL
- **Deliverables:** species base stats (32 B) and move data (7 B) loaded as records; battle state
  model; turn loop; Gen-2 damage formula; the **battle-effect command language** as a DU with an
  interpreter; minimal battle UI (HP bars, move menu); one scripted wild encounter.
- **Acceptance:** start a scripted wild battle, choose a move, deal damage matching the Gen-2
  formula for a fixed input (no-crit, fixed roll), and reach faint/win/run.
- **Depends on:** M1, M2, M5.
- **Risks:** damage-formula edge cases (stat stages, type effectiveness, crit). *Mitigation:*
  table-driven unit tests with worked examples from the disassembly.

### M7 — Save / load  · M
- **Deliverables:** versioned own-format save (per D6) round-tripping party, position, event
  flags, and bag.
- **Acceptance:** save, restart the process, load — game state is restored exactly.
- **Depends on:** M4 (state to save); grows as systems are added.
- **Risks:** schema churn as state grows. *Mitigation:* version the format from day one.

### M8 — Audio  · L
- **Deliverables:** high-level audio engine — channel model + the **audio script language**
  interpreter — feeding `DynamicSoundEffectInstance` in the Host; one BGM and a couple SFX.
- **Acceptance:** the slice map's BGM plays and loops; a menu/selection SFX triggers on input.
- **Depends on:** M2 (audio data); recon notes audio internals are under-documented — expect
  exploration.
- **Risks:** synthesizing the 4 GB channels (pulse/wave/noise) and tempo correctly.
  *Mitigation:* start with one pulse channel + a single track; expand.

### M9+ — Outward
More maps and warps, overworld events/NPCs (event script DU), menus (party/bag/Pokédex), wild
encounter tables, trainers. Each reuses the interpreters and renderer from M3–M8.

---

## Dependency graph

```
M0 → M1 → M2 → M3 → M4 ─┐
               │        ├→ M7
               └→ M5 ───┤
                        └→ M6
M2 → M8
```

## Risk register (top)

| Risk | Impact | Likelihood | Mitigation / owner-action |
|------|--------|------------|---------------------------|
| F#+MonoGame tooling friction | Blocks M0 | Med | Reference DesktopGL NuGet directly from F# exe; spike in M0 |
| Data-extraction strategy churn (D1) | Rework M2+ | Med | Decide D1 before M2; isolate behind a loader interface |
| Gen-2 map/block model complexity | Slips M2/M3 | Med | Model only what the slice needs; golden-image tests |
| Damage formula / battle edge cases | Subtle M6 bugs | High | Worked-example unit tests from disassembly |
| Audio synthesis fidelity | M8 sounds wrong | High | One channel/track first; iterate |

## Definition of done (project, v-slice)

A single contiguous slice is playable end to end: boot → walk one real map → read an NPC/sign
→ enter and finish one wild battle → save and reload. Data in that slice is spot-checked against
the disassembly; interpreters have unit tests; the slice renders to tile accuracy.

## Tracking

Milestones M0–M8 are mirrored as todos in the session database with dependencies. Update status
there as work proceeds.
