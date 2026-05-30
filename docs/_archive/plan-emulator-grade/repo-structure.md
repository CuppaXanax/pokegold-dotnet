# Pokegold C# repo structure proposal

Status: proposal for review by the project lead. This is intentionally practical, AOT-aware, and optimized for many parallel coding agents. It assumes the current docs remain the ground truth: `docs/recon/*`, `docs/conventions/*`, and `docs/plan/milestones.md`.

## Design goals

1. Mirror pret's source layout closely enough that a translator can jump from `engine/battle/core.asm` to one obvious C# home.
2. Keep platform and verification concerns outside translated gameplay code.
3. Keep the core runtime NativeAOT-safe: no reflection-heavy framework assumptions, no plugin discovery, no dynamic codegen.
4. Make parallel work cheap: battle, overworld, menus, data, maps, audio, and verification should mostly live in separate folders and projects.
5. Preserve the byte-first architecture already established by the memory, platform, translation, and verification docs.

---

## 1. Proposed top-level repo layout

```text
/
  Pokegold.sln
  Directory.Build.props
  Directory.Packages.props
  eng/
    publish/
    scripts/
  src/
    Pokegold.Platform/
    Pokegold.Core/
    Pokegold.Hosting/
    Pokegold.Platform.Sokol/
    Pokegold.App/
    Pokegold.Verification/
    Pokegold.Verification.SameBoy/
  tests/
    Pokegold.Tests.Unit/
    Pokegold.Tests.Integration/
    Fixtures/
      Routines/
      Verification/
      Saves/
  native/
    SameBoyCapture/
  docs/
    recon/
    conventions/
    plan/
    translation-log/
    adr/
  local/                 # gitignored; ROMs, reference builds, private config
  artifacts/             # gitignored; test output, publish output, capture dumps
```

### Why this split

- `src/` contains shipping/runtime code.
- `tests/` contains test projects plus committed fixtures.
- `native/` isolates the small amount of C/C++ interop glue needed for SameBoy.
- `local/` is the well-known ignored place for copyrighted ROM/reference artifacts.
- `docs/translation-log/` and `docs/adr/` give future Claude instances durable places to find non-code decisions.

---

## 2. Solution structure (`.csproj` projects)

Recommended initial solution: **9 projects** total.

| Project | Type | Purpose | Depends on |
|---|---|---|---|
| `Pokegold.Platform` | class library | Shared host boundary: `IPlatform`, `IDisplay`, `IAudioOutput`, `IInputSource`, `IBatteryStore`, `ISerialEndpoint`, `BatteryImage`, button/signal enums. | none |
| `Pokegold.Core` | class library | Translated game logic, memory/bus, renderer, audio synthesis, RTC model, data tables, and pret-mirrored subsystems. | `Pokegold.Platform` |
| `Pokegold.Hosting` | class library | `GameHost`, frame pacing, ROM discovery/hash verification, battery flush policy, app configuration, headless run loop. | `Pokegold.Core`, `Pokegold.Platform` |
| `Pokegold.Platform.Sokol` | class library | First platform backend using Sokol.NET or a pinned vendored equivalent. | `Pokegold.Platform` |
| `Pokegold.App` | executable | Thin desktop composition root wiring `Hosting + Core + Platform.Sokol`. | `Pokegold.Hosting`, `Pokegold.Platform.Sokol` |
| `Pokegold.Verification` | class library | Replay harness, domain hashes, fixture loading, screenshot/save diff helpers, scenario runner abstractions. | `Pokegold.Core`, `Pokegold.Hosting`, `Pokegold.Platform` |
| `Pokegold.Verification.SameBoy` | class library | Managed wrapper over the native SameBoy capture shim. Test-only/reference-only. | `Pokegold.Verification` |
| `Pokegold.Tests.Unit` | test project | Routine fixtures and small subsystem tests. | `Pokegold.Core`, `Pokegold.Verification` |
| `Pokegold.Tests.Integration` | test project | Replay, lockstep, save, screenshot, and long-scenario verification. | `Pokegold.Hosting`, `Pokegold.Verification`, `Pokegold.Verification.SameBoy` |

