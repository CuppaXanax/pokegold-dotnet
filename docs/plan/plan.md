# Execution Plan — Pokémon Gold in F#

This is the actionable plan: ordered milestones, each with concrete deliverables, measurable
acceptance criteria, dependencies, and risks. The vision and principles live in `README.md`.

Target: **Pokémon Gold** (international English, game ID `AAUE`, SHA1
`d8b8a3600a465308c9953dfa04f0081c05bdcb94`) reimplemented in **F#** on **MonoGame** (DesktopGL).
Reference source is the `pret/pokegold` disassembly in this repo and the analysis in `docs/recon/`.

**North star — 100%-able Gold.** The done state is the *complete game*, not a tech demo:
a fresh save can be played start to finish and **fully completed** — all 16 badges (Johto +
Kanto), the Elite Four, Red, Hall of Fame + credits, and a **completable National Pokédex**
(all 251 obtainable within the implementation, including trade evolutions and event-only
species — see D7–D9). Glitchless completion is the bar; glitch-category parity is a separate
non-goal unless explicitly chosen.

The milestones below build **bottom-up**: M0–M8 prove every subsystem once via a thin vertical
slice (the fastest path to "it's a game"), then M9–M22 take each subsystem to **full breadth**
until the north star is met. Slice first so we fail fast; then go wide.

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
| D7 | **Link / trading** | real netcode / offline "trade terminal" / none | **Offline trade terminal** — a local mechanism that performs trades (and thus trade evolutions) without networking; real link is a non-goal. Needed so the Pokédex is completable single-player. |
| D8 | **Pokédex-complete bar** | strict (all 251 self-obtainable) / pragmatic | **Pragmatic**: every species obtainable in-impl via normal capture, evolution (incl. trade-evo through D7), in-game trades, gifts, and event hooks (D9). Version exclusives sourced via the offline trade terminal. |
| D9 | **Event-only species** (Celebi/GS Ball, roamers, Red Gyarados, Lugia/Ho-Oh, Suicune) | emulate Mobile/event / built-in unlocks | **Built-in unlocks** — implement the in-game triggers directly (no Mobile Adapter); GS Ball/Celebi event provided as a standard scripted event so the dex is completable. |

> All decisions have working answers; D1 is resolved (parse repo assets directly — no ROM).
> Revisit D2, D4–D9 only if something forces it.

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

#### M4 addendum — locomotion taxonomy & ledge hops

The base M4 above covers *walk + face + block-on-solid*. The disassembly's player
movement, however, is a small state machine over the collision permission table: the
high nybble of a tile's `COLL_*` id selects a **movement behavior**, not just
"passable / solid." Captured here so later milestones don't rediscover it. Source of
truth: `engine/overworld/player_movement.asm` (`DoPlayerMovement`), the permission
scan `GetMovementPermissions` (`home/map.asm`), `constants/collision_constants.asm`,
and `data/collision/collision_permissions.asm`.

**Player states** (`wPlayerState`): `NORMAL`, `BIKE`, `SKATE` (ice), `SURF`,
`SURF_PIKA`. **Step types** (animation + distance): `STEP_SLOW`, `STEP_WALK`,
`STEP_BIKE` (fast), `STEP_LEDGE` (2-tile hop), `STEP_ICE` (slide), `STEP_TURN`,
`STEP_BACK_LEDGE`, `STEP_WALK_IN_PLACE`.

**Permission-tile behaviors** (high nybble groups):
- **Plain:** `LAND_TILE`/`WATER_TILE`/`WALL_TILE` — walk, surf, block *(have)*.
- **Ledges** (`$a0-$a7`, `HI_NYBBLE_LEDGES`): `HOP_DOWN/LEFT/RIGHT` (+ diagonals;
  `UP` variants unused) — one-way hop, allowed only if facing matches a per-ledge
  mask; plays `SFX_JUMP_OVER_LEDGE`, runs `STEP_LEDGE` (2-tile arc + shadow).
- **Grass:** `TALL_GRASS` ($18), `LONG_GRASS` ($14) — walkable; hook for wild
  encounter check + rustle (encounter system itself is M6/M9).
- **Ice:** `ICE` ($23) — forced-continue in last direction (`STEP_ICE` slide).
- **Water features:** `WHIRLPOOL` ($24), `WATERFALL` ($33), `CURRENT_*` ($38-$3b) —
  force movement in a fixed direction (surf-state only).
- **Conveyors:** `WALK_*` ($41-$44, alt $50-$53) force-walk; `BRAKE_*` stop.
- **Directional one-way walls** (`$b0-$b7`) and **buoys** (`$c0-$c7`).
- **Entrances** gated to approach-from-above: `DOOR` ($71), `STAIRCASE` ($7a),
  `CAVE` ($7b); `LADDER` ($72) is visual-only walkable.
