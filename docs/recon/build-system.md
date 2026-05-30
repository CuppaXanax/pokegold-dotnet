# Build system ground truth

This note is repo-grounded and intended for future Claude instances with zero chat context. When a point is not explicit in-tree, it is marked `UNCLEAR:` instead of guessed.

## 1) What `make` does

### Top-level targets

The root `Makefile` defines these phony targets: `all`, `gold`, `silver`, `gold_debug`, `silver_debug`, `gold_vc`, `silver_vc`, `clean`, `tidy`, `compare`, and `tools` (`Makefile:71-82`).

- `all` builds four `.gbc` images: `pokegold.gbc`, `pokesilver.gbc`, `pokegold_debug.gbc`, `pokesilver_debug.gbc` (`Makefile:1-5,84-90`).
- `gold` / `silver` build the retail-matching Gold/Silver ROMs (`Makefile:84-90`).
- `gold_debug` / `silver_debug` build debug-enabled ROMs (`Makefile:84-90,134-135`).
- `gold_vc` / `silver_vc` do **not** publish `_vc.gbc` as the final artifact; they publish `pokegold.patch` / `pokesilver.patch`, generated from a temporary `_vc.gbc` plus the retail `.gbc` and a VC patch template (`Makefile:6-8,89-90,139-141`).

### Object model and version split

Shared object roots are listed in `rom_obj`; version-exclusive sources are only `data/pokemon/dex_entries` and `gfx/pics` (`Makefile:10-28`). The Makefile expands those into per-build object lists:

- retail Gold: `$(rom_obj:.o=_gold.o)` + `data/pokemon/dex_entries_gold.o` + `gfx/pics_gold.o`
- retail Silver: same pattern with `_silver`
- debug Gold/Silver: same pattern with `_gold_debug` / `_silver_debug`
- VC Gold/Silver: same pattern with `_gold_vc` / `_silver_vc` (`Makefile:30-42`).

This means most code/data is shared across versions, while Pokédex entries and picture tables are assembled from explicitly versioned source files (`Makefile:25-42,175-185`).

### Build tools are compiled first

Unless the goal is `clean`, `tidy`, or `tools`, the Makefile runs `$(MAKE) -C tools` during makefile evaluation so helper binaries exist before dependency scanning happens (`Makefile:146-150`). This is important because `tools/scan_includes` is used while dependency rules are being generated (`Makefile:147-157`).

### Dependency scanning and preinclude behavior

Every object rule is synthesized by the `DEP` macro. Each object depends on:

- its source `.asm`
- the recursive output of `tools/scan_includes <source>`
- `includes.asm`
- the recursive includes of `includes.asm`
- order-only prerequisite `rgbdscheck.o` (`Makefile:155-159`).

`RGBASMFLAGS` always adds `-Q8 -P includes.asm`, so every assembly unit is assembled with `includes.asm` preincluded (`Makefile:126`). `includes.asm` pulls in the global macro and constant layer, and conditionally includes VC constant files when `_GOLD_VC` or `_SILVER_VC` is defined (`includes.asm:1-75`).

### Preprocessor defines per build target

Per-object flags are appended like this (`Makefile:132-137`):

| Build flavor | Defines |
|---|---|
| Gold retail | `_GOLD` |
| Silver retail | `_SILVER` |
| Gold debug | `_GOLD`, `_DEBUG` |
| Silver debug | `_SILVER`, `_DEBUG` |
| Gold VC | `_GOLD`, `_GOLD_VC` |
| Silver VC | `_SILVER`, `_SILVER_VC` |

Observed effects:

- `_DEBUG` enables the title-screen/main-menu “DEBUG ROOM” path and includes `engine/debug/debug_room.asm` in bank 70 (`engine/menus/main_menu.asm:13-15,52-56,73-75,83-85,97-114`; `engine/menus/intro_menu.asm:16-20`; `main.asm:364-369`; `ram/wram.asm:1022-1035`).
- `_GOLD_VC` / `_SILVER_VC` activate VC-only constants and the `vc_hook` / `vc_patch` / `vc_patch_end` macros (`includes.asm:68-73`; `macros/vc.asm:1-29`). Example: serial timing constants are changed only in VC builds (`home/serial.asm:313-331`).

### Exact RGBDS flags used

Tool variables and default warning flags are:

- `RGBASM  ?= $(RGBDS)rgbasm`
- `RGBFIX  ?= $(RGBDS)rgbfix`
- `RGBGFX  ?= $(RGBDS)rgbgfx`
- `RGBLINK ?= $(RGBDS)rgblink`
- `RGBASMFLAGS  ?= -Weverything -Wtruncation=1`
- `RGBLINKFLAGS ?= -Weverything -Wtruncation=1`
- `RGBFIXFLAGS  ?= -Weverything`
- `RGBGFXFLAGS  ?= -Weverything` (`Makefile:53-62`)

Additional flags:

- `RGBASMFLAGS += -Q8 -P includes.asm` always (`Makefile:126`).
- If top-level `make` is run with `DEBUG=1`, `RGBASMFLAGS += -E` (`Makefile:127-130`). CI does this for `make compare` (`.github/workflows/main.yml:36-40,92-96`).
- `RGBFIXFLAGS += -cjsv -k 01 -l 0x33 -m MBC3+TIMER+RAM+BATTERY -r 3 -p 0` (`Makefile:190`).
- Per-ROM header flags:
  - Gold / Gold debug / Gold VC: `-t POKEMON_GLD -i AAUE`
  - Silver / Silver debug / Silver VC: `-t POKEMON_SLV -i AAXE` (`Makefile:191-196`).

### Full build pipeline

For each `.gbc` target, the pipeline is:

1. **rgbasm** assembles many `.asm` translation units into `.o` files via the generated dependency rules (`Makefile:156-159`).
2. **rgblink** links them with `layout.link`, emitting the ROM plus `.sym` and `.map` files:
   `$(RGBLINK) $(RGBLINKFLAGS) -l layout.link -n $*.sym -m $*.map -o $@ ...` (`Makefile:198-199`).
3. **rgbfix** writes/fixes header metadata and checksums:
   `$(RGBFIX) $(RGBFIXFLAGS) $@` (`Makefile:199-200`).
4. **tools/stadium** postprocesses the finished ROM:
   `tools/stadium $@` (`Makefile:201`).

For VC targets there is one more step:

5. Build `%_vc.gbc`, then generate `%.patch` from `%_vc.gbc`, `%.gbc`, and `vc/%.patch.template` using `tools/make_patch --ignore 0x1ffdf8:0x208 ...` (`Makefile:139-141`).

That ignore range exactly matches the Stadium footer area size written by `tools/stadium`: `N64PS3_TOTAL_SIZE` is `0x208`, stored at offset `ROM_SIZE - 0x208 = 0x1ffdf8` in a 2 MiB ROM (`tools/stadium.c:19-25`; `Makefile:140-141`).

## 2) RGBDS toolchain version

- The repo pins RGBDS to **`1.0.1`** in `.rgbds-version` (`.rgbds-version:1`).
- `INSTALL.md` repeatedly tells users to install/build **rgbds 1.0.1** (`INSTALL.md:45-47,70,87,97,107,123,134,166-176`).
- CI checks out `gbdev/rgbds` at tag `v1.0.1` on both Ubuntu and macOS (`.github/workflows/main.yml:8-30,68-86`).

### What `rgbdscheck.asm` actually verifies

`rgbdscheck.asm` is a **coarse compatibility gate**, not an exact `1.0.1` pin:

- it fails if `__RGBDS_MAJOR__` is undefined
- it fails if `__RGBDS_MAJOR__ < 1`
- otherwise it succeeds (`rgbdscheck.asm:1-6`)

So in practice:

- the repo policy is “use 1.0.1” (`.rgbds-version`, `INSTALL.md`, CI)
- the assembler-side hard check only guarantees “RGBDS 1.x or newer enough to define `__RGBDS_MAJOR__`” (`rgbdscheck.asm:1-6`)

Because `rgbdscheck.o` is an order-only prerequisite of every assembled object, the check runs before the rest of the build proceeds (`Makefile:157-159`).

## 3) ROM hash targets

`roms.sha1` is the ground-truth hash file used by `make compare` (`Makefile:119-120`; `roms.sha1:1-6`).

| Artifact | SHA1 |
|---|---|
| `pokegold.gbc` | `d8b8a3600a465308c9953dfa04f0081c05bdcb94` |
| `pokesilver.gbc` | `49b163f7e57702bc939d642a18f591de55d92dae` |
| `pokegold_debug.gbc` | `53783c57378122805c5b4859d19e1a224f02a1ed` |
| `pokesilver_debug.gbc` | `4c2fafebdbc7551f4cd3f348bdd17e420b93b6e7` |
| `pokegold.patch` | `b8253b915ade89c784c71adfdb11cf60bc1f7b59` |
| `pokesilver.patch` | `a38c0dec807e8a9e3626a0ec0fdf96bfb795ef3a` |

