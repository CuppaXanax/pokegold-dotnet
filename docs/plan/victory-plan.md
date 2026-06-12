# Victory Plan — from "all milestones landed" to a playable all-badges run

This is the **handoff document** for finishing the port. It assumes the milestone plan
(`plan.md`, M0–M25) is complete — it is — and describes only what remains between today's
state and **victory**: a fresh save that can be played, glitchlessly, through **all 16
badges, the Elite Four, and Red on Mt. Silver** (the "100% more-or-less speedrun" bar),
with the strict-100% side systems behind it.

It is written so that a model (or human) with **no prior context** can pick up any single
work item, complete it, and verify it, without re-deriving the project's architecture.
Read this file, then read only the files each work item names.

**State as of 2026-06-11:** 993/993 tests green. The script-VM golden path is verified
end-to-end from the New Bark bedroom to the credits after Red (`GoldenPathStoryGateTests`
gates **G1–G14**). The remaining risk is concentrated in Workstream A below.

---

## 0. Orientation — read this once

### What exists and where

| Thing | Where | Notes |
|---|---|---|
| Game logic (platform-free) | `engine-dotnet/src/PokeGold.Game/` | No MonoGame types anywhere in here |
| Desktop shell | `engine-dotnet/src/PokeGold.Host/` | MonoGame DesktopGL |
| Android shell | `engine-dotnet/src/PokeGold.Host.Android/` | **Needs the Android SDK — skip it locally** |
| Map/script data model + parsers | `engine-dotnet/src/PokeGold.MapData/` | Shared by Game and DataGen |
| Build-time data baking | `engine-dotnet/tools/PokeGold.DataGen/` | Emits `Data/Generated/*.Generated.fs` (gitignored) |
| Tests | `engine-dotnet/tests/PokeGold.Tests/` | xUnit; this is the only verification surface |
| Script VM | `src/PokeGold.Game/Overworld/Script/Script.fs` | Pure, resumable; suspends on `ScriptEffect` |
| Scene shell / effect handlers | `src/PokeGold.Game/Scenes/OverworldScene.fs` | Turns `ScriptEffect`s into scenes/state |
| Battle engine | `src/PokeGold.Game/Battle/` | Deterministic, seeded RNG |
| Headless full-game driver | `tests/PokeGold.Tests/GameDriver.fs` + `src/PokeGold.Game/Debug/RuntimeApi.fs` | Drives real frames with buttons |

### The three verification layers (know which one you're working at)

1. **Script-VM layer** — `GoldenPathStoryGateTests.fs`. Runs map scripts directly through
   the pure VM with a simplified effect applier. Proves: script logic, event/flag/scene
   wiring, item/party effects, cross-map staging. **Done bedroom→credits (G1–G14).**
2. **Runtime layer** — `GameDriver` tests (`JohtoRuntimeTests.fs`, `NewBarkRuntimeTests.fs`,
   `OverworldSchedulerTests.fs`). Boots the real `Game`, ticks real frames, presses real
   buttons. Proves: movement, collision, warps, scene stack, callbacks, coord triggers,
   text boxes, battles-from-overworld. **Only spot-covered. This is the gap.**
3. **Eyeball layer** — `dotnet run --project src/PokeGold.Host`. F5 save / F9 load.

### The debt ledger is the to-do list

`tests/PokeGold.Tests/ConformanceLedgerTests.fs` enumerates **every** script command,
script special, object/bg-event kind, scene surface, move effect, held-item effect, and
field move, each with a status (`FaithfulTested` / `ImplementedApproximate` / `StubNoOp` /
`Unknown`) and tags (`CriticalPathJohto`, `CriticalPathKanto`, `RequiredFor100Percent`,
`SideSystem`, `Cosmetic`, `LinkOnly`). The ledger is **enforced by tests** — adding a DU
case or special without classifying it fails the build. When you implement something,
**move its ledger entry** and say in the note what test covers it.

### Build & test (Windows; the repo root is the disassembly)

```
cd engine-dotnet
dotnet build src/PokeGold.Game     # also regenerates Data/Generated/* via DataGen
dotnet test  tests/PokeGold.Tests  # full suite, ~10 s
dotnet build src/PokeGold.Host     # desktop shell
```

