# Agent Guidelines

## Project focus

Prefer working under `engine-dotnet`. The root disassembly/source-asset tree is
the behavioral and data source for the .NET engine, but most active
implementation work belongs in the F# projects.

Before changing behavior, read the relevant `.asm` source first: common sources
include `maps\*.asm`, `engine\**\*.asm`, `constants\*.asm`, and `data\**\*.asm`.
Preserve behavior over clever refactors, and prefer small conformance fixes with
tests over broad rewrites.

## Boundaries

- Do not introduce ROM dependencies. The native engine must continue to read or
  bake repository source assets directly.
- Keep generated data and source assets separate from handwritten runtime code.
  `PokeGold.DataGen` emits `engine-dotnet\src\PokeGold.Game\Data\Generated\*`.
- Avoid editing upstream disassembly/source-asset files unless the task
  explicitly requires it.
- Do not add copyrighted branding, logos, or assets beyond what is already
  present in the repository.
- Keep commits focused on one concern.

## Build and test

Use Windows paths from `engine-dotnet`:

```powershell
cd engine-dotnet
dotnet build .\src\PokeGold.Game
dotnet test .\tests\PokeGold.Tests
dotnet build .\src\PokeGold.Host
dotnet run --project .\src\PokeGold.Host
```

`dotnet build .\src\PokeGold.Game` also regenerates baked data when needed.
Avoid building the whole `PokeGold.slnx` unless the Android SDK is installed;
`PokeGold.Host.Android` is in the solution and the plan docs note it can fail
locally without Android setup. If a command cannot run locally, document the
exact command and failure.

Run the smallest relevant tests before and after behavior changes, then expand
to `dotnet test .\tests\PokeGold.Tests` when shared systems are affected.

## High-value test-first areas

Favor test-first work for route verification, script VM behavior, movement,
collision, warps, NPC triggers, battle conformance, and overworld conformance.
When changing ledger-covered behavior, update the ledger entry and name the test
that proves it.

For route work, follow `docs\plan\victory-plan.md`: prove behavior through the
runtime layer with real inputs and assertions on movement, collision, warps,
coord triggers, NPC/script triggers, battle transitions, and save state.

## Documentation handoff

Keep `README.md`, `docs\status.md`, and `docs\plan\victory-plan.md` aligned
when status changes. Do not claim the game is fully playable until the
fresh-save, glitchless runtime route gate is proven.