- **Warps:** directional `WARP_CARPET_*` ($70/$76/$78/$7e), `WARP_PANEL` ($7c),
  `PIT` ($60) — trigger a warp/teleport.
- **Talk-through walls** (`WALL_TILE | TALK`, `$90-$9f`): `COUNTER`, `PC`, `TV`,
  `BOOKSHELF`, `MART_SHELF`, `WINDOW`, … — solid but interactable.
- **Field-move gates:** `CUT_TREE` ($12), `HEADBUTT_TREE` ($15) — solid until the
  matching field move; strength boulders are objects, not tiles.

**In scope for M4 (this addendum) — ledge hops only — ✅ IMPLEMENTED:**
- **Deliverables:** detect ledge tiles by the `HI_NYBBLE_LEDGES` ($a0) group on the
  player's **current** cell (the tile being stood on, *not* the destination); a
  facing→allow mask; a `STEP_LEDGE` state that hops **two cells** in the faced
  direction with a parabolic arc, not re-validating the landing cell. Hop SFX
  deferred to M8 — silent hop is acceptable.
- **Acceptance:** standing on a south ledge and pressing Down hops two cells south
  and lands; pressing into that ledge from any other facing is blocked; left/right
  ledges behave symmetrically. (Covered by `MovementTests.fs` ledge tests.)

**Rubber-duck findings (verified against the disassembly + our code):**
- **The ledge is on the tile the player STANDS on, not the destination.**
  `wPlayerTileCollision` = collision of the player's current cell
  (`home/map.asm` `GetMovementPermissions`), and `.TryJump`
  (`player_movement.asm:354-391`) reads *that* tile. Dispatch order is `.TryStep`
  (normal walk) **then** `.TryJump`: the hop only fires once the forward step is
  blocked (the cell ahead of a ledge is a wall in real maps). **Implication:** in
  `Movement.step` the ledge check runs in the *else* branch, after the normal
  `cellWalkable` test fails — not before it.
- **Ledge tiles are `LAND_TILE` permission**, so the player walks *onto* the ledge
  tile normally (it looks like ordinary ground), then a second press in the hop
  direction vaults over the cliff below it. `.CheckHiNybble` only matches
  `SIDE_WALLS`/`SIDE_BUOYS`, so `.MovementPermissionsData` does **not** apply to
  ledges — standing on a ledge imposes no special directional permission; the hop
  is driven purely by (current tile is ledge) + (forward blocked) + (facing in mask).
- **Hops are always cardinal.** The ledge_table (`player_movement.asm:383-390`) maps a
  ledge id's low nybble (0-7) to a *facing mask*, and the hop runs in the player's
  walking direction (`jump_step DOWN/UP/LEFT/RIGHT`). Diagonal ledge ids ($a4-$a7)
  only permit **two approach facings** (e.g. `FACE_RIGHT | FACE_DOWN`); they never
  produce diagonal movement. So all 8 ids are handled by one mask table — no diagonal
  movement, nothing to defer. Mask table (low nybble → allowed facings):
  `0→R, 1→L, 2→U, 3→D, 4→{R,D}, 5→{D,L}, 6→{U,R}, 7→{U,L}`.
- **Data already supports this:** `Collision.BlockColl` keeps the raw `COLL_*` id per
  quadrant. Added pure accessors `Collision.collisionIdAt coll blockId qx qy` and
  `Collision.tryLedge : byte -> Direction list option`; no parser changes.
- **Movement reuse:** existing interpolation uses `(cellX - srcX)`, so setting
  `CellX = SrcX + 2·dx` covers a 2-cell hop for free; added a `Hopping` flag for a
  `sin(π·t)` vertical arc and a longer `HopFrames` (32 = 2× `StepFrames`) duration.
  Landing tile is **not** re-validated (mirrors the original). Shadow sprite deferred.
- **Testability:** `Player.create cx cy` already lets tests place the player on any
  cell. The ledge tests scan the real Azalea map for a ledge, place the player on it,
  and assert a 2-cell hop; plus pure `Collision` unit tests for ledge-id detection + mask.

**Out of scope (catalogued above, deferred):** surfing & water-force tiles, ice
slide, bike speed, conveyors/one-way walls/buoys, doors/stairs/warps (→ overworld
events / M9+), grass encounters (→ M6/M9), field-move gates, NPC collision.

Rubber-duck review: **passed** (findings folded in above).


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

### M7 — Save / load  · M  ✅ (scope-appropriate slice)
- **Deliverables:** versioned own-format save (per D6) round-tripping party, position, event
  flags, and bag.
- **Status:** **Done for current scope.** A versioned **JSON** save (`SaveData { Version;
  Overworld }`) round-trips the only player-owned state that exists today — map id + player
  cell + facing — through `%LocalAppData%/PokeGold/pokegold.sav`. `Save/SaveData.fs`
  (pure capture/apply) + `Save/SaveFile.fs` (System.Text.Json + IO, version-gated read).
  Host **F5 = save / F9 = load**. Party/bag/event-flag fields join `SaveData` as those
  systems land (M6+/M9/M11); the `Version` gate is the migration seam.