### Canonical target for a C# reimplementation

If one canonical target must be chosen, `pokegold.gbc` is the safest single target:

- the repository itself is named `pokegold`
- `README.md` lists Gold first (`README.md:5-12`)
- `INSTALL.md` uses `make gold` as the first explicit build example (`INSTALL.md:148-152`)
- the VC outputs are **patches layered on top of** retail ROMs, not the primary ROM build (`Makefile:89-90,139-141`)

`UNCLEAR:` whether your broader reimplementation effort wants Gold-only parity or dual Gold/Silver parity. The repo supports both retail versions equally at build time (`Makefile:84-90`).

## 4) Custom build tools in `tools/`

The tools directory builds these executables: `gbcpal`, `gfx`, `lzcomp`, `make_patch`, `png_dimensions`, `scan_includes`, and `stadium` (`tools/Makefile:6-26`).

### Tool summary

| Tool | What it does | Runtime relevance |
|---|---|---|
| `scan_includes` | Parses `INCLUDE`/`INCBIN` directives and recursively prints dependencies for Makefile rule generation | Build-time only |
| `lzcomp` | Compresses/decompresses/dumps the project’s LZ command-stream format; used for `%.lz` assets | Build-time compressor; output is consumed by runtime decompression code |
| `gfx` | Postprocesses tile binaries: trim/remove blank tiles, interleave tiles, dedupe tiles, remove flip-equivalent tiles, preserve specific indices | Build-time only, but output bytes are runtime graphics data |
| `gbcpal` | Merges/normalizes `.gbcpal` palette files into a 4-color GBC palette | Build-time only, but output is runtime palette data |
| `png_dimensions` | Validates pic PNG widths and emits packed dimensions bytes | Build-time only, but output is runtime metadata |
| `make_patch` | Expands VC patch templates against retail vs VC ROM differences, using `.sym` symbol resolution | Build-time only for VC artifacts |
| `stadium` | Rewrites the global checksum and appends N64PS3/Stadium checksum data at the end of 2 MiB ROMs | Final-ROM postprocess for matching/Stadium compatibility |

### `scan_includes`

`scan_includes` tokenizes assembly source, skips comments/strings, recognizes `INCLUDE` and `INCBIN`, prints referenced file paths, and recursively descends into `INCLUDE`d files (`tools/scan_includes.c:28-107`). The Makefile uses it to synthesize dependencies for every object and for `includes.asm` itself (`Makefile:155-159`).

### `lzcomp`

`%.lz: %` invokes `tools/lzcomp $(LZFLAGS) -- $< $@` (`Makefile:209-210`). `gfx/lz.mk` then overrides `LZFLAGS` per asset to force matching compression settings (`gfx/lz.mk:1-60`).

The compressor supports binary/text output, decompression, dump mode, alignment, and explicit method/compressor selection (`tools/lz/options.c:97-121`). Internally it can optimize across **96 methods** spanning four compressor families:

- `singlepass` (72 methods)
- `null` (2 methods)
- `repetitions` (6 methods)
- `multipass` (16 methods) (`tools/lz/global.c:3-10`)

The main entry point either compresses input to a command stream or reconstructs uncompressed data from an existing stream (`tools/lz/main.c:3-25`). This is a build tool, but its output format matters to runtime because the game consumes `.lz` resources.

### `gfx`

The catch-all graphics rules run `rgbgfx` first, then optionally run `tools/gfx` over the produced tile data (`Makefile:360-368`). The tool supports:

- `--trim-whitespace`: remove trailing all-zero tiles only (`tools/gfx.c:117-126,269-271`)
- `--remove-whitespace`: remove all all-zero tiles, unless preserved (`tools/gfx.c:132-147,291-293`)
- `--interleave --png=<file>`: reorder tiles based on PNG width; requires the PNG so width can be read (`tools/gfx.c:128-130,242-255,272-278`)
- `--remove-duplicates`: remove repeated tiles (`tools/gfx.c:165-185,279-281`)
- `--keep-whitespace`: keep whitespace tiles when deduping / flip-removing (`tools/gfx.c:29,171-173,226-228`)
- `--remove-xflip` / `--remove-yflip`: remove tiles already representable as horizontal/vertical flips of earlier tiles (`tools/gfx.c:209-240,282-290`)
- `--preserve indexes`: exempt specific tile indices from removal (`tools/gfx.c:32,62-67,91-106`)
- `--depth`: operate on 1bpp or 2bpp tile sizes (`tools/gfx.c:16,68-70,128-130`)

