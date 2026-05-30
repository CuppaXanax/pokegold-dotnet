# Execution flow

This note traces execution from cartridge entry through the overworld/battle/script loops, plus all interrupt and RST handling. Citations point at the disassembly sources.

## 1. Reset vector and boot sequence

### What is at `$0000`?
- `rst0` is a tiny trampoline: `di` then `jp Start`. It is *not* the normal post-boot entry; it exists at the reset vector and can also serve as a hard reset trampoline. (`layout.link:1-31`, `home\header.asm:3-5`)

### Where does execution begin after the boot ROM?
- The actual cartridge entry point is the header entry at `$0100`: `Start:: nop` / `jp _Start`. The comment explicitly says Nintendo requires this exact shape. (`home\header.asm:59-70`)
- `_Start` checks register `A` against `BOOTUP_A_CGB = $11` to detect CGB/AGB boot, writes the result to `hCGB`, and falls into `Init`. (`constants\hardware.inc:1033-1043`, `home\init.asm:16-28`)

### `Init` step-by-step
1. Disable interrupts and clear interrupt enables/flags plus a large set of hardware registers and startup variables (`rIF`, `rIE`, `rRP`, scroll/window regs, serial regs, DMG palettes, timer regs, `wBetaTitleSequenceOpeningType`). (`home\init.asm:28-47`)
2. Start the timer at 4096 Hz by writing `%100` to `rTAC`. (`home\init.asm:48-49`)
3. Wait until `LY == LY_VBLANK + 1`, then turn LCD off. (`home\init.asm:51-58`)
4. Clear all WRAM0+WRAMX, set `sp = wStackTop`, clear VRAM, clear HRAM, and preserve/restore `hCGB` across the HRAM wipe. (`home\init.asm:59-83`)
5. Clear sprite/OAM shadow state with `ClearSprites`. (`home\init.asm:84`, `home\clear_sprites.asm:1-7`)
6. Bank-switch to the bank containing `WriteOAMDMACodeToHRAM` and copy the OAM DMA stub into HRAM. (`home\init.asm:86-89`, `engine\gfx\load_push_oam.asm:1-29`)
7. Initialize runtime mirrors/state: zero `hMapAnims`, `hSCX`, `hSCY`, `rJOYP`; set `rSTAT = STAT_MODE_0`; initialize window position; set serial state to `CONNECTION_NOT_ESTABLISHED`. (`home\init.asm:91-109`, `constants\serial_constants.asm:23-25`)
8. Blank both BG maps, initialize CGB palettes, and point `hBGMapAddress` at `vBGMap1`. (`home\init.asm:111-121`)
9. Start RTC/clock handling with `StartClock`. That routine latches RTC data, fixes day overflow, records RTC status if needed, then starts the RTC. (`home\init.asm:123`, `engine\rtc\rtc.asm:91-101`)
10. Briefly enable SRAM, unlatch/disable RTC/SRAM, then turn the LCD back on with `LCDC_DEFAULT`. (`home\init.asm:125-140`, `constants\ram_constants.asm:354-358`)
11. Enable the default interrupt mask `IE_DEFAULT`, execute `ei`, and wait one frame. (`home\init.asm:142-146`, `constants\ram_constants.asm:357-358`, `home\delay.asm:1-20`)
12. Initialize the SGB border through the predef system, initialize sound, clear `wMapMusic`, and jump to `GameInit`. (`home\init.asm:148-153`)
13. `GameInit` clears window data, tries to load save data, then jumps into `IntroSequence`. (`engine\menus\intro_menu.asm:1156-1159`)

### Soft reset path
- `UpdateJoypad` treats `A+B+Select+Start` as soft reset and jumps to `Reset`. (`home\joypad.asm:99-104`)
- `Reset` re-initializes sound/palettes, enables interrupts, disables SGB transfer via `wJoypadDisable`, waits 32 frames, then jumps back into `Init`. (`home\init.asm:1-14`)

## 2. RST vectors

