# Pokémon Gold for F#/.NET — Completion Plan

> Replaces the former milestone and victory checklists.  
> Audit baseline: 2026-07-09.  
> Target: Pokémon Gold only. Silver support is outside this plan.

## Executive summary

The project has a substantial native F#/.NET engine, but it is not yet a demonstrated, 100%-completable game.

Estimated current position:

- Approximately **55% complete toward a 100%-able Pokémon Gold**.
- Approximately **65–70% of an ordinary route through Red is represented in code or isolated tests**.
- **Under 15% of that route is proven as one continuous fresh-save runtime playthrough**.
- The current desktop suite records **1,355 passing tests**, but this means implemented slices pass their tests—not that the game is completable.

The following foundations are real and worth preserving:

- Platform-independent F# game library and native MonoGame desktop host.
- Build-time generation of maps, scripts, encounters, trainers, species, moves, items, audio, and other data directly from repository source assets.
- No ROM dependency.
- Runtime rendering, movement, collision, connections, warps, objects, text, menus, saves, audio, battles, and scenes.
- A typed script VM with no generic `Unsupported` commands in generated map data.
- Script-level story gates from New Bark through Red.
- Broad battle move-effect conformance tests.
- Independent A1–A21 runtime leg tests for many route components.

The main blockers are integration failures:

1. Battle results are not propagated faithfully into persistent party state.
2. EXP, stat growth, move learning, evolution, and loss/blackout behavior are not route-ready.
3. All A1–A21 route legs depend heavily on debug warps or direct state mutation.
4. The only no-shortcuts runtime proof ends after receiving Elm’s starter.
5. Field moves mostly set variables instead of performing their complete map action.
6. The all-251 proof counts static or hypothetical sources that players cannot use.
8. Required script and side-system no-ops remain.
9. Presentation coverage is broad but frequently approximate.

Work must therefore proceed from battle integrity to exact overworld actions, then through one persistent route, playable acquisition channels, and release verification.

## Status definitions

- ✅ **DONE** — The user-facing outcome works through the real runtime and is protected by a test that would fail if it regressed.
- 🟡 **PARTIAL** — Useful implementation or component coverage exists, but the user-facing outcome is incomplete, approximate, or dependent on debug setup.
- ⬜ **TODO** — No acceptable player-facing implementation or proof exists.

A parser, pure helper, static graph, debug command, direct script invocation, injected event, injected badge, or debug warp does not by itself satisfy a player-facing story.

## Verification layers

All work must state which layer it proves:

1. **Data/parser tests** prove that source assets were translated correctly.
2. **Pure-system tests** prove isolated state transitions and formulas.
3. **Script-VM tests** prove script branching and effects without physical gameplay.
4. **Staged runtime tests** prove a local runtime interaction after explicit setup.
5. **Continuous runtime tests** prove reachability through real input from a prior checkpoint.
6. **Manual host verification** proves the shipped application is understandable and usable.

Layers 1–4 are valuable component coverage. Only layers 5 and 6 can close route or completion stories.

---

# Epic 0 — Preserve the working engine foundation

## Story 0.1 — The game builds without a ROM

**Status: ✅ DONE**

The engine consumes repository source assets and generated data without runtime or user-supplied ROM dependencies.

### Work items

- ✅ **FND-001 — Maintain the source-asset boundary.**
  - `PokeGold.DataGen` remains the only build-time generator of `Data/Generated/*`.
  - Handwritten runtime code must not be placed in generated files.
  - No ROM path, ROM checksum, emulator, or SRAM file becomes a runtime dependency.

- ✅ **FND-002 — Maintain targeted desktop builds.**
  - `dotnet build .\src\PokeGold.Game` succeeds.
  - `dotnet test .\tests\PokeGold.Tests` succeeds.
  - `dotnet build .\src\PokeGold.Host` succeeds.
  - Android remains separately gated and must not block desktop work.

## Story 0.2 — Repository data is available to the native runtime

**Status: ✅ DONE**

Maps, scripts, species, moves, encounters, trainers, items, marts, audio metadata, and related source data are generated into typed F# structures.

### Work items

- ✅ **FND-003 — Preserve generated map and script coverage.**
  - All generated map scripts continue to parse without generic `Unsupported` commands.
  - Command-count pins are updated only with an explanation tied to parser changes.

- ✅ **FND-004 — Expand schemas that discard gameplay data.**
  - Generated trainer records preserve party type, held item, all four explicit move slots, and class DVs from source.
  - Proved by `all source trainer records preserve their declared layout`.

## Story 0.3 — Core rendering, audio, input, and save infrastructure exists

**Status: ✅ COMPLETE**

The systems work, but full-game breadth and exact restoration are not yet proven.

### Work items

- ✅ **FND-005 — Preserve the native host and fixed-step game loop.**
- ✅ **FND-006 — Preserve map, text, window, menu, sprite, and battle rendering infrastructure.**
- ✅ **FND-007 — Preserve the four-channel synthesized audio path.**
- ✅ **FND-008 — Preserve versioned save migration and current player/world round trips.**
- ⬜ **FND-009 — Prove complete checkpoint restoration.**
  - Save and reload at representative points during Johto, the Elite Four, Kanto, and species-acquisition work.
  - Restore map, position, party identity, stats, HP, PP, held items, bag, money, storage, Pokédex, phone, daycare, roamers, scenes, events, engine flags, and persistent map changes.
  - Test names must identify the checkpoint restored.

### Epic 0 acceptance

Epic 0 is complete when all later epics can use generated source data, save state, rendering, input, and audio without adding a ROM dependency or bypassing the runtime.

---

# Epic 1 — Battle and progression integrity

This epic is the first critical-path blocker. Route extension beyond early Johto is not meaningful until real battles produce trustworthy persistent results.

**Status: ✅ COMPLETE**

## Story 1.1 — Trainers and wild encounters use authentic battle parties

**Status: ✅ COMPLETE**

Runtime trainer and wild construction uses source moves, held-item data, and preserved DVs/gender. Missing or invalid production battle staging now fails visibly; the old fixture is isolated behind explicit debug warps.

### Work items

- ✅ **BAT-001 — Preserve complete trainer party data.**
  - Extend generator and runtime schemas with trainer type, moves, held items, and DVs where supplied.
  - Read `data/trainers/parties.asm` and corresponding trainer-loading assembly first.
  - Acceptance: Falkner, Whitney, Lance, and Red runtime parties exactly match source species, levels, items, and explicit moves.
  - Proved by `BAT-001 runtime trainer parties match source moves and held items` and `BAT-001 runtime item trainer preserves held item`.

- ✅ **BAT-002 — Derive moves for normal trainer parties.**
  - For `TRAINERTYPE_NORMAL` and item-only records, derive the correct level-up moveset from generated evolution/learnset data.
  - Acceptance: tests cover a normal trainer, a moves trainer, an item trainer, and an item-plus-moves trainer.
  - Proved by `BAT-002 runtime derives normal and item moves while preserving explicit moves`, `BAT-002 source starting moves skip duplicates and retain the latest four`, and the synthetic `BAT-002 synthetic item plus moves trainer keeps explicit source slots` because Gold has no `TRAINERTYPE_ITEM_MOVES` record.

- ✅ **BAT-003 — Give wild Pokémon authentic moves and generated attributes.**
  - Wild Pokémon receive their source-appropriate level-up moves, DVs, held item chances, and encounter attributes.
  - Acceptance: a deterministic runtime encounter asserts the complete constructed opponent.
  - Proved by `BAT-003 Route 2 encounter constructs a complete source wild opponent`, the BAT-003 item/DV/gender boundary tests, and the runtime Master Ball catch preserving DVs.

