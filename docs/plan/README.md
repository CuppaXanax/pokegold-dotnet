# Pokémon Gold in F#

A native reimplementation of Pokémon Gold written in idiomatic **F#**, running on
**MonoGame**. The game's logic, data, and systems are expressed directly as F# — records,
discriminated unions, and functions — so the source reads as a clean specification of how
the game works.

## What this is

Pokémon Gold is, at heart, a collection of **data tables**, **small bytecode languages**, and
**turn-based state machines**. F# expresses each of these natively:

- **Data** (species, moves, items, types, trainers, maps) → immutable records and arrays.
- **Scripts** (events, text, movement, battle commands) → discriminated unions interpreted by
  exhaustive pattern matches.
- **Systems** (overworld, battle, menus, save) → functions over explicit game state.

MonoGame provides the surrounding shell: a window, the frame loop, input, audio output, and
texture presentation. The game produces a 160×144 image and a stream of sound each frame;
MonoGame puts them on screen and through the speakers.

## Principles

1. **The source is the spec.** Reading the F# should teach you how Pokémon Gold works. Names,
   types, and control flow mirror the game's concepts, not any underlying machine.
2. **Express, don't transcribe.** Each system is rebuilt in the most natural F# for its shape:
   unions for scripts, records for data, functions for logic.
3. **State is explicit and typed.** Game state lives in F# values with meaningful types. Reading
   and updating it is ordinary, total, well-typed code.
4. **MonoGame is the shell.** Platform concerns — window, timing, input, audio, presentation —
   belong to MonoGame so the F# stays about the game.
5. **Build a slice, then grow.** Get something playable early and let the architecture emerge from
   real systems rather than up-front design.

## Fidelity

The target is **behavioral**: the game plays correctly and looks right. Ported data and systems
are spot-checked against the original `pret/pokegold` disassembly (base stats, type chart, move
data, map layouts). The game reads and writes its own save format. Correctness is judged by
playing the game, not by matching any lower-level trace.

## Building blocks

| Concern   | Approach |
|-----------|----------|
| Language  | **F#** (.NET) |
| Shell     | **MonoGame** (DesktopGL) |
| Graphics  | Compose background, window, and sprite layers into a 160×144 image presented as a texture |
| Audio     | Reimplement the sound engine in F#; output through MonoGame audio |
| Data      | Derive tables from the `pret/pokegold` disassembly into F# data and resources |
| Reference | `pret/pokegold` ASM (this repo) and the analysis in `docs/recon/` |

## Direction

The work is sequenced bottom-up so there's something on screen early and each step exercises the
next: shell → graphics primitives → data pipeline → overworld → text → battle → save → audio →
outward.

The **executable plan** — ordered milestones with concrete deliverables, measurable acceptance
criteria, dependencies, open decisions, and risks — lives in [`plan.md`](plan.md).

## Reference material

`docs/recon/` holds an analysis of the `pret/pokegold` disassembly — source map, memory map, data
formats, execution flow — useful as a reference while deriving F# data and systems.