| Address | Symbol | Behavior | Notes |
|---|---|---|---|
| `$0000` | `rst0` | `di; jp Start` | reset trampoline to the `$0100` header entry. (`home\header.asm:3-5`) |
| `$0008` | `FarCall` | `jp FarCall_hl` | banked subroutine call entry. (`home\header.asm:7-9`, `home\farcall.asm:1-31`) |
| `$0010` | `Bankswitch` | write `A` to `hROMBank` and `rROMB`, then `ret` | fast bank switch helper. (`home\header.asm:11-15`) |
| `$0018` | `rst18` | `rst $38` | trap/invalid entry. (`home\header.asm:17-18`) |
| `$0020` | `rst20` | `rst $38` | trap/invalid entry. (`home\header.asm:20-21`) |
| `$0028` | `JumpTable` | indexed `jp hl` via 16-bit pointer table | common switch/jumptable helper. (`home\header.asm:23-35`) |
| `$0038` | `rst38` | `rst $38` | self-loop trap. (`home\header.asm:37-38`) |

- There is **no separate `rst30` section** in `layout.link`; `JumpTable`’s code physically occupies the `$0028-$0035` range, so `$0030` is not a distinct callable entry here. (`layout.link:5-17`, `home\header.asm:23-35`)

## 3. Interrupt vectors

### Vector map
- `$0040`: `jp VBlank` (`home\header.asm:43-44`)
- `$0048`: `jp LCD` (`home\header.asm:46-47`)
- `$0050`: `reti` (timer vector does nothing) (`home\header.asm:49-50`)
- `$0058`: `jp Serial` (`home\header.asm:52-53`)
- `$0060`: `jp Joypad` (`home\header.asm:55-56`)

### VBlank (`$0040`)
- `VBlank` saves registers, uses `hVBlank` to select one of 8 handlers from `VBlankHandlers`, runs it, then calls `GameTimer`, restores registers, and `reti`s. (`home\vblank.asm:9-38`, `constants\ram_constants.asm:341-352`, `home\game_time.asm:11-106`)
- Handler IDs:
  - `0 = VBLANK_NORMAL`
  - `1 = VBLANK_CUTSCENE`
  - `2 = VBLANK_SOUND_ONLY`
  - `4 = VBLANK_SERIAL`
  - `5 = VBLANK_CREDITS` (`home\vblank.asm:40-51`, `constants\ram_constants.asm:343-352`)
- Examples of mode switches:
  - battle transition sets `VBLANK_CUTSCENE` (`engine\battle\battle_transition.asm:20-31`)
  - Magnet Train sets `VBLANK_CUTSCENE` (`engine\events\magnet_train.asm:31-43`)
  - printer code sets `VBLANK_SERIAL` (`engine\printer\printer.asm:71-79`, `engine\printer\printer.asm:146-157`)
  - credits sets `VBLANK_CREDITS` (`engine\movie\credits.asm:77-96`)
  - some link timeout checks temporarily use `VBLANK_SOUND_ONLY` (`engine\link\link.asm:2240-2247`, `engine\link\link.asm:2262-2268`)

### LCD STAT (`$0048`)
- `LCD` checks `hLCDCPointer`; if nonzero, it indexes `wLYOverrides` by current `LY` and writes that value to the hardware register selected by `hLCDCPointer` (for example, title code sets it to `LOW(rSCX)`). (`home\lcd.asm:3-23`, `engine\movie\title.asm:81-89`)
- `Init` sets `rSTAT = STAT_MODE_0`, so the LCD interrupt is configured for mode-0/HBlank behavior when enabled. (`home\init.asm:97-98`)
- `VBlank_Cutscene`, `VBlank_Credits`, and `VBlank_Unused` temporarily replace `rIE` with `IE_STAT`, request `IF_STAT`, and then restore `IE_DEFAULT` after sound. This is how per-scanline scroll effects are driven during cutscenes/credits. (`home\vblank.asm:175-210`, `home\vblank.asm:287-306`, `home\vblank.asm:383-406`)
- The handler is used by title/intro/battle-transition/credits/magnet-train effects that manipulate `wLYOverrides`/`hLCDCPointer`. (`engine\movie\title.asm:81-89`, `engine\movie\intro.asm:127-151`, `engine\battle\battle_transition.asm:20-25`, `engine\movie\credits.asm:66-80`, `engine\events\magnet_train.asm:31-43`)