- ✅ **BAT-004 — Remove fallback battle Pokémon from normal gameplay.**
  - The emergency Cyndaquil/Pidgey/Tackle fallbacks may remain assertion failures or debug-only fixtures.
  - A missing staged party in a production script must fail a test instead of silently creating a substitute.
  - Proved by `BAT-004 production battles reject missing or invalid staged combatants` and `all generated loadwildmon operands resolve exactly`.

## Story 1.2 — Persistent party members enter and leave battle without identity loss

**Status: ✅ COMPLETE**

Stable GUID identity synchronizes duplicate species/level party members exactly and survives reordering, boxing, saving, and legacy-save migration. Packed DVs and five-field stat experience calculate all six stats exactly. Canonical moves/PP, items, status including sleep count, EXP/stat-EXP carriers, level, and friendship now round-trip by identity while Transform/Mimic state is discarded and Sketch persists.

### Work items

- ✅ **BAT-005 — Introduce stable party identity.**
  - Each persistent Pokémon receives a stable identifier that survives party reordering, boxing, saving, and battle conversion.
  - Acceptance: two same-species, same-level party members leave battle with the correct individual HP, PP, status, held item, EXP, and moves.
  - Proved by `BAT-005 duplicate party members keep individual battle state`, the conversion/reorder/boxing assertions, and `v6 Pokemon without identity migrate uniquely across persistent storage`.

- ✅ **BAT-006 — Model all persistent battle stats correctly.**
  - Replace DV-zero/stat-exp-zero derivation with Gen 2 per-stat calculations.
  - Replace scalar `StatExp` if necessary with the actual per-stat representation.
  - Preserve Attack, Defense, Speed, and Special DVs and HP DV derivation.
  - Acceptance: worked source-based examples cover all six stats and level-up stat changes.
  - Proved by `BAT-006 packed DVs and five stat experience words determine all six stats`, stat-exp rounding boundaries, save round-trip coverage, and v7 scalar migration.

- ✅ **BAT-007 — Round-trip complete battle state.**
  - Synchronize current HP, status, PP, held-item changes, transformed/copied move cleanup, EXP, level, stats, and friendship as applicable.
  - Acceptance: switching and fainting multiple same-species party members cannot corrupt another member.
  - Proved by `BAT-007 switching and fainting identical members round-trips by identity`, `BAT-007 temporary copied moves clean up while Sketch persists`, and the persistent sleep/friendship conversion test. Per-defeat award policy remains correctly scoped to BAT-008–BAT-010.

## Story 1.3 — Multi-Pokémon trainer battles award correct progression

**Status: ✅ COMPLETE**

Multi-mon battles emit ordered per-defeat events and now consume them into the persistent party with source-ordered participant and EXP Share division, modifier rounding, stat EXP, Pokérus doubling, and saturation. Crossed-level side effects remain scoped to BAT-010.

### Work items

- ✅ **BAT-008 — Emit per-defeat progression events from the battle engine.**
  - Record each defeated enemy, participants, EXP Share participation, trainer modifier, stat EXP, and relevant rewards.
  - Do not recompute the battle history from staged script data after the battle.
  - Proved by `BAT-008 records exact ordered progression events for every enemy defeat`, including two trainer defeats across switches, independent overlapping EXP Share membership, exact stat yields, wild/trainer modifiers, event order, and terminal de-duplication.

- ✅ **BAT-009 — Award EXP and stat EXP per defeated enemy.**
  - Each eligible persistent party member receives the correct share.
  - Acceptance: a multi-mon trainer battle with switches asserts exact EXP for every participant.
  - Proved by `defeat events distribute exact EXP and stat EXP across overlapping pools` and the runtime identity round-trip test, covering ordered multi-defeat awards, overlapping participant/EXP Share membership, per-stage flooring, trainer/traded/Lucky Egg boosts, Pokérus, and untouched nonrecipients.

- ✅ **BAT-010 — Process every crossed level.**
  - A large EXP award must process stats, moves, and evolutions for each intermediate level rather than only the final level.
  - Source conformance recalculates stats once at each tranche's final level, checks moves at every crossed level in ascending order, applies one level-up happiness change per level-changing tranche, and defers at-most-one evolution until victorious cleanup. Proved by `BAT-010 processes every crossed move level and evolves once after a win`, including the loss path retaining earned progression without evolving.

- ✅ **BAT-011 — Award money and post-battle rewards exactly once.**
  - Trainer payout uses the correct trainer class/base reward and final enemy level.
  - Amulet Coin applies under source-accurate conditions.
  - Losses, catches, and script retries cannot duplicate rewards.
  - Proved by the exact runtime trainer settlement test plus `BAT-011 Amulet Coin activates only after its holder is sent out and stays active` and `BAT-011 Pay Day records level-scaled coins once per successful use`. Settlement uses the terminal battle state, the source four-quarter prize formula and final defeated level, sticky sent-out Amulet Coin activation, winning-only Pay Day, capped wallet addition, and a one-shot terminal callback; wild catches receive no trainer prize and running is not treated as a loss.

## Story 1.4 — Move learning and evolution are player-controlled and source-correct

**Status: ✅ COMPLETE**

Level-up move learning preserves source order and suspends battle-script resumption for explicit replace/decline decisions. All 50 TMs and seven HMs are generated with source compatibility.

### Work items

- ✅ **BAT-012 — Add the four-move learning decision flow.**
  - When a Pokémon knows four moves, show the real learn/decline/delete decision.
  - Cancellation preserves the old moveset.
  - HM deletion remains restricted to the Move Deleter where required.
  - Proved by `BAT-012 queues full-set move decisions in source order without replacing` and `LearnMoveSceneTests`: requests retain stable party identity and level/learnset order, free slots receive full base PP, explicit replacement preserves the chosen slot order, two-stage cancellation preserves all moves/PP, HM choices are rejected, callbacks are one-shot, and the suspended `StartBattle` script resumes only after the ordered queue drains.

- ✅ **BAT-013 — Support all TM01–TM50 and HM01–HM07.**
  - Generate the TM/HM mapping rather than maintaining a partial handwritten match.
  - Enforce species compatibility.
  - Consume TMs and preserve HMs according to Gold behavior.
  - Acceptance: tests cover TM01, TM10, TM50, an HM, compatible and incompatible species, cancellation, and a full moveset.
  - Proved by BAT-013 `TmHmTests`, `PackUseGiveTests`, and the shared BAT-012 decision-scene tests. DataGen emits the exact 57-entry source mapping and all 251 species compatibility sets; boundary TMs/HMs, compatible/incompatible and already-known outcomes, chosen-slot replacement/full PP, cancellation preservation, successful-TM consumption, and reusable HMs are covered.

- ✅ **BAT-014 — Complete all evolution methods.**
  - Level, item, friendship, time-of-day, stat comparison, Tyrogue branches, trade, and trade-with-item paths must work.
  - Evolution cancellation and move learning after evolution must follow source ordering.
  - Proved by BAT-014 `EvolutionTests`, `EvolutionSceneTests`, `TradingTests`, `PackUseGiveTests`, and crossed-level progression coverage. The ordered source matcher covers all five encoded methods, friendship/time thresholds, calculated Tyrogue stats, Everstone, stone branches, plain/item trade and Time Capsule restrictions; battle and stone evolution suspend for explicit acceptance/cancellation, attempt-time catalysts follow ROM consumption timing, and evolved-species current-level move decisions follow acceptance.