Never build the whole solution unless the Android SDK is installed — `PokeGold.Host.Android`
fails with XA5300 otherwise. Everything else builds individually.

### Non-negotiable conventions

- **The disassembly is the spec.** Before implementing anything, read the actual `.asm`
  (`maps/*.asm`, `engine/**/*.asm`, `constants/*.asm`, `data/**`). Do not implement from
  memory of how Gen 2 "probably works" — several "obvious" facts are wrong (e.g. the EXPN
  CARD comes from the Lavender radio director, not the Power Plant manager).
- **No runtime `.asm` parsing.** All static data goes through DataGen at build time.
  Parser changes in `PokeGold.MapData` auto-trigger regeneration (the `GenerateGameData`
  MSBuild target keys on those `.fs` files).
- **`CoverageSweepTests.fs` pins the baked command total (currently 21280).** If you change
  a macro expansion in `ScriptParser.fs`, the pin moves — update it *and* the comment
  explaining the delta, as the existing comments do.
- **Tests gate everything.** Each work item below ends with a named test. A change without
  a test that would fail before the change is not done.
- Commit style: short imperative subject, explanatory body, author Xander Hawthorne,
  `Co-authored-by:` trailer. One concern per commit.

---

## Workstream A — runtime route verification (the critical path)

**This is most of the remaining work.** The script-VM gates prove the *story logic*; they
do not prove the player can physically walk the route: warps load, connections stream,
collision blocks correctly, NPCs/triggers fire at the right tiles, gated guards
(badge checks, Saffron power outage, Snorlax body) actually block and unblock.

### The recipe (copy `JohtoRuntimeTests.fs`)

Each leg becomes one `[<Fact>]` using `GameDriver`:

```fsharp
let driver = GameDriver()
driver.Apply(StartNewGame "A")                       // fresh save, real boot state
driver.Apply(Warp("MapId", x, y, Some facing))       // teleport to the leg start
driver.Apply(SetFlag("ENGINE_FOGBADGE", true))       // grant prerequisites the leg assumes
// drive: driver.Step Up / driver.Talk() / driver.Press / driver.RunUntil(pred, frames)
// assert on driver.Snapshot: .TopScene, .Overworld.MapId, .Player.CellX/Y,
//   .Events, .EngineFlags, .Scenes, .LastTextLabel
driver.Trace |> List.iter (fun t -> assertHold core t.Snapshot)  // invariants every frame
```

A leg passes when: (a) every warp on it loads the destination map, (b) walking the leg's
corridor is possible (collision permits) and its walls block, (c) the leg's story trigger
fires from real movement/A-press (not from running the script by label).

### Leg checklist

Work top to bottom; each row is one test (or a few). Prerequisites = flags/events to
`Apply` before the leg so legs stay independent. Tiles/coords come from the map `.asm`
(`def_warp_events`, `def_coord_events`) — read them, don't guess.

**Johto**

| # | Leg | Key gates to assert |
|---|---|---|
| ✅ A1 | PlayersHouse2F → New Bark → Route 29 → Cherrygrove | bedroom→downstairs warp; Elm intro coord trigger; connection streaming both ways |
| ✅ A2 | Cherrygrove → Route 30/31 → Violet City | Mr. Pokémon house warp; rival coord event on return |
| ✅ A3 | Violet Gym → Falkner | gym warp; trainer sight-line battles; badge + TM31 |
| ✅ A4 | Route 32 → Union Cave → Route 33 → Azalea | cave darkness not required (no Flash gate); Slowpoke Well grunt blocking |
| ✅ A5 | Slowpoke Well B1F clear; Kurt leaves; Azalea Gym → Bugsy; rival ambush | scenes/events per `maps/AzaleaTown.asm` |
| ✅ A6 | Ilex Forest: Farfetch'd chase; HM01; Cut tree gate on Route 34 side | the Cut tree actually blocks until Cut is used (`FieldMoves`) |
| ✅ A7 | Goldenrod: Whitney (incl. cry/return), Flower Shop SquirtBottle | already partially covered — extend, don't duplicate |
| ✅ A8 | Route 36: Sudowoodo unblocks; Ecruteak; Burned Tower rival + beasts release | `InitRoamMons` fires (see B4) |
| ✅ A9 | Morty; Routes 38/39; Olivine lighthouse climb; Surf to Cianwood | SURF gate: water tile blocks walking, Surf crosses it |
| ✅ A10 | Pharmacy; Chuck; Jasmine return; Mahogany; Lake of Rage (Gyarados, Lance) | forced Red Gyarados battle from surf tile |
| ✅ A11 | Rocket Hideout B1–B2F; Pryce; Radio Tower takeover → clear | persistence: save/reload mid-arc keeps stage |
| ✅ A12 | Route 44 → Ice Path → Blackthorn → Clair → Dragon's Den | ice-slide tiles; Whirlpool gate; boulder puzzle (Strength) |
| ✅ A13 | New Bark → Route 27/26 → Victory Road gate (badge guard) → Indigo Plateau | guard blocks at <8 badges, passes at 8 |
| ✅ A14 | Elite Four rooms in sequence → Lance → Hall of Fame | door locks behind you; HoF scene → credits → post-game save state |

