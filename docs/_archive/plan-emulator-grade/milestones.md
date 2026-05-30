# Pokegold C# reimplementation milestone plan

Target: retail `pokegold.gbc` parity first, with `pokesilver` and VC behavior deferred until Gold is stable. Treat the pret repo as byte-accurate ground truth, not as design inspiration.

## Assumptions and verification legend

- Canonical runtime target: retail Gold (`pokegold.gbc`), matching `docs/recon/build-system.md`.
- Critical path ends at a complete Gold playthrough that reaches Hall of Fame and credits.
- Link/trading/printer stay off the credits critical path unless a later milestone explicitly pulls them in.
- `UNCLEAR:` marks places where the repo does not fully answer a dependency or ownership question.

Verification approaches from `docs/conventions/verification.md`:

- **A** — SameBoy-backed lockstep at frame/VBlank boundaries
- **B** — Per-frame domain hashes (`logicHash`, `videoHash`, `timingHash`, `audioHash`, `saveHash`)
- **C** — Recorded input replay (frame-indexed)
- **D** — Per-routine fixtures against captured ROM state
- **E** — Framebuffer / screenshot comparison
- **F** — Save compatibility / round-trip testing

---

## Ordered milestones

### M01 — Solution scaffolding and canonical reference target
- **Goal** — A buildable .NET solution exists with separate core/platform/test projects, can load the canonical Gold ROM and symbol artifacts, and can run a headless deterministic harness skeleton.
- **Scope** — Build/layout spine and reference artifacts: `main.asm`, `home.asm`, `ram.asm`, `layout.link`, `Makefile`, `roms.sha1`; repo-wide organization from `docs/recon/source-map.md` build/layout sections.
- **Verification criteria** — B baseline manifest for the retail Gold ROM SHA1; D smoke fixtures proving ROM + `.sym` + `.map` ingestion; headless harness can step a scripted bootstrap slice deterministically.
- **Dependencies** — None.
- **Estimated complexity** — M
- **Risk notes** — Easy to pick the wrong ROM target or erase pret umbrella-file semantics too early. Gold/Silver/version split is mostly shared code with version-specific data roots, so the scaffold must not bake in Silver/VC assumptions accidentally.

### M02 — GoldMemory, bus, typed views, and bank identity
- **Goal** — Raw, banked, byte-addressable ROM/VRAM/SRAM/WRAM/HRAM/OAM storage exists with typed span-backed views layered on top; no gameplay state is modeled as detached object graphs.
- **Scope** — `ram\{vram,wram,sram,hram}.asm`, `layout.link`, `home\copy.asm`, `home\sram.asm`, `home\map.asm`, `home\battle.asm`, `constants\{hardware.inc,ram_constants.asm,map_data_constants.asm,pokemon_data_constants.asm}`.
- **Verification criteria** — D fixtures for far-byte reads, party-struct access, save checksum boundaries, and active-box chunk copies; B logic hashes over synthetic memory round-trips; future F save tests must consume the exact same backing stores.
- **Dependencies** — M01.
- **Estimated complexity** — XL
- **Risk notes** — Mixed endianness, `UNION`/overlay aliasing, active box living outside `sGameData`, and HRAM/OAM DMA bytes are all load-bearing. Hazards: MBC3 banking semantics, WRAM aliasing, and save-layout-sensitive glitches.

### M03 — CpuMath, flag model, and banked dispatch helpers
- **Goal** — A reusable helper layer exists for 8-bit flags, `daa`, carry chains, rotates, `farcall`, `predef`, and jump-table dispatch, so later subsystem ports do not hand-roll incompatible arithmetic/control-flow.
- **Scope** — `engine\math\math.asm`, `home\math.asm`, `home\random.asm`, `home\farcall.asm`, `home\predef.asm`, `home\header.asm`, `macros\{farcall,predef,const}.asm`, `data\predef_pointers.asm`.
- **Verification criteria** — D fixtures for `Random`, `RandomRange`, `_Multiply`, `_Divide`, `FixTime` carry propagation, and `daa` examples; B micro-trace hashes from SameBoy captures.
- **Dependencies** — M02.
- **Estimated complexity** — L
- **Risk notes** — Stale carry RNG, `ccf`-based time math, synthetic calls (`push return` + `jp hl`), and MBC3 bank restore behavior are easy to mistranslate. Hazards: flag-coupled arithmetic/control flow, BCD math, stack/register tricks.

