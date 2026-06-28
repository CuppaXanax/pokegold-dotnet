---
name: Route verification task
about: Track work needed to prove a route leg through the real runtime.
title: "[route] "
---

## Route leg

Start map/position:

End map/position:

## Source files to read

Relevant `maps\*.asm`, `engine\**\*.asm`, `constants\*.asm`, or `data\**\*.asm`:

## Runtime behavior to prove

Warps, collision, coord triggers, NPC triggers, battles, flags, save/load, or
other gates:

## Done when

- A runtime test drives the leg with real inputs.
- Assertions cover the route-critical gates.
- Any required ledger/status docs are updated.

