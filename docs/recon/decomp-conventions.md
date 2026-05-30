# pret/pokegold disassembly conventions

This note is repo-grounded and intended for future Claude instances with zero chat context. When a point is not explicit in-tree, it is marked `UNCLEAR:` instead of guessed.

## 1) Naming conventions

### Global labels

Observed label families:

- **Code entry points** usually read like PascalCase verbs/nouns: `Reset`, `Init`, `OverworldLoop`, `WarpToSpawnPoint`, `PrintText`, `VBlank_Normal`, `DoBattle` (`home/init.asm:1-28,155-170`; `engine/overworld/events.asm:3-31,1022-1028`; `home/text.asm:139-156`; `home/vblank.asm:9-53`; `engine/battle/core.asm:3-18`).
- **Subsystem-prefixed labels** use a domain prefix plus an underscore-separated action/name: `BattleCommand_CheckTurn`, `TextCommand_START`, `TreeMonSet_Forest`, `Credits_WilliamGiese`, `TrainerPicPointers` (`engine/battle/effect_commands.asm:121-124`; `home/text.asm:615-643`; `data/wild/treemons.asm:16-36`; `data/credits_strings.asm:3-53`; `data/trainers/pic_pointers.asm:3-6`).
- **Table labels** are descriptive nouns: `BaseData`, `MoveEffectsPointers`, `ScriptCommandTable`, `VBlankHandlers`, `MovementPointers` (`data/pokemon/base_stats.asm:21-24`; `data/moves/effects_pointers.asm:1-4`; `engine/overworld/scripting.asm:58-67`; `home/vblank.asm:40-51`; `engine/overworld/movement.asm:1-4`).
- **Constants** are overwhelmingly `ALL_CAPS_WITH_UNDERSCORES`, including IDs (`BULBASAUR`, `POUND`), flags (`ENGINE_BUG_CONTEST_TIMER`), and bit positions (`PLAYERSTEP_STOP_F`) (`constants/pokemon_constants.asm:16-19,168-169,269-272`; `constants/move_constants.asm:7-24`; `constants/engine_flags.asm:26-33,97-112`; `constants/ram_constants.asm:117-123`).

A few symbols intentionally start with `_` (`_Start`, `_BattleRandom`, `_CheckObjectEnteringVisibleRange`). `UNCLEAR:` the repo does not explicitly document the semantic meaning of the leading underscore beyond “this is another named global label” (`home/init.asm:16-28`; `engine/overworld/events.asm:231-236`; `engine/battle/core.asm:6644`).

### Local labels (`.`)

Dot-prefixed labels are used as **inner control-flow or inner-data labels** under a preceding global label:

- Short flow labels: `.loop`, `.done`, `.quit`, `.wild`, `.player_2` (`home/init.asm:51-68,173-179`; `engine/battle/core.asm:14-22,44-48,147-203`).
- Inner helper/data labels: `.Jumptable`, `.TextCommand`, `.Days`, `.Sun` (`engine/overworld/events.asm:16-21,161-165`; `home/text.asm:597-615,952-968`).

The same local names are reused under different globals, so they are clearly meant as scoped internals rather than unique repo-wide names (`engine/battle/core.asm:14,147,793`; `home/text.asm:91,819,873`).

### RAM/hardware prefixes

The storage-class prefix is consistent and comes from the memory section files:

- `v*` = **VRAM** (`vTiles0`, `vBGMap0`) (`ram/vram.asm:1-16`).
- `w*` = **WRAM** (`wMusicPlaying`, `wInputType`, `wDebugFlags`) (`ram/wram.asm:1-18,104-120`).
- `s*` = **SRAM** (`sPartyMail`, `sRTCStatusFlags`, `sBox`) (`ram/sram.asm:14-18,42-64,87-109`).
- `h*` = **HRAM** (`hROMBank`, `hJoypadDown`, `hVBlankCounter`) (`ram/hram.asm:22-39`).
- `r*` = **hardware registers**, pulled from `constants/hardware.inc` and used directly in code (`includes.asm:22`; `home/init.asm:32-45,125-140`).

Within constants, suffixes also carry meaning:

- `_F` = bit index (`PLAYERSTEP_STOP_F`, `BATTLEANIM_STOP_F`) (`constants/ram_constants.asm:117-123,151-156`).
- `_MASK` = packed bitmask (`TEXT_DELAY_MASK`, `JUMPTABLE_INDEX_MASK`) (`constants/ram_constants.asm:25,37`).
- `_command` / `_cmd` = DSL opcode constant (`scall_command`, `octave_cmd`) (`macros/scripts/events.asm:4-10`; `macros/scripts/audio.asm:47-55`).

## 2) Macro usage

### Generic data macros (`macros/data.asm`)