### Separation of concerns

- `Pokegold.Core` owns Game Boy semantics.
- `Pokegold.Platform*` owns only presentation/device/persistence adapters.
- `Pokegold.Hosting` owns orchestration and ROM/bootstrap concerns.
- `Pokegold.Verification*` owns reference-emulator integration and fixture plumbing.
- Test projects consume verification code; they do not contain the harness logic themselves.

### Dependency graph

```text
Pokegold.Platform
      ^
      |
Pokegold.Core          Pokegold.Platform.Sokol
      ^                         ^
      |                         |
Pokegold.Hosting  <-------------+
      ^
      |
Pokegold.Verification
      ^              ^
      |              |
Pokegold.Verification.SameBoy
      ^
      |
Pokegold.Tests.Unit / Pokegold.Tests.Integration

Pokegold.App -> Pokegold.Hosting + Pokegold.Platform.Sokol
```

### Recommendation against extra projects

- **CONTENTIOUS:** do **not** split `Data`, `Maps`, or `Audio` into separate `.csproj` projects initially. Pret organizes them as subsystems/content buckets, but the translated runtime will cross-reference them constantly; separate assemblies would add ceremony without reducing real coupling.
- **CONTENTIOUS:** do **not** put platform interfaces inside `Pokegold.Core`. Keeping them in `Pokegold.Platform` prevents Sokol/SDL/Web concerns from leaking into the translated runtime.

---

## 3. Namespace organization

Use `Pokegold` as the root namespace. Mirror pret at the first meaningful level, then use C#-style PascalCase folders and file names underneath.

| pret path | C# namespace | Notes |
|---|---|---|
| `home/` | `Pokegold.Home` | Keep ROM0/home routines distinct; do not flatten into `Utils`. |
| `engine/battle/` | `Pokegold.Engine.Battle` | Includes AI, move effects, transitions, HUDs. |
| `engine/overworld/` | `Pokegold.Engine.Overworld` | Map loop, movement, scripting, objects. |
| `engine/events/` | `Pokegold.Engine.Events` | Field moves, standard scripts, hall of fame, daycare, etc. |
| `engine/menus/` | `Pokegold.Engine.Menus` | Title, options, save, scrolling menus, trainer card. |
| `engine/pokemon/` | `Pokegold.Engine.Pokemon` | Party/PC/stats/breeding/mail/evolution. |
| `engine/items/` | `Pokegold.Engine.Items` | Bag, marts, item effects, TM/HM flows. |
| `engine/link/` | `Pokegold.Engine.Link` | Link/trade/time capsule/mystery gift. |
| `engine/rtc/` | `Pokegold.Engine.Rtc` | RTC-specific engine routines. |
| `engine/gfx/` | `Pokegold.Engine.Gfx` | Engine-side graphics loaders/layout helpers. |
| `engine/pokedex/` | `Pokegold.Engine.Pokedex` | Pokédex flows. |
| `engine/pokegear/` | `Pokegold.Engine.Pokegear` | Pokégear UI/radio/map. |
| `engine/phone/` | `Pokegold.Engine.Phone` | Phone-call logic. |
| `engine/games/` | `Pokegold.Engine.Games` | Minigames. |
| `engine/printer/` | `Pokegold.Engine.Printer` | Printer-specific flows. |
| `engine/sprite_anims/` | `Pokegold.Engine.SpriteAnims` | Sprite animation engine. |
| `engine/tilesets/` | `Pokegold.Engine.Tilesets` | Tileset runtime helpers. |
| `audio/` | `Pokegold.Audio` | Keep top-level like pret. |
| `data/battle/` | `Pokegold.Data.Battle` | Runtime-read tables. |
| `data/items/` | `Pokegold.Data.Items` | Item attributes, marts, descriptions, names. |
| `data/maps/` | `Pokegold.Data.Maps` | Shared map data tables, block references, setup metadata. |
| `data/moves/` | `Pokegold.Data.Moves` | Move data/effects tables. |
| `data/pokemon/` | `Pokegold.Data.Pokemon` | Base stats, evolutions, egg moves, palettes, pic pointers. |
| `data/text/` | `Pokegold.Data.Text` | Shared text banks and input char sets. |
| `data/trainers/` | `Pokegold.Data.Trainers` | Trainer tables, pictures, parties. |
| `data/wild/` | `Pokegold.Data.Wild` | Encounter tables. |
| `maps/` | `Pokegold.Maps` | Per-map script/object definitions. |
| `constants/` | `Pokegold.Constants` | IDs, offsets, flags, charmap, hardware constants. |
| `ram/` | `Pokegold.Memory.Layout` | Layout-derived offsets/views, not a separate runtime island. |