- ✅ **BAT-015 — Update owned/seen Pokédex state after evolution.**
  - The evolved species is registered without losing the prior species.
  - Proved by `BAT-015 accepted evolution retains prior dex entry and registers target`; both battle-accepted and stone-accepted evolution callbacks add the target to `DexSeen` and `DexOwn` while preserving the source species.

## Story 1.5 — Losing a battle causes a real blackout

**Status: ✅ COMPLETE**

Defeat now has an explicit result, source-defined blackout transition, and aborts the suspended post-battle continuation. Real generated boss scripts prove that loss cannot execute victory-only mutations, and a loss-save-reload-retry cycle preserves trainer availability until a legitimate later victory.

### Work items

- ✅ **BAT-016 — Add an explicit defeated battle result.**
  - A loss must not be represented as an ordinary script result that resumes post-victory commands.
  - Proved by `BAT-016 battle outcomes map to explicit script results` and `BAT-016 defeated battle aborts the suspended script continuation`; victory resumes with the source `wBattleResult = 0`, while defeat has an explicit result and discards the suspended post-battle continuation.

- ✅ **BAT-017 — Implement blackout destination and abort semantics.**
  - Source-defined spawn data resolves a valid `blackoutmod` map and falls back to `PLAYERS_HOUSE_2F` at `(3, 3)` when it is invalid. Defeat heals party HP, status, and PP; floors money-halving; clears transient battle/script state; warps; and aborts the defeated script’s normal continuation.
  - Proved by `BAT-017 defeat applies source blackout spawn and aborts continuation`, a runtime theory covering `CHERRYGROVE_CITY` at `(29, 4)`, the home fallback, odd-money flooring, party healing, and stable post-loss idle behavior.

- ✅ **BAT-018 — Protect every boss script from loss-as-victory.**
  - Real generated-map battle transitions with a level-2 Magikarp using Splash record actual losses, blackout to `PLAYERS_HOUSE_2F` at `(3, 3)`, and abort the victory continuation. Falkner grants no Zephyr Badge or TM31; Lance does not enter the Hall of Fame; and Red remains present without credits.
  - Proved by `BAT-018 Falkner loss blackouts without ZephyrBadge or TM31`, `BAT-018 Lance loss blackouts before Hall of Fame`, and `BAT-018 Red loss blackouts without removal or credits`.

- ✅ **BAT-019 — Prove retry behavior.**
  - A real Falkner loss blackouts, saves only after the runtime is capturable, reloads in a new runtime, retains Falkner and no beaten/badge/TM state, then retries with a real victory. The isolated retry setup now uses a legal level-100 Mewtwo with naturally learned `PSYCHIC_M`, normal base PP, and derived source stats; the component seam rejects levels outside 1–100 and incompatible moves.
  - Proved by `BAT-019 Falkner loss save reload and retry awards progression once` and `battle test setup accepts only legal level and source-compatible moves`.

## Story 1.6 — The battle command shell supports a normal playthrough

**Status: ✅ COMPLETE**

FIGHT/PKMN/PACK/RUN, switching, basic items, capture, trainer ball rejection, and broad move-effect behavior exist.

### Work items

- ✅ **BAT-020 — Preserve audited move-effect coverage.**
  - All generated move effects currently have explicit ledger classification and worked tests.
  - Do not restart this audit unless route failures expose integration problems.

- ✅ **BAT-021 — Complete forced-switch and replacement timing.**
  - A fainted active player Pokémon now remains pending until a legal player-selected replacement; fainted targets are rejected, no newly sent-out monster acts in the fainting turn, and simultaneous faints select the player replacement before automatically sending the next enemy. Repeated multi-mon cycles retain persistent party identity, HP, and PP.
  - Proved by `BAT-021 trainer faint waits for a player replacement before another action`, `BAT-021 enemy replacement waits until the next turn to act`, `BAT-021 forced replacement rejects fainted targets until a legal party choice`, `BAT-021 simultaneous faint chooses the player replacement before enemy replacement`, `BAT-021 multi-mon trainer battle repeats forced player and enemy replacements`, and `BAT-021 real Falkner battle requires replacement before repeated trainer cycles`.

- ✅ **BAT-022 — Complete battle item coverage.**
  - `BuildBattle` retains every persistent party member while selecting the first conscious member as active, so fainted reserves are visible to the real battle PACK target menu without becoming active battlers. Revive/Max Revive, all source status berries, Bitter Berry, MiracleBerry, EnergyPowder, Energy Root, Heal Powder, Revival Herb, MysteryBerry, HP/status/PP recovery, X-items, Guard Spec., Dire Hit, and Poké Doll now use source target, amount, rejection, consumption, and turn semantics. Bitter medicines apply their source friendship penalty only after a successful effect and persist it through battle synchronization.
  - The source-menu guard verifies every generated `ITEMMENU_PARTY`/`ITEMMENU_CLOSE` item-pocket entry has a battle transition. Real staged input tests prove Revive identity/status/PP/bag synchronization after victory, Max Revive full recovery, status cure, direct X-item turn consumption, Ether PP recovery, and trainer Poké Doll rejection.
  - Proved by `BAT-022 runtime Revive targets fainted bench consumes a turn and synchronizes identity`, `BAT-022 runtime Max Revive targets a fainted bench at full HP`, `BAT-022 runtime Pack status and direct items consume a turn and persist`, `BAT-022 runtime trainer battle rejects Poke Doll without consuming a turn`, `BAT-022 source berries bitter medicine and Revival Herb preserve battle item semantics`, `BAT-022 every source battle-menu item has a supported battle transition`, `BAT-022 Ether battle use persists PP and bag state after runtime victory`, and the existing item transition tests.

- ✅ **BAT-023 — Complete held-item integration.**
  - Consumable held healing/status/PP effects activate once, remain unconsumed when a holder faints before residual handling, and do not resurrect through switching, capture cleanup, battle synchronization, or save/reload. Nonconsumables retain their item state across turns and cleanup; existing source-effect tests cover type damage, priority, critical, survival, PP, and status behavior. Source held stat-up entries are explicitly unused by the original battle core.
  - Proved by `BAT-023 fainted Berry holder retains item and Leftovers remains held across turns` and `BAT-023 Berry consumption persists through runtime switch capture cleanup and save reload`, together with existing `type boosting held item increases matching move damage`, `Quick Claw lets a slower holder move first on a successful roll`, `Focus Band can leave the holder at 1 HP against lethal damage`, `MysteryBerry restores PP when a move reaches zero`, and status-berry tests.

