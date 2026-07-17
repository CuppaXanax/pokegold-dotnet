# Project Status

Last updated: 2026-07-17.

## Current goal

Prove a fresh-save, glitchless, playable route through Pokemon Gold in the
MonoGame/F# runtime. The victory check described in `docs\plan\victory-plan.md`
is a real-runtime route from `StartNewGame` to the post-Red credits with real
inputs, movement, collision, warps, triggers, battles, save state, and runtime
invariants, without shortcut flag setup.

## Current known-good systems

The current completion plan is `docs\plan\plan.md`. Known-good areas include:

- F# platform-free game library plus MonoGame DesktopGL host.
- Direct repository source-asset/data pipeline with build-time generated F#
  tables; no ROM input required.
- Overworld map, movement, collision, connection, warp, object, script, and
  scene systems covered by unit/runtime tests.
- Script-VM golden path gates G1-G14, from New Bark bedroom through post-Red
  credits, verified at the VM/story layer.
- Fresh-save/no-shortcuts runtime proof now boots only with `StartNewGame`,
  drives real inputs through the New Bark home, Elm's Lab, and starter
  acquisition, and asserts `RuntimeInvariants.assertHold` across the trace.
- Build-time map script generation expands Goldenrod Underground `ugdoor_def` /
  `changeugdoor` macros without generic `Unsupported` script commands.
- Generated trainer data preserves all 495 source party layouts, held items,
  explicit move slots, and class DVs. Runtime Falkner, Whitney, Lance, and Red
  parties use their source species, levels, and explicit moves.
- Normal and item-only trainer parties derive moves with the source `FillMoves`
  order, duplicate suppression, and oldest-move replacement behavior.
- Generated wild opponents use source learnsets, common/rare held-item chances,
  packed DVs, gender ratios, and special forced-item/forced-shiny attributes;
  caught Pokémon retain their DVs.
- Battle, party, bag, storage, save/load, audio, menu, field-move, encounter,
  evolution, trading, breeding, Pokedex, and conformance-ledger tests.
- Pokédex obtainability has a static 251-species source inventory. This is data
  coverage, not yet a player-facing runtime acquisition proof.
- Oak's Lab now rates the live Pokédex through the source 19-entry threshold,
  message, and fanfare table, with staged runtime proof that his script resumes.
- Cut now replaces the source tileset-specific obstruction block, opens real
  traversal in Ilex Forest and Route 2, and resets with source map reloads.
- Surf now enters from a facing shore, renders the normal or Pikachu source
  sprite, traverses water while rejecting invalid terrain, dismounts on legal
  land, and restores a legal water state through save/reload.
- Strength boulders push through the runtime and source-generated stone tables
  now drive pit detection and fallout scripts in Ice Path and Blackthorn Gym.
- Current validation: `dotnet build .\src\PokeGold.Game` and `dotnet test
  .\tests\PokeGold.Tests` (1416/1416) are green from `engine-dotnet`.
- `dotnet build .\src\PokeGold.Host --no-restore` is green from `engine-dotnet`.

## Current known gaps

- The public engineering gate is still the full fresh-save route proof through
  the real runtime; the no-shortcuts test currently proves only the opening
  route prefix through Elm's starter gift.
- Production battle staging rejects missing/invalid combatants; stable identities
  survive battle/storage/save; and persistent stats now use packed DVs plus five
  source stat-exp words. Identity-safe battle round-tripping now preserves canonical
  moves/PP, items, sleep counters, progression carriers, and friendship while cleaning
  up Transform/Mimic. Ordered per-defeat participant and EXP Share awards now
  persist exact EXP/stat EXP with source modifier rounding. Every crossed move
  level is processed in order, final-level stats are recalculated per tranche,
  and level evolution is deferred to victorious cleanup. Trainer and Pay Day
  money now settle exactly once from terminal battle state with source payout
  and Amulet Coin rules. Full level-up movesets now enter an ordered,
  player-controlled replace/decline flow with HM protection. All 50 TMs, seven
  HMs, and 251 source compatibility sets are generated and enforced. All Gold
  evolution methods share ordered source eligibility with cancellable battle/
  item presentation and exact trade catalysts; accepted evolutions preserve the
  prior Pokédex entry and register the target. Source blackout now resolves baked
  spawn destinations, heals party HP/status/PP, floors money-halving, clears
  transient state, and aborts the defeated script continuation. Real generated
  Falkner, Lance, and Red losses now prove that no victory-only mutation occurs.
  A saved Falkner blackout reloads with the trainer still available and settles its
  later legitimate victory exactly once, including badge, event, and TM persistence.
  Fainted player Pokémon now wait for a legal chosen replacement; replacement timing
  blocks extra actions, handles simultaneous faint ordering, and is proven through
  repeated multi-mon cycles plus the real Falkner runtime battle.
  Battle PACK now retains fainted reserves as targetable battle-team members while
  selecting a conscious active battler. Its source battle-menu item set is covered by
  a generated-data support guard and staged UI tests for Revive/Max Revive, status
  cures, PP recovery, direct X-items, and trainer-restricted Poké Doll use; bitter
  medicine friendship changes persist through post-battle synchronization.
  Consumable and nonconsumable held items now retain source-consistent state through
  residual activation, fainting, switching, capture cleanup, battle synchronization,
  and save/reload.
  Trainer AI dispatch covers every generated move layer and every
  `AI_Smart_EffectHandlers` entry. Named branch tests verify seeded minimum-score ties,
  switch candidate masks/categories and probabilities, locked-action move history,
  good-weather branches, and trainer-item context/RNG behavior. Legal generated Falkner,
  Will, Lance, and Red fixtures cover the integration path.
- One persistent runtime save has not yet acquired all 251 species through
  playable channels.
- Off-route play is not exhaustively covered; the golden route is the first
  target, and unusual menuing, losses, alternate starters, and detours may expose
  more issues.
- Conformance ledger `Unknown` RequiredFor100Percent entries have been burned
  down, but visible `StubNoOp` debt remains and must stay test-backed as it is
  implemented or reclassified.
- Frame-perfect Game Boy Color parity is not the goal of this runtime.
- `PokeGold.Host.Android` needs Android SDK setup and should be skipped for
  normal local validation unless Android work is the task.

## Next high-value tasks

1. Complete route-required overworld and script semantics, then extend the
   no-shortcuts route one earned checkpoint at a time.
2. Keep conformance-ledger changes test-backed and tied to the relevant `.asm`
   source.
3. After the route gate is proven, update public docs with the exact command,
   current test count, and a host screenshot or GIF.