### Timer (`$0050`)
- The timer interrupt vector is a bare `reti`. (`home\header.asm:49-50`)
- There is also an unreferenced `Timer:: reti` in the RTC code. (`engine\rtc\rtc.asm:1-4`)
- `Init` still starts the timer and later enables `IE_TIMER` via `IE_DEFAULT`. **UNCLEAR:** this code never gives the timer interrupt any gameplay logic; it appears intentionally unused. (`home\init.asm:48-49`, `home\init.asm:142-144`, `constants\ram_constants.asm:357-358`)

### Serial (`$0058`)
- `Serial` is a real ISR. It either hands control to printer receive code, establishes link-clock ownership, or exchanges one byte by moving `rSB` into `hSerialReceive` and queueing the next transmit byte from `hSerialSend`. It ends by setting `hSerialReceivedNewData = TRUE`. (`home\serial.asm:1-81`)
- Higher-level link routines (`Serial_ExchangeByte`, `LinkTransfer`, etc.) poll the flags set by the ISR. (`home\serial.asm:122-229`, `home\serial.asm:284-399`)

### Joypad (`$0060`)
- `Joypad` is just a placeholder `reti`; the file comment says real input handling was replaced by `UpdateJoypad` in VBlank. (`home\joypad.asm:1-6`)
- `UpdateJoypad` runs every VBlank and fills `hJoypadReleased/Pressed/Down/Sum`; overworld/gameplay code later copies that into the mirrored `hJoyReleased/Pressed/Down` via `GetJoypad`. (`home\joypad.asm:16-104`, `home\joypad.asm:106-162`)
- **UNCLEAR:** `IE_DEFAULT` still includes `IE_JOYPAD`, even though the ISR is a dummy. (`constants\ram_constants.asm:357-358`)

## 4. Initialization hand-off

- `Init` ends with `jp GameInit`. (`home\init.asm:150-153`)
- `GameInit` does only three things: clear window state, try loading save data, and jump to `IntroSequence`. (`engine\menus\intro_menu.asm:1156-1159`)
- `IntroSequence` runs splash + intro movie, then falls into `StartTitleScreen`. (`engine\menus\intro_menu.asm:852-860`)

## 5. Main game loop / top-level flow

### Title / intro / main menu
- `IntroSequence`:
  - `callfar SplashScreen`
  - if carry, skip straight to title
  - else `callfar GoldSilverIntro`
  - then `StartTitleScreen`. (`engine\menus\intro_menu.asm:852-860`)
- `StartTitleScreen` calls `TitleScreen`, then repeatedly calls `RunTitleScreen` until it returns carry. The selected title option is then dispatched via a small table:
  - `0 -> MainMenu`
  - `1 -> DeleteSaveData`
  - `2/3 -> IntroSequence`
  - `4 -> ResetClock` (`engine\menus\intro_menu.asm:859-897`)
- `MainMenu` loops until the player picks an entry; `B` quits back to `StartTitleScreen`. (`engine\menus\main_menu.asm:17-49`)

### New Game / Continue hand-off to overworld
- `NewGame` resets WRAM/game data, runs Oak speech and world initialization, sets `wDefaultSpawnpoint = SPAWN_HOME`, sets `hMapEntryMethod = MAPSETUP_WARP`, then jumps to `FinishContinueFunction`. (`engine\menus\intro_menu.asm:1-14`, `engine\menus\intro_menu.asm:22-138`, `engine\menus\intro_menu.asm:219-223`)
- `Continue` loads the save, confirms RTC state, updates roamers/mystery gift/clock, then normally sets `hMapEntryMethod = MAPSETUP_CONTINUE` and jumps to `FinishContinueFunction`. If loading after Hall of Fame/Red, it may rewrite the spawnpoint and use `MAPSETUP_WARP` instead. (`engine\menus\intro_menu.asm:251-311`)
- `FinishContinueFunction` is the real top-level gameplay loop: it enables the game timer, `farcall OverworldLoop`, and when that loop returns it usually jumps to `Reset` (title reset). The one special case is post-Red credits, where it rewrites the spawn and re-enters the loop. (`engine\menus\intro_menu.asm:343-357`)

### Overworld exploration
- `OverworldLoop` is the top-level overworld state machine. `wMapStatus` drives four states:
  - `MAPSTATUS_START`
  - `MAPSTATUS_ENTER`
  - `MAPSTATUS_HANDLE`
  - `MAPSTATUS_DONE` (`engine\overworld\events.asm:3-22`, `constants\ram_constants.asm:169-179`)
