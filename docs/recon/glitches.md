# Glitch inventory for `pret/pokegold`

Project assumption: **byte-accurate behavior is required; glitches are load-bearing**.

This file cross-references the requested Gen II glitch catalog against the current repo. Every entry lists the concrete code/data that would need faithful translation into C# to preserve the behavior. If the repo does not clearly prove a detail, it is marked **UNCLEAR**.

## Priority summary

| Glitch | Priority | Short preservation note |
|---|---|---|
| Celebi Egg glitch | CRITICAL | Preserve hidden egg species bytes and hatch-time trust of party struct species. |
| Coin Case glitch | CRITICAL | Preserve the text engine’s `done` vs `text_end` semantics and stack behavior. |
| Bad Clone glitch | CRITICAL | Preserve interruptible box-save ordering and unchecked active-box SRAM mirror. |
| Wrong pocket TMs | HIGH | Preserve separate pocket arrays plus lack of pocket/content validation. |
| Experience underflow | HIGH | Preserve Medium-Slow level-1 EXP math exactly, including underflow. |
| Stat recalculation glitch | HIGH | Preserve cached party stats and the fact that many views do not recalc them. |
| Berry / RTC glitch | HIGH | Preserve 140-day RTC modulo logic and day-difference wraparound. |
| Move PP overflow / PP Up bug | MEDIUM | Preserve PP-Up bit packing and battle logic that forgets to mask PP bits. |
| Trainer AI exploits | CRITICAL | Preserve score-based move choice plus AI bug branches. |
| DV/stat inheritance quirks | HIGH | Preserve Gen II breeding DV inheritance exactly, including compatibility quirks. |
| Type matchup errors | MEDIUM | Preserve canonical table and AI-side matchup misuse. |
| Save corruption exploits | CRITICAL | Preserve checksum boundaries, active-box handling, and Hall of Fame save bug. |
| Text buffer overflow | HIGH | Preserve fixed-size string buffers and unbounded text placement. |
| Map connection bugs | HIGH | Preserve scripted Surf movement bypassing normal connection checks. |
| RNG manipulation | CRITICAL | Preserve `rDIV`-driven RNG and VBlank advancement timing. |

---

## 1. Celebi Egg glitch ("GS Ball" / Celebi egg)

**Priority:** CRITICAL

**Description**

An egg’s visible species and its hidden species are different pieces of state. If party/species memory is manipulated so the hidden species byte becomes Celebi, the egg can still hatch into Celebi even though normal breeding should never create one.

**Root cause**

- `GiveEgg` creates eggs by first adding a normal party mon, then rewriting the last party slot so the **party list entry** becomes `EGG` while the **party struct species byte** stays whatever species was being wrapped into the egg (`engine/pokemon/move_mon.asm:1121-1188`).
- `HatchEggs` does not derive hatch species from the visible `wPartySpecies` entry. It walks party slots looking for visible `EGG`, then reads the hidden species from that slot’s party struct and writes it back into the visible list when hatching (`engine/pokemon/breeding.asm:206-250`).
- Normal breeding explicitly rejects species in the No Eggs group, so Celebi is not reachable by intended breeding (`engine/pokemon/breeding.asm:105-120`).
- **UNCLEAR:** the exact community setup used to turn this into a Celebi-specific egg is not fully recoverable from this repo alone. The code clearly shows *why arbitrary hidden-species eggs are possible*; it does not by itself document the player setup used to inject Celebi specifically.

**Subsystems involved**

- Egg creation/wrapping logic
- Party list vs party struct data model
- Hatch logic
- Any party manipulation that can alter hidden species bytes without sanitizing them

**Regression test approach**

- Build a fixture where a party slot is visibly `EGG` but its hidden party-struct species is Celebi.
- Hatch it and verify the visible species resolves to Celebi exactly as on GB/C.
- Also verify normal breeding still refuses No Eggs group parents.

---

## 2. Coin Case glitch

**Priority:** CRITICAL