### M04 — Platform layer and verification harness
- **Goal** — `IPlatform`/`IDisplay`/`IAudioOutput`/`IInputSource`/`IBatteryStore` exist, a first Sokol.NET backend is usable, and a SameBoy-backed replay + hash + framebuffer-diff harness is operational.
- **Scope** — Behavior boundary defined by `home\vblank.asm`, `home\joypad.asm`, `home\audio.asm`, `audio\engine.asm`, `home\serial.asm`, `home\printer.asm`; verification inputs from `pokegold.gbc`, `.sym`, and `.map`.
- **Verification criteria** — A/B/C/E harness can replay the first few hundred boot frames and emit domain hashes plus screenshot diffs; battery image load/save smoke works; CI lane exists for short replays and routine fixtures.
- **Dependencies** — M01-M03.
- **Estimated complexity** — XL
- **Risk notes** — The easiest mistake is pushing Game Boy semantics into the backend instead of core code. `UNCLEAR:` direct SameBoy P/Invoke may be less maintainable than a small native wrapper.

### M05 — Reset vector through hardware init to first stable blank frame
- **Goal** — `_Start` through `Init` runs correctly: interrupts/registers are reset, WRAM/VRAM/HRAM are cleared, the OAM DMA stub is installed in HRAM, RTC startup runs, LCD comes back on, and the first stable post-init frame matches reference behavior.
- **Scope** — `home\header.asm`, `home\init.asm`, `home\lcd.asm`, `home\delay.asm`, `engine\gfx\load_push_oam.asm`, `home\sram.asm`, `home\time.asm`.
- **Verification criteria** — A/B/C boot replay to the first post-init VBlank; E blank/initial framebuffer checkpoints; D fixtures for DMA stub installation, interrupt-mask setup, and RTC latch/start behavior.
- **Dependencies** — M02-M04.
- **Estimated complexity** — M
- **Risk notes** — Boot-ROM register assumptions, HRAM DMA trampoline behavior, LCD off/on timing, and RTC latch-close/open ordering all matter. Hazards: HRAM trampoline, interrupt timing, boundary conditions from startup state.

### M06 — Nintendo logo / startup presentation compatibility
- **Goal** — The port has an explicit, tested startup presentation policy that hands off cleanly into cartridge code and does not leave this boot-visible gap implicit.
- **Scope** — Entry contract around `home\header.asm`; title-hand-off plumbing in `engine\menus\intro_menu.asm`; any host-side compatibility shell needed before the cartridge-owned flow begins.
- **Verification criteria** — C/E startup replay or screenshot sequence with a documented hand-off boundary; decision recorded on whether the logo is boot-ROM emulation or a host-side compatibility veneer.
- **Dependencies** — M05.
- **Estimated complexity** — S
- **Risk notes** — `UNCLEAR:` the repo begins after the Nintendo boot ROM, so no decomposed in-tree subsystem truly “owns” the logo. This milestone is partly a product decision, not just a code-port decision.

### M07 — VBlank scheduler, joypad polling, and title screen with audio
- **Goal** — `IntroSequence`, `StartTitleScreen`, and `MainMenu` run with correct VBlank ordering, joypad edge detection, OAM DMA timing, palette pushes, and title-screen audio.
- **Scope** — `home\vblank.asm`, `home\joypad.asm`, `home\video.asm`, `home\palettes.asm`, `home\tilemap.asm`, `engine\menus\{intro_menu,main_menu}.asm`, `audio\engine.asm`, `audio\music\{titlescreen,mainmenu}.asm`, `engine\gfx\{load_font,color,cgb_layouts,sgb_layouts}.asm`.
- **Verification criteria** — A/B/C boot-to-title replay; E title/main-menu checkpoints; B `audioHash` and register-state comparison during title attract mode.
- **Dependencies** — M04-M06.
- **Estimated complexity** — L
- **Risk notes** — VBlank carry-return scheduling is part of correctness, not an optimization. Hazards: VRAM/STAT timing, stale-carry RNG advancing inside VBlank, OAM DMA gating, audio tick order.

### M08 — Text engine and window/tilemap primitives
- **Goal** — All `TX_*` commands, inline charmap control bytes, textbox pacing, number/BCD formatting, far text, and `done` vs `text_end` semantics work exactly enough to support every later menu/script subsystem.
- **Scope** — `home\text.asm`, `home\print_text.asm`, `home\print_num.asm`, `home\print_bcd.asm`, `home\window.asm`, `home\tilemap.asm`, `data\text\*`, `constants\charmap.asm`, `macros\scripts\text.asm`.
- **Verification criteria** — D fixtures for `TX_FAR`, `TX_BCD`, `TX_STRINGBUFFER`, and `<DONE>`/`TX_END`; A/B/C scripted textbox replays; E textbox/framebuffer diffs.
- **Dependencies** — M07.
- **Estimated complexity** — L
- **Risk notes** — Text is raw-byte-driven and fixed-buffered; normalizing it to strings will erase bugs and behavior. Hazards/glitches: Coin Case terminator mismatch, text buffer overflow, fixed string buffers, far-bank text fetch/restore.