- ✅ **BAT-024 — Audit trainer AI at the integration layer.**
  - Generated trainer-class profiles now drive source-directional $20$-base move scores. All generated flags execute explicit ASM-backed layers: `AI_BASIC`, `AI_SETUP`, `AI_TYPES`, `AI_OFFENSIVE`, `AI_SMART`, `AI_OPPORTUNIST`, `AI_AGGRESSIVE`, `AI_CAUTIOUS`, `AI_STATUS`, and `AI_RISKY`. Legal PP/Disable filtering, randomized minimum-score tie selection, profile-specific switch rates, trapped/lock-in switch restrictions, highest-level trainer-item eligibility, context/probability gates, and the source Full Heal confusion quirk use the seeded battle RNG.
  - The old deterministic high-score heuristic has been removed. Source AI decisions consume and persist battle RNG state through switch, item, and move selection. Branch tests cover unique rolling player-move history (including Bide, charge, and recharge locks), defensive/offensive switch candidate masks and matchup categories, Perish fallback, inverse tier-$30$ probability branches, Rain Dance against Fire, Sunny Day against Water, and both outcomes of the X-item second roll. Real generated Falkner, Will, Lance, and Red profiles remain covered with legal player-party fixtures; Will’s five generated party members are defeated through real battle turns, allowing source switching and preserving all progression events.
  - Proved by `BAT-024 source switch scoring distinguishes neutral from not very effective offense`, `BAT-024 source switch candidate masks reject weak benches and prefer super effective offense`, `BAT-024 source switch tier uses the selected candidate matchup score`, `BAT-024 source tier 30 switch policies use the inverse cutoff branch`, `BAT-024 Smart weather strongly favors the source good opponent type on first turn`, `BAT-024 source trainer item context uses seeded thresholds and preserves Full Heal confusion`, `BAT-024 player used move history is unique rolling and resets on replacement`, `BAT-024 locked Bide records the executed move instead of later selections`, and `BAT-024 charge and recharge history records only source-equivalent executed moves`. The generated-profile/Falkner/Will/Lance/Red integration tests cover the broader runtime paths.

### Epic 1 acceptance

Epic 1 is complete when a fresh party can fight and legitimately defeat representative wild encounters, ordinary trainers, a gym leader, a multi-mon Elite Four trainer, and Red while preserving exact party identity and progression. Losses must blackout and never execute victory-only script commands.

**Acceptance matrix: ✅** `EPIC1 matrix legal wild and ordinary trainer victories synchronize persistent battle state`, `EPIC1 matrix legal Will runtime victory synchronizes all five generated defeats`, `EPIC1 matrix legal Red runtime victory synchronizes generated six member battle`, `BAT-021 real Falkner battle requires replacement before repeated trainer cycles`, `A21 Silver Cave warps reach Red and credits roll after battle`, the BAT-018 boss-loss blackout tests, and `BAT-019 Falkner loss save reload and retry awards progression once` use legal levels, source-compatible moves, normal PP, and real battle transitions. They prove wild, ordinary trainer, gym leader, multi-mon Elite Four, and Red component paths, identity/HP/status/PP/held-item synchronization, EXP/stat-EXP/money settlement, victory-only rewards, blackout, and retry persistence. Direct map and actor setup is isolated component staging; the continuous fresh-save route remains the separate Epic 3/release gate.

---

# Epic 2 — Overworld action and script integrity

## Story 2.1 — Players can traverse generated maps through real movement

**Status: 🟡 PARTIAL**

Basic walking, collision, warps, connections, ledges, objects, trainer sight, and ice movement exist, but route-wide proof is incomplete.

### Work items

- ✅ **OVR-001 — Preserve ordinary walking, collision, warps, and connections.**
- ✅ **OVR-002 — Preserve object visibility, A-press dispatch, trainer sight, and coordinate-trigger scheduling.**
- 🟡 **OVR-003 — Verify special collision families across representative maps.**
  - Ice, currents, conveyors, directional walls, pits, warp carpets, stairs, doors, ladders, and water features each require runtime tests using real map cells.
  - Replace Azalea-only representative coverage with a matrix across indoor, outdoor, cave, water, ice, and Kanto maps.

## Story 2.2 — Every route-required field move performs its complete action

**Status: 🟡 PARTIAL**

Badge, move, and some terrain checks exist. Most actions set world variables rather than completing the source map behavior.

### Work items

- ⬜ **OVR-004 — Complete Cut.**
  - Cut removes the correct tree/grass obstruction and makes the route walkable.
  - Persistence follows source event/map reload behavior.
  - Prove on Ilex Forest/Route 34 and at least one second map.

- ⬜ **OVR-005 — Complete Surf.**
  - Enter surfing state, render the proper player state, traverse water, block invalid terrain, and dismount on valid land.
  - Save/reload while surfing must restore a legal state.

- 🟡 **OVR-006 — Complete Strength.**
  - Existing boulder pushing and selected Ice Path hole handling are retained.
  - Generalize hole/event handling from map-specific coordinate matches to source-generated behavior.
  - Prove Ice Path and a non-Ice-Path Strength puzzle.

- ✅ **OVR-007 — Complete Whirlpool.**
  - Source block `$07` is replaced by passable water block `$36`; surf state is preserved and real-map tests prove traversal in Dragon’s Den B1F and Route 41.
  - Missing Glacier Badge or a party Pokémon with Whirlpool leaves the obstruction intact and non-traversable.

- ⬜ **OVR-008 — Complete Waterfall.**
  - Implement required facing, ascent/descent, forced movement, animation state, and landing behavior.

- ⬜ **OVR-009 — Complete Fly.**
  - Present discovered destinations, move to the correct destination warp, and preserve state.
  - A `__fly_requested` variable is not completion.

- 🟡 **OVR-010 — Complete Flash.**
  - Existing eligibility state must control actual cave darkness rendering and survive map transitions as the source requires.

- ⬜ **OVR-011 — Add runtime Headbutt.**
  - The script command currently pauses only.
  - Select encounters from generated tree tables for the current map/tree group and start a catchable battle.

## Story 2.3 — Required script commands have real semantics

**Status: 🟡 PARTIAL**

Generated commands are typed, but many are approximate and several required commands are no-ops or fixed dummy results.

### Work items

- ⬜ **SCR-001 — Fix battle-script control flow.**
  - `startbattle`, `reloadmapafterbattle`, `checkjustbattled`, and `endifjustbattled` must use explicit win/loss/catch/run state.
  - Acceptance is covered jointly with BAT-016 through BAT-019.

- ✅ **SCR-002 — Implement route-relevant command stubs.**
  - `prompt` and `text_ram` are source text directives, not script opcodes; the parser no longer emits phantom script commands for them, while `MapText`, `TextBox`, and runtime buffer substitution preserve their behavior.
  - `catchtutorial` preserves its battle-type operand and runs the Route 29 Dude's automated ordinary battle flow with a temporary level-5 Rattata, staged wild opponent, and one Poké Ball without mutating the real player.

- ⬜ **SCR-003 — Implement required side-system command stubs.**
  - ✅ `Itemnotify` renders the source item and pocket names from the current `giveitem`, waits for dismissal, and resumes the script.
  - Audit and implement or accurately exclude:
    - `Pokepic`, `Closepokepic`
    - `Elevator`, `Elevfloor`
    - `Trade`
    - `Givepokemail`, `Checkpokemail`
    - `Writeobjectxy`, `Ugdoor`, `Warpcheck`
    - `Checkjustbattled`
    - `ConditionalEvent`, `Describedecoration`, `Stonetable`
    - `Cmdqueue`, `Writecmdqueue`
  - No item may remain tagged `RequiredFor100Percent` and `StubNoOp` at release.

- ⬜ **SCR-004 — Add command-level conformance tests for approximate critical commands.**
  - Prioritize map callbacks, object events, scene changes, changeblock, door state, follow/movement, money/items, and battle staging.
  - Each ledger upgrade names its proving test.

## Story 2.4 — Required specials and side systems are player-facing

**Status: 🟡 PARTIAL**

Many specials have useful seams, but some are simplified or skipped.

### Work items

