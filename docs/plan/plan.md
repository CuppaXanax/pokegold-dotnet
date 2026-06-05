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

**M10.2 — per-map music binding — DONE.** The overworld now plays each map's own
BGM. Added a build-time `MUSIC_* → song-file` table (`Music.Generated.fs`, module
`MusicData.byId`, 93 entries): `MusicParsers` zips the parallel `music_constants.asm`
(ordered ids) and `audio/music_pointers.asm` (`dba Music_<Label>`) tables by index,
then resolves each to `audio/music/<label>.asm`, filtering to shipped files. This
pointer-table join is authoritative — a naive `MUSIC_X → x.asm` convention is wrong
for ~24 songs (e.g. `MUSIC_TITLE → Music_TitleScreen → titlescreen.asm`).
`OverworldScene.musicFor` resolves `MapsData.byName(mapId).Meta.Music` through
`MusicData.byId`, returning `option` (no track for `MUSIC_NONE`/unshipped songs)
instead of the old hard-coded `azaleatown.asm`. 131/131 tests green (new gate: ≥90
bindings, the `MUSIC_TITLE` exception, and AzaleaTown's id resolves to a real file).

**M10.3 — map connections — DONE.** Baked per-map `Connection` records (border map,
offset, streaming window) from `data/maps/maps.asm`; `OverworldState` streams the
neighbouring map's border tiles and crosses seams without a visible join, camera
clamped per the connection math. Border golden checks in `ConnectionsTests`.

**M10.4 — explicit script warps — DONE.** `warp`/`warpfacing` script effects move
the player between maps (interior↔exterior), loading the destination's assets and
placing the player at the target warp with the requested facing.

**M10.5 — NPC object engine — DONE.** A pure wander state machine (`ObjectStep`)
reproducing GSC `map_objects.asm` (sleep → pick direction → walk a tile inside the
wander radius → re-sleep) seeded per object for deterministic tests; `SPRITEMOVEDATA_*`
classifies each object's behaviour. NPCs render with facing/animation; occupancy is
threaded so two NPCs never share a cell. `ObjectTests` (13 gates).

**M10.6 — player↔NPC collision — DONE.** `ObjectStep.stepAllBlocked` folds the
player's occupied cells into NPC walkability and excludes NPC cells from the player's
walkable set — the player can no longer walk through Gramps, and wanderers don't step
onto the player. Two collision gates added.

**M10.7 — applymovement actor paths — DONE.** Movement scripts (`step`/`turn_head`/
`step_sleep`/…) are baked at build time into `GeneratedMap.Movements` (+ `ObjectConsts`
for object→index resolution); `MovementRunner` drives one NPC through a scripted path
one frame at a time (collision-checked), suspending the map VM until the path
completes, then resuming. The 348 `applymovement` call sites across the maps now enact
real motion. `MovementScriptTests` (9 gates).

**M10.8 — overworld locomotion SFX — DONE.** Ledge hop → `Sfx_JumpOverLedge` and wall
bump → `Sfx_Bump`, wired in the scene layer off the pre-existing `Player.Motion`/
`Player.Bumped` hooks (movement model stays pure). `OverworldSfxTests`.

**M10.9 — coverage sweep — DONE.** A regression gate over the baked world rather than a
runtime parse: 18 770 script commands across all 368 maps (stable vs the M9.6 sweep);
1 819 movement commands of which only 6 distinct macros remain unsupported
(`fix_facing`/`remove_fixed_facing`/`set_sliding`/`remove_sliding`/`teleport_from`/
`tree_shake` — all explicitly deferred to M11+/field-move milestones), ≥96% movement
coverage; and an enumerated overworld-sprite art gap (112 referenced, 33 without PNGs —
Pokémon-overworld + deferred field objects, rendered as blanks). `CoverageSweepTests`.

**M10 COMPLETE.** All sub-milestones (M10.0–M10.9) landed; 169/169 tests green. The
overworld loads every baked map, streams connections, warps between maps, plays per-map
music, runs wandering + scripted NPCs that collide with the player, and emits locomotion
SFX — all from build-time-baked data, no runtime `.asm` parsing.

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

##### M13.R — Devil's advocate: M0–M13 gap closure  · M

Items that were not explicitly owned by any M0–M13 milestone but are required for correct
gameplay before the content milestones (M16+) can be considered complete. Agents picking up
M14+ work should check these off first or in parallel.

- **Wild encounter wiring.** The grass/water/cave encounter *trigger* loop is not owned by any
  milestone. M6 proved a scripted wild battle; M13 completed battle mechanics including capture;
  M16 bakes encounter tables — but the actual "step in grass → RNG roll against encounter rate →
  spawn a wild mon from the map's table → enter battle" overworld hook is an integration seam
  that falls between M9 (scripts), M13 (battle), and M16 (data). **Action:** wire the encounter
  trigger in the overworld step loop, reading `TALL_GRASS`/`LONG_GRASS`/`WATER_TILE` collision
  ids (already catalogued in the M4 addendum), consulting the map's baked encounter table (M16),
  rolling against the per-tile encounter rate, and dispatching to the battle scene (M13). The
  repel check (below) gates the roll. Source of truth:
  `engine/overworld/wildmons.asm::TryWildEncounter`.
- **Repel system.** `REPEL`/`SUPER_REPEL`/`MAX_REPEL` decrement a step counter (`wRepelEffect`)
  and suppress encounters when the lead party mon's level exceeds the wild mon's. Trivial logic
  but blocks any serious playtesting — without it, grass is either always-encounter or
  never-encounter. Source: `engine/overworld/wildmons.asm::CanEncounter` + item-use effect.
  Needs an item-use handler in the Bag/Pack scene (M11) and a step-counter field in save (M7).
- **Fishing.** Old Rod / Good Rod / Super Rod are field-move items that trigger a
  "fishing minigame" (dot timing prompt) and then a water-encounter from a separate table.
  Not mentioned in M17's HM/field-move list. **Action:** add rod use as a field-move-adjacent
  item action in M17; encounter tables come from M16 (`data/wild/*.asm` `fish_group` entries).
  Source: `engine/overworld/events.asm::FishingRod`, `engine/overworld/wildmons.asm::WildFish`.
- **Shiny Pokémon.** Gen-2 shininess is DV-based (attack DV = 2/3/6/7/10/11/14/15, all other
  DVs = 10+). Detection is a pure function of the 4 DV nybbles and should be threaded through
  `PartyMon`/`BattleMon` creation. Visual indicator: a star sparkle on send-out + a star icon
  in the summary screen. Without this, shinies exist in the data but the player can never tell.
  Source: `engine/pokemon/search.asm::CheckShininess`. **Action:** add `IsShiny` derived
  property to `PartyMon`; render star in `SummaryScene`; sparkle SFX in `BattleScene` send-out.
- **Unown letter forms.** The 26 Unown forms are determined by DVs (each DV nybble contributes
  bits to a letter index). M21 mentions the Ruins of Alph puzzles but not the per-form sprite
  loading, the Unown dex (a separate tracked list of caught letters), or the word-display wall
  in the Ruins. Source: `engine/pokemon/unown_form.asm`, `data/pokemon/unown_words.asm`.
  **Action:** add `UnownLetter` derivation; track caught-letters set in save; load per-letter
  sprites from `gfx/pokemon/unown/`.
- **Intro / title screen / new-game flow.** No milestone owns the boot sequence: Game Freak
  logo → title screen (Ho-Oh animation, press Start) → New Game / Continue / Options → Prof. Oak
  intro → player/rival name entry → wake up in bedroom. Currently the game hard-boots into the
  overworld. **Action:** see new milestone M24 below.

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
  to overworld locomotion (extends the M4 addendum); **fishing** (Old Rod / Good Rod / Super Rod
  as field-move-adjacent item actions, dot-timing prompt, water encounter tables from M16);
  rival fights and the **Team Rocket** arc (Slowpoke Well, Radio Tower).
- **Acceptance:** a continuous playthrough from New Bark through all 8 Johto badges is possible,
  gated correctly by badges/HMs, with story events firing in order and persisting across saves;
  fishing from a water tile produces the correct species for the map's fish group.
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

##### M23 — GBC overworld color & per-tile palettes  · M
- **Why this exists:** the overworld currently renders with a single hand-picked 4-shade palette
  for the whole map and a grayscale sprite palette (`OverworldState.fs` `mapPalette`/`spritePalette`),
  so every area reads as one hue (Azalea = green). The color *pipeline* already exists — `Palette`
  does RGB555→RGBA and parses pret `.pal` files, and the renderers already take a palette argument —
  what's missing is the **per-tile/per-object palette data wiring** that gives the real Game Boy
  Color look. This milestone supplies that data and the attribute lookups that drive it. It is a
  prerequisite for M20's day/night **tinting**, which swaps between palette sets per time-of-day.
- **Deliverables:**
  - **Baked palette data (DataGen):** generate per-tileset CGB palettes from the disassembly's
    `gfx/tilesets/<name>.pal` and the per-tile palette-attribute maps
    (`gfx/tilesets/<name>_palette_map.asm` / metatile attribute bytes), emitted as a generated F#
    table keyed by tileset stem — no runtime `.asm`/`.pal` parsing on the hot path.
  - **Per-tile palette selection in `MapRenderer`:** each metatile/tile picks its CGB BG palette
    index from the attribute map instead of a single global palette; the blitter resolves the 2bpp
    pixel through that tile's palette.
  - **Per-object/sprite palettes:** honour the `object_event` `palette` field we already parse
    (plan §M10, line ~300) plus the OW sprite palette table, so NPCs and the player use their
    assigned CGB OBJ palettes rather than one grayscale ramp.
  - **Time-of-day palette sets** structured so M20 can select morning/day/night variants without
    re-plumbing the renderer (the selection hook lands here; the *clock* that drives it is M20).
- **Acceptance:** Azalea Town (and a second, differently-tinted map — e.g. a cave or interior)
  render in their correct multi-palette GBC colors, matching the disassembly's tileset palettes to
  a spot-checked golden image; NPCs show their assigned palettes; switching the time-of-day variant
  by hand visibly re-tints without artefacts.
- **Depends on:** M3 (palette/decoder primitives), M9/M10 (maps, tilesets, objects). **Risks:**
  RGB555→RGBA color-curve fidelity (already flagged in M3) and attribute-map extraction accuracy.
  *Mitigation:* start from the straight 5→8-bit scale, golden-image per tileset, and a later
  optional perceptual curve.

##### M20 — Time, Pokégear & telephony  · L
- **Deliverables:** **RTC day/night** clock and time-of-day tinting (selecting the palette sets
  built in **M23**); time-based events &
  encounters (incl. swarms, morning/day/night tables); the **Pokégear UI** (map screen with
  fly-point markers, radio tuner with station selection, phone contact list — the gear itself is
  opened from the Start menu / select button and hosts the phone/radio/map tabs); the **phone**
  (register/receive calls, rematches, gifts, tips); **trainer rematches** (phone-registered
  trainers offer rematches with progressively stronger teams — requires the rematch party data
  from `data/trainers/` and a rematch-ready flag per trainer, checked on phone call generation);
  the **bug-catching contest**; **Radio** programs (Prof. Oak's talk, Pokémon music, Lucky Channel,
  Buena's Password, Team Rocket radio — each program has scripted effects on encounters/events).
- **Acceptance:** the clock advances day/night with correct palettes and encounter tables; the
  Pokégear opens with working map/radio/phone tabs; a registered trainer calls and offers a
  rematch with a stronger team; the bug contest runs on its schedule; radio stations produce
  their scripted effects.
- **Depends on:** M9, M16, M11 (menu framework). **Risks:** RTC source-of-truth. *Mitigation:*
  drive from real wall clock; make it test-injectable.

##### M21 — Breeding, apricorns & diversions  · L
- **Deliverables:** **Day-Care & breeding** (egg generation, inheritance, hatching); **berries**;
  **apricorns + Kurt's custom Poké Balls**; the **Game Corner** (slots/prizes); **Mom's savings**;
  the **Ruins of Alph** puzzles (unlock Unown); and **Unown letter forms** — DV-based letter
  derivation (`engine/pokemon/unown_form.asm`), per-letter sprite loading from
  `gfx/pokemon/unown/`, the Unown dex (a tracked set of caught letter forms persisted in save),
  and the word-display wall inside the Ruins.
- **Acceptance:** leave two compatible Pokémon at the Day-Care, receive and hatch an egg with
  inherited moves; turn apricorns into balls via Kurt; Ruins of Alph puzzles unlock Unown;
  catching Unown records the letter form in the Unown dex; all 26 letter forms are visually
  distinct and derivable from DVs.
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

##### M24 — Intro, title screen & new-game flow  · M
- **Why this exists:** the game currently hard-boots into the overworld with a pre-seeded save
  state. A real playthrough needs the full boot sequence. Without this, there is no "fresh save"
  path to DoD-2.
- **Deliverables:**
  - **Title screen:** Game Freak logo fade → title screen with Ho-Oh sprite animation, scrolling
    Pokémon, "PRESS START" prompt. Source: `engine/menus/title.asm`, `gfx/title/`.
  - **Main menu:** New Game / Continue / Options (+ Mystery Gift placeholder). Continue loads the
    existing M7 save; Options drives the existing `OptionsScene`.
    Source: `engine/menus/main_menu.asm`.
  - **New-game sequence:** Prof. Oak intro (sprite + text), player name entry (keyboard grid or
    preset names), rival name entry, gender selection (Crystal had it; Gold does not — confirm
    and skip), opening cutscene (shrink into Game Boy, wake up in bedroom).
    Source: `engine/menus/intro_menu.asm`, `engine/menus/naming_screen.asm`.
  - **Name-entry screen:** reusable keyboard-grid scene (also needed for box naming, Pokémon
    nicknames). Source: `gfx/naming_screen/`, `engine/menus/naming_screen.asm`.
- **Acceptance:** cold-start the game → see title → select New Game → enter a name → arrive in
  the player's bedroom in New Bark Town with the starter event ready to fire; Continue loads an
  existing save correctly.
- **Depends on:** M11 (menu framework), M9 (scripting for Oak's intro), M5 (text).
- **Risks:** the intro has bespoke animations not used elsewhere. *Mitigation:* these are
  self-contained scenes; implement as one-off renderers, not general systems.

##### M25 — Battle move animations  · M  *(optional — conscious scope decision)*
- **Status:** **Deferred by design.** The original game has ~250 move animations built from a
  custom animation scripting language (`engine/battle_anims/`, `data/battle_anims/`). These are
  purely cosmetic — they don't affect gameplay correctness. The port can ship DoD-2 without them
  (moves resolve instantly with HP bar changes and text, as they currently do).
- **If picked up:** parse the battle animation script language (`anim_*` macros in
  `macros/scripts/battle_anims.asm`) into a DU; implement the ~30 animation primitives
  (sprite movement, palette flash, screen shake, particle spawn) in `BattleRenderer`; bake
  animation data via DataGen. This is high effort / low gameplay value but high *feel* value.
- **Recommendation:** revisit after DoD-2. A post-DoD polish pass where move animations,
  evolution animations, and Hall of Fame animations all land together.

##### M26 — Multiplayer (future)  · XL  *(post-DoD-2, not on critical path)*
- **Why this exists:** the `PokeGold.Game` / `PokeGold.Host` architecture already separates
  pure game logic from platform I/O. The battle engine is deterministic (seeded LCG RNG).
  These properties make multiplayer structurally feasible without a rewrite.
- **Deliverables (sketch — to be scoped when picked up):**
  - **Online trading:** extend the offline trade terminal (D7/M22) with a network transport.
    Two players connect, browse each other's boxes, propose/accept trades. The `TradeData`
    contract already exists; the transport is the new work (WebSocket or relay server).
  - **Online battling:** two clients share a battle seed + team data; each turn, both select a
    move and exchange selections. The deterministic battle engine runs identically on both
    sides — only move indices are transmitted, not game state. Latency-tolerant by design
    (turn-based). Needs a lobby/matchmaking stub and a spectator-friendly replay log.
  - **Link Cable emulation (stretch):** a virtual "link cable" session that mimics the GB serial
    protocol at a high level — enabling any link-cable interaction (trade, battle, time capsule,
    mystery gift) through a single transport. Lower priority than purpose-built trade/battle.
- **Acceptance:** two instances of the game on different machines can trade a Pokémon and battle
  each other with correct move resolution.
- **Depends on:** M22 (trading infrastructure), M14 (battle), M15 (growth — traded mons must
  level correctly). **Risks:** NAT traversal, relay hosting, cheat prevention. *Mitigation:*
  start with LAN/direct-connect; relay and anti-cheat are separate milestones.

#### Engineering tooling (cross-cutting, not on the content critical path)

These support development/debugging of the milestones above; they ship outside the M-number
sequence and can land whenever useful.

##### T1 — Debug command pipe  · S  *(in progress)*
- **Deliverables:** an in-process **named-pipe debug server** (`PokeGold.Game/Debug`) exposing the
  running game over a simple newline command protocol, plus a thread-safe **command queue** so all
  inspection/mutation runs on the MonoGame update thread (no races against the frame loop). A small
  command set covers live inspection (`player`, `npcs`, `flags`, `vars`, `map`, `bag`, `scene`,
  `frame`) and mutation (`warp`, `tp`, `setflag`/`clearflag`, `setvar`). A PowerShell client
  (`engine-dotnet/tools/debug-cli.ps1`) lets a developer **or an agent** poke a running instance.
- **Acceptance:** with the game running, a client can read player position/NPC state/flags and warp
  or set a flag and see the effect on screen, all without stalling or corrupting the frame loop.
- **Depends on:** M10 (a live overworld scene to inspect). **Risks:** thread-safety vs. the game
  loop. *Mitigation:* commands are marshalled onto the update thread via the queue; the pipe thread
  only blocks its own client.

##### T2 — Embedded FSI REPL over the debug pipe  · M  *(post-game / future)*
- **Deliverables:** host an `FSharp.Compiler.Service` `FsiEvaluationSession` inside the game,
  bound to the live scene/world objects, reachable **through the same T1 pipe** (a `:fsi <expr>`
  mode) so a developer or agent can evaluate arbitrary F# against running state
  (e.g. `scene.DebugState.Npcs |> Array.filter ...`). The pipe transport, command queue, and
  game-thread marshalling are reused wholesale from T1; T2 only adds the FCS evaluation backend and
  a bound symbol environment.
- **Acceptance:** over the pipe, an arbitrary F# expression referencing live game state evaluates on
  the game thread and returns its rendered result; errors are reported without crashing the game.
- **Depends on:** T1. **Risks:** FCS is a heavy dependency with version sensitivity, and arbitrary
  eval on the game thread can stall it. *Mitigation:* keep it opt-in (debug builds / explicit flag),
  time-box/serialise evaluations on the queue, and treat T1's fixed command set as the default path.

---

## Dependency graph

```
Vertical slice (M0–M8):  ✅ ALL COMPLETE
  M0 → M1 → M2 → M3 → M4 ─┐
                 │        ├→ M7
                 └→ M5 ───┤
                          └→ M6
  M2 → M8

Full game (M9–M22+):
  M5,M4 → M9 ─┬→ M10 ┐                        ✅ M9–M13 COMPLETE
              │       ├→ M17 → M18 → M19 ┐
  M2 → M16 ───┼→ M15 ┤                   ├→ M22  (Definition of Done)
  M6 → M13 → M14 ─────┘                  │
  M7 → M11 → M12                         │
  M9,M16 → M20                           │
  M3,M9,M10 → M23 → M20                  │
  M15,M9 → M21 ──────────────────────────┘
  M13.R (gap closure) — parallel to M14+, no hard deps, items feed into M16/M17
  M11,M9,M5 → M24 (intro/new-game — required for DoD-2 "fresh save" gate)

Post-DoD-2 (not on critical path):
  M22 → M25 (move animations — optional polish)
  M22,M14,M15 → M26 (multiplayer)
```
Phase A (M9–M12) widens the overworld; Phase B (M13–M15) completes battle & growth; Phase C
(M16–M19) is content & progression through Red; Phase D (M20–M22) adds Gen-2 systems and closes
on a completable Pokédex. M24 (intro) is required for DoD-2 but can land any time after M11.
M25 (move animations) and M26 (multiplayer) are post-DoD-2 stretch goals.

## Risk register (top)

| Risk | Impact | Likelihood | Mitigation / owner-action |
|------|--------|------------|---------------------------|
| F#+MonoGame tooling friction | Blocks M0 | Med | ✅ Resolved — F# exe + DesktopGL NuGet works |
| Data-extraction strategy churn (D1) | Rework M2+ | Med | ✅ Resolved — parse repo assets directly |
| Gen-2 map/block model complexity | Slips M2/M3 | Med | ✅ Resolved — model complete, golden-image tests pass |
| Damage formula / battle edge cases | Subtle M6/M13 bugs | High | ✅ Mitigated — 120+ effects implemented with worked-example tests |
| Audio synthesis fidelity | M8 sounds wrong | High | ✅ Mitigated — faithful APU emulator from PyBoy reference |
| Script/event command long-tail | Story gaps, M9/M17 slip | High | Enumerate the full command table up front; ~110 opcodes remain for M17+ |
| Content volume (M16–M19, M21) | Long grind, data bugs | High | Generated loaders + per-table count/spot-check tests; reuse systems, add only data |
| Completable-dex constraints (trade-evo, exclusives, events) | Blocks DoD-2 at M22 | Med | D7–D9: offline trade terminal + built-in event unlocks make every species single-player-reachable |
| Save schema churn as state grows | Save/reload breakage | Med | Version the save from M7; migrate on load (v4 schema in place) |
| Wild encounter integration gap | Grass/water encounters never trigger | High | M13.R explicitly owns the wiring; source of truth identified (`TryWildEncounter`) |
| Intro/new-game flow missing | No "fresh save" path to DoD-2 | Med | M24 added; can land any time after M11; self-contained scenes |
| Shiny/Unown visual identity | Players can't identify shinies or Unown forms | Low | M13.R (shiny) + M21 (Unown) — pure DV derivation, low risk |
| Pokégear UI not owned | Phone/radio/map tabs have no scene | Med | M20 now explicitly owns the Pokégear scene + all tabs |
| Multiplayer scope creep | NAT/relay/cheat concerns grow unbounded | Med | M26 explicitly post-DoD-2; start LAN-only; relay is a separate milestone |

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

Milestones M0–M26 are mirrored as todos in the session database with dependencies. Update status
there as work proceeds. M0–M13 (vertical slice + battle mechanics) are **complete**. M13.R (gap
closure) runs in parallel with M14+. M14–M22 + M24 are the DoD-2 critical path. M25 (move
animations) and M26 (multiplayer) are post-DoD-2 stretch goals.