`macros/data.asm` provides packing/encoding helpers rather than game-specific schemas:

- mixed-width records: `dwb`, `dbw` (`macros/data.asm:34-42`)
- nibble/crumb packing: `dn`, `dc` (`macros/data.asm:44-56`)
- big-endian numbers: `bigdw`, `bigdt`, `bigdd` (`macros/data.asm:58-77`)
- far pointers: `dba`, `dab`, `dba_pic`, `dba_pics` (`macros/data.asm:79-120`)
- fixed-length strings: `dname` (`macros/data.asm:122-132`)
- BCD and generated sine tables: `bcd`, `sine_table` (`macros/data.asm:134-146`)

Typical use:

- `dba_pic` in picture pointer tables (`data/trainers/pic_pointers.asm:3-6`)
- `dba` in cry pointer tables (`audio/cry_pointers.asm:1-4`)
- `dn` in packed fields like egg groups (`data/pokemon/base_stats/bulbasaur.asm:20-21`)

`tmhm` is **not** in `macros/data.asm`; it is a data-specific helper defined locally in `data/pokemon/base_stats.asm` for the base-stats format (`data/pokemon/base_stats.asm:1-18`).

### Code macros (`macros/code.asm`)

These macro-ize common code-generation patterns:

- `lb` / `ln`: pack two bytes or nibbles into an immediate load (`macros/code.asm:3-9`), used in real code like `lb de, 2, 1` and `ln a, 1, 0` (`engine/sprite_anims/functions.asm:200-204`; `engine/printer/printer.asm:63-89`).
- `jumptable`: load index from memory and jump through a pointer table (`macros/code.asm:13-24`), used in menu/minigame state machines (`engine/games/card_flip.asm:75`; `engine/menus/options_menu.asm:92`).
- `maskbits`: synthesize rejection-sampling masks (`macros/code.asm:26-43`), used all over RNG-heavy code (`engine/overworld/scripting.asm:493,1936`; `audio/engine.asm:1290`).
- `calc_sine_wave`: inline sine-table multiplication (`macros/code.asm:45-90`), used in animation math (`engine/battle/battle_transition.asm:676`; `engine/sprite_anims/core.asm:532`).

### Graphics macros (`macros/gfx.asm`)

These macros normalize palette/tile/sprite encodings:

- `RGB` packs three 5-bit channels into a 15-bit palette entry (`macros/gfx.asm:3-20`), used in SGB palette packets (`gfx/sgb/pal_packets.asm:82-96`).
- EQUS helpers (`palettes`, `palette`, `color`, `tiles`, `tile`) provide readable byte offsets (`macros/gfx.asm:22-28`).
- `dbpixel` / `ldpixel` convert tile+pixel coordinates into pixel-space pairs (`macros/gfx.asm:35-65`).
- `dbsprite` emits four-byte OAM sprite entries (`macros/gfx.asm:67-70`).

### Farcall macros (`macros/farcall.asm`)

Cross-bank calls use explicit bank switching macros:

- `farcall` and `callfar` both compute `BANK(label)` and jump through `rst FarCall` (`macros/farcall.asm:1-17`).
- `homecall` bank-switches, calls, and restores the previous bank (`macros/farcall.asm:19-27`).
- Runtime support lives in `home/farcall.asm` (`home/farcall.asm:1-31`).

Examples: `callfar InitCGBPals`, `farcall StartClock`, `farcall RunMapSetupScript` (`home/init.asm:116-123`; `engine/overworld/events.asm:104,111,141,169`).

### Predef macros (`macros/predef.asm`)

`predef` is an indexed dispatch system:

- `predef` / `predef_jump` load `a = (LabelPredef - PredefPointers) / 3` and call/jump `Predef` (`macros/predef.asm:3-17`).
- `GetPredefPointer` resolves that ID into bank+address from `PredefPointers` (`engine/predef.asm:1-28`).
- The pointer table itself is generated with `add_predef` macros (`data/predef_pointers.asm:4-80`).
- `home/predef.asm` performs the bank switch and preserves `bc`, `de`, `hl`, and `f` (`home/predef.asm:1-52`).

So `predef` is a stable indexed ABI, not a plain call alias.

### Assertion/list/table macros (`macros/asserts.asm`)

These macros enforce data layout invariants:

- `table_width` + `assert_table_length` for fixed-width tables (`macros/asserts.asm:20-32`)
- `list_start` + `li` + `assert_list_length` for string lists (`macros/asserts.asm:34-58`)
- wild encounter size guards (`macros/asserts.asm:60-86`)

This pattern is used everywhere:

- pointer tables like `VBlankHandlers`, `MovementPointers`, `BattleCommandPointers` (`home/vblank.asm:40-51`; `engine/overworld/movement.asm:1-93`; `data/battle/effect_command_pointers.asm:5-183`)
- string lists like `StatNames` (`data/battle/stat_names.asm:1-12`)
- large included tables like `BaseData` (`data/pokemon/base_stats.asm:21-24,274`)