### M09 — Menu system, naming screen, options, and start menu
- **Goal** — Reusable menu/window/cursor engines support title options, naming, scrolling menus, start menu, and trainer card flows with correct metadata overlays and joypad filtering.
- **Scope** — `engine\menus\{menu,menu_2,scrolling_menu,naming_screen,options_menu,start_menu,trainer_card}.asm`, `home\menu.asm`, `home\scrolling_menu.asm`, `home\names.asm`, `data\text\name_input_chars.asm`.
- **Verification criteria** — C/B menu-navigation replays; E visual diffs for naming/options/start menus; D fixtures for `wMenu*`, `w2DMenu*`, and scrolling-menu metadata overlays.
- **Dependencies** — M08.
- **Estimated complexity** — M
- **Risk notes** — Many menus reuse the same WRAM overlays, so over-structuring them will hide aliasing bugs. Hazards: stack tricks in tilemap copy helpers, interrupt-sensitive whole-screen VRAM copies.

### M10 — Overworld map loading, movement, and map connections
- **Goal** — New Game/Continue can enter a live overworld map; player and NPC movement, collisions, warps, scrolling, and ordinary map connections all function in the steady-state `HandleMap` loop.
- **Scope** — `engine\overworld\{events,init_map,map_setup,load_map_part,map_objects,map_objects_2,overworld,player_object,player_movement,player_step,movement,npc_movement,tile_events}.asm`, `home\map.asm`, `home\movement.asm`, `home\map_objects.asm`, `data\maps\{maps,attributes,blocks,setup_scripts}.asm`, `data\tilesets.asm`, `gfx\tilesets.asm`.
- **Verification criteria** — A/B/C replays from New Bark Town through ordinary route/map transitions; E checkpoints for scrolling/warp/connection scenes; D fixtures for connection-strip math, map-header decoding, and player-step vectors.
- **Dependencies** — M07-M09.
- **Estimated complexity** — XL
- **Risk notes** — The overworld loop is a scheduler, not just movement code. Hazards/glitches: connection-load timing, `sp`-as-copy-pointer VRAM loops, queued overworld commands, map buffer overlays, default WRAM-bank assumptions.

### M11 — NPC/event scripting, callbacks, trainer triggers, and field interactions
- **Goal** — The full map/event VM works: scene scripts, callbacks, trainer encounters, movement scripts, standard specials, hidden items, field moves, whiteout/reload flows, and script-driven state transitions.
- **Scope** — `engine\overworld\scripting.asm`, `engine\overworld\cmd_queue.asm`, `engine\overworld\variables.asm`, `macros\scripts\{events,maps,movement}.asm`, `engine\events\{std_scripts,specials,trainer_scripts,overworld,field_moves,forced_movement,checkforhiddenitems,fruit_trees,money,misc_scripts,whiteout,magnet_train,mom,mom_phone}.asm`, `maps\*.asm`.
- **Verification criteria** — D opcode fixtures across control-flow, variable, text, battle, and map/object opcode families; A/B/C story-segment replays; E scene-transition checkpoints.
- **Dependencies** — M10.
- **Estimated complexity** — XL
- **Risk notes** — This is a 162-opcode bytecode interpreter with a real script stack and banked PCs. Hazards/glitches: synthetic calls, script-stack overflow behavior, map connection bugs during scripted Surf, fixed text buffers reached through scripts.