- 🟡 **SCR-005 — Finish daycare behavior.**
  - Replace unconditional compatibility.
  - Implement egg groups, gender rules, Ditto rules, species inheritance, move inheritance, hatch cycles, and parent growth.
  - Advance egg generation through runtime steps.

- ⬜ **SCR-006 — Implement egg hatching.**
  - Eggs count down while walking, hatch through a runtime scene, become usable Pokémon, and update the Pokédex.
  - Saving and loading preserves remaining cycles.

- 🟡 **SCR-007 — Finish the Bug-Catching Contest.**
  - Replace deterministic/minimal contest behavior with a playable timed or explicitly approved equivalent loop.
  - Park Balls, temporary party handling, catch selection, judging, prizes, and save state must compose correctly.

- 🟡 **SCR-008 — Finish Game Corner acquisition paths.**
  - The player can earn or acquire coins and exchange them for prize Pokémon through the runtime.
  - Pure helpers or directly seeded coins do not count.

- ⬜ **SCR-009 — Burn down non-link completion-special stubs.**
  - ✅ `DaisysGrooming` routes through party selection and applies the source grooming probability/friendship tiers with Blues House runtime coverage.
  - ✅ `ProfOaksPCBoot` counts live seen/owned species, preserves all 19 source rating thresholds and messages, plays the matching fanfare, and resumes the real Oak's Lab script.
  - Triage and implement the remaining required behavior for Lucky Number, happiness checks, Shuckle ownership/return, Trainer House, Mystery Gift replacement policy, decorations, Diploma, and related normal-play specials.
  - Link-only cable-club behavior remains excluded.

- ⬜ **SCR-010 — Define deliberate replacements for unavailable external services.**
  - Mystery Gift, Mobile/event distribution, and version trading need documented offline equivalents where required for completion.
  - The replacement must be accessible through gameplay and must not silently grant completion state.

## Story 2.5 — Menus and inventory expose all required player actions

**Status: 🟡 PARTIAL**

The menu framework is broad, but several actions are missing or simplified.

### Work items

- ⬜ **UI-001 — Wire fishing rods from Pack to overworld.**
  - `PackScene` returns the selected rod.
  - The overworld validates facing water, chooses from generated fish-group data, and starts a battle.
  - The existing hard-coded helper is replaced or restricted to tests.

- ⬜ **UI-002 — Complete item field-use behavior.**
  - Stones, Rare Candy, vitamins, PP items, Escape Rope, Repels, evolutionary items, key items, rods, bicycles, and TM/HM use have runtime outcomes.

- 🟡 **UI-003 — Complete party, storage, mail, and PC workflows.**
  - Deposit, withdraw, move, release, held items, mail, full boxes, and last-usable-party constraints work through menus and save/load.

### Epic 2 acceptance

Epic 2 is complete when every action required by the continuous route and the 251-species acquisition plan is performed through ordinary menus, movement, map interactions, and source-backed script semantics.

---

# Epic 3 — Continuous fresh-save route through Red

## Route proof policy

The existing A1–A21 tests are retained as staged component tests. They contain approximately:

- 111 debug warps.
- 40 direct event mutations.
- 12 direct badge/engine-flag mutations.
- 14 direct scene mutations.

Those tests remain useful for fast diagnosis, but their checklist status does not mean the corresponding story is playable.

Every route story below has two proof obligations:

1. Preserve its independent staged component tests.
2. Add its checkpoint to one persistent `StartNewGame` run using only normal input and state earned by earlier checkpoints.

The continuous route may use deterministic controller automation and accelerated text, but it may not call debug warp, set-event, set-flag, set-scene, seed-party, seed-item, or auto-win controls.

## Story 3.1 — Route harness supports long-lived checkpoints

**Status: 🟡 PARTIAL**

A no-shortcuts runtime test reaches Elm’s starter; the remaining route uses staged tests.

### Work items

- ✅ **RTE-001 — Preserve `GameDriver` and per-frame invariants.**
- ✅ **RTE-002 — Preserve the opening no-shortcuts proof through Elm’s starter.**
- ⬜ **RTE-003 — Add durable route checkpoint helpers.**
  - Checkpoints are ordinary save files captured from the continuous run, not constructed state.
  - Each later test may load the prior earned checkpoint to control test duration.
  - A separate chaining test verifies checkpoint ancestry and hashes.

- ⬜ **RTE-004 — Remove false completion labels.**
  - A1–A21 are marked PARTIAL until their continuous checkpoint passes.
  - Script-VM gates are described as script coverage, not victory proof.

## Johto stories

### Story 3.2 — A1: New Bark to Cherrygrove is continuously playable

**Status: 🟡 PARTIAL**

- ✅ Staged movement, home warp, New Bark triggers, and connections have coverage.
- ✅ The continuous prefix reaches the starter.
- ⬜ Walk from the earned starter state through Route 29 into Cherrygrove without debug setup.
- ⬜ Assert map transitions, collisions, encounters, party state, and save/reload.

### Story 3.3 — A2: Cherrygrove to Violet is continuously playable

**Status: 🟡 PARTIAL**

- ✅ Mr. Pokémon and rival script/component coverage exists.
- ⬜ Walk Route 30/31, complete Mr. Pokémon’s visit, return, legitimately battle the rival, and reach Violet.
- ⬜ The rival must use the correct starter and authentic moves; losing must blackout.

### Story 3.4 — A3: Falkner can be defeated for the Zephyr Badge

**Status: 🟡 PARTIAL**

- ✅ Gym warp, trainer sight, and boss-script components exist.
- ⬜ Defeat gym trainers and Falkner using the persistent party.
- ⬜ Assert the badge and TM31 were absent before victory and granted exactly once afterward.
- ⬜ Add a loss test proving no badge, TM, beaten event, or post-battle script continuation.

### Story 3.5 — A4: Route 32 and Union Cave lead to Azalea

**Status: 🟡 PARTIAL**

- ✅ Route, cave, connection, and Slowpoke Well blocker components exist.
- ⬜ Continue from the Falkner checkpoint through Route 32, Union Cave, Route 33, and Azalea.
- ⬜ Prove required encounter, darkness, warp, and blocker behavior without injected flags.

### Story 3.6 — A5: Slowpoke Well, Bugsy, and the rival are completable

**Status: 🟡 PARTIAL**

- ✅ Script and staged runtime coverage exists for the Azalea arc.
- ⬜ Clear Slowpoke Well through real battles and observe the town transition.
- ⬜ Defeat Bugsy for Hive Badge and TM49.
- ⬜ Defeat the Ilex rival encounter; losses must not advance any branch.

### Story 3.7 — A6: Ilex Forest and Cut open the Goldenrod route

**Status: 🟡 PARTIAL**

- ✅ Farfetch’d/HM script and Cut eligibility components exist.
- ⬜ Complete the Farfetch’d puzzle through movement.
- ⬜ Receive and teach Cut through normal menus.
- ⬜ Cut the real obstruction and walk through the newly opened route.

### Story 3.8 — A7: Whitney and SquirtBottle progression work

**Status: 🟡 PARTIAL**

- ✅ Gym/script component coverage exists.
- ⬜ Reach Goldenrod, defeat Whitney, complete her delayed badge conversation, and receive Plain Badge/TM45.
- ⬜ Obtain the SquirtBottle through its actual prerequisites.
- ⬜ Save/reload between the gym and flower shop without losing progression.

### Story 3.9 — A8: Sudowoodo and the Burned Tower arc work

**Status: 🟡 PARTIAL**