`Makefile` applies these transformations surgically to many assets (intro fire, Pokédex art, title logos, slots graphics, battle anims, trainer card, borders, etc.) (`Makefile:279-355`).

### `gbcpal`

`gbcpal` reads one or more `.gbcpal` files, unpacks 15-bit GBC colors, sorts by luminance, filters out black/white and duplicates, then writes a 4-color palette `[white, color1, color2-or-color1, black]` (`tools/gbcpal.c:35-60,62-132`). `--reverse` flips the luminance ordering (`tools/gbcpal.c:6-26,55-60`).

The Makefile uses it both generically (`Makefile:370-372`) and for combined Pokémon front/back palettes (`Makefile:222-223,238-239,262-263,268-277`).

### `png_dimensions`

`png_dimensions` reads the PNG width, requires exactly 40/48/56 px, converts that to 5/6/7 tiles, and emits one byte with width in both nibbles (`tools/png_dimensions.c:6-22`). The generic rule is `%.dimensions: %.png` (`Makefile:374-375`). This is runtime-relevant metadata for variable-size Pokémon pictures.

### `make_patch`

`make_patch` parses the linker symbol file, interprets patch-template commands, compares the VC ROM against the original retail ROM, and verifies that every ROM difference is accounted for except ignored ranges and checksum bytes (`tools/make_patch.c:116-160,174-210,352-438,446-527`).

Notable built-in ignores:

- header global checksum bytes at `0x014E-0x014F` are always ignored (`tools/make_patch.c:367-369`)
- the Makefile also ignores the entire Stadium footer region `0x1ffdf8:0x208` (`Makefile:140-141`; `tools/make_patch.c:370-373`)

### `stadium`

`stadium` only does work if the file is exactly **128 banks × 0x4000 = 2 MiB** (`tools/stadium.c:6-12,80-92`). It:

1. clears the Game Boy global checksum field at `0x014E-0x014F` (`tools/stadium.c:13-15,55-57`)
2. zeroes and rewrites the `N64PS3` footer at the end of the ROM (`tools/stadium.c:17-25,58-60`)
3. computes 2-byte checksums for every half-bank (`tools/stadium.c:21-23,62-66`)
4. computes a CRC over those checksum bytes (`tools/stadium.c:27-29,68-73`)
5. recomputes the ROM global checksum (`tools/stadium.c:75-77`)

`main.asm` reserves space for this footer as `SECTION "Stadium 2 Checksums", ROMX[$7DF8], BANK[$7F]` and explains the historical reason in a comment (`main.asm:385-390`).

## 5) Existing test / verification infrastructure

### `make compare`

`compare` depends on all four `.gbc` outputs plus both `.patch` outputs, then runs `$(SHA1) -c roms.sha1` (`Makefile:119-120`). The Makefile selects `sha1sum` if available, otherwise `shasum` (`Makefile:47-51`).

So the project’s primary verification loop is:

- build artifacts
- compare each final artifact against `roms.sha1`
- fail if any hash mismatches (`Makefile:119-120`; `roms.sha1:1-6`)

### CI

GitHub Actions installs RGBDS `v1.0.1` and:

- on the upstream `pret` repo, runs `make DEBUG=1 ... compare`
- on forks, runs plain `make` (`.github/workflows/main.yml:36-46,92-102`)

After building, CI also runs `.github/checkdiff.sh`, which fails if the working tree changed (`.github/checkdiff.sh:1-8`). That catches generated-file drift.

### Other testing

I did **not** find any in-tree unit-test, emulator-test, or dedicated `tests/` harness. The explicit verification infrastructure visible in this repo is the SHA1 compare target plus CI’s “no diff after build” check (`Makefile:71-82,119-123`; `.github/workflows/main.yml:36-46,92-102`; `.github/checkdiff.sh:1-8`).

## 6) Which Pokémon Gold revision this targets

What is clear from the repo:

- This targets the **international English Gold/Silver pair**, not the Japanese pair.
- Gold uses title/game ID flags `-t POKEMON_GLD -i AAUE`; Silver uses `-t POKEMON_SLV -i AAXE` (`Makefile:191-196`).
- `README.md` names the retail outputs `Pokemon - Gold Version (UE) [C][!]` and `Pokemon - Silver Version (UE) [C][!]` (`README.md:5-12`).