### M12 — Pokémon data model, items, party/PC, breeding, and core gameplay tables
- **Goal** — Party/box structs, stat calculation, evolutions, DV/PP packing, TM/HM compatibility, Bill’s PC, daycare/breeding, Pokédex flags, and bag pockets all behave byte-accurately.
- **Scope** — `engine\pokemon\{party_menu,mon_menu,mon_stats,stats_screen,health,experience,learn,evolve,tempmon,move_mon,bills_pc,bills_pc_top,breeding,mail}.asm`, `data\pokemon\{base_stats,base_stats\*,evos_attacks,egg_moves,palettes,pic_pointers}.asm`, `engine\items\{items,pack,item_effects,tmhm,buy_sell_toss,switch_items,mart}.asm`, `data\items\*`, `constants\{pokemon_data_constants,item_data_constants,item_constants}.asm`.
- **Verification criteria** — D fixtures for stat calculation, EXP underflow, DV inheritance, packed PP, TM/HM pocket logic, egg creation/hatching, and box chunk copies; B logic/save hashes for party/PC scenarios; C replays for party menu, PC, and daycare paths.
- **Dependencies** — M09-M11.
- **Estimated complexity** — XL
- **Risk notes** — The hidden egg species byte, stale party stats, PP-up bit packing, and non-contiguous TM/HM numbering are all compatibility-sensitive. Glitches: Celebi Egg, stat recalculation glitch, wrong pocket TMs, DV inheritance quirks, PP overflow.

### M13 — Battle engine core, move scripts, AI, and battle visuals
- **Goal** — Wild and trainer battles run end-to-end with correct turn order, damage/effects, AI decisions, battle menus, transitions, HUDs, and map reload after battle.
- **Scope** — `engine\battle\{core,effect_commands,start_battle,battle_transition,menu,trainer_huds,used_move_text,anim_hp_bar,hidden_power,read_trainer_*.asm}`, `engine\battle\ai\*`, `engine\battle\move_effects\*`, `data\battle\*`, `data\moves\{moves,effects,effects_pointers}.asm`, `home\battle.asm`, `engine\battle_anims\*`, `gfx\battle_anims.asm`.
- **Verification criteria** — A/B/C deterministic battle replays with frame and turn checkpoints; D fixtures for damage, EXP, AI scoring, packed PP, and AI-only type-matchup misuse; E battle intro/HUD checkpoints; B `audioHash` spot checks on battle intro and one move resolution.
- **Dependencies** — M11-M12.
- **Estimated complexity** — XL
- **Risk notes** — Battle is the densest concentration of stale carry, carry chains, overlay aliasing, and bug-preservation requirements. Glitches: trainer AI exploits, PP overflow, type-matchup AI misuse, RNG manipulation, EXP underflow.

### M14 — Full audio engine, cries, SFX, and special VBlank modes
- **Goal** — All music, cries, SFX, wave RAM behavior, fades, and sound-only/cutscene/credits VBlank interactions are complete, not just the title-screen subset.
- **Scope** — `audio\engine.asm`, `audio\music_pointers.asm`, `audio\sfx*.asm`, `audio\cries.asm`, `audio\drumkits.asm`, `audio\notes.asm`, `audio\wave_samples.asm`, `audio\music\*`, `home\audio.asm`, `home\vblank.asm` special handlers.
- **Verification criteria** — B `audioHash` across title/battle/overworld/credits scenarios; C replay suite for music/SFX/cry transitions; D fixtures for wave RAM writes and fade sequencing; A lockstep on scenes that change VBlank mode for sound.
- **Dependencies** — M07, M13.
- **Estimated complexity** — L
- **Risk notes** — Audio is register-shaped and frame-ticked; backend buffering must not hide ordering bugs. Hazards: sound registers, VBlank-as-scheduler behavior, special VBlank modes interacting with LCD/serial work.

### M15 — Save/load, RTC, Hall of Fame, and long-lived battery state
- **Goal** — Continue/save/delete work, RTC state and 140-day wrap match the original, active box and numbered boxes round-trip, and Hall of Fame behavior remains compatible, including edge cases.
- **Scope** — `engine\menus\{save,empty_sram,delete_save,savemenu_copytilemapatonce}.asm`, `engine\rtc\*`, `home\sram.asm`, `home\time.asm`, `ram\sram.asm`, `engine\events\halloffame.asm`, `engine\events\daycare.asm`, `engine\pokemon\move_mon.asm`.
- **Verification criteria** — F bidirectional save compatibility and battery-image round-trips; C interrupted-save and box-switch replays; D fixtures for checksum, backup checksum, `FixDays`, `SetClock`, and RTC staging; B `saveHash` over continue/save cycles.
- **Dependencies** — M12-M14.
- **Estimated complexity** — XL
- **Risk notes** — Save flow is intentionally phaseful and interruptible. Glitches/hazards: Bad Clone, save corruption exploits, RTC latch semantics, modulo-140 day wrap, Hall of Fame without prior save, active box outside checksum.