- `StartMap` clears map/script state and jumps into `EnterMap`. (`engine\overworld\events.asm:98-106`)
- `EnterMap` runs the map-setup script selected by `hMapEntryMethod`, then moves to `MAPSTATUS_HANDLE`. (`engine\overworld\events.asm:107-131`, `engine\overworld\map_setup.asm:1-15`, `data\maps\setup_scripts.asm:1-185`)
- `HandleMap` is the steady-state overworld loop. (`engine\overworld\events.asm:138-153`)

### Battle
- Scripted battles are launched by `Script_startbattle`, which buffers the current screen and calls `predef StartBattle`. (`engine\overworld\scripting.asm:1065-1071`)
- After battle, map scripts usually call `reloadmapafterbattle`, which interprets win/loss flags, possibly queues follow-up scripts, then sets `hMapEntryMethod = MAPSETUP_RELOADMAP` and `wMapStatus = MAPSTATUS_ENTER`. (`engine\overworld\scripting.asm:1080-1116`)
- `StartBattle` does battle setup, calls `DoBattle`, then `ExitBattle`, restores time-of-day palette state, and returns with carry set. (`engine\battle\core.asm:7749-7819`, `engine\battle\core.asm:7959-8051`)

### Menu screens
- The overworld START-button menu is not a separate top-level mode variable; it is started as a map script (`StartMenuScript`) from overworld input processing. (`engine\overworld\events.asm:802-852`)
- `StartMenu` runs its own menu loop and returns either to overworld directly or via queued script/asm callbacks using `hMenuReturn`. (`engine\menus\start_menu.asm:13-93`, `engine\overworld\events.asm:840-852`)
- Returning from submenus typically uses `MAPSETUP_SUBMENU` / `RunMapSetupScript` or `ReanchorMap` to redraw the current map. (`home\map.asm:208-214`, `home\window.asm:1-14`, `data\maps\setup_scripts.asm:181-184`)

### Credits / Hall of Fame
- `Script_halloffame` pauses the game timer, runs `HallOfFame`, resumes the timer, then falls into `ReturnFromCredits`. (`engine\overworld\scripting.asm:2207-2213`)
- `Script_credits` runs `RedCredits`, then `ReturnFromCredits`. (`engine\overworld\scripting.asm:2215-2221`)
- `ReturnFromCredits` does `Script_endall`, sets `wMapStatus = MAPSTATUS_DONE`, and stops the script, which causes `OverworldLoop` to return to `FinishContinueFunction`. (`engine\overworld\scripting.asm:2217-2221`, `engine\overworld\events.asm:10-14`)
- `HallOfFame` and `RedCredits` both end by jumping to `Credits`. (`engine\events\halloffame.asm:3-33`, `engine\events\halloffame.asm:35-53`, `engine\movie\credits.asm:6-105`)

## 6. VBlank handler details (critical frame order)

### `VBlank_Normal` exact order
1. Increment `hVBlankCounter`. (`home\vblank.asm:67-69`)
2. Advance RNG (`hRandomAdd`, `hRandomSub`) from `rDIV`. (`home\vblank.asm:71-82`)
3. Save current ROM bank in `wROMBankBackup`. (`home\vblank.asm:84-85`)
4. Copy scroll/window mirrors (`hSCX/hSCY/hWY/hWX`) to hardware regs. (`home\vblank.asm:87-94`)
5. Run **at most one** of these high-priority VRAM jobs, in this order:
   - `UpdateBGMapBuffer`
   - else `UpdatePalsIfCGB`
   - else `UpdateBGMap` (`home\vblank.asm:96-104`)
6. Run the timing-checked graphics helpers:
   - `Serve2bppRequest`
   - `Serve1bppRequest`
   - `AnimateTileset`
   - `FillBGMap0WithBlack` (`home\vblank.asm:105-110`)