### Coordinate macros (`macros/coords.asm`)

These convert x/y positions into tilemap pointers or inline addresses:

- `hlcoord`, `bccoord`, `decoord`, `coord` target WRAM tilemaps (`macros/coords.asm:3-22`)
- `hlbgcoord`, `bcbgcoord`, `debgcoord`, `bgcoord` target VRAM BG maps (`macros/coords.asm:24-43`)
- `dwcoord`, `ldcoord_a`, `lda_coord`, `menu_coords` emit inline coordinate data (`macros/coords.asm:45-75`)

Example use: `hlcoord 1, 5` in battle picture placement (`engine/battle/core.asm:82-84`).

### RAM macros (`macros/ram.asm`)

These define actual struct-like storage layouts and generate member labels:

- Pokémon/mail/battle structs: `box_struct`, `party_struct`, `battle_struct`, `mailmsg` (`macros/ram.asm:7-42,76-97,182-189`)
- larger composite layouts: `curbox`, `box`, `hall_of_fame`, `link_battle_record`, `trademon` (`macros/ram.asm:99-124,216-243`)
- non-Pokémon structs: `map_connection_struct`, `channel_struct`, `move_struct`, etc. (`macros/ram.asm:126-180,245-253`)

These macros generate labels like `wChannel1MusicID` or `sBoxMon1Nickname` automatically (`ram/wram.asm:6-10`; `ram/sram.asm:107-109`).

### Constant macros (`macros/const.asm`)

This is the repo’s enum/bitfield DSL:

- `const_def [start[, inc]]` initializes the running value (`macros/const.asm:3-14`)
- `const NAME` assigns and auto-increments (`macros/const.asm:16-19`)
- `shift_const NAME` makes `1 << const_value` plus a `_F` constant (`macros/const.asm:21-24`)
- `const_skip` reserves gaps (`macros/const.asm:26-32`)
- `const_next` jumps to an exact next value (`macros/const.asm:34-40`)
- `rb_skip` advances `rs`-style offset layouts (`macros/const.asm:42-48`)

The codebase uses all of these patterns, including non-1 starts and negative increments (`constants/ram_constants.asm:80-99`; `macros/scripts/movement.asm:1-76`).

### VC macros (`macros/vc.asm`)

These gate **Virtual Console-only hook/patch metadata**:

- `vc_hook` creates `.VC_*` labels only in `_GOLD_VC`/`_SILVER_VC` builds (`macros/vc.asm:3-7`)
- `vc_patch` / `vc_patch_end` bracket named patch regions (`macros/vc.asm:9-23`)
- `vc_assert` enforces patch assumptions in VC builds (`macros/vc.asm:25-29`)

Observed uses are for wireless/serial timing, flashing reduction, and printing restrictions (`home/serial.asm:313-331`; `engine/battle/battle_transition.asm:23-51`; `engine/pokedex/pokedex.asm:358`; `engine/menus/menu.asm:218-248`).

### Legacy macros (`macros/legacy.asm`)

This file is explicitly compatibility glue for older pret disassemblies (`macros/legacy.asm:1-4`). It provides:

- alias names like `callba`/`callab` -> `farcall`/`callfar` (`macros/legacy.asm:5-8`)
- legacy graphics/data aliases like `dsprite`, `dt`, `dd` (`macros/legacy.asm:9-37`)
- a large set of old script/audio names remapped onto current macros (`macros/legacy.asm:39-257`)

The broader codebase also marks compatibility shims inline with `LEGACY:` comments (`macros/scripts/events.asm:190-197`; `macros/scripts/audio.asm:137-141,167-171`).

## 3) File organization patterns

### Translation units and preinclude layer

The build does **not** assemble one giant source file. It assembles a small set of large roots such as `audio.o`, `home.o`, `main.o`, `ram.o`, `engine/overworld/events.o`, and several data/gfx roots (`Makefile:10-23`). Every object is assembled with `-P includes.asm`, so `includes.asm` is preincluded before each unit (`Makefile:126,155-159`).

`includes.asm` establishes the shared language layer in this order:

1. charmap (`includes.asm:1`)
2. core macros (`includes.asm:3-11`)
3. script DSL macros (`includes.asm:13-20`)
4. hardware constants (`includes.asm:22`)
5. game constants (`includes.asm:24-66`)
6. VC constants, conditionally (`includes.asm:68-73`)
7. legacy aliases last (`includes.asm:75`)

### `home.asm`, `main.asm`, `ram.asm`