**Description**

Using the Coin Case can produce arbitrary code execution because its text script is terminated with the wrong text opcode.

**Root cause**

- `CoinCaseEffect` feeds a menu textbox script into `MenuTextboxWaitButton` (`engine/items/item_effects.asm:2243-2249`).
- `_CoinCaseCountText` ends with `done` instead of `text_end` (`data/text/common_3.asm:336-341`).
- In the text macro layer, `done` emits `<DONE>` while `text_end` emits `TX_END` (`macros/scripts/text.asm:25-31,167-170`).
- The text interpreter only naturally stops on `TX_END` (`home/text.asm:590-595`). `<DONE>` instead dispatches to `DoneText`, which pops the text frame and returns through a different control path (`home/text.asm:220,484-491,597-615`).
- That mismatch is harmless in normal textbox contexts but unsafe in this menu-wrapper call site, which is what makes the Coin Case exploitable.

**Subsystems involved**

- Item-effect dispatch
- Menu textbox wrappers
- Text command encoding
- Text interpreter stack/control-flow behavior

**Regression test approach**

- Keep a low-level test that distinguishes `done` and `text_end` in menu textbox contexts.
- Reproduce the Coin Case call path specifically and verify it does **not** get silently normalized into a plain string print.
- For high-confidence preservation, add an emulator-verified test ROM/input that reproduces the original mis-termination behavior.

---

## 3. Bad Clone glitch

**Priority:** CRITICAL

**Description**

Interrupting a save during a box change can leave the outgoing box, incoming box, and active SRAM mirror out of sync, creating clones or corrupted "bad clones."

**Root cause**

- Box switching saves the current box *before* changing `wCurBox`, loads the new box into the active box mirror, and only then runs the full save text/save flow (`engine/menus/save.asm:40-58`).
- The box save/load itself is done in three partial SRAM copies because the active box buffer is larger than `wBoxPartialData` (`engine/menus/save.asm:851-987`).
- Checksums cover `sGameData` but **not** the active `sBox` mirror (`ram/sram.asm:93-109`; `engine/menus/save.asm:424-434`).
- On load, after checksum verification, the game still restores the active box via `LoadBox` (`engine/menus/save.asm:538-559`).
- Result: power loss/reset at the wrong point can preserve mismatched box state that the checksum system does not catch.

**Subsystems involved**

- Save menu flow
- SRAM box-mirror architecture
- Checksum boundaries
- Bill’s PC box switching

**Regression test approach**

- Simulate box-change save interruption after: `SaveBox`, `wCurBox` write, `LoadBox`, and before/after `SaveChecksum`.
- Verify the same desync/cloning states appear in the translated implementation.
- Do **not** collapse the active-box mirror into a transactional save unless there is an explicit compatibility mode.

---

## 4. Wrong pocket TMs

**Priority:** HIGH

**Description**

TM/HM items can be made to appear in the wrong bag pocket; once there, the game tends to trust the item ID more than the pocket container.

**Root cause**

- TM/HM items are flagged as `TM_HM` pocket items and as party-usable in the item attributes table (`data/items/attributes.asm:393-505`).
- TM/HMs are stored in their own flat quantity array `wTMsHMs`, while the normal pockets are separate arrays (`ram/wram.asm:2421-2430`; `engine/items/tmhm.asm:10-18`).
- The normal pack submenu path decides what an item does by consulting item attributes (`engine/items/pack.asm:242-309,425-455`); it does not re-validate that the item ID belongs in the currently-open pocket.
- TM/HM numbering is further complicated by non-contiguous dummy item IDs (`ITEM_C3`, `ITEM_DC`) and conversion helpers that explicitly skip those gaps (`data/items/attributes.asm:401-402,451-452`; `engine/items/items.asm:459-492`).
- Therefore, once corruption/PC item manipulation gets a TM/HM ID into the wrong container, the UI tends to honor the ID’s TM/HM behavior instead of sanitizing the mismatch.
- **UNCLEAR:** the exact setup players use to inject the wrong-pocket state is not documented in this repo.