- **Acceptance:** save, restart the process, load — game state is restored exactly. ✓
- **Depends on:** M4 (state to save); grows as systems are added.
- **Risks:** schema churn as state grows. *Mitigation:* version the format from day one.

### M8 — Audio  · L  ✅ (full 4-channel synth)
- **Deliverables:** high-level audio engine — channel model + the **audio script language**
  interpreter — feeding `DynamicSoundEffectInstance` in the Host; one BGM and a couple SFX.
- **Acceptance:** the slice map's BGM plays and loops; a menu/selection SFX triggers on input.
- **Depends on:** M2 (audio data); recon notes audio internals are under-documented — expect
  exploration.
- **Risks:** synthesizing the 4 GB channels (pulse/wave/noise) and tempo correctly.
  *Mitigation:* start with one pulse channel + a single track; expand.
- **Done:** pure `Audio/` subsystem in PokeGold.Game (AudioData, SoundCommand, SongParser,
  Synth, AudioEngine) parses the GSC script from `.asm` at the macro level, sequences it on a
  60 Hz frame clock, and software-synths all four channels (2 pulse, wave, noise/LFSR) to float
  PCM. Host `HostAudio.fs` pumps a `DynamicSoundEffectInstance` (44100 Hz stereo). Azalea Town
  BGM loops on overworld load; `Sfx_Menu` fires on the A/Start edge. 7 new tests (78 total).

### M9+ — Full game (breadth to 100%)

M0–M8 prove every subsystem once on a thin slice. M9–M22 take each to full breadth until the
**north star (100%-able Gold)** is met. Sizing stays rough; these are coarser than M0–M8 and will
be split into sub-tasks as each is picked up. They cluster into four phases.

#### Phase A — Overworld at scale

##### M9 — Event & script engine  · XL
- **Deliverables:** the **overworld script command language** as a DU + interpreter (the `pret`
  `*_script` macros): NPC/sign text, triggers, item balls, give/take, `setflag`/`clearflag` event
  flags and variables, script-driven warps and movement, `applymovement` actor paths, yes/no &
  multi-choice prompts, money/item give, battle-from-script. Map event records (warps, coord
  events, bg events, object events) parsed into typed values.
- **Acceptance:** on a real map, talking to a scripted NPC runs its script (sets a flag, gives an
  item, branches on the flag next time); stepping on a trigger fires once; an item ball is
  collectable and doesn't respawn after save/reload.
- **Depends on:** M5 (text), M4 (movement). **Risks:** command-set breadth. *Mitigation:* enumerate
  the full command table from the disassembly; unit-test the interpreter against real script bytes.

---

#### M9 — Implementation Plan (verified against the disassembly)

**Ground truth (read directly from the repo, not memory):**
- The script VM lives in `engine/overworld/scripting.asm`. `ScriptCommandTable` (line 64) has
  **162 commands**, opcodes `$00–$a1`, one `dw Script_*` each. The `*_command` constants and arg
  layouts are in `macros/scripts/events.asm` (1016 lines).
- Real opcodes (verified, the explore agent's first guesses were wrong): `scall $00`, `sjump $03`,
  `ifequal $06`, `ifnotequal $07`, `iffalse $08`, `iftrue $09`, `ifgreater $0a`, `ifless $0b`,
  `special $0f`, `setval $15`, `readvar $1c`, `giveitem $1f` (item,qty), `takeitem $20`,
  `checkitem $21`, `checkevent $31`, `clearevent $32`, `setevent $33`, `checkflag $34`,
  `clearflag $35`, `setflag $36`, `warp $3c`, `opentext $47`, `closetext $49`, `writetext $4c`,
  `yesorno $4e`, `jumptextfaceplayer $51`, `jumptext $52`, `waitbutton $53`, `promptbutton $54`,
  `loadtrainer $5d`, `startbattle $5e`, `reloadmapafterbattle $5f`, `setlasttalked $67`,
  `applymovement $68`, `faceplayer $6a`, `disappear $6d`, `appear $6e`, `playmusic $7e`,
  `playsound $84`, `waitsfx $85`, `verbosegiveitem $9d`, `warpfacing $a1`, `end $90`.
- Map events (`maps/AzaleaTown.asm`, macros in `macros/scripts/maps.asm`): four blocks —
  `def_warp_events` (`warp_event x,y,destMap,destWarpId`), `def_coord_events`
  (`coord_event x,y,sceneId,script`), `def_bg_events` (`bg_event x,y,type,script`),
  `def_object_events` (`object_event x,y,SPRITE,MOVEDATA,radX,radY,timeStart,timeEnd,palette,
  OBJECTTYPE,sightRange,script,eventFlag`). `eventFlag = -1` ⇒ always present.
- Event flags: `constants/event_flags.asm` (`EVENT_*` indices into `wEventFlags` bitset);
  bit get/set/clear via `home/flag.asm::FlagAction` (line 32). Engine flags (`ENGINE_*`, badges)
  are a *separate* bitset via `setflag`/`checkflag`.
- Suspension points: `writetext`/`jumptext` suspend until the textbox closes; `yesorno` until a
  choice; `applymovement` until movement finishes; `startbattle` until the battle returns its
  result into `wScriptVar`. `scall`/`sjump` use a script call-stack; `end` pops or stops.
- Text seam already exists (M5): scripts hold a text pointer/label; `writetext` feeds the existing
  `TextStream`/`TextBox`. Integration replaces `OverworldScene`'s hardcoded `DemoText` A-press.

**Design — re-express, don't transcribe** (mirrors our Audio/Battle DU+interpreter pattern):
- **Parse `.asm`, not bytecode.** A `ScriptParser` reads a map's `.asm` (like `SongParser` reads
  audio `.asm`), producing `Map<Label, ScriptCommand list>` with jumps/calls referencing labels
  symbolically. "The source is the spec."
