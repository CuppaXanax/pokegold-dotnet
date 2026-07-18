# pokegold-dotnet

`pokegold-dotnet` is an experimental F# + MonoGame reimplementation/runtime for
Pokemon Gold behavior. It uses the pret Pokemon Gold disassembly and source
assets in this repository as its data source, and the native .NET engine does
not require a ROM to run.

The active implementation lives in [`engine-dotnet`](engine-dotnet/). The
current engineering gate is to prove a fresh-save, glitchless, playable route
through Pokemon Gold in MonoGame/F# land. Longer-term research may explore pure
functional modeling, solver-backed route analysis, and formal verification, but
those are research directions, not current project guarantees.

## Current status

This is an active port, not a finished game release. The plan documents track a
large amount of engine work as complete, including source-asset data generation,
overworld systems, script VM work, battle systems, save/load, audio, UI scenes,
and a substantial xUnit conformance suite. The current desktop suite has
1,432/1,432 tests green as of 2026-07-17, including a script-VM golden path from
New Bark bedroom to post-Red credits, source-authentic trainer/wild construction,
visible failure for missing production battle data, stable party identity, and
source-exact persistent DV/stat-experience calculations and complete identity-safe
battle-state round-tripping, engine-owned per-defeat progression history, and
source-defined blackout behavior that heals the party, floors lost money, and
prevents Falkner, Lance, and Red defeats from continuing as victories, plus a
real Falkner loss-save-reload-retry path that grants progression exactly once,
source-ordered forced replacement across real multi-mon trainer battles,
source-backed battle items plus held-item consumption/persistence coverage across
runtime battle cleanup and save reload, seeded trainer AI with branch tests for player-move
history, candidate-filtered switching, good-weather scoring, and X-item rolls, and a
legal-party runtime component matrix spanning wild, ordinary trainer, gym, Elite Four,
and Red battles. The remaining public bar is the continuous fresh-save Epic 3 route.

The remaining public bar is stronger than "tests exist": prove the route through
the real runtime with real inputs, movement, collision, warps, triggers, battles,
save state, and invariants from a fresh save without shortcut flags.

See [`docs/status.md`](docs/status.md) for the concise handoff status and
[`docs/plan/`](docs/plan/) for the detailed milestone and victory plans.

## What works

- `engine-dotnet/src/PokeGold.Game` is a platform-free F# game library with no
  MonoGame dependency.
- `engine-dotnet/src/PokeGold.Host` is a MonoGame DesktopGL host for the native
  engine.
- Build-time data generation reads checked-in disassembly/source assets from
  `constants/`, `data/`, `maps/`, `gfx/`, and `audio/`.
- The test suite covers many core systems: graphics/data parsing, map rendering,
  collision, movement, scripts, story gates, battles, save/load, audio, menus,
  field moves, encounters, party/bag/storage systems, and conformance ledgers.
- The desktop host can run the native engine without a ROM.

## What is not done yet

- Do not assume this is fully playable end-to-end as a public game release until
  the fresh-save glitchless route gate is proven in the real runtime.
- Off-route behavior is not exhaustively proven; the golden route is the first
  target, and unusual menuing, losses, alternate starters, and detours may expose
  more issues.
- Frame-perfect Game Boy Color parity is not the goal of this MonoGame/F#
  runtime.
- The Android host is present, but the plan docs note that it requires an
  Android SDK setup and should not be part of default local builds.

## Build

Use commands from `engine-dotnet`. Avoid building the whole solution unless the
Android SDK is installed, because `PokeGold.Host.Android` is part of the solution.

```powershell
cd engine-dotnet
dotnet build .\src\PokeGold.Game
dotnet build .\src\PokeGold.Host
```

Building `PokeGold.Game` also runs the build-time data generator when its inputs
or outputs require it.

## Test

```powershell
cd engine-dotnet
dotnet test .\tests\PokeGold.Tests
```

For focused work, prefer the smallest relevant test project or test filter first,
then run the full test project when the change affects shared behavior.

## Run the MonoGame host

If your machine has the .NET SDK and desktop graphics support needed by
MonoGame DesktopGL:

```powershell
cd engine-dotnet
dotnet run --project .\src\PokeGold.Host
```

The host runs the native .NET engine against repository source assets. It does
not ask for a ROM.

## Repository layout

```text
engine-dotnet/
  PokeGold.slnx
  src/
    PokeGold.Game/          F# platform-free game/runtime library
    PokeGold.Host/          MonoGame DesktopGL host
    PokeGold.Host.Android/  Android host; requires Android SDK
    PokeGold.MapData/       Shared map/script data model and parsers
  tests/PokeGold.Tests/     xUnit verification suite
  tools/PokeGold.DataGen/   Build-time source-data generator

audio/, constants/, data/, engine/, gfx/, maps/
  Disassembly/source assets used as the .NET engine's data source

docs/status.md              Current concise project status
docs/plan/                  Detailed milestone, victory, and agent handoff docs
```

## Legal and asset note

This repository is based on a reverse-engineered Pokemon Gold disassembly and
contains checked-in source assets and data files. The .NET runtime reads or bakes
those repository files directly and does not require users to provide a ROM.

This project is not affiliated with, endorsed by, or sponsored by Nintendo, Game
Freak, Creatures, The Pokemon Company, or pret. Do not treat this repository as a
commercial game release, and do not add new copyrighted branding or assets unless
the rights and project purpose are clear.

## Contributing

Most active work should happen under `engine-dotnet`. Before changing behavior,
read the relevant `.asm` source in this repository and preserve behavior over
clever refactors. Keep generated code, source assets, and runtime code clearly
separated, and do not introduce a ROM dependency.

Use [`AGENTS.md`](AGENTS.md) for coding-agent handoff rules. Use
[`docs/plan/victory-plan.md`](docs/plan/victory-plan.md) for the route gate and
[`docs/plan/plan.md`](docs/plan/plan.md) for the longer milestone history.