### M16 — Complete data tables and content coverage for a full Gold run
- **Goal** — All static data and content needed for a complete Gold playthrough are translated/loaded correctly: maps, trainer parties, wild data, text, item tables, Pokémon tables, map scripts, and credits strings.
- **Scope** — `maps\*.asm`, `data\maps\*.asm`, `data\trainers\*.asm`, `data\wild\*.asm`, `data\pokemon\*`, `data\items\*`, `data\moves\*`, `data\text\*`, `data\credits_strings.asm`, `constants\*.asm`, `gfx\pics_gold.asm`.
- **Verification criteria** — B/C long scenario replays across Johto progression, marts, gyms, daycare, PC, and Elite Four lead-in; D fixture sweeps for table loaders and enum/id gaps; E checkpoints on representative map/script content.
- **Dependencies** — M10-M15.
- **Estimated complexity** — XL
- **Risk notes** — Large content banks hide silent ID/offset mistakes. Risks include non-contiguous TM/HM/item IDs, per-map scene/callback counts, Gold/Silver data splits, and trainer/wild table parsing errors.

### M17 — Glitch regression matrix and explicit link/trade deferral
- **Goal** — All 15 documented glitches have named fixtures and pass; link/trading/printer are either explicitly deferred off the critical path or promoted with their own follow-on plan after credits parity.
- **Scope** — Cross-cutting regression coverage over `engine\{battle,items,pokemon,overworld,events,menus,rtc,link,printer}\*`, `home\{random,serial,text}.asm`, `ram\{wram,sram,hram}.asm`; optional `engine\link\*`, `home\serial.asm`, `home\printer.asm`, `engine\printer\*` for deferred triage.
- **Verification criteria** — D/C/F glitch fixtures following the catalog in `verification.md`; nightly A/B replays for RNG, save, RTC, AI, text, and map-connection cases; written deferral decision for serial/link/printer parity.
- **Dependencies** — M13-M16.
- **Estimated complexity** — L
- **Risk notes** — The biggest risk is “cleaning up” original bugs by accident. Serial/printer timing is one of the hardest hazard clusters and should not block the credits milestone unless product scope changes.

### M18 — Complete Gold playthrough to Hall of Fame and credits
- **Goal** — A full Gold playthrough replay reaches Hall of Fame, runs credits, returns cleanly, and remains byte/frame/save compatible with the reference implementation.
- **Scope** — Integration of all prior milestones, especially `engine\events\halloffame.asm`, `engine\movie\credits.asm` (per `docs/recon/execution-flow.md`), `home\vblank.asm` credits mode, `audio\music\{halloffame,credits,postcredits}.asm`, and the late-game maps/trainers/data exercised by a full run.
- **Verification criteria** — A/B/C full-playthrough replay with sparse full dumps at major checkpoints; E golden screenshots for Hall of Fame and credits; F pre/post-run save compatibility; complete glitch suite green.
- **Dependencies** — M17.
- **Estimated complexity** — XL
- **Risk notes** — Long-run drift, special credits STAT/VBlank behavior, Hall of Fame save edge cases, and incomplete late-game content are the main integration risks. This is the first milestone that proves the port as a game rather than as a collection of subsystems.

---

## Parallelization notes

### Strictly sequential backbone
- **M01 -> M02 -> M03 -> M04** should stay sequential enough to lock the core architecture before large gameplay ports begin.
- **M05 -> M07** is the first visible boot/title chain and should also stay mostly sequential because it validates the frame heartbeat everything else depends on.
- **M18** is purely integration and should start only after the glitch matrix (M17) is largely green.

### Good parallel work bands
- After **M04**, one team can push **M05-M07** while another prepares fixture-capture infrastructure and routine harness extensions under the same architecture.
- After **M09**, **M10** (overworld loop/map loading) and **M12** (party/items/data model) can advance in parallel, because battle and save later need both.
- After **M11**, **M13** (battle) and **M15** (save/RTC) can proceed in parallel once party/box and text/menu plumbing are stable.
- After **M15**, **M16** (content coverage) and **M17** (glitch matrix) can run in parallel; one is breadth-first data/content work, the other is depth-first compatibility work.

### Explicitly deferred from the credits critical path
- `engine\link\*`, `home\serial.asm`, `home\printer.asm`, and `engine\printer\*` are **not** required for the Hall of Fame/credits critical path.
- Time Capsule, Mystery Gift transport specifics, cable link, and printer support should be scheduled only after Gold credits parity unless product scope changes.
- `UNCLEAR:` if a future requirement demands authentic boot-ROM Nintendo logo behavior or full link parity before launch, those should become new pre-release milestones rather than being smuggled into the current critical path.