- **`ScriptCommand` DU** for the M9 slice (~35 commands, not all 162 — defer phone/trade/menu/
  decoration/elevator to later milestones). Unknown opcodes parse to `Unsupported of name` so a
  whole map still loads and we can see coverage gaps.
- **Resumable VM.** `Script.step` runs commands until it hits a *yield*, returning a
  `ScriptYield`: `ShowText label`, `AskYesNo`, `Move (objId, path)`, `StartBattle spec`,
  `Warp dest`, `PlaySound id`, or `Finished`. The VM state is `{ Pc; Stack; }`; the scene handles
  the yield (push textbox/battle, run movement) and calls `resume` with the result
  (`YesNo bool`, `BattleResult`, `Unit`). This keeps the interpreter pure and total.
- **`ScriptContext`** = the mutable world the VM reads/writes: `EventFlags` bitset, `EngineFlags`
  bitset, `wScriptVar`, bag/money (thin slices), and object visibility. Lives beside
  `OverworldState`.
- **Event flags persist** via M7 save: extend `SaveData` with the `EventFlags` (+ engine flags)
  bitset so a collected item ball / set event survives reload (the acceptance gate).

**Sub-milestones (each = build + tests + green before next):**
1. **M9.1 — Script DU + parser.** `Overworld/Script/ScriptCommand.fs` (the DU) +
   `ScriptParser.fs` (`.asm` label-block → `ScriptCommand list`, symbolic jumps, `Unsupported`
   fallback). Tests: parse `AzaleaTownGrampsScript` and Bugsy's script from the real map files;
   assert exact command sequences.
2. **M9.2 — Event/engine flag store + VM core.** `EventFlags.fs` (bitset get/set/clear keyed by
   `EVENT_*`/`ENGINE_*` names from the constants) + `Script.fs` (`step`/`resume`, call-stack,
   `iftrue/iffalse/ifequal`, `checkevent/setevent/clearevent`, `checkflag/setflag/clearflag`,
   `setval/readvar`, `scall/sjump/end`). Tests: flag round-trips; a branch that takes the
   `iftrue` path only after `setevent`; call/return nesting.
3. **M9.3 — Map event records + parser.** `MapEvents.fs` (warp/coord/bg/object DUs) +
   parse the four `def_*_events` blocks from a map `.asm`. Wire into `OverworldState` (objects →
   NPC sprites with positions; gate visibility on `eventFlag`). Tests: AzaleaTown event counts &
   first records match the file.
4. **M9.4 — Overworld integration.** `OverworldScene` runs the VM: **A** while facing an
   `object_event` runs its script (faceplayer→text→…); **stepping** onto a `coord_event` whose
   scene matches fires it once; `writetext`/`yesorno` push the existing TextBox/choice scene and
   resume on close; `verbosegiveitem`/`giveitem` add to the bag and set `wScriptVar`. Replaces the
   hardcoded `DemoText`. Manual + scripted-input tests.
5. **M9.5 — Warps + persistence.** `warp`/`warpfacing` switch maps (extend
   `OverworldState.loadAssets` map table); collected item balls / set events persist through
   `SaveData` (round-trip test = the **acceptance gate**).
6. **M9.6 — Coverage pass + commit.** Run a parser sweep over all bundled map `.asm` files;
   report `Unsupported` opcode frequency to size the next slice. Full `dotnet test` green; commit
   per sub-milestone (author Xander, Copilot trailer).

**Acceptance (unchanged, now testable):** talk to a scripted NPC → runs its script (sets a flag,
gives an item, branches on the flag next time); a coord trigger fires once; an item ball is
collectable and doesn't respawn after save/reload.