**Subsystems involved**

- Bag storage layout
- Item attribute lookup
- Pack submenu dispatch
- TM/HM ID<->number conversion

**Regression test approach**

- Construct a corrupted save/bag state with a TM item ID in the item pocket.
- Verify the C# pack UI/use logic behaves like the original instead of rejecting/re-homing the item.
- Add separate tests for the non-contiguous TM item-ID conversion path.

---

## 5. Experience underflow

**Priority:** HIGH

**Description**

A level-1 Medium-Slow Pokémon can have its required EXP calculation underflow, which can turn tiny EXP gains into huge level jumps.

**Root cause**

- The Medium-Slow growth formula is `6/5*n^3 - 15*n^2 + 100*n - 140` (`data/growth_rates.asm:15-20`).
- `CalcExpAtLevel` implements the generic formula by subtracting the constant term before finishing the signed quadratic adjustment (`engine/pokemon/experience.asm:33-120`).
- The function is explicitly annotated as a bug for level-1 Medium-Slow mons (`engine/pokemon/experience.asm:35`).
- At level 1, `100*1 - 140` underflows before the final signed-term handling can compensate.

**Subsystems involved**

- Growth-rate data
- EXP-at-level math
- Level-up logic that compares current EXP to thresholds

**Regression test approach**

- Use a level-1 Medium-Slow mon with the original underflowed EXP state.
- Award minimal EXP and verify the exact original level jump sequence.
- Keep the arithmetic byte-accurate; do not replace it with safe signed integer math.

---

## 6. Stat recalculation glitch

**Priority:** HIGH

**Description**

Party Pokémon can keep stale stats because many code paths display cached stats instead of recalculating them. Boxed/temp mons *do* get recalculated, which is the classic Gen II "box trick" behavior.

**Root cause**

- `CopyMonToTempMon` copies the current party struct into `wTempMon` as-is (`engine/pokemon/tempmon.asm:1-33`).
- In the stats screen path, party mons go straight to `.got_stats`; only boxed/temp mons run `CalcTempmonStats` (`engine/pokemon/stats_screen.asm:35-44`).
- `PrintTempMonStats` then prints the already-copied stat fields from `wTempMonAttack` onward (`engine/pokemon/mon_stats.asm:87-108`).
- The real recalculation routine is `CalcMonStats` in `move_mon.asm` (`engine/pokemon/move_mon.asm:1402-1422`).
- So stat-affecting changes that update underlying EXP/DVs but do not explicitly call the calc routine leave party stats stale until a recalculating event occurs (boxing, leveling, evolution, etc.).

**Subsystems involved**

- Party struct caching
- Temp-mon copy logic
- Stats screen logic
- Stat recalculation call sites

**Regression test approach**

- Give a party mon stat EXP without leveling.
- Verify party-view stats remain stale while a box/tempmon path recalculates them.
- Preserve the call-site behavior; do not auto-recompute stats on every read.

---

## 7. Berry glitch / RTC rollover glitch

**Priority:** HIGH

**Description**

Long-lived saves can break or distort berry/daily-event behavior because the RTC and day-difference helpers intentionally wrap on a 140-day cycle.

**Root cause**

- `FixDays` explicitly reduces RTC day count modulo 140 (`home/time.asm:61-115`).
- Day-difference math also wraps by `20 * 7` days on underflow (`engine/overworld/time.asm:354-363`).
- `ClockContinue` treats RTC overflow/reset specially and clears daily timers on rollover/reset conditions (`engine/rtc/rtc.asm:139-158`).
- Daily systems such as fruit trees and daily reset logic are built on those same day/timer helpers (`engine/events/fruit_trees.asm:43-69`; `engine/overworld/time.asm:85-97`).
- **UNCLEAR:** whether the community label here should map 1:1 to the later, more famous Gen III "Berry Glitch." This repo clearly documents a long-term RTC/day-wrap problem; it does not call it by that name.