7. If `hOAMUpdate == 0`, call `hTransferShadowOAM` (the HRAM OAM DMA stub copied during init). (`home\vblank.asm:114-118`, `engine\gfx\load_push_oam.asm:13-27`)
8. Clear `wVBlankOccurred` so `DelayFrame` can resume. (`home\vblank.asm:120-124`, `home\delay.asm:1-13`)
9. Decrement `wOverworldDelay` if nonzero. (`home\vblank.asm:125-130`)
10. Decrement `wTextDelayFrames` if nonzero. (`home\vblank.asm:132-137`)
11. Poll hardware input with `UpdateJoypad`. (`home\vblank.asm:139`, `home\joypad.asm:16-104`)
12. Bank-switch to `_UpdateSound`, run the sound engine tick, then restore the old ROM bank. (`home\vblank.asm:141-145`)
13. Copy `hSeconds` into `hUnusedBackup`. (`home\vblank.asm:147-148`)
14. Return to the common VBlank epilogue, which calls `GameTimer` and then `reti`. (`home\vblank.asm:31-38`, `home\game_time.asm:11-106`)

### Notes
- The comment is explicit that only one of `UpdateBGMapBuffer` / palette upload / full BG map update fits in a given VBlank, and priority is fixed in that order. (`home\vblank.asm:96-103`)
- OAM DMA is skipped when `hOAMUpdate != 0`; many menu/cutscene routines temporarily set that flag. (`home\vblank.asm:114-118`, `home\joypad.asm:303-311`)
- There is **no RTC latch/read inside VBlank**. Actual RTC reads happen in `UpdateTime`, which calls `GetClock`, `FixDays`, `FixTime`, and `GetTimeOfDay` outside the interrupt path. The only VBlank-time clock-related action is the final `GameTimer` frame counter increment. (`engine\rtc\rtc.asm:14-19`, `home\vblank.asm:31-38`, `home\game_time.asm:14-41`)

### Other VBlank modes
- `VBlank_Cutscene`: scroll mirrors -> `UpdatePals` -> `UpdateBGMap` -> `Serve2bppRequest` -> OAM DMA -> clear `wVBlankOccurred` -> temporarily hand control to LCD STAT -> sound -> restore `IE_DEFAULT`. (`home\vblank.asm:152-211`)
- `VBlank_Serial`: `UpdateBGMap` -> `Serve2bppRequest` -> OAM DMA -> `UpdateJoypad` -> clear `wVBlankOccurred` -> `AskSerial` -> sound. (`home\vblank.asm:231-260`, `home\printer.asm:5-41`)
- `VBlank_Credits`: SCX -> palette upload -> BG map -> `Serve2bppRequest` -> clear `wVBlankOccurred` -> `UpdateJoypad` -> temporarily enable LCD STAT -> sound. (`home\vblank.asm:262-307`)
- `VBlank_SoundOnly`: just sound, clear `wVBlankOccurred`, return. (`home\vblank.asm:309-324`)

## 7. Per-frame / per-iteration overworld execution order

The overworld does **not** run all gameplay inside VBlank. Instead, the main loop does game logic in `HandleMap`, then waits for VBlank(s) with `DelayFrames`.

### Overworld iteration order (`HandleMap`)
1. `ResetOverworldDelay` sets `wOverworldDelay = 2`. (`engine\overworld\events.asm:138-140`, `engine\overworld\events.asm:175-181`)
2. `HandleMapTimeAndJoypad`:
   - `UpdateTime`
   - `GetJoypad`
   - `TimeOfDayPals` (`engine\overworld\events.asm:191-199`)
3. `HandleCmdQueue` runs queued map commands. (`engine\overworld\events.asm:141`, `engine\overworld\cmd_queue.asm:1-24`)
4. `MapEvents` runs if `wMapEventStatus == MAPEVENTS_ON`:
   - `PlayerEvents`
   - `DisableEvents`
   - `ScriptEvents` (`engine\overworld\events.asm:155-173`)
5. `PlayerEvents` checks events in this exact order:
   - trainer sight
   - tile/warp/coord/wild-step events
   - queued reentry memory script
   - scene script
   - time events
   - raw overworld input (`OWPlayerInput`, including A/START/SELECT actions) (`engine\overworld\events.asm:238-275`)
6. If still on the same map status, update movement/object state:
   - `HandleNPCStep`
   - `_HandlePlayerStep`
   - `_CheckObjectEnteringVisibleRange` (`engine\overworld\events.asm:145-153`, `engine\overworld\events.asm:201-206`)
