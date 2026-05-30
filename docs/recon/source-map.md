# Source map

Purpose: a file-by-file layout map for future Claude instances working in the `pret/pokegold` disassembly. Line counts are physical line counts for text files; committed binary assets (`.blk`, `.png`, `.2bpp`, `.lz`, `.gbcpal`, etc.) are marked `n/a (binary)`.

## Build/layout spine

- `main.asm` is the main ROM bank layout file: it `INCLUDE`s banked engine/data files across battle, overworld, menus, Pokémon, items, link, graphics, audio, and text banks (`main.asm:1-382`).
- `home.asm` is the ROM0 spine and `INCLUDE`s the `home/` routines that are always callable without bank switching (`home.asm:1-60`).
- `ram.asm` is the RAM spine and `INCLUDE`s the split RAM definitions for VRAM, WRAM, SRAM, and HRAM (`ram.asm:1-7`).

## Top-level source directories

| Directory | Verified role | Evidence |
|---|---|---|
| `home\` | ROM0 routines always reachable without bank switching. | `home.asm:6-60` |
| `engine\` | Banked engine code. | `main.asm:3-377` |
| `data\` | Banked data tables and text. | `main.asm:12, 21, 83-92, 117, 134, 172, 211-212, 327-382` |
| `constants\` | Constant/enum definitions. | file heads such as `constants\item_constants.asm:1-6`, `constants\map_data_constants.asm:1-12` |
| `macros\` | RGBASM macros plus DSLs for scripts/audio/text/maps. | file heads such as `macros\code.asm:1-12`, `macros\scripts\events.asm:1-15` |
| `audio\` | Sound engine plus music/SFX/cry data. | `audio.asm:1-140`, `audio\engine.asm:1-100` |
| `maps\` | Per-map script/object files and raw block layouts. | `maps\NewBarkTown.asm:1-80`, `data\maps\blocks.asm:1-20` |
| `gfx\` | Graphics assets, manifests, and compression rules. | `gfx\misc.asm:1-72`, `gfx\pics_gold.asm:5-23`, `gfx\lz.mk:1-60` |
| `ram\` | WRAM/HRAM/SRAM/VRAM layouts. | `ram\wram.asm:1-120`, `ram\sram.asm:1-160`, `ram\hram.asm:1-120`, `ram\vram.asm:1-18` |

## RAM layout files

For quick RAM navigation: `ram.asm` simply includes these four files (`ram.asm:1-7`). `ram\vram.asm` names VRAM tile/BG-map regions, `ram\wram.asm` is the large gameplay WRAM map, `ram\sram.asm` defines persistent SRAM/save storage, and `ram\hram.asm` defines HRAM scratch/register-shadow variables (`ram\vram.asm:1-18`, `ram\wram.asm:1-120`, `ram\sram.asm:1-160`, `ram\hram.asm:1-120`).

| Path | ~Lines | Description |
|---|---:|---|
| `ram\vram.asm` | 13 | VRAM layout: tile blocks and BG maps. |
| `ram\wram.asm` | 2258 | WRAM layout: audio state, gameplay state, menus, maps, battle, and buffers. |
| `ram\sram.asm` | 124 | SRAM layout: save blocks, backups, mail, active box, Hall of Fame, and PC boxes. |
| `ram\hram.asm` | 136 | HRAM layout: register shadows, joypad state, math scratch, and other fast temp variables. |

# 1. Battle system

Battle flow starts in `DoBattle` inside `engine\battle\core.asm`, which initializes battle state, chooses combatants, and runs the main loop (`engine\battle\core.asm:1-80`). Move effects are script-driven: `engine\battle\effect_commands.asm` reads a move's effect script from `MoveEffectsPointers`, buffers it, and dispatches command opcodes through `BattleCommandPointers` (`engine\battle\effect_commands.asm:1-120`; `data\battle\effect_command_pointers.asm:1-40`). Trainer AI is split into move choice, scoring layers, and item/switch logic (`engine\battle\ai\move.asm:1-120`, `engine\battle\ai\scoring.asm:1-76`, `engine\battle\ai\items.asm:1-40`).

### `engine\\battle\\`

| Path | ~Lines | Description |
|---|---:|---|
| `engine\battle\ai\items.asm` | 729 | Trainer AI item-use and switch-decision logic. |
| `engine\battle\ai\move.asm` | 179 | Top-level trainer move chooser and AI layer application. |
| `engine\battle\ai\redundant.asm` | 171 | Checks whether candidate AI moves are redundant or ineffective. |
| `engine\battle\ai\scoring.asm` | 2610 | AI scoring layers that encourage or discourage specific move choices. |
| `engine\battle\ai\switch.asm` | 558 | Trainer AI switch heuristics and switch-target selection. |
| `engine\battle\anim_hp_bar.asm` | 389 | Battle HP bar drawing and animation helpers. |
| `engine\battle\battle_transition.asm` | 673 | Battle entry transition effects and screen setup. |
| `engine\battle\consume_held_item.asm` | 52 | Held-item consumption/removal helpers. |
| `engine\battle\core.asm` | 7814 | Core battle loop, turn flow, switching, and battle-state control. |
| `engine\battle\effect_commands.asm` | 5603 | Move-effect script reader and battle-command dispatcher. |
| `engine\battle\getgen1trainerclassname.asm` | 20 | Gen 1 trainer class-name compatibility helper. |
| `engine\battle\hidden_power.asm` | 88 | Hidden Power type/power calculation helpers. |
| `engine\battle\menu.asm` | 80 | Battle, Safari, and Contest command menu layouts. |
| `engine\battle\misc.asm` | 176 | Miscellaneous battle helpers not split elsewhere. |
| `engine\battle\move_effects\attract.asm` | 63 | Specialized move-effect routine for attract. |
| `engine\battle\move_effects\baton_pass.asm` | 154 | Specialized move-effect routine for baton pass. |
| `engine\battle\move_effects\beat_up.asm` | 186 | Specialized move-effect routine for beat up. |
| `engine\battle\move_effects\belly_drum.asm` | 22 | Specialized move-effect routine for belly drum. |
| `engine\battle\move_effects\bide.asm` | 88 | Specialized move-effect routine for bide. |
| `engine\battle\move_effects\conversion.asm` | 92 | Specialized move-effect routine for conversion. |
| `engine\battle\move_effects\conversion2.asm` | 59 | Specialized move-effect routine for conversion2. |
| `engine\battle\move_effects\counter.asm` | 48 | Specialized move-effect routine for counter. |
| `engine\battle\move_effects\curse.asm` | 72 | Specialized move-effect routine for curse. |
| `engine\battle\move_effects\destiny_bond.asm` | 7 | Specialized move-effect routine for destiny bond. |
| `engine\battle\move_effects\disable.asm` | 64 | Specialized move-effect routine for disable. |
| `engine\battle\move_effects\encore.asm` | 110 | Specialized move-effect routine for encore. |
| `engine\battle\move_effects\endure.asm` | 10 | Specialized move-effect routine for endure. |
| `engine\battle\move_effects\false_swipe.asm` | 40 | Specialized move-effect routine for false swipe. |
| `engine\battle\move_effects\focus_energy.asm` | 12 | Specialized move-effect routine for focus energy. |
| `engine\battle\move_effects\foresight.asm` | 16 | Specialized move-effect routine for foresight. |
| `engine\battle\move_effects\frustration.asm` | 26 | Specialized move-effect routine for frustration. |
| `engine\battle\move_effects\fury_cutter.asm` | 43 | Specialized move-effect routine for fury cutter. |
| `engine\battle\move_effects\future_sight.asm` | 73 | Specialized move-effect routine for future sight. |
| `engine\battle\move_effects\heal_bell.asm` | 30 | Specialized move-effect routine for heal bell. |
| `engine\battle\move_effects\hidden_power.asm` | 6 | Specialized move-effect routine for hidden power. |
| `engine\battle\move_effects\leech_seed.asm` | 34 | Specialized move-effect routine for leech seed. |
| `engine\battle\move_effects\lock_on.asm` | 15 | Specialized move-effect routine for lock on. |
| `engine\battle\move_effects\magnitude.asm` | 25 | Specialized move-effect routine for magnitude. |
| `engine\battle\move_effects\metronome.asm` | 33 | Specialized move-effect routine for metronome. |
| `engine\battle\move_effects\mimic.asm` | 47 | Specialized move-effect routine for mimic. |
| `engine\battle\move_effects\mirror_coat.asm` | 48 | Specialized move-effect routine for mirror coat. |
| `engine\battle\move_effects\mirror_move.asm` | 39 | Specialized move-effect routine for mirror move. |
| `engine\battle\move_effects\mist.asm` | 12 | Specialized move-effect routine for mist. |
| `engine\battle\move_effects\nightmare.asm` | 25 | Specialized move-effect routine for nightmare. |
| `engine\battle\move_effects\pain_split.asm` | 85 | Specialized move-effect routine for pain split. |
| `engine\battle\move_effects\pay_day.asm` | 22 | Specialized move-effect routine for pay day. |
| `engine\battle\move_effects\perish_song.asm` | 29 | Specialized move-effect routine for perish song. |
| `engine\battle\move_effects\present.asm` | 67 | Specialized move-effect routine for present. |
| `engine\battle\move_effects\protect.asm` | 58 | Specialized move-effect routine for protect. |
| `engine\battle\move_effects\psych_up.asm` | 43 | Specialized move-effect routine for psych up. |
| `engine\battle\move_effects\pursuit.asm` | 20 | Specialized move-effect routine for pursuit. |
| `engine\battle\move_effects\rage.asm` | 5 | Specialized move-effect routine for rage. |
| `engine\battle\move_effects\rain_dance.asm` | 8 | Specialized move-effect routine for rain dance. |
| `engine\battle\move_effects\rapid_spin.asm` | 32 | Specialized move-effect routine for rapid spin. |
| `engine\battle\move_effects\return.asm` | 25 | Specialized move-effect routine for return. |
| `engine\battle\move_effects\rollout.asm` | 76 | Specialized move-effect routine for rollout. |
| `engine\battle\move_effects\safeguard.asm` | 20 | Specialized move-effect routine for safeguard. |
| `engine\battle\move_effects\sandstorm.asm` | 14 | Specialized move-effect routine for sandstorm. |
| `engine\battle\move_effects\selfdestruct.asm` | 30 | Specialized move-effect routine for selfdestruct. |
| `engine\battle\move_effects\sketch.asm` | 111 | Specialized move-effect routine for sketch. |
| `engine\battle\move_effects\sleep_talk.asm` | 128 | Specialized move-effect routine for sleep talk. |
| `engine\battle\move_effects\snore.asm` | 10 | Specialized move-effect routine for snore. |
| `engine\battle\move_effects\spikes.asm` | 17 | Specialized move-effect routine for spikes. |
| `engine\battle\move_effects\spite.asm` | 83 | Specialized move-effect routine for spite. |
| `engine\battle\move_effects\splash.asm` | 3 | Specialized move-effect routine for splash. |
| `engine\battle\move_effects\substitute.asm` | 79 | Specialized move-effect routine for substitute. |
| `engine\battle\move_effects\sunny_day.asm` | 8 | Specialized move-effect routine for sunny day. |
| `engine\battle\move_effects\teleport.asm` | 88 | Specialized move-effect routine for teleport. |
| `engine\battle\move_effects\thief.asm` | 85 | Specialized move-effect routine for thief. |
| `engine\battle\move_effects\thunder.asm` | 15 | Specialized move-effect routine for thunder. |
| `engine\battle\move_effects\transform.asm` | 150 | Specialized move-effect routine for transform. |
| `engine\battle\move_effects\triple_kick.asm` | 28 | Specialized move-effect routine for triple kick. |
| `engine\battle\read_trainer_attributes.asm` | 58 | Loads trainer-class AI/item attributes. |
| `engine\battle\read_trainer_dvs.asm` | 16 | Loads trainer DVs for battle use. |
| `engine\battle\read_trainer_party.asm` | 326 | Builds trainer parties from trainer data. |
| `engine\battle\returntobattle_useball.asm` | 18 | Returns from submenus to battle and handles thrown balls. |
| `engine\battle\sliding_intro.asm` | 53 | Sliding battle intro / HUD entrance effect. |
| `engine\battle\start_battle.asm` | 117 | Battle setup helpers such as battle music and RAM reset. |
| `engine\battle\trainer_huds.asm` | 236 | Trainer battle HUD and badge/name display helpers. |
| `engine\battle\used_move_text.asm` | 195 | Battle text helpers for announcing used moves and results. |

### `data\\battle\\`

| Path | ~Lines | Description |
|---|---:|---|
| `data\battle\accuracy_multipliers.asm` | 16 | Accuracy/evasion multiplier table. |
| `data\battle\ai\constant_damage_effects.asm` | 9 | AI support list for constant damage effects. |
| `data\battle\ai\encore_moves.asm` | 33 | AI support list for encore moves. |
| `data\battle\ai\rain_dance_moves.asm` | 14 | AI support list for rain dance moves. |
| `data\battle\ai\reckless_moves.asm` | 8 | AI support list for reckless moves. |
| `data\battle\ai\residual_moves.asm` | 15 | AI support list for residual moves. |
| `data\battle\ai\risky_effects.asm` | 6 | AI support list for risky effects. |
| `data\battle\ai\stall_moves.asm` | 36 | AI support list for stall moves. |
| `data\battle\ai\status_only_effects.asm` | 8 | AI support list for status only effects. |
| `data\battle\ai\sunny_day_moves.asm` | 12 | AI support list for sunny day moves. |
| `data\battle\ai\useful_moves.asm` | 23 | AI support list for useful moves. |
| `data\battle\critical_hit_chances.asm` | 8 | Critical-hit chance table. |
| `data\battle\effect_command_pointers.asm` | 181 | Battle-command pointer table used by move-effect scripts. |
| `data\battle\held_consumables.asm` | 24 | Held consumable item table. |
| `data\battle\held_heal_status.asm` | 9 | Held-item status-healing table. |
| `data\battle\held_stat_up.asm` | 9 | Held-item stat-boost table. |
| `data\battle\stat_multipliers.asm` | 18 | Battle stat-stage multiplier table. |
| `data\battle\stat_names.asm` | 12 | Battle stat-name strings/data. |
| `data\battle\weather_modifiers.asm` | 9 | Weather-based damage modifier table. |
| `data\battle\wobble_probabilities.asm` | 27 | Poké Ball wobble probability table. |

# 2. Overworld / map engine

Map entry runs through `OverworldLoop`/`StartMap`/`EnterMap`, then `RunMapSetupScript`; active play stays in `HandleMap`, which updates time, runs player events, steps objects, scrolls the screen, and re-enables scripts when the player stops (`engine\overworld\events.asm:3-22`, `engine\overworld\events.asm:138-239`, `engine\overworld\map_setup.asm:1-16`). Player input is turned into movement commands in `DoPlayerMovement`, NPC movement permission is checked in `CanObjectMoveInDirection`, and script bytecodes are interpreted by `ScriptEvents`/`ScriptCommandTable` (`engine\overworld\player_movement.asm:1-119`, `engine\overworld\npc_movement.asm:1-58`, `engine\overworld\scripting.asm:10-80`). Per-map files use the map-script DSL from `macros\scripts\maps.asm`; `maps\NewBarkTown.asm` shows the standard structure of scene scripts, callbacks, NPC scripts, movement blocks, and text labels, while `.blk` files are the raw block layouts included from `data\maps\blocks.asm` (`macros\scripts\maps.asm:12-80`, `maps\NewBarkTown.asm:6-80`, `data\maps\blocks.asm:1-20`).

### `engine\\overworld\\`

| Path | ~Lines | Description |
|---|---:|---|
| `engine\overworld\cmd_queue.asm` | 226 | Queued overworld command execution helpers. |
| `engine\overworld\decorations.asm` | 1027 | Bedroom decoration placement, menus, and state handling. |
| `engine\overworld\events.asm` | 1111 | Main overworld loop and map/player-event dispatch. |
| `engine\overworld\init_map.asm` | 86 | Initial map bootstrap and state reset helpers. |
| `engine\overworld\landmarks.asm` | 72 | Map-to-landmark lookup helpers. |
| `engine\overworld\load_map_part.asm` | 148 | Loads map tile rows/columns from surrounding metatiles. |
| `engine\overworld\map_object_action.asm` | 239 | Object action/state handlers for overworld sprites. |
| `engine\overworld\map_objects_2.asm` | 67 | Additional map-object visibility/spawn helpers. |
| `engine\overworld\map_objects.asm` | 2751 | Core overworld object stepping, visibility, and deletion logic. |
| `engine\overworld\map_setup.asm` | 179 | Map setup-script dispatcher and map-entry hooks. |
| `engine\overworld\movement.asm` | 630 | Movement command table and movement-step implementations. |
| `engine\overworld\npc_movement.asm` | 503 | NPC movement permission, collision, and range checks. |
| `engine\overworld\overworld.asm` | 435 | Player/NPC sprite selection and overworld sprite-GFX loading. |
| `engine\overworld\player_movement.asm` | 698 | Translates joypad input and player state into movement actions. |
| `engine\overworld\player_object.asm` | 758 | Player object setup and object-struct maintenance. |
| `engine\overworld\player_step.asm` | 241 | Player step-state execution and movement progression. |
| `engine\overworld\scripting.asm` | 1991 | Map script engine and script-command interpreter. |
| `engine\overworld\select_menu.asm` | 151 | SELECT-button field menu / registered-item helpers. |
| `engine\overworld\spawn_points.asm` | 52 | Spawn-point lookup helpers. |
| `engine\overworld\tile_events.asm` | 91 | Warp, grass, cut-tree, and tile-collision event checks. |
| `engine\overworld\time.asm` | 344 | Overworld time-of-day and clock display helpers. |
| `engine\overworld\variables.asm` | 111 | Overworld/map variable getters and setters. |
| `engine\overworld\wildmons.asm` | 882 | Wild encounter loading and Pokédex nest-search helpers. |

### `engine\\events\\`

| Path | ~Lines | Description |
|---|---:|---|
| `engine\events\basement_key.asm` | 29 | Event/field helper centered on basement key. |
| `engine\events\bug_contest\caught_mon.asm` | 34 | Bug-Catching Contest event logic for caught mon. |
| `engine\events\bug_contest\contest_2.asm` | 107 | Bug-Catching Contest event logic for contest 2. |
| `engine\events\bug_contest\contest.asm` | 35 | Bug-Catching Contest event logic for contest. |
| `engine\events\bug_contest\display_stats.asm` | 83 | Bug-Catching Contest event logic for display stats. |
| `engine\events\bug_contest\judging.asm` | 338 | Bug-Catching Contest event logic for judging. |
| `engine\events\card_key.asm` | 33 | Event/field helper centered on card key. |
| `engine\events\catch_tutorial_input.asm` | 36 | Event/field helper centered on catch tutorial input. |
| `engine\events\catch_tutorial.asm` | 73 | Event/field helper centered on catch tutorial. |
| `engine\events\checkforhiddenitems.asm` | 79 | Event/field helper centered on checkforhiddenitems. |
| `engine\events\checktime.asm` | 17 | Event/field helper centered on checktime. |
| `engine\events\daycare.asm` | 642 | Day-Care talk/deposit/withdraw script logic. |
| `engine\events\diploma.asm` | 83 | Event/field helper centered on diploma. |
| `engine\events\elevator.asm` | 197 | Event/field helper centered on elevator. |
| `engine\events\engine_flags.asm` | 68 | Event/field helper centered on engine flags. |
| `engine\events\field_moves.asm` | 408 | Event/field helper centered on field moves. |
| `engine\events\fish.asm` | 101 | Event/field helper centered on fish. |
| `engine\events\fishing_gfx.asm` | 20 | Event/field helper centered on fishing gfx. |
| `engine\events\forced_movement.asm` | 43 | Event/field helper centered on forced movement. |
| `engine\events\fruit_trees.asm` | 100 | Fruit-tree scripts and daily reset helpers. |
| `engine\events\haircut.asm` | 62 | Event/field helper centered on haircut. |
| `engine\events\halloffame.asm` | 561 | Event/field helper centered on halloffame. |
| `engine\events\happiness_egg.asm` | 194 | Event/field helper centered on happiness egg. |
| `engine\events\heal_machine_anim.asm` | 228 | Event/field helper centered on heal machine anim. |
| `engine\events\hidden_item.asm` | 32 | Event/field helper centered on hidden item. |
| `engine\events\itemfinder.asm` | 43 | Event/field helper centered on itemfinder. |
| `engine\events\lucky_number.asm` | 199 | Event/field helper centered on lucky number. |
| `engine\events\magikarp.asm` | 270 | Event/field helper centered on magikarp. |
| `engine\events\magnet_train.asm` | 333 | Event/field helper centered on magnet train. |
| `engine\events\misc_scripts.asm` | 50 | Small shared scripts such as item balls and contest-abort handling. |
| `engine\events\mom_phone.asm` | 210 | Event/field helper centered on mom phone. |
| `engine\events\mom.asm` | 573 | Event/field helper centered on mom. |
| `engine\events\money.asm` | 189 | Money add/subtract/compare helpers. |
| `engine\events\move_deleter.asm` | 135 | Event/field helper centered on move deleter. |
| `engine\events\name_rater.asm` | 194 | Event/field helper centered on name rater. |
| `engine\events\npc_trade.asm` | 406 | Event/field helper centered on npc trade. |
| `engine\events\overworld.asm` | 1528 | Field-move state machine and party-move checks used from the overworld. |
| `engine\events\play_slow_cry.asm` | 28 | Event/field helper centered on play slow cry. |
| `engine\events\poisonstep_pals.asm` | 39 | Event/field helper centered on poisonstep pals. |
| `engine\events\poisonstep.asm` | 135 | Event/field helper centered on poisonstep. |
| `engine\events\pokecenter_pc.asm` | 587 | Event/field helper centered on pokecenter pc. |
| `engine\events\pokepic.asm` | 46 | Event/field helper centered on pokepic. |
| `engine\events\pokerus\apply_pokerus_tick.asm` | 26 | Pokérus-related event helper for apply pokerus tick. |
| `engine\events\pokerus\check_pokerus.asm` | 24 | Pokérus-related event helper for check pokerus. |
| `engine\events\pokerus\pokerus.asm` | 151 | Pokérus-related event helper for pokerus. |
| `engine\events\print_photo.asm` | 41 | Event/field helper centered on print photo. |
| `engine\events\print_unown_2.asm` | 96 | Event/field helper centered on print unown 2. |
| `engine\events\print_unown.asm` | 184 | Event/field helper centered on print unown. |
| `engine\events\prof_oaks_pc.asm` | 161 | Event/field helper centered on prof oaks pc. |
| `engine\events\repel.asm` | 9 | Event/field helper centered on repel. |
| `engine\events\sacred_ash.asm` | 60 | Event/field helper centered on sacred ash. |
| `engine\events\shuckle.asm` | 118 | Event/field helper centered on shuckle. |
| `engine\events\specials.asm` | 395 | Dispatch table for script `special` handlers. |
| `engine\events\squirtbottle.asm` | 36 | Event/field helper centered on squirtbottle. |
| `engine\events\std_collision.asm` | 25 | Event/field helper centered on std collision. |
| `engine\events\std_scripts.asm` | 699 | Shared standard scripts (nurse, bookshelf, PC, signs, etc.). |
| `engine\events\sweet_scent.asm` | 56 | Event/field helper centered on sweet scent. |
| `engine\events\trainer_scripts.asm` | 28 | Shared trainer encounter / talk / post-battle scripts. |
| `engine\events\treemons.asm` | 228 | Event/field helper centered on treemons. |
| `engine\events\whiteout.asm` | 62 | Event/field helper centered on whiteout. |

### `maps\\`

| Path | ~Lines | Description |
|---|---:|---|
| `maps\AzaleaGym.asm` | 305 | Per-map script/event/object definition file. |
| `maps\AzaleaGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\AzaleaMart.asm` | 45 | Per-map script/event/object definition file. |
| `maps\AzaleaPokecenter1F.asm` | 59 | Per-map script/event/object definition file. |
| `maps\AzaleaTown.asm` | 351 | Per-map script/event/object definition file. |
| `maps\AzaleaTown.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\BillsFamilysHouse.asm` | 198 | Per-map script/event/object definition file. |
| `maps\BillsHouse.asm` | 306 | Per-map script/event/object definition file. |
| `maps\BillsOlderSistersHouse.asm` | 30 | Per-map script/event/object definition file. |
| `maps\BlackthornCity.asm` | 270 | Per-map script/event/object definition file. |
| `maps\BlackthornCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\BlackthornDragonSpeechHouse.asm` | 38 | Per-map script/event/object definition file. |
| `maps\BlackthornEmysHouse.asm` | 25 | Per-map script/event/object definition file. |
| `maps\BlackthornGym1F.asm` | 338 | Per-map script/event/object definition file. |
| `maps\BlackthornGym1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\BlackthornGym2F.asm` | 123 | Per-map script/event/object definition file. |
| `maps\BlackthornGym2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\BlackthornMart.asm` | 45 | Per-map script/event/object definition file. |
| `maps\BlackthornPokecenter1F.asm` | 48 | Per-map script/event/object definition file. |
| `maps\BluesHouse.asm` | 126 | Per-map script/event/object definition file. |
| `maps\BrunosRoom.asm` | 113 | Per-map script/event/object definition file. |
| `maps\BrunosRoom.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\BurnedTower1F.asm` | 256 | Per-map script/event/object definition file. |
| `maps\BurnedTower1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\BurnedTowerB1F.asm` | 138 | Per-map script/event/object definition file. |
| `maps\BurnedTowerB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeladonCafe.asm` | 187 | Per-map script/event/object definition file. |
| `maps\CeladonCafe.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeladonCity.asm` | 192 | Per-map script/event/object definition file. |
| `maps\CeladonCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeladonDeptStore1F.asm` | 66 | Per-map script/event/object definition file. |
| `maps\CeladonDeptStore2F.asm` | 66 | Per-map script/event/object definition file. |
| `maps\CeladonDeptStore3F.asm` | 88 | Per-map script/event/object definition file. |
| `maps\CeladonDeptStore4F.asm` | 51 | Per-map script/event/object definition file. |
| `maps\CeladonDeptStore5F.asm` | 70 | Per-map script/event/object definition file. |
| `maps\CeladonDeptStore6F.asm` | 132 | Per-map script/event/object definition file. |
| `maps\CeladonDeptStoreElevator.asm` | 32 | Per-map script/event/object definition file. |
| `maps\CeladonGameCorner.asm` | 276 | Per-map script/event/object definition file. |
| `maps\CeladonGameCorner.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeladonGameCornerPrizeRoom.asm` | 245 | Per-map script/event/object definition file. |
| `maps\CeladonGameCornerPrizeRoom.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeladonGym.asm` | 234 | Per-map script/event/object definition file. |
| `maps\CeladonGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeladonMansion1F.asm` | 76 | Per-map script/event/object definition file. |
| `maps\CeladonMansion1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeladonMansion2F.asm` | 46 | Per-map script/event/object definition file. |
| `maps\CeladonMansion2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeladonMansion3F.asm` | 167 | Per-map script/event/object definition file. |
| `maps\CeladonMansion3F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeladonMansionRoof.asm` | 18 | Per-map script/event/object definition file. |
| `maps\CeladonMansionRoof.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeladonMansionRoofHouse.asm` | 95 | Per-map script/event/object definition file. |
| `maps\CeladonPokecenter1F.asm` | 44 | Per-map script/event/object definition file. |
| `maps\CeladonPokecenter2FBeta.asm` | 10 | Per-map script/event/object definition file. |
| `maps\CeruleanCity.asm` | 247 | Per-map script/event/object definition file. |
| `maps\CeruleanCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeruleanGym.asm` | 318 | Per-map script/event/object definition file. |
| `maps\CeruleanGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CeruleanGymBadgeSpeechHouse.asm` | 20 | Per-map script/event/object definition file. |
| `maps\CeruleanMart.asm` | 44 | Per-map script/event/object definition file. |
| `maps\CeruleanPokecenter1F.asm` | 41 | Per-map script/event/object definition file. |
| `maps\CeruleanPokecenter2FBeta.asm` | 10 | Per-map script/event/object definition file. |
| `maps\CeruleanPoliceStation.asm` | 44 | Per-map script/event/object definition file. |
| `maps\CeruleanTradeSpeechHouse.asm` | 54 | Per-map script/event/object definition file. |
| `maps\CharcoalKiln.asm` | 133 | Per-map script/event/object definition file. |
| `maps\CherrygroveCity.asm` | 481 | Per-map script/event/object definition file. |
| `maps\CherrygroveCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CherrygroveEvolutionSpeechHouse.asm` | 43 | Per-map script/event/object definition file. |
| `maps\CherrygroveGymSpeechHouse.asm` | 43 | Per-map script/event/object definition file. |
| `maps\CherrygroveMart.asm` | 67 | Per-map script/event/object definition file. |
| `maps\CherrygrovePokecenter1F.asm` | 66 | Per-map script/event/object definition file. |
| `maps\CianwoodCity.asm` | 196 | Per-map script/event/object definition file. |
| `maps\CianwoodCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CianwoodGym.asm` | 267 | Per-map script/event/object definition file. |
| `maps\CianwoodGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CianwoodLugiaSpeechHouse.asm` | 54 | Per-map script/event/object definition file. |
| `maps\CianwoodPharmacy.asm` | 67 | Per-map script/event/object definition file. |
| `maps\CianwoodPhotoStudio.asm` | 47 | Per-map script/event/object definition file. |
| `maps\CianwoodPokecenter1F.asm` | 69 | Per-map script/event/object definition file. |
| `maps\CinnabarIsland.asm` | 102 | Per-map script/event/object definition file. |
| `maps\CinnabarIsland.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CinnabarPokecenter1F.asm` | 37 | Per-map script/event/object definition file. |
| `maps\CinnabarPokecenter2FBeta.asm` | 10 | Per-map script/event/object definition file. |
| `maps\Colosseum.asm` | 54 | Per-map script/event/object definition file. |
| `maps\Colosseum.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CopycatsHouse1F.asm` | 68 | Per-map script/event/object definition file. |
| `maps\CopycatsHouse1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\CopycatsHouse2F.asm` | 216 | Per-map script/event/object definition file. |
| `maps\CopycatsHouse2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DanceTheater.asm` | 276 | Per-map script/event/object definition file. |
| `maps\DanceTheater.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DarkCaveBlackthornEntrance.asm` | 59 | Per-map script/event/object definition file. |
| `maps\DarkCaveBlackthornEntrance.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DarkCaveVioletEntrance.asm` | 38 | Per-map script/event/object definition file. |
| `maps\DarkCaveVioletEntrance.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DayCare.asm` | 58 | Per-map script/event/object definition file. |
| `maps\DayCare.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DayOfWeekSiblingsHouse.asm` | 61 | Per-map script/event/object definition file. |
| `maps\DeptStore1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DeptStore2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DeptStore3F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DeptStore4F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DeptStore5F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DeptStore6F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DeptStoreElevator.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DiglettsCave.asm` | 29 | Per-map script/event/object definition file. |
| `maps\DiglettsCave.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DragonsDen1F.asm` | 13 | Per-map script/event/object definition file. |
| `maps\DragonsDen1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\DragonsDenB1F.asm` | 227 | Per-map script/event/object definition file. |
| `maps\DragonsDenB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\EarlsPokemonAcademy.asm` | 342 | Per-map script/event/object definition file. |
| `maps\EarlsPokemonAcademy.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\EastWestGate.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\EcruteakCity.asm` | 205 | Per-map script/event/object definition file. |
| `maps\EcruteakCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\EcruteakGym.asm` | 310 | Per-map script/event/object definition file. |
| `maps\EcruteakGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\EcruteakItemfinderHouse.asm` | 135 | Per-map script/event/object definition file. |
| `maps\EcruteakLugiaSpeechHouse.asm` | 42 | Per-map script/event/object definition file. |
| `maps\EcruteakMart.asm` | 44 | Per-map script/event/object definition file. |
| `maps\EcruteakPokecenter1F.asm` | 160 | Per-map script/event/object definition file. |
| `maps\EcruteakTinTowerBackEntrance.asm` | 12 | Per-map script/event/object definition file. |
| `maps\EcruteakTinTowerBackEntrance.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\EcruteakTinTowerEntrance.asm` | 131 | Per-map script/event/object definition file. |
| `maps\EcruteakTinTowerEntrance.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\ElmsHouse.asm` | 72 | Per-map script/event/object definition file. |
| `maps\ElmsHouse.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\ElmsLab.asm` | 1040 | Per-map script/event/object definition file. |
| `maps\ElmsLab.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\FastShip1F.asm` | 261 | Per-map script/event/object definition file. |
| `maps\FastShip1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\FastShipB1F.asm` | 385 | Per-map script/event/object definition file. |
| `maps\FastShipB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\FastShipCabins_NNW_NNE_NE.asm` | 236 | Per-map script/event/object definition file. |
| `maps\FastShipCabins_NNW_NNE_NE.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\FastShipCabins_SE_SSE_CaptainsCabin.asm` | 393 | Per-map script/event/object definition file. |
| `maps\FastShipCabins_SE_SSE_CaptainsCabin.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\FastShipCabins_SW_SSW_NW.asm` | 185 | Per-map script/event/object definition file. |
| `maps\FastShipCabins_SW_SSW_NW.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\FightingDojo.asm` | 42 | Per-map script/event/object definition file. |
| `maps\FightingDojo.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\FuchsiaCity.asm` | 120 | Per-map script/event/object definition file. |
| `maps\FuchsiaCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\FuchsiaGym.asm` | 340 | Per-map script/event/object definition file. |
| `maps\FuchsiaGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\FuchsiaMart.asm` | 39 | Per-map script/event/object definition file. |
| `maps\FuchsiaPokecenter1F.asm` | 86 | Per-map script/event/object definition file. |
| `maps\FuchsiaPokecenter2FBeta.asm` | 10 | Per-map script/event/object definition file. |
| `maps\GiftShop.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\GoldenrodBikeShop.asm` | 97 | Per-map script/event/object definition file. |
| `maps\GoldenrodBikeShop.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\GoldenrodCity.asm` | 300 | Per-map script/event/object definition file. |
| `maps\GoldenrodCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\GoldenrodDeptStore1F.asm` | 70 | Per-map script/event/object definition file. |
| `maps\GoldenrodDeptStore2F.asm` | 91 | Per-map script/event/object definition file. |
| `maps\GoldenrodDeptStore3F.asm` | 56 | Per-map script/event/object definition file. |
| `maps\GoldenrodDeptStore4F.asm` | 73 | Per-map script/event/object definition file. |
| `maps\GoldenrodDeptStore5F.asm` | 193 | Per-map script/event/object definition file. |
| `maps\GoldenrodDeptStore6F.asm` | 138 | Per-map script/event/object definition file. |
| `maps\GoldenrodDeptStoreB1F.asm` | 100 | Per-map script/event/object definition file. |
| `maps\GoldenrodDeptStoreB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\GoldenrodDeptStoreElevator.asm` | 56 | Per-map script/event/object definition file. |
| `maps\GoldenrodFlowerShop.asm` | 91 | Per-map script/event/object definition file. |
| `maps\GoldenrodFlowerShop.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\GoldenrodGameCorner.asm` | 464 | Per-map script/event/object definition file. |
| `maps\GoldenrodGameCorner.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\GoldenrodGym.asm` | 324 | Per-map script/event/object definition file. |
| `maps\GoldenrodGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\GoldenrodHappinessRater.asm` | 123 | Per-map script/event/object definition file. |
| `maps\GoldenrodMagnetTrainStation.asm` | 142 | Per-map script/event/object definition file. |
| `maps\GoldenrodMagnetTrainStation.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\GoldenrodNameRater.asm` | 29 | Per-map script/event/object definition file. |
| `maps\GoldenrodPokecenter1F.asm` | 67 | Per-map script/event/object definition file. |
| `maps\GoldenrodPPSpeechHouse.asm` | 49 | Per-map script/event/object definition file. |
| `maps\GoldenrodUnderground.asm` | 567 | Per-map script/event/object definition file. |
| `maps\GoldenrodUnderground.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\GoldenrodUndergroundSwitchRoomEntrances.asm` | 821 | Per-map script/event/object definition file. |
| `maps\GoldenrodUndergroundSwitchRoomEntrances.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\GoldenrodUndergroundWarehouse.asm` | 179 | Per-map script/event/object definition file. |
| `maps\GoldenrodUndergroundWarehouse.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\GuideGentsHouse.asm` | 30 | Per-map script/event/object definition file. |
| `maps\HallOfFame.asm` | 97 | Per-map script/event/object definition file. |
| `maps\HallOfFame.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\House1.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\House2.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\IcePath1F.asm` | 22 | Per-map script/event/object definition file. |
| `maps\IcePath1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\IcePathB1F.asm` | 79 | Per-map script/event/object definition file. |
| `maps\IcePathB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\IcePathB2FBlackthornSide.asm` | 19 | Per-map script/event/object definition file. |
| `maps\IcePathB2FBlackthornSide.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\IcePathB2FMahoganySide.asm` | 41 | Per-map script/event/object definition file. |
| `maps\IcePathB2FMahoganySide.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\IcePathB3F.asm` | 20 | Per-map script/event/object definition file. |
| `maps\IcePathB3F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\IlexForest.asm` | 593 | Per-map script/event/object definition file. |
| `maps\IlexForest.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\IlexForestAzaleaGate.asm` | 34 | Per-map script/event/object definition file. |
| `maps\IndigoPlateauPokecenter1F.asm` | 273 | Per-map script/event/object definition file. |
| `maps\IndigoPlateauPokecenter1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\KarensRoom.asm` | 118 | Per-map script/event/object definition file. |
| `maps\KarensRoom.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\KogasRoom.asm` | 117 | Per-map script/event/object definition file. |
| `maps\KogasRoom.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\KurtsHouse.asm` | 414 | Per-map script/event/object definition file. |
| `maps\KurtsHouse.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\LakeOfRage.asm` | 423 | Per-map script/event/object definition file. |
| `maps\LakeOfRage.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\LakeOfRageHiddenPowerHouse.asm` | 64 | Per-map script/event/object definition file. |
| `maps\LakeOfRageMagikarpHouse.asm` | 175 | Per-map script/event/object definition file. |
| `maps\LancesRoom.asm` | 298 | Per-map script/event/object definition file. |
| `maps\LancesRoom.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\LavenderMart.asm` | 45 | Per-map script/event/object definition file. |
| `maps\LavenderNameRater.asm` | 26 | Per-map script/event/object definition file. |
| `maps\LavenderPokecenter1F.asm` | 72 | Per-map script/event/object definition file. |
| `maps\LavenderPokecenter2FBeta.asm` | 10 | Per-map script/event/object definition file. |
| `maps\LavenderSpeechHouse.asm` | 30 | Per-map script/event/object definition file. |
| `maps\LavenderTown.asm` | 100 | Per-map script/event/object definition file. |
| `maps\LavenderTown.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\LavRadioTower1F.asm` | 180 | Per-map script/event/object definition file. |
| `maps\LavRadioTower1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\MahoganyGym.asm` | 313 | Per-map script/event/object definition file. |
| `maps\MahoganyGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\MahoganyMart1F.asm` | 198 | Per-map script/event/object definition file. |
| `maps\MahoganyPokecenter1F.asm` | 53 | Per-map script/event/object definition file. |
| `maps\MahoganyRedGyaradosSpeechHouse.asm` | 56 | Per-map script/event/object definition file. |
| `maps\MahoganyTown.asm` | 217 | Per-map script/event/object definition file. |
| `maps\MahoganyTown.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\ManiasHouse.asm` | 170 | Per-map script/event/object definition file. |
| `maps\Mart.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\MountMoon.asm` | 144 | Per-map script/event/object definition file. |
| `maps\MountMoon.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\MountMoonGiftShop.asm` | 33 | Per-map script/event/object definition file. |
| `maps\MountMoonSquare.asm` | 126 | Per-map script/event/object definition file. |
| `maps\MountMoonSquare.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\MountMortar1FInside.asm` | 35 | Per-map script/event/object definition file. |
| `maps\MountMortar1FInside.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\MountMortar1FOutside.asm` | 30 | Per-map script/event/object definition file. |
| `maps\MountMortar1FOutside.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\MountMortar2FInside.asm` | 39 | Per-map script/event/object definition file. |
| `maps\MountMortar2FInside.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\MountMortarB1F.asm` | 109 | Per-map script/event/object definition file. |
| `maps\MountMortarB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\MoveDeletersHouse.asm` | 25 | Per-map script/event/object definition file. |
| `maps\MrFujisHouse.asm` | 76 | Per-map script/event/object definition file. |
| `maps\MrFujisHouse.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\MrPokemonsHouse.asm` | 313 | Per-map script/event/object definition file. |
| `maps\MrPokemonsHouse.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\MrPsychicsHouse.asm` | 47 | Per-map script/event/object definition file. |
| `maps\NationalPark.asm` | 408 | Per-map script/event/object definition file. |
| `maps\NationalPark.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\NationalParkBugContest.asm` | 204 | Per-map script/event/object definition file. |
| `maps\NewBarkTown.asm` | 263 | Per-map script/event/object definition file. |
| `maps\NewBarkTown.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\NorthSouthGate.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OaksLab.asm` | 219 | Per-map script/event/object definition file. |
| `maps\OaksLab.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OlivineCafe.asm` | 66 | Per-map script/event/object definition file. |
| `maps\OlivineCafe.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OlivineCity.asm` | 250 | Per-map script/event/object definition file. |
| `maps\OlivineCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OlivineGoodRodHouse.asm` | 74 | Per-map script/event/object definition file. |
| `maps\OlivineGym.asm` | 172 | Per-map script/event/object definition file. |
| `maps\OlivineGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OlivineHouseBeta.asm` | 41 | Per-map script/event/object definition file. |
| `maps\OlivineLighthouse1F.asm` | 39 | Per-map script/event/object definition file. |
| `maps\OlivineLighthouse1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OlivineLighthouse2F.asm` | 131 | Per-map script/event/object definition file. |
| `maps\OlivineLighthouse2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OlivineLighthouse3F.asm` | 107 | Per-map script/event/object definition file. |
| `maps\OlivineLighthouse3F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OlivineLighthouse4F.asm` | 80 | Per-map script/event/object definition file. |
| `maps\OlivineLighthouse4F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OlivineLighthouse5F.asm` | 88 | Per-map script/event/object definition file. |
| `maps\OlivineLighthouse5F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OlivineLighthouse6F.asm` | 231 | Per-map script/event/object definition file. |
| `maps\OlivineLighthouse6F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OlivineMart.asm` | 44 | Per-map script/event/object definition file. |
| `maps\OlivinePokecenter1F.asm` | 45 | Per-map script/event/object definition file. |
| `maps\OlivinePort.asm` | 346 | Per-map script/event/object definition file. |
| `maps\OlivinePort.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\OlivinePortPassage.asm` | 25 | Per-map script/event/object definition file. |
| `maps\OlivinePunishmentSpeechHouse.asm` | 40 | Per-map script/event/object definition file. |
| `maps\OlivineTimsHouse.asm` | 25 | Per-map script/event/object definition file. |
| `maps\PalletTown.asm` | 65 | Per-map script/event/object definition file. |
| `maps\PalletTown.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\PewterCity.asm` | 150 | Per-map script/event/object definition file. |
| `maps\PewterCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\PewterGym.asm` | 175 | Per-map script/event/object definition file. |
| `maps\PewterGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\PewterMart.asm` | 44 | Per-map script/event/object definition file. |
| `maps\PewterNidoranSpeechHouse.asm` | 31 | Per-map script/event/object definition file. |
| `maps\PewterPokecenter1F.asm` | 65 | Per-map script/event/object definition file. |
| `maps\PewterPokecenter2FBeta.asm` | 10 | Per-map script/event/object definition file. |
| `maps\PewterSnoozeSpeechHouse.asm` | 25 | Per-map script/event/object definition file. |
| `maps\PlayersHouse1F.asm` | 246 | Per-map script/event/object definition file. |
| `maps\PlayersHouse1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\PlayersHouse2F.asm` | 105 | Per-map script/event/object definition file. |
| `maps\PlayersHouse2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\PlayersNeighborsHouse.asm` | 78 | Per-map script/event/object definition file. |
| `maps\Pokecenter1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Pokecenter2F.asm` | 503 | Per-map script/event/object definition file. |
| `maps\Pokecenter2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\PokemonFanClub.asm` | 254 | Per-map script/event/object definition file. |
| `maps\PokemonFanClub.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\PortPassage.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\PowerPlant.asm` | 329 | Per-map script/event/object definition file. |
| `maps\PowerPlant.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RadioTower1F.asm` | 401 | Per-map script/event/object definition file. |
| `maps\RadioTower1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RadioTower2F.asm` | 221 | Per-map script/event/object definition file. |
| `maps\RadioTower2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RadioTower3F.asm` | 282 | Per-map script/event/object definition file. |
| `maps\RadioTower3F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RadioTower4F.asm` | 213 | Per-map script/event/object definition file. |
| `maps\RadioTower4F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RadioTower5F.asm` | 375 | Per-map script/event/object definition file. |
| `maps\RadioTower5F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RedsHouse1F.asm` | 69 | Per-map script/event/object definition file. |
| `maps\RedsHouse1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RedsHouse2F.asm` | 27 | Per-map script/event/object definition file. |
| `maps\RedsHouse2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RockTunnel1F.asm` | 30 | Per-map script/event/object definition file. |
| `maps\RockTunnel1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RockTunnelB1F.asm` | 29 | Per-map script/event/object definition file. |
| `maps\RockTunnelB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route1.asm` | 71 | Per-map script/event/object definition file. |
| `maps\Route1.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route10North.asm` | 20 | Per-map script/event/object definition file. |
| `maps\Route10North.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route10Pokecenter1F.asm` | 73 | Per-map script/event/object definition file. |
| `maps\Route10Pokecenter2FBeta.asm` | 10 | Per-map script/event/object definition file. |
| `maps\Route10South.asm` | 67 | Per-map script/event/object definition file. |
| `maps\Route10South.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route11.asm` | 125 | Per-map script/event/object definition file. |
| `maps\Route11.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route12.asm` | 144 | Per-map script/event/object definition file. |
| `maps\Route12.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route12SuperRodHouse.asm` | 71 | Per-map script/event/object definition file. |
| `maps\Route13.asm` | 165 | Per-map script/event/object definition file. |
| `maps\Route13.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route14.asm` | 96 | Per-map script/event/object definition file. |
| `maps\Route14.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route15.asm` | 176 | Per-map script/event/object definition file. |
| `maps\Route15.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route15FuchsiaGate.asm` | 24 | Per-map script/event/object definition file. |
| `maps\Route16.asm` | 33 | Per-map script/event/object definition file. |
| `maps\Route16.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route16FuchsiaSpeechHouse.asm` | 26 | Per-map script/event/object definition file. |
| `maps\Route16Gate.asm` | 58 | Per-map script/event/object definition file. |
| `maps\Route17.asm` | 119 | Per-map script/event/object definition file. |
| `maps\Route17.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route17Route18Gate.asm` | 52 | Per-map script/event/object definition file. |
| `maps\Route18.asm` | 70 | Per-map script/event/object definition file. |
| `maps\Route18.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route19.asm` | 202 | Per-map script/event/object definition file. |
| `maps\Route19.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route19FuchsiaGate.asm` | 46 | Per-map script/event/object definition file. |
| `maps\Route2.asm` | 136 | Per-map script/event/object definition file. |
| `maps\Route2.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route20.asm` | 98 | Per-map script/event/object definition file. |
| `maps\Route20.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route21.asm` | 81 | Per-map script/event/object definition file. |
| `maps\Route21.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route22.asm` | 18 | Per-map script/event/object definition file. |
| `maps\Route22.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route23.asm` | 26 | Per-map script/event/object definition file. |
| `maps\Route23.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route24.asm` | 98 | Per-map script/event/object definition file. |
| `maps\Route24.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route25.asm` | 373 | Per-map script/event/object definition file. |
| `maps\Route25.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route26.asm` | 332 | Per-map script/event/object definition file. |
| `maps\Route26.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route26HealHouse.asm` | 48 | Per-map script/event/object definition file. |
| `maps\Route27.asm` | 365 | Per-map script/event/object definition file. |
| `maps\Route27.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route27SandstormHouse.asm` | 83 | Per-map script/event/object definition file. |
| `maps\Route28.asm` | 20 | Per-map script/event/object definition file. |
| `maps\Route28.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route28SteelWingHouse.asm` | 68 | Per-map script/event/object definition file. |
| `maps\Route29.asm` | 360 | Per-map script/event/object definition file. |
| `maps\Route29.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route29Route46Gate.asm` | 38 | Per-map script/event/object definition file. |
| `maps\Route2Gate.asm` | 28 | Per-map script/event/object definition file. |
| `maps\Route2NuggetHouse.asm` | 49 | Per-map script/event/object definition file. |
| `maps\Route3.asm` | 115 | Per-map script/event/object definition file. |
| `maps\Route3.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route30.asm` | 282 | Per-map script/event/object definition file. |
| `maps\Route30.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route30BerryHouse.asm` | 48 | Per-map script/event/object definition file. |
| `maps\Route31.asm` | 289 | Per-map script/event/object definition file. |
| `maps\Route31.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route31VioletGate.asm` | 32 | Per-map script/event/object definition file. |
| `maps\Route32.asm` | 702 | Per-map script/event/object definition file. |
| `maps\Route32.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route32Pokecenter1F.asm` | 86 | Per-map script/event/object definition file. |
| `maps\Route32RuinsOfAlphGate.asm` | 46 | Per-map script/event/object definition file. |
| `maps\Route33.asm` | 108 | Per-map script/event/object definition file. |
| `maps\Route33.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route34.asm` | 552 | Per-map script/event/object definition file. |
| `maps\Route34.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route34IlexForestGate.asm` | 74 | Per-map script/event/object definition file. |
| `maps\Route35.asm` | 385 | Per-map script/event/object definition file. |
| `maps\Route35.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route35GoldenrodGate.asm` | 157 | Per-map script/event/object definition file. |
| `maps\Route35NationalParkGate.asm` | 365 | Per-map script/event/object definition file. |
| `maps\Route35NationalParkGate.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route36.asm` | 399 | Per-map script/event/object definition file. |
| `maps\Route36.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route36NationalParkGate.asm` | 734 | Per-map script/event/object definition file. |
| `maps\Route36NationalParkGate.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route36RuinsOfAlphGate.asm` | 37 | Per-map script/event/object definition file. |
| `maps\Route37.asm` | 194 | Per-map script/event/object definition file. |
| `maps\Route37.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route38.asm` | 297 | Per-map script/event/object definition file. |
| `maps\Route38.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route38EcruteakGate.asm` | 27 | Per-map script/event/object definition file. |
| `maps\Route39.asm` | 241 | Per-map script/event/object definition file. |
| `maps\Route39.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route39Barn.asm` | 170 | Per-map script/event/object definition file. |
| `maps\Route39Barn.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route39Farmhouse.asm` | 168 | Per-map script/event/object definition file. |
| `maps\Route4.asm` | 100 | Per-map script/event/object definition file. |
| `maps\Route4.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route40.asm` | 238 | Per-map script/event/object definition file. |
| `maps\Route40.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route41.asm` | 291 | Per-map script/event/object definition file. |
| `maps\Route41.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route42.asm` | 214 | Per-map script/event/object definition file. |
| `maps\Route42.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route42EcruteakGate.asm` | 24 | Per-map script/event/object definition file. |
| `maps\Route43.asm` | 356 | Per-map script/event/object definition file. |
| `maps\Route43.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route43Gate.asm` | 218 | Per-map script/event/object definition file. |
| `maps\Route43MahoganyGate.asm` | 41 | Per-map script/event/object definition file. |
| `maps\Route44.asm` | 345 | Per-map script/event/object definition file. |
| `maps\Route44.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route45.asm` | 342 | Per-map script/event/object definition file. |
| `maps\Route45.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route46.asm` | 171 | Per-map script/event/object definition file. |
| `maps\Route46.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route5.asm` | 40 | Per-map script/event/object definition file. |
| `maps\Route5.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route5CleanseTagHouse.asm` | 59 | Per-map script/event/object definition file. |
| `maps\Route5SaffronGate.asm` | 25 | Per-map script/event/object definition file. |
| `maps\Route5UndergroundPathEntrance.asm` | 23 | Per-map script/event/object definition file. |
| `maps\Route6.asm` | 30 | Per-map script/event/object definition file. |
| `maps\Route6.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route6SaffronGate.asm` | 54 | Per-map script/event/object definition file. |
| `maps\Route6UndergroundPathEntrance.asm` | 12 | Per-map script/event/object definition file. |
| `maps\Route7.asm` | 34 | Per-map script/event/object definition file. |
| `maps\Route7.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route7SaffronGate.asm` | 46 | Per-map script/event/object definition file. |
| `maps\Route8.asm` | 156 | Per-map script/event/object definition file. |
| `maps\Route8.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\Route8SaffronGate.asm` | 24 | Per-map script/event/object definition file. |
| `maps\Route9.asm` | 176 | Per-map script/event/object definition file. |
| `maps\Route9.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RuinsOfAlphAerodactylChamber.asm` | 62 | Per-map script/event/object definition file. |
| `maps\RuinsOfAlphHoOhChamber.asm` | 62 | Per-map script/event/object definition file. |
| `maps\RuinsOfAlphInnerChamber.asm` | 102 | Per-map script/event/object definition file. |
| `maps\RuinsOfAlphInnerChamber.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RuinsOfAlphKabutoChamber.asm` | 82 | Per-map script/event/object definition file. |
| `maps\RuinsOfAlphOmanyteChamber.asm` | 62 | Per-map script/event/object definition file. |
| `maps\RuinsOfAlphOutside.asm` | 191 | Per-map script/event/object definition file. |
| `maps\RuinsOfAlphOutside.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RuinsOfAlphPuzzleChamber.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\RuinsOfAlphResearchCenter.asm` | 273 | Per-map script/event/object definition file. |
| `maps\RuinsOfAlphResearchCenter.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SafariZoneBeta.asm` | 11 | Per-map script/event/object definition file. |
| `maps\SafariZoneBeta.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SafariZoneFuchsiaGateBeta.asm` | 13 | Per-map script/event/object definition file. |
| `maps\SafariZoneMainOffice.asm` | 11 | Per-map script/event/object definition file. |
| `maps\SafariZoneWardensHome.asm` | 71 | Per-map script/event/object definition file. |
| `maps\SaffronCity.asm` | 240 | Per-map script/event/object definition file. |
| `maps\SaffronCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SaffronGym.asm` | 277 | Per-map script/event/object definition file. |
| `maps\SaffronGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SaffronMagnetTrainStation.asm` | 185 | Per-map script/event/object definition file. |
| `maps\SaffronMagnetTrainStation.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SaffronMart.asm` | 39 | Per-map script/event/object definition file. |
| `maps\SaffronPokecenter1F.asm` | 73 | Per-map script/event/object definition file. |
| `maps\SaffronPokecenter2FBeta.asm` | 10 | Per-map script/event/object definition file. |
| `maps\SeafoamGym.asm` | 130 | Per-map script/event/object definition file. |
| `maps\SeafoamGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SilphCo1F.asm` | 54 | Per-map script/event/object definition file. |
| `maps\SilphCo1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SilverCaveItemRooms.asm` | 20 | Per-map script/event/object definition file. |
| `maps\SilverCaveItemRooms.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SilverCaveOutside.asm` | 27 | Per-map script/event/object definition file. |
| `maps\SilverCaveOutside.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SilverCavePokecenter1F.asm` | 31 | Per-map script/event/object definition file. |
| `maps\SilverCaveRoom1.asm` | 30 | Per-map script/event/object definition file. |
| `maps\SilverCaveRoom1.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SilverCaveRoom2.asm` | 16 | Per-map script/event/object definition file. |
| `maps\SilverCaveRoom2.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SilverCaveRoom3.asm` | 51 | Per-map script/event/object definition file. |
| `maps\SilverCaveRoom3.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SlowpokeWellB1F.asm` | 275 | Per-map script/event/object definition file. |
| `maps\SlowpokeWellB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SlowpokeWellB2F.asm` | 57 | Per-map script/event/object definition file. |
| `maps\SlowpokeWellB2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SoulHouse.asm` | 64 | Per-map script/event/object definition file. |
| `maps\SoulHouse.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SproutTower1F.asm` | 96 | Per-map script/event/object definition file. |
| `maps\SproutTower1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SproutTower2F.asm` | 78 | Per-map script/event/object definition file. |
| `maps\SproutTower2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\SproutTower3F.asm` | 286 | Per-map script/event/object definition file. |
| `maps\SproutTower3F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TeamRocketBaseB1F.asm` | 699 | Per-map script/event/object definition file. |
| `maps\TeamRocketBaseB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TeamRocketBaseB2F.asm` | 812 | Per-map script/event/object definition file. |
| `maps\TeamRocketBaseB2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TeamRocketBaseB3F.asm` | 487 | Per-map script/event/object definition file. |
| `maps\TeamRocketBaseB3F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TimeCapsule.asm` | 54 | Per-map script/event/object definition file. |
| `maps\TinTower1F.asm` | 39 | Per-map script/event/object definition file. |
| `maps\TinTower1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TinTower2F.asm` | 11 | Per-map script/event/object definition file. |
| `maps\TinTower2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TinTower3F.asm` | 16 | Per-map script/event/object definition file. |
| `maps\TinTower3F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TinTower4F.asm` | 29 | Per-map script/event/object definition file. |
| `maps\TinTower4F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TinTower5F.asm` | 24 | Per-map script/event/object definition file. |
| `maps\TinTower5F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TinTower6F.asm` | 11 | Per-map script/event/object definition file. |
| `maps\TinTower6F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TinTower7F.asm` | 19 | Per-map script/event/object definition file. |
| `maps\TinTower7F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TinTower8F.asm` | 28 | Per-map script/event/object definition file. |
| `maps\TinTower8F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TinTower9F.asm` | 22 | Per-map script/event/object definition file. |
| `maps\TinTower9F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TinTowerRoof.asm` | 52 | Per-map script/event/object definition file. |
| `maps\TinTowerRoof.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TohjoFalls.asm` | 16 | Per-map script/event/object definition file. |
| `maps\TohjoFalls.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TradeCenter.asm` | 54 | Per-map script/event/object definition file. |
| `maps\TradeCenter.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TrainerHouse1F.asm` | 116 | Per-map script/event/object definition file. |
| `maps\TrainerHouse1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\TrainerHouseB1F.asm` | 155 | Per-map script/event/object definition file. |
| `maps\TrainerHouseB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\UndergroundPath.asm` | 17 | Per-map script/event/object definition file. |
| `maps\UndergroundPath.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\UndergroundPathEntrance.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\UnionCave1F.asm` | 174 | Per-map script/event/object definition file. |
| `maps\UnionCave1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\UnionCaveB1F.asm` | 137 | Per-map script/event/object definition file. |
| `maps\UnionCaveB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\UnionCaveB2F.asm` | 126 | Per-map script/event/object definition file. |
| `maps\UnionCaveB2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\unused\BetaAzaleaTown.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaBlackthornCity.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaCapsuleHouse.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaCaveTestMap.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaCeladonMansion1F.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaCeladonMansion2F.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaCherrygroveCity.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaCianwoodCity.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaCinnabarPokemonLabHallway.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaCinnabarPokemonLabRoom1.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaCinnabarPokemonLabRoom2.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaCinnabarPokemonLabRoom3.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaEcruteakCity.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaElevator.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaFastShipInsideCutOut.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaGoldenrodCity.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaHouse.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaLakeOfRage.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaMahoganyTown.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaNewBarkTown.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaOlivineCity.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaPewterMuseumOfScience1F.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaPewterMuseumOfScience2F.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaPlayersHouse2F.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaPokecenter.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaRocketHideout1F.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaRocketHideoutB1F.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaRocketHideoutB2F.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaRocketHideoutB3F.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaRoute23.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaRuinsOfAlphUnsolvedPuzzleRoom.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSilverCaveOutside.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSlowpokeWell1F.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSproutTower1.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSproutTower2.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSproutTower3.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSproutTower5.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSproutTower6.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSproutTower7.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSproutTower8.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSproutTower9.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSproutTowerCutOut1.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSproutTowerCutOut2.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaSproutTowerCutOut3.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaUnionCave.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaUnknownGym.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\unused\BetaVioletCity.blk` | n/a (binary) | Unused/beta binary block layout for this map. |
| `maps\VermilionCity.asm` | 241 | Per-map script/event/object definition file. |
| `maps\VermilionCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\VermilionDiglettsCaveSpeechHouse.asm` | 23 | Per-map script/event/object definition file. |
| `maps\VermilionFishingSpeechHouse.asm` | 43 | Per-map script/event/object definition file. |
| `maps\VermilionGym.asm` | 237 | Per-map script/event/object definition file. |
| `maps\VermilionGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\VermilionMagnetTrainSpeechHouse.asm` | 36 | Per-map script/event/object definition file. |
| `maps\VermilionMart.asm` | 38 | Per-map script/event/object definition file. |
| `maps\VermilionPokecenter1F.asm` | 71 | Per-map script/event/object definition file. |
| `maps\VermilionPokecenter2FBeta.asm` | 10 | Per-map script/event/object definition file. |
| `maps\VermilionPort.asm` | 268 | Per-map script/event/object definition file. |
| `maps\VermilionPort.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\VermilionPortPassage.asm` | 25 | Per-map script/event/object definition file. |
| `maps\VictoryRoad.asm` | 220 | Per-map script/event/object definition file. |
| `maps\VictoryRoad.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\VictoryRoadGate.asm` | 94 | Per-map script/event/object definition file. |
| `maps\VictoryRoadGate.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\VioletCity.asm` | 260 | Per-map script/event/object definition file. |
| `maps\VioletCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\VioletGym.asm` | 237 | Per-map script/event/object definition file. |
| `maps\VioletGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\VioletKylesHouse.asm` | 34 | Per-map script/event/object definition file. |
| `maps\VioletMart.asm` | 47 | Per-map script/event/object definition file. |
| `maps\VioletNicknameSpeechHouse.asm` | 45 | Per-map script/event/object definition file. |
| `maps\VioletPokecenter1F.asm` | 168 | Per-map script/event/object definition file. |
| `maps\ViridianCity.asm` | 185 | Per-map script/event/object definition file. |
| `maps\ViridianCity.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\ViridianGym.asm` | 140 | Per-map script/event/object definition file. |
| `maps\ViridianGym.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\ViridianMart.asm` | 38 | Per-map script/event/object definition file. |
| `maps\ViridianNicknameSpeechHouse.asm` | 59 | Per-map script/event/object definition file. |
| `maps\ViridianPokecenter1F.asm` | 68 | Per-map script/event/object definition file. |
| `maps\ViridianPokecenter2FBeta.asm` | 10 | Per-map script/event/object definition file. |
| `maps\WhirlIslandB1F.asm` | 52 | Per-map script/event/object definition file. |
| `maps\WhirlIslandB1F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\WhirlIslandB2F.asm` | 26 | Per-map script/event/object definition file. |
| `maps\WhirlIslandB2F.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\WhirlIslandCave.asm` | 11 | Per-map script/event/object definition file. |
| `maps\WhirlIslandCave.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\WhirlIslandLugiaChamber.asm` | 52 | Per-map script/event/object definition file. |
| `maps\WhirlIslandLugiaChamber.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\WhirlIslandNE.asm` | 17 | Per-map script/event/object definition file. |
| `maps\WhirlIslandNE.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\WhirlIslandNW.asm` | 13 | Per-map script/event/object definition file. |
| `maps\WhirlIslandNW.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\WhirlIslandSE.asm` | 11 | Per-map script/event/object definition file. |
| `maps\WhirlIslandSE.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\WhirlIslandSW.asm` | 19 | Per-map script/event/object definition file. |
| `maps\WhirlIslandSW.blk` | n/a (binary) | Binary block layout for this map. |
| `maps\WillsRoom.asm` | 115 | Per-map script/event/object definition file. |
| `maps\WillsRoom.blk` | n/a (binary) | Binary block layout for this map. |

# 3. Menu system

The core menu machinery lives in `engine\menus\menu.asm`, which draws boxes, places strings, initializes cursor state, and reads 2D menu input (`engine\menus\menu.asm:1-98`). `engine\menus\main_menu.asm` defines the title menu, `engine\menus\start_menu.asm` defines the in-game START menu, `engine\menus\naming_screen.asm` owns the naming UI, `engine\menus\options_menu.asm` owns settings, and `engine\menus\save.asm` owns the save path (`engine\menus\main_menu.asm:17-45`, `engine\menus\start_menu.asm:13-69`, `engine\menus\naming_screen.asm:7-60`, `engine\menus\options_menu.asm:13-67`, `engine\menus\save.asm:1-100`).

| Path | ~Lines | Description |
|---|---:|---|
| `engine\menus\delete_save.asm` | 31 | Delete-save confirmation flow. |
| `engine\menus\empty_sram.asm` | 18 | SRAM wipe/clear helper. |
| `engine\menus\intro_menu.asm` | 982 | Title-screen intro/menu flow and new-game entry path. |
| `engine\menus\main_menu.asm` | 251 | Main title menu (Continue/New Game/Option/Mystery Gift). |
| `engine\menus\menu_2.asm` | 270 | Secondary/general-purpose menu helpers. |
| `engine\menus\menu.asm` | 621 | Core static/2D menu engine. |
| `engine\menus\naming_screen.asm` | 1224 | Naming-screen UI and name-entry loop. |
| `engine\menus\options_menu.asm` | 477 | Options/settings menu. |
| `engine\menus\save.asm` | 992 | Save flow, checksums, backup save, and box-save routines. |
| `engine\menus\savemenu_copytilemapatonce.asm` | 74 | Save-menu tilemap copy helper. |
| `engine\menus\scrolling_menu.asm` | 474 | Scrolling list-menu engine. |
| `engine\menus\start_menu.asm` | 465 | In-game START menu dispatcher. |
| `engine\menus\trainer_card.asm` | 557 | Multi-page Trainer Card UI. |

# 4. Pokémon data & management

Party/box management is split across Bill's PC, party-menu/stat-screen code, move-learning code, and breeding code (`engine\pokemon\bills_pc_top.asm:1-94`, `engine\pokemon\party_menu.asm:1-35`, `engine\pokemon\stats_screen.asm:1-58`, `engine\pokemon\learn.asm:1-120`, `engine\pokemon\breeding.asm:1-120`). Species data is layered: `data\pokemon\base_stats.asm` includes per-species base data records, `data\pokemon\evos_attacks.asm` stores evolution + level-up data, `data\pokemon\egg_moves.asm` stores egg moves, and `data\pokemon\pic_pointers.asm` / `data\pokemon\palettes.asm` link species to graphics and palettes (`data\pokemon\base_stats.asm:1-24`, `data\pokemon\base_stats\chikorita.asm:1-25`, `data\pokemon\evos_attacks.asm:1-31`, `data\pokemon\egg_moves.asm:1-30`, `data\pokemon\pic_pointers.asm:1-20`, `data\pokemon\palettes.asm:1-20`).

### `engine\\pokemon\\`

| Path | ~Lines | Description |
|---|---:|---|
| `engine\pokemon\bills_pc_top.asm` | 331 | Top-level Bill's PC menu flow. |
| `engine\pokemon\bills_pc.asm` | 2274 | Bill's PC box management, withdraw/deposit, and move flows. |
| `engine\pokemon\breeding.asm` | 857 | Day-Care breeding compatibility and egg-generation helpers. |
| `engine\pokemon\breedmon_level_growth.asm` | 26 | Reads Day-Care level growth for deposited mons. |
| `engine\pokemon\caught_nickname.asm` | 123 | Nickname flow for newly caught Pokémon. |
| `engine\pokemon\correct_nick_errors.asm` | 65 | Sanitizes malformed nicknames before use. |
| `engine\pokemon\evolve.asm` | 561 | Evolution checks and evolution sequence. |
| `engine\pokemon\experience.asm` | 156 | Experience-growth and EXP award helpers. |
| `engine\pokemon\health.asm` | 98 | HP/status maintenance helpers. |
| `engine\pokemon\knows_move.asm` | 22 | Checks whether a mon knows a move. |
| `engine\pokemon\learn.asm` | 214 | Level-up / TM move learning and forget-move flow. |
| `engine\pokemon\mail_2.asm` | 872 | Mail reading/printing UI and mail graphics loading. |
| `engine\pokemon\mail.asm` | 512 | Party/PC mail storage transfer helpers. |
| `engine\pokemon\mon_menu.asm` | 1134 | Per-mon action menu logic. |
| `engine\pokemon\mon_stats.asm` | 419 | HP/stat drawing plus temp-mon stat calculation helpers. |
| `engine\pokemon\mon_submenu.asm` | 262 | Small submenus attached to mon actions. |
| `engine\pokemon\move_mon_wo_mail.asm` | 129 | Box/party move operations for mons without mail. |
| `engine\pokemon\move_mon.asm` | 1652 | General party/box move, deposit, and withdraw helpers. |
| `engine\pokemon\party_menu.asm` | 705 | Party menu layout, selection, and redraw flow. |
| `engine\pokemon\print_move_description.asm` | 16 | Move-description print helper. |
| `engine\pokemon\search_party.asm` | 122 | Party search helpers. |
| `engine\pokemon\stats_screen.asm` | 798 | Multi-page stats screen UI. |
| `engine\pokemon\switchpartymons.asm` | 141 | Party reordering helpers. |
| `engine\pokemon\tempmon.asm` | 111 | Copy-to-temp-mon and temp-mon stat calculation helpers. |
| `engine\pokemon\types.asm` | 76 | Type-name and type-data helpers. |

### `data\\pokemon\\`

| Path | ~Lines | Description |
|---|---:|---|
| `data\pokemon\base_stats.asm` | 273 | Includes every per-species base-stat record and defines the `tmhm` helper macro used there. |
| `data\pokemon\base_stats\abra.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\aerodactyl.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\aipom.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\alakazam.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\ampharos.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\arbok.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\arcanine.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\ariados.asm` | 18 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\articuno.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\azumarill.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\bayleef.asm` | 18 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\beedrill.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\bellossom.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\bellsprout.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\blastoise.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\blissey.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\bulbasaur.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\butterfree.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\caterpie.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\celebi.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\chansey.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\charizard.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\charmander.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\charmeleon.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\chikorita.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\chinchou.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\clefable.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\clefairy.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\cleffa.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\cloyster.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\corsola.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\crobat.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\croconaw.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\cubone.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\cyndaquil.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\delibird.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\dewgong.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\diglett.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\ditto.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\dodrio.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\doduo.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\donphan.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\dragonair.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\dragonite.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\dratini.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\drowzee.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\dugtrio.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\dunsparce.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\eevee.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\ekans.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\electabuzz.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\electrode.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\elekid.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\entei.asm` | 18 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\espeon.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\exeggcute.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\exeggutor.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\farfetch_d.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\fearow.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\feraligatr.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\flaaffy.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\flareon.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\forretress.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\furret.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\gastly.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\gengar.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\geodude.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\girafarig.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\gligar.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\gloom.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\golbat.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\goldeen.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\golduck.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\golem.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\granbull.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\graveler.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\grimer.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\growlithe.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\gyarados.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\haunter.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\heracross.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\hitmonchan.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\hitmonlee.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\hitmontop.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\ho_oh.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\hoothoot.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\hoppip.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\horsea.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\houndoom.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\houndour.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\hypno.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\igglybuff.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\ivysaur.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\jigglypuff.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\jolteon.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\jumpluff.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\jynx.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\kabuto.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\kabutops.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\kadabra.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\kakuna.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\kangaskhan.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\kingdra.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\kingler.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\koffing.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\krabby.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\lanturn.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\lapras.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\larvitar.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\ledian.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\ledyba.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\lickitung.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\lugia.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\machamp.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\machoke.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\machop.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\magby.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\magcargo.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\magikarp.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\magmar.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\magnemite.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\magneton.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\mankey.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\mantine.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\mareep.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\marill.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\marowak.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\meganium.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\meowth.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\metapod.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\mew.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\mewtwo.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\miltank.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\misdreavus.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\moltres.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\mr__mime.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\muk.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\murkrow.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\natu.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\nidoking.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\nidoqueen.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\nidoran_f.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\nidoran_m.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\nidorina.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\nidorino.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\ninetales.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\noctowl.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\octillery.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\oddish.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\omanyte.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\omastar.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\onix.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\paras.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\parasect.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\persian.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\phanpy.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\pichu.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\pidgeot.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\pidgeotto.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\pidgey.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\pikachu.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\piloswine.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\pineco.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\pinsir.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\politoed.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\poliwag.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\poliwhirl.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\poliwrath.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\ponyta.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\porygon.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\porygon2.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\primeape.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\psyduck.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\pupitar.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\quagsire.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\quilava.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\qwilfish.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\raichu.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\raikou.asm` | 18 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\rapidash.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\raticate.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\rattata.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\remoraid.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\rhydon.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\rhyhorn.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\sandshrew.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\sandslash.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\scizor.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\scyther.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\seadra.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\seaking.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\seel.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\sentret.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\shellder.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\shuckle.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\skarmory.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\skiploom.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\slowbro.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\slowking.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\slowpoke.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\slugma.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\smeargle.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\smoochum.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\sneasel.asm` | 18 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\snorlax.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\snubbull.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\spearow.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\spinarak.asm` | 18 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\squirtle.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\stantler.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\starmie.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\staryu.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\steelix.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\sudowoodo.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\suicune.asm` | 18 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\sunflora.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\sunkern.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\swinub.asm` | 18 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\tangela.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\tauros.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\teddiursa.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\tentacool.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\tentacruel.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\togepi.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\togetic.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\totodile.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\typhlosion.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\tyranitar.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\tyrogue.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\umbreon.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\unown.asm` | 18 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\ursaring.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\vaporeon.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\venomoth.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\venonat.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\venusaur.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\victreebel.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\vileplume.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\voltorb.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\vulpix.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\wartortle.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\weedle.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\weepinbell.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\weezing.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\wigglytuff.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\wobbuffet.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\wooper.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\xatu.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\yanma.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\zapdos.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\base_stats\zubat.asm` | 22 | Per-species base-stat record, types, items, growth/egg groups, and TM/HM learnset. |
| `data\pokemon\cries.asm` | 264 | Species-to-cry/pitch/length table. |
| `data\pokemon\dex_entries_gold.asm` | 255 | Banked include file for all Gold Pokédex entries. |
| `data\pokemon\dex_entries_silver.asm` | 255 | Banked include file for all Silver Pokédex entries. |
| `data\pokemon\dex_entries\gold\abra.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\aerodactyl.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\aipom.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\alakazam.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\ampharos.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\arbok.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\arcanine.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\ariados.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\articuno.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\azumarill.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\bayleef.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\beedrill.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\bellossom.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\bellsprout.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\blastoise.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\blissey.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\bulbasaur.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\butterfree.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\caterpie.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\celebi.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\chansey.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\charizard.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\charmander.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\charmeleon.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\chikorita.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\chinchou.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\clefable.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\clefairy.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\cleffa.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\cloyster.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\corsola.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\crobat.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\croconaw.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\cubone.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\cyndaquil.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\delibird.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\dewgong.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\diglett.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\ditto.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\dodrio.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\doduo.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\donphan.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\dragonair.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\dragonite.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\dratini.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\drowzee.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\dugtrio.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\dunsparce.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\eevee.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\ekans.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\electabuzz.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\electrode.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\elekid.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\entei.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\espeon.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\exeggcute.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\exeggutor.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\farfetch_d.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\fearow.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\feraligatr.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\flaaffy.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\flareon.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\forretress.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\furret.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\gastly.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\gengar.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\geodude.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\girafarig.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\gligar.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\gloom.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\golbat.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\goldeen.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\golduck.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\golem.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\granbull.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\graveler.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\grimer.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\growlithe.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\gyarados.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\haunter.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\heracross.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\hitmonchan.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\hitmonlee.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\hitmontop.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\ho_oh.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\hoothoot.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\hoppip.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\horsea.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\houndoom.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\houndour.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\hypno.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\igglybuff.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\ivysaur.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\jigglypuff.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\jolteon.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\jumpluff.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\jynx.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\kabuto.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\kabutops.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\kadabra.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\kakuna.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\kangaskhan.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\kingdra.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\kingler.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\koffing.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\krabby.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\lanturn.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\lapras.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\larvitar.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\ledian.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\ledyba.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\lickitung.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\lugia.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\machamp.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\machoke.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\machop.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\magby.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\magcargo.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\magikarp.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\magmar.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\magnemite.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\magneton.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\mankey.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\mantine.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\mareep.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\marill.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\marowak.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\meganium.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\meowth.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\metapod.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\mew.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\mewtwo.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\miltank.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\misdreavus.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\moltres.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\mr__mime.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\muk.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\murkrow.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\natu.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\nidoking.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\nidoqueen.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\nidoran_f.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\nidoran_m.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\nidorina.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\nidorino.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\ninetales.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\noctowl.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\octillery.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\oddish.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\omanyte.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\omastar.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\onix.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\paras.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\parasect.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\persian.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\phanpy.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\pichu.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\pidgeot.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\pidgeotto.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\pidgey.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\pikachu.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\piloswine.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\pineco.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\pinsir.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\politoed.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\poliwag.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\poliwhirl.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\poliwrath.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\ponyta.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\porygon.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\porygon2.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\primeape.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\psyduck.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\pupitar.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\quagsire.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\quilava.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\qwilfish.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\raichu.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\raikou.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\rapidash.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\raticate.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\rattata.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\remoraid.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\rhydon.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\rhyhorn.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\sandshrew.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\sandslash.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\scizor.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\scyther.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\seadra.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\seaking.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\seel.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\sentret.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\shellder.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\shuckle.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\skarmory.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\skiploom.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\slowbro.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\slowking.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\slowpoke.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\slugma.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\smeargle.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\smoochum.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\sneasel.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\snorlax.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\snubbull.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\spearow.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\spinarak.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\squirtle.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\stantler.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\starmie.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\staryu.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\steelix.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\sudowoodo.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\suicune.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\sunflora.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\sunkern.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\swinub.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\tangela.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\tauros.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\teddiursa.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\tentacool.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\tentacruel.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\togepi.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\togetic.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\totodile.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\typhlosion.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\tyranitar.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\tyrogue.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\umbreon.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\unown.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\ursaring.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\vaporeon.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\venomoth.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\venonat.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\venusaur.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\victreebel.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\vileplume.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\voltorb.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\vulpix.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\wartortle.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\weedle.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\weepinbell.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\weezing.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\wigglytuff.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\wobbuffet.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\wooper.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\xatu.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\yanma.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\zapdos.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\gold\zubat.asm` | 8 | Per-species Gold Pokédex entry text. |
| `data\pokemon\dex_entries\silver\abra.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\aerodactyl.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\aipom.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\alakazam.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\ampharos.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\arbok.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\arcanine.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\ariados.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\articuno.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\azumarill.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\bayleef.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\beedrill.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\bellossom.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\bellsprout.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\blastoise.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\blissey.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\bulbasaur.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\butterfree.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\caterpie.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\celebi.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\chansey.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\charizard.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\charmander.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\charmeleon.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\chikorita.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\chinchou.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\clefable.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\clefairy.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\cleffa.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\cloyster.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\corsola.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\crobat.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\croconaw.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\cubone.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\cyndaquil.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\delibird.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\dewgong.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\diglett.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\ditto.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\dodrio.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\doduo.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\donphan.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\dragonair.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\dragonite.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\dratini.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\drowzee.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\dugtrio.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\dunsparce.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\eevee.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\ekans.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\electabuzz.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\electrode.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\elekid.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\entei.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\espeon.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\exeggcute.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\exeggutor.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\farfetch_d.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\fearow.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\feraligatr.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\flaaffy.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\flareon.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\forretress.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\furret.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\gastly.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\gengar.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\geodude.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\girafarig.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\gligar.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\gloom.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\golbat.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\goldeen.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\golduck.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\golem.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\granbull.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\graveler.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\grimer.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\growlithe.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\gyarados.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\haunter.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\heracross.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\hitmonchan.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\hitmonlee.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\hitmontop.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\ho_oh.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\hoothoot.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\hoppip.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\horsea.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\houndoom.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\houndour.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\hypno.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\igglybuff.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\ivysaur.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\jigglypuff.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\jolteon.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\jumpluff.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\jynx.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\kabuto.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\kabutops.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\kadabra.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\kakuna.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\kangaskhan.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\kingdra.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\kingler.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\koffing.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\krabby.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\lanturn.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\lapras.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\larvitar.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\ledian.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\ledyba.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\lickitung.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\lugia.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\machamp.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\machoke.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\machop.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\magby.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\magcargo.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\magikarp.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\magmar.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\magnemite.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\magneton.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\mankey.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\mantine.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\mareep.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\marill.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\marowak.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\meganium.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\meowth.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\metapod.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\mew.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\mewtwo.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\miltank.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\misdreavus.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\moltres.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\mr__mime.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\muk.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\murkrow.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\natu.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\nidoking.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\nidoqueen.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\nidoran_f.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\nidoran_m.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\nidorina.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\nidorino.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\ninetales.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\noctowl.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\octillery.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\oddish.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\omanyte.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\omastar.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\onix.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\paras.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\parasect.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\persian.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\phanpy.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\pichu.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\pidgeot.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\pidgeotto.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\pidgey.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\pikachu.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\piloswine.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\pineco.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\pinsir.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\politoed.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\poliwag.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\poliwhirl.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\poliwrath.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\ponyta.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\porygon.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\porygon2.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\primeape.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\psyduck.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\pupitar.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\quagsire.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\quilava.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\qwilfish.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\raichu.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\raikou.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\rapidash.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\raticate.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\rattata.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\remoraid.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\rhydon.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\rhyhorn.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\sandshrew.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\sandslash.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\scizor.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\scyther.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\seadra.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\seaking.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\seel.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\sentret.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\shellder.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\shuckle.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\skarmory.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\skiploom.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\slowbro.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\slowking.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\slowpoke.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\slugma.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\smeargle.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\smoochum.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\sneasel.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\snorlax.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\snubbull.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\spearow.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\spinarak.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\squirtle.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\stantler.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\starmie.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\staryu.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\steelix.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\sudowoodo.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\suicune.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\sunflora.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\sunkern.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\swinub.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\tangela.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\tauros.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\teddiursa.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\tentacool.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\tentacruel.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\togepi.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\togetic.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\totodile.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\typhlosion.asm` | 9 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\tyranitar.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\tyrogue.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\umbreon.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\unown.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\ursaring.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\vaporeon.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\venomoth.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\venonat.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\venusaur.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\victreebel.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\vileplume.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\voltorb.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\vulpix.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\wartortle.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\weedle.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\weepinbell.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\weezing.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\wigglytuff.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\wobbuffet.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\wooper.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\xatu.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\yanma.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\zapdos.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entries\silver\zubat.asm` | 8 | Per-species Silver Pokédex entry text. |
| `data\pokemon\dex_entry_pointers.asm` | 255 | Pointer table from species to Pokédex entry text. |
| `data\pokemon\dex_order_alpha.asm` | 255 | Alphabetical Pokédex order table. |
| `data\pokemon\dex_order_new.asm` | 255 | New Pokédex order table. |
| `data\pokemon\egg_move_pointers.asm` | 254 | Species-to-egg-move-list pointer table. |
| `data\pokemon\egg_moves.asm` | 694 | Egg move lists. |
| `data\pokemon\evos_attacks_pointers.asm` | 255 | Species-to-evolution/learnset pointer table. |
| `data\pokemon\evos_attacks.asm` | 3096 | Evolution methods and level-up learnsets. |
| `data\pokemon\gen1_base_special.asm` | 155 | Original Red/Blue base Special stat table for compatibility. |
| `data\pokemon\gen1_order.asm` | 256 | Gen 1 species-order / compatibility table. |
| `data\pokemon\menu_icons.asm` | 255 | Species-to-party-menu-icon table. |
| `data\pokemon\names.asm` | 261 | Pokémon name strings. |
| `data\pokemon\palettes.asm` | 539 | Normal/shiny Pokémon palette table. |
| `data\pokemon\pic_pointers.asm` | 256 | Species-to-front/back picture pointer table. |
| `data\pokemon\unown_pic_pointers.asm` | 30 | Pointer table for individual Unown form pictures. |
| `data\pokemon\unown_words.asm` | 63 | Unown-word lookup table used by the Unown mode/puzzles. |
| `data\pokemon\unused_pic_banks.asm` | 16 | Unused prototype-era sprite-bank table kept for reference. |

# 5. Audio engine

Audio is banked through `audio.asm`, which includes the engine, pointer tables, song banks, and SFX/cry banks (`audio.asm:1-140`). The sound engine itself initializes hardware and updates active channels every frame in `audio\engine.asm`, while `audio\music_pointers.asm` maps `MUSIC_*` IDs to songs and `audio\sfx.asm` / `audio\cries.asm` store channel scripts for SFX and cries (`audio\engine.asm:1-100`, `audio\music_pointers.asm:1-80`, `audio\sfx.asm:1-79`, `audio\cries.asm:1-20`).

| Path | ~Lines | Description |
|---|---:|---|
| `audio\cries.asm` | 1858 | Pokémon cry channel scripts. |
| `audio\cry_pointers.asm` | 72 | Pointer table for cry data. |
| `audio\drumkits.asm` | 221 | Drum/noise instrument tables. |
| `audio\engine.asm` | 2645 | Core sound engine: channel state, playback, fades, and per-frame update. |
| `audio\music_pointers.asm` | 98 | Pointer table from music IDs to song data. |
| `audio\music\aftertherivalfight.asm` | 61 | Song data for aftertherivalfight. |
| `audio\music\azaleatown.asm` | 645 | Song data for azaleatown. |
| `audio\music\bicycle.asm` | 509 | Song data for bicycle. |
| `audio\music\bugcatchingcontest.asm` | 663 | Song data for bugcatchingcontest. |
| `audio\music\burnedtower.asm` | 256 | Song data for burnedtower. |
| `audio\music\celadoncity.asm` | 380 | Song data for celadoncity. |
| `audio\music\championbattle.asm` | 761 | Song data for championbattle. |
| `audio\music\cherrygrovecity.asm` | 302 | Song data for cherrygrovecity. |
| `audio\music\contestresults.asm` | 170 | Song data for contestresults. |
| `audio\music\credits.asm` | 1656 | Song data for credits. |
| `audio\music\dancinghall.asm` | 300 | Song data for dancinghall. |
| `audio\music\darkcave.asm` | 479 | Song data for darkcave. |
| `audio\music\dragonsden.asm` | 165 | Song data for dragonsden. |
| `audio\music\ecruteakcity.asm` | 662 | Song data for ecruteakcity. |
| `audio\music\elmslab.asm` | 534 | Song data for elmslab. |
| `audio\music\evolution.asm` | 193 | Song data for evolution. |
| `audio\music\gamecorner.asm` | 793 | Song data for gamecorner. |
| `audio\music\goldenrodcity.asm` | 464 | Song data for goldenrodcity. |
| `audio\music\goldsilveropening.asm` | 613 | Song data for goldsilveropening. |
| `audio\music\goldsilveropening2.asm` | 214 | Song data for goldsilveropening2. |
| `audio\music\gym.asm` | 560 | Song data for gym. |
| `audio\music\gymleadervictory.asm` | 413 | Song data for gymleadervictory. |
| `audio\music\halloffame.asm` | 170 | Song data for halloffame. |
| `audio\music\healpokemon.asm` | 48 | Song data for healpokemon. |
| `audio\music\indigoplateau.asm` | 176 | Song data for indigoplateau. |
| `audio\music\johtogymbattle.asm` | 1009 | Song data for johtogymbattle. |
| `audio\music\johtotrainerbattle.asm` | 1319 | Song data for johtotrainerbattle. |
| `audio\music\johtowildbattle.asm` | 581 | Song data for johtowildbattle. |
| `audio\music\johtowildbattlenight.asm` | 26 | Song data for johtowildbattlenight. |
| `audio\music\kantogymbattle.asm` | 607 | Song data for kantogymbattle. |
| `audio\music\kantotrainerbattle.asm` | 1533 | Song data for kantotrainerbattle. |
| `audio\music\kantowildbattle.asm` | 1207 | Song data for kantowildbattle. |
| `audio\music\lakeofrage.asm` | 319 | Song data for lakeofrage. |
| `audio\music\lakeofragerocketradio.asm` | 33 | Song data for lakeofragerocketradio. |
| `audio\music\lavendertown.asm` | 508 | Song data for lavendertown. |
| `audio\music\lighthouse.asm` | 306 | Song data for lighthouse. |
| `audio\music\lookbeauty.asm` | 321 | Song data for lookbeauty. |
| `audio\music\lookhiker.asm` | 110 | Song data for lookhiker. |
| `audio\music\lookkimonogirl.asm` | 258 | Song data for lookkimonogirl. |
| `audio\music\looklass.asm` | 111 | Song data for looklass. |
| `audio\music\lookofficer.asm` | 135 | Song data for lookofficer. |
| `audio\music\lookpokemaniac.asm` | 158 | Song data for lookpokemaniac. |
| `audio\music\lookrival.asm` | 343 | Song data for lookrival. |
| `audio\music\lookrocket.asm` | 363 | Song data for lookrocket. |
| `audio\music\looksage.asm` | 176 | Song data for looksage. |
| `audio\music\lookyoungster.asm` | 290 | Song data for lookyoungster. |
| `audio\music\magnettrain.asm` | 275 | Song data for magnettrain. |
| `audio\music\mainmenu.asm` | 142 | Song data for mainmenu. |
| `audio\music\mom.asm` | 106 | Song data for mom. |
| `audio\music\mtmoon.asm` | 135 | Song data for mtmoon. |
| `audio\music\mtmoonsquare.asm` | 106 | Song data for mtmoonsquare. |
| `audio\music\nationalpark.asm` | 649 | Song data for nationalpark. |
| `audio\music\newbarktown.asm` | 317 | Song data for newbarktown. |
| `audio\music\nothing.asm` | 11 | Song data for nothing. |
| `audio\music\pallettown.asm` | 355 | Song data for pallettown. |
| `audio\music\pokeflutechannel.asm` | 224 | Song data for pokeflutechannel. |
| `audio\music\pokemoncenter.asm` | 396 | Song data for pokemoncenter. |
| `audio\music\pokemonchannel.asm` | 228 | Song data for pokemonchannel. |
| `audio\music\pokemonlullaby.asm` | 130 | Song data for pokemonlullaby. |
| `audio\music\pokemonmarch.asm` | 451 | Song data for pokemonmarch. |
| `audio\music\postcredits.asm` | 262 | Song data for postcredits. |
| `audio\music\printer.asm` | 319 | Song data for printer. |
| `audio\music\profoak.asm` | 301 | Song data for profoak. |
| `audio\music\profoakspokemontalk.asm` | 304 | Song data for profoakspokemontalk. |
| `audio\music\rivalbattle.asm` | 856 | Song data for rivalbattle. |
| `audio\music\rocketbattle.asm` | 1021 | Song data for rocketbattle. |
| `audio\music\rockethideout.asm` | 305 | Song data for rockethideout. |
| `audio\music\rockettheme.asm` | 468 | Song data for rockettheme. |
| `audio\music\route1.asm` | 640 | Song data for route1. |
| `audio\music\route12.asm` | 443 | Song data for route12. |
| `audio\music\route2.asm` | 503 | Song data for route2. |
| `audio\music\route26.asm` | 657 | Song data for route26. |
| `audio\music\route29.asm` | 516 | Song data for route29. |
| `audio\music\route3.asm` | 495 | Song data for route3. |
| `audio\music\route30.asm` | 661 | Song data for route30. |
| `audio\music\route36.asm` | 515 | Song data for route36. |
| `audio\music\route37.asm` | 418 | Song data for route37. |
| `audio\music\ruinsofalphinterior.asm` | 44 | Song data for ruinsofalphinterior. |
| `audio\music\ruinsofalphradio.asm` | 69 | Song data for ruinsofalphradio. |
| `audio\music\showmearound.asm` | 338 | Song data for showmearound. |
| `audio\music\sprouttower.asm` | 239 | Song data for sprouttower. |
| `audio\music\ssaqua.asm` | 1158 | Song data for ssaqua. |
| `audio\music\successfulcapture.asm` | 20 | Song data for successfulcapture. |
| `audio\music\surf.asm` | 710 | Song data for surf. |
| `audio\music\tintower.asm` | 288 | Song data for tintower. |
| `audio\music\titlescreen.asm` | 1225 | Song data for titlescreen. |
| `audio\music\trainervictory.asm` | 227 | Song data for trainervictory. |
| `audio\music\unioncave.asm` | 253 | Song data for unioncave. |
| `audio\music\vermilioncity.asm` | 327 | Song data for vermilioncity. |
| `audio\music\victoryroad.asm` | 193 | Song data for victoryroad. |
| `audio\music\violetcity.asm` | 768 | Song data for violetcity. |
| `audio\music\viridiancity.asm` | 768 | Song data for viridiancity. |
| `audio\music\wildpokemonvictory.asm` | 177 | Song data for wildpokemonvictory. |
| `audio\notes.asm` | 29 | Pitch/frequency tables for notes. |
| `audio\sfx_pointers.asm` | 192 | Pointer table for sound effects. |
| `audio\sfx.asm` | 4628 | Sound-effect channel scripts. |
| `audio\wave_samples.asm` | 13 | Wave-channel sample data. |

# 6. Graphics / rendering

Engine-side rendering helpers live in `engine\gfx\`: fonts are loaded by `load_font.asm`, front/back Pokémon pictures are decompressed by `load_pics.asm`, and palette/layout setup is split between `color.asm`, `cgb_layouts.asm`, and `sgb_layouts.asm` (`engine\gfx\load_font.asm:1-70`, `engine\gfx\load_pics.asm:1-119`, `engine\gfx\color.asm:8-44`, `engine\gfx\cgb_layouts.asm:1-32`, `engine\gfx\sgb_layouts.asm:1-26`). Asset-side, root manifest files such as `gfx\misc.asm`, `gfx\pics_gold.asm`, and `gfx\tilesets.asm` tie binary assets into ROM with `INCBIN`, while `gfx\lz.mk` records custom compression rules (`gfx\misc.asm:1-72`, `gfx\pics_gold.asm:5-23`, `gfx\tilesets.asm:7-26`, `gfx\lz.mk:1-60`).

### `engine\\gfx\\`

| Path | ~Lines | Description |
|---|---:|---|
| `engine\gfx\cgb_layouts.asm` | 805 | CGB palette/layout dispatcher. |
| `engine\gfx\color.asm` | 1132 | Color/shininess/palette helper routines. |
| `engine\gfx\load_font.asm` | 95 | Font, textbox frame, and HP/EXP bar tile loaders. |
| `engine\gfx\load_pics.asm` | 398 | Pokémon picture decompression/loading and Unown-form selection. |
| `engine\gfx\load_push_oam.asm` | 27 | OAM load/push helper. |
| `engine\gfx\mon_icons.asm` | 305 | Party/menu icon loading and icon animation setup. |
| `engine\gfx\place_graphic.asm` | 46 | Places predecoded graphics onto the tilemap. |
| `engine\gfx\sgb_layouts.asm` | 526 | SGB palette/layout dispatcher. |

### `gfx\\`

| Path | ~Lines | Description |
|---|---:|---|
| `gfx\battle_anims.asm` | 40 | Graphics manifest / INCBIN table for battle animation assets. |
| `gfx\battle_anims\aeroblast.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\angels.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\battle_anims.pal` | 30 | Palette definition file. |
| `gfx\battle_anims\beam.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\bubble.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\charge.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\cut.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\egg.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\explosion.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\fire.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\flower.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\globe.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\haze.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\hit.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\horn.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\ice.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\lightning.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\misc.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\noise.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\objects.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\plant.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\poison.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\pokeball.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\powder.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\psychic.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\reflect.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\rocks.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\rope.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\sand.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\shapes.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\shine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\skyattack.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\smoke.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\speed.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\status.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\unused_battle_anims.pal` | 30 | Palette definition file. |
| `gfx\battle_anims\water.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\wave.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\web.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\whip.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle_anims\wind.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle\balls.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle\dude.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle\enemy_hp_bar_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle\exp_bar.pal` | 3 | Palette definition file. |
| `gfx\battle\expbar.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle\expbarend_sgb.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle\expbarend.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle\hp_bar.pal` | 9 | Palette definition file. |
| `gfx\battle\hp_exp_bar_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\battle\minimize.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\beta_poker\beta_poker.pal` | 16 | Palette definition file. |
| `gfx\card_flip\card_flip_1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\card_flip\card_flip_2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\card_flip\card_flip_3.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\card_flip\card_flip.pal` | 36 | Palette definition file. |
| `gfx\card_flip\card_flip.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\card_flip\off.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\card_flip\on.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\credits\bellossom.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\credits\border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\credits\credits.pal` | 6 | Palette definition file. |
| `gfx\credits\elekid.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\credits\sentret.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\credits\theend.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\credits\togepi.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\debug\bg.pal` | 36 | Palette definition file. |
| `gfx\debug\color_test.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\debug\ob.pal` | 35 | Palette definition file. |
| `gfx\debug\up_arrow.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\diploma\diploma.pal` | 32 | Palette definition file. |
| `gfx\diploma\diploma.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\diploma\page1.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\diploma\page2.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\emotes.asm` | 12 | Graphics manifest / INCBIN table for emote assets. |
| `gfx\emotes\bolt.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\emotes\fish.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\emotes\happy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\emotes\heart.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\emotes\question.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\emotes\sad.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\emotes\shock.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\emotes\sleep.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\evo\bubble_large.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\evo\bubble.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\evo\egg_hatch.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font.asm` | 42 | Graphics manifest / INCBIN table for font assets. |
| `gfx\font\black.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font\feet_inches.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font\font_battle_extra.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font\font_extra.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font\font_inversed.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font\font.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font\phone_icon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font\space.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font\unown_font.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font\unused_bold_font.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font\unused_weekday_kanji.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\font\up_arrow.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints.asm` | 587 | Graphics manifest / INCBIN table for footprint assets. |
| `gfx\footprints\252.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\253.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\254.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\255.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\256.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\abra.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\aerodactyl.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\aipom.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\alakazam.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\ampharos.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\arbok.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\arcanine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\ariados.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\articuno.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\azumarill.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\bayleef.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\beedrill.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\bellossom.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\bellsprout.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\blastoise.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\blissey.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\bulbasaur.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\butterfree.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\caterpie.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\celebi.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\chansey.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\charizard.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\charmander.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\charmeleon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\chikorita.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\chinchou.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\clefable.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\clefairy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\cleffa.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\cloyster.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\corsola.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\crobat.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\croconaw.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\cubone.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\cyndaquil.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\delibird.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\dewgong.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\diglett.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\ditto.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\dodrio.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\doduo.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\donphan.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\dragonair.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\dragonite.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\dratini.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\drowzee.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\dugtrio.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\dunsparce.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\eevee.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\ekans.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\electabuzz.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\electrode.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\elekid.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\entei.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\espeon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\exeggcute.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\exeggutor.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\farfetch_d.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\fearow.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\feraligatr.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\flaaffy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\flareon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\forretress.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\furret.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\gastly.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\gengar.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\geodude.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\girafarig.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\gligar.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\gloom.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\golbat.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\goldeen.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\golduck.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\golem.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\granbull.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\graveler.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\grimer.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\growlithe.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\gyarados.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\haunter.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\heracross.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\hitmonchan.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\hitmonlee.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\hitmontop.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\ho_oh.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\hoothoot.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\hoppip.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\horsea.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\houndoom.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\houndour.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\hypno.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\igglybuff.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\ivysaur.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\jigglypuff.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\jolteon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\jumpluff.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\jynx.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\kabuto.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\kabutops.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\kadabra.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\kakuna.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\kangaskhan.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\kingdra.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\kingler.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\koffing.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\krabby.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\lanturn.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\lapras.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\larvitar.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\ledian.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\ledyba.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\lickitung.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\lugia.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\machamp.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\machoke.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\machop.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\magby.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\magcargo.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\magikarp.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\magmar.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\magnemite.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\magneton.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\mankey.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\mantine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\mareep.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\marill.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\marowak.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\meganium.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\meowth.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\metapod.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\mew.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\mewtwo.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\miltank.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\misdreavus.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\moltres.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\mr__mime.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\muk.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\murkrow.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\natu.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\nidoking.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\nidoqueen.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\nidoran_f.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\nidoran_m.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\nidorina.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\nidorino.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\ninetales.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\noctowl.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\octillery.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\oddish.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\omanyte.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\omastar.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\onix.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\paras.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\parasect.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\persian.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\phanpy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\pichu.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\pidgeot.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\pidgeotto.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\pidgey.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\pikachu.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\piloswine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\pineco.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\pinsir.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\politoed.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\poliwag.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\poliwhirl.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\poliwrath.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\ponyta.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\porygon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\porygon2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\primeape.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\psyduck.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\pupitar.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\quagsire.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\quilava.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\qwilfish.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\raichu.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\raikou.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\rapidash.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\raticate.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\rattata.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\remoraid.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\rhydon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\rhyhorn.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\sandshrew.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\sandslash.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\scizor.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\scyther.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\seadra.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\seaking.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\seel.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\sentret.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\shellder.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\shuckle.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\skarmory.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\skiploom.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\slowbro.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\slowking.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\slowpoke.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\slugma.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\smeargle.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\smoochum.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\sneasel.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\snorlax.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\snubbull.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\spearow.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\spinarak.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\squirtle.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\stantler.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\starmie.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\staryu.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\steelix.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\sudowoodo.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\suicune.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\sunflora.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\sunkern.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\swinub.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\tangela.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\tauros.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\teddiursa.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\tentacool.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\tentacruel.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\togepi.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\togetic.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\totodile.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\typhlosion.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\tyranitar.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\tyrogue.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\umbreon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\unown.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\ursaring.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\vaporeon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\venomoth.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\venonat.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\venusaur.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\victreebel.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\vileplume.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\voltorb.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\vulpix.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\wartortle.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\weedle.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\weepinbell.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\weezing.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\wigglytuff.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\wobbuffet.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\wooper.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\xatu.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\yanma.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\zapdos.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\footprints\zubat.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\frames\1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\frames\2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\frames\3.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\frames\4.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\frames\5.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\frames\6.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\frames\7.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\frames\8.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\frames\9.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons.asm` | 40 | Graphics manifest / INCBIN table for icon assets. |
| `gfx\icons\bat.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\bigmon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\bird.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\blob.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\bug.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\bulbasaur.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\caterpillar.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\charmander.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\clefairy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\diglett.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\egg.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\equine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\fighter.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\fish.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\fox.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\geodude.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\ghost.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\gyarados.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\ho_oh.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\humanshape.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\jellyfish.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\jigglypuff.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\lapras.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\lugia.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\monster.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\moth.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\oddish.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\pikachu.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\poliwag.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\serpent.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\shell.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\slowpoke.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\snorlax.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\squirtle.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\staryu.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\sudowoodo.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\unown.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\icons\voltorb.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\intro\charizard1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\intro\charizard2_bottom.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\intro\charizard2_top.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\intro\charizard3.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\intro\fire.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\intro\grass.bin` | n/a (binary) | UNCLEAR: graphics asset helper file. |
| `gfx\intro\grass.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\intro\grass1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\intro\grass2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\intro\magikarp_bg.pal` | 4 | Palette definition file. |
| `gfx\intro\magikarp_ob.pal` | 4 | Palette definition file. |
| `gfx\intro\shellder_lapras_bg.pal` | 4 | Palette definition file. |
| `gfx\intro\shellder_lapras_ob.pal` | 8 | Palette definition file. |
| `gfx\intro\space.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\intro\unused_blastoise_venusaur.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\intro\water.bin` | n/a (binary) | UNCLEAR: graphics asset helper file. |
| `gfx\intro\water.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\intro\water1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\intro\water2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\lz.mk` | 44 | Compression-rule overrides for selected `.lz` outputs. |
| `gfx\mail.asm` | 72 | Graphics manifest / INCBIN table for mail assets. |
| `gfx\mail\cloud.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\ditto.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\dragonite.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\dratini.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\eevee.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\eon_mail_border_1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\eon_mail_border_2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\flower_1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\flower_2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\flower_mail_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\grass.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\lapras.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\large_circle.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\large_heart.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\large_note.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\large_pokeball.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\large_triangle.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\litebluemail_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\lovely_mail_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\lovely_mail_underline.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\mail.pal` | 10 | Palette definition file. |
| `gfx\mail\mew.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\morph_mail_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\morph_mail_corner.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\morph_mail_divider.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\music_mail_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\natu.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\oddish.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\poliwag.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\portraitmail_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\portraitmail_underline.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\sentret.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\small_heart.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\small_note.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\small_pokeball.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\small_triangle.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\surf_mail_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mail\wave.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\memory_game\memory_game.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\misc.asm` | 46 | Graphics manifest / INCBIN table for shared title/UI assets. |
| `gfx\mystery_gift\border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mystery_gift\mystery_gift_2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mystery_gift\mystery_gift.pal` | 4 | Palette definition file. |
| `gfx\mystery_gift\mystery_gift.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\mystery_gift\question_mark.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\naming_screen\border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\naming_screen\cursor.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\naming_screen\end.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\naming_screen\mail.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\naming_screen\middle_line.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\naming_screen\underline.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\new_game\down_arrow.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\new_game\shrink1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\new_game\shrink2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\new_game\timeset_bg.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\new_game\up_arrow.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\overworld\boulder_dust.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\overworld\chris_fish.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\overworld\cut_grass.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\overworld\cut_tree.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\overworld\fishing_rod.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\overworld\grass_rustle.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\overworld\headbutt_tree.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\overworld\heal_machine.pal` | 4 | Palette definition file. |
| `gfx\overworld\heal_machine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\overworld\magnet_train_bg.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\overworld\magnet_train_fg.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\overworld\npc_sprites.pal` | 36 | Palette definition file. |
| `gfx\overworld\shadow.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\overworld\trainer_battle_dark.pal` | 4 | Palette definition file. |
| `gfx\overworld\trainer_battle_pokeball_tiles.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\overworld\trainer_battle.pal` | 4 | Palette definition file. |
| `gfx\pack\pack_menu.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pack\pack_menu.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\pack\pack.pal` | 24 | Palette definition file. |
| `gfx\pack\pack.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pc\orange.pal` | 4 | Palette definition file. |
| `gfx\pc\pc_mail.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pc\pc.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pics_gold.asm` | 640 | Gold-version picture manifest for Pokémon and trainer sprites. |
| `gfx\pics_silver.asm` | 643 | Silver-version picture manifest for Pokémon and trainer sprites. |
| `gfx\player\chris_back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokedex\cursor.pal` | 4 | Palette definition file. |
| `gfx\pokedex\pokedex_sgb.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokedex\pokedex.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokedex\question_mark.pal` | 4 | Palette definition file. |
| `gfx\pokedex\question_mark.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokedex\slowpoke.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokegear\clock.tilemap.rle` | n/a (binary) | UNCLEAR: graphics asset helper file. |
| `gfx\pokegear\dexmap_nest_icon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokegear\fast_ship.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokegear\flymap_label_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokegear\johto.bin` | n/a (binary) | UNCLEAR: graphics asset helper file. |
| `gfx\pokegear\kanto.bin` | n/a (binary) | UNCLEAR: graphics asset helper file. |
| `gfx\pokegear\phone.tilemap.rle` | n/a (binary) | UNCLEAR: graphics asset helper file. |
| `gfx\pokegear\pokegear_sprites.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokegear\pokegear.pal` | 30 | Palette definition file. |
| `gfx\pokegear\pokegear.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokegear\radio.tilemap.rle` | n/a (binary) | UNCLEAR: graphics asset helper file. |
| `gfx\pokegear\town_map_palette_map.asm` | 27 | Graphics manifest / include file for this asset group. |
| `gfx\pokegear\town_map.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\abra\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\abra\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\abra\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\abra\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\aerodactyl\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\aerodactyl\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\aerodactyl\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\aerodactyl\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\aipom\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\aipom\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\aipom\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\aipom\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\alakazam\back_silver.2bpp.lz.bin` | n/a (binary) | UNCLEAR: graphics asset helper file. |
| `gfx\pokemon\alakazam\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\alakazam\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\alakazam\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\alakazam\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\ampharos\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ampharos\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ampharos\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ampharos\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\arbok\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\arbok\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\arbok\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\arbok\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\arcanine\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\arcanine\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\arcanine\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\arcanine\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\ariados\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ariados\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ariados\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\articuno\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\articuno\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\articuno\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\articuno\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\azumarill\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\azumarill\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\azumarill\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\azumarill\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\bayleef\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\bayleef\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\bayleef\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\beedrill\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\beedrill\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\beedrill\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\beedrill\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\bellossom\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\bellossom\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\bellossom\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\bellossom\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\bellsprout\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\bellsprout\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\bellsprout\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\bellsprout\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\blastoise\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\blastoise\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\blastoise\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\blastoise\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\blissey\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\blissey\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\blissey\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\blissey\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\bulbasaur\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\bulbasaur\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\bulbasaur\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\bulbasaur\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\butterfree\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\butterfree\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\butterfree\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\butterfree\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\caterpie\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\caterpie\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\caterpie\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\caterpie\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\celebi\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\celebi\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\celebi\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\celebi\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\chansey\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\chansey\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\chansey\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\chansey\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\charizard\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\charizard\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\charizard\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\charizard\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\charmander\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\charmander\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\charmander\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\charmander\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\charmeleon\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\charmeleon\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\charmeleon\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\charmeleon\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\chikorita\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\chikorita\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\chikorita\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\chikorita\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\chinchou\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\chinchou\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\chinchou\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\chinchou\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\clefable\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\clefable\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\clefable\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\clefable\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\clefairy\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\clefairy\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\clefairy\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\clefairy\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\cleffa\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cleffa\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cleffa\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cleffa\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\cloyster\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cloyster\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cloyster\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cloyster\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\corsola\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\corsola\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\corsola\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\corsola\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\crobat\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\crobat\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\crobat\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\crobat\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\croconaw\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\croconaw\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\croconaw\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\croconaw\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\cubone\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cubone\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cubone\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cubone\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\cyndaquil\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cyndaquil\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cyndaquil\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\cyndaquil\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\delibird\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\delibird\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\delibird\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\delibird\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\dewgong\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dewgong\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dewgong\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dewgong\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\diglett\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\diglett\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\diglett\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\diglett\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\ditto\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ditto\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ditto\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ditto\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\dodrio\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dodrio\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dodrio\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dodrio\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\doduo\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\doduo\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\doduo\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\doduo\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\donphan\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\donphan\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\donphan\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\donphan\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\dragonair\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dragonair\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dragonair\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dragonair\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\dragonite\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dragonite\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dragonite\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dragonite\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\dratini\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dratini\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dratini\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dratini\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\drowzee\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\drowzee\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\drowzee\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\drowzee\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\dugtrio\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dugtrio\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dugtrio\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dugtrio\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\dunsparce\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dunsparce\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dunsparce\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\dunsparce\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\eevee\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\eevee\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\eevee\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\eevee\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\egg\egg.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\egg\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\ekans\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ekans\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ekans\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ekans\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\electabuzz\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\electabuzz\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\electabuzz\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\electabuzz\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\electrode\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\electrode\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\electrode\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\electrode\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\elekid\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\elekid\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\elekid\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\elekid\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\entei\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\entei\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\entei\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\espeon\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\espeon\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\espeon\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\espeon\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\exeggcute\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\exeggcute\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\exeggcute\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\exeggcute\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\exeggutor\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\exeggutor\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\exeggutor\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\exeggutor\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\farfetch_d\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\farfetch_d\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\farfetch_d\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\farfetch_d\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\fearow\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\fearow\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\fearow\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\fearow\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\feraligatr\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\feraligatr\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\feraligatr\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\feraligatr\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\flaaffy\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\flaaffy\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\flaaffy\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\flaaffy\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\flareon\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\flareon\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\flareon\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\flareon\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\forretress\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\forretress\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\forretress\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\forretress\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\furret\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\furret\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\furret\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\furret\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\gastly\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gastly\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gastly\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gastly\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\gengar\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gengar\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gengar\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gengar\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\geodude\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\geodude\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\geodude\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\geodude\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\girafarig\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\girafarig\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\girafarig\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\girafarig\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\gligar\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gligar\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gligar\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gligar\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\gloom\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gloom\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gloom\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gloom\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\golbat\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\golbat\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\golbat\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\golbat\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\goldeen\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\goldeen\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\goldeen\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\goldeen\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\golduck\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\golduck\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\golduck\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\golduck\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\golem\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\golem\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\golem\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\golem\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\granbull\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\granbull\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\granbull\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\granbull\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\graveler\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\graveler\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\graveler\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\graveler\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\grimer\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\grimer\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\grimer\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\grimer\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\growlithe\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\growlithe\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\growlithe\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\growlithe\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\gyarados\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gyarados\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gyarados\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\gyarados\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\haunter\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\haunter\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\haunter\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\haunter\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\heracross\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\heracross\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\heracross\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\heracross\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\hitmonchan\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hitmonchan\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hitmonchan\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hitmonchan\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\hitmonlee\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hitmonlee\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hitmonlee\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hitmonlee\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\hitmontop\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hitmontop\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hitmontop\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hitmontop\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\ho_oh\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ho_oh\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ho_oh\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ho_oh\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\hoothoot\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hoothoot\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hoothoot\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hoothoot\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\hoppip\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hoppip\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hoppip\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hoppip\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\horsea\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\horsea\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\horsea\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\horsea\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\houndoom\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\houndoom\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\houndoom\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\houndoom\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\houndour\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\houndour\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\houndour\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\houndour\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\hypno\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hypno\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hypno\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\hypno\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\igglybuff\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\igglybuff\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\igglybuff\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\igglybuff\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\ivysaur\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ivysaur\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ivysaur\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ivysaur\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\jigglypuff\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jigglypuff\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jigglypuff\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jigglypuff\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\jolteon\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jolteon\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jolteon\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jolteon\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\jumpluff\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jumpluff\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jumpluff\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jumpluff\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\jynx\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jynx\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jynx\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\jynx\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\kabuto\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kabuto\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kabuto\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kabuto\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\kabutops\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kabutops\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kabutops\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kabutops\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\kadabra\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kadabra\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kadabra\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kadabra\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\kakuna\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kakuna\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kakuna\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kakuna\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\kangaskhan\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kangaskhan\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kangaskhan\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kangaskhan\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\kingdra\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kingdra\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kingdra\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kingdra\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\kingler\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kingler\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kingler\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\kingler\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\koffing\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\koffing\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\koffing\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\koffing\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\krabby\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\krabby\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\krabby\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\krabby\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\lanturn\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lanturn\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lanturn\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lanturn\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\lapras\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lapras\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lapras\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lapras\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\larvitar\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\larvitar\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\larvitar\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\larvitar\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\ledian\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ledian\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ledian\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ledian\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\ledyba\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ledyba\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ledyba\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ledyba\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\lickitung\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lickitung\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lickitung\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lickitung\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\lugia\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lugia\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lugia\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\lugia\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\machamp\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\machamp\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\machamp\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\machamp\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\machoke\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\machoke\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\machoke\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\machoke\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\machop\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\machop\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\machop\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\machop\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\magby\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magby\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magby\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magby\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\magcargo\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magcargo\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magcargo\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magcargo\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\magikarp\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magikarp\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magikarp\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magikarp\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\magmar\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magmar\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magmar\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magmar\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\magnemite\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magnemite\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magnemite\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magnemite\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\magneton\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magneton\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magneton\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\magneton\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\mankey\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mankey\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mankey\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mankey\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\mantine\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mantine\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mantine\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mantine\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\mareep\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mareep\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mareep\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mareep\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\marill\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\marill\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\marill\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\marill\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\marowak\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\marowak\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\marowak\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\marowak\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\meganium\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\meganium\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\meganium\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\meganium\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\meowth\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\meowth\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\meowth\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\meowth\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\metapod\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\metapod\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\metapod\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\metapod\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\mew\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mew\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mew\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mew\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\mewtwo\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mewtwo\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mewtwo\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mewtwo\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\miltank\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\miltank\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\miltank\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\miltank\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\misdreavus\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\misdreavus\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\misdreavus\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\misdreavus\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\moltres\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\moltres\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\moltres\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\moltres\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\mr__mime\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mr__mime\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mr__mime\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\mr__mime\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\muk\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\muk\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\muk\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\muk\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\murkrow\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\murkrow\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\murkrow\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\murkrow\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\natu\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\natu\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\natu\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\natu\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\nidoking\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoking\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoking\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoking\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\nidoqueen\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoqueen\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoqueen\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoqueen\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\nidoran_f\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoran_f\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoran_f\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoran_f\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\nidoran_m\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoran_m\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoran_m\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidoran_m\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\nidorina\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidorina\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidorina\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidorina\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\nidorino\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidorino\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidorino\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\nidorino\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\ninetales\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ninetales\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ninetales\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ninetales\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\noctowl\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\noctowl\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\noctowl\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\noctowl\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\octillery\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\octillery\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\octillery\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\octillery\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\oddish\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\oddish\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\oddish\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\oddish\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\omanyte\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\omanyte\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\omanyte\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\omanyte\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\omastar\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\omastar\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\omastar\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\omastar\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\onix\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\onix\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\onix\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\onix\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\paras\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\paras\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\paras\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\paras\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\parasect\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\parasect\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\parasect\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\parasect\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\persian\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\persian\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\persian\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\persian\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\phanpy\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\phanpy\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\phanpy\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\phanpy\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\pichu\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pichu\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pichu\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pichu\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\pidgeot\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pidgeot\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pidgeot\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pidgeot\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\pidgeotto\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pidgeotto\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pidgeotto\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pidgeotto\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\pidgey\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pidgey\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pidgey\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pidgey\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\pikachu\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pikachu\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pikachu\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pikachu\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\piloswine\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\piloswine\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\piloswine\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\piloswine\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\pineco\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pineco\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pineco\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pineco\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\pinsir\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pinsir\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pinsir\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pinsir\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\politoed\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\politoed\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\politoed\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\politoed\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\poliwag\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\poliwag\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\poliwag\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\poliwag\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\poliwhirl\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\poliwhirl\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\poliwhirl\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\poliwhirl\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\poliwrath\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\poliwrath\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\poliwrath\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\poliwrath\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\ponyta\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ponyta\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ponyta\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ponyta\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\porygon\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\porygon\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\porygon\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\porygon\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\porygon2\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\porygon2\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\porygon2\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\porygon2\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\primeape\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\primeape\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\primeape\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\primeape\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\psyduck\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\psyduck\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\psyduck\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\psyduck\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\pupitar\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pupitar\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pupitar\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\pupitar\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\quagsire\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\quagsire\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\quagsire\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\quagsire\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\quilava\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\quilava\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\quilava\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\quilava\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\qwilfish\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\qwilfish\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\qwilfish\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\qwilfish\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\raichu\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\raichu\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\raichu\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\raichu\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\raikou\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\raikou\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\raikou\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\rapidash\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rapidash\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rapidash\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rapidash\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\raticate\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\raticate\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\raticate\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\raticate\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\rattata\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rattata\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rattata\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rattata\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\remoraid\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\remoraid\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\remoraid\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\remoraid\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\rhydon\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rhydon\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rhydon\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rhydon\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\rhyhorn\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rhyhorn\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rhyhorn\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\rhyhorn\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\sandshrew\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sandshrew\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sandshrew\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sandshrew\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\sandslash\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sandslash\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sandslash\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sandslash\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\scizor\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\scizor\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\scizor\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\scizor\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\scyther\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\scyther\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\scyther\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\scyther\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\seadra\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\seadra\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\seadra\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\seadra\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\seaking\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\seaking\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\seaking\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\seaking\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\seel\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\seel\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\seel\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\seel\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\sentret\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sentret\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sentret\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sentret\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\shellder\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\shellder\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\shellder\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\shellder\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\shuckle\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\shuckle\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\shuckle\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\shuckle\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\skarmory\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\skarmory\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\skarmory\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\skarmory\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\skiploom\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\skiploom\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\skiploom\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\skiploom\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\slowbro\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slowbro\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slowbro\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slowbro\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\slowking\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slowking\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slowking\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slowking\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\slowpoke\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slowpoke\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slowpoke\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slowpoke\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\slugma\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slugma\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slugma\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\slugma\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\smeargle\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\smeargle\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\smeargle\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\smeargle\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\smoochum\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\smoochum\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\smoochum\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\smoochum\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\sneasel\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sneasel\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sneasel\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\snorlax\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\snorlax\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\snorlax\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\snorlax\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\snubbull\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\snubbull\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\snubbull\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\snubbull\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\spearow\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\spearow\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\spearow\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\spearow\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\spinarak\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\spinarak\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\spinarak\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\squirtle\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\squirtle\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\squirtle\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\squirtle\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\stantler\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\stantler\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\stantler\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\stantler\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\starmie\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\starmie\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\starmie\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\starmie\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\staryu\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\staryu\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\staryu\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\staryu\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\steelix\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\steelix\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\steelix\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\steelix\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\sudowoodo\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sudowoodo\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sudowoodo\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sudowoodo\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\suicune\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\suicune\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\suicune\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\sunflora\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sunflora\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sunflora\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sunflora\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\sunkern\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sunkern\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sunkern\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\sunkern\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\swinub\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\swinub\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\swinub\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\tangela\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tangela\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tangela\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tangela\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\tauros\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tauros\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tauros\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tauros\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\teddiursa\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\teddiursa\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\teddiursa\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\teddiursa\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\tentacool\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tentacool\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tentacool\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tentacool\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\tentacruel\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tentacruel\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tentacruel\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tentacruel\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\togepi\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\togepi\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\togepi\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\togepi\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\togetic\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\togetic\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\togetic\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\togetic\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\totodile\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\totodile\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\totodile\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\totodile\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\typhlosion\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\typhlosion\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\typhlosion\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\typhlosion\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\tyranitar\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tyranitar\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tyranitar\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tyranitar\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\tyrogue\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tyrogue\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tyrogue\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\tyrogue\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\umbreon\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\umbreon\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\umbreon\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\umbreon\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\unown_a\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_a\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_b\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_b\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_c\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_c\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_d\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_d\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_e\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_e\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_f\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_f\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_g\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_g\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_h\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_h\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_i\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_i\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_j\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_j\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_k\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_k\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_l\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_l\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_m\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_m\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_n\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_n\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_o\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_o\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_p\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_p\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_q\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_q\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_r\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_r\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_s\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_s\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_t\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_t\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_u\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_u\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_v\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_v\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_w\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_w\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_x\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_x\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_y\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_y\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_z\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown_z\front.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\unown\normal.pal` | 2 | Palette definition file. |
| `gfx\pokemon\unown\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\ursaring\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ursaring\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ursaring\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\ursaring\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\vaporeon\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\vaporeon\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\vaporeon\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\vaporeon\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\venomoth\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\venomoth\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\venomoth\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\venomoth\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\venonat\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\venonat\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\venonat\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\venonat\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\venusaur\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\venusaur\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\venusaur\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\venusaur\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\victreebel\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\victreebel\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\victreebel\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\victreebel\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\vileplume\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\vileplume\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\vileplume\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\vileplume\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\voltorb\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\voltorb\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\voltorb\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\voltorb\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\vulpix\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\vulpix\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\vulpix\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\vulpix\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\wartortle\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wartortle\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wartortle\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wartortle\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\weedle\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\weedle\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\weedle\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\weedle\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\weepinbell\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\weepinbell\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\weepinbell\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\weepinbell\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\weezing\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\weezing\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\weezing\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\weezing\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\wigglytuff\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wigglytuff\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wigglytuff\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wigglytuff\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\wobbuffet\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wobbuffet\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wobbuffet\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wobbuffet\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\wooper\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wooper\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wooper\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\wooper\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\xatu\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\xatu\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\xatu\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\xatu\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\yanma\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\yanma\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\yanma\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\yanma\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\zapdos\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\zapdos\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\zapdos\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\zapdos\shiny.pal` | 2 | Palette definition file. |
| `gfx\pokemon\zubat\back.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\zubat\front_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\zubat\front_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\pokemon\zubat\shiny.pal` | 2 | Palette definition file. |
| `gfx\printer\bold_a.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\printer\bold_b.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\printer\hp.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\printer\lv.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sgb\blk_packets.asm` | 82 | Graphics manifest / include file for this asset group. |
| `gfx\sgb\gold_border.bin` | n/a (binary) | UNCLEAR: graphics asset helper file. |
| `gfx\sgb\gold_border.pal` | 64 | Palette definition file. |
| `gfx\sgb\gold_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sgb\pal_packets.asm` | 70 | Graphics manifest / include file for this asset group. |
| `gfx\sgb\predef.pal` | 96 | Palette definition file. |
| `gfx\sgb\silver_border.bin` | n/a (binary) | UNCLEAR: graphics asset helper file. |
| `gfx\sgb\silver_border.pal` | 64 | Palette definition file. |
| `gfx\sgb\silver_border.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\slots\slots_1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\slots\slots_2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\slots\slots_3.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\slots\slots_gold.pal` | 64 | Palette definition file. |
| `gfx\slots\slots_silver.pal` | 64 | Palette definition file. |
| `gfx\slots\slots.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\splash\copyright.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\splash\gamefreak_logo.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\splash\gamefreak_presents.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\splash\logo_sparkle.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\splash\logo_star.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites.asm` | 97 | Graphics manifest / INCBIN table for overworld sprite assets. |
| `gfx\sprites\beauty.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\big_lapras.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\big_onix.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\big_snorlax.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\biker.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\bill.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\bird.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\black_belt.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\blaine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\blue.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\boulder.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\brock.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\bruno.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\bug_catcher.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\bugsy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\cal.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\captain.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\chris_bike.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\chris.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\chuck.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\clair.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\clerk.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\cooltrainer_f.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\cooltrainer_m.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\daisy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\dragon.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\elder.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\elm.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\erika.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\fairy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\falkner.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\famicom.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\fisher.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\fishing_guru.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\fruit_tree.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\gameboy_kid.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\gentleman.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\gold_trophy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\gramps.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\granny.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\gym_guide.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\janine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\jasmine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\karen.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\kimono_girl.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\koga.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\kurt.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\lance.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\lass.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\link_receptionist.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\misty.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\mom.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\monster.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\morty.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\n64.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\nurse.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\oak.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\officer.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\old_link_receptionist.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\paper.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\pharmacist.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\poke_ball.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\pokedex.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\pokefan_f.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\pokefan_m.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\pryce.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\receptionist.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\red.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\reds_mom.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\rival.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\rock.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\rocker.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\rocket_girl.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\rocket.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\sabrina.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\sage.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\sailor.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\scientist.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\silver_trophy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\slowpoke.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\snes.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\sudowoodo.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\super_nerd.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\surf.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\surfing_pikachu.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\surge.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\swimmer_girl.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\swimmer_guy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\teacher.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\twin.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\unused_guy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\virtual_boy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\whitney.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\will.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\sprites\youngster.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\stats\item.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\stats\mail.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\stats\pages.pal` | 15 | Palette definition file. |
| `gfx\stats\party_menu_ob.pal` | 32 | Palette definition file. |
| `gfx\stats\stats_tiles.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\stats\stats.pal` | 6 | Palette definition file. |
| `gfx\tileset_palette_maps.asm` | 72 | Tileset palette-map include file. |
| `gfx\tilesets.asm` | 183 | Tileset graphics manifest tying gfx, metatiles, and collision data together. |
| `gfx\tilesets\bg_tiles.pal` | 48 | Palette definition file. |
| `gfx\tilesets\cave_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\cave.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\champions_room_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\champions_room.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\dark_cave.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\elite_four_room_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\elite_four_room.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\facility_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\facility.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\flower\cgb_1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\flower\cgb_2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\flower\dmg_1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\flower\dmg_2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\forest_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\forest.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\game_corner_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\game_corner.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\gate_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\gate.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\house_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\house.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\ice_path_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\ice_path.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\johto_modern_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\johto_modern.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\johto_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\johto.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\kanto_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\kanto.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\lab_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\lab.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\lava\1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\lava\2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\lava\3.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\lava\4.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\lighthouse_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\lighthouse.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\mansion_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\mansion.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\mart_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\mart.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\park_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\park.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\players_house_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\players_house.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\players_room_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\players_room.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\pokecenter_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\pokecenter.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\port_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\port.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\radio_tower_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\radio_tower.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\roofs.pal` | 108 | Palette definition file. |
| `gfx\tilesets\roofs\azalea.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\roofs\goldenrod.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\roofs\new_bark.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\roofs\olivine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\roofs\violet.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\ruins_of_alph_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\ruins_of_alph.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\tower_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\tower-pillar\1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\tower-pillar\10.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\tower-pillar\2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\tower-pillar\3.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\tower-pillar\4.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\tower-pillar\5.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\tower-pillar\6.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\tower-pillar\7.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\tower-pillar\8.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\tower-pillar\9.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\tower.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\traditional_house_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\traditional_house.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\train_station_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\train_station.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\underground_palette_map.asm` | 12 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\underground.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\unused_museum_palette_map.asm` | 14 | Graphics manifest / include file for this asset group. |
| `gfx\tilesets\water\water.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\whirlpool\1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\whirlpool\2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\whirlpool\3.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\tilesets\whirlpool\4.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\title\hooh_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\title\logo_bottom_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\title\logo_bottom_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\title\logo_top_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\title\logo_top_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\title\logo.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\title\lugia_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\title\title_bg_gold.pal` | 20 | Palette definition file. |
| `gfx\title\title_bg_silver.pal` | 20 | Palette definition file. |
| `gfx\title\title_fg.pal` | 8 | Palette definition file. |
| `gfx\title\title_trail_gold.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\title\title_trail_silver.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trade\arrow_left.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trade\arrow_right.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trade\ball.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trade\border_tiles.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trade\bubble.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trade\cable.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trade\game_boy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trade\game_boy.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\trade\link_cable.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trade\link_cable.tilemap` | n/a (binary) | Tilemap data. |
| `gfx\trade\poof.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainer_card\badges.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainer_card\card_status.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainer_card\chris_card.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainer_card\leaders.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainer_card\trainer_card.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\beauty.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\biker.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\bird_keeper.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\blackbelt_t.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\blaine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\blue.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\boarder.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\brock.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\bruno.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\bug_catcher.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\bugsy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\burglar.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\cal.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\camper.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\champion.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\chuck.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\clair.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\cooltrainer_f.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\cooltrainer_m.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\erika.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\executive_f.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\executive_m.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\falkner.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\firebreather.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\fisher.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\gentleman.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\grunt_f.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\grunt_m.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\guitarist.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\hiker.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\janine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\jasmine.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\juggler.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\karen.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\kimono_girl.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\koga.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\lass.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\lt_surge.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\medium.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\misty.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\morty.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\oak.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\officer.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\picnicker.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\pokefan_f.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\pokefan_m.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\pokemaniac.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\pryce.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\psychic_t.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\red.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\rival1.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\rival2.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\sabrina.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\sage.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\sailor.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\schoolboy.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\scientist.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\skier.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\super_nerd.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\swimmer_f.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\swimmer_m.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\teacher.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\twins.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\whitney.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\will.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\trainers\youngster.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\unown_puzzle\aerodactyl.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\unown_puzzle\cursor.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\unown_puzzle\hooh.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\unown_puzzle\kabuto.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\unown_puzzle\omanyte.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\unown_puzzle\start_cancel.png` | n/a (binary) | Source PNG for this graphics asset. |
| `gfx\unown_puzzle\tile_borders.png` | n/a (binary) | Source PNG for this graphics asset. |