**Subsystems involved**

- RTC read/write
- Day rollover normalization
- Daily timer math
- Fruit tree/daily event reset logic

**Regression test approach**

- Simulate day 139 -> 140 -> 141 transitions and verify timers/fruit tree behavior matches the original.
- Preserve the modulo-140 wrap and the overflow side effects.
- Do not normalize RTC handling to monotonic real-world days in compatibility mode.

---

## 8. Move PP overflow / PP Up interaction bug

**Priority:** MEDIUM

**Description**

PP values pack current PP and PP Up count into one byte. Some battle logic forgets to mask out the PP Up bits, so PP-Up data can make a 0-PP move look usable.

**Root cause**

- `ComputeMaxPP` explicitly acknowledges the packed PP format and clamps 40-PP moves to 61 max PP to avoid overflowing into the PP Up bits (`engine/items/item_effects.asm:2749-2783`).
- `GetMaxPPOfMove` rebuilds max PP from the base PP plus the upper-bit PP Up count (`engine/items/item_effects.asm:2820-2890`).
- In battle, the "all moves exhausted" check for a disabled move path mistakenly does `and a` instead of `and PP_MASK`, and the file itself documents that PP Up bits will confuse the result (`engine/battle/core.asm:5284-5303`).

**Subsystems involved**

- Packed PP byte format
- PP Up application/max-PP math
- Battle move-availability logic

**Regression test approach**

- Use a mon whose disabled move has 0 current PP but PP Up bits set.
- Verify the battle engine reproduces the original erroneous decision about Struggle/usable moves.
- Also verify 40-PP moves cap at 61 after three PP Ups.

---

## 9. Trainer AI exploits

**Priority:** CRITICAL

**Description**

Gen II trainer AI is a deterministic score engine with multiple documented logic bugs, making it highly predictable and abusable.

**Root cause**

- Move choice is score-based: every move starts at a default score, layers modify those scores, then the AI randomly picks among the lowest-score surviving moves (`engine/battle/ai/move.asm:1-4,18-25,109-189`).
- Specific repo-documented scoring bugs:
  - `AI_Smart_Conversion2` discourages Conversion2 after the first turn because `wLastPlayerMove != 0` immediately routes to `.discourage` (`engine/battle/ai/scoring.asm:1654-1692`).
  - `AI_Smart_MeanLook` mistakenly checks the **enemy’s** Toxic substatus and can strongly encourage Mean Look on a badly poisoned AI mon (`engine/battle/ai/scoring.asm:1725-1746`).
- Specific item-AI bugs:
  - The AI item scan can use the trainer class’s base reward value as though it were an item (`engine/battle/ai/items.asm:159-209`).
  - Full Heal/Full Restore status cleanup misses Nightmare and burn/paralysis stat-drop cleanup (`engine/battle/ai/items.asm:716-728`).
  - Full Restore has special confusion cleanup, while the nearby comment notes inconsistent Full Heal behavior (`engine/battle/ai/items.asm:541-549`).

**Subsystems involved**

- Move scoring layers
- AI random tie-breaking
- Type/status heuristics
- AI item usage

**Regression test approach**

- Build deterministic battle-state fixtures and assert exact AI move/item choices.
- Include explicit fixtures for Conversion2, Mean Look under Toxic, and mis-read trainer-item data.
- Preserve the scoring bugs rather than substituting a smarter policy.

---

## 10. DV/stat inheritance quirks

**Priority:** HIGH

**Description**

Gen II breeding does not inherit all DVs. Instead, it inherits only specific bits from one parent, and compatibility is itself reduced by certain DV matches.

**Root cause**