**Kanto**

| # | Leg | Key gates to assert |
|---|---|---|
| ✅ A15 | Elm ticket → Olivine Port (S.S. Ticket check at gangway) → ship → Vermilion Port | sailor blocks without ticket; granddaughter quest on foot |
| ✅ A16 | Vermilion: Surge; Saffron gates (closed pre-power, open post); Sabrina | Route 5/6/7/8 Saffron gate guards honour `EVENT_RETURNED_MACHINE_PART` |
| ✅ A17 | Cerulean/Power Plant machine-part chain on foot | hidden item via A-press on the gym tile (BGEVENT_ITEM dispatch) |
| ✅ A18 | Lavender EXPN card; radio tune via Pokégear UI; Snorlax wake battle | tune through the real radio tab (it writes `__radio_station`) |
| ✅ A19 | Diglett's Cave → Pewter → Brock; Celadon → Erika; Fuchsia → Janine | — |
| ✅ A20 | Cinnabar (Blue) → Seafoam (Blaine) → Viridian (Blue) | Blue object visibility flips per `EVENT_VIRIDIAN_GYM_BLUE` |
| ✅ A21 | Pallet → Oak (16 badges) → Route 28/Silver Cave gate → Red → credits | Mt. Silver guard honours `EVENT_OPENED_MT_SILVER` |

When a leg fails, the failure is the work: fix the runtime (collision id handling, warp
table, callback ordering, trigger tile), not the test. The script logic is already proven —
suspect the runtime layer first.

### Field-move runtime fidelity (folds into legs A6/A9/A12)

`FieldMoves.tryUse` validates badge + move + target tile and posts a message, but the
**map mutations** are approximations. The legs above force the real behavior:
- **Cut/headbutt trees**: tile must become walkable for the session (`changeblock`-style
  mutation; the tree→floor block swap lives in `engine/events/overworld.asm`
  (`Script_UsedCut` → `Script_CutDownTreeOrGrass`) and `data/events/*cut*`).
- **Surf**: player state transitions to surfing; water walkable, land dismount.
- **Strength boulders**: object pushing onto `BOULDER_HOLE` style targets
  (`engine/events/overworld.asm:934` `StrengthFunction`; boulders are map objects).
- **Whirlpool/Waterfall**: tile clears / forced vertical movement.
- **Flash**: cave darkness rendering toggle (cosmetic-adjacent; lowest priority).

---

## Workstream B — remaining script specials (each is small and self-contained)

Pattern to copy: `Special "SnorlaxAwake"` in `Script.fs` (pure, reads world state) or
`OpenMomBank`/`SetDayOfWeek` (suspending effect handled by `OverworldScene` with a small
scene). Always: read the engine `.asm` first; move the ledger entry; add a VM test
(`ScriptTests.fs`) and, if a scene is involved, a scene test
(`CriticalSpecialSceneTests.fs`).

Ordered by value:

| # | Special(s) | Source of truth | Needed for | Sketch |
|---|---|---|---|---|
| ✅ B1 | `SelectApricornForKurt` | `engine/events/specials.asm:345`, picker in `engine/menus/menu_2.asm` (`Kurt_SelectApricorn`), ball delivery in `maps/KurtsHouse.asm` | 100% (apricorn balls) | suspend on a list-pick effect over apricorns in bag; result → `wScriptVar` (item id, 0 = cancel) and the script tosses the apricorn. Apricorn→ball table: `data/items/apricorn_balls.asm` |
| ✅ B2 | `DayCareMan`/`DayCareLady`/`DayCareManOutside`/`DayCareMon1/2`/`CheckFirstMonIsEgg` | `engine/events/daycare.asm` | 100% (breeding; Breeding logic already exists in `Player/`) | deposit/withdraw via a party-pick effect; egg generation already tested in `BreedingTests.fs` — these specials are just the script seam |
| ✅ B3 | `MoveDeletion` | `engine/events/move_deleter.asm` (Blackthorn) | 100% (forgetting HMs) | party-pick + move-pick effect; remove move, compact list |
| ✅ B4 | `InitRoamMons` | `engine/events/specials.asm` | 100% dex (Raikou/Entei) | seed roamer state (species, level 40, random route) into world/save; encounter hook reads it |
| ✅ B5 | `MagnetTrain` | `maps/SaffronMagnetTrainStation.asm`, `engine/events/` | convenience transport | gate on `PASS` item; warp Saffron↔Goldenrod with a cutscene-less ride |
| ✅ B6 | `OlderHaircutBrother`/`YoungerHaircutBrother` | `engine/events/haircut.asm` (day-gated) | happiness evolutions | money take + happiness bump on a party pick |
| ✅ B7 | `SlotMachine`/`CardFlip` | `engine/games/` | Game Corner prizes (Porygon for dex) | a fair minimal game or an honest "buy coins" path; prize exchange is mart-like data |
| ✅ B8 | `NameRater` | `engine/menus/name_rater.asm` | cosmetic | reuse `NamingScene` |
| ✅ B9 | `BugContestJudging`/`GiveParkBalls`/contest specials | `engine/events/bug_contest*` | 100% (Scyther/Pinsir) | timed contest loop can be simplified to: enter, catch with park balls, judged vs seeded NPC scores (`data/events/bug_contest_winners.asm`) |
| ✅ B10 | `BillsGrandfather` | `maps/Route25.asm` area / `engine` | Eevee + stones | show-mon checks via `CheckPoke` like `FindPartyMonThatSpecies` |
| ✅ B11 | `CheckMagikarpLength`/`MagikarpHouseSign` | `engine/events/magikarp.asm` | side reward | DV-derived length formula, port directly |
| ✅ B12 | `UnownPrinter`/`UnownPuzzle` | `engine/games/unown_puzzle.asm` | Unown dex | puzzle can be a stub-accept (auto-solve prompt) initially; dex tracking already exists |
| — | `TimeCapsule`/`TradeCenter`/`Colosseum`/link specials | — | **excluded by design (D7)** — leave `LinkOnly` | the offline trade terminal already covers trade evolutions |

Also still stubbed (cosmetic, do last): `HealMachineAnim`, `TryQuickSave`, `FadeOutToBlack`/
`FadeInFromBlack`/`FadeOutToWhite`/`FadeInFromWhite` (wire to a 4-frame palette fade in the
scene shell), `Cry`/`PlayCurMonCry`/`PlaySlowCry` (route to the existing SFX synth — cries
are pitch-bent base cries, see `audio/cries.asm`), `UpdateSprites`/`ReloadSpritesNoPalettes`
(probably correct as no-ops — verify against call sites before "implementing").

---

## Workstream C — battle move-effect conformance audit

All ~140 `EFFECT_*` entries are `Unknown` in the ledger: implemented (M13) with tests, but
not audited line-by-line against the disassembly. Don't boil the ocean; audit in this order:

1. **Effects used by gym leaders / E4 / Red and the obvious player picks** (the run's
   actual battle surface): `EFFECT_NORMAL_HIT`, accuracy-down (Mud-Slap), confuse-hit
   (DynamicPunch), `EFFECT_THUNDER`, sleep/paralyze/burn families, stat-stage families,
   `EFFECT_HYPER_BEAM`, priority (Quick Attack), trap, drain, recoil.
2. The weird ones with bug-compatible behavior documented in `docs/bugs_and_glitches.md`
   (Belly Drum, Present, Beat Up, etc.) — decide per-effect whether to reproduce the bug
   (glitchless category ⇒ usually yes, reproduce faithful behavior).