- `home.asm` owns **ROM0/home bank** code and includes the boot/header/home routines (`home.asm:1-60`).
- `main.asm` owns most **ROMX banks** and groups includes by bank/feature (`main.asm:1-385`).
- `ram.asm` owns **VRAM/WRAM/SRAM/HRAM layout** by including the memory section files (`ram.asm:1-8`).

### How `SECTION` names map to bank numbers

`main.asm` names sections, but `layout.link` is the ground truth for final bank placement:

- `"bank1"` -> ROMX `$01` (`main.asm:1`; `layout.link:32-33`)
- `"Battle Core"` -> ROMX `$0f` (`main.asm:169-173`; `layout.link:63-68`)
- `"Standard Scripts"` -> ROMX `$40` (`main.asm:324-328`; `layout.link:166-168`)
- `"Phone Scripts"` -> ROMX `$41` (`main.asm:330-343`; `layout.link:168-170`)
- `"Names"` -> ROMX `$6c` (`main.asm:346-352`; `layout.link:248-250`)
- `"Credits Strings"` -> ROMX `$70` (`main.asm:380-382`; `layout.link:254-259`)
- `"Stadium 2 Checksums"` is explicitly pinned to ROMX `$7f`, `org $7df8` (`main.asm:385-391`; `layout.link:260-262`)

Important convention: section **names are human-facing organization**, not necessarily whole-bank ownership. `layout.link` shows many banks containing several differently named sections from different roots, e.g. ROMX `$07` contains `"Roofs"`, `"Tileset Data 2"`, and `"Extra Songs 1"`; ROMX `$70` contains `"bank70"`, `"Tileset Data 6"`, `"bank70_2"`, `"Pokégear GFX"`, and `"Credits Strings"` (`layout.link:45-47,254-259`).

### Why some sections are descriptive and others generic

Observed pattern:

- **Descriptive names** are used when the section is strongly associated with one subsystem or content bucket (`"Battle Core"`, `"Enemy Trainers"`, `"Move Animations"`, `"Phone Scripts"`, `"Names"`) (`main.asm:156-173,270-277,324-352`).
- **Generic `bankNN` names** are used for banks that mix several unrelated includes or where no single friendly title was chosen (`main.asm:1-99,115-154,175-260`).

Because `layout.link` can place additional sections into the same bank, do not assume the `SECTION` name is the complete semantic identity of the final bank (`layout.link:42-47,107-109,254-259`).

### `engine/` and `data/` structure

`main.asm`’s include paths show the repo is organized by **subsystem** under `engine/` and by **content type** under `data/`:

- `engine/`: `overworld`, `events`, `battle`, `battle_anims`, `menus`, `items`, `pokemon`, `link`, `rtc`, `gfx`, `games`, `phone`, `pokedex`, `pokegear`, `sprite_anims`, `tilesets`, `printer` (`main.asm:3-17,31-49,53-74,78-98,117-153,177-253,265-276,281-377`).
- `data/`: `items`, `maps`, `moves`, `pokemon`, `battle`, `phone`, `text`, `collision`, `tilesets` (`main.asm:12,83,87,91,117,134,172,178,211-212,266,299,327,338-361`).

`layout.link` reinforces that large data is bank-sharded by topic: `"Map Scripts 1-32"`, `"Text 1-3"`, and `"Pokedex Entries 001-064"` through `"193-251"` (`layout.link:170-247`).

## 4) Comment conventions

### Style

Comments are all semicolon-prefixed and often come in one of these structured forms:

- **file/subsystem overview** (`home/vblank.asm:1-7`; `engine/battle/core.asm:1`) 
- **table schema markers** like `entries correspond to ...` (`home/vblank.asm:41-51`; `engine/overworld/movement.asm:1-3`; `data/trainers/pic_pointers.asm:3-5`)
- **field-format comments** (`data/pokemon/base_stats/bulbasaur.asm:3-24`; `data/wild/treemons.asm:12-15`)
- **memory-group headers** keyed by backing variable (`constants/ram_constants.asm:1-18,24-35,169-203`)

### Mostly WHAT, sometimes WHY

Most comments explain **what the data/code is** or **what a field means** (`data/pokemon/base_stats/bulbasaur.asm:3-24`; `constants/pokemon_constants.asm:1-16`). But the repo absolutely keeps **WHY/problem-context** comments when important:

- VBlank-as-main-loop rationale (`home/vblank.asm:3-7`)
- connected-map warning (`engine/overworld/events.asm:144-147`)
- Stadium checksum explanation (`main.asm:387-391`)
- battle bug annotation (`engine/battle/core.asm:137-143`)

### Structured annotations worth preserving