- Compatibility is reduced to zero if Defense DVs match and the low 3 bits of Special DVs match (`engine/pokemon/breeding.asm:87-103`).
- Breeding picks the mother/non-Ditto parent into `wBreedMotherOrNonDitto` (`engine/events/daycare.asm:553-600`).
- Egg DVs are initially randomized, then only the low nibble of the first DV byte and the low 3 bits of the second DV byte are overwritten from the chosen parent (`engine/events/daycare.asm:639-692`). In practice, that means inherited Defense + Special bits, while other DV components remain random.

**Subsystems involved**

- Breeding compatibility
- Parent-role selection (mother / non-Ditto)
- Egg DV generation
- Hatch/shiny side effects downstream from DVs

**Regression test approach**

- Use fixed parents with known DVs and verify inherited Defense/Special bits exactly.
- Verify Attack/Speed remain randomized.
- Verify identical Defense + low-Special-bit parents become incompatible.

---

## 11. Type matchup errors

**Priority:** MEDIUM

**Description**

I did **not** find a clearly wrong player-facing type chart entry in this repo, but I *did* find a documented type-matchup misuse on the AI side.

**Root cause**

- The actual type matchup table appears canonical, including the split Foresight section (`data/types/type_matchups.asm:1-118`).
- `CheckTypeMatchup` is documented in-code as being misused by AI callers that assume setting `a` to the offensive type is sufficient, even though the routine overwrites `a` internally (`engine/battle/effect_commands.asm:1407-1413`).
- That means AI type evaluation can be wrong even when actual battle damage is correct.
- **UNCLEAR:** a distinct non-AI damage-calculation type bug is not evident from this repo scan.

**Subsystems involved**

- Type matchup table
- Type matchup evaluator
- AI type-evaluation callers

**Regression test approach**

- Keep the battle damage table canonical.
- Separately preserve the AI-side mismatch by testing AI move choice in states affected by the bad `CheckTypeMatchup` assumption.

---

## 12. Save corruption exploits

**Priority:** CRITICAL

**Description**

Several save-corruption vectors exist because the save system treats the active box differently from the checksummed main save and because some flows save even when a normal save file does not exist.

**Root cause**

- `sGameData`/`sChecksum` do not include the active box mirror `sBox` (`ram/sram.asm:93-109`).
- The main and backup load paths both restore `LoadBox` after checksum validation (`engine/menus/save.asm:538-559`).
- `HallOfFame` is explicitly marked as buggy because it calls `SaveGameData` even without a prior save file, which can corrupt PC boxes (`engine/events/halloffame.asm:17-27`).
- The box-change save ordering that causes Bad Clone is another save-corruption vector (cross-reference section 3).

**Subsystems involved**

- Save layout / checksum boundaries
- Active box mirror
- Hall of Fame save path
- Recovery from backup save

**Regression test approach**

- Test no-save Hall of Fame entry.
- Test interrupted save at multiple box-change phases.
- Verify active-box corruption is still possible when the checksummed save itself is considered valid.

---

## 13. Text buffer overflow

**Priority:** HIGH

**Description**

The text engine uses fixed buffers and streams characters without bounds checks. Long inserted names can overflow intended layouts.

**Root cause**

- Name/string buffers are fixed-size: `wMonOrItemNameBuffer` is 11 bytes, and `wStringBuffer1..4` are 19 bytes (`constants/text_constants.asm:1-8`; `constants/script_constants.asm:5-12`; `ram/wram.asm:1718-1725`).
- `PlaceString` keeps writing until `@` and does not perform bounds checks (`home/text.asm:156-167`).
- Text command substitutions like `<TARGET>`/`<USER>` ultimately call the same unbounded placement path (`home/text.asm:227-228,302-365`).
- The repo already documents one concrete manifestation: `PresentFailedText` overflows when the enemy name is long (`data/text/battle.asm:1074-1078`).
- Coin Case ACE is a sibling issue in the same fragile text subsystem, but caused by terminator misuse rather than name length.

**Subsystems involved**

- Fixed text/name buffers
- Text command substitution
- Textbox layout/placement

**Regression test approach**