Per effect: read `engine/battle/move_effects/<effect>.asm` + `effect_commands.asm`
ordering; write one worked-example test in `BattleTests.fs` (fixed seed, fixed roll,
asserted exact damage/state); flip the ledger entry to `FaithfulTested` with the test name.
Batch ~10 effects per commit.

Progress:
- ✅ C1 batch 1: `EFFECT_NORMAL_HIT`, `EFFECT_ACCURACY_DOWN_HIT`, `EFFECT_CONFUSE_HIT`,
  `EFFECT_THUNDER`, `EFFECT_SLEEP`, `EFFECT_PARALYZE`, `EFFECT_BURN_HIT`,
  `EFFECT_ATTACK_DOWN_HIT`, `EFFECT_HYPER_BEAM`, `EFFECT_PRIORITY_HIT`.
- ✅ C1 batch 2: `EFFECT_PARALYZE_HIT`, `EFFECT_FREEZE_HIT`, `EFFECT_FLINCH_HIT`,
  `EFFECT_POISON_HIT`, `EFFECT_DEFENSE_DOWN_HIT`, `EFFECT_SPEED_DOWN_HIT`,
  `EFFECT_SP_ATK_DOWN_HIT`, `EFFECT_SP_DEF_DOWN_HIT`, `EFFECT_TRAP_TARGET`,
  `EFFECT_LEECH_HIT`, `EFFECT_RECOIL_HIT`.
- ✅ C1 batch 3: `EFFECT_ATTACK_UP`, `EFFECT_DEFENSE_UP`, `EFFECT_SP_ATK_UP`,
  `EFFECT_EVASION_UP`, `EFFECT_ATTACK_UP_2`, `EFFECT_DEFENSE_UP_2`,
  `EFFECT_SPEED_UP_2`, `EFFECT_SP_DEF_UP_2`, `EFFECT_ATTACK_DOWN`,
  `EFFECT_DEFENSE_DOWN`, `EFFECT_SPEED_DOWN`, `EFFECT_ACCURACY_DOWN`,
  `EFFECT_EVASION_DOWN`, `EFFECT_ATTACK_DOWN_2`, `EFFECT_DEFENSE_DOWN_2`,
  `EFFECT_SPEED_DOWN_2`.
- ✅ C1 batch 4: `EFFECT_POISON`, `EFFECT_CONFUSE`, `EFFECT_TOXIC`,
  `EFFECT_HEAL`, `EFFECT_REFLECT`, `EFFECT_LIGHT_SCREEN`, `EFFECT_MIST`,
  `EFFECT_SAFEGUARD`, `EFFECT_RAIN_DANCE`, `EFFECT_SUNNY_DAY`,
  `EFFECT_SANDSTORM`.
- ✅ C1 batch 5: `EFFECT_ALWAYS_HIT`, `EFFECT_STATIC_DAMAGE`,
  `EFFECT_LEVEL_DAMAGE`, `EFFECT_SUPER_FANG`, `EFFECT_FALSE_SWIPE`,
  `EFFECT_RETURN`, `EFFECT_FRUSTRATION`, `EFFECT_FOCUS_ENERGY`,
  `EFFECT_SUBSTITUTE`, `EFFECT_LEECH_SEED`.
- ✅ C1 batch 6: `EFFECT_PSYWAVE`, `EFFECT_REVERSAL`, `EFFECT_PRESENT`,
  `EFFECT_MAGNITUDE`, `EFFECT_TRIPLE_KICK`, `EFFECT_MULTI_HIT`,
  `EFFECT_DOUBLE_HIT`, `EFFECT_POISON_MULTI_HIT`.
- ✅ C1 batch 7: `EFFECT_JUMP_KICK`, `EFFECT_PAY_DAY`, `EFFECT_RAPID_SPIN`.
- ✅ C1 batch 8: `EFFECT_ATTRACT`, `EFFECT_MEAN_LOOK`, `EFFECT_CURSE`,
  `EFFECT_SPIKES`.
- ✅ C1 batch 9: `EFFECT_BELLY_DRUM`, `EFFECT_PSYCH_UP`,
  `EFFECT_RESET_STATS`, `EFFECT_DREAM_EATER`, `EFFECT_PAIN_SPLIT`,
  `EFFECT_SPLASH`.