- `; BUG:` for known behavioral defects (`engine/battle/core.asm:137-139`)
- `; unreferenced` on dead/unused labels (`home/init.asm:166`; `engine/overworld/events.asm:38,43,48,53,58`; `engine/sprite_anims/core.asm:495`)
- `; unused` or `const_skip ; unused` for reserved holes (`constants/event_flags.asm:29,68-69`; `data/battle/effect_command_pointers.asm:67,100`; `engine/battle_anims/anim_commands.asm:331-337`)
- `; dummy` for placeholder handlers (`engine/battle_anims/anim_commands.asm:334-337`)
- `; LEGACY:` for compatibility shims (`macros/scripts/events.asm:191`; `macros/scripts/audio.asm:140,170`)
- debug-only blocks are both commented and conditionalized (`ram/wram.asm:1022-1045`; `engine/menus/main_menu.asm:13-15,73-75,83-85`)

## 5) Label hierarchies and `::`

### Global-to-local hierarchy

A non-dot label introduces a new top-level anchor; following dot-labels act as its nested inner labels. The codebase relies on this heavily for inner loops, branch targets, jump tables, and embedded data (`home/init.asm:28-68,162-179`; `engine/battle/core.asm:3-48,146-203`; `home/text.asm:177-239,597-643`).

### `::` vs `:`

Observed convention:

- `::` marks labels treated as **public/stable top-level names**: e.g. `Reset::`, `Init::`, `EnableEvents::`, `PrintText::`, `VBlank_Normal::`, `CreditsStrings::` (`home/init.asm:1-28`; `engine/overworld/events.asm:28-31`; `home/text.asm:139-148`; `home/vblank.asm:9-53`; `data/credits_strings.asm:1-3`).
- `:` marks **private helpers/internal tables** within the current include file: e.g. `DisableEvents:`, `StartMap:`, `MapEvents:`, `DoBattle:`, `VBlankHandlers:` (`engine/overworld/events.asm:23-27,98-155`; `engine/battle/core.asm:3`; `home/vblank.asm:40`).

The build model matters here: the Makefile compiles a few large roots which `INCLUDE` many source files, so “public” vs “private” is often a convention for included files inside one translation unit, not just separate `.asm` objects (`Makefile:10-23,155-159`).

`UNCLEAR:` the repo does not explicitly document whether every `::` is exported purely for linker visibility, or whether pret also uses it as a style marker for “important/public” symbols inside large included translation units. In practice, treating `::` labels as public API/data and `:` labels as file-local is the safest approximation.

## 6) Constant definition patterns

### Auto-incremented enums

The repo’s standard enum pattern is:

```asm
const_def [start[, inc]]
const NAME
const NAME2
...
DEF NUM_THINGS EQU const_value
```

This is used for species, moves, flags, menu items, etc. (`constants/pokemon_constants.asm:16-19,168-169,269-272`; `constants/move_constants.asm:7-24`; `engine/menus/main_menu.asm:1-15`).

Specialized variants are common:

- nonzero starts: `const_def 1` for 1-based enums (`constants/pokemon_constants.asm:16`; `constants/pokemon_data_constants.asm:55-70,139-155`)
- negative starts/increments: directions and facing masks (`constants/ram_constants.asm:80-99`)
- custom step size: movement opcodes use `const_def 0, 4` so low bits can hold direction (`macros/scripts/movement.asm:1-44`)
- reserved holes: `const_skip` / `const_next` (`constants/event_flags.asm:29,197-198,1327-1330`; `constants/pokemon_constants.asm:269-271`)

### Enums and IDs

- Pokémon IDs are flat species enums, with a `JOHTO_POKEMON` cutover marker and special post-table values like `EGG` (`constants/pokemon_constants.asm:16-19,168-169,269-272`).
- Moves and items follow the same pattern (`constants/move_constants.asm:7-24`; `constants/item_constants.asm:199-220`).
- TM/HM constants are generated by macros that simultaneously define item IDs, TM numbers, and move aliases (`constants/item_constants.asm:201-216,219-295`).

### Flags

There are two main flag patterns:

- **bit positions inside a specific byte**: comments name the backing RAM field, then constants are declared for that field (`constants/ram_constants.asm:24-35,169-203`).
- **flat global flag enums**: `EVENT_*` and `ENGINE_*` are linear flag indices grouped by domain (`constants/event_flags.asm:1-35,197-199,1327-1330`; `constants/engine_flags.asm:1-33,92-112`).

`engine_flags.asm` groups flags by the WRAM field that stores them (`; wPokegearFlags`, `; wStatusFlags2`, etc.), while still generating one flat index space (`constants/engine_flags.asm:1-33,92-112`).

### Struct-like layouts and offsets

The repo uses RGBDS `rsreset` / `rb` / `rw` plus `rb_skip` to define **symbolic member offsets**:

- base-stat layout (`constants/pokemon_data_constants.asm:1-32`)
- Pokémon struct layout (`constants/pokemon_data_constants.asm:75-107`)
- lengths like `BASE_DATA_SIZE`, `PARTYMON_STRUCT_LENGTH`, `BOX_LENGTH` derived from `_RS` (`constants/pokemon_data_constants.asm:31-32,95-107,121-123`)

This offset layer mirrors the actual storage macros in `macros/ram.asm` (`macros/ram.asm:7-42,76-97`).

## 7) Script DSLs

### Event scripts (`macros/scripts/events.asm` -> `engine/overworld/scripting.asm`)

This is a bytecode DSL with commands `0x00` through `0xa1` (`macros/scripts/events.asm:4-1015`; `engine/overworld/scripting.asm:64-229`). Encoding conventions:

- opcode byte via `db <name>_command`
- little-endian `dw` for in-bank pointers (`scall`, `sjump`, `memcall`) (`macros/scripts/events.asm:5-38`)
- `dba` for far bank+address pointers (`farscall`, `farsjump`, `callasm`) (`macros/scripts/events.asm:10-14,28-32,92-96`)
- inline bytes for values/IDs (`setval`, `loadvar`, `giveitem`) (`macros/scripts/events.asm:134-219`)
- structured helpers like `map_id`, `bigdt`, trainer/name helpers, and object/movement operands throughout (`macros/scripts/events.asm:110-173,228-246,429-457,663-1015`)

Runtime model:

- `ScriptEvents` loops on `wScriptMode` (`engine/overworld/scripting.asm:10-25`)
- `RunScriptCommand` reads one opcode with `GetScriptByte` and dispatches through `ScriptCommandTable` (`engine/overworld/scripting.asm:58-67`)
- categories visible in the table: control flow (`00-18`), memory/vars (`19-1e`), inventory/money/phone (`1f-2b`), Pokémon/flags (`2c-38`), text/menu/battle (`45-66`), object/movement/map/audio (`67-8e`), endings/system screens (`8f-a1`) (`engine/overworld/scripting.asm:67-229`)

### Text scripts (`macros/scripts/text.asm` -> `home/text.asm`)

This DSL is actually **two layers**:

1. **TX_* metacommands** in the text stream, terminated by `TX_END = $50` (`macros/scripts/text.asm:33-171`; `home/text.asm:590-641`).
2. **Inline text control tokens** like `<LINE>`, `<PARA>`, `<CONT>`, `<DONE>`, `<PROMPT>`, plus name placeholders like `<PLAYER>` and `<RIVAL>`, interpreted by `PlaceString`’s `dict` table (`home/text.asm:156-225`).

Examples of TX_* encodings:

- `text_ram`: opcode + `dw address` (`macros/scripts/text.asm:41-45`)
- `text_decimal`: opcode + `dw address` + packed digit info via `dn` (`macros/scripts/text.asm:87-92`)
- `text_far`: opcode + `dw address` + `db BANK(label)` (`macros/scripts/text.asm:156-161`)

Runtime model:

- `DoTextUntilTerminator` reads until `TX_END` (`home/text.asm:590-595`)
- `TextCommands` dispatches TX_* opcodes (`home/text.asm:597-641`)
- `TextCommand_START` writes inline text until `@`; `TextCommand_FAR` bank-switches and recursively interprets text in another bank (`home/text.asm:643-691`)

### Movement scripts (`macros/scripts/movement.asm` -> `engine/overworld/movement.asm`)

Movement is a compact bytecode where the **low 2 bits encode direction** for many commands because the enum starts at `0` with increment `4` (`macros/scripts/movement.asm:1-44`). Examples:

- `step DOWN/UP/LEFT/RIGHT` = base opcode block `$0c` + direction (`macros/scripts/movement.asm:21-24`)
- `step_sleep n` either folds `1..8` into the opcode range or emits an extra length byte for larger values (`macros/scripts/movement.asm:111-118`)
- `step_wait_end`, `step_dig`, `step_shake`, `rock_smash`, `return_dig` emit extra parameter bytes (`macros/scripts/movement.asm:127-131,163-167,194-208,211-215`)

Runtime model:

- `Script_applymovement` reads an object ID and movement pointer, then calls `GetMovementData` and switches script mode to `SCRIPT_WAIT_MOVEMENT` (`engine/overworld/scripting.asm:747-773`)
- `GetMovementData` / `LoadMovementDataPointer` record bank+pointer and tag the object as scripted (`home/map.asm:1467-1480`; `home/map_objects.asm:394-418`)
- movement bytes are fetched through `OBJECT_MOVEMENT_INDEX` and dispatched via `MovementPointers` (`home/map_objects.asm:552-575`; `engine/overworld/map_objects.asm:1872-1908`; `engine/overworld/movement.asm:1-93`)

### Audio scripts (`macros/scripts/audio.asm` -> `audio/engine.asm`)