- ✅ Sudowoodo, rival, and beast-release script coverage exists.
- ⬜ Use the SquirtBottle, win or capture the real Sudowoodo, and open Route 36.
- ⬜ Complete the Burned Tower rival fight and release the beasts through movement.
- ⬜ Roamer state must be generated and persisted, not merely represented by an event flag.

### Story 3.10 — A9: Morty, Olivine, Surf, and Cianwood work

**Status: 🟡 PARTIAL**

- ✅ Staged Morty, lighthouse, and Surf-gate coverage exists.
- ⬜ Defeat Morty and receive Fog Badge.
- ⬜ Teach Surf and cross water using the completed surfing state.
- ⬜ Climb the lighthouse and reach Cianwood without debug transitions.

### Story 3.11 — A10: Chuck, Jasmine, and Lake of Rage work

**Status: 🟡 PARTIAL**

- ✅ Pharmacy, boss-script, and Red Gyarados components exist.
- ⬜ Obtain medicine, defeat Chuck, return to Jasmine, and defeat her.
- ⬜ Reach Lake of Rage and win or capture the forced Red Gyarados.
- ⬜ Continue into the Lance/Rocket arc with earned state only.

### Story 3.12 — A11: Rocket Hideout, Pryce, and Radio Tower work

**Status: 🟡 PARTIAL**

- ✅ Isolated guards, battles, scripts, and persistence checks exist.
- ⬜ Clear the hideout’s traps, switches, passwords, boss battles, and Electrode sequence.
- ⬜ Defeat Pryce and complete the Radio Tower takeover and Director rescue.
- ⬜ Save/reload at two points in the arc and resume correctly.

### Story 3.13 — A12: Ice Path, Clair, and Dragon’s Den work

**Status: 🟡 PARTIAL**

- ✅ Ice movement, selected boulder handling, gym scripts, and field-move eligibility have coverage.
- ⬜ Solve Ice Path and boulder-hole puzzles through movement.
- ⬜ Complete Whirlpool and Waterfall interactions required by the route.
- ⬜ Defeat Clair, complete Dragon’s Den, and receive Rising Badge through the source sequence.

### Story 3.14 — A13: The Pokémon League is reachable

**Status: 🟡 PARTIAL**

- ✅ Badge-guard and route components exist.
- ⬜ Travel from New Bark through Routes 27/26 and Victory Road.
- ⬜ Prove each badge guard rejects insufficient progress and accepts the earned eight badges.
- ⬜ Reach Indigo Plateau with no injected badge flags.

### Story 3.15 — A14: The Elite Four and Lance can be defeated

**Status: 🟡 PARTIAL**

- ✅ Room, door, Hall of Fame, and credit components exist.
- ⬜ Defeat Will, Koga, Bruno, Karen, and Lance sequentially with authentic teams.
- ⬜ Door locking, healing restrictions, EXP, money, party damage, and item use persist between fights.
- ⬜ A loss to any member blackouts and does not enter the Hall of Fame.
- ⬜ A legitimate Lance victory records the Hall of Fame, rolls credits, and creates the post-game save.

## Kanto stories

### Story 3.16 — A15: The S.S. Aqua reaches Kanto

**Status: 🟡 PARTIAL**

- ✅ Ticket, gangway, ship, and granddaughter components exist.
- ⬜ Obtain the S.S. Ticket from earned post-game state.
- ⬜ Board, complete the ship quest through movement, and arrive in Vermilion.
- ⬜ The sailor rejects the player before the ticket is earned.

### Story 3.17 — A16: Surge, Saffron, and Sabrina work

**Status: 🟡 PARTIAL**

- ✅ Gym and gate-script components exist.
- ⬜ Defeat Surge and Sabrina through real battles.
- ⬜ Prove Saffron gates reflect the actual Power Plant state without injected events.

### Story 3.18 — A17: The Machine Part quest works

**Status: 🟡 PARTIAL**

- ✅ Hidden Machine Part A-press dispatch has strong staged coverage.
- ⬜ Traverse Cerulean and Power Plant locations, confront the Rocket, find the hidden item, and return it.
- ⬜ Assert item consumption and power-restoration state across save/reload.

### Story 3.19 — A18: EXPN Card and Snorlax work

**Status: 🟡 PARTIAL**

- ✅ Radio tuning and Snorlax staged runtime behavior exist.
- ⬜ Obtain the EXPN Card through the continuous route.
- ⬜ Tune the Poké Flute through Pokégear and win or capture Snorlax.
- ⬜ Losing leaves Snorlax present; victory/capture removes it exactly once.

### Story 3.20 — A19: Brock, Erika, and Janine work

**Status: 🟡 PARTIAL**

- ✅ Isolated gym and map components exist.
- ⬜ Traverse Diglett’s Cave, Pewter, Celadon, and Fuchsia and legitimately win all three badges.

### Story 3.21 — A20: Blaine and Blue work

**Status: 🟡 PARTIAL**

- ✅ Cinnabar/Seafoam/Viridian scripts and Blue visibility components exist.
- ⬜ Defeat Blaine, trigger Blue’s return, and defeat Blue in Viridian.
- ⬜ Badge count reaches 16 only from real gym victories.

### Story 3.22 — A21: Red can be legitimately defeated

**Status: 🟡 PARTIAL**

- ✅ Oak, Mt. Silver guard, Red script, disappearance, and credits components exist.
- ⬜ Speak to Oak with 16 earned badges and unlock Mt. Silver.
- ⬜ Traverse Route 28 and Mt. Silver through normal movement.
- ⬜ Defeat Red’s authentic six-Pokémon team.
- ⬜ Losing to Red blackouts, leaves Red present, and never reaches credits.
- ⬜ Winning removes Red and reaches post-Red credits exactly once.

### Epic 3 acceptance

A deterministic test must start with `StartNewGame` and reach post-Red credits through earned checkpoints. The chain may reload saves produced by its own earlier checkpoints, but may not construct or mutate progression state. Every required boss battle must end in an actual win.

---

# Epic 4 — Playable acquisition of all 251 species

The current static obtainability graph is useful as an inventory, but it is not proof that a player can complete the Pokédex.

## Story 4.1 — Obtainability is tracked as executable acquisition channels

**Status: 🟡 PARTIAL**

### Work items

- ✅ **DEX-001 — Preserve the static source inventory.**
  - Rename it to make clear that it proves data coverage, not player obtainability.

- ⬜ **DEX-002 — Define one runtime acquisition recipe per species.**
  - Each species maps to a concrete channel: grass, water, fishing, headbutt, static battle, gift, in-game trade, evolution, breeding, roamer, contest, swarm, offline import, or built-in event.
  - The recipe names prerequisites, map, time, item, and predecessor species.

- ⬜ **DEX-003 — Reject inaccessible sources.**
  - A species counts only if its channel has a player-facing runtime test.
  - Hard-coded lists alone cannot satisfy the proof.

## Story 4.2 — Ordinary encounter channels are playable

**Status: 🟡 PARTIAL**

### Work items

- 🟡 **DEX-004 — Verify grass and surfing encounters.**
  - Generated time-of-day slots, encounter rates, Repel, held modifiers, and catches update party/storage/dex state.

- ⬜ **DEX-005 — Implement generated fishing encounters.**
  - Use the real fish groups, rod slots, time groups, and swarms.
  - Prove at least one species unique to fishing.

- ⬜ **DEX-006 — Implement headbutt encounters.**
  - Use generated tree groups and rare-tree behavior.
  - Prove Heracross or another headbutt-dependent species from a real tree.