7. `NextOverworldFrame` waits `wOverworldDelay` frames via `DelayFrames`; each `DelayFrame` halts until VBlank clears `wVBlankOccurred`. (`engine\overworld\events.asm:183-189`, `home\delay.asm:1-20`)
8. After the wait, update background/sprites:
   - `_UpdateSprites`
   - `ScrollScreen` (`engine\overworld\events.asm:207-210`)
9. `CheckPlayerState` decides whether map events stay enabled next iteration. (`engine\overworld\events.asm:212-229`)

### Important input distinction
- `UpdateJoypad` (VBlank) reads hardware into `hJoypad*`. (`home\joypad.asm:16-104`)
- `GetJoypad` (main loop) mirrors that into `hJoy*`, or substitutes auto-input streams. Overworld/button logic reads the mirrored set. (`home\joypad.asm:106-162`)

## 8. Battle loop

### Battle startup
- `StartBattle` stops map anims, clears battle temp state, plays battle music, disables overworld sprite updates, initializes enemy data/display, shows the intro message, then calls `DoBattle`. (`engine\battle\core.asm:7757-7815`)

### `DoBattle`
- Initializes participant masks and `wBattleEnded`, chooses the first live enemy battler, handles early enemy switch/setup for trainer battles, waits 40 frames, validates the player has a usable mon, sends out the player mon, applies entry hazards, and jumps into `BattleTurn`. (`engine\battle\core.asm:3-113`)

### `BattleTurn` loop
1. Check contest-end condition. (`engine\battle\core.asm:146-150`, `engine\battle\core.asm:515-531`)
2. Clear per-turn flags (`wPlayerIsSwitching`, `wEnemyIsSwitching`, `wBattleHasJustStarted`, frozen flags, damage accumulator). (`engine\battle\core.asm:151-159`)
3. Run `HandleBerserkGene`, update the player’s party copy, and let AI pick an enemy move with `AIChooseMove`. (`engine\battle\core.asm:160-162`)
4. If the player is not locked into a move, open `BattleMenu`; if battle/run/forced-switch ended the fight, leave. (`engine\battle\core.asm:163-174`, `engine\battle\core.asm:4635-5032`)
5. `ParsePlayerAction` resolves the player’s chosen action. For ordinary move use, it runs `MoveSelectionScreen`, loads move data, and then calls `ParseEnemyAction`. (`engine\battle\core.asm:175-177`, `engine\battle\core.asm:558-642`, `engine\battle\core.asm:5045-5225`, `engine\battle\core.asm:5479-5555`)
6. If the enemy flees/forfeits, exit. (`engine\battle\core.asm:178-180`, `engine\battle\core.asm:377-392`)
7. `DetermineMoveOrder` decides who acts first using, in order:
   - action class (switch/item special cases in link battles)
   - move priority (`CompareMovePriority`)
   - Quick Claw checks
   - speed comparison
   - speed-tie RNG. (`engine\battle\core.asm:181-187`, `engine\battle\core.asm:394-513`, `engine\battle\core.asm:767-807`)
8. Execute either `Battle_EnemyFirst` or `Battle_PlayerFirst`. These wrappers also let the AI switch/use an item, apply residual damage after each side acts, refresh HUDs, and handle faints/forced switches between actions. (`engine\battle\core.asm:821-903`)
9. If battle still continues, run end-of-turn processing via `HandleBetweenTurnEffects`. (`engine\battle\core.asm:188-200`, `engine\battle\core.asm:205-251`)
10. Loop. (`engine\battle\core.asm:200`)

### What actually executes a move?
- `DoPlayerTurn` / `DoEnemyTurn` first run `CheckTurn` (status/recharge/flinch/disable/etc. gating), then `UpdateMoveData`, then `DoMove`. (`engine\battle\effect_commands.asm:1-21`, `engine\battle\effect_commands.asm:22-51`, `engine\battle\effect_commands.asm:121-240`)
- `DoMove` is itself a bytecode interpreter:
  - read move effect ID
  - fetch that move’s effect script from `MoveEffectsPointers`
  - copy commands into `wBattleScriptBuffer`
  - execute command opcodes through `BattleCommandPointers` until `endmove_command`/`endturn_command`. (`engine\battle\effect_commands.asm:51-119`)
