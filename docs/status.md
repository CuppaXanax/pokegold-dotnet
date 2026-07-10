# Project Status

Last updated: 2026-07-09.

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
- Current validation: `dotnet build .\src\PokeGold.Game` and `dotnet test
  .\tests\PokeGold.Tests` (1302/1302) are green from `engine-dotnet`.
- `dotnet build .\src\PokeGold.Host` could not be repeated in this environment:
  its MonoGame assets were not restored and NuGet access failed with `NU1301`.

## Current known gaps

- The public engineering gate is still the full fresh-save route proof through
  the real runtime; the no-shortcuts test currently proves only the opening
  route prefix through Elm's starter gift.
- Production battle staging now rejects missing/invalid combatants instead of
  substituting Cyndaquil/Pidgey. Persistent party identity, DV-aware stats,
  progression, and blackout semantics remain battle critical-path work.
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

1. Complete BAT-005 through BAT-019: add stable party identity and finish
   persistent stats, progression, evolution, and blackout.
2. Complete route-required overworld and script semantics, then extend the
   no-shortcuts route one earned checkpoint at a time.
3. Keep conformance-ledger changes test-backed and tied to the relevant `.asm`
   source.
4. After the route gate is proven, update public docs with the exact command,
   current test count, and a host screenshot or GIF.