**Out of scope for M9 (explicit defers):** phone/cellnum, trade, `_2dmenu`/`verticalmenu`/
`loadmenu` complex menus, decorations, fruit trees, elevators, credits/hall-of-fame, `callasm`/
`memcall` raw-ASM commands → these `Unsupported`-fallback now, picked up by M11/M12/M17+.

##### M10 — World assembly & NPC objects  · L
- **Deliverables:** load **all 286 maps**; map connections/streaming and warp transitions between
  maps; per-map tileset/palette/music binding; overworld **object/NPC sprites** with movement
  patterns (`SPRITEMOVEDATA_*`), facing, animation, and player↔object collision; map-change music.
- **Acceptance:** walk a connected region (e.g. New Bark → Route 29 → Cherrygrove) crossing map
  borders with no seams; warps move between interior/exterior; NPCs wander and block the player and
  can be talked to (via M9).
- **Depends on:** M9, M3. **Risks:** connection/border math, object slot limits. *Mitigation:*
  golden-image border tests; model the real object-engine slot rules.

###### M10 addendum — build-time data generation (no runtime ASM)

**Directive (user):** static GBC `.text`/data should live baked in our binary, not parsed from `.asm`
at runtime. Follow the existing **`PokeGold.DataGen`** pattern that already bakes Species/Moves/Type
chart into `Data/Generated/*.Generated.fs`. M9 shipped the map/script/text/event parsers but wired
them at **runtime** (`MapEventParser.parseFile`/`ScriptParser.parseFile`/`MapText.parseFile` →
`Assets.readText`). M10 moves that parsing to **build time**: the parsers run in DataGen and emit F#
literals; the runtime loads generated values, never `.asm`.

Key constraints discovered (the planner must honour these):
- `PokeGold.Game` has a `ProjectReference` to `PokeGold.DataGen` and runs it via the
  `GenerateGameData` MSBuild target (`BeforeTargets="CoreCompile"`). So **DataGen cannot reference
  `PokeGold.Game` types** (would be circular). DataGen emits plain F# *source text*; that text is
  compiled *inside* `PokeGold.Game`, so it CAN construct Game DUs (`ScriptCommand`, `ScriptEffect`,
  `MapEvents`/`ObjectEvent`/`WarpEvent` records, etc.).
- Today the parse logic lives in `PokeGold.Game` (`Overworld/Script/*`). To run it at build time
  without duplicating, the pure parser logic likely needs to move to a place DataGen can use (e.g. a
  shared `PokeGold.Core`/`PokeGold.AsmParse` library both reference, or relocate the pure
  `parseText` functions into DataGen and have Game consume only generated values). The planner picks
  the cleanest option and documents the migration so the 125 existing tests stay green.
- `.blk` block data is already **binary** (`INCBIN`), not ASM text — decide whether to keep loading
  it as a binary content asset or embed it; metadata/events/scripts/text are the ASM-text tables to
  bake.

**M10.0 — build-time map data generation — DONE.** Resolved the circular-reference constraint
via a new shared **`PokeGold.MapData`** library (Option B): it holds the map DUs/records
(`ScriptCommand`, `MapEvents`/`WarpEvent`/`CoordEvent`/`BgEvent`/`ObjectEvent`, `MapMeta`/`Connection`,
`GeneratedMap`) + the pure parsers (`ScriptParser`/`MapText`/`MapEventParser`/`MapMetaParser`), and is
referenced by BOTH `PokeGold.Game` and `PokeGold.DataGen` — no DU duplication, DUs keep namespace
`PokeGold.Game.Overworld.Script` so Game consumers need no `open` changes. DataGen's new
`MapParsers.fs`/`EmitMaps.fs` parse all **368 maps** at build time (metadata, 142 connections, warps/
coords/bg/object events, scripts, text) and emit `Data/Generated/Maps.Generated.fs` (~1.97 MB, module
`MapsData` with `all : Map<string,GeneratedMap>` + `byName`), git-ignored and regenerated by the
`GenerateGameData` MSBuild target (now keyed on `constants/map_constants.asm`, `data/maps/{maps,
attributes}.asm`, `maps/*.asm`). Game compiles the generated file in ~10 s (single file — no region
split needed). Runtime still parses via the new `Overworld/Script/AsmLoad.fs` seam (M10.1 removes it);
the `MapEvents` query module stays in Game (needs `World`). 129/129 tests green (4 new `MapDataTests`
gating count=368 + connections + NewBarkTown warps + name/const integrity).