Practical reading: the repo is matching the **UE (USA/Europe) English** releases, not JP.

`UNCLEAR:` whether the repo itself explicitly identifies the retail Gold target as “v1.0” vs “Rev A / v1.1”. The in-tree ground truth is the SHA1 hash (`roms.sha1:1`) plus the UE header/game ID (`Makefile:191-196`; `README.md:5-12`), but I did not find a human-readable revision label inside the repository.

## 7) Graphics pipeline

### Base pipeline

The generic graphics rules are (`Makefile:360-375`):

- `%.2bpp: %.png`
  - run `rgbgfx --colors dmg ... -o $@ $<`
  - optionally postprocess output with `tools/gfx`
- `%.1bpp: %.png`
  - same, but `--depth 1`
- `%.gbcpal: %.png`
  - run `rgbgfx -p $@ $<`
  - normalize with `tools/gbcpal`
- `%.dimensions: %.png`
  - run `tools/png_dimensions`

So `rgbgfx` is the **first-stage PNG converter** that turns indexed PNGs into raw Game Boy tile/palette data; the repo’s custom tools then reshape that output to match retail bytes (`Makefile:360-375`).

### Pokémon / trainer sprite rules

For Pokémon and trainer sprites, the Makefile does not use the generic DMG path. Instead it runs `rgbgfx` with GBC palette files and often `--columns` (`Makefile:215-246,252-263,284-285,339-348`).

Examples:

- Pokémon front/back sprites: `rgbgfx ... --colors gbc:<normal.gbcpal>` (`Makefile:216-223,229-239`)
- trainer sprites: same pattern (`Makefile:244-246`)
- egg, player sprites, battle “dude”, and some new-game art also use per-file flags (`Makefile:252-255,284-285,339-348`)

`UNCLEAR:` this repo uses RGBGFX’s `--columns` flag extensively, but the flag’s semantics are not documented in-tree. The ground truth here is only **where** it is used (`Makefile:216-221,229-237,244-246,284-285,339-348`).

### Custom `tools/gfx` transformations used by the Makefile

The Makefile attaches asset-specific `tools/gfx += ...` arguments before the generic `%.1bpp` / `%.2bpp` rules run (`Makefile:279-355,360-368`). Important transformations:

- `--remove-whitespace`: remove all zero/blank tiles (`Makefile:279,287-291,301-303,310,319,325-337,346`)
- `--trim-whitespace`: remove only trailing blank tiles (`Makefile:293-299,302,305-306,314,318,321,323-324,342,344,350,352-353`)
- `--interleave --png=$<`: reorder tiles based on source PNG width (`Makefile:307-308,315-316`)
- `--remove-duplicates`: collapse repeated tiles (`Makefile:311,316,330`)
- `--keep-whitespace`: keep blank tiles while deduping/flips (`Makefile:316,333`)
- `--remove-xflip`: remove horizontal-flip duplicates (`Makefile:316,322,330,332-333,346`)
- `--remove-yflip`: supported by the tool (`tools/gfx.c:31,285-290`) but I did not see it used in this Makefile
- `--preserve=...`: preserve tile indices even when deduping (`Makefile:311`)

### Compression stage after graphics conversion

Many generated graphics are then compressed to `.lz` using `tools/lzcomp` (`Makefile:204-210`). `gfx/lz.mk` exists specifically to force matching compression settings, e.g.:

- default `%.lz` uses `--compressor multipass` (`gfx/lz.mk:1-4`)
- selected assets pin specific `--method` and/or `--align` values (`gfx/lz.mk:5-60`)
- some assets intentionally use the `null` compressor with method 1, i.e. effectively stored/uncompressed payloads inside the project’s LZ command-stream format (`gfx/lz.mk:26-30,42-44`)

That file is purely about **matching the original byte-for-byte compressed assets** (`gfx/lz.mk:1`).

## 8) Link / layout (`layout.link`)

`rgblink` consumes `layout.link` via `-l layout.link` (`Makefile:198-199`). The file fixes named sections into exact ROM/RAM banks and sometimes exact addresses (`layout.link:1-308`).

### High-level ROM organization