- ✅ C1 batch 10: `EFFECT_SELFDESTRUCT`, `EFFECT_TRI_ATTACK`.
- ✅ C1 batch 11: `EFFECT_ALL_UP_HIT`, `EFFECT_ATTACK_UP_HIT`,
  `EFFECT_DEFENSE_UP_HIT`, `EFFECT_DEFENSE_CURL`, `EFFECT_ROLLOUT`.
- ✅ C1 batch 12: `EFFECT_DESTINY_BOND`, `EFFECT_SWAGGER`.
- ✅ C1 batch 13: `EFFECT_EARTHQUAKE`, `EFFECT_GUST`, `EFFECT_STOMP`,
  `EFFECT_TWISTER`.
- ✅ C1 batch 14: `EFFECT_FURY_CUTTER`, `EFFECT_SNORE`.
- ✅ C1 batch 15: `EFFECT_HIDDEN_POWER`, `EFFECT_LOCK_ON`,
  `EFFECT_FORESIGHT`, `EFFECT_NIGHTMARE`, `EFFECT_PERISH_SONG`.
- ✅ C1 batch 16: `EFFECT_FLAME_WHEEL`, `EFFECT_SACRED_FIRE`,
  `EFFECT_HEAL_BELL`.
- ✅ C1 batch 17: `EFFECT_FLY`, `EFFECT_SOLARBEAM`,
  `EFFECT_RAZOR_WIND`, `EFFECT_SKULL_BASH`, `EFFECT_SKY_ATTACK`.
- ✅ C1 batch 18: `EFFECT_MORNING_SUN`, `EFFECT_SYNTHESIS`,
  `EFFECT_MOONLIGHT`.
- ✅ C1 batch 19: `EFFECT_RAGE`.
- ✅ C1 batch 20: `EFFECT_PROTECT`, `EFFECT_ENDURE`.
- ✅ C1 batch 21: `EFFECT_BEAT_UP`.
- ✅ C1 batch 22: `EFFECT_COUNTER`.
- ✅ C1 batch 23: `EFFECT_DISABLE`.
- ✅ C1 batch 24: `EFFECT_ENCORE`.
- ✅ C1 batch 25: `EFFECT_FUTURE_SIGHT`.
- ✅ C1 batch 26: `EFFECT_MIRROR_COAT`.
- ✅ C1 batch 27: `EFFECT_OHKO`.
- ✅ C1 batch 28: `EFFECT_RAMPAGE`.
- ✅ C1 batch 29: `EFFECT_SKETCH`.
- ✅ C1 batch 30: `EFFECT_SLEEP_TALK`.
- ✅ C1 batch 31: `EFFECT_SPITE`.
- ✅ C1 batch 32: `EFFECT_TELEPORT`.
- ✅ C1 batch 33: `EFFECT_THIEF`.
- ✅ C1 batch 34: `EFFECT_TRANSFORM`.
- ✅ C1 batch 35: `EFFECT_BATON_PASS`.
- ✅ C1 batch 36: `EFFECT_BIDE`.
- ✅ C1 batch 37: `EFFECT_CONVERSION`.
- ✅ C1 batch 38: `EFFECT_CONVERSION2`.

### C′ (optional) — generate the worked examples from the real ROM as oracle

Instead of hand-transcribing examples from the ASM, they can be machine-generated: this
repo builds the **byte-identical retail ROM** (`make`, verified by `roms.sha1`), and
`pokegold.sym` names every WRAM address. A one-off Python script driving a headless
emulator (PyBoy — already used as the APU reference for M8) can poke battle state
(species, stats, stages, move id, fixed RNG bytes) into symbol-mapped WRAM, step the
damage/effect routine, and read back the result — sampled across a grid of cases and
dumped as a JSON corpus checked into `tests/` (no emulator in CI; tests just consume the
fixture). Scope it to **closed-form mechanics only** (damage, stat calc, catch rate, exp
curves, type/crit/held-item modifiers); do not attempt lockstep overworld comparison —
the port intentionally diverges there. This mechanizes the most error-prone part of
Workstream C and upgrades those ledger entries to `FaithfulTested` against ground truth.