### Balance rule

- Mirror pret strongly enough that source lookup is obvious.
- Do **not** mirror bank names or `SECTION` names into namespaces.
- Do **not** create deep namespaces for every tiny folder if the source tree does not use them semantically.

Example:

- `engine/battle/core.asm` -> `Pokegold.Engine.Battle.Core`
- `data/pokemon/base_stats/bulbasaur.asm` -> `Pokegold.Data.Pokemon.BaseStats.Bulbasaur`
- `maps/NewBarkTown.asm` -> `Pokegold.Maps.NewBarkTown`

---

## 4. Core project layout (`src/Pokegold.Core`)

```text
src/Pokegold.Core/
  Cpu/
  Memory/
    Layout/
    Views/
    Bus/
  Runtime/
  Home/
  Engine/
    Battle/
    Events/
    Gfx/
    Games/
    Items/
    Link/
    Menus/
    Overworld/
    Phone/
    Pokedex/
    Pokegear/
    Pokemon/
    Printer/
    Rtc/
    SpriteAnims/
    Tilesets/
  Audio/
  Data/
    Battle/
    Collision/
    Items/
    Maps/
    Moves/
    Phone/
    Pokemon/
    Text/
    Tilesets/
    Trainers/
    Wild/
  Maps/
  Constants/
```

### What belongs in `Cpu/`

- `CpuMath`
- flag/ALU result types
- rotate/BCD/carry-chain helpers
- bank-dispatch helpers closely tied to translation patterns

These are shared translation tools, not a full CPU emulator.

### What belongs in `Memory/`

- `GoldMemory`
- bus facade and address decoding
- `IoRegisterFile`, `HramFile`, OAM/VRAM/SRAM/WRAM stores
- typed views for party structs, battle structs, map structs, save structs
- layout-derived offsets from `ram/*.asm` and `constants/*_constants.asm`

### What belongs in `Runtime/`

Only genuinely cross-cutting runtime scaffolding that pret does not provide as a folder:

- `GoldGame` or equivalent top-level runtime object
- frame scheduler helpers
- renderer/audio mixer entrypoints that consume memory state
- ROM manifest/hash metadata

**CONTENTIOUS:** keep `Runtime/` small. It is for glue, not a dumping ground for translated logic.

### What belongs in `Home/`

All always-banked ROM0 routines:

- VBlank/timing/video
- text/menu/string formatting helpers
- map/script helpers in ROM0
- farcall/predef/copy/math/random helpers
- battle/item/pokemon wrappers that live in `home/`

Do not collapse `Home` into `Engine.Common`; `home/` is a real source-level distinction in pret and matters for translation/debugging.

### What belongs in `Engine/`

Translated subsystem code grouped exactly like pret's `engine/` organization. Important guideline:

- keep each interpreter near its owning subsystem
- event scripts stay with `Engine.Overworld`
- battle effect-command VM stays with `Engine.Battle`
- text command interpreter stays with `Home`
- movement command runtime stays with `Engine.Overworld`