# 7. Text engine

Textbox/text-command execution lives in `home\text.asm`: `PrintText` sets up the textbox, `PlaceString` parses inline control characters, and `TextCommands` dispatches the higher-level `TX_*` commands defined by the text DSL (`home\text.asm:139-221`, `home\text.asm:615-700`). Shared print pacing / far-text helpers live in `home\print_text.asm`, and most reusable text content is stored under `data\text\` (`home\print_text.asm:1-41`, `home\print_text.asm:103-120`, `data\text\common.asm:1-13`, `data\text\common_1.asm:1-20`, `data\text\std_text.asm:1-40`).

| Path | ~Lines | Description |
|---|---:|---|
| `home\text.asm` | 852 | Core text-command interpreter and character renderer. |
| `home\print_text.asm` | 103 | Textbox setup, text delay, and far-text printing helpers. |
| `data\text\battle.asm` | 891 | Battle text data. |
| `data\text\common_1.asm` | 1496 | Common in-game text bank 1. |
| `data\text\common_2.asm` | 1287 | Common in-game text bank 2. |
| `data\text\common_3.asm` | 450 | Common in-game text bank 3. |
| `data\text\common.asm` | 6 | Banked text include that pulls in the common text banks. |
| `data\text\dakutens.asm` | 14 | Dakuten/handakuten text tables. |
| `data\text\mail_input_chars.asm` | 15 | Character-set data for mail input. |
| `data\text\name_input_chars.asm` | 27 | Character-set data for naming-screen input. |
| `data\text\std_text.asm` | 330 | Shared standard-script text strings. |
| `data\text\unused_gen1_trainer_names.asm` | 70 | Unused Gen 1 trainer-name text data. |
| `data\text\unused_sweet_honey.asm` | 53 | Unused Sweet Honey-related text data. |

# 8. Save system

The save UI and save-writing flow are in `engine\menus\save.asm`: the main menu prompt, overwrite checks, checksum writes, backup save writes, RTC staging, and save-after-link/box-save paths are all there (`engine\menus\save.asm:1-100`). The actual SRAM format is laid out in `ram\sram.asm`, which defines the main save block, backup blocks, checksums/check values, active box, Hall of Fame, link battle stats, mail, Mystery Gift data, and boxed Pokémon sections (`ram\sram.asm:1-160`).

| Path | ~Lines | Description |
|---|---:|---|
| `engine\menus\save.asm` | 992 | Primary save/checksum/backup/box-save logic. |
| `engine\menus\savemenu_copytilemapatonce.asm` | 74 | Save-menu tilemap copy helper. |
| `engine\menus\empty_sram.asm` | 18 | SRAM erase helper. |
| `engine\menus\delete_save.asm` | 31 | Delete-save menu flow. |
| `ram\sram.asm` | 124 | SRAM layout: save blocks, backups, mail, active box, Hall of Fame, and boxed mons. |

# 9. Link / trading

Link flow is anchored by `engine\link\link.asm`, which sets up the trade room UI, prepares party data, and runs serial byte exchange for link battle/trade rooms (`engine\link\link.asm:1-120`). Time Capsule compatibility is split into `time_capsule.asm` and `time_capsule_2.asm`, and Mystery Gift is split across `mystery_gift*.asm` plus a tiny graphics helper (`engine\link\time_capsule.asm:1-39`, `engine\link\time_capsule_2.asm:1-39`, `engine\link\mystery_gift.asm:25-120`).

| Path | ~Lines | Description |
|---|---:|---|
| `engine\link\init_list.asm` | 50 | Name/list pointer initialization helper used by link flows. |
| `engine\link\link.asm` | 2301 | Serial link, trade/colosseum room flow, and data exchange. |
| `engine\link\mystery_gift_2.asm` | 140 | Additional Mystery Gift helpers and follow-up transfer logic. |
| `engine\link\mystery_gift_3.asm` | 179 | Further Mystery Gift helpers. |
| `engine\link\mystery_gift_gfx.asm` | 28 | Mystery Gift graphics assets hooks. |
| `engine\link\mystery_gift.asm` | 1149 | Infrared Mystery Gift exchange flow. |
| `engine\link\place_waiting_text.asm` | 21 | Draws the standard "Waiting...!" link textbox. |
| `engine\link\time_capsule_2.asm` | 37 | Additional Time Capsule / backward-compatibility validation helpers. |
| `engine\link\time_capsule.asm` | 129 | Gen 2 ↔ Gen 1 Time Capsule conversion helpers. |

# 10. Items

The item system is split between runtime behavior and static data. `engine\items\item_effects.asm` dispatches item use, `engine\items\pack.asm` owns the bag UI/pockets, `engine\items\mart.asm` owns shops, and `engine\items\tmhm.asm` owns TM/HM teaching (`engine\items\item_effects.asm:1-120`, `engine\items\pack.asm:1-120`, `engine\items\mart.asm:27-120`, `engine\items\tmhm.asm:1-44`). Static item attributes and shop inventories live in `data\items\attributes.asm` and `data\items\marts.asm` (`data\items\attributes.asm:1-40`, `data\items\marts.asm:1-40`).

### `engine\\items\\`

| Path | ~Lines | Description |
|---|---:|---|
| `engine\items\buy_sell_toss.asm` | 193 | Quantity-selection UI for buying, selling, and tossing. |
| `engine\items\item_effects.asm` | 2483 | Item-effect dispatch table and field/battle item handlers. |
| `engine\items\items.asm` | 514 | Add/check/toss item wrappers and pocket routing. |
| `engine\items\mart.asm` | 713 | Mart dialog state machine and inventory loading. |
| `engine\items\pack.asm` | 1402 | Pack UI and pocket state machine. |
| `engine\items\print_item_description.asm` | 29 | Prints the current item description. |
| `engine\items\switch_items.asm` | 255 | Item-list reordering and stack-combining logic. |
| `engine\items\tmhm.asm` | 510 | TM/HM pocket UI and teaching flow. |
| `engine\items\tmhm2.asm` | 41 | TM/HM compatibility and move-number helpers. |
| `engine\items\update_item_description.asm` | 13 | Refreshes the description box for the selected item. |

### `data\\items\\`

| Path | ~Lines | Description |
|---|---:|---|
| `data\items\apricorn_balls.asm` | 10 | Item-related data or routines for apricorn balls. |
| `data\items\attributes.asm` | 526 | Per-item attribute table (price, pocket, permissions, menu behavior). |
| `data\items\bargain_shop.asm` | 8 | Item-related data or routines for bargain shop. |
| `data\items\catch_rate_items.asm` | 17 | Item-related data or routines for catch rate items. |
| `data\items\descriptions.asm` | 812 | Item description strings. |
| `data\items\fruit_trees.asm` | 34 | Item-related data or routines for fruit trees. |
| `data\items\heal_hp.asm` | 17 | Item-related data or routines for heal hp. |
| `data\items\heal_status.asm` | 18 | Item-related data or routines for heal status. |
| `data\items\mail_items.asm` | 12 | Item-related data or routines for mail items. |
| `data\items\marts.asm` | 372 | Mart inventory tables. |
| `data\items\mom_phone.asm` | 25 | Item-related data or routines for mom phone. |
| `data\items\mystery_gift_items.asm` | 39 | Item-related data or routines for mystery gift items. |
| `data\items\names.asm` | 262 | Item name strings. |
| `data\items\pocket_names.asm` | 12 | Pack pocket names. |
| `data\items\x_stats.asm` | 6 | Item-related data or routines for x stats. |

# 11. Home routines (ROM0)

These are the files folded into ROM0 by `home.asm` (`home.asm:6-60`). Categories below are functional buckets for quicker navigation.

### Interrupts, timing, and video

| Path | ~Lines | Description |
|---|---:|---|
| `home\header.asm` | 52 | Cartridge header and entry-point definitions. |
| `home\vblank.asm` | 326 | VBlank interrupt and per-frame main-loop handler. |
| `home\delay.asm` | 18 | Frame delay helpers. |
| `home\time_palettes.asm` | 17 | Time-of-day palette hooks. |
| `home\fade.asm` | 111 | Palette fade routines. |
| `home\lcd.asm` | 51 | LCD enable/disable/control helpers. |
| `home\time.asm` | 237 | Timekeeping utilities. |
| `home\init.asm` | 142 | Hardware/WRAM startup initialization. |
| `home\game_time.asm` | 83 | Game-clock helper routines. |
| `home\palettes.asm` | 245 | Palette copy/apply helpers. |
| `home\gfx.asm` | 230 | VRAM graphics loading/copy helpers. |
| `home\video.asm` | 364 | BG map anchoring and font-load video helpers. |
| `home\sprite_updates.asm` | 18 | Sprite-update enable/disable helpers. |
| `home\clear_sprites.asm` | 22 | Clears sprite/OAM state. |
| `home\copy_tilemap.asm` | 19 | Tilemap copy helpers. |
| `home\tilemap.asm` | 190 | Textbox and tilemap drawing helpers. |
| `home\window.asm` | 81 | Window stack/background window helpers. |

### Input, serial, printer, and audio

| Path | ~Lines | Description |
|---|---:|---|
| `home\serial.asm` | 366 | Serial/link-byte helpers. |
| `home\joypad.asm` | 411 | Joypad input reading and debouncing. |
| `home\printer.asm` | 32 | Game Boy Printer wrappers. |
| `home\audio.asm` | 424 | ROM0 audio wrapper routines. |
| `home\sram.asm` | 22 | SRAM open/close wrappers. |

### Text, menus, and string formatting

| Path | ~Lines | Description |
|---|---:|---|
| `home\text.asm` | 852 | Text command interpreter and character renderer. |
| `home\print_text.asm` | 103 | Textbox setup, text delay, and far-text printing helpers. |
| `home\print_num.asm` | 309 | Number printing helpers. |
| `home\print_bcd.asm` | 78 | BCD printing helpers. |
| `home\menu.asm` | 710 | Menu/cursor/window wrappers. |
| `home\scrolling_menu.asm` | 52 | Scrolling-menu wrappers. |
| `home\names.asm` | 222 | Name lookup routines. |
| `home\copy_name.asm` | 12 | Fixed-length name-copy helpers. |
| `home\string.asm` | 33 | String helpers. |

### Map / overworld / scripts

| Path | ~Lines | Description |
|---|---:|---|
| `home\map_objects.asm` | 549 | Trainer sight and map-object interaction helpers. |
| `home\movement.asm` | 119 | Movement/facing helpers. |
| `home\map.asm` | 2348 | Map rendering and scene helpers. |
| `home\queue_script.asm` | 11 | Script queue setter. |
| `home\stone_queue.asm` | 117 | Checks stone/warp-triggered object scripts and queues them. |
| `home\flag.asm` | 110 | Bitflag action helpers. |
| `home\region.asm` | 86 | Region checks and XY compare-flag helpers. |

### Pokémon, battle, items, and trainers

| Path | ~Lines | Description |
|---|---:|---|
| `home\battle.asm` | 212 | Battle/Pokémon UI helpers in ROM0. |
| `home\battle_vars.asm` | 101 | Side-neutral battle-variable accessors. |
| `home\item.asm` | 60 | Banked item add/check/toss wrappers. |
| `home\hm_moves.asm` | 22 | HM and HM-move checks. |
| `home\pokedex_flags.asm` | 56 | Seen/caught Pokédex flag helpers. |
| `home\pokemon.asm` | 244 | Mon frontpic/HP bar/stat helpers. |
| `home\trainers.asm` | 216 | Trainer battle detection/helpers. |

### Low-level helpers and math

| Path | ~Lines | Description |
|---|---:|---|
| `home\farcall.asm` | 27 | Far-call trampoline helpers. |
| `home\predef.asm` | 41 | Predef dispatch helpers. |
| `home\call_regs.asm` | 6 | Small register-call helper shims. |
| `home\copy.asm` | 61 | Byte copy/fill helpers. |
| `home\array.asm` | 39 | Array/list helper routines. |
| `home\compare.asm` | 30 | Comparison helpers. |
| `home\decompress.asm` | 252 | Decompression entrypoints. |
| `home\math.asm` | 51 | Math helper routines. |
| `home\random.asm` | 61 | RNG helpers. |
| `home\sine.asm` | 9 | Sine helper routine. |
| `home\sprite_anims.asm` | 22 | Sprite animation wrappers. |

# 12. Constants

Each constants file opens with the struct/table/comment context it serves (examples: `constants\item_constants.asm:1-6`, `constants\map_data_constants.asm:1-12`, `constants\trainer_constants.asm:8-12`, `constants\hardware.inc:1-11`). Categories below are purely navigational.

### Audio and sound

| Path | ~Lines | Description |
|---|---:|---|
| `constants\audio_constants.asm` | 119 | Note/pitch/audio command constants. |
| `constants\cry_constants.asm` | 73 | Pokémon cry IDs. |
| `constants\music_constants.asm` | 107 | Music track IDs. |
| `constants\sfx_constants.asm` | 191 | Sound-effect IDs. |

### Battle and animation

| Path | ~Lines | Description |
|---|---:|---|
| `constants\battle_constants.asm` | 241 | Battle-system constants. |
| `constants\battle_anim_constants.asm` | 844 | Battle animation object/frame constants. |
| `constants\move_constants.asm` | 289 | Move IDs. |
| `constants\move_effect_constants.asm` | 160 | Move-effect IDs. |
| `constants\type_constants.asm` | 34 | Pokémon type IDs and matchup constants. |
| `constants\scgb_constants.asm` | 150 | SGB/CGB layout IDs. |

### Maps, overworld, sprites, and environment

| Path | ~Lines | Description |
|---|---:|---|
| `constants\collision_constants.asm` | 133 | Map collision/tile-permission constants. |
| `constants\deco_constants.asm` | 175 | Decoration data constants. |
| `constants\engine_flags.asm` | 112 | Persistent engine-flag IDs. |
| `constants\event_flags.asm` | 1323 | Event-flag IDs. |
| `constants\landmark_constants.asm` | 105 | Landmark IDs. |
| `constants\map_constants.asm` | 454 | Map-group/map IDs and map-definition helpers. |
| `constants\map_data_constants.asm` | 126 | Map-struct field constants. |
| `constants\map_object_constants.asm` | 272 | Overworld object-struct constants. |
| `constants\map_setup_constants.asm` | 25 | Map setup-script IDs. |
| `constants\sprite_anim_constants.asm` | 285 | Sprite-animation struct constants. |
| `constants\sprite_constants.asm` | 159 | Overworld sprite IDs. |
| `constants\sprite_data_constants.asm` | 34 | Sprite-data struct constants. |
| `constants\tileset_constants.asm` | 46 | Tileset IDs. |

### Items, menus, radio/phone, printer, and link

| Path | ~Lines | Description |
|---|---:|---|
| `constants\icon_constants.asm` | 48 | Menu icon IDs. |
| `constants\item_constants.asm` | 295 | Item IDs and indexes. |
| `constants\item_data_constants.asm` | 119 | Item-attribute struct constants. |
| `constants\mart_constants.asm` | 44 | Mart-type and mart IDs. |
| `constants\menu_constants.asm` | 117 | Menu flag and menu-option constants. |
| `constants\npc_trade_constants.asm` | 42 | NPC-trade struct constants. |
| `constants\phone_constants.asm` | 68 | Phone contact IDs. |
| `constants\printer_constants.asm` | 18 | Printer status constants. |
| `constants\radio_constants.asm` | 94 | Radio channel IDs. |
| `constants\serial_constants.asm` | 44 | Link-mode/serial constants. |

### Pokémon, trainers, RAM, and scripts

| Path | ~Lines | Description |
|---|---:|---|
| `constants\pokemon_constants.asm` | 307 | Species IDs and related indexes. |
| `constants\pokemon_data_constants.asm` | 195 | Pokémon base-data struct constants. |
| `constants\ram_constants.asm` | 301 | WRAM/HRAM state constants. |
| `constants\script_constants.asm` | 274 | Script engine/object/text-buffer constants. |
| `constants\trainer_constants.asm` | 585 | Trainer class IDs. |
| `constants\trainer_data_constants.asm` | 37 | Trainer attribute constants. |

### Low-level / text / misc

| Path | ~Lines | Description |
|---|---:|---|
| `constants\charmap.asm` | 377 | Text character encoding map. |
| `constants\credits_constants.asm` | 88 | Credits string IDs. |
| `constants\gfx_constants.asm` | 18 | Graphics/tile geometry constants. |
| `constants\hardware.inc` | 869 | Game Boy hardware register/bit definitions. |
| `constants\misc_constants.asm` | 49 | Miscellaneous booleans/input/gender constants. |
| `constants\text_constants.asm` | 42 | Text/name-length and GetName constants. |

# 13. Macros

The macro layer is split between general assembler helpers and purpose-built DSLs. The event/map/text/audio DSLs are declared in `macros\scripts\*.asm` and are consumed directly by the runtime dispatch tables in files such as `engine\overworld\scripting.asm`, `engine\overworld\movement.asm`, `home\text.asm`, and `audio\engine.asm` (`macros\scripts\events.asm:1-15`, `macros\scripts\maps.asm:12-80`, `macros\scripts\movement.asm:1-40`, `macros\scripts\text.asm:1-40`, `macros\scripts\audio.asm:1-20`).

### General assembler helpers

| Path | ~Lines | Description |
|---|---:|---|
| `macros\asserts.asm` | 76 | Assertion and label-sanity macros. |
| `macros\code.asm` | 84 | General code-structuring/syntactic-sugar macros. |
| `macros\const.asm` | 42 | Constant-enumeration macros. |
| `macros\coords.asm` | 63 | Coordinate/register helper macros. |
| `macros\data.asm` | 128 | Data-declaration/value helper macros. |
| `macros\farcall.asm` | 23 | Far-call macros. |
| `macros\gfx.asm` | 58 | Graphics/palette helper macros. |
| `macros\legacy.asm` | 469 | Legacy compatibility macros for older pokegold/pokecrystal syntax. |
| `macros\predef.asm` | 14 | Predef-call macros. |
| `macros\ram.asm` | 360 | RAM struct and layout macros. |
| `macros\vc.asm` | 25 | Virtual Console patch/hook macros. |

### DSLs for scripts, text, audio, and animation

| Path | ~Lines | Description |
|---|---:|---|
| `macros\scripts\audio.asm` | 270 | DSL macros for music, SFX, notes, and channel scripts. |
| `macros\scripts\battle_anims.asm` | 252 | DSL macros for battle animation scripts. |
| `macros\scripts\battle_commands.asm` | 185 | DSL constants/macros for battle effect-command scripts. |
| `macros\scripts\events.asm` | 852 | DSL macros for overworld event scripts. |
| `macros\scripts\maps.asm` | 177 | DSL macros for map headers, scenes, callbacks, warps, objects, and BG events. |
| `macros\scripts\movement.asm` | 173 | DSL macros for movement scripts. |
| `macros\scripts\oam_anims.asm` | 33 | DSL macros for OAM/sprite animation scripts. |
| `macros\scripts\text.asm` | 136 | DSL macros for text scripts and text commands. |