- Reproduce `PresentFailedText` with a maximum-length target name and verify the original overflow/layout damage.
- Ensure text insertion is *not* auto-truncated or rewritten in compatibility mode.

---

## 14. Map connection bugs

**Priority:** HIGH

**Description**

Surfing directly across a map connection fails because the scripted Surf entry path bypasses the normal edge-of-map connection check.

**Root cause**

- Normal tile-event handling checks `CheckMovingOffEdgeOfMap` and returns `PLAYEREVENT_CONNECTION` when appropriate (`engine/overworld/events.asm:292-333`).
- `UsedSurfScript` is explicitly annotated as buggy and performs scripted movement with `SurfStartStep` + `applymovement` instead of going through the normal step/tile-event path (`engine/events/overworld.asm:393-407`).
- That means the first Surf step can cross a connection boundary without invoking the normal connection loader.

**Subsystems involved**

- Scripted overworld movement
- Tile-event pipeline
- Connection loading
- Surf state transitions

**Regression test approach**

- Place the player on shoreline tiles adjacent to a map connection.
- Trigger Surf and verify the translated engine reproduces the failure to load the new map on the scripted step.

---

## 15. RNG manipulation

**Priority:** CRITICAL

**Description**

The field RNG is simple and highly timing-sensitive, so frame delays/menu timing can manipulate outcomes reliably.

**Root cause**

- `Random` reads `rDIV` twice and adds/subtracts it into two HRAM bytes, `hRandomAdd` and `hRandomSub` (`home/random.asm:1-29`; `ram/hram.asm:154-155`).
- The same update is also performed automatically every VBlank (`home/vblank.asm:68-82`).
- `RandomRange` derives bounded results from `hRandomAdd` via rejection sampling (`home/random.asm:50-80`).
- Battle RNG is intentionally separated into `_BattleRandom` for sync (`home/random.asm:31-48`), so field manip and battle manip are related but not identical.

**Subsystems involved**

- Field RNG core
- VBlank timing
- Any gameplay logic consuming `Random` / `RandomRange`
- Separate battle PRNG

**Regression test approach**

- Drive the translated engine with a deterministic VBlank / `rDIV` schedule and verify byte-for-byte RNG outputs.
- Add timing tests showing that delaying by N frames changes field RNG outcomes in the same way as the original.

---

## Additional repo-documented glitches discovered during comment scan

These are not the main assigned fifteen, but the repo already marks them with `; BUG:` comments and they belong in a later regression backlog:

- **Hall of Fame without save file corrupts PC boxes** — `engine/events/halloffame.asm:17-27`
- **Lucky Number Show ignores inactive boxes 10-14** — `engine/events/lucky_number.asm:100-102`
- **Present text overflow** — `data/text/battle.asm:1074-1078`
- **Surf on top of NPCs** — `engine/events/overworld.asm:338-347`
- **Surf directly across connection fails to load new map** — `engine/events/overworld.asm:393-407`
- **Pokémon deposited in Day-Care might lose experience** — `engine/pokemon/move_mon.asm:882-898`
- **AI might use base reward value as an item** — `engine/battle/ai/items.asm:167-209`
- **AI Full Heal/Full Restore cleanup bugs** — `engine/battle/ai/items.asm:542-549,716-728`
- **Catch-rate formula breaks for max HP > 341** — `engine/items/item_effects.asm:309-360`
- **TryObjectEvent arbitrary code execution** — `engine/overworld/events.asm:542`
- **ScriptCall can overflow `wScriptStack`** — `engine/overworld/scripting.asm:1149`

## Comment scan notes

- `BUG:` comments are widespread and are a strong signal that current behavior is intentionally preserving original Gold/Silver bugs.
- I did **not** find any `TODO`/`FIXME` comments in the asm/md scan.
- `_DEBUG` sections exist, but they are debug-menu/debug-room code gates rather than glitch-preservation fixes (`engine/menus/main_menu.asm:13-15`; `ram/wram.asm:1022-1026`).