| Banks | Contents |
|---|---|
| `ROM0` | interrupt vectors, header, home code (`layout.link:1-31`) |
| `01-05` | early core banks (`bank1`..`bank5`) (`layout.link:32-41`) |
| `06-08` | tileset data, roofs, extra songs, clock reset, catch tutorial, egg moves (`layout.link:42-53`) |
| `09-11` | additional core banks plus battle/evolution data (`layout.link:53-72`) |
| `12-20` | picture pointers plus `Pics 1`-`Pics 13`, including Unown and trainer pic pointers (`layout.link:72-106`) |
| `21-26` | credits, more code, maps/events, title screen (`layout.link:106-118`) |
| `2A-2B` | map blocks 1-2 (`layout.link:118-121`) |
| `2E` | `Pics 14` plus late-bank content at `org $6300` (`layout.link:122-125`) |
| `30-32` | sprite banks and `The End` graphics (`layout.link:126-134`) |
| `33` | move animations and extra songs (`layout.link:135-137`) |
| `36-39` | inverse font, map blocks 3, tileset data 5, copyright, title screen 2 (`layout.link:138-148`) |
| `3A-3D` | audio, songs 1-4, SFX, cries (`layout.link:149-159`) |
| `3E-3F` | shrink pics and more code (`layout.link:160-165`) |
| `40-62` | standard scripts, phone scripts, and `Map Scripts 1` through `Map Scripts 32` (`layout.link:166-233`) |
| `64-66` | text banks 1-3 (`layout.link:234-239`) |
| `68-6B` | Pokédex entries 001-251 split into four banks (`layout.link:240-247`) |
| `6C-6E` | names, move descriptions, item descriptions (`layout.link:248-253`) |
| `70` | misc late assets: tileset data 6, Pokégear GFX, credits strings (`layout.link:254-259`) |
| `7F` | Stadium 2 checksum footer at `org $7df8` (`layout.link:260-262`) |

A few anchor examples tying link-script names back to source files:

- `SECTION "Maps"` is defined in `data/maps/map_data.asm` (`data/maps/map_data.asm:1`)
- `SECTION "Map Scripts 1"` etc. are defined in `data/maps/scripts.asm` (`data/maps/scripts.asm:1-485`)
- `SECTION "Credits"` is defined in `engine/movie/credits.asm` (`engine/movie/credits.asm:4`)
- `SECTION "Pic Pointers"` / `Pics N` come from `gfx/pics_gold.asm` or `gfx/pics_silver.asm` (`gfx/pics_gold.asm:5-20`; `gfx/pics_silver.asm:5-20`)
- `SECTION "Stadium 2 Checksums"` is reserved in `main.asm` (`main.asm:385-390`)

### RAM layout also fixed here

`layout.link` also locks down RAM sections:

- `WRAM0`: audio RAM, main WRAM, palettes, sprites, tilemap, overworld map, video (`layout.link:263-277`)
- `WRAMX 1`: WRAM bank 1, game data, party, stack (`layout.link:277-281`)
- `VRAM $00/$01`: two VRAM banks (`layout.link:282-285`)
- `SRAM $00-$03`: scratch/save/boxes/backups/HOF/link data (`layout.link:286-304`)
- `HRAM`: OAM DMA and HRAM (`layout.link:305-307`)

### Important special placements

- The header is fixed at `ROM0[$0100]` (`layout.link:28-31`).
- Picture pointer sections and other named sections sometimes start at exact addresses with `org`, e.g. `ROMX $12 / org $4000` and `ROMX $1f / org $4000` (`layout.link:72-75,99-102`).
- The Stadium footer is explicitly placed at bank `7F`, offset `0x7DF8` (`layout.link:260-262`; `main.asm:385-390`).

## Bottom line

If you need a concise mental model:

- `make gold` / `make silver` assemble many objects with `_GOLD` or `_SILVER`, link with `layout.link`, run `rgbfix`, then run `tools/stadium` (`Makefile:132-137,198-201`).
- `make compare` is the project’s real “test”: build all standard/debug/VC artifacts and SHA1-check them against `roms.sha1` (`Makefile:119-120`; `roms.sha1:1-6`).
- RGBDS is pinned to `1.0.1`, but `rgbdscheck.asm` only enforces “RGBDS major version >= 1” (`.rgbds-version:1`; `rgbdscheck.asm:1-6`).
- The repo matches the **UE international English** Gold/Silver releases; `UNCLEAR:` the in-repo docs do not label the retail Gold hash as v1.0 vs v1.1 (`Makefile:191-196`; `README.md:5-12`; `roms.sha1:1`).