- So “damage/effects” are not hardcoded in `BattleTurn`; they happen inside the battle-command script executed by `DoMove`. (`engine\battle\effect_commands.asm:51-119`)

### End-of-turn order
`HandleBetweenTurnEffects` runs, in order:
1. faint check order setup (player-first or enemy-first, depending on serial clock ownership)
2. `HandleFutureSight`
3. `HandleWeather`
4. `HandleWrap`
5. `HandlePerishSong`
6. `HandleLeftovers`
7. `HandleMysteryberry`
8. `HandleDefrost`
9. `HandleSafeguard`
10. `HandleScreens`
11. `HandleStatBoostingHeldItems`
12. `HandleHealingItems`
13. `UpdateBattleMonInParty`
14. `LoadTilemapToTempTilemap`
15. `HandleEncore` (`engine\battle\core.asm:205-251`)

## 9. Script engine

### Is it a bytecode interpreter?
Yes.
- `ScriptEvents` is the top-level interpreter loop. It dispatches by `wScriptMode` (`SCRIPT_OFF`, `SCRIPT_READ`, `SCRIPT_WAIT_MOVEMENT`, `SCRIPT_WAIT`). (`engine\overworld\scripting.asm:10-25`, `constants\ram_constants.asm:197-202`)
- `RunScriptCommand` fetches one opcode with `GetScriptByte` and dispatches through `ScriptCommandTable` via `rst JumpTable`. (`engine\overworld\scripting.asm:58-63`, `home\map.asm:1482-1510`)
- `GetScriptByte` reads from the banked instruction stream at `wScriptBank:wScriptPos` and advances the script PC. (`home\map.asm:1482-1510`)

### Command table
- The authoritative command table is `ScriptCommandTable` in `engine\overworld\scripting.asm`; it maps opcodes `$00-$a1`. (`engine\overworld\scripting.asm:64-229`)
- The assembler-side bytecode macros that emit those opcodes live in `macros\scripts\events.asm`. (`macros\scripts\events.asm:4-1015`)
- Major groups in that table:
  - calls/jumps/conditionals (`scall`, `farscall`, `sjump`, `ifequal`, etc.)
  - variable/memory access (`readmem`, `readvar`, `loadvar`, etc.)
  - inventory/money/flags/events
  - text/menu/UI commands
  - battle commands (`loadwildmon`, `loadtrainer`, `startbattle`, `reloadmapafterbattle`)
  - movement/object/map manipulation
  - audio commands
  - script termination/load-map commands (`newloadmap`, `end`, `reloadend`, `endall`, `credits`). (`engine\overworld\scripting.asm:64-229`, `macros\scripts\events.asm:4-1015`)

### How map scripts are started
- `CallScript` seeds `wScriptBank`/`wScriptPos`, marks `wScriptRunning`, and returns carry. (`home\map.asm:1340-1353`)
- Scene scripts are selected from the current map’s scene table by `RunSceneScript`, which can also immediately chain into deferred scripts. (`engine\overworld\events.asm:388-434`)
- Queued “memory scripts” live in `wMapReentryScript*`; `RunMemScript` runs them once and clears the queue. (`engine\overworld\events.asm:1028-1066`)
- Callback scripts run via `ExecuteCallbackScript`, which temporarily enables script mode, executes `ScriptEvents`, then restores the previous script state. (`home\map.asm:1413-1428`)

### Script subroutines
- `scall`/`farscall`/`memcall` all funnel into `ScriptCall`, which pushes the current `(bank, pos)` onto `wScriptStack` and switches `wScriptBank:wScriptPos` to the callee. (`engine\overworld\scripting.asm:1127-1173`)
- `end`/`endcallback` unwind through `ExitScriptSubroutine`; if there is no parent frame, they stop the script entirely. (`engine\overworld\scripting.asm:2142-2194`)

## 10. State machine / game modes