- 🟡 **DEX-007 — Complete swarm behavior.**
  - Phone/event activation changes the appropriate grass or fishing tables and persists.

- 🟡 **DEX-008 — Complete roaming encounters.**
  - Raikou, Entei, and Suicune have persistent route, HP/status, movement, and capture state.
  - A static “roamer source” entry is insufficient.

## Story 4.3 — Gifts, static encounters, contests, and events are playable

**Status: 🟡 PARTIAL**

### Work items

- 🟡 **DEX-009 — Audit all gifts and static encounters.**
  - Each must be reachable, battle/capture or gift correctly, disappear once, and update owned state.

- 🟡 **DEX-010 — Complete Bug-Catching Contest acquisition.**
  - Scyther and Pinsir must be catchable through the contest runtime.

- ⬜ **DEX-011 — Provide playable Celebi and Mew policies.**
  - Implement the documented built-in event replacement through visible scripts and prerequisites.
  - A set containing `"CELEBI"` and `"MEW"` is not an acquisition path.

- 🟡 **DEX-012 — Verify Lugia, Ho-Oh, Red Gyarados, and other one-time encounters.**
  - Loss, run, capture, faint, and reload behavior must follow the chosen source-compatible policy.

## Story 4.4 — Breeding produces and hatches required species

**Status: ⬜ TODO**

### Work items

- ⬜ **DEX-013 — Implement authentic compatibility and inheritance.**
- ⬜ **DEX-014 — Generate eggs through runtime walking.**
- ⬜ **DEX-015 — Hatch eggs through runtime walking.**
- ⬜ **DEX-016 — Prove each baby-species family.**
  - Pichu, Cleffa, Igglybuff, Smoochum, Elekid, and Magby must be acquired in a runtime test from obtainable parents.

## Story 4.5 — Offline trading makes Gold self-completable

**Status: ⬜ TODO**

Pure trading helpers and an import catalog exist, but there is no playable terminal.

### Work items

- ⬜ **DEX-017 — Build a player-facing offline trade terminal.**
  - Accessible at a documented in-game location.
  - Supports local trade evolution and the approved import catalog.
  - Uses menus, confirmations, party/storage capacity rules, and save persistence.

- ⬜ **DEX-018 — Define the cost and unlock policy.**
  - Imports cannot be an unlabelled debug grant.
  - The policy must be deterministic, documented, and achievable in one save.

- ⬜ **DEX-019 — Complete trade evolutions.**
  - Kadabra, Machoke, Graveler, Haunter, Poliwhirl with King’s Rock, Slowpoke with King’s Rock, Onix with Metal Coat, Scyther with Metal Coat, Seadra with Dragon Scale, and Porygon with Up-Grade are covered.

- ⬜ **DEX-020 — Prove version-exclusive and legacy imports.**
  - Every catalog entry is acquired through the terminal and registered in party/storage/dex state.

## Story 4.6 — One long-lived save can own all 251 species

**Status: ⬜ TODO**

### Work items

- ⬜ **DEX-021 — Add per-channel runtime fixtures.**
  - Each acquisition mechanism has at least one real-runtime test.

- ⬜ **DEX-022 — Add the compositional completion save.**
  - Start from a real post-game save.
  - Execute or replay authenticated acquisition receipts for all channels.
  - End with 251 distinct valid species IDs in `DexOwn`.
  - Every owned entry corresponds to a Pokémon currently or historically acquired through the runtime.

- ⬜ **DEX-023 — Handle party and storage capacity.**
  - The player can store the complete living collection if that is the chosen bar, or the documentation explicitly defines owned-history completion.
  - Full boxes must never silently discard captures or gifts.

- ⬜ **DEX-024 — Present the completion outcome.**
  - Oak’s evaluation and Diploma work from the completed runtime save.

### Epic 4 acceptance

The static graph contains 251 recipes, every recipe’s acquisition channel has runtime proof, and one persistent save reaches `DexOwn.Count = 251` without debug mutation.

---

# Epic 5 — Completion content, presentation, and release quality

## Story 5.1 — No required gameplay surface is a silent no-op

**Status: ⬜ TODO**

### Work items

- ⬜ **REL-001 — Burn down the conformance ledger.**
  - No `RequiredFor100Percent` entry remains `Unknown` or `StubNoOp`.
  - `ImplementedApproximate` entries identify their exact divergence and have a test proving the approximation does not block completion.
  - Link-only entries remain clearly excluded.

- ⬜ **REL-002 — Audit fallback branches.**
  - Generic script-special fallback, dummy menu results, fallback Pokémon, and unknown labels must fail visibly in tests when reached by normal gameplay.

## Story 5.2 — Required graphics and feedback are visible

**Status: 🟡 PARTIAL**

### Work items

- ⬜ **REL-003 — Close overworld sprite gaps.**
  - Every sprite type reachable during the route or completion content renders a nonblank correct asset.
  - Add an automated inventory test and representative host captures.

- 🟡 **REL-004 — Replace generic battle-animation tints.**
  - Animation scripts are parsed, but the renderer currently reduces them to generic full-screen tints.
  - Implement enough animation primitives to visibly distinguish objects, movement, background effects, sound, waits, calls, and loops.
  - This is release quality rather than a route blocker unless an animation obscures control flow.

- 🟡 **REL-005 — Finish cries, fades, credits, and Pokédex presentation.**
  - Existing implementations are retained.
  - Verify each through the host and close reachable blank or misleading states.

- ⬜ **REL-006 — Ensure all required text and menus are understandable.**
  - No placeholder, internal constant, empty menu, or dummy choice appears on the golden route or acquisition paths.

## Story 5.3 — The release survives ordinary player variation

**Status: ⬜ TODO**

### Work items

- ⬜ **REL-007 — Test all three starters.**
  - Each starter reaches at least the first badge and can complete representative battles and evolution.

- ⬜ **REL-008 — Test losses and retries.**
  - Wild loss, ordinary trainer loss, gym loss, Elite Four loss, Red loss, and no-money loss.

- ⬜ **REL-009 — Test save/load interruption points.**
  - During scripts, between trainer battles, while surfing, during the Elite Four sequence, with daycare/roamers active, and with nearly full storage.

- ⬜ **REL-010 — Test menu cancellation and full-capacity cases.**
  - Full party, full box, full bag pocket, four moves, no usable Pokémon, no money, cancelled trade, cancelled evolution, and cancelled item use.

## Story 5.4 — A clean desktop release is demonstrably usable

**Status: ⬜ TODO**

### Work items

- ⬜ **REL-011 — Perform a manual fresh-save playthrough.**
  - A human completes the golden route through Red using the desktop host.
  - Every defect is recorded and either fixed or explicitly accepted.

- ⬜ **REL-012 — Perform a manual completion-channel pass.**
  - Exercise fishing, headbutt, breeding/hatching, contest, roamer, trade terminal, storage, and built-in events.

- ⬜ **REL-013 — Validate a clean-machine build.**
  - Document prerequisites and exact commands.
  - Build and run without an Android SDK and without a ROM.

- ⬜ **REL-014 — Align public documentation.**
  - Update `README.md`, `docs/status.md`, and this plan together.
  - State exactly what was proven, the current test command/count, supported platform, Gold-only scope, and known divergences.

- ⬜ **REL-015 — Record release evidence.**
  - Current screenshots and gameplay video show the native host, overworld, menus, battle, Hall of Fame/credits, and completion systems.