Do **not** centralize all bytecode systems into one abstract `Scripts/` mega-namespace.

### What belongs in `Data/`

ROM data tables and manifests that are not per-map files:

- Pokémon tables
- move/item/trainer/wild data
- shared text banks
- map metadata tables in `data/maps/`
- battle/item constants derived from lookup tables

### What belongs in `Maps/`

Per-map translated definitions mirroring `maps/*.asm`:

- scene scripts
- callbacks
- warp/bg/object events
- local movement blocks
- map-local text/script labels

Recommended split inside `Maps/`:

```text
Maps/
  NewBarkTown.cs
  CherrygroveCity.cs
  VioletCity.cs
  ...
```

Keep one map per file unless a map grows large enough to justify partials.

---

## 5. Platform projects

## `src/Pokegold.Platform`

Shared interfaces live here, not in `Core` and not in a backend project.

Keep it tiny:

- `IPlatform`
- `IDisplay`
- `IAudioOutput`
- `IInputSource`
- `IBatteryStore`
- `ISerialEndpoint`
- shared enums and records (`GameBoyButtons`, `PlatformSignals`, `BatteryImage`)

No backend-specific types should leak out of this project.

## `src/Pokegold.Platform.Sokol`

First backend implementation.

Suggested internal folders:

```text
src/Pokegold.Platform.Sokol/
  Display/
  Audio/
  Input/
  Persistence/
  Lifecycle/
  SokolPlatform.cs
```

Rules:

- Sokol types stay inside this project.
- `Pokegold.Platform.Sokol` should implement the interface boundary from `docs/conventions/platform-interface.md`, not invent a richer one.
- `Pokegold.App` should be the composition root; the Sokol project should not know about ROM loading or gameplay bootstrap.

## Future backends

Add one project per backend:

- `src/Pokegold.Platform.Sdl`
- `src/Pokegold.Platform.Raylib`
- `src/Pokegold.Platform.WebCanvas`

If browser delivery needs a different entry-point shape, add a separate host/app project too, e.g. `src/Pokegold.App.Web`.

**CONTENTIOUS:** do not build plugin-style backend discovery. Explicit compile-time references are simpler and much friendlier to NativeAOT.

---

## 6. Test and verification projects

## Unit tests

`tests/Pokegold.Tests.Unit`

Use for:

- routine fixtures
- memory-view tests
- ALU/flag tests
- save checksum helpers
- stat/EXP/AI/RTC/text edge cases

## Integration tests

`tests/Pokegold.Tests.Integration`

Use for:

- replay scenarios
- frame-hash verification
- screenshot/framebuffer comparisons
- save/load round-trips
- long deterministic milestone paths

## Fixture locations

```text
tests/Fixtures/
  Routines/
    Battle/
    Pokemon/
    Rtc/
    Text/
  Verification/
    boot-to-title/
    first-battle/
    hall-of-fame/
  Saves/
    clean-start/
    active-box-edge-cases/
    rtc-rollover/
```

Recommended contents for replay scenarios, following `docs/conventions/verification.md`:

```text
tests/Fixtures/Verification/<scenario>/
  manifest.json
  input.bin
  frames.csv
  screenshots/
  saves/
  checkpoints/
```

Generated divergence dumps and long nightly artifacts should go under `artifacts/verification/`, not into git.

## SameBoy interop

Make SameBoy interop a **separate project**:

- managed wrapper: `src/Pokegold.Verification.SameBoy`
- native shim: `native/SameBoyCapture`

Why separate:

- keeps shipping app code clean
- isolates native build complexity
- avoids pulling reference-emulator dependencies into ordinary unit tests
- leaves room to swap the capture backend later without rewriting the whole harness

---

## 7. Build configuration

## Target framework

Recommended default: **`.NET 8`**.

Why:

- LTS
- mature NativeAOT story
- works well for desktop and verification tooling
- easier baseline for a long port

**CONTENTIOUS:** if browser-wasm tooling or Sokol.NET support is materially better on `.NET 9`, revisit once the project reaches a stable playable milestone. I would still start the shared libraries on `.NET 8` unless a concrete blocker appears.

## Central build files

- `Directory.Build.props` for common compiler settings
- `Directory.Packages.props` for central package version management

Suggested shared defaults:

- `Nullable=enable`
- `ImplicitUsings=enable`
- deterministic builds
- warnings-as-errors for `src/*`
- looser analyzer/test settings in `tests/*`

## Build flavors

Use three practical lanes:

1. `Debug` - fast local iteration, no AOT, verification hooks enabled
2. `Release` - optimized JIT build for ordinary testing
3. publish profile / CI lane for NativeAOT desktop builds

Do **not** make NativeAOT the default developer inner loop.

## Publish targets

Desktop app (`Pokegold.App`):

- `win-x64`
- `linux-x64`
- `osx-arm64`

Browser:

- **UNCLEAR:** browser-wasm should probably be a separate app/backend project rather than another RID on the desktop app, because the packaging/runtime model is different.

Recommended publish layout:

```text
eng/publish/
  win-x64.pubxml
  linux-x64.pubxml
  osx-arm64.pubxml
  browser-wasm.pubxml   # future, likely for Pokegold.App.Web
```

## Conditional compilation

Keep custom symbols minimal.

Recommended rule:

- no gameplay-behavior symbols in `Pokegold.Core`
- prefer runtime interfaces/options over `#if`
- allow small project-local symbols only where unavoidable, e.g. verification interop or browser-specific entry points

Good examples:

- `POKEGOLD_VERIFICATION` in verification-only assemblies
- `POKEGOLD_BROWSER` in a future browser app project

Bad examples:

- `#if SOKOL` inside translated battle/text/overworld code
- `#if DEBUG` changing core gameplay behavior

---

## 8. NuGet dependency policy

Policy: **minimize external dependencies**.

## Runtime/core projects

Preferred external dependencies:

- none in `Pokegold.Core`
- none in `Pokegold.Hosting` beyond the BCL
- no DI container / reflection-heavy host framework

Use built-in .NET features instead:

- `System.Text.Json` with source generation if app config/fixture manifests need serialization
- `LibraryImport`/P/Invoke source generation for native interop
- BCL collections/spans/crypto for hashes and manifests

## Sokol

**UNCLEAR:** I could not verify a stable NuGet.org package ID for Sokol.NET from the repo docs alone.

Recommendation:

1. if the project lead has a vetted package ID, keep that dependency isolated to `Pokegold.Platform.Sokol`
2. otherwise, pin/vendor the exact Sokol.NET source revision inside that backend project or under a dedicated `external/` or git submodule path
3. never let Sokol-specific types escape the backend project

This keeps the rest of the solution insulated whether Sokol arrives via NuGet or source pinning.

## Test dependencies

Reasonable baseline:

- `Microsoft.NET.Test.Sdk`
- `xunit`
- `xunit.runner.visualstudio`

Optional later, verification-only:

- image diff helper library if PNG diff generation becomes painful
- compression library if committed checkpoint size becomes a real problem

**CONTENTIOUS:** do not add snapshot-test frameworks, mocking frameworks, or generic game-engine libraries up front.

---

## 9. ROM and reference artifact policy

The retail ROM is not committed.

Recommended lookup order:

1. explicit CLI argument / config value
2. environment variable `POKEGOLD_ROM_PATH`
3. ignored fallback path `local/roms/pokegold.gbc`

For verification/reference artifacts, use parallel variables or a root directory:

- `POKEGOLD_REFERENCE_ROOT`
- or `POKEGOLD_SYM_PATH` / `POKEGOLD_MAP_PATH`
- ignored fallback: `local/reference/`

At startup and in tests:

- compute SHA-1 of the ROM
- compare against the canonical retail Gold SHA-1
- fail fast on mismatch, with an explicit override only for intentional experiments

Recommended app behavior on mismatch:

- ordinary app: clear error message and refuse to run
- verification harness: fail the scenario immediately and print expected vs actual hashes

---

## 10. Translation log policy

Each non-trivial porting decision should be logged under a path that mirrors the original ASM tree.

Recommended layout:

```text
docs/translation-log/
  README.md
  home/
    text.md
    vblank.md
  engine/
    battle/
      core.md
      effect_commands.md
    overworld/
      scripting.md
  data/
    pokemon/
      base_stats.md
```

## Per-log format

Each file should contain:

- source ASM path(s)
- C# output path(s)
- decision summary
- why the direct translation was not obvious
- verification status / fixture names
- open questions
- last updated by / date / commit if helpful

## How future Claude instances find/update logs

- first look in `docs/translation-log/<mirrored-pret-path>.md`
- if the decision affects multiple subsystems or project-wide rules, promote it to `docs/adr/`
- when a file is translated or materially rewritten, update its mirrored translation log in the same change

**CONTENTIOUS:** keep translation logs as Markdown, not JSON/YAML. They are primarily for human/Claude synthesis, not machine execution.

---

## 11. File naming conventions

## C# file names

- PascalCase file names
- default file stem derived from the ASM file stem
- class/record/enum names should match the file name when practical

Examples:

- `home/text.asm` -> `Home/Text.cs`
- `engine/menus/main_menu.asm` -> `Engine/Menus/MainMenu.cs`
- `engine/gfx/load_push_oam.asm` -> `Engine/Gfx/LoadPushOam.cs`
- `data/pokemon/base_stats/bulbasaur.asm` -> `Data/Pokemon/BaseStats/Bulbasaur.cs`

## Mapping granularity

Default rule: **one primary C# file per ASM file**.

Why:

- easiest source traceability
- easiest parallel ownership
- smallest merge-conflict surface
- matches the source-map docs well

Allowed exception:

- if one ASM file is extremely large (`battle/core.asm`, `effect_commands.asm`, some menu/save files), use a root partial plus a few focused partials

Example:

```text
Engine/Battle/Core.cs
Engine/Battle/Core.TurnFlow.cs
Engine/Battle/Core.Switching.cs
Engine/Battle/Core.Exp.cs
```

Even then, keep the root partial named after the source ASM file so the mapping stays obvious.

## Comments linking back to ASM

Recommended minimum:

- one file header comment with the original ASM path
- extra method-level comments only for tricky routines, split partials, or behavior-preservation notes

Example:

```csharp
// Source: engine/battle/core.asm
// ASM routines here: DoBattle, BattleTurn, HandleFaint
```

For especially fragile translations, include line ranges in the comment or the translation log rather than cluttering every line of code.

---

## 12. Docs organization

Keep these directories as-is:

- `docs/recon/`
- `docs/conventions/`
- `docs/plan/`

Add only two new durable buckets:

- `docs/translation-log/` - per-file/per-subsystem translation decisions
- `docs/adr/` - cross-cutting architectural decisions that should not be buried in one translation log

Recommended ADR scope:

- target framework changes
- backend strategy changes
- save/RTC ownership changes
- verification oracle changes
- any decision that changes multiple milestones or multiple projects

Do **not** create a sprawl of extra doc buckets until there is a concrete need.

---

## Bottom line

The practical shape is:

- one core translated runtime project mirroring pret's folders
- one tiny platform-abstractions project
- one thin host/orchestration project
- one backend project per platform implementation
- one reusable verification library plus separate SameBoy interop
- two test projects backed by committed fixtures
- mirrored translation logs and a small ADR folder for durable decisions

That layout preserves source traceability, keeps NativeAOT viable, and gives multiple coding agents clear ownership boundaries without fighting the original pokegold organization.