There is **no single global “game mode” variable** for the whole game. Instead, different subsystems keep their own state machines:
- `wMapStatus` = overworld phase (`START/ENTER/HANDLE/DONE`). (`constants\ram_constants.asm:169-174`, `engine\overworld\events.asm:3-22`)
- `hMapEntryMethod` = which map-setup script to run next (`WARP`, `CONTINUE`, `RELOADMAP`, `SUBMENU`, etc.). (`constants\map_setup_constants.asm:1-15`, `engine\overworld\map_setup.asm:1-15`)
- `wMapEventStatus` = whether player/map events are currently enabled. (`constants\ram_constants.asm:176-179`, `engine\overworld\events.asm:155-173`, `engine\overworld\events.asm:212-229`)
- `wScriptMode` + `wScriptRunning` = script interpreter state. (`constants\ram_constants.asm:181-202`, `engine\overworld\scripting.asm:10-25`)
- `hVBlank` = rendering/interrupt mode (`NORMAL`, `CUTSCENE`, `SERIAL`, `CREDITS`, etc.). (`constants\ram_constants.asm:341-352`, `home\vblank.asm:15-29`, `home\vblank.asm:40-51`)
- `wJumptableIndex` = many self-contained loops (title screen, credits, battle transitions, Hall of Fame animation). (`engine\menus\intro_menu.asm:901-919`, `engine\movie\credits.asm:137-175`, `engine\battle\battle_transition.asm:26-33`)
- `wBattleMode` / `wBattleResult` / `wBattleEnded` = battle mode/result. (`engine\battle\core.asm:3-10`, `engine\battle\core.asm:7749-7819`)

Practical consequence: top-level transitions are mostly explicit `jp`/`call` hand-offs plus map-status rewrites, not a single monolithic dispatcher.

## 11. Bank switching during execution (`farcall` / `callfar`)

### Fast bank switch helper
- `rst10` (`Bankswitch`) writes `A` to both `hROMBank` and the MBC ROM bank register, then returns. (`home\header.asm:11-15`)

### `farcall` / `callfar`
- The macros simply load `A = BANK(target)` and `HL = target`, then `rst FarCall`. (`macros\farcall.asm:7-17`)
- `rst8` jumps to `FarCall_hl`. (`home\header.asm:7-9`)
- `FarCall_hl`:
  1. stores target bank in `wTempBank`
  2. pushes current `hROMBank`
  3. switches to the target bank
  4. `call`s a `jp hl` trampoline (`FarCall_JumpToHL`)
  5. saves/restores `bc` through `wFarCallBC`
  6. pops the old bank and switches back
  7. returns. (`home\farcall.asm:1-31`)
- `homecall` is the same pattern, but it switches banks and then does a normal `call` to a home-routine name. (`macros\farcall.asm:19-27`)

## 12. Predef system

- `predef` is an indexed dispatch system, not a direct symbol call. The macro converts a symbolic name into an index relative to `PredefPointers`, then `call`s `Predef`. (`macros\predef.asm:3-17`)
- `Predef`:
  1. stores the predef ID
  2. saves the current ROM bank
  3. switches to the bank containing `GetPredefPointer`
  4. looks up the callee’s `(address, bank)` in `PredefPointers`
  5. bank-switches to the target
  6. pushes a synthetic return address and the target address onto the stack
  7. restores the caller’s `hl`
  8. uses `ret` to jump into the callee
  9. on `.Return`, restores the original bank and `hl`. (`home\predef.asm:1-52`, `engine\predef.asm:1-28`)
- `PredefPointers` is a 3-byte table of predefined routines. It includes `StartBattle`, HUD drawing, graphics decompression, RTC helpers, etc. (`data\predef_pointers.asm:1-80`)
- Example: `Script_startbattle` uses `predef StartBattle`, and `StartBattle` is indeed present in `PredefPointers`. (`engine\overworld\scripting.asm:1065-1071`, `data\predef_pointers.asm:28-33`)

## Bottom line
- Normal boot path: boot ROM -> `$0100 Start` -> `_Start` -> `Init` -> `GameInit` -> intro/title/main menu. (`home\header.asm:59-70`, `home\init.asm:16-153`, `engine\menus\intro_menu.asm:1156-1159`)
- Core gameplay path: `FinishContinueFunction` -> `OverworldLoop` -> map/scripts/input/object update -> `DelayFrames`/VBlank -> repeat. (`engine\menus\intro_menu.asm:343-357`, `engine\overworld\events.asm:3-22`, `engine\overworld\events.asm:138-153`)
- Battles and credits are entered by explicit script commands, then hand control back by rewriting map status / resetting to title. (`engine\overworld\scripting.asm:1065-1116`, `engine\overworld\scripting.asm:2207-2221`)