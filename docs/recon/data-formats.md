# Data format ground truth

This note is repo-grounded and intended for future Claude instances with zero chat context. Offsets are zero-based within each record. When a point is not explicit in-tree, it is marked `UNCLEAR:` instead of guessed.

## 1) Pokémon base stats (`BASE_DATA_SIZE = 32` bytes)

The base-stats table is a fixed-width ROM table. `BASE_DATA_SIZE` is defined from the struct in `constants/pokemon_data_constants.asm`, and `data/pokemon/base_stats.asm` assembles one record per species with that exact width (`constants/pokemon_data_constants.asm:1-32`; `data/pokemon/base_stats.asm:21-22`; `data/pokemon/base_stats/bulbasaur.asm:1-25`).

`NUM_TM_HM = 57`, so the TM/HM bitfield is `(57 + 7) / 8 = 8` bytes (`constants/item_constants.asm:218-295`; `constants/pokemon_data_constants.asm:31-32`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | `BASE_DEX_NO` | Pokédex number byte stored in the record. |
| `0x01` | 1 | `BASE_HP` | Base HP. |
| `0x02` | 1 | `BASE_ATK` | Base Attack. |
| `0x03` | 1 | `BASE_DEF` | Base Defense. |
| `0x04` | 1 | `BASE_SPD` | Base Speed. |
| `0x05` | 1 | `BASE_SAT` | Base Special Attack. |
| `0x06` | 1 | `BASE_SDF` | Base Special Defense. |
| `0x07` | 1 | `BASE_TYPE_1` | Primary type. |
| `0x08` | 1 | `BASE_TYPE_2` | Secondary type. |
| `0x09` | 1 | `BASE_CATCH_RATE` | Catch rate. |
| `0x0a` | 1 | `BASE_EXP` | Base EXP yield. |
| `0x0b` | 1 | `BASE_ITEM_1` | Common wild held item. |
| `0x0c` | 1 | `BASE_ITEM_2` | Rare wild held item. |
| `0x0d` | 1 | `BASE_GENDER` | Gender ratio byte. |
| `0x0e` | 1 | — | `wBaseUnknown1`; copied into RAM but not named/used elsewhere. `UNCLEAR:` exact meaning (`ram/wram.asm:2149-2152`). |
| `0x0f` | 1 | `BASE_EGG_STEPS` | Egg cycles to hatch. |
| `0x10` | 1 | — | `wBaseUnknown2`; copied into RAM but not named/used elsewhere. `UNCLEAR:` exact meaning (`ram/wram.asm:2151-2153`). |
| `0x11` | 1 | `BASE_PIC_SIZE` | Packed dimensions byte. Build tool emits `(width_tiles << 4) | width_tiles`, so current values are square `5x5`, `6x6`, or `7x7` (`tools/png_dimensions.c:6-13`; `engine/gfx/load_pics.asm:73-76`). |
| `0x12` | 2 | `BASE_FRONTPIC` | Unused beta frontpic pointer (`dw`, little-endian in ROM). |
| `0x14` | 2 | `BASE_BACKPIC` | Unused beta backpic pointer (`dw`, little-endian in ROM). |
| `0x16` | 1 | `BASE_GROWTH_RATE` | Growth-rate enum. |
| `0x17` | 1 | `BASE_EGG_GROUPS` | Packed egg groups: high nibble = group 1, low nibble = group 2 (`dn`). |
| `0x18` | 8 | `BASE_TMHM` | TM/HM learnset bitfield. Bit `n` corresponds to TM/HM number `n + 1`; the `tmhm` macro sets byte `(TMNUM - 1) / 8`, bit `(TMNUM - 1) % 8` (`data/pokemon/base_stats.asm:2-19`). |

## 2) Move data (`MOVE_LENGTH = 7` bytes)

Move records are fixed-width 7-byte entries. `NO_MOVE` (`00`) has no record; the first table entry is move `01` (`POUND`) (`constants/battle_constants.asm:48-57`; `constants/move_constants.asm:7-10`; `data/moves/moves.asm:3-17`).

Accuracy and effect-chance bytes are **not** literal percentages in ROM; the source uses the `percent` macro, which expands to `* $ff / 100`, so `100 percent` assembles to `$ff` (`macros/data.asm:3-26`; `data/moves/moves.asm:3-10`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | `MOVE_ANIM` | Battle animation id. |
| `0x01` | 1 | `MOVE_EFFECT` | Effect routine id. |
| `0x02` | 1 | `MOVE_POWER` | Base power. |
| `0x03` | 1 | `MOVE_TYPE` | Type id. |
| `0x04` | 1 | `MOVE_ACC` | Accuracy threshold on a `0..255` scale (`percent` macro in source). |
| `0x05` | 1 | `MOVE_PP` | Base PP (asserted `<= 40`). |
| `0x06` | 1 | `MOVE_CHANCE` | Secondary-effect chance on a `0..255` scale (`percent` macro in source). |

## 3) Party Pokémon structure vs. box Pokémon

### BoxMon core (`BOXMON_STRUCT_LENGTH = 32` bytes)

`box_struct` in `macros/ram.asm` matches the offset constants in `constants/pokemon_data_constants.asm` (`macros/ram.asm:7-27`; `constants/pokemon_data_constants.asm:75-107`).

`MON_OT_ID` is written low byte then high byte when a new mon is created (`engine/pokemon/move_mon.asm:144-149`). `MON_EXP` is written high/mid/low (`engine/pokemon/move_mon.asm:156-165`). DVs are packed Attack/Defense in byte 1 and Speed/Special in byte 2; HP DV is derived from their low bits (`engine/gfx/load_pics.asm:4-17`; `engine/pokemon/move_mon.asm:1496-1518`). Party HP uses high byte at `MON_HP` and low byte at `MON_HP + 1` (`engine/items/item_effects.asm:1800-1825`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | `MON_SPECIES` | Species id. |
| `0x01` | 1 | `MON_ITEM` | Held item id. |
| `0x02` | 4 | `MON_MOVES` | Four move ids. |
| `0x06` | 2 | `MON_OT_ID` | Original trainer id, low byte then high byte. |
| `0x08` | 3 | `MON_EXP` | Experience, high/mid/low bytes. |
| `0x0b` | 2 | `MON_HP_EXP` | HP stat-exp word. |
| `0x0d` | 2 | `MON_ATK_EXP` | Attack stat-exp word. |
| `0x0f` | 2 | `MON_DEF_EXP` | Defense stat-exp word. |
| `0x11` | 2 | `MON_SPD_EXP` | Speed stat-exp word. |
| `0x13` | 2 | `MON_SPC_EXP` | Special stat-exp word. |
| `0x15` | 2 | `MON_DVS` | Packed DVs: byte 0 = Atk hi nibble / Def low nibble; byte 1 = Spd hi nibble / Spc low nibble. |
| `0x17` | 4 | `MON_PP` | Current PP for the four moves. Upper two bits = PP Ups used; lower six bits = current PP (`constants/pokemon_data_constants.asm:215-219`). |
| `0x1b` | 1 | `MON_HAPPINESS` | Happiness/friendship. |
| `0x1c` | 1 | `MON_POKERUS` | Pokerus status byte. |
| `0x1d` | 1 | — | Unused 1. |
| `0x1e` | 1 | — | Unused 2. |
| `0x1f` | 1 | `MON_LEVEL` | Current level. |

### PartyMon extension (`PARTYMON_STRUCT_LENGTH = 48` bytes)

A `party_struct` is a `box_struct` plus 16 live-battle bytes (`macros/ram.asm:29-42`; `constants/pokemon_data_constants.asm:95-107`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x20` | 1 | `MON_STATUS` | Non-volatile status byte. |
| `0x21` | 1 | — | Unused filler byte. |
| `0x22` | 2 | `MON_HP` | Current HP, high byte then low byte. |
| `0x24` | 2 | `MON_MAXHP` | Max HP, high byte then low byte. |
| `0x26` | 2 | `MON_ATK` | Current Attack stat, big-endian word (`macros/ram.asm:35-40`). |
| `0x28` | 2 | `MON_DEF` | Current Defense stat, big-endian word. |
| `0x2a` | 2 | `MON_SPD` | Current Speed stat, big-endian word. |
| `0x2c` | 2 | `MON_SAT` | Current Special Attack stat, big-endian word. |
| `0x2e` | 2 | `MON_SDF` | Current Special Defense stat, big-endian word. |

### Party wrapper blob in WRAM (`wPartyCount .. wPartyMonNicknamesEnd = 428` bytes)

The in-memory party block is not just six `PartyMon`s; OT names and nicknames live in parallel arrays (`ram/wram.asm:2667-2691`; `constants/text_constants.asm:1-10`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x000` | 1 | `wPartyCount` | Number of party mons. |
| `0x001` | 6 | `wPartySpecies` | Species list for up to six mons. |
| `0x007` | 1 | `wPartyEnd` | Terminator slot for older code. |
| `0x008` | `6 * 48 = 288` | `wPartyMons` | Six `PartyMon` records. |
| `0x128` | `6 * 11 = 66` | `wPartyMonOTs` | Six OT-name strings (`NAME_LENGTH = 11`). |
| `0x16a` | `6 * 11 = 66` | `wPartyMonNicknames` | Six nickname strings (`MON_NAME_LENGTH = 11`). |

### Box wrapper formats

The active box in SRAM uses `curbox`; stored numbered boxes use `box = curbox + 2 padding bytes` (`macros/ram.asm:99-124`; `constants/pokemon_data_constants.asm:117-122`; `ram/sram.asm:107-160`).

#### `curbox` / active box (`0x44e = 1102` bytes)

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x000` | 1 | Count | Number of mons in box. |
| `0x001` | 21 | Species list | 20 species slots + 1 terminator slot. |
| `0x016` | `20 * 32 = 640` | BoxMons | Twenty `box_struct` records. |
| `0x296` | `20 * 11 = 220` | OT names | Twenty OT strings. |
| `0x372` | `20 * 11 = 220` | Nicknames | Twenty nickname strings. |

#### Stored numbered box (`BOX_LENGTH = 0x450 = 1104` bytes)

Same as `curbox`, plus `0x44e-0x44f`: 2 padding bytes (`macros/ram.asm:121-124`; `constants/pokemon_data_constants.asm:121`).

## 4) Trainer party definitions

Trainer groups are a pointer table; each group contains back-to-back trainer definitions. `ReadTrainerParty` finds trainer `n` within a class by scanning for `-1` record terminators, skips the name string up to `@`, reads the 1-byte trainer type, then dispatches to one of four parsers (`data/trainers/party_pointers.asm:3-72`; `data/trainers/parties.asm:3-12`; `engine/battle/read_trainer_party.asm:17-69,79-85`; `constants/trainer_data_constants.asm:35-40`).

### Trainer definition prefix

| Part | Size | Description |
|---|---:|---|
| Name | variable | Charmap string terminated by `@` (`$50`). |
| Type | 1 | `TRAINERTYPE_*` enum. |
| Party payload | variable | Repeated mon entries of a format determined by type. |
| Terminator | 1 | `0xff` as the next level byte ends the party. |

### Party payload variants

| Type | Constant | Per-mon layout | Bytes/mon |
|---|---|---|---:|
| 0 | `TRAINERTYPE_NORMAL` | `level, species` | 2 |
| 1 | `TRAINERTYPE_MOVES` | `level, species, move1, move2, move3, move4` | 6 |
| 2 | `TRAINERTYPE_ITEM` | `level, species, item` | 3 |
| 3 | `TRAINERTYPE_ITEM_MOVES` | `level, species, item, move1, move2, move3, move4` | 7 |

`TRAINERTYPE_ITEM_MOVES` is implemented in `ReadTrainerParty`, but this repo's `data/trainers/parties.asm` appears not to use it (`engine/battle/read_trainer_party.asm:209-274`; `data/trainers/parties.asm:9-10`; no `TRAINERTYPE_ITEM_MOVES` hits in that file).

Special case: `CAL2` is not read from `data/trainers/parties.asm`; it loads a raw `TRAINERTYPE_MOVES` payload from `sMysteryGiftTrainer` in SRAM (`engine/battle/read_trainer_party.asm:17-24,71-77`; `ram/wram.asm:594-600`).

## 5) Level-up movesets

After the evolution list terminator, each species has a learnset encoded as repeated 2-byte pairs `level, move`, terminated by a single `0x00` byte (`data/pokemon/evos_attacks.asm:3-14`).

| Bytes | Meaning |
|---|---|
| `level` | Learn level. |
| `move` | Move id. |
| `0x00` | End of learnset. |

The list is stored in increasing level order by convention (`data/pokemon/evos_attacks.asm:11-13`).

## 6) Evolution data

Each species starts with zero or more evolution records, then a single `0x00` terminator byte, then the level-up learnset described above (`data/pokemon/evos_attacks.asm:3-14`; `constants/pokemon_data_constants.asm:138-156`).

| Method | Bytes | Layout |
|---|---:|---|
| `EVOLVE_LEVEL` | 3 | `method, level, species` |
| `EVOLVE_ITEM` | 3 | `method, item, species` |
| `EVOLVE_TRADE` | 3 | `method, held_item_or_$ff, species` |
| `EVOLVE_HAPPINESS` | 3 | `method, trigger(TR_ANYTIME/TR_MORNDAY/TR_NITE), species` |
| `EVOLVE_STAT` | 4 | `method, level, relation(ATK_GT_DEF / ATK_LT_DEF / ATK_EQ_DEF), species` |
| Terminator | 1 | `0x00` = end of evolution list |

Species with no evolutions begin immediately with `db 0`, then their learnset (`data/pokemon/evos_attacks.asm:51-67`).

## 7) Wild encounter tables

### Grass encounter records (`GRASS_WILDDATA_LENGTH = 47` bytes)

Grass tables in Johto/Kanto/swarm files use `def_grass_wildmons` / `end_grass_wildmons`, which assert exactly 47 bytes per record. The whole table is terminated by `db -1` after the last map record (`constants/pokemon_data_constants.asm:160-165`; `macros/asserts.asm:60-72`; `data/wild/johto_grass.asm:5-31,2374`; `data/wild/swarm_grass.asm:6-32,148`).

`map_id` stores map group then map number (`macros/scripts/maps.asm:1-6`). Grass slot probabilities come from `GrassMonProbTable`: slots 0..6 are 30%, 30%, 20%, 10%, 5%, 4%, 1% (`data/wild/probabilities.asm:6-15`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | Map group | `GROUP_*`. |
| `0x01` | 1 | Map number | `MAP_*`. |
| `0x02` | 1 | Morning encounter rate | Percent threshold byte (`percent` macro). |
| `0x03` | 1 | Day encounter rate | Percent threshold byte. |
| `0x04` | 1 | Night encounter rate | Percent threshold byte. |
| `0x05` | 14 | Morning slots | 7 × `(level, species)`. |
| `0x13` | 14 | Day slots | 7 × `(level, species)`. |
| `0x21` | 14 | Night slots | 7 × `(level, species)`. |

### Water/surf encounter records (`WATER_WILDDATA_LENGTH = 9` bytes)

Water tables use `def_water_wildmons` / `end_water_wildmons`, which assert 9 bytes per record. Whole tables end with `db -1` (`constants/pokemon_data_constants.asm:160-165`; `macros/asserts.asm:74-86`; `data/wild/johto_water.asm:5-10,264`; `data/wild/swarm_water.asm:6-13`).

Water slot probabilities are 60%, 30%, 10% (`data/wild/probabilities.asm:17-22`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | Map group | `GROUP_*`. |
| `0x01` | 1 | Map number | `MAP_*`. |
| `0x02` | 1 | Encounter rate | Percent threshold byte (`percent` macro). |
| `0x03` | 2 | Slot 0 | `(level, species)` |
| `0x05` | 2 | Slot 1 | `(level, species)` |
| `0x07` | 2 | Slot 2 | `(level, species)` |

There is no morning/day/night split for surfing.

### Fishing data

Fishing is indirect: the map header stores a 1-byte fish-group id, which indexes `FishGroups` (`constants/map_data_constants.asm:9-18,41-57`; `engine/events/fish.asm:10-18`; `data/maps/maps.asm:1-15`).

#### Fish group record (`FISHGROUP_DATA_LENGTH = 7` bytes)

`fishgroup` expands to one bite-chance byte plus three little-endian pointers (old/good/super rod) (`constants/pokemon_data_constants.asm:163-165`; `data/wild/fish.asm:3-25`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | Bite chance | Percent threshold byte (`percent` macro). |
| `0x01` | 2 | Old Rod table pointer | Little-endian pointer. |
| `0x03` | 2 | Good Rod table pointer | Little-endian pointer. |
| `0x05` | 2 | Super Rod table pointer | Little-endian pointer. |

#### Rod table entries (3 bytes each)

The fishing engine walks a rod table as repeated `(chance, species, level_or_time_group)` triples until the cumulative chance matches (`engine/events/fish.asm:45-65`). Old Rod tables in `data/wild/fish.asm` have 3 entries; Good/Super Rod tables have 4 (`data/wild/fish.asm:27-208`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | Cumulative chance | Percent threshold byte (`percent` macro). |
| `0x01` | 1 | Species | Species id, or `0` for time-based lookup. |
| `0x02` | 1 | Level / time-group index | If species != 0, this is the level. If species == 0, it indexes `TimeFishGroups`. |

`TimeFishGroups` entries are fixed 4-byte records: `day_species, day_level, nite_species, nite_level` (`engine/events/fish.asm:71-90`; `data/wild/fish.asm:210-233`). Fishing time-of-day only distinguishes day vs. night here.

### Bug-Catching Contest table (special case)

The contest does **not** use map-id records. `ContestMons` is a flat table of 4-byte entries `chance, species, min_level, max_level`; the engine subtracts the chance byte until it underflows. The final `db -1, VENOMOTH, 30, 40` record is therefore a guaranteed fallback, not a separate terminator (`data/wild/bug_contest_mons.asm:1-13`; `engine/overworld/events.asm:1194-1225`).

## 8) Map headers

There are two important header layers:

1. the 9-byte per-map record in `data/maps/maps.asm`
2. the variable-length map-attributes record in `data/maps/attributes.asm`

(`constants/map_data_constants.asm:8-18`; `data/maps/maps.asm:1-15`; `data/maps/attributes.asm:1-17`; `macros/ram.asm:126-136`).

### Per-map record (`MAP_LENGTH = 9` bytes)

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | `MAP_MAPATTRIBUTES_BANK` | Bank of the attributes/scripts/events record. |
| `0x01` | 1 | `MAP_TILESET` | `TILESET_*` id. |
| `0x02` | 1 | `MAP_ENVIRONMENT` | `TOWN`, `ROUTE`, `INDOOR`, etc. |
| `0x03` | 2 | `MAP_MAPATTRIBUTES` | Pointer to `*_MapAttributes` record. |
| `0x05` | 1 | `MAP_LOCATION` | Landmark/location id. |
| `0x06` | 1 | `MAP_MUSIC` | Default music id. |
| `0x07` | 1 | Packed flags | High nibble = phone-service suppression flag, low nibble = map palette (`dn \6, \7`). |
| `0x08` | 1 | `MAP_FISHGROUP` | Fish group id for fishing encounters. |

### Map-attributes record (base = 12 bytes, plus 12 bytes per connection)

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | Border block | Metatile id used beyond map edges. |
| `0x01` | 1 | Height | Map height in metatiles. |
| `0x02` | 1 | Width | Map width in metatiles. |
| `0x03` | 1 | Blocks bank | Bank of `*_Blocks`. |
| `0x04` | 2 | Blocks pointer | Pointer to metatile/block data. |
| `0x06` | 1 | Scripts/events bank | Bank of `*_MapScripts` / `*_MapEvents`. |
| `0x07` | 2 | Scripts pointer | Pointer to `*_MapScripts`. |
| `0x09` | 2 | Events pointer | Pointer to `*_MapEvents`. |
| `0x0b` | 1 | Connection flags | Bitfield of `NORTH/SOUTH/WEST/EAST`. |

Connections then follow in **north, south, west, east** order, one 12-byte `map_connection_struct` per present direction (`data/maps/attributes.asm:19-97`; `macros/ram.asm:126-136`).

### Connection record (`map_connection_struct`, 12 bytes)

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | Connected map group | `GROUP_*`. |
| `0x01` | 1 | Connected map number | `MAP_*`. |
| `0x02` | 2 | Connection strip pointer | Source block-strip pointer in connected map block data. |
| `0x04` | 2 | Connection strip location | Destination pointer in `wOverworldMapBlocks`. |
| `0x06` | 1 | Strip length | Number of blocks copied. |
| `0x07` | 1 | Connected map width | Width in metatiles. |
| `0x08` | 1 | Strip Y offset | Signed tile offset. |
| `0x09` | 1 | Strip X offset | Signed tile offset. |
| `0x0a` | 2 | Connection window | Window pointer in `wOverworldMapBlocks`. |

## 9) Map events and script headers

A map file such as `maps/NewBarkTown.asm` defines two labels referenced by the map-attributes record: `*_MapScripts` and `*_MapEvents` (`maps/NewBarkTown.asm:6-13,294-316`). Sizes come from `constants/script_constants.asm`; entry macros come from `macros/scripts/maps.asm` (`constants/script_constants.asm:103-140`; `macros/scripts/maps.asm:12-140`).

### `*_MapScripts` layout

| Part | Size | Description |
|---|---:|---|
| Scene-script count | 1 | Number of scene entries. |
| Scene entry | 4 each | `dw script_ptr`, `dw 0` filler (`scene_script`). |
| Callback count | 1 | Number of callback entries. |
| Callback entry | 3 each | `db type`, `dw script_ptr` (`callback`). |

### `*_MapEvents` layout

Every map event header begins with two filler bytes, then four counted sublists (`maps/NewBarkTown.asm:294-316`).

| Part | Size | Description |
|---|---:|---|
| Filler | 2 | Always `db 0, 0` in map sources. |
| Warp count | 1 | Number of warp entries. |
| Warp entry | 5 each | See below. |
| Coord-event count | 1 | Number of coord entries. |
| Coord entry | 8 each | See below. |
| BG-event count | 1 | Number of BG entries. |
| BG entry | 5 each | See below. |
| Object-event count | 1 | Number of object entries. |
| Object entry | 13 each | See below. |

#### Warp entry (`WARP_EVENT_SIZE = 5`)

Stored as `y, x, warp_id, map_group, map_number` (`macros/scripts/maps.asm:57-71`; `constants/script_constants.asm:103-110`).

#### Coord entry (`COORD_EVENT_SIZE = 8`)

Stored as `scene, y, x, 0, script_ptr_lo, script_ptr_hi, 0, 0` (`macros/scripts/maps.asm:73-89`; `constants/script_constants.asm:103-110`). `SCENE_ALWAYS = -1` means “always active” (`constants/script_constants.asm:112-114`).

#### BG entry (`BG_EVENT_SIZE = 5`)

Stored as `y, x, function, script_ptr_lo, script_ptr_hi` (`macros/scripts/maps.asm:91-105`; `constants/script_constants.asm:103-128`).

#### Object entry (`OBJECT_EVENT_SIZE = 13`)

Stored layout is exactly what `object_event` emits; it is **not** the full 16-byte `map_object` RAM struct (`macros/scripts/maps.asm:113-140`; `constants/script_constants.asm:109-110`; `constants/map_object_constants.asm:79-105`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | Sprite | `SPRITE_*` id. |
| `0x01` | 1 | Y coord | Stored as `y + 4`. |
| `0x02` | 1 | X coord | Stored as `x + 4`. |
| `0x03` | 1 | Movement | `SPRITEMOVEDATA_*`. |
| `0x04` | 1 | Radius | Packed nybbles: high = Y radius, low = X radius. |
| `0x05` | 1 | Hour 1 | Start hour, or `-1` to interpret byte `0x06` as time-of-day flags. |
| `0x06` | 1 | Hour 2 / time-of-day | End hour, or MORN/DAY/NITE mask when byte `0x05 == -1`. |
| `0x07` | 1 | Palette/type | High nibble = palette override, low nibble = `OBJECTTYPE_*`. |
| `0x08` | 1 | Sight range | Used by trainers. |
| `0x09` | 2 | Script pointer | Little-endian pointer. |
| `0x0b` | 2 | Event flag | Little-endian flag id, or `-1` for always present. |

## 10) Sprite / OAM data

### Hardware OAM entry (`sprite_oam_struct`, 4 bytes)

`wShadowOAM` is an array of 40 OAM entries, each 4 bytes: Y, X, tile id, attributes (`ram/wram.asm:145-150`; `macros/ram.asm:327-338`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | Y | Screen Y. |
| `0x01` | 1 | X | Screen X. |
| `0x02` | 1 | Tile id | OAM tile number. |
| `0x03` | 1 | Attributes | Priority / flips / palette / VRAM bank bits. |

### Overworld sprite metadata (`NUM_SPRITEDATA_FIELDS = 6` bytes each)

`OverworldSprites` is the master metadata table for NPC/player/object graphics (`constants/sprite_data_constants.asm:1-14`; `data/sprites/sprites.asm:1-105`). The ROM assets themselves are included from `gfx/sprites/*.2bpp`; the repo also keeps matching source PNGs in the same directory (`gfx/sprites.asm:1-101`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 2 | GFX pointer | Little-endian pointer to `.2bpp` sprite sheet (`data/sprites/sprites.asm:1-5`). |
| `0x02` | 1 | Size | Sheet length in 8x8 tiles (`4` or `12` in this table). |
| `0x03` | 1 | GFX bank | Bank of the sprite sheet. |
| `0x04` | 1 | Type | `WALKING_SPRITE`, `STANDING_SPRITE`, or `STILL_SPRITE`. |
| `0x05` | 1 | Default palette | `PAL_OW_*` enum. |

### Facing/OAM composition tables

`Facings` is a pointer table. Each facing data block begins with a count byte, then `count` 4-byte records `dy, dx, attribute_flags, tile_index` (`data/sprites/facings.asm:1-43`). The overworld renderer interprets those records into OAM by adding `dy/dx` to the sprite position, using `attribute_flags` for absolute-vs-relative tile handling and flip merging, and adding `tile_index` to the sprite's current base tile unless `ABSOLUTE_TILE_ID` is set (`engine/overworld/map_objects.asm:2891-2934`; `data/sprites/facings.asm:44-238`).

Common organization observed in the facing tables:

- normal human/NPC sprites are composed as 2x2 OAM blocks (`count = 4`)
- fishing facings append a fifth rod tile (`count = 5`)
- shadows use 2 OAM entries
- big dolls use 14 or 16 OAM entries
- `OverworldSprites` marks most walking/standing characters as 12-tile sheets, while simple objects like rocks, Poké Balls, trees, etc. use 4-tile sheets (`data/sprites/sprites.asm:7-105`)

`UNCLEAR:` I did not fully trace the renderer-side convention for the `$80`-based relative tile indices used by walking facings in `data/sprites/facings.asm`; the count/record layout is exact, but the semantic meaning of those high-bit tile-index variants was not exhaustively reconstructed in this pass.

## 11) Text encoding and text commands

There are **two layers**:

1. a scripted text-command stream (`TX_*` commands)
2. inline charmap strings interpreted by `PlaceString`

(`macros/scripts/text.asm:1-171`; `home/text.asm:578-640`; `constants/charmap.asm:1-418`).

### Scripted text-command stream

`PrintTextboxTextAt` / `DoTextUntilTerminator` read bytes until `TX_END` (`$50`), dispatching command ids `00-16` through `TextCommands` (`home/text.asm:578-640`).

| Byte | Command | Args after opcode | Meaning |
|---|---|---:|---|
| `00` | `TX_START` | inline charmap string until `@` | Print inline text (`home/text.asm:643-653`). |
| `01` | `TX_RAM` | 2 | Little-endian RAM pointer to a string (`home/text.asm:655-666`). |
| `02` | `TX_BCD` | 3 | LE pointer + 1 BCD-print flags byte (`home/text.asm:693-708`). |
| `03` | `TX_MOVE` | 2 | Raw BC cursor destination bytes (`home/text.asm:710-718`). |
| `04` | `TX_BOX` | 4 | LE pointer + height + width; draws a text box (`home/text.asm:720-735`). |
| `05` | `TX_LOW` | 0 | Move cursor to low textbox line (`home/text.asm:737-740`). |
| `06` | `TX_PROMPT_BUTTON` | 0 | Wait for A/B with arrow (`home/text.asm:742-755`). |
| `07` | `TX_SCROLL` | 0 | Scroll text up two lines (`home/text.asm:757-766`). |
| `08` | `TX_START_ASM` | 0 | Execute assembly at current HL (`home/text.asm:768-770`). |
| `09` | `TX_DECIMAL` | 3 | LE pointer + packed `bytes/digits` nybble byte (`home/text.asm:772-794`; `macros/scripts/text.asm:87-92`). |
| `0a` | `TX_PAUSE` | 0 | Pause 30 frames or until A/B (`home/text.asm:796-809`). |
| `0b` | `TX_SOUND_DEX_FANFARE_50_79` | 0 | Sound command (`home/text.asm:811-863`). |
| `0c` | `TX_DOTS` | 1 | Print that many `…` with delays (`home/text.asm:865-891`). |
| `0d` | `TX_WAIT_BUTTON` | 0 | Wait for A/B without arrow (`home/text.asm:893-900`). |
| `0e` | `TX_SOUND_DEX_FANFARE_20_49` | 0 | Sound command. |
| `0f` | `TX_SOUND_ITEM` | 0 | Sound command. |
| `10` | `TX_SOUND_CAUGHT_MON` | 0 | Sound command. |
| `11` | `TX_SOUND_DEX_FANFARE_80_109` | 0 | Sound command. |
| `12` | `TX_SOUND_FANFARE` | 0 | Sound command. |
| `13` | `TX_SOUND_SLOT_MACHINE_START` | 0 | Sound command. |
| `14` | `TX_STRINGBUFFER` | 1 | String-buffer selector `0..6` (`home/text.asm:902-926`). |
| `15` | `TX_DAY` | 0 | Print weekday name (`home/text.asm:928-940`). |
| `16` | `TX_FAR` | 3 | LE pointer + bank; recursively parse far-bank text (`home/text.asm:668-691`). |
| `50` | `TX_END` | 0 | End command stream (`macros/scripts/text.asm:165-170`; `home/text.asm:590-595`). |

### Inline string control bytes (common English-text meanings)

Inside a `TX_START` inline string, `PlaceString` interprets control bytes from `constants/charmap.asm` and `home/text.asm` (`constants/charmap.asm:3-35`; `home/text.asm:177-275`). Common ones:

| Byte | Token | Meaning |
|---|---|---|
| `00` | `<NULL>` | Debug leftover; prints an error diagnostic (`home/text.asm:493-508`). |
| `16` | `<CR>` | Carriage-return control char in charmap. |
| `1f` | `<BSP>` | Breakable space; usually becomes a space. |
| `22` | `<LF>` | Line feed. |
| `24` | `<POKE>` | Expands to `<PO><KE>`. |
| `25` | `<WBR>` | Word-break opportunity; usually skipped. |
| `38` | `<RED>` | Insert Red's name. |
| `39` | `<GREEN>` | Insert Green's name. |
| `3f` | `<ENEMY>` | Insert enemy trainer/battler name. |
| `49` | `<MOM>` | Insert Mom's name. |
| `4a` | `<PKMN>` | Expands to `<PK><MN>`. |
| `4b` | `<_CONT>` | Continue text with pause+scroll. |
| `4c` | `<SCROLL>` | Scroll without pause. |
| `4e` | `<NEXT>` | Move down one line. |
| `4f` | `<LINE>` | Jump to bottom line. |
| `50` | `@` | String terminator for `PlaceString`. |
| `51` | `<PARA>` | New paragraph. |
| `52` | `<PLAYER>` | Insert player name. |
| `53` | `<RIVAL>` | Insert rival name. |
| `54` | `#` | Expands to `POKé`. |
| `55` | `<CONT>` | Literal continue marker text flow. |
| `56` | `<……>` | Six-dot ellipsis token. |
| `57` | `<DONE>` | End textbox. |
| `58` | `<PROMPT>` | End textbox with prompt. |
| `59` | `<TARGET>` | Insert move target's name. |
| `5a` | `<USER>` | Insert move user's name. |
| `5b` | `<PC>` | `PC`. |
| `5c` | `<TM>` | `TM`. |
| `5d` | `<TRAINER>` | `TRAINER`. |
| `5e` | `<ROCKET>` | `ROCKET`. |
| `5f` | `<DEXEND>` | Old Gen 1 dex-entry end marker. |

### Printable Latin/English ranges

Primary English-font mappings are in `constants/charmap.asm:37-208`.

- `80-99` = `A-Z`
- `9a-9f` = `(` `)` `:` `;` `[` `]`
- `a0-b9` = `a-z`
- `c0-c5` = `Ä Ö Ü ä ö ü`
- `d0-d6` = `'d 'l 'm 'r 's 't 'v`
- `df` = `←`
- `e0` = `'`
- `e1-e2` = `<PK> <MN>`
- `e3` = `-`
- `e6-e9` = `? ! . &`
- `ea-ef` = `é → ▷ ▶ ▼ ♂`
- `f0-f5` = `¥ × <DOT> / , ♀`
- `f6-ff` = `0-9`

### Context-dependent overlay glyphs

Bytes `60-78` and a few others have multiple meanings depending on which font sheet is loaded (`constants/charmap.asm:37-96`). Examples:

- extra-font glyphs: `60-6d`, `70-78`
- battle-extra overrides: `6e=<LV>`, `70=<DO>`, `71=◀`, `72=『`, `73=<ID>`, `74=№`
- misc overrides: `60=■`, `61=▲`, `62=☎`, `6e=′`, `6f=″`, `3f=⁂`

`UNCLEAR:` exact runtime font selection for every one of these overlapping bytes was not exhaustively traced here; use the charmap plus the calling UI/font loader when precision is needed.

### Japanese supplement

Some untranslated strings still use the Japanese mappings in `constants/charmap.asm:210-418`.

- named JP control tokens: `14`, `18`, `1d`, `1e`, `1f`, `22`, `23`, `24`, `25`, `35`, `36`, `37`, `4a`
- voiced katakana: `05-13 = ガ..ド`, `19-1c = バ ビ ブ ボ`, `40-48 = パ ピ プ ポ ぱ ぴ ぷ ぺ ぽ`
- voiced hiragana: `26-34 = が..ど`, `3a-3e = ば..ぼ`
- punctuation/specials: `70=「`, `71=」`, `73=』`, `74=・`, `75=⋯`, `7f=　`
- katakana core: `80-ab = ア..ン`, `ac-af = ッ ャ ュ ョ`, `b0 = ィ`
- hiragana core: `b1-de = あ..ん`, `df = っ`, `e0-e2 = ゃ ゅ ょ`
- extras: `e3=ー`, `e4=ﾟ`, `e5=ﾞ`, `e6=？`, `e7=！`, `e8=。`, `e9=ァ`, `ea=ゥ`, `eb=ェ`, `f0=円`, `f2=．`, `f3=／`, `f4=ォ`, `f6-ff = ０-９`

## 12) Tileset data

### Tileset master record (`TILESET_LENGTH = 15` bytes)

`Tilesets` is a fixed-width table of 15-byte records (`data/tilesets.asm:1-45`; `constants/tileset_constants.asm:33-35`; `macros/data.asm:79-84`). `dba` stores **bank first, then little-endian address**.

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 1 | GFX bank | Bank of compressed 2bpp graphics. |
| `0x01` | 2 | GFX ptr | Pointer to `*GFX`. |
| `0x03` | 1 | Metatile bank | Bank of `*Meta`. |
| `0x04` | 2 | Metatile ptr | Pointer to metatile data. |
| `0x06` | 1 | Collision bank | Bank of `*Coll`. |
| `0x07` | 2 | Collision ptr | Pointer to collision data. |
| `0x09` | 2 | Animation ptr | Pointer to `*Anim` (bank fixed elsewhere in WRAM comments). |
| `0x0b` | 2 | Unused ptr | Always `NULL` in this table. |
| `0x0d` | 2 | Palette-map ptr | Pointer to `*PalMap`. |

### Component formats

- **GFX**: LZ-compressed 2bpp tile graphics, decompressed to VRAM (`gfx/tilesets.asm:7-28`; `home/map.asm:1729-1752`).
- **Metatile data**: 16 bytes per metatile. The map loader multiplies block id by 16 and copies a `4x4` tile block (`constants/gfx_constants.asm:5-10`; `home/map.asm:148-183`).
- **Collision data**: 4 bytes per metatile; `tilecoll` writes four collision bytes, one per quadrant (`gfx/tilesets.asm:1-5`; `constants/collision_constants.asm:7-136`).
- **Palette map**: packed palette descriptors, two 4-bit entries per byte (`tilepal` uses `dn`). The palette loader indexes this table by tile id and reads low/high nybbles as palette/VRAM-bank descriptors (`gfx/tileset_palette_maps.asm:1-9`; `engine/tilesets/map_palettes.asm:1-59`).

## 13) Save data format in SRAM

SRAM layout is declared in `ram/sram.asm`; save/load/checksum behavior is in `engine/menus/save.asm` (`ram/sram.asm:1-175`; `engine/menus/save.asm:375-566,624-825,851-1051`; `constants/misc_constants.asm:29-31`).

### Major SRAM sections

| Section | Contents |
|---|---|
| `Scratch` | Temporary decompression buffers (`ram/sram.asm:1-12`). |
| `SRAM Bank 0` | Party mail, mailbox, mystery gift, RTC/lucky-number misc (`ram/sram.asm:14-64`). |
| `Backup Save 1` | Backup copy of `wPlayerData3`, `wPokemonData`, `wPlayerData1` (`ram/sram.asm:66-71`). |
| `Save` | Primary save: options, check values, game data, checksum (`ram/sram.asm:87-105`). |
| `Active Box` | `sBox:: curbox sBox` (active PC box only, no 2-byte padding) (`ram/sram.asm:107-110`). |
| `Link Battle Data` | Link battle win/loss/draw records (`ram/sram.asm:112-125`). |
| `SRAM Hall of Fame` | 30 Hall of Fame teams (`ram/sram.asm:127-134`). |
| `Backup Save 2` | Backup copy of `wPlayerData2` (`ram/sram.asm:137-140`). |
| `Boxes 1-7` | Saved numbered PC boxes 1-7 (`ram/sram.asm:142-156`). |
| `Boxes 8-14` | Saved numbered PC boxes 8-14 (`ram/sram.asm:157-164`). |
| `Backup Save 3` | Backup options, backup check values, backup `wCurMapData`, backup checksum (`ram/sram.asm:167-174`). |

### Primary save record

The primary save section is laid out as:

| Order | Field |
|---|---|
| 1 | `sOptions` |
| 2 | `sCheckValue1 = 99` |
| 3 | `sGameData = sPlayerData + sCurMapData + sPokemonData` |
| 4 | `sChecksum` |
| 5 | `sCheckValue2 = 127` |

(`ram/sram.asm:87-105`; `constants/misc_constants.asm:29-31`).

`SavePlayerData` copies `wPlayerData` then `wCurMapData`; `SavePokemonData` copies `wPokemonData`; `SaveChecksum` computes a 16-bit additive checksum over **only** `sGameData .. sGameDataEnd` and stores it in `sChecksum` (`engine/menus/save.asm:396-435,714-729,1038-1051`). Therefore:

- `sOptions` is **outside** the checksum.
- `sCheckValue1` / `sCheckValue2` are **outside** the checksum.
- numbered PC boxes, party mail, Hall of Fame, link battle data, and mystery gift data are **outside** the checksum.

### Backup save record

The backup save is split across three SRAM sections/banks:

- `Backup Save 1`: `wPlayerData3`, `wPokemonData`, `wPlayerData1`
- `Backup Save 2`: `wPlayerData2`
- `Backup Save 3`: `wOptions`, check values, `wCurMapData`, checksum

(`ram/sram.asm:66-71,137-174`; `engine/menus/save.asm:437-535,731-823`).

`SaveBackupChecksum` sums only the copied backup data blocks (`wPlayerData3`, `wPokemonData`, `wPlayerData1`, `wPlayerData2`, `wCurMapData`) and stores the result in `sBackupChecksum`; backup options and backup check bytes are again outside that checksum (`engine/menus/save.asm:495-535,772-823`).

### PC box storage

- The active box `sBox` uses `curbox` (`0x44e` bytes).
- Stored boxes `sBox1..sBox14` use `box` (`0x450` bytes each).
- Boxes `1-7` occupy one SRAM bank; boxes `8-14` occupy the next; the repo asserts that all 14 boxes fit exactly in those two banks (`ram/sram.asm:142-164`; `constants/pokemon_data_constants.asm:121-123`).
- `BoxAddresses` is a 5-byte per-box table: `bank, start_ptr, end_ptr` (`engine/menus/save.asm:1030-1036`).

Because `wBoxPartialData` is only 480 bytes, box save/load is chunked in three transfers of `0x1e0`, `0x1e0`, and `0x8e` bytes (`ram/wram.asm:172-176`; `engine/menus/save.asm:851-987`).

Important consequence: there is only **one** set of numbered PC boxes in SRAM. They are neither part of the primary `sGameData` checksum nor duplicated in the backup-save blocks. The backup path reloads the active box from the shared numbered-box storage after restoring backup core data (`engine/menus/save.asm:538-566`).

## 14) Item data (`ITEMATTR_STRUCT_LENGTH = 7` bytes)

Item attributes are a fixed-width ROM table. The macro defines the exact byte layout, and the engine reads `ITEMATTR_POCKET` as a low-nibble item-class/pocket enum and `ITEMATTR_HELP` as packed field/battle menu context nybbles (`constants/item_data_constants.asm:1-12`; `data/items/attributes.asm:1-10`; `engine/items/items.asm:512-535`).

| Off | Size | Field | Description |
|---|---:|---|---|
| `0x00` | 2 | `ITEMATTR_PRICE` | Price, little-endian. |
| `0x02` | 1 | `ITEMATTR_EFFECT` | Held-item effect enum (`HELD_*`). |
| `0x03` | 1 | `ITEMATTR_PARAM` | Effect parameter; meaning depends on effect/item family. |
| `0x04` | 1 | `ITEMATTR_PERMISSIONS` | Bitfield (`CANT_SELECT` bit 6, `CANT_TOSS` bit 7; `0` = `NO_LIMITS`). |
| `0x05` | 1 | `ITEMATTR_POCKET` | Low nibble is the item class/pocket enum (`ITEM`, `KEY_ITEM`, `BALL`, `TM_HM`). |
| `0x06` | 1 | `ITEMATTR_HELP` | Packed nybbles: high nibble = field menu behavior, low nibble = battle menu/context. |