**M10.1 — OverworldState consumes MapsData — DONE.** Repointed the overworld load
path at the baked table: `eventsFor`/`scriptFor`/`textFor` now read `MapsData.byName`
(no more `AsmLoad` in the load path), `loadAssets` derives dimensions + tileset stem
from the baked `MapMeta` (`TILESET_JOHTO_MODERN` → `johto_modern`) instead of a
hard-coded Azalea case, and `mapIdOfConst` is a `Const→Name` map built from all 368
maps. `tryWarp` now resolves any map's warp and loads it when its on-disk assets
(`.blk` + tileset gfx + collision) are present, no-opping otherwise (interiors whose
`.blk` isn't in the tree yet). `AsmLoad.fs` is retained as a test-only utility (parse
live `.asm` to assert against the baked data). `OverworldState.fs` has zero
`Assets.readText`. 130/130 tests green (new gate: loading AzaleaTown yields the exact
baked Events/Script/Text; the no-op warp test repointed to a `.blk`-less map).

###### M10 addendum — inherited deferrals (from M4/M8) to enumerate here


These were explicitly catalogued as out-of-scope during the thin-slice milestones and now come
due as part of M10's "NPCs block the player" + overworld-feel work. None are bugs today; the seams
already exist in the code.

- **NPC / object collision (from M4 "Out of scope: NPC collision").** `Movement.cellWalkable`
  currently checks only the map tile-collision table — object positions aren't passed in, so the
  player walks through Gramps. M10 must fold the live object set into the walkability test (a cell
  occupied by a non-passable object is blocked), matching the real object-engine's occupied-tile
  rules (`wObjectStructs` / `GetObjectStruct`; objects reserve their current **and** target tile
  while stepping). Player↔object **and** object↔object (so wanderers don't overlap).
- **Overworld SFX seams (from M4/M8 "Hop SFX deferred"; M8 shipped synth only).** The audio synth
  exists and `ISoundBoard.PlaySfx` works (used for `Sfx_Menu`); only the overworld triggers are
  unwired. Two pre-built hooks to consume:
  - **Ledge hop → `SFX_JUMP_OVER_LEDGE`.** `Player.Motion = Hopping` already marks the hop frame;
    play the cry-less SFX on the hop's first frame.
  - **Wall bump → `SFX_BUMP`** (`constants/sfx_constants.asm:39`, label `Sfx_Bump`; played in
    `engine/overworld/player_movement.asm:771-776`, debounced against itself). `Player.Bumped` is
    already set true for exactly the one frame a bump begins (its comment literally says "so a
    future audio system can play the bump SFX").
  - Plain walking is **silent in GSC by design** — no footstep SFX to add.
  - Wire these in the scene layer (where `ISoundBoard` lives), not in pure `Movement`/`Player`,
    keeping the movement model pure and testable. The SFX labels resolve via `audio/sfx_pointers.asm`
    (`Sfx_JumpOverLedge`, `Sfx_Bump`).
- **`applymovement` actor paths (M9 effect, enacted here).** M9 surfaces `ApplyMovement(obj, path)`
  as a suspending effect but the scene currently no-ops it; M10's object engine is where scripted
  movement is actually played out (the object-movement step function M10 builds for wandering NPCs
  is the same one that runs a scripted path to completion).


##### M11 — Menus & UI shell  · L
- **Deliverables:** the windowing/menu framework, then the core menus: Start menu, **Party**
  (summary, switch order), **Bag** (pockets, use/give/toss), **Pokémon summary** pages, **Pokédex**
  (seen/own, area, search), **Options**, and the **Save** menu (drives M7). Field-move use from
  menus (Cut/Surf/etc. dispatched to M17 gating).
- **Acceptance:** open each menu over the overworld, navigate with the GB button set, use an item
  on a Pokémon, reorder the party, and view a populated Pokédex entry; all render to tile accuracy.
- **Depends on:** M5, M7. **Risks:** menu-state breadth. *Mitigation:* shared menu DU + interpreter;
  build incrementally per menu.

##### M12 — Town services & storage  · M
- **Deliverables:** **Pokémon Center** healing + nurse script; **Poké Mart** buy/sell with the
  money/economy; the **PC box storage system** (Bill's PC: deposit/withdraw/move across all boxes);
  PC item storage; mailbox basics.
- **Acceptance:** faint, heal at a Center to full; buy and sell items with correct prices and money
  math; deposit/withdraw a Pokémon across boxes and have it persist through save/reload.
- **Depends on:** M9, M11, M7. **Risks:** box-data volume/persistence. *Mitigation:* version box
  schema (D6) from the start.

#### Phase B — Battle to full coverage

##### M13 — Complete battle mechanics  · XL
- **Deliverables:** finish the **battle-effect command interpreter** to cover **every move effect**;
  status conditions, stat stages, the full type chart, criticals, multi-turn/charge/recharge moves,
  weather, flinch/recoil/drain/OHKO/etc.; **capture mechanics** (all balls + Gen-2 catch formula);
  end-of-turn resolution order.
- **Acceptance:** a table-driven suite of worked examples from the disassembly passes for damage,
  status, stat-stage, type, crit, and catch-rate cases; a wild battle can be won, lost, fled, or
  caught.
- **Depends on:** M6. **Risks:** effect long-tail & ordering bugs. *Mitigation:* one unit test per
  move effect id, sourced from the disassembly.

##### M14 — Trainers & battle AI  · L
- **Deliverables:** **trainer battles** (all trainer-class/party data), switching, multi-Pokémon
  flow, item-in-battle, the **enemy AI** (move scoring + switch logic per the disassembly), battle
  rewards (money, EXP split, EVs/stat-exp).
- **Acceptance:** a scripted trainer battle plays out with AI choosing moves/switches, awards correct
  money and EXP, and ends the encounter cleanly; double-checks against disassembly AI tables.
- **Depends on:** M13, M9. **Risks:** AI fidelity. *Mitigation:* port the AI scoring tables directly;
  spot-test decisions.

##### M15 — Growth systems  · M
- **Deliverables:** EXP curves & leveling, stat/stat-exp recalculation, **move learnsets** (level/
  TM/HM/tutor), **evolution** (level, item, trade [via D7], happiness, time-of-day, stats),
  held-item effects, friendship.
- **Acceptance:** a Pokémon gains EXP, levels, learns a level-up move (with the "forget a move"
  prompt), and evolves by each trigger type (incl. a trade-evo through the offline terminal).
- **Depends on:** M13, M16 (data). **Risks:** evolution trigger coverage. *Mitigation:* enumerate
  evolution methods from `evos_attacks`.

#### Phase C — Content & progression

##### M16 — Full game data  · L
- **Deliverables:** all **251 species** (base stats, types, learnsets, evolutions, dex entries),
  every **move** and **item**, the full type chart, and **all encounter tables** for every map
  (grass/water/headbutt/rod, time-of-day and swarm variants).
- **Acceptance:** data spot-checks across the whole tables (species count = 251, per-map encounter
  slots, item/move counts) pass against the disassembly; encounters in any map draw from the right
  table for the time of day.
- **Depends on:** M2. **Risks:** volume/format edge cases. *Mitigation:* generated loaders + count
  assertions per table.

##### M17 — Johto story, gyms & field moves  · XL
- **Deliverables:** the complete **Johto main-story scripts/cutscenes**; all **8 gym leaders +
  badges**; **HM/field-move gating** (Cut, Surf, Strength, Whirlpool, Waterfall, Fly, Flash) wired
  to overworld locomotion (extends the M4 addendum); rival fights and the **Team Rocket** arc
  (Slowpoke Well, Radio Tower).
- **Acceptance:** a continuous playthrough from New Bark through all 8 Johto badges is possible,
  gated correctly by badges/HMs, with story events firing in order and persisting across saves.
- **Depends on:** M9, M10, M14, M15, M16. **Risks:** script breadth & ordering. *Mitigation:* lean
  on the M9 interpreter; track story flags explicitly.

##### M18 — Elite Four, Hall of Fame & post-game unlock  · M
- **Deliverables:** Victory Road, the **Elite Four + Champion** gauntlet, **Hall of Fame** record +
  **credits**, and the post-game unlock (S.S. Aqua to Kanto, Pokémon expanded, etc.).
- **Acceptance:** beat the Elite Four from a valid save, see Hall of Fame + credits, and land in the
  post-game state with Kanto accessible.
- **Depends on:** M17. **Risks:** Hall-of-Fame data persistence. *Mitigation:* version it into the
  save (D6).

##### M19 — Kanto  · XL
- **Deliverables:** all **16 Kanto maps**, the **8 Kanto gyms/badges**, Kanto trainers/encounters,
  rematches, the Power Plant/Cerulean Rocket events, and **Red** on Mt. Silver.
- **Acceptance:** travel to Kanto, earn all 8 Kanto badges, and battle Red to completion; world,
  encounters, and scripts behave as in Johto.
- **Depends on:** M18. **Risks:** content volume. *Mitigation:* reuse M9/M10/M14 wholesale; it's
  data + scripts, not new systems.

#### Phase D — Gen-2 signature systems & the last mile

##### M20 — Time & telephony  · L
- **Deliverables:** **RTC day/night** clock and time-of-day tinting; time-based events &
  encounters (incl. swarms, morning/day/night tables); the **phone** (register/receive calls,
  rematches, gifts, tips); the **bug-catching contest**; **Radio** programs.
- **Acceptance:** the clock advances day/night with correct palettes and encounter tables; a
  registered trainer calls and offers a rematch; the bug contest runs on its schedule.
- **Depends on:** M9, M16. **Risks:** RTC source-of-truth. *Mitigation:* drive from real wall clock;
  make it test-injectable.

##### M21 — Breeding, apricorns & diversions  · L
- **Deliverables:** **Day-Care & breeding** (egg generation, inheritance, hatching); **berries**;
  **apricorns + Kurt's custom Poké Balls**; the **Game Corner** (slots/prizes); **Mom's savings**;
  and the **Ruins of Alph** puzzles (unlock Unown).
- **Acceptance:** leave two compatible Pokémon at the Day-Care, receive and hatch an egg with
  inherited moves; turn apricorns into balls via Kurt; Ruins of Alph puzzles unlock Unown.
- **Depends on:** M15, M9. **Risks:** breeding-inheritance rules. *Mitigation:* port inheritance
  tables from the disassembly; unit-test egg generation.

##### M22 — Trading, events & Pokédex completion  · L
- **Deliverables:** the **offline trade terminal** (D7) enabling trades + trade-evolutions and
  version-exclusive sourcing; **in-game trades**; **event/legendary** triggers (D9) — roaming
  beasts (Suicune story, Raikou/Entei), Lugia/Ho-Oh, Red Gyarados, the GS Ball/Celebi event; the
  **Pokédex-complete** reward/flow.
- **Acceptance:** **the National Pokédex can be completed** to all 251 within the implementation
  (capture + evolve + trade-evo + in-game trade + events), and dex-completion triggers its reward.
  This is the **project Definition of Done gate**.
- **Depends on:** M15, M16, M17, M19. **Risks:** the "completable dex" constraint (trade-evos,
  exclusives, events). *Mitigation:* D7–D9 make every species reachable single-player.

---

## Dependency graph

```
Vertical slice (M0–M8):
  M0 → M1 → M2 → M3 → M4 ─┐
                 │        ├→ M7
                 └→ M5 ───┤
                          └→ M6
  M2 → M8

Full game (M9–M22):
  M5,M4 → M9 ─┬→ M10 ┐
              │       ├→ M17 → M18 → M19 ┐
  M2 → M16 ───┼→ M15 ┤                   ├→ M22  (Definition of Done)
  M6 → M13 → M14 ─────┘                  │
  M7 → M11 → M12                         │
  M9,M16 → M20                           │
  M15,M9 → M21 ──────────────────────────┘
```
Phase A (M9–M12) widens the overworld; Phase B (M13–M15) completes battle & growth; Phase C
(M16–M19) is content & progression through Red; Phase D (M20–M22) adds Gen-2 systems and closes
on a completable Pokédex.

## Risk register (top)

| Risk | Impact | Likelihood | Mitigation / owner-action |
|------|--------|------------|---------------------------|
| F#+MonoGame tooling friction | Blocks M0 | Med | Reference DesktopGL NuGet directly from F# exe; spike in M0 |
| Data-extraction strategy churn (D1) | Rework M2+ | Med | Decide D1 before M2; isolate behind a loader interface |
| Gen-2 map/block model complexity | Slips M2/M3 | Med | Model only what the slice needs; golden-image tests |
| Damage formula / battle edge cases | Subtle M6/M13 bugs | High | Worked-example unit tests from disassembly |
| Audio synthesis fidelity | M8 sounds wrong | High | One channel/track first; iterate |
| Script/event command long-tail | Story gaps, M9/M17 slip | High | Enumerate the full command table up front; test the interpreter on real script bytes |
| Content volume (M16–M19, M21) | Long grind, data bugs | High | Generated loaders + per-table count/spot-check tests; reuse systems, add only data |
| Completable-dex constraints (trade-evo, exclusives, events) | Blocks DoD-2 at M22 | Med | D7–D9: offline trade terminal + built-in event unlocks make every species single-player-reachable |
| Save schema churn as state grows | Save/reload breakage | Med | Version the save from M7; migrate on load |

## Definition of done

Two gates, in order:

**DoD-1 — Vertical slice (after M8).** A single contiguous slice is playable end to end: boot →
walk one real map → read an NPC/sign → enter and finish one wild battle → save and reload. Data in
that slice is spot-checked against the disassembly; interpreters have unit tests; the slice renders
to tile accuracy. *(This proves the architecture — it is a checkpoint, not the finish line.)*

**DoD-2 — 100%-able Gold (the north star, gated at M22).** From a fresh save the game can be
**fully completed**: all 16 badges (Johto + Kanto), Elite Four + Champion, Hall of Fame + credits,
Red on Mt. Silver, and a **completable National Pokédex** (all 251 obtainable in-impl per D7–D9).
Throughout, save/reload preserves all state; story/event flags fire in the correct order;
encounters, battles, and growth match the disassembly on a table-driven test suite. Glitchless
completion is the bar.

## Tracking

Milestones M0–M22 are mirrored as todos in the session database with dependencies. Update status
there as work proceeds. M0–M8 (vertical slice) are scoped tightly; M9–M22 (full game) are coarse
and get split into sub-tasks as each is picked up.