### Epic 5 acceptance

A clean desktop build can be installed, played, saved, resumed, completed through Red, and used to obtain all 251 species without blank required assets, silent no-ops, debug controls, or undocumented setup.

---

# Scale back and clean up

## 1. Keep Azalea coverage, remove Azalea as the architectural center

The original vertical slice left strong Azalea-specific residue:

- `loadAzalea`, `DebugLoadAzalea`, `LoadDebugAzalea`, and the `debug-azalea` command.
- Azalea-specific debug seed state.
- Many foundational tests use Azalea as their only map, connection, music, nurse, mart, object, text, or movement example.
- Some comments and type descriptions still describe later work as future “slices.”

Actions:

- 🟡 Keep the Azalea debug command as an explicitly development-only fixture.
- ⬜ Remove production fallbacks or defaults that implicitly choose Azalea.
- ⬜ Generalize representative tests into data-driven matrices:
  - Outdoor town: Azalea.
  - Early route: Route 29.
  - Interior: player house or Pokécenter.
  - Cave: Union Cave.
  - Ice: Ice Path.
  - Water: Route 40/Whirl Islands.
  - Kanto city/route: Saffron or Route 16.
- ⬜ Retain a smaller set of exact Azalea regression tests where the map genuinely exercises unique behavior.

## 2. Stop treating staged route legs as completed gameplay

The A1–A21 structure is useful and should remain. The problem is status, not test existence.

Actions:

- Rename or document staged tests as component tests.
- Remove completion checkmarks until the corresponding continuous checkpoint passes.
- Keep debug setup local to staged tests.
- Prevent debug controls from being referenced by the continuous route assembly.

## 3. Reclassify the all-251 graph as an inventory

`PokedexObtainability` currently mixes real generated encounters with hard-coded fishing, headbutt, roamer, offline import, and event source lists.

Actions:

- Keep it as a static coverage report.
- Rename its tests so they do not claim runtime obtainability.
- Add channel readiness to every source.
- Fail the true completion test when a recipe points to a channel without player-facing implementation.

## 4. Pause low-level battle polish until battle integration is sound

The move-effect audit is substantial and should be preserved. More isolated effect refinement has lower value than fixing opponent construction, party identity, progression, and blackout flow.

Actions:

- Freeze new effect micro-conformance work unless required by a failing authentic boss battle.
- Direct battle work first to BAT-001 through BAT-019.
- Reopen effect-level work from real route failures.

## 5. Remove duplicated or misleading state

Likely cleanup targets include:

- Badge count in `PlayerState` versus badge engine flags in the script world.
- Battle progression recomputed after the fact from staged trainer/wild data.
- Map-specific boulder-hole coordinate matches.
- Script result integers standing in for distinct outcomes such as win, loss, catch, run, and cancel.
- Species-and-level matching used as party identity.
- Handwritten TM tables when source-generated data exists.

Actions:

- Establish one authoritative representation for each fact.
- Derive display counts from authoritative flags where practical.
- Introduce typed outcomes instead of magic integers at subsystem boundaries.
- Keep migrations small and test-backed; do not combine this with a broad functional rewrite.

## 6. Retire obsolete milestone narration

The old plan repeatedly says all milestones landed while also describing required work as deferred.

Actions:

- Replace milestone completion claims with this epic/story/task plan.
- Archive superseded milestone prose if historical context is useful.
- Make `docs/status.md` report only currently passing enforced gates.
- Change the agent prompt from “first unchecked old row” to “first unblocked TODO on the critical path.”

---

# Critical path and dependency order

```text
Epic 0 data schema expansion
        |
        v
Epic 1 authentic battles and persistent progression
        |
        +--------------------+
        |                    |
        v                    v
Epic 2 exact field/script    Epic 4 acquisition-channel foundations
actions                      (fishing, breeding, trade terminal)
        |
        v
Epic 3 A1-A14 continuous Johto
        |
        v
Epic 3 A15-A21 continuous Kanto and Red
        |
        +--------------------+
        |                    |
        v                    v
Epic 4 all-251 save      Epic 5 presentation and variation QA
        |                    |
        +----------+---------+
                   |
                   v
             Release gate
```

Mandatory execution order:

1. Expand trainer and persistent Pokémon data.
2. Fix battle party construction, synchronization, EXP, move learning, evolution, and blackout.
3. Complete route-required field moves and script battle semantics.
4. Extend the continuous route one earned checkpoint at a time through Lance.
5. Continue through Kanto and Red.
6. In parallel after stable battle persistence, implement acquisition channels.
7. Produce the all-251 long-lived save.
8. Burn down required no-ops, close visual gaps, run manual QA, and package the desktop release.

An autonomous agent should select the first TODO whose dependencies are complete, read the cited assembly sources, add a failing test at the highest applicable verification layer, implement the smallest conformance fix, run the smallest relevant tests, then run the full desktop test suite.

---

# Final victory criteria

The project may claim “100%-completable Pokémon Gold” only when all of the following are true.

## Build and architecture

- `PokeGold.Game`, `PokeGold.Tests`, and `PokeGold.Host` build on a clean supported desktop environment.
- No ROM is required.
- Generated data remains separated from handwritten runtime code.
- Gold is the only claimed supported version.

## Continuous route

- One authenticated checkpoint chain begins with `StartNewGame`.
- It obtains all eight Johto badges, defeats the Elite Four and Lance, obtains all eight Kanto badges, unlocks Mt. Silver, defeats Red, and reaches post-Red credits.
- The chain uses real input, movement, collision, warps, scripts, menus, battles, and saves.
- It contains no debug warps, direct event mutations, direct flag mutations, scene mutation, party seeding, item seeding, or battle auto-win.
- Every boss battle is actually won.
- Per-frame runtime invariants hold throughout.

## Battle integrity

- Trainer and wild parties use authentic species, levels, moves, items, and relevant generated attributes.
- Persistent Pokémon retain correct individual identity.
- HP, status, PP, held items, EXP, levels, stats, moves, evolution, friendship, money, and Pokédex state survive battle and save/load correctly.
- Multi-Pokémon battles award progression per defeated opponent and participant.
- A loss causes blackout, aborts victory-only script execution, and permits a legitimate retry.
- Red cannot disappear or trigger credits after a loss.

## Overworld and scripts

- All route-required field moves complete their actual map action.
- Route-required objects, callbacks, triggers, map mutations, puzzles, and blockers work through gameplay.
- No reachable required script command or special silently no-ops.
- Save/load restores every persistent route state needed to continue.

## All 251 species

- Every species has a concrete runtime acquisition recipe.
- Grass, water, fishing, headbutt, swarm, roamer, contest, gift, static, evolution, breeding, trade, offline import, and built-in event channels are playable where used.
- Eggs generate and hatch through runtime steps.
- The offline terminal is player-facing and supports version imports and trade evolutions.
- One persistent save reaches 251 owned species without debug state mutation.
- Oak/Diploma completion behavior recognizes that save.

## Release quality

- All required sprites, menus, text, and feedback are visible and understandable.
- Alternate starters, battle losses, cancellations, full capacity, and representative save/load interruptions have regression coverage.
- A human completes a fresh-save desktop-host run through Red.
- A human exercises every acquisition channel.
- README, status, and plan claims match the enforced evidence.
- Current screenshots or video demonstrate the shipped native application.

Until every criterion above is satisfied, the project should describe itself as an advanced reimplementation framework or playable prototype—not a complete Pokémon Gold release.