Audio has a channel-header DSL plus note/command stream:

- `channel_count` + `channel` encode a song header with channel count and per-channel pointers (`macros/scripts/audio.asm:1-13`)
- ordinary notes are nibble-packed pitch/length bytes via `note` / `rest` (`macros/scripts/audio.asm:15-25`)
- command bytes start at `FIRST_MUSIC_CMD = $d0` and run through `$ff` (`macros/scripts/audio.asm:47-320`)

Notable commands:

- musical state: `octave`, `note_type`, `transpose`, `tempo`, `duty_cycle`, `volume_envelope`, `pitch_sweep`, `vibrato`, `stereo_panning` (`macros/scripts/audio.asm:51-230`)
- SFX/noise control: `toggle_sfx`, `toggle_noise`, `sfx_toggle_noise`, `sfx_priority_on/off` (`macros/scripts/audio.asm:121-156,210-237`)
- control flow: `set_condition`, `sound_jump_if`, `sound_jump`, `sound_loop`, `sound_call`, `sound_ret` (`macros/scripts/audio.asm:285-320`)

Runtime model:

- `ParseMusic` reads bytes until it gets either a note (`< $d0`) or end/commands (`audio/engine.asm:1144-1212`)
- `ParseMusicCommand` dispatches `FIRST_MUSIC_CMD..$ff` through `MusicCommands` (`audio/engine.asm:1356-1424`)
- `MusicCommands` includes the control-flow commands at the end (`audio/engine.asm:1418-1424`)

### Battle command scripts (`macros/scripts/battle_commands.asm` -> `data/moves/effects*.asm` / `engine/battle/effect_commands.asm`)

Move effects are script data, not hardcoded switch statements:

- `macros/scripts/battle_commands.asm` defines command opcodes `0x01..0xaf`, plus terminators `endmove = $ff` and `endturn = $fe` (`macros/scripts/battle_commands.asm:1-187`).
- `MoveEffectsPointers` maps each `EFFECT_*` to a script label (`data/moves/effects_pointers.asm:1-80`).
- Actual scripts are sequences like `checkobedience`, `usedmovetext`, `damagecalc`, `moveanim`, `endmove` (`data/moves/effects.asm:3-24,25-53`).

Runtime model:

- `DoMove` resolves the move’s `EFFECT_*` to a script pointer (`engine/battle/effect_commands.asm:51-62`)
- the script is copied byte-for-byte into `wBattleScriptBuffer` until `endmove_command` (`engine/battle/effect_commands.asm:65-79`)
- commands `01-af` dispatch through `BattleCommandPointers` (`engine/battle/effect_commands.asm:97-119`; `data/battle/effect_command_pointers.asm:5-183`)

### Battle animation scripts (`macros/scripts/battle_anims.asm` -> `engine/battle_anims/anim_commands.asm`)

Encoding split:

- bytes **below `$d0`** are literal frame waits (`anim_wait`) (`macros/scripts/battle_anims.asm:2-8`; `engine/battle_anims/anim_commands.asm:278-283`)
- bytes `$d0..$ff` are commands (`anim_obj`, `anim_1gfx`, `anim_sound`, `anim_bgeffect`, `anim_loop`, `anim_call`, `anim_ret`, etc.) (`macros/scripts/battle_anims.asm:10-301`)

Runtime model:

- `RunBattleAnimCommand` decrements `wBattleAnimDelay` and then interprets commands until it hits another wait or a return/stop (`engine/battle_anims/anim_commands.asm:243-288`)
- `BattleAnimCommands` dispatches `anim_obj_command` upward (`engine/battle_anims/anim_commands.asm:290-356`)
- conditionals/flow control live near the end: `anim_if_param_and`, `anim_if_param_equal`, `anim_if_var_equal`, `anim_jump`, `anim_loop`, `anim_call`, `anim_ret` (`macros/scripts/battle_anims.asm:194-301`; `engine/battle_anims/anim_commands.asm:338-356`)

### OAM animation scripts (`macros/scripts/oam_anims.asm` -> `engine/sprite_anims/core.asm`)

OAM frame data is compact:

- `oamframe duration, flags...` emits **two bytes**: duration and packed flags/xflip/yflip bits (`macros/scripts/oam_anims.asm:3-15`)
- special commands are descending sentinels: `oamend = $ff`, `oamrestart = $fe`, `oamwait = $fd`, `oamdelete = $fc` (`macros/scripts/oam_anims.asm:17-40`)

Runtime model:

- `GetSpriteAnimFrame` handles restart/end/wait/delete semantics and updates per-sprite duration/frame state (`engine/sprite_anims/core.asm:400-485`)
- `UpdateAnimFrame` expands the chosen frame into Shadow OAM entries (`engine/sprite_anims/core.asm:216-302`)
- `oamend` repeats the last frame/halts motion, while `oamdelete` actually deinitializes the sprite (`engine/sprite_anims/core.asm:219-223,292-295,419-465`)

