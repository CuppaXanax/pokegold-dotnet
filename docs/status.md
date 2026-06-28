# Project Status

Last updated: 2026-06-28.

## Current goal

Prove a fresh-save, glitchless, playable route through Pokemon Gold in the
MonoGame/F# runtime. The victory check described in `docs\plan\victory-plan.md`
is a real-runtime route from `StartNewGame` to the post-Red credits with real
inputs, movement, collision, warps, triggers, battles, save state, and runtime
invariants, without shortcut flag setup.

## Current known-good systems

The existing plan docs state that the core milestone work has landed and that
`docs\plan\victory-plan.md` recorded 993/993 tests green as of 2026-06-11.
Known-good areas documented there include:

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
- Battle, party, bag, storage, save/load, audio, menu, field-move, encounter,
  evolution, trading, breeding, Pokedex, and conformance-ledger tests.
- Pokédex obtainability has an automated all-251 proof over generated
  encounters/scripts, evolutions, breeding, roamers, fishing, headbutt, bug
  contest, swarms, the documented offline trade terminal, and built-in event
  unlocks.
- Current validation: `dotnet build .\src\PokeGold.Game`, `dotnet test
  .\tests\PokeGold.Tests` (1288/1288), and `dotnet build
  .\src\PokeGold.Host` are green from `engine-dotnet`.

## Current known gaps

- The public engineering gate is still the full fresh-save route proof through
  the real runtime; the no-shortcuts test currently proves only the opening
  route prefix through Elm's starter gift.
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

1. Extend the no-shortcuts victory test from the Elm starter prefix toward
   Cherrygrove, Violet, and the rest of the Red route with
   `RuntimeInvariants.assertHold` on every frame.
2. Fix runtime failures found by that route in movement, collision, warps,
   connections, coord triggers, NPC triggers, battle transitions, and save/load.
3. Keep conformance-ledger changes test-backed and tied to the relevant `.asm`
   source.
4. After the route gate is proven, update public docs with the exact command,
   current test count, and a host screenshot or GIF.
