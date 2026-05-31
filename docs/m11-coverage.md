# M11 Coverage Note

Milestone M11 ships a complete set of overworld menus backed by real `PlayerState` /
`Bag` / `Party` / `DexData` data. This document enumerates what is implemented and
what is intentionally deferred.

---

## Implemented (M11.0 – M11.9)

| Feature | Status |
|---|---|
| **Start menu** (POKÉDEX / POKÉMON / PACK / SAVE / OPTION / EXIT) | ✅ |
| **Pack** — four pockets (ITEM / BALL / KEY ITEM / TM·HM) | ✅ |
| Pack — TOSS with quantity selector + YES/NO | ✅ |
| Pack — GIVE (Item pocket) via Party picker | ✅ |
| Pack — USE for HP-restore items via Party picker | ✅ (see scope below) |
| **Party** — list with HP bars; action submenu | ✅ |
| Party — SWITCH (reorder two slots) | ✅ |
| Party — ITEM (take held item back to bag) | ✅ |
| **Summary** — three pages (info / stats / moves) | ✅ |
| **Pokédex** — 251-entry scrollable list; owned-entry detail | ✅ |
| **Options** — text speed / box border / sound; persisted via save | ✅ |
| **Save menu** — drives existing SaveFile; confirm / cancel flow | ✅ |
| Item + Dex generated metadata (DataGen baked at build time) | ✅ |
| SaveData v3 round-trip + v2 migration | ✅ |

---

## Pack USE: implemented HP-restore items

`PackScene` recognises the following item IDs as HP-restore items and routes them
through the Party picker. Heal amount = `ItemData.Param` HP (or `MaxHp` when
`Param < 0`).

```
POTION          SUPER_POTION    HYPER_POTION    MAX_POTION
FULL_RESTORE    FRESH_WATER     SODA_POP        LEMONADE
MOOMOO_MILK     BERRY_JUICE     BERRY           GOLD_BERRY
RAGECANDYBAR
```

**Decision rationale:** The `FieldMenu` metadata does not distinguish HP-restore items
from other party-targeting items (`ITEMMENU_PARTY` is shared with status cures,
revives, vitamins, evolution stones, PP restores, and TMs). An explicit enumeration
is used as permitted by the M11.9 spec; it is documented here for maintainability.

**Known gap:** `FULL_RESTORE` heals HP but does *not* clear status conditions in this
implementation (status clearing is deferred to the full item-effect system, M17+).

---

## Deferred item-use effects (gate: "Can't use that here yet.")

The following categories of item-use are recognised by the USE action menu but routed
to the gated stub message. They will be implemented in the full item-effect system
(planned M17+).

| Category | Example items |
|---|---|
| Status cures | ANTIDOTE, BURN_HEAL, ICE_HEAL, AWAKENING, PARLYZ_HEAL, FULL_HEAL |
| Status-cure berries (field use) | PSNCUREBERRY, PRZCUREBERRY, BURNT_BERRY, ICE_BERRY, MINT_BERRY, MIRACLEBERRY |
| Revives | REVIVE, MAX_REVIVE, REVIVAL_HERB |
| Vitamins / stat-boosters | HP_UP, PROTEIN, IRON, CARBOS, CALCIUM, PP_UP, RARE_CANDY |
| Evolution stones | MOON_STONE, FIRE_STONE, THUNDERSTONE, WATER_STONE, LEAF_STONE, SUN_STONE |
| PP restores / elixirs | ETHER, MAX_ETHER, ELIXER, MAX_ELIXER, MYSTERYBERRY |
| Bitter herbs | ENERGYPOWDER, ENERGY_ROOT, HEAL_POWDER |
| TMs / HMs (teach move) | TM01–TM50, HM01–HM07 |
| Key-item field effects | BICYCLE (cycle field), SUPER_ROD (fish), SQUIRTBOTTLE (Sudowoodo), etc. |
| Repels | REPEL, SUPER_REPEL, MAX_REPEL |
| Escape Rope | ESCAPE_ROPE |
| SECRETPOTION (script-driven) | SECRETPOTION |

---

## Open seams / stubs in M11

- **Pokédex area / search modes** — area and search submenu entries exist in the
  original but are minimal stubs; no area data is rendered.
- **Dex cry / front sprite** — Pokédex entry detail shows text only; sprite and cry
  playback are deferred (art pipeline and audio integration, M14+).
- **STATUS screen / trainer card** — not in scope for M11; no entry in Start menu.
- **POKEGEAR** — not in scope for M11.
- **Field moves (CUT/SURF/FLY/etc.)** — Party action submenu lists detected field
  moves but dispatches to the M17 gate stub. Full HM field-use system is M17.
- **Party ITEM from Pack (double-give path)** — giving from Pack clears any prior held
  item and returns it to the bag. The Party's own ITEM action only handles *taking*
  a held item back; the give direction is exclusively driven from Pack.
- **Battle item use** — `BattleMenu` metadata is parsed but battle-side item dispatch
  is M13+.