## 8) Conditional compilation

The build defines these symbols per target:

- Gold retail: `_GOLD`
- Silver retail: `_SILVER`
- Gold/Silver debug: plus `_DEBUG`
- Gold VC: `_GOLD_VC`
- Silver VC: `_SILVER_VC` (`Makefile:132-137`)

### Gold vs Silver

Observed Gold/Silver divergences are mostly **data/assets/version text**, with occasional behavior differences:

- version ID constant (`constants/misc_constants.asm:18-24`)
- credits version string (`data/credits_strings.asm:54-60`)
- version-specific Pokémon picture dimensions in base stats (`data/pokemon/base_stats/bulbasaur.asm:14-18`)
- version-specific wild tree encounters (`data/wild/treemons.asm:34-71`)
- version-specific title animation behavior (`engine/sprite_anims/functions.asm:727-814,824-832`)

`UNCLEAR:` this is not an exhaustive list of all Gold/Silver deltas; those are simply representative grounded examples.

### `_DEBUG`

`_DEBUG` enables debug-only code/data rather than lightly toggling behavior:

- extra main-menu item and menu height (`engine/menus/main_menu.asm:13-15,52-56,73-75,83-85,97-114`)
- debug room bank include (`main.asm:364-369`)
- extra WRAM unions for debug-room state (`ram/wram.asm:1022-1045`)

### `_GOLD_VC` / `_SILVER_VC`

VC builds include extra constants and activate VC patch hooks (`includes.asm:68-73`; `macros/vc.asm:1-29`). Observed VC-only changes include serial/link timing, print restrictions, and flash-reduction hook points (`home/serial.asm:313-331`; `engine/link/link.asm:2188-2195`; `engine/pokedex/pokedex.asm:358`; `engine/battle/battle_transition.asm:23-51`).

## 9) Include patterns and dependency order

The effective dependency order is:

1. `rgbasm -P includes.asm` preincludes the shared macro/constants layer into **every** translation unit (`Makefile:126,155-159`).
2. The root unit (`home.asm`, `main.asm`, `ram.asm`, etc.) then `INCLUDE`s its actual implementation/data files (`home.asm:1-60`; `main.asm:1-385`; `ram.asm:1-8`).
3. `tools/scan_includes` recursively tracks those `INCLUDE` and `INCBIN` dependencies for Makefile rules (`Makefile:155-159`).

So for future work:

- **Do not duplicate macros/constants inside individual `.asm` files.** They arrive via `includes.asm`.
- **Treat root `.asm` files as umbrellas** and included files as fragments inside that root object.
- **Use `layout.link` as final placement truth** when bank identity matters (`layout.link:1-270`).

## 10) Data table patterns

The pret style here is very regular:

- fixed-width tables start with `table_width N` and end with `assert_table_length ...` (`macros/asserts.asm:20-32`)
- tables are usually documented with `; entries correspond to ...` comments (`home/vblank.asm:40-51`; `engine/overworld/movement.asm:1-3`; `data/moves/effects_pointers.asm:1-3`)
- pointer tables use `dw` for same-bank pointers and `dba`/`dba_pic` for far pointers (`data/moves/effects_pointers.asm:1-80`; `data/trainers/pic_pointers.asm:3-6`; `audio/cry_pointers.asm:1-4`)
- indexed dispatch is usually manual: load index into `de`, add twice for word tables, then jump/call through the fetched pointer (`home/vblank.asm:15-29`; `engine/overworld/scripting.asm:58-62`; `engine/battle/effect_commands.asm:101-119`; `engine/battle_anims/anim_commands.asm:290-303`; `audio/engine.asm:1356-1371`)
- string lists use `list_start` / `li` / `assert_list_length` instead of hand-counting (`data/battle/stat_names.asm:1-12`)
- large master tables are often assembled by `INCLUDE`ing one file per entry and then asserted for total length/count, e.g. Pokémon base stats (`data/pokemon/base_stats.asm:21-24,274`; `data/pokemon/base_stats/bulbasaur.asm:1-25`)

### Practical implication for decomp work

If we mirror pret structure in C#, the safest mapping is:

- preserve **subsystem boundaries** (`engine/battle`, `engine/overworld`, `engine/pokemon`, etc.)
- preserve **data schemas and index spaces** as first-class types/enums
- preserve the distinction between **same-bank pointers**, **far pointers**, and **indexed dispatch tables** even if C# eventually models them with delegates/arrays instead of raw addresses
- treat `layout.link`, `includes.asm`, macro files, and the root umbrella `.asm` files as the canonical organization layer, not just the leaf `.asm` fragments