(For the curious: static *semantic* equivalence ASM↔F# is not pursued — undecidable and
meaningless given intentional divergence. Static *surface* totality — "every behavior the
ASM can express is at least enumerated and classified" — is already enforced by
`ConformanceLedgerTests` over the baked data; cheap extensions are DU-totality audits
over `constants/collision_constants.asm`, text control codes, and trainer AI layers.)

---

## Workstream D — presentation (after A–C; ship-quality, not correctness)

| Item | Now | Target |
|---|---|---|
| Credits | `Credits` command ends the script silently | a scrolling-text `CreditsScene` (data: `data/credits_*.asm`, sequencing: `engine/movie/credits.asm`); Red's battle and HoF both reach it |
| Battle move animations | none (instant resolution) | M25 in `plan.md` — parse `anim_*` scripts, ~30 primitives; biggest "feel" win for GitHub |
| Screen fades | no-ops | palette lerp in scene shell (also closes the Fade* specials) |
| Pokémon cries | silent | pitch/length-modulated base cry through the existing 4-channel synth (`audio/cries.asm`) |
| Pokédex area/search, dex sprites | stubs | needed for "complete-feeling" dex UI |
| Title→continue polish | works | verify Continue→save→exact-state with a runtime test |

---

## Suggested order & definition of done

1. **A1–A14** (Johto runtime legs) — interleave fixes as legs fail.
2. **A15–A21** (Kanto runtime legs).
3. **B1–B4** (the 100%-blocking specials), then C's first batch.
4. Remaining B items, C long tail, D.

**Tracking convention:** when a work item is complete (implemented + tested + ledger
updated), prefix its `#` cell in the table above with ✅ in the same commit. Blocked items
get ⚠️ plus a one-line note in the row. Agents: your kickoff prompt is
[`agent-prompt.md`](agent-prompt.md) — it enforces this loop.

**Victory check** (automatable end-state): one mega-test that chains all runtime legs with
no `Apply(SetFlag …)` shortcuts — every flag earned by play — from `StartNewGame` to the
Red credits, with `RuntimeInvariants.assertHold` on every frame. When that passes, tag a
release and record a GIF from the desktop host for the README.

#### What a green `dotnet test` means (and doesn't)

When the victory check passes, green means: **a full 16-badge + Red playthrough is
achievable from a fresh save, executed through the real engine at frame granularity —
real inputs, real movement/collision/warps, real battles — deterministically, with
invariants asserted on every frame.** Any regression to a warp, trigger, badge gate, or
route-relevant battle mechanic turns the suite red. Call this **playthrough-soundness**.

It deliberately does *not* mean:

- **Frame-parity with the original GBC game.** This is a high-level port (the
  emulator-grade plan was retired to `docs/_archive/plan-emulator-grade/`); pacing and
  RNG are faithful-feeling approximations, so original-hardware speedrun timings don't
  transfer tick-for-tick. (If that bar is ever wanted, the repo builds the byte-identical
  retail ROM, so an emulator-trace diff harness is *possible* — but it is a separate
  workstream and contradicts the project's thesis. Not planned.)
- **All-routes coverage.** The mega-test proves the golden route. Off-route play
  (different starter, unusual menuing, losses) is covered statistically by the
  conformance ledger and per-system tests, not exhaustively. Off-route bugs are fixed as
  found; they do not block victory.

### Notes for the GitHub showcase

- The repo's `engine-dotnet/README.md` undersells the project — refresh it with the
  current feature surface, a screenshot, and the G1–G14 story.
- Mind the asset posture statement in `plan.md` ("Asset & legal posture") when writing
  public-facing text: source assets, no ROM, SM64-decomp-style.

### Notes for vNext (pure-FP redesign — out of scope here)

Do **not** mix purification into the victory work. But while working, leave breadcrumbs:
the impure hot spots are `OverworldScene` (mutable fields + callback wiring), scene
callbacks mutating captured `player`/`world`, and `Game`'s scene stack. The script VM,
battle engine, movement model, and all data are already pure — vNext is mostly inverting
`OverworldScene` into a `State → Input → State * Effect list` fold. The
`ScriptEffect`/`HostEffect` seams are the right shape already; keep new code on that
pattern so the rewrite stays mechanical.
