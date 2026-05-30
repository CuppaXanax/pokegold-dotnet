# Memory map annotation for `pret/pokegold`

## Region overview

| Region | Effective range | What lives here | Sources |
| --- | --- | --- | --- |
| VRAM bank 0 | `$8000-$9FFF` | Tile pattern data (`vTiles0`-`vTiles2`) and two BG tilemaps (`vBGMap0`/`vBGMap1`). | `ram/vram.asm:1-7`, `constants/hardware.inc:449-557` |
| VRAM bank 1 | `$8000-$9FFF` (bank 1) | CGB-only second tile/attribute bank: `vTiles3`-`vTiles5`, `vBGMap2`, `vBGMap3`. | `ram/vram.asm:10-16`, `constants/hardware.inc:586-690` |
| SRAM bank 0 | `$A000-$BFFF` | Scratch/decompression workspace, mail + Mystery Gift save data, backup save fragment 1, RTC/stack scratch. | `layout.link:286-293`, `ram/sram.asm:1-85` |
| SRAM bank 1 | `$A000-$BFFF` | Main save payload, active PC box, link-battle records, Hall of Fame, backup save fragments 2/3. | `layout.link:294-299`, `ram/sram.asm:87-173` |
| SRAM bank 2 | `$A000-$BFFF` | Stored boxes 1-7. | `layout.link:300-301`, `ram/sram.asm:152-155` |
| SRAM bank 3 | `$A000-$BFFF` | Stored boxes 8-14 plus backup save fragment 3. | `layout.link:302-304`, `ram/sram.asm:157-173` |
| WRAM0 | `$C000-$CFFF` | Audio state, rendering buffers, overlays for overworld/link/battle/UI, palette/OAM/tilemap scratch. | `layout.link:263-276`, `ram/wram.asm:1-1818` |
| WRAM1 (bank 1) | `$D000-$DFFF` | Persistent game/player/map data, loaded objects, party/Pokédex/day-care state, runtime stack. | `layout.link:277-281`, `ram/wram.asm:1820-2818` |
| HRAM | `$FF80-$FFFE` | OAM DMA stub at `$FF80-$FF89`, then fast-access RTC/time, joypad, scroll, serial, RNG, and interrupt-side variables. | `layout.link:305-307`, `engine/gfx/load_push_oam.asm:13-27`, `ram/hram.asm:1-175` |

## Important globals and state hotspots

### Party Pokémon storage
- `wPartyCount` at `$D986` stores the number of active party members; `wPartySpecies`/`wPartyEnd` follow it as the species list terminator layout. `wPartyMons` starts at `$D98E` and holds 6 `party_struct` entries, 48 bytes each. OT names and nicknames are stored immediately after in `wPartyMonOTs` and `wPartyMonNicknames`. (`ram/wram.asm:2669-2691`, `macros/ram.asm:29-42`, `constants/pokemon_data_constants.asm:75-107`)
- `box_struct` is 32 bytes; `party_struct` extends it with status/current stats to 48 bytes; `battle_struct` is a compact 32-byte active-battler copy. (`macros/ram.asm:7-42`, `macros/ram.asm:76-97`, `constants/pokemon_data_constants.asm:75-107`)
- The active box in SRAM uses `curbox` (1102 bytes, no trailing padding) at `sBox`; archived PC boxes use `box` (1104 bytes, including 2 padding bytes) in `sBox1`-`sBox14`. (`ram/sram.asm:107-109`, `ram/sram.asm:142-173`, `macros/ram.asm:99-124`, `constants/pokemon_data_constants.asm:117-123`)

### Current map state
- The live map identifier/position block saved into SRAM is `wCurMapData` at `$D952`. The crucial fields are `wWarpNumber`, `wMapGroup`, `wMapNumber`, `wYCoord`, and `wXCoord`. (`ram/wram.asm:2636-2664`)
- Runtime map attributes and connections live earlier in WRAM1: `wMapAttributes*`, `wNorthMapConnection`/`wSouthMapConnection`/`wWestMapConnection`/`wEastMapConnection`, and `wTileset*`. (`ram/wram.asm:1895-1934`)
- Loaded NPC/object runtime state is split between `wObjectStructs` at `$D1FD` (13 live `object_struct`s) and `wMapObjects` at `$D445` (16 map template objects). (`ram/wram.asm:2361-2388`, `macros/ram.asm:272-325`, `constants/map_object_constants.asm:1-100`)

### RNG state
- The main non-battle RNG state is just two HRAM bytes: `hRandomAdd` at `$FFE3` and `hRandomSub` at `$FFE4`. Both are advanced from the hardware divider register (`rDIV`) every time `Random` runs and again every normal VBlank. (`ram/hram.asm:154-155`, `home/random.asm:1-29`, `home/vblank.asm:67-82`)
- Battle RNG uses a banked `_BattleRandom` routine so link battles can share a synchronized PRNG stream. (`home/random.asm:31-48`)

### Joypad state
- Raw VBlank input lives in `hJoypadReleased`, `hJoypadPressed`, `hJoypadDown`, and `hJoypadSum`; `GetJoypad` mirrors those into `hJoyReleased`, `hJoyPressed`, `hJoyDown`, and `hJoyLast` for menu/text code. (`ram/hram.asm:33-40`, `home/joypad.asm:16-104`, `home/joypad.asm:106-162`)
- `wJoypadDisable` in WRAM1 is the main software input gate. (`ram/wram.asm:2533-2538`, `home/joypad.asm:29-37`, `constants/ram_constants.asm:32-35`)

### Battle state
- The main battle overlay begins at `wBattle` `$CAA0-$CBD6`. It includes `wEnemyMoveStruct`, `wPlayerMoveStruct`, active-battler nicknames, `wBattleMon`, trainer AI fields, type modifier / critical / miss flags, substatus bytes, per-side stat blocks and stat stages, scripted battle text buffer, weather, move history, future-sight state, trapping/rampage flags, and the live enemy battler overlay `wEnemyMon`. (`ram/wram.asm:738-1015`, `ram/wram.asm:2087-2124`)
- The currently active player battler index is `wCurBattleMon`; currently selected move slot is `wCurMoveNum`; battle type/mode are `wBattleMode` and `wBattleType`. (`ram/wram.asm:1732-1737`, `ram/wram.asm:2100-2115`)

### Save data and checksuming
- The primary save payload in SRAM bank 1 is `sGameData`, which is laid out as `sPlayerData1`, `sPlayerData2`, `sPlayerData3`, `sCurMapData`, and `sPokemonData`, bracketed by `sCheckValue1`/`sCheckValue2` sentinels and a 16-bit `sChecksum`. (`ram/sram.asm:87-105`)
- `SaveChecksum` computes a straight 16-bit additive checksum over every byte in `sGameData`; `VerifyChecksum` recomputes the same sum and compares it with `sChecksum`. Backup verification adds together the separately stored backup fragments from banks 0/1/3 and compares the total against `sBackupChecksum`. (`engine/menus/save.asm:424-435`, `engine/menus/save.asm:495-535`, `engine/menus/save.asm:714-823`, `engine/menus/save.asm:1038-1051`)

### Text buffers
- `wStringBuffer1`-`wStringBuffer5` are the main formatted-text buffers; `wMonOrItemNameBuffer` provides two extra NAME_LENGTH scratch areas; `wBattleScriptBuffer` holds temporary battle text script bytes; `wRadioText` is a 40-character radio line buffer. (`ram/wram.asm:833`, `ram/wram.asm:1442-1445`, `ram/wram.asm:1718-1726`)

### OAM / sprite buffers
- `wShadowOAM` at `$C2B1-$C350` is the 40-entry shadow copy of hardware OAM. During VBlank, `hTransferShadowOAM` at `$FF80-$FF89` writes `HIGH(wShadowOAM)` to `rDMA`, waits for 40 cycles, and returns. (`ram/wram.asm:143-150`, `engine/gfx/load_push_oam.asm:13-27`, `home/vblank.asm:114-118`)

### Palette data
- CGB palette WRAM buffers are `wBGPals1`, `wOBPals1`, `wBGPals2`, and `wOBPals2`; DMG palette mirrors are `wBGP`, `wOBP0`, `wOBP1`; SGB packet data uses `wSGBPals`; the tile attribute plane is `wAttrmap`. (`ram/wram.asm:134-140`, `ram/wram.asm:1072-1085`, `ram/wram.asm:1709-1712`, `home/vblank.asm:213-229`)

## MBC3 bank switching and cartridge registers

- `rROMB` (`$2000-$3FFF`) selects the active switchable ROM bank; the game's `rst $10` handler `Bankswitch` writes the bank number both to `hROMBank` and `rROMB`. (`constants/hardware.inc:757-763`, `home/header.asm:11-15`)
- `FarCall_hl` is the standard far-call helper: it saves the target bank in `wTempBank`, pushes the current `hROMBank`, bankswitches, calls `hl`, then restores the original bank and BC. (`home/farcall.asm:1-28`)
- External RAM / RTC are controlled through the other MBC3 cartridge registers: `rRAMG` (`$0000-$1FFF`) enables/disables SRAM/RTC access, `rRAMB` (`$4000-$5FFF`) selects either SRAM bank 0-3 or RTC register mappings `$08-$0C`, and `rRTCLATCH` (`$6000-$7FFF`) latches RTC counters. (`constants/hardware.inc:737-776`, `home/sram.asm:1-23`, `home/time.asm:6-12`)
- `OpenSRAM` performs the standard SRAM access sequence: latch clock, enable SRAM (`RAMG_SRAM_ENABLE`), then select the desired bank in `rRAMB`. `CloseSRAM` unlatches/disables access by writing `RAMG_SRAM_DISABLE`. (`home/sram.asm:1-23`)

## RTC (MBC3 real-time clock)

- `GetClock` latches the RTC, selects `RAMB_RTC_S`, `RAMB_RTC_M`, `RAMB_RTC_H`, `RAMB_RTC_DL`, and `RAMB_RTC_DH` in `rRAMB`, then reads the mapped value through `rRTCREG` (`$A000`) into HRAM bytes `hRTCSeconds`, `hRTCMinutes`, `hRTCHours`, `hRTCDayLo`, and `hRTCDayHi`. (`constants/hardware.inc:765-776`, `home/time.asm:21-59`, `ram/hram.asm:5-9`)
- `FixDays` mods the RTC day count by 140 days, preserves/clears the MBC3 day-high bits, and records overflow conditions in `sRTCStatusFlags`. `StartClock` runs `_GetClock`, `GetClock`, `_FixDays`, and `FixDays`, then resumes the RTC. (`home/time.asm:61-120`, `engine/rtc/rtc.asm:91-115`, `ram/sram.asm:60-64`, `constants/ram_constants.asm:335-339`)
- `FixTime` adds the saved start offset (`wStartDay`/`wStartHour`/`wStartMinute`/`wStartSecond`) to the raw latched RTC values to produce the live in-game day/time in `wCurDay`, `hHours`, `hMinutes`, and `hSeconds`. (`home/time.asm:122-168`, `ram/wram.asm:2324-2351`, `ram/hram.asm:13-18`)
- `StageRTCTimeForSave` copies `wCurDay` + `hHours`/`hMinutes`/`hSeconds` into `wRTC` before save logic writes the save file. `SaveRTC` clears the MBC3 day-carry bit and resets `sRTCStatusFlags`. (`engine/rtc/rtc.asm:63-89`, `ram/wram.asm:2330-2339`, `ram/sram.asm:60-64`)
- `InitClock`, `RestartClock`, and the timeset UI store proposed day/hour/minute values in `wInitHourBuffer`, `wInitMinuteBuffer`, `wRestartClockDay`, `wRestartClockHour`, and `wRestartClockMin`, then call `InitTime`/`SetClock` to rebase the saved offset and/or write new latched RTC values. (`engine/rtc/timeset.asm:4-118`, `engine/rtc/restart_clock.asm:38-108`, `ram/wram.asm:239-246`, `ram/wram.asm:2056-2062`)

## DMA and OAM handling

- At startup, `WriteOAMDMACodeToHRAM` copies a tiny routine from ROM into HRAM at `hTransferShadowOAM`. (`home/init.asm:84-90`, `engine/gfx/load_push_oam.asm:1-27`)
- That HRAM routine writes `HIGH(wShadowOAM)` to `rDMA` (`$FF46`) and busy-waits long enough for hardware OAM DMA to finish. (`engine/gfx/load_push_oam.asm:17-26`, `constants/hardware.inc:531-533`)
- Normal VBlank calls `hTransferShadowOAM` whenever `hOAMUpdate` is zero, after scroll/palette/BG updates but before joypad and sound. (`home/vblank.asm:53-150`, `ram/hram.asm:140-146`)
- The shadow buffer itself is `wShadowOAM` in WRAM0 and contains 40 `sprite_oam_struct` entries (`Y`, `X`, `TileID`, `Attributes`). (`ram/wram.asm:143-150`, `macros/ram.asm:327-338`, `constants/hardware.inc:980-1001`)

## Memory-mapped I/O used by the engine

### Joypad / serial / timer / interrupts
- `rJOYP $FF00` — joypad mux/data register. The engine selects d-pad first, then buttons, in `UpdateJoypad`. (`constants/hardware.inc:50-105`, `home/joypad.asm:39-73`)
- `rSB $FF01`, `rSC $FF02` — serial data/control. Used by link/serial code and initialized in `Init`. (`constants/hardware.inc:127-145`, `home/init.asm:36-39`)
- `rDIV $FF04`, `rTIMA $FF05`, `rTMA $FF06`, `rTAC $FF07` — timer/divider registers. `Init` starts the timer at 4096 Hz, and RNG reads `rDIV`. (`constants/hardware.inc:148-173`, `home/init.asm:44-50`, `home/random.asm:16-26`)
- `rIF $FF0F`, `rIE $FFFF` — interrupt request/enable. VBlank handlers aggressively mask/unmask STAT/serial/VBlank around graphics + sound work. (`constants/hardware.inc:176-189`, `constants/hardware.inc:710-723`, `home/vblank.asm:186-210`, `home/init.asm:31-33`, `home/init.asm:142-144`)

### Sound registers
- Channel 1: `rAUD1SWEEP $FF10`, `rAUD1LEN $FF11`, `rAUD1ENV $FF12`, `rAUD1LOW $FF13`, `rAUD1HIGH $FF14`.
- Channel 2: `rAUD2LEN $FF16`, `rAUD2ENV $FF17`, `rAUD2LOW $FF18`, `rAUD2HIGH $FF19`.
- Channel 3: `rAUD3ENA $FF1A`, `rAUD3LEN $FF1B`, `rAUD3LEVEL $FF1C`, `rAUD3LOW $FF1D`, `rAUD3HIGH $FF1E`, wave RAM `rAUD3WAVE_0-$FF30` .. `rAUD3WAVE_F-$FF3F`.
- Channel 4 / mixer / master: `rAUD4LEN $FF20`, `rAUD4ENV $FF21`, `rAUD4POLY $FF22`, `rAUD4GO $FF23`, `rAUDVOL $FF24`, `rAUDTERM $FF25`, `rAUDENA $FF26`, plus PCM monitors `rPCM12 $FF76` and `rPCM34 $FF77`. (`constants/hardware.inc:191-447`, `constants/hardware.inc:694-706`)
- WRAM mirrors `wVolume`, `wSoundOutput`, and `wPitchSweep` correspond directly to mixer/sweep registers. (`ram/wram.asm:22-41`)

### LCD / VRAM / palette registers
- Core LCD registers: `rLCDC $FF40`, `rSTAT $FF41`, `rSCY $FF42`, `rSCX $FF43`, `rLY $FF44`, `rLYC $FF45`, `rDMA $FF46`, `rBGP $FF47`, `rOBP0 $FF48`, `rOBP1 $FF49`, `rWY $FF4A`, `rWX $FF4B`. (`constants/hardware.inc:449-557`)
- CGB-only VRAM/palette control: `rVBK $FF4F`, `rVDMA_SRC_HIGH $FF51`, `rVDMA_SRC_LOW $FF52`, `rVDMA_DEST_HIGH $FF53`, `rVDMA_DEST_LOW $FF54`, `rVDMA_LEN $FF55`, `rBGPI $FF68`, `rBGPD $FF69`, `rOBPI $FF6A`, `rOBPD $FF6B`, `rWBK $FF70`. (`constants/hardware.inc:586-690`)
- `Init` clears/initializes the scroll, window, palette, and LCD registers; VBlank continually copies HRAM mirrors (`hSCX`, `hSCY`, `hWX`, `hWY`) into the hardware registers. (`home/init.asm:31-58`, `home/init.asm:97-140`, `home/vblank.asm:84-118`, `home/vblank.asm:160-180`)

## Detailed WRAM0 (`$C000-$CFFF`) tables

### Audio RAM

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wMusicPlaying` | `$C000` | 1 | nonzero if playing. | `ram/wram.asm:4` |
| `wAudio` | `$C001-$C197` | 407 | Alias for the start of the full audio engine state block. | `ram/wram.asm:6` |
| `wChannel1` | `$C001-$C02D` | 45 | Audio channel 1 state struct. | `ram/wram.asm:9` |
| `wChannel2` | `$C02E-$C05A` | 45 | Audio channel 2 state struct. | `ram/wram.asm:9` |
| `wChannel3` | `$C05B-$C087` | 45 | Audio channel 3 state struct. | `ram/wram.asm:9` |
| `wChannel4` | `$C088-$C0B4` | 45 | Audio channel 4 state struct. | `ram/wram.asm:9` |
| `wChannel5` | `$C0B5-$C0E1` | 45 | Audio channel 5 state struct. | `ram/wram.asm:9` |
| `wChannel6` | `$C0E2-$C10E` | 45 | Audio channel 6 state struct. | `ram/wram.asm:9` |
| `wChannel7` | `$C10F-$C13B` | 45 | Audio channel 7 state struct. | `ram/wram.asm:9` |
| `wChannel8` | `$C13C-$C168` | 45 | Audio channel 8 state struct. | `ram/wram.asm:9` |
| `wCurTrackDuty` | `$C16A` | 1 | Stores cur track duty. | `ram/wram.asm:14` |
| `wCurTrackVolumeEnvelope` | `$C16B` | 1 | Stores cur track volume envelope. | `ram/wram.asm:15` |
| `wCurTrackFrequency` | `$C16C-$C16D` | 2 | Stores cur track frequency. | `ram/wram.asm:16` |
| `wUnusedBCDNumber` | `$C16E` | 1 | BCD value, dummied out. | `ram/wram.asm:17` |
| `wCurNoteDuration` | `$C16F` | 1 | used in MusicE0 and LoadNote. | `ram/wram.asm:18` |
| `wCurMusicByte` | `$C170` | 1 | Stores cur music byte. | `ram/wram.asm:20` |
| `wCurChannel` | `$C171` | 1 | Stores cur channel. | `ram/wram.asm:21` |
| `wVolume` | `$C172` | 1 | Stores volume. | `ram/wram.asm:22` |
| `wSoundOutput` | `$C173` | 1 | Stores sound output. | `ram/wram.asm:30` |
| `wPitchSweep` | `$C174` | 1 | Stores pitch sweep. | `ram/wram.asm:35` |
| `wMusicID` | `$C175-$C176` | 2 | Stores music id. | `ram/wram.asm:43` |
| `wMusicBank` | `$C177` | 1 | Stores music bank. | `ram/wram.asm:44` |
| `wNoiseSampleAddress` | `$C178-$C179` | 2 | Pointer/address for Noise Sample Address. | `ram/wram.asm:45` |
| `wNoiseSampleDelay` | `$C17A` | 1 | Stores noise sample delay. | `ram/wram.asm:46` |
| `wMusicNoiseSampleSet` | `$C17C` | 1 | Stores music noise sample set. | `ram/wram.asm:48` |
| `wSFXNoiseSampleSet` | `$C17D` | 1 | Stores sfx noise sample set. | `ram/wram.asm:49` |
| `wLowHealthAlarm` | `$C17E` | 1 | Stores low health alarm. | `ram/wram.asm:51` |
| `wMusicFade` | `$C17F` | 1 | Stores music fade. | `ram/wram.asm:57` |
| `wMusicFadeCount` | `$C180` | 1 | Stores music fade count. | `ram/wram.asm:63` |
| `wMusicFadeID` | `$C181-$C182` | 2 | Stores music fade id. | `ram/wram.asm:64` |
| `wCryPitch` | `$C188-$C189` | 2 | Stores cry pitch. | `ram/wram.asm:68` |
| `wCryLength` | `$C18A-$C18B` | 2 | Stores cry length. | `ram/wram.asm:69` |
| `wLastVolume` | `$C18C` | 1 | Stores last volume. | `ram/wram.asm:71` |
| `wUnusedMusicF9Flag` | `$C18D` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:72` |
| `wSFXPriority` | `$C18E` | 1 | Stores sfx priority. | `ram/wram.asm:74` |
| `wChannel1JumpCondition` | `$C190` | 1 | Jump-condition byte for audio channel 1. | `ram/wram.asm:80` |
| `wChannel2JumpCondition` | `$C191` | 1 | Jump-condition byte for audio channel 2. | `ram/wram.asm:81` |
| `wChannel3JumpCondition` | `$C192` | 1 | Jump-condition byte for audio channel 3. | `ram/wram.asm:82` |
| `wChannel4JumpCondition` | `$C193` | 1 | Jump-condition byte for audio channel 4. | `ram/wram.asm:83` |
| `wStereoPanningMask` | `$C194` | 1 | Stores stereo panning mask. | `ram/wram.asm:85` |
| `wCryTracks` | `$C195` | 1 | Stores cry tracks. | `ram/wram.asm:87` |
| `wSFXDuration` | `$C196` | 1 | Stores sfx duration. | `ram/wram.asm:92` |
| `wCurSFX` | `$C197` | 1 | Stores cur sfx. | `ram/wram.asm:93` |
| `wAudioEnd` | `$C198` | alias | End marker for the audio engine state block. | `ram/wram.asm:97` |
| `wMapMusic` | `$C198` | 1 | Buffer/data field for map music. | `ram/wram.asm:99` |
| `wDontPlayMapMusicOnReload` | `$C199` | 1 | Buffer/data field for dont play map music on reload. | `ram/wram.asm:101` |

### WRAM

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wLZAddress` | `$C19A-$C19B` | 2 | Pointer/address for LZ Address. | `ram/wram.asm:106` |
| `wLZBank` | `$C19C` | 1 | Stores lz bank. | `ram/wram.asm:107` |
| `wInputType` | `$C19E` | 1 | Stores input type. | `ram/wram.asm:111` |
| `wAutoInputAddress` | `$C19F-$C1A0` | 2 | Pointer/address for Auto Input Address. | `ram/wram.asm:112` |
| `wAutoInputBank` | `$C1A1` | 1 | Stores auto input bank. | `ram/wram.asm:113` |
| `wAutoInputLength` | `$C1A2` | 1 | Stores auto input length. | `ram/wram.asm:114` |
| `wDebugFlags` | `$C1A3` | 1 | Stores debug flags. | `ram/wram.asm:116` |
| `wGameLogicPaused` | `$C1A4` | 1 | Stores game logic paused. | `ram/wram.asm:117` |
| `wSpriteUpdatesEnabled` | `$C1A5` | 1 | Stores sprite updates enabled. | `ram/wram.asm:118` |
| `wUnusedScriptByte` | `$C1A6` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:120` |
| `wMapTimeOfDay` | `$C1A7` | 1 | Buffer/data field for map time of day. | `ram/wram.asm:122` |
| `wPrinterConnectionOpen` | `$C1AB` | 1 | Stores printer connection open. | `ram/wram.asm:126` |
| `wPrinterOpcode` | `$C1AC` | 1 | Stores printer opcode. | `ram/wram.asm:127` |
| `wPrevDexEntry` | `$C1AD` | 1 | Stores prev dex entry. | `ram/wram.asm:128` |
| `wDisableTextAcceleration` | `$C1AE` | 1 | Buffer/data field for disable text acceleration. | `ram/wram.asm:129` |
| `wPCItemsCursor` | `$C1AF` | 1 | Stores pc items cursor. | `ram/wram.asm:130` |
| `wPCItemsScrollPosition` | `$C1B0` | 1 | Stores pc items scroll position. | `ram/wram.asm:131` |

### GBC Palettes

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wBGPals1` | `$C1B1-$C1F0` | 64 | CGB background palette buffer 1 (8 palettes). | `ram/wram.asm:137` |
| `wOBPals1` | `$C1F1-$C230` | 64 | CGB OBJ palette buffer 1 (8 palettes). | `ram/wram.asm:138` |
| `wBGPals2` | `$C231-$C270` | 64 | CGB background palette buffer 2 (8 palettes). | `ram/wram.asm:139` |
| `wOBPals2` | `$C271-$C2B0` | 64 | CGB OBJ palette buffer 2 (8 palettes). | `ram/wram.asm:140` |

### Sprites

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wShadowOAM` | `$C2B1-$C350` | 160 | Shadow OAM mirror copied into hardware OAM during DMA. | `ram/wram.asm:145` |
| `wShadowOAMSprite00` | `$C2B1-$C2B4` | 4 | Shadow OAM sprite slot 00. | `ram/wram.asm:148` |
| `wShadowOAMSprite01` | `$C2B5-$C2B8` | 4 | Shadow OAM sprite slot 01. | `ram/wram.asm:148` |
| `wShadowOAMSprite02` | `$C2B9-$C2BC` | 4 | Shadow OAM sprite slot 02. | `ram/wram.asm:148` |
| `wShadowOAMSprite03` | `$C2BD-$C2C0` | 4 | Shadow OAM sprite slot 03. | `ram/wram.asm:148` |
| `wShadowOAMSprite04` | `$C2C1-$C2C4` | 4 | Shadow OAM sprite slot 04. | `ram/wram.asm:148` |
| `wShadowOAMSprite05` | `$C2C5-$C2C8` | 4 | Shadow OAM sprite slot 05. | `ram/wram.asm:148` |
| `wShadowOAMSprite06` | `$C2C9-$C2CC` | 4 | Shadow OAM sprite slot 06. | `ram/wram.asm:148` |
| `wShadowOAMSprite07` | `$C2CD-$C2D0` | 4 | Shadow OAM sprite slot 07. | `ram/wram.asm:148` |
| `wShadowOAMSprite08` | `$C2D1-$C2D4` | 4 | Shadow OAM sprite slot 08. | `ram/wram.asm:148` |
| `wShadowOAMSprite09` | `$C2D5-$C2D8` | 4 | Shadow OAM sprite slot 09. | `ram/wram.asm:148` |
| `wShadowOAMSprite10` | `$C2D9-$C2DC` | 4 | Shadow OAM sprite slot 10. | `ram/wram.asm:148` |
| `wShadowOAMSprite11` | `$C2DD-$C2E0` | 4 | Shadow OAM sprite slot 11. | `ram/wram.asm:148` |
| `wShadowOAMSprite12` | `$C2E1-$C2E4` | 4 | Shadow OAM sprite slot 12. | `ram/wram.asm:148` |
| `wShadowOAMSprite13` | `$C2E5-$C2E8` | 4 | Shadow OAM sprite slot 13. | `ram/wram.asm:148` |
| `wShadowOAMSprite14` | `$C2E9-$C2EC` | 4 | Shadow OAM sprite slot 14. | `ram/wram.asm:148` |
| `wShadowOAMSprite15` | `$C2ED-$C2F0` | 4 | Shadow OAM sprite slot 15. | `ram/wram.asm:148` |
| `wShadowOAMSprite16` | `$C2F1-$C2F4` | 4 | Shadow OAM sprite slot 16. | `ram/wram.asm:148` |
| `wShadowOAMSprite17` | `$C2F5-$C2F8` | 4 | Shadow OAM sprite slot 17. | `ram/wram.asm:148` |
| `wShadowOAMSprite18` | `$C2F9-$C2FC` | 4 | Shadow OAM sprite slot 18. | `ram/wram.asm:148` |
| `wShadowOAMSprite19` | `$C2FD-$C300` | 4 | Shadow OAM sprite slot 19. | `ram/wram.asm:148` |
| `wShadowOAMSprite20` | `$C301-$C304` | 4 | Shadow OAM sprite slot 20. | `ram/wram.asm:148` |
| `wShadowOAMSprite21` | `$C305-$C308` | 4 | Shadow OAM sprite slot 21. | `ram/wram.asm:148` |
| `wShadowOAMSprite22` | `$C309-$C30C` | 4 | Shadow OAM sprite slot 22. | `ram/wram.asm:148` |
| `wShadowOAMSprite23` | `$C30D-$C310` | 4 | Shadow OAM sprite slot 23. | `ram/wram.asm:148` |
| `wShadowOAMSprite24` | `$C311-$C314` | 4 | Shadow OAM sprite slot 24. | `ram/wram.asm:148` |
| `wShadowOAMSprite25` | `$C315-$C318` | 4 | Shadow OAM sprite slot 25. | `ram/wram.asm:148` |
| `wShadowOAMSprite26` | `$C319-$C31C` | 4 | Shadow OAM sprite slot 26. | `ram/wram.asm:148` |
| `wShadowOAMSprite27` | `$C31D-$C320` | 4 | Shadow OAM sprite slot 27. | `ram/wram.asm:148` |
| `wShadowOAMSprite28` | `$C321-$C324` | 4 | Shadow OAM sprite slot 28. | `ram/wram.asm:148` |
| `wShadowOAMSprite29` | `$C325-$C328` | 4 | Shadow OAM sprite slot 29. | `ram/wram.asm:148` |
| `wShadowOAMSprite30` | `$C329-$C32C` | 4 | Shadow OAM sprite slot 30. | `ram/wram.asm:148` |
| `wShadowOAMSprite31` | `$C32D-$C330` | 4 | Shadow OAM sprite slot 31. | `ram/wram.asm:148` |
| `wShadowOAMSprite32` | `$C331-$C334` | 4 | Shadow OAM sprite slot 32. | `ram/wram.asm:148` |
| `wShadowOAMSprite33` | `$C335-$C338` | 4 | Shadow OAM sprite slot 33. | `ram/wram.asm:148` |
| `wShadowOAMSprite34` | `$C339-$C33C` | 4 | Shadow OAM sprite slot 34. | `ram/wram.asm:148` |
| `wShadowOAMSprite35` | `$C33D-$C340` | 4 | Shadow OAM sprite slot 35. | `ram/wram.asm:148` |
| `wShadowOAMSprite36` | `$C341-$C344` | 4 | Shadow OAM sprite slot 36. | `ram/wram.asm:148` |
| `wShadowOAMSprite37` | `$C345-$C348` | 4 | Shadow OAM sprite slot 37. | `ram/wram.asm:148` |
| `wShadowOAMSprite38` | `$C349-$C34C` | 4 | Shadow OAM sprite slot 38. | `ram/wram.asm:148` |
| `wShadowOAMSprite39` | `$C34D-$C350` | 4 | Shadow OAM sprite slot 39. | `ram/wram.asm:148` |
| `wShadowOAMEnd` | `$C351` | alias | End marker for the shadow OAM buffer. | `ram/wram.asm:150` |

### Tilemap

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wTilemap` | `$C351-$C4B8` | 360 | Main 20x18 tilemap buffer used by menu/text rendering. | `ram/wram.asm:155` |
| `wTilemapEnd` | `$C4B9` | alias | End marker for Tilemap. | `ram/wram.asm:158` |

### Miscellaneous

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wSurroundingTiles` | `$C4B9-$C698` | 480 | 24x20 surrounding-tile buffer used while drawing the map border and view window. | `ram/wram.asm:167` |
| `wBoxPartialData` | `$C4B9-$C698` | 480 | 480-byte staging buffer used when saving/loading PC boxes in chunks. | `ram/wram.asm:175` |
| `wBoxPartialDataEnd` | `$C699` | alias | End marker for Box Partial Data. | `ram/wram.asm:176` |
| `wTempTilemap` | `$C4B9-$C620` | 360 | Temporary 20x18 tilemap scratch buffer. | `ram/wram.asm:182` |
| `wPlayerPatchLists` | `$C4B9-$C580` | 200 | link patch lists. | `ram/wram.asm:189` |
| `wOTPatchLists` | `$C581-$C648` | 200 | Buffer/data field for ot patch lists. | `ram/wram.asm:190` |
| `wSpriteAnimData` | `$C4B9-$C579` | 193 | This union spans 200 bytes. | `ram/wram.asm:197` |
| `wSpriteAnimDict` | `$C4B9-$C4CC` | 20 | This union spans 200 bytes. | `ram/wram.asm:199` |
| `wSpriteAnimationStructs` | `$C4CD-$C56C` | 160 | Alias for the start of Sprite Animation Structs block. | `ram/wram.asm:205` |
| `wSpriteAnim1` | `$C4CD-$C4DC` | 16 | Sprite animation struct 1. | `ram/wram.asm:210` |
| `wSpriteAnim2` | `$C4DD-$C4EC` | 16 | Sprite animation struct 2. | `ram/wram.asm:210` |
| `wSpriteAnim3` | `$C4ED-$C4FC` | 16 | Sprite animation struct 3. | `ram/wram.asm:210` |
| `wSpriteAnim4` | `$C4FD-$C50C` | 16 | Sprite animation struct 4. | `ram/wram.asm:210` |
| `wSpriteAnim5` | `$C50D-$C51C` | 16 | Sprite animation struct 5. | `ram/wram.asm:210` |
| `wSpriteAnim6` | `$C51D-$C52C` | 16 | Sprite animation struct 6. | `ram/wram.asm:210` |
| `wSpriteAnim7` | `$C52D-$C53C` | 16 | Sprite animation struct 7. | `ram/wram.asm:210` |
| `wSpriteAnim8` | `$C53D-$C54C` | 16 | Sprite animation struct 8. | `ram/wram.asm:210` |
| `wSpriteAnim9` | `$C54D-$C55C` | 16 | Sprite animation struct 9. | `ram/wram.asm:210` |
| `wSpriteAnim10` | `$C55D-$C56C` | 16 | Sprite animation struct 10. | `ram/wram.asm:210` |
| `wSpriteAnimationStructsEnd` | `$C56D` | alias | End marker for Sprite Animation Structs. | `ram/wram.asm:212` |
| `wSpriteAnimCount` | `$C56D` | 1 | Stores sprite anim count. | `ram/wram.asm:214` |
| `wCurSpriteOAMAddr` | `$C56E` | 1 | Pointer/address for Cur Sprite OAM Addr. | `ram/wram.asm:215` |
| `wCurIcon` | `$C56F` | 1 | Stores cur icon. | `ram/wram.asm:217` |
| `wCurIconTile` | `$C570` | 1 | Stores cur icon tile. | `ram/wram.asm:219` |
| `wSpriteAnimID` | `$C4B9` | alias | Stores sprite anim id. | `ram/wram.asm:221` |
| `wCurSpriteOAMFlags` | `$C4B9` | 1 | Stores cur sprite oam flags. | `ram/wram.asm:222` |
| `wSpriteAnimAddrBackup` | `$C4B9-$C4BA` | 2 | Pointer/address for Sprite Anim Addr Backup. | `ram/wram.asm:224` |
| `wCurAnimVTile` | `$C573` | 1 | Stores cur anim v tile. | `ram/wram.asm:226` |
| `wCurAnimXCoord` | `$C574` | 1 | Stores cur anim x coord. | `ram/wram.asm:227` |
| `wCurAnimYCoord` | `$C575` | 1 | Stores cur anim y coord. | `ram/wram.asm:228` |
| `wCurAnimXOffset` | `$C576` | 1 | Stores cur anim x offset. | `ram/wram.asm:229` |
| `wCurAnimYOffset` | `$C577` | 1 | Stores cur anim y offset. | `ram/wram.asm:230` |
| `wGlobalAnimYOffset` | `$C578` | 1 | Stores global anim y offset. | `ram/wram.asm:231` |
| `wGlobalAnimXOffset` | `$C579` | 1 | Stores global anim x offset. | `ram/wram.asm:232` |
| `wSpriteAnimDataEnd` | `$C57A-$C580` | 7 | End marker for Sprite Anim Data. | `ram/wram.asm:234` |
| `wTimeSetBuffer` | `$C4B9-$C4CC` | 20 | timeset temp storage. | `ram/wram.asm:240` |
| `wInitHourBuffer` | `$C4CD` | 1 | Buffer/data field for init hour buffer. | `ram/wram.asm:242` |
| `wInitMinuteBuffer` | `$C4D7` | 1 | Buffer/data field for init minute buffer. | `ram/wram.asm:244` |
| `wTimeSetBufferEnd` | `$C4EB` | alias | End marker for Time Set Buffer. | `ram/wram.asm:246` |
| `wHallOfFameTemp` | `$C4B9-$C51A` | 98 | hall of fame temp struct. | `ram/wram.asm:250` |
| `wDebugMiddleColors` | `$C4B9` | alias | debug mon color picker. | `ram/wram.asm:254` |
| `wDebugLightColor` | `$C4B9-$C4BA` | 2 | debug mon color picker. | `ram/wram.asm:255` |
| `wDebugDarkColor` | `$C4BB-$C4BC` | 2 | Stores debug dark color. | `ram/wram.asm:256` |
| `wDebugRedChannel` | `$C4C3` | 1 | Stores debug red channel. | `ram/wram.asm:258` |
| `wDebugGreenChannel` | `$C4C4` | 1 | Stores debug green channel. | `ram/wram.asm:259` |
| `wDebugBlueChannel` | `$C4C5` | 1 | Stores debug blue channel. | `ram/wram.asm:260` |
| `wDebugPalette` | `$C4B9` | alias | debug tileset color picker. | `ram/wram.asm:264` |
| `wDebugWhiteTileColor` | `$C4B9-$C4BA` | 2 | debug tileset color picker. | `ram/wram.asm:265` |
| `wDebugLightTileColor` | `$C4BB-$C4BC` | 2 | Stores debug light tile color. | `ram/wram.asm:266` |
| `wDebugDarkTileColor` | `$C4BD-$C4BE` | 2 | Stores debug dark tile color. | `ram/wram.asm:267` |
| `wDebugBlackTileColor` | `$C4BF-$C4C0` | 2 | Stores debug black tile color. | `ram/wram.asm:268` |
| `wPokedexDataStart` | `$C581-$C695` | 277 | Start marker for Pokedex Data. | `ram/wram.asm:274` |
| `wPokedexOrder` | `$C581-$C680` | 256 | This union spans 280 bytes. | pokedex. | `ram/wram.asm:275` |
| `wPokedexOrderEnd` | `$C681` | alias | End marker for Pokedex Order. | `ram/wram.asm:276` |
| `wDexListingScrollOffset` | `$C681` | 1 | offset of the first displayed entry from the start. | `ram/wram.asm:277` |
| `wDexListingCursor` | `$C682` | 1 | Dex cursor. | `ram/wram.asm:278` |
| `wDexListingEnd` | `$C683` | 1 | End marker for Dex Listing. | `ram/wram.asm:279` |
| `wDexListingHeight` | `$C684` | 1 | number of entries displayed at once in the dex listing. | `ram/wram.asm:280` |
| `wCurDexMode` | `$C685` | 1 | Pokedex Mode. | `ram/wram.asm:281` |
| `wDexSearchMonType1` | `$C686` | 1 | first type to search. | `ram/wram.asm:282` |
| `wDexSearchMonType2` | `$C687` | 1 | second type to search. | `ram/wram.asm:283` |
| `wDexSearchResultCount` | `$C688` | 1 | Stores dex search result count. | `ram/wram.asm:284` |
| `wDexArrowCursorPosIndex` | `$C689` | 1 | Stores dex arrow cursor pos index. | `ram/wram.asm:285` |
| `wDexArrowCursorDelayCounter` | `$C68A` | 1 | Stores dex arrow cursor delay counter. | `ram/wram.asm:286` |
| `wDexArrowCursorBlinkCounter` | `$C68B` | 1 | Stores dex arrow cursor blink counter. | `ram/wram.asm:287` |
| `wDexSearchSlowpokeFrame` | `$C68C` | 1 | Stores dex search slowpoke frame. | `ram/wram.asm:288` |
| `wUnlockedUnownMode` | `$C68D` | 1 | Stores unlocked unown mode. | `ram/wram.asm:289` |
| `wDexCurUnownIndex` | `$C68E` | 1 | Stores dex cur unown index. | `ram/wram.asm:290` |
| `wDexUnownCount` | `$C68F` | 1 | Stores dex unown count. | `ram/wram.asm:291` |
| `wDexConvertedMonType` | `$C690` | 1 | mon type converted from dex search mon type. | `ram/wram.asm:292` |
| `wDexListingScrollOffsetBackup` | `$C691` | 1 | Stores dex listing scroll offset backup. | `ram/wram.asm:293` |
| `wDexListingCursorBackup` | `$C692` | 1 | Buffer/data field for dex listing cursor backup. | `ram/wram.asm:294` |
| `wBackupDexListingCursor` | `$C693` | 1 | Buffer/data field for backup dex listing cursor. | `ram/wram.asm:295` |
| `wBackupDexListingPage` | `$C694` | 1 | Buffer/data field for backup dex listing page. | `ram/wram.asm:296` |
| `wDexCurLocation` | `$C695` | 1 | Stores dex cur location. | `ram/wram.asm:297` |
| `wPokedexDataEnd` | `$C696-$C698` | 3 | End marker for Pokedex Data. | `ram/wram.asm:298` |
| `wPokegearPhoneDisplayPosition` | `$C581` | 1 | pokegear. | `ram/wram.asm:303` |
| `wPokegearPhoneCursorPosition` | `$C582` | 1 | Stores pokegear phone cursor position. | `ram/wram.asm:304` |
| `wPokegearPhoneScrollPosition` | `$C583` | 1 | Stores pokegear phone scroll position. | `ram/wram.asm:305` |
| `wPokegearPhoneSelectedPerson` | `$C584` | 1 | Stores pokegear phone selected person. | `ram/wram.asm:306` |
| `wPokegearPhoneSubmenuCursor` | `$C585` | 1 | Stores pokegear phone submenu cursor. | `ram/wram.asm:307` |
| `wPokegearMapCursorObjectPointer` | `$C586-$C587` | 2 | Pointer/address for Pokegear Map Cursor Object Pointer. | `ram/wram.asm:308` |
| `wPokegearMapCursorLandmark` | `$C588` | 1 | Buffer/data field for pokegear map cursor landmark. | `ram/wram.asm:309` |
| `wPokegearMapPlayerIconLandmark` | `$C589` | 1 | Buffer/data field for pokegear map player icon landmark. | `ram/wram.asm:310` |
| `wPokegearRadioChannelBank` | `$C58A` | 1 | Stores pokegear radio channel bank. | `ram/wram.asm:311` |
| `wPokegearRadioChannelAddr` | `$C58B-$C58C` | 2 | Pointer/address for Pokegear Radio Channel Addr. | `ram/wram.asm:312` |
| `wPokegearRadioMusicPlaying` | `$C58D` | 1 | Stores pokegear radio music playing. | `ram/wram.asm:313` |
| `wPlayerTrademon` | `$C581-$C5B1` | 49 | trade. | `ram/wram.asm:317` |
| `wOTTrademon` | `$C5B2-$C5E2` | 49 | Stores ot trademon. | `ram/wram.asm:318` |
| `wTradeAnimAddress` | `$C5E3-$C5E4` | 2 | Pointer/address for Trade Anim Address. | `ram/wram.asm:319` |
| `wLinkPlayer1Name` | `$C5E5-$C5EF` | 11 | Buffer/data field for link player1 name. | `ram/wram.asm:320` |
| `wLinkPlayer2Name` | `$C5F0-$C5FA` | 11 | Buffer/data field for link player2 name. | `ram/wram.asm:321` |
| `wLinkTradeSendmonSpecies` | `$C5FB` | 1 | Stores link trade sendmon species. | `ram/wram.asm:322` |
| `wLinkTradeGetmonSpecies` | `$C5FC` | 1 | Stores link trade getmon species. | `ram/wram.asm:323` |
| `wNamingScreenDestinationPointer` | `$C581-$C582` | 2 | naming screen. | `ram/wram.asm:327` |
| `wNamingScreenCurNameLength` | `$C583` | 1 | Buffer/data field for naming screen cur name length. | `ram/wram.asm:328` |
| `wNamingScreenMaxNameLength` | `$C584` | 1 | Buffer/data field for naming screen max name length. | `ram/wram.asm:329` |
| `wNamingScreenType` | `$C585` | 1 | Stores naming screen type. | `ram/wram.asm:330` |
| `wNamingScreenCursorObjectPointer` | `$C586-$C587` | 2 | Pointer/address for Naming Screen Cursor Object Pointer. | `ram/wram.asm:331` |
| `wNamingScreenLastCharacter` | `$C588` | 1 | Stores naming screen last character. | `ram/wram.asm:332` |
| `wNamingScreenStringEntryCoord` | `$C589-$C58A` | 2 | Stores naming screen string entry coord. | `ram/wram.asm:333` |
| `wSlots` | `$C581-$C5E1` | 97 | slot machine. | `ram/wram.asm:337` |
| `wReel1` | `$C581-$C58F` | 15 | slot machine. | `ram/wram.asm:338` |
| `wReel2` | `$C590-$C59E` | 15 | Stores reel2. | `ram/wram.asm:339` |
| `wReel3` | `$C59F-$C5AD` | 15 | Stores reel3. | `ram/wram.asm:340` |
| `wReel1Stopped` | `$C5AE-$C5B0` | 3 | Stores reel1 stopped. | `ram/wram.asm:341` |
| `wReel2Stopped` | `$C5B1-$C5B3` | 3 | Stores reel2 stopped. | `ram/wram.asm:342` |
| `wReel3Stopped` | `$C5B4-$C5B6` | 3 | Stores reel3 stopped. | `ram/wram.asm:343` |
| `wSlotBias` | `$C5B7` | 1 | Stores slot bias. | `ram/wram.asm:344` |
| `wSlotBet` | `$C5B8` | 1 | Stores slot bet. | `ram/wram.asm:345` |
| `wFirstTwoReelsMatching` | `$C5B9` | 1 | Stores first two reels matching. | `ram/wram.asm:346` |
| `wFirstTwoReelsMatchingSevens` | `$C5BA` | 1 | Stores first two reels matching sevens. | `ram/wram.asm:347` |
| `wSlotMatched` | `$C5BB` | 1 | Stores slot matched. | `ram/wram.asm:348` |
| `wCurReelStopped` | `$C5BC-$C5BE` | 3 | Stores cur reel stopped. | `ram/wram.asm:349` |
| `wPayout` | `$C5BF-$C5C0` | 2 | Stores payout. | `ram/wram.asm:350` |
| `wCurReelXCoord` | `$C5C1` | 1 | Stores cur reel x coord. | `ram/wram.asm:351` |
| `wCurReelYCoord` | `$C5C2` | 1 | Stores cur reel y coord. | `ram/wram.asm:352` |
| `wSlotBuildingMatch` | `$C5C5` | 1 | Stores slot building match. | `ram/wram.asm:354` |
| `wSlotsDataEnd` | `$C5C6-$C5E1` | 28 | End marker for Slots Data. | `ram/wram.asm:355` |
| `wSlotsEnd` | `$C5E2` | alias | End marker for Slots. | `ram/wram.asm:357` |
| `wDeck` | `$C581-$C598` | 24 | card flip. | `ram/wram.asm:361` |
| `wCardFlipNumCardsPlayed` | `$C599` | 1 | Stores card flip num cards played. | `ram/wram.asm:362` |
| `wCardFlipFaceUpCard` | `$C59A` | 1 | Stores card flip face up card. | `ram/wram.asm:363` |
| `wDiscardPile` | `$C59B-$C5B2` | 24 | Stores discard pile. | `ram/wram.asm:364` |
| `wBetaPokerSGBPals` | `$C5B3` | 1 | beta poker game. | `ram/wram.asm:367` |
| `wBetaPokerSGBAttr` | `$C5B6` | 1 | Stores beta poker sgb attr. | `ram/wram.asm:369` |
| `wBetaPokerSGBCol` | `$C5B7` | 1 | Stores beta poker sgb col. | `ram/wram.asm:370` |
| `wBetaPokerSGBRow` | `$C5B8` | 1 | Stores beta poker sgb row. | `ram/wram.asm:371` |
| `wMemoryGameCards` | `$C581-$C5AD` | 45 | unused memory game. | `ram/wram.asm:375` |
| `wMemoryGameCardsEnd` | `$C5AE` | alias | End marker for Memory Game Cards. | `ram/wram.asm:376` |
| `wMemoryGameLastCardPicked` | `$C5AE` | 1 | Stores memory game last card picked. | `ram/wram.asm:377` |
| `wMemoryGameCard1` | `$C5AF` | 1 | Stores memory game card1. | `ram/wram.asm:378` |
| `wMemoryGameCard2` | `$C5B0` | 1 | Stores memory game card2. | `ram/wram.asm:379` |
| `wMemoryGameCard1Location` | `$C5B1` | 1 | Stores memory game card1 location. | `ram/wram.asm:380` |
| `wMemoryGameCard2Location` | `$C5B2` | 1 | Stores memory game card2 location. | `ram/wram.asm:381` |
| `wMemoryGameNumberTriesRemaining` | `$C5B3` | 1 | Stores memory game number tries remaining. | `ram/wram.asm:382` |
| `wMemoryGameLastMatches` | `$C5B4-$C5B8` | 5 | Stores memory game last matches. | `ram/wram.asm:383` |
| `wMemoryGameCounter` | `$C5B9` | 1 | Stores memory game counter. | `ram/wram.asm:384` |
| `wMemoryGameNumCardsMatched` | `$C5BA` | 1 | Stores memory game num cards matched. | `ram/wram.asm:385` |
| `wPuzzlePieces` | `$C581-$C5A4` | 36 | unown puzzle. | `ram/wram.asm:389` |

### Unused Map Buffer

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wUnusedMapBuffer` | `$C699-$C6B0` | 24 | Prototype-era map pointer buffer; unused in retail build. | `ram/wram.asm:397` |
| `wUnusedMapBufferEnd` | `$C6B1` | alias | End marker for Unused Map Buffer. | `ram/wram.asm:398` |

### Overworld Map

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wOverworldMapBlocks` | `$C6B1-$CBC4` | 1300 | 1300-byte decompressed overworld map block buffer. | `ram/wram.asm:404` |
| `wOverworldMapBlocksEnd` | `$CBC5` | alias | End marker for Overworld Map Blocks. | `ram/wram.asm:405` |
| `wDecompressScratch` | `$C6B1-$C930` | 640 | 40-tile WRAM decompression scratch buffer. | `ram/wram.asm:411` |
| `wGameboyPrinterRAM` | `$C6B1-$CABC` | 1036 | Overlay used for Game Boy Printer transfers and tilemap backup during printing. | `ram/wram.asm:417` |
| `wGameboyPrinter2bppSource` | `$C6B1-$C930` | 640 | GB Printer data. | `ram/wram.asm:418` |
| `wGameboyPrinter2bppSourceEnd` | `$C931` | alias | End marker for Gameboy Printer2bpp Source. | `ram/wram.asm:419` |
| `wUnusedGameboyPrinterSafeCancelFlag` | `$C931` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:420` |
| `wPrinterRowIndex` | `$C932` | 1 | Stores printer row index. | `ram/wram.asm:421` |
| `wPrinterData` | `$C933-$C936` | 4 | Printer data. | `ram/wram.asm:424` |
| `wPrinterChecksum` | `$C937-$C938` | 2 | Stores printer checksum. | `ram/wram.asm:425` |
| `wPrinterHandshake` | `$C939` | 1 | Stores printer handshake. | `ram/wram.asm:426` |
| `wPrinterStatusFlags` | `$C93A` | 1 | Stores printer status flags. | `ram/wram.asm:427` |
| `wHandshakeFrameDelay` | `$C93B` | 1 | Stores handshake frame delay. | `ram/wram.asm:434` |
| `wPrinterSerialFrameDelay` | `$C93C` | 1 | Stores printer serial frame delay. | `ram/wram.asm:435` |
| `wPrinterSendByteOffset` | `$C93D-$C93E` | 2 | Stores printer send byte offset. | `ram/wram.asm:436` |
| `wPrinterSendByteCounter` | `$C93F-$C940` | 2 | Stores printer send byte counter. | `ram/wram.asm:437` |
| `wPrinterTilemapBuffer` | `$C941-$CAA8` | 360 | tilemap backup?. | `ram/wram.asm:440` |
| `wPrinterStatus` | `$CAA9` | 1 | Stores printer status. | `ram/wram.asm:441` |
| `wPrinterMargins` | `$CAAB` | 1 | High nibble is for margin before the image, low nibble is for after. | `ram/wram.asm:444` |
| `wPrinterExposureTime` | `$CAAC` | 1 | Stores printer exposure time. | `ram/wram.asm:445` |
| `wGameboyPrinterRAMEnd` | `$CABD` | alias | End marker for Gameboy Printer RAM. | `ram/wram.asm:447` |
| `wBillsPCData` | `$C6B1-$C9E8` | 824 | Overlay used by Bill's PC box-management UI. | `ram/wram.asm:453` |
| `wBillsPCPokemonList` | `$C6B1-$C70A` | 90 | bill's pc data. | `ram/wram.asm:454` |
| `wBillsPC_ScrollPosition` | `$C9DB` | 1 | Stores bills pc scroll position. | `ram/wram.asm:456` |
| `wBillsPC_CursorPosition` | `$C9DC` | 1 | Stores bills pc cursor position. | `ram/wram.asm:457` |
| `wBillsPC_NumMonsInBox` | `$C9DD` | 1 | Stores bills pc num mons in box. | `ram/wram.asm:458` |
| `wBillsPC_NumMonsOnScreen` | `$C9DE` | 1 | Stores bills pc num mons on screen. | `ram/wram.asm:459` |
| `wBillsPC_LoadedBox` | `$C9DF` | 1 | 0 if party, 1 - 14 if box, 15 if active box. | `ram/wram.asm:460` |
| `wBillsPC_BackupScrollPosition` | `$C9E0` | 1 | Stores bills pc backup scroll position. | `ram/wram.asm:461` |
| `wBillsPC_BackupCursorPosition` | `$C9E1` | 1 | Stores bills pc backup cursor position. | `ram/wram.asm:462` |
| `wBillsPC_BackupLoadedBox` | `$C9E2` | 1 | Stores bills pc backup loaded box. | `ram/wram.asm:463` |
| `wBillsPC_MonHasMail` | `$C9E3` | 1 | Stores bills pc mon has mail. | `ram/wram.asm:464` |
| `wBillsPCDataEnd` | `$C9E9` | alias | End marker for Bills PC Data. | `ram/wram.asm:466` |
| `wHallOfFamePokemonList` | `$C6B1-$C712` | 98 | Hall of Fame team buffer used before writing to SRAM. | `ram/wram.asm:472` |
| `wDebugOriginalColors` | `$C6B1-$CAB0` | 1024 | debug color picker. | `ram/wram.asm:478` |
| `wUnusedPikachuFrameset` | `$C6B5` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:485` |
| `wUnusedJigglypuffNoteXCoord` | `$C6C8` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:487` |
| `wLinkData` | `$C6B1-$CBC4` | 1300 | Raw 1300-byte serial link data buffer. | `ram/wram.asm:493` |
| `wLinkDataEnd` | `$CBC5` | alias | End marker for Link Data. | `ram/wram.asm:494` |
| `wLinkPlayerName` | `$C6B1-$C6BB` | 11 | link data members. | `ram/wram.asm:500` |
| `wLinkPartyCount` | `$C6BC` | 1 | Stores link party count. | `ram/wram.asm:501` |
| `wLinkPartySpecies` | `$C6BD-$C6C2` | 6 | Stores link party species. | `ram/wram.asm:502` |
| `wLinkPartyEnd` | `$C6C3` | 1 | End marker for Link Party. | `ram/wram.asm:503` |
| `wLinkPlayerData` | `$C6C4` | alias | link player data. | `ram/wram.asm:507` |
| `wLinkPlayerPartyMon1` | `$C6C4-$C6F3` | 48 | Linked player's party monster 1. | `ram/wram.asm:510` |
| `wLinkPlayerPartyMon2` | `$C6F4-$C723` | 48 | Linked player's party monster 2. | `ram/wram.asm:510` |
| `wLinkPlayerPartyMon3` | `$C724-$C753` | 48 | Linked player's party monster 3. | `ram/wram.asm:510` |
| `wLinkPlayerPartyMon4` | `$C754-$C783` | 48 | Linked player's party monster 4. | `ram/wram.asm:510` |
| `wLinkPlayerPartyMon5` | `$C784-$C7B3` | 48 | Linked player's party monster 5. | `ram/wram.asm:510` |
| `wLinkPlayerPartyMon6` | `$C7B4-$C7E3` | 48 | Linked player's party monster 6. | `ram/wram.asm:510` |
| `wLinkPlayerPartyMonOTs` | `$C7E4` | alias | Stores link player party mon o ts. | `ram/wram.asm:513` |
| `wLinkPlayerPartyMon1OT` | `$C7E4-$C7EE` | 11 | Linked player's OT name for monster 1. | `ram/wram.asm:516` |
| `wLinkPlayerPartyMon2OT` | `$C7EF-$C7F9` | 11 | Linked player's OT name for monster 2. | `ram/wram.asm:516` |
| `wLinkPlayerPartyMon3OT` | `$C7FA-$C804` | 11 | Linked player's OT name for monster 3. | `ram/wram.asm:516` |
| `wLinkPlayerPartyMon4OT` | `$C805-$C80F` | 11 | Linked player's OT name for monster 4. | `ram/wram.asm:516` |
| `wLinkPlayerPartyMon5OT` | `$C810-$C81A` | 11 | Linked player's OT name for monster 5. | `ram/wram.asm:516` |
| `wLinkPlayerPartyMon6OT` | `$C81B-$C825` | 11 | Linked player's OT name for monster 6. | `ram/wram.asm:516` |
| `wLinkPlayerPartyMonNicknames` | `$C826` | alias | Buffer/data field for link player party mon nicknames. | `ram/wram.asm:519` |
| `wLinkPlayerPartyMon1Nickname` | `$C826-$C830` | 11 | Linked player's nickname for monster 1. | `ram/wram.asm:522` |
| `wLinkPlayerPartyMon2Nickname` | `$C831-$C83B` | 11 | Linked player's nickname for monster 2. | `ram/wram.asm:522` |
| `wLinkPlayerPartyMon3Nickname` | `$C83C-$C846` | 11 | Linked player's nickname for monster 3. | `ram/wram.asm:522` |
| `wLinkPlayerPartyMon4Nickname` | `$C847-$C851` | 11 | Linked player's nickname for monster 4. | `ram/wram.asm:522` |
| `wLinkPlayerPartyMon5Nickname` | `$C852-$C85C` | 11 | Linked player's nickname for monster 5. | `ram/wram.asm:522` |
| `wLinkPlayerPartyMon6Nickname` | `$C85D-$C867` | 11 | Linked player's nickname for monster 6. | `ram/wram.asm:522` |
| `wTimeCapsulePlayerData` | `$C6C4` | alias | time capsule party data. | `ram/wram.asm:527` |
| `wTimeCapsulePartyMon1` | `$C6C4-$C6EF` | 44 | Time Capsule party monster 1 in Gen I-compatible format. | `ram/wram.asm:530` |
| `wTimeCapsulePartyMon2` | `$C6F0-$C71B` | 44 | Time Capsule party monster 2 in Gen I-compatible format. | `ram/wram.asm:530` |
| `wTimeCapsulePartyMon3` | `$C71C-$C747` | 44 | Time Capsule party monster 3 in Gen I-compatible format. | `ram/wram.asm:530` |
| `wTimeCapsulePartyMon4` | `$C748-$C773` | 44 | Time Capsule party monster 4 in Gen I-compatible format. | `ram/wram.asm:530` |
| `wTimeCapsulePartyMon5` | `$C774-$C79F` | 44 | Time Capsule party monster 5 in Gen I-compatible format. | `ram/wram.asm:530` |
| `wTimeCapsulePartyMon6` | `$C7A0-$C7CB` | 44 | Time Capsule party monster 6 in Gen I-compatible format. | `ram/wram.asm:530` |
| `wTimeCapsulePartyMonOTs` | `$C7CC` | alias | Stores time capsule party mon o ts. | `ram/wram.asm:533` |
| `wTimeCapsulePartyMon1OT` | `$C7CC-$C7D6` | 11 | Time Capsule OT name for monster 1. | `ram/wram.asm:536` |
| `wTimeCapsulePartyMon2OT` | `$C7D7-$C7E1` | 11 | Time Capsule OT name for monster 2. | `ram/wram.asm:536` |
| `wTimeCapsulePartyMon3OT` | `$C7E2-$C7EC` | 11 | Time Capsule OT name for monster 3. | `ram/wram.asm:536` |
| `wTimeCapsulePartyMon4OT` | `$C7ED-$C7F7` | 11 | Time Capsule OT name for monster 4. | `ram/wram.asm:536` |
| `wTimeCapsulePartyMon5OT` | `$C7F8-$C802` | 11 | Time Capsule OT name for monster 5. | `ram/wram.asm:536` |
| `wTimeCapsulePartyMon6OT` | `$C803-$C80D` | 11 | Time Capsule OT name for monster 6. | `ram/wram.asm:536` |
| `wTimeCapsulePartyMonNicknames` | `$C80E` | alias | Buffer/data field for time capsule party mon nicknames. | `ram/wram.asm:539` |
| `wTimeCapsulePartyMon1Nickname` | `$C80E-$C818` | 11 | Time Capsule nickname for monster 1. | `ram/wram.asm:542` |
| `wTimeCapsulePartyMon2Nickname` | `$C819-$C823` | 11 | Time Capsule nickname for monster 2. | `ram/wram.asm:542` |
| `wTimeCapsulePartyMon3Nickname` | `$C824-$C82E` | 11 | Time Capsule nickname for monster 3. | `ram/wram.asm:542` |
| `wTimeCapsulePartyMon4Nickname` | `$C82F-$C839` | 11 | Time Capsule nickname for monster 4. | `ram/wram.asm:542` |
| `wTimeCapsulePartyMon5Nickname` | `$C83A-$C844` | 11 | Time Capsule nickname for monster 5. | `ram/wram.asm:542` |
| `wTimeCapsulePartyMon6Nickname` | `$C845-$C84F` | 11 | Time Capsule nickname for monster 6. | `ram/wram.asm:542` |
| `wCurLinkOTPartyMonTypePointer` | `$CA99-$CA9A` | 2 | Pointer/address for Cur Link OT Party Mon Type Pointer. | `ram/wram.asm:552` |
| `wLinkOTPartyMonTypes` | `$CA9B` | alias | Stores link ot party mon types. | `ram/wram.asm:554` |
| `wLinkOTPartyMon1Type` | `$CA9B-$CA9C` | 2 | wLinkOTPartyMon1Type - wLinkOTPartyMon6Type. | `ram/wram.asm:557` |
| `wLinkOTPartyMon2Type` | `$CA9D-$CA9E` | 2 | Stores link ot party mon2 type. | `ram/wram.asm:557` |
| `wLinkOTPartyMon3Type` | `$CA9F-$CAA0` | 2 | Stores link ot party mon3 type. | `ram/wram.asm:557` |
| `wLinkOTPartyMon4Type` | `$CAA1-$CAA2` | 2 | Stores link ot party mon4 type. | `ram/wram.asm:557` |
| `wLinkOTPartyMon5Type` | `$CAA3-$CAA4` | 2 | Stores link ot party mon5 type. | `ram/wram.asm:557` |
| `wLinkOTPartyMon6Type` | `$CAA5-$CAA6` | 2 | Stores link ot party mon6 type. | `ram/wram.asm:557` |
| `wLinkPlayerMail` | `$C8A5-$CA2A` | 390 | Alias for the start of Link Player Mail block. | `ram/wram.asm:565` |
| `wLinkPlayerMailPreamble` | `$C8A5-$C8A9` | 5 | Stores link player mail preamble. | `ram/wram.asm:566` |
| `wLinkPlayerMailMessages` | `$C8AA-$C96F` | 198 | Stores link player mail messages. | `ram/wram.asm:567` |
| `wLinkPlayerMailMetadata` | `$C970-$C9C3` | 84 | Stores link player mail metadata. | `ram/wram.asm:568` |
| `wLinkPlayerMailPatchSet` | `$C9C4-$CA2A` | 103 | Stores link player mail patch set. | `ram/wram.asm:569` |
| `wLinkPlayerMailEnd` | `$CA2B-$CA34` | 10 | End marker for Link Player Mail. | `ram/wram.asm:570` |
| `wLinkOTMail` | `$CA35-$CBBA` | 390 | Alias for the start of Link OT Mail block. | `ram/wram.asm:572` |
| `wLinkOTMailMessages` | `$CA35-$CAFA` | 198 | Stores link ot mail messages. | `ram/wram.asm:573` |
| `wLinkOTMailMetadata` | `$CAFB-$CB4E` | 84 | Stores link ot mail metadata. | `ram/wram.asm:574` |
| `wLinkOTMailPatchSet` | `$CB4F-$CBB5` | 103 | Stores link ot mail patch set. | `ram/wram.asm:575` |
| `wLinkOTMailPadding` | `$CBB6-$CBBA` | 5 | Stores link ot mail padding. | `ram/wram.asm:576` |
| `wLinkOTMailEnd` | `$CBBB-$CBC4` | 10 | End marker for Link OT Mail. | `ram/wram.asm:577` |
| `wLinkReceivedMail` | `$C8A5-$C9BE` | 282 | Alias for the start of Link Received Mail block. | `ram/wram.asm:585` |
| `wLinkReceivedMailEnd` | `$C9BF` | 1 | End marker for Link Received Mail. | `ram/wram.asm:586` |
| `wMysteryGiftStaging` | `$C6B1-$C700` | 80 | mystery gift data. | `ram/wram.asm:592` |
| `wMysteryGiftTrainer` | `$C701-$C726` | 38 | Mystery Gift trainer-party payload buffer. | `ram/wram.asm:594` |
| `wMysteryGiftTrainerEnd` | `$C727-$C7B0` | 138 | End marker for Mystery Gift Trainer. | `ram/wram.asm:595` |
| `wMysteryGiftPartnerData` | `$C7B1-$C7C4` | 20 | Alias for the start of Mystery Gift Partner Data block. | `ram/wram.asm:599` |
| `wMysteryGiftPartnerGameVersion` | `$C7B1` | 1 | Stores mystery gift partner game version. | `ram/wram.asm:600` |
| `wMysteryGiftPartnerID` | `$C7B2-$C7B3` | 2 | Stores mystery gift partner id. | `ram/wram.asm:601` |
| `wMysteryGiftPartnerName` | `$C7B4-$C7BE` | 11 | Buffer/data field for mystery gift partner name. | `ram/wram.asm:602` |
| `wMysteryGiftPartnerDexCaught` | `$C7BF` | 1 | Stores mystery gift partner dex caught. | `ram/wram.asm:603` |
| `wMysteryGiftPartnerSentDeco` | `$C7C0` | 1 | Stores mystery gift partner sent deco. | `ram/wram.asm:604` |
| `wMysteryGiftPartnerWhichItem` | `$C7C1` | 1 | Stores mystery gift partner which item. | `ram/wram.asm:605` |
| `wMysteryGiftPartnerWhichDeco` | `$C7C2` | 1 | Stores mystery gift partner which deco. | `ram/wram.asm:606` |
| `wMysteryGiftPartnerBackupItem` | `$C7C3` | 1 | Stores mystery gift partner backup item. | `ram/wram.asm:607` |
| `wMysteryGiftPartnerDataEnd` | `$C7C5-$C800` | 60 | End marker for Mystery Gift Partner Data. | `ram/wram.asm:609` |
| `wMysteryGiftPlayerData` | `$C801-$C814` | 20 | Alias for the start of Mystery Gift Player Data block. | `ram/wram.asm:613` |
| `wMysteryGiftPlayerGameVersion` | `$C801` | 1 | Stores mystery gift player game version. | `ram/wram.asm:614` |
| `wMysteryGiftPlayerID` | `$C802-$C803` | 2 | Stores mystery gift player id. | `ram/wram.asm:615` |
| `wMysteryGiftPlayerName` | `$C804-$C80E` | 11 | Buffer/data field for mystery gift player name. | `ram/wram.asm:616` |
| `wMysteryGiftPlayerDexCaught` | `$C80F` | 1 | Stores mystery gift player dex caught. | `ram/wram.asm:617` |
| `wMysteryGiftPlayerSentDeco` | `$C810` | 1 | Stores mystery gift player sent deco. | `ram/wram.asm:618` |
| `wMysteryGiftPlayerWhichItem` | `$C811` | 1 | Stores mystery gift player which item. | `ram/wram.asm:619` |
| `wMysteryGiftPlayerWhichDeco` | `$C812` | 1 | Stores mystery gift player which deco. | `ram/wram.asm:620` |
| `wMysteryGiftPlayerBackupItem` | `$C813` | 1 | Stores mystery gift player backup item. | `ram/wram.asm:621` |
| `wMysteryGiftPlayerDataEnd` | `$C815` | alias | End marker for Mystery Gift Player Data. | `ram/wram.asm:623` |
| `wLYOverrides` | `$C6B8-$C747` | 144 | Per-scanline LCD register override buffer used by cutscenes/credits effects. | `ram/wram.asm:629` |
| `wLYOverridesEnd` | `$C748` | alias | End marker for LY Overrides. | `ram/wram.asm:630` |
| `wLYOverrides2` | `$C758-$C7E7` | 144 | Secondary per-scanline LCD override buffer. | `ram/wram.asm:634` |
| `wLYOverrides2End` | `$C7E8` | alias | End marker for LY Overrides2. | `ram/wram.asm:635` |
| `wLYOverridesBackup` | `$C7B8-$C847` | 144 | Backup copy of LY override data. | `ram/wram.asm:641` |
| `wLYOverridesBackupEnd` | `$C848` | alias | End marker for LY Overrides Backup. | `ram/wram.asm:642` |
| `wCreditsBlankFrame2bpp` | `$C8B8-$C9B7` | 256 | blank credits tile buffer. | `ram/wram.asm:649` |
| `wCreditsBlankFrame2bppEnd` | `$C9B8` | alias | End marker for Credits Blank Frame2bpp. | `ram/wram.asm:650` |
| `wUnusedMysteryGiftStagedDataLength` | `$C8B8` | 1 | mystery gift data. | `ram/wram.asm:654` |
| `wMysteryGiftMessageCount` | `$C8B9` | 1 | Stores mystery gift message count. | `ram/wram.asm:655` |
| `wMysteryGiftStagedDataLength` | `$C8BA` | 1 | Buffer/data field for mystery gift staged data length. | `ram/wram.asm:656` |
| `wBattleAnimTileDict` | `$C8B8-$C8C1` | 10 | battle. | `ram/wram.asm:660` |
| `wActiveAnimObjects` | `$C8C2` | alias | Stores active anim objects. | `ram/wram.asm:666` |
| `wAnimObject1` | `$C8C2-$C8D9` | 24 | Active battle animation object struct 1. | `ram/wram.asm:669` |
| `wAnimObject2` | `$C8DA-$C8F1` | 24 | Active battle animation object struct 2. | `ram/wram.asm:669` |
| `wAnimObject3` | `$C8F2-$C909` | 24 | Active battle animation object struct 3. | `ram/wram.asm:669` |
| `wAnimObject4` | `$C90A-$C921` | 24 | Active battle animation object struct 4. | `ram/wram.asm:669` |
| `wAnimObject5` | `$C922-$C939` | 24 | Active battle animation object struct 5. | `ram/wram.asm:669` |
| `wAnimObject6` | `$C93A-$C951` | 24 | Active battle animation object struct 6. | `ram/wram.asm:669` |
| `wAnimObject7` | `$C952-$C969` | 24 | Active battle animation object struct 7. | `ram/wram.asm:669` |
| `wAnimObject8` | `$C96A-$C981` | 24 | Active battle animation object struct 8. | `ram/wram.asm:669` |
| `wAnimObject9` | `$C982-$C999` | 24 | Active battle animation object struct 9. | `ram/wram.asm:669` |
| `wAnimObject10` | `$C99A-$C9B1` | 24 | Active battle animation object struct 10. | `ram/wram.asm:669` |
| `wActiveBGEffects` | `$C9B2` | alias | Stores active bg effects. | `ram/wram.asm:672` |
| `wBGEffect1` | `$C9B2-$C9B5` | 4 | Active battle BG-effect struct 1. | `ram/wram.asm:675` |
| `wBGEffect2` | `$C9B6-$C9B9` | 4 | Active battle BG-effect struct 2. | `ram/wram.asm:675` |
| `wBGEffect3` | `$C9BA-$C9BD` | 4 | Active battle BG-effect struct 3. | `ram/wram.asm:675` |
| `wBGEffect4` | `$C9BE-$C9C1` | 4 | Active battle BG-effect struct 4. | `ram/wram.asm:675` |
| `wBGEffect5` | `$C9C2-$C9C5` | 4 | Active battle BG-effect struct 5. | `ram/wram.asm:675` |
| `wLastAnimObjectIndex` | `$C9C6` | 1 | Stores last anim object index. | `ram/wram.asm:678` |
| `wBattleAnimFlags` | `$C9C7` | 1 | Stores battle anim flags. | `ram/wram.asm:680` |
| `wBattleAnimAddress` | `$C9C8-$C9C9` | 2 | Pointer/address for Battle Anim Address. | `ram/wram.asm:681` |
| `wBattleAnimDelay` | `$C9CA` | 1 | Stores battle anim delay. | `ram/wram.asm:682` |
| `wBattleAnimParent` | `$C9CB-$C9CC` | 2 | Stores battle anim parent. | `ram/wram.asm:683` |
| `wBattleAnimLoops` | `$C9CD` | 1 | Stores battle anim loops. | `ram/wram.asm:684` |
| `wBattleAnimVar` | `$C9CE` | 1 | Stores battle anim var. | `ram/wram.asm:685` |
| `wBattleAnimByte` | `$C9CF` | 1 | Stores battle anim byte. | `ram/wram.asm:686` |
| `wBattleAnimOAMPointerLo` | `$C9D0` | 1 | Pointer/address for Battle Anim OAM Pointer Lo. | `ram/wram.asm:687` |
| `wBattleObjectTempID` | `$C8B8` | 1 | Stores battle object temp id. | `ram/wram.asm:690` |
| `wBattleObjectTempXCoord` | `$C8B9` | 1 | Stores battle object temp x coord. | `ram/wram.asm:691` |
| `wBattleObjectTempYCoord` | `$C8BA` | 1 | Stores battle object temp y coord. | `ram/wram.asm:692` |
| `wBattleObjectTempParam` | `$C8BB` | 1 | Stores battle object temp param. | `ram/wram.asm:693` |
| `wBattleBGEffectTempID` | `$C8B8` | 1 | Stores battle bg effect temp id. | `ram/wram.asm:696` |
| `wBattleBGEffectTempJumptableIndex` | `$C8B9` | 1 | Stores battle bg effect temp jumptable index. | `ram/wram.asm:697` |
| `wBattleBGEffectTempTurn` | `$C8BA` | 1 | Stores battle bg effect temp turn. | `ram/wram.asm:698` |
| `wBattleBGEffectTempParam` | `$C8BB` | 1 | Stores battle bg effect temp param. | `ram/wram.asm:699` |
| `wBattleAnimTempOAMFlags` | `$C8B8` | 1 | Stores battle anim temp oam flags. | `ram/wram.asm:702` |
| `wBattleAnimTempFixY` | `$C8B9` | 1 | Stores battle anim temp fix y. | `ram/wram.asm:703` |
| `wBattleAnimTempTileID` | `$C8BA` | 1 | Stores battle anim temp tile id. | `ram/wram.asm:704` |
| `wBattleAnimTempXCoord` | `$C8BB` | 1 | Stores battle anim temp x coord. | `ram/wram.asm:705` |
| `wBattleAnimTempYCoord` | `$C8BC` | 1 | Stores battle anim temp y coord. | `ram/wram.asm:706` |
| `wBattleAnimTempXOffset` | `$C8BD` | 1 | Stores battle anim temp x offset. | `ram/wram.asm:707` |
| `wBattleAnimTempYOffset` | `$C8BE` | 1 | Stores battle anim temp y offset. | `ram/wram.asm:708` |
| `wBattleAnimTempFrameOAMFlags` | `$C8BF` | 1 | Stores battle anim temp frame oam flags. | `ram/wram.asm:709` |
| `wBattleAnimTempPalette` | `$C8C0` | 1 | Stores battle anim temp palette. | `ram/wram.asm:710` |
| `wBattleAnimGFXTempTileID` | `$C8B8` | alias | Stores battle anim gfx temp tile id. | `ram/wram.asm:713` |
| `wBattleAnimGFXTempPicHeight` | `$C8B8` | 1 | Stores battle anim gfx temp pic height. | `ram/wram.asm:714` |
| `wBattleSineWaveTempProgress` | `$C8B8` | 1 | Stores battle sine wave temp progress. | `ram/wram.asm:717` |
| `wBattleSineWaveTempOffset` | `$C8B9` | 1 | Stores battle sine wave temp offset. | `ram/wram.asm:718` |
| `wBattleSineWaveTempAmplitude` | `$C8BA` | 1 | Stores battle sine wave temp amplitude. | `ram/wram.asm:719` |
| `wBattleSineWaveTempTimer` | `$C8BB` | 1 | Stores battle sine wave temp timer. | `ram/wram.asm:720` |
| `wBattlePicResizeTempBaseTileID` | `$C8B8` | 1 | Stores battle pic resize temp base tile id. | `ram/wram.asm:723` |
| `wBattlePicResizeTempPointer` | `$C8B9-$C8BA` | 2 | Pointer/address for Battle Pic Resize Temp Pointer. | `ram/wram.asm:724` |
| `wBattleAnimEnd` | `$C8EA` | alias | End marker for Battle Anim. | `ram/wram.asm:729` |
| `wSurfWaveBGEffect` | `$C8B8-$C8F7` | 64 | Alias for the start of Surf Wave BG Effect block. | `ram/wram.asm:732` |
| `wSurfWaveBGEffectEnd` | `$C8F8` | alias | End marker for Surf Wave BG Effect. | `ram/wram.asm:733` |
| `wBattle` | `$CAA0-$CBD6` | 311 | Main battle-state overlay block. | `ram/wram.asm:738` |
| `wEnemyMoveStruct` | `$CAA0-$CAA6` | 7 | Stores enemy move struct. | `ram/wram.asm:739` |
| `wPlayerMoveStruct` | `$CAA7-$CAAD` | 7 | Stores player move struct. | `ram/wram.asm:740` |
| `wEnemyMonNickname` | `$CAAE-$CAB8` | 11 | Buffer/data field for enemy mon nickname. | `ram/wram.asm:742` |
| `wBattleMonNickname` | `$CAB9-$CAC3` | 11 | Buffer/data field for battle mon nickname. | `ram/wram.asm:743` |
| `wBattleMon` | `$C8B8-$C8D7` | 32 | Player-side active battler struct (battle_struct, 32 bytes). | `ram/wram.asm:747` |
| `wIntroJumptableIndex` | `$C8BC` | 1 | Stores intro jumptable index. | `ram/wram.asm:752` |
| `wIntroBGMapPointer` | `$C8BD-$C8BE` | 2 | Pointer/address for Intro BG Map Pointer. | `ram/wram.asm:753` |
| `wIntroTilemapPointer` | `$C8BF-$C8C0` | 2 | Pointer/address for Intro Tilemap Pointer. | `ram/wram.asm:754` |
| `wIntroTilesPointer` | `$C8C1-$C8C2` | 2 | Pointer/address for Intro Tiles Pointer. | `ram/wram.asm:755` |
| `wIntroFrameCounter1` | `$C8C3` | 1 | Stores intro frame counter1. | `ram/wram.asm:756` |
| `wIntroFrameCounter2` | `$C8C4` | 1 | Stores intro frame counter2. | `ram/wram.asm:757` |
| `wIntroSpriteStateFlag` | `$C8C5` | 1 | Stores intro sprite state flag. | `ram/wram.asm:758` |
| `wEnemyTrainerItem1` | `$CAE6` | 1 | Stores enemy trainer item1. | `ram/wram.asm:763` |
| `wEnemyTrainerItem2` | `$CAE7` | 1 | Stores enemy trainer item2. | `ram/wram.asm:764` |
| `wEnemyTrainerBaseReward` | `$CAE8` | 1 | Stores enemy trainer base reward. | `ram/wram.asm:765` |
| `wEnemyTrainerAIFlags` | `$CAE9-$CAEB` | 3 | Stores enemy trainer ai flags. | `ram/wram.asm:766` |
| `wOTClassName` | `$CAEC-$CAF8` | 13 | Buffer/data field for ot class name. | `ram/wram.asm:767` |
| `wCurOTMon` | `$CAF9` | 1 | Stores cur ot mon. | `ram/wram.asm:769` |
| `wBattleParticipantsNotFainted` | `$CAFA` | 1 | Stores battle participants not fainted. | `ram/wram.asm:771` |
| `wTypeModifier` | `$CAFB` | 1 | Stores type modifier. | `ram/wram.asm:779` |
| `wCriticalHit` | `$CAFC` | 1 | Stores critical hit. | `ram/wram.asm:786` |
| `wAttackMissed` | `$CAFD` | 1 | Stores attack missed. | `ram/wram.asm:792` |
| `wPlayerSubStatus1` | `$CAFE` | 1 | Stores player sub status1. | `ram/wram.asm:796` |
| `wPlayerSubStatus2` | `$CAFF` | 1 | Stores player sub status2. | `ram/wram.asm:797` |
| `wPlayerSubStatus3` | `$CB00` | 1 | Stores player sub status3. | `ram/wram.asm:798` |
| `wPlayerSubStatus4` | `$CB01` | 1 | Stores player sub status4. | `ram/wram.asm:799` |
| `wPlayerSubStatus5` | `$CB02` | 1 | Stores player sub status5. | `ram/wram.asm:800` |
| `wEnemySubStatus1` | `$CB03` | 1 | Stores enemy sub status1. | `ram/wram.asm:802` |
| `wEnemySubStatus2` | `$CB04` | 1 | Stores enemy sub status2. | `ram/wram.asm:803` |
| `wEnemySubStatus3` | `$CB05` | 1 | Stores enemy sub status3. | `ram/wram.asm:804` |
| `wEnemySubStatus4` | `$CB06` | 1 | Stores enemy sub status4. | `ram/wram.asm:805` |
| `wEnemySubStatus5` | `$CB07` | 1 | Stores enemy sub status5. | `ram/wram.asm:806` |
| `wPlayerRolloutCount` | `$CB08` | 1 | Stores player rollout count. | `ram/wram.asm:808` |
| `wPlayerConfuseCount` | `$CB09` | 1 | Stores player confuse count. | `ram/wram.asm:809` |
| `wPlayerToxicCount` | `$CB0A` | 1 | Stores player toxic count. | `ram/wram.asm:810` |
| `wPlayerDisableCount` | `$CB0B` | 1 | Stores player disable count. | `ram/wram.asm:811` |
| `wPlayerEncoreCount` | `$CB0C` | 1 | Stores player encore count. | `ram/wram.asm:812` |
| `wPlayerPerishCount` | `$CB0D` | 1 | Stores player perish count. | `ram/wram.asm:813` |
| `wPlayerFuryCutterCount` | `$CB0E` | 1 | Stores player fury cutter count. | `ram/wram.asm:814` |
| `wPlayerProtectCount` | `$CB0F` | 1 | Stores player protect count. | `ram/wram.asm:815` |
| `wEnemyRolloutCount` | `$CB10` | 1 | Stores enemy rollout count. | `ram/wram.asm:817` |
| `wEnemyConfuseCount` | `$CB11` | 1 | Stores enemy confuse count. | `ram/wram.asm:818` |
| `wEnemyToxicCount` | `$CB12` | 1 | Stores enemy toxic count. | `ram/wram.asm:819` |
| `wEnemyDisableCount` | `$CB13` | 1 | Stores enemy disable count. | `ram/wram.asm:820` |
| `wEnemyEncoreCount` | `$CB14` | 1 | Stores enemy encore count. | `ram/wram.asm:821` |
| `wEnemyPerishCount` | `$CB15` | 1 | Stores enemy perish count. | `ram/wram.asm:822` |
| `wEnemyFuryCutterCount` | `$CB16` | 1 | Stores enemy fury cutter count. | `ram/wram.asm:823` |
| `wEnemyProtectCount` | `$CB17` | 1 | Stores enemy protect count. | `ram/wram.asm:824` |
| `wPlayerDamageTaken` | `$CB18-$CB19` | 2 | Stores player damage taken. | `ram/wram.asm:826` |
| `wEnemyDamageTaken` | `$CB1A-$CB1B` | 2 | Stores enemy damage taken. | `ram/wram.asm:827` |
| `wBattleReward` | `$CB1C-$CB1E` | 3 | Stores battle reward. | `ram/wram.asm:829` |
| `wBattleAnimParam` | `$CB1F` | 1 | Stores battle anim param. | `ram/wram.asm:831` |
| `wBattleScriptBuffer` | `$CB20-$CB47` | 40 | Buffer/data field for battle script buffer. | `ram/wram.asm:833` |
| `wBattleScriptBufferAddress` | `$CB48-$CB49` | 2 | Pointer/address for Battle Script Buffer Address. | `ram/wram.asm:835` |
| `wTurnEnded` | `$CB4A` | 1 | Stores turn ended. | `ram/wram.asm:836` |
| `wPlayerStats` | `$CB4C` | alias | Stores player stats. | `ram/wram.asm:840` |
| `wPlayerAttack` | `$CB4C-$CB4D` | 2 | Stores player attack. | `ram/wram.asm:841` |
| `wPlayerDefense` | `$CB4E-$CB4F` | 2 | Stores player defense. | `ram/wram.asm:842` |
| `wPlayerSpeed` | `$CB50-$CB51` | 2 | Stores player speed. | `ram/wram.asm:843` |
| `wPlayerSpAtk` | `$CB52-$CB53` | 2 | Stores player sp atk. | `ram/wram.asm:844` |
| `wPlayerSpDef` | `$CB54-$CB55` | 2 | Stores player sp def. | `ram/wram.asm:845` |
| `wEnemyStats` | `$CB57` | alias | Stores enemy stats. | `ram/wram.asm:848` |
| `wEnemyAttack` | `$CB57-$CB58` | 2 | Stores enemy attack. | `ram/wram.asm:849` |
| `wEnemyDefense` | `$CB59-$CB5A` | 2 | Stores enemy defense. | `ram/wram.asm:850` |
| `wEnemySpeed` | `$CB5B-$CB5C` | 2 | Stores enemy speed. | `ram/wram.asm:851` |
| `wEnemySpAtk` | `$CB5D-$CB5E` | 2 | Stores enemy sp atk. | `ram/wram.asm:852` |
| `wEnemySpDef` | `$CB5F-$CB60` | 2 | Stores enemy sp def. | `ram/wram.asm:853` |
| `wPlayerStatLevels` | `$CB62` | alias | Stores player stat levels. | `ram/wram.asm:856` |
| `wPlayerAtkLevel` | `$CB62` | 1 | Stores player atk level. | `ram/wram.asm:857` |
| `wPlayerDefLevel` | `$CB63` | 1 | Stores player def level. | `ram/wram.asm:858` |
| `wPlayerSpdLevel` | `$CB64` | 1 | Stores player spd level. | `ram/wram.asm:859` |
| `wPlayerSAtkLevel` | `$CB65` | 1 | Stores player s atk level. | `ram/wram.asm:860` |
| `wPlayerSDefLevel` | `$CB66` | 1 | Stores player s def level. | `ram/wram.asm:861` |
| `wPlayerAccLevel` | `$CB67` | 1 | Stores player acc level. | `ram/wram.asm:862` |
| `wPlayerEvaLevel` | `$CB68` | 1 | Stores player eva level. | `ram/wram.asm:863` |
| `wEnemyStatLevels` | `$CB6A` | alias | Stores enemy stat levels. | `ram/wram.asm:866` |
| `wEnemyAtkLevel` | `$CB6A` | 1 | Stores enemy atk level. | `ram/wram.asm:867` |
| `wEnemyDefLevel` | `$CB6B` | 1 | Stores enemy def level. | `ram/wram.asm:868` |
| `wEnemySpdLevel` | `$CB6C` | 1 | Stores enemy spd level. | `ram/wram.asm:869` |
| `wEnemySAtkLevel` | `$CB6D` | 1 | Stores enemy s atk level. | `ram/wram.asm:870` |
| `wEnemySDefLevel` | `$CB6E` | 1 | Stores enemy s def level. | `ram/wram.asm:871` |
| `wEnemyAccLevel` | `$CB6F` | 1 | Stores enemy acc level. | `ram/wram.asm:872` |
| `wEnemyEvaLevel` | `$CB70` | 1 | Stores enemy eva level. | `ram/wram.asm:873` |
| `wEnemyTurnsTaken` | `$CB72` | 1 | Stores enemy turns taken. | `ram/wram.asm:876` |
| `wPlayerTurnsTaken` | `$CB73` | 1 | Stores player turns taken. | `ram/wram.asm:877` |
| `wPlayerSubstituteHP` | `$CB75` | 1 | Stores player substitute hp. | `ram/wram.asm:880` |
| `wEnemySubstituteHP` | `$CB76` | 1 | Stores enemy substitute hp. | `ram/wram.asm:881` |
| `wUnusedPlayerLockedMove` | `$CB77` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:883` |
| `wCurPlayerMove` | `$CB79` | 1 | Stores cur player move. | `ram/wram.asm:886` |
| `wCurEnemyMove` | `$CB7A` | 1 | Stores cur enemy move. | `ram/wram.asm:887` |
| `wLinkBattleRNCount` | `$CB7B` | 1 | Stores link battle rn count. | `ram/wram.asm:889` |
| `wEnemyItemState` | `$CB7C` | 1 | Stores enemy item state. | `ram/wram.asm:893` |
| `wCurEnemyMoveNum` | `$CB7F` | 1 | Stores cur enemy move num. | `ram/wram.asm:895` |
| `wEnemyHPAtTimeOfPlayerSwitch` | `$CB80-$CB81` | 2 | Stores enemy hp at time of player switch. | `ram/wram.asm:897` |
| `wPayDayMoney` | `$CB82-$CB84` | 3 | Stores pay day money. | `ram/wram.asm:898` |
| `wSafariMonAngerCount` | `$CB85` | 1 | unreferenced. | `ram/wram.asm:900` |
| `wSafariMonEating` | `$CB86` | 1 | Stores safari mon eating. | `ram/wram.asm:901` |
| `wEnemyBackupDVs` | `$CB88-$CB89` | 2 | used when enemy is transformed. | `ram/wram.asm:903` |
| `wAlreadyDisobeyed` | `$CB8A` | 1 | Stores already disobeyed. | `ram/wram.asm:904` |
| `wDisabledMove` | `$CB8B` | 1 | Stores disabled move. | `ram/wram.asm:906` |
| `wEnemyDisabledMove` | `$CB8C` | 1 | Stores enemy disabled move. | `ram/wram.asm:907` |
| `wWhichMonFaintedFirst` | `$CB8D` | 1 | Stores which mon fainted first. | `ram/wram.asm:908` |
| `wLastPlayerCounterMove` | `$CB8E` | 1 | exists so you can't counter on switch. | `ram/wram.asm:911` |
| `wLastEnemyCounterMove` | `$CB8F` | 1 | Stores last enemy counter move. | `ram/wram.asm:912` |
| `wEnemyMinimized` | `$CB90` | 1 | Stores enemy minimized. | `ram/wram.asm:914` |
| `wAlreadyFailed` | `$CB91` | 1 | Stores already failed. | `ram/wram.asm:916` |
| `wBattleParticipantsIncludingFainted` | `$CB92` | 1 | Stores battle participants including fainted. | `ram/wram.asm:918` |
| `wBattleLowHealthAlarm` | `$CB93` | 1 | Stores battle low health alarm. | `ram/wram.asm:919` |
| `wPlayerMinimized` | `$CB94` | 1 | Stores player minimized. | `ram/wram.asm:920` |
| `wPlayerScreens` | `$CB95` | 1 | Stores player screens. | `ram/wram.asm:922` |
| `wEnemyScreens` | `$CB96` | 1 | Stores enemy screens. | `ram/wram.asm:932` |
| `wPlayerSafeguardCount` | `$CB97` | 1 | Stores player safeguard count. | `ram/wram.asm:936` |
| `wPlayerLightScreenCount` | `$CB98` | 1 | Stores player light screen count. | `ram/wram.asm:937` |
| `wPlayerReflectCount` | `$CB99` | 1 | Stores player reflect count. | `ram/wram.asm:938` |
| `wEnemySafeguardCount` | `$CB9B` | 1 | Stores enemy safeguard count. | `ram/wram.asm:941` |
| `wEnemyLightScreenCount` | `$CB9C` | 1 | Stores enemy light screen count. | `ram/wram.asm:942` |
| `wEnemyReflectCount` | `$CB9D` | 1 | Stores enemy reflect count. | `ram/wram.asm:943` |
| `wBattleWeather` | `$CBA0` | 1 | Stores battle weather. | `ram/wram.asm:946` |
| `wWeatherCount` | `$CBA1` | 1 | Stores weather count. | `ram/wram.asm:956` |
| `wLoweredStat` | `$CBA2` | 1 | Stores lowered stat. | `ram/wram.asm:960` |
| `wEffectFailed` | `$CBA3` | 1 | Stores effect failed. | `ram/wram.asm:961` |
| `wFailedMessage` | `$CBA4` | 1 | Stores failed message. | `ram/wram.asm:962` |
| `wEnemyGoesFirst` | `$CBA5` | 1 | Stores enemy goes first. | `ram/wram.asm:963` |
| `wPlayerIsSwitching` | `$CBA6` | 1 | Stores player is switching. | `ram/wram.asm:965` |
| `wEnemyIsSwitching` | `$CBA7` | 1 | Stores enemy is switching. | `ram/wram.asm:966` |
| `wPlayerUsedMoves` | `$CBA8-$CBAB` | 4 | Stores player used moves. | `ram/wram.asm:968` |
| `wEnemyAISwitchScore` | `$CBAC` | 1 | Stores enemy ai switch score. | `ram/wram.asm:973` |
| `wEnemySwitchMonParam` | `$CBAD` | 1 | Stores enemy switch mon param. | `ram/wram.asm:974` |
| `wEnemySwitchMonIndex` | `$CBAE` | 1 | Stores enemy switch mon index. | `ram/wram.asm:975` |
| `wTempLevel` | `$CBAF` | 1 | Stores temp level. | `ram/wram.asm:976` |
| `wLastPlayerMon` | `$CBB0` | 1 | Stores last player mon. | `ram/wram.asm:977` |
| `wLastPlayerMove` | `$CBB1` | 1 | Stores last player move. | `ram/wram.asm:978` |
| `wLastEnemyMove` | `$CBB2` | 1 | Stores last enemy move. | `ram/wram.asm:979` |
| `wPlayerFutureSightCount` | `$CBB3` | 1 | Stores player future sight count. | `ram/wram.asm:981` |
| `wEnemyFutureSightCount` | `$CBB4` | 1 | Stores enemy future sight count. | `ram/wram.asm:982` |
| `wGivingExperienceToExpShareHolders` | `$CBB5` | 1 | Stores giving experience to exp share holders. | `ram/wram.asm:984` |
| `wBackupEnemyMonBaseStats` | `$CBB6-$CBBA` | 5 | Stores backup enemy mon base stats. | `ram/wram.asm:986` |
| `wBackupEnemyMonCatchRate` | `$CBBB` | 1 | Stores backup enemy mon catch rate. | `ram/wram.asm:987` |
| `wBackupEnemyMonBaseExp` | `$CBBC` | 1 | Stores backup enemy mon base exp. | `ram/wram.asm:988` |
| `wPlayerFutureSightDamage` | `$CBBD-$CBBE` | 2 | Stores player future sight damage. | `ram/wram.asm:990` |
| `wEnemyFutureSightDamage` | `$CBBF-$CBC0` | 2 | Stores enemy future sight damage. | `ram/wram.asm:991` |
| `wPlayerRageCounter` | `$CBC1` | 1 | Stores player rage counter. | `ram/wram.asm:992` |
| `wEnemyRageCounter` | `$CBC2` | 1 | Stores enemy rage counter. | `ram/wram.asm:993` |
| `wBeatUpHitAtLeastOnce` | `$CBC3` | 1 | Stores beat up hit at least once. | `ram/wram.asm:995` |
| `wPlayerTrappingMove` | `$CBC4` | 1 | Stores player trapping move. | `ram/wram.asm:997` |
| `wEnemyTrappingMove` | `$CBC5` | 1 | Stores enemy trapping move. | `ram/wram.asm:998` |
| `wPlayerWrapCount` | `$CBC6` | 1 | Stores player wrap count. | `ram/wram.asm:999` |
| `wEnemyWrapCount` | `$CBC7` | 1 | Stores enemy wrap count. | `ram/wram.asm:1000` |
| `wPlayerCharging` | `$CBC8` | 1 | Stores player charging. | `ram/wram.asm:1001` |
| `wEnemyCharging` | `$CBC9` | 1 | Stores enemy charging. | `ram/wram.asm:1002` |
| `wBattleEnded` | `$CBCA` | 1 | Stores battle ended. | `ram/wram.asm:1004` |
| `wWildMonMoves` | `$CBCB-$CBCE` | 4 | Stores wild mon moves. | `ram/wram.asm:1006` |
| `wWildMonPP` | `$CBCF-$CBD2` | 4 | Stores wild mon pp. | `ram/wram.asm:1007` |
| `wAmuletCoin` | `$CBD3` | 1 | Stores amulet coin. | `ram/wram.asm:1009` |
| `wSomeoneIsRampaging` | `$CBD4` | 1 | Stores someone is rampaging. | `ram/wram.asm:1011` |
| `wPlayerJustGotFrozen` | `$CBD5` | 1 | Stores player just got frozen. | `ram/wram.asm:1013` |
| `wEnemyJustGotFrozen` | `$CBD6` | 1 | Stores enemy just got frozen. | `ram/wram.asm:1014` |
| `wBattleEnd` | `$CBD7` | 1 | End marker for the main battle-state overlay block. | `ram/wram.asm:1015` |
| `wDebugRoomItemID` | `$C6B1` | 1 | debug room paged values | debug room new item values. | `ram/wram.asm:1028` |
| `wDebugRoomItemQuantity` | `$C6B2` | 1 | Stores debug room item quantity. | `ram/wram.asm:1029` |
| `wDebugRoomMon` | `$C6B1-$C6D0` | 32 | debug room new pokemon values. | `ram/wram.asm:1032` |
| `wDebugRoomMonBox` | `$C6D1` | 1 | Stores debug room mon box. | `ram/wram.asm:1033` |
| `wDebugRoomRTCSec` | `$C6B1` | 1 | debug room RTC values. | `ram/wram.asm:1036` |
| `wDebugRoomRTCMin` | `$C6B2` | 1 | Stores debug room rtc min. | `ram/wram.asm:1037` |
| `wDebugRoomRTCHour` | `$C6B3` | 1 | Stores debug room rtc hour. | `ram/wram.asm:1038` |
| `wDebugRoomRTCDay` | `$C6B4-$C6B5` | 2 | Stores debug room rtc day. | `ram/wram.asm:1039` |
| `wDebugRoomRTCCurSec` | `$C6B6` | 1 | Stores debug room rtc cur sec. | `ram/wram.asm:1040` |
| `wDebugRoomRTCCurMin` | `$C6B7` | 1 | Stores debug room rtc cur min. | `ram/wram.asm:1041` |
| `wDebugRoomRTCCurHour` | `$C6B8` | 1 | Stores debug room rtc cur hour. | `ram/wram.asm:1042` |
| `wDebugRoomRTCCurDay` | `$C6B9-$C6BA` | 2 | Stores debug room rtc cur day. | `ram/wram.asm:1043` |
| `wDebugRoomGBID` | `$C6B1-$C6B2` | 2 | debug room GB ID values. | `ram/wram.asm:1046` |

### Video

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wBGMapBuffer` | `$CBD8-$CBFF` | 40 | Two-row background tile update buffer. | `ram/wram.asm:1055` |
| `wBGMapPalBuffer` | `$CC00-$CC27` | 40 | Two-row attribute/palette update buffer paired with wBGMapBuffer. | `ram/wram.asm:1056` |
| `wBGMapBufferPointers` | `$CC28-$CC4F` | 40 | Pointer/address for BG Map Buffer Pointers. | `ram/wram.asm:1057` |
| `wBGMapBufferEnd` | `$CC50` | alias | End marker for BG Map Buffer. | `ram/wram.asm:1058` |
| `wDefaultSGBLayout` | `$CC50` | 1 | Stores default sgb layout. | `ram/wram.asm:1060` |
| `wPlayerHPPal` | `$CC51` | 1 | Stores player hp pal. | `ram/wram.asm:1062` |
| `wEnemyHPPal` | `$CC52` | 1 | Stores enemy hp pal. | `ram/wram.asm:1063` |
| `wHPPals` | `$CC53-$CC58` | 6 | Stores hp pals. | `ram/wram.asm:1065` |
| `wCurHPPal` | `$CC59` | 1 | Stores cur hp pal. | `ram/wram.asm:1066` |
| `wSGBPals` | `$CC61-$CC90` | 48 | Stores sgb pals. | `ram/wram.asm:1070` |
| `wAttrmap` | `$CC91-$CDF8` | 360 | 20x18 attribute map for CGB BG tile attributes. | `ram/wram.asm:1072` |
| `wAttrmapEnd` | `$CDF9` | alias | End marker for Attrmap. | `ram/wram.asm:1082` |
| `wTileAnimBuffer` | `$CDF9-$CE08` | 16 | Single-tile scratch buffer for animated tile updates. | `ram/wram.asm:1084` |
| `wOtherPlayerLinkMode` | `$CE09` | 1 | link data. | `ram/wram.asm:1088` |
| `wOtherPlayerLinkAction` | `$CE0A` | alias | Stores other player link action. | `ram/wram.asm:1089` |
| `wBattleAction` | `$CE0A` | 1 | Stores battle action. | `ram/wram.asm:1090` |
| `wPlayerLinkAction` | `$CE0E` | 1 | Stores player link action. | `ram/wram.asm:1092` |
| `wUnusedLinkAction` | `$CE0F` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:1093` |
| `wLinkReceivedSyncBuffer` | `$CE09-$CE0D` | 5 | Buffer/data field for link received sync buffer. | `ram/wram.asm:1096` |
| `wLinkPlayerSyncBuffer` | `$CE0E-$CE12` | 5 | Buffer/data field for link player sync buffer. | `ram/wram.asm:1097` |
| `wLinkTimeoutFrames` | `$CE13-$CE14` | 2 | Stores link timeout frames. | `ram/wram.asm:1099` |
| `wLinkByteTimeout` | `$CE15-$CE16` | 2 | Stores link byte timeout. | `ram/wram.asm:1100` |
| `wMonType` | `$CE17` | 1 | Stores mon type. | `ram/wram.asm:1102` |
| `wCurSpecies` | `$CE18` | 1 | Stores cur species. | `ram/wram.asm:1104` |
| `wNamedObjectType` | `$CE19` | 1 | Stores named object type. | `ram/wram.asm:1106` |
| `wJumptableIndex` | `$CE1B` | 1 | Stores jumptable index. | `ram/wram.asm:1110` |
| `wIntroSceneFrameCounter` | `$CE1C` | 1 | intro data. | `ram/wram.asm:1114` |
| `wIntroSceneTimer` | `$CE1D` | 1 | Stores intro scene timer. | `ram/wram.asm:1115` |
| `wTitleScreenSelectedOption` | `$CE1C` | 1 | title data. | `ram/wram.asm:1119` |
| `wTitleScreenTimer` | `$CE1D-$CE1E` | 2 | Stores title screen timer. | `ram/wram.asm:1120` |
| `wCreditsBorderFrame` | `$CE1C` | 1 | credits data. | `ram/wram.asm:1124` |
| `wCreditsBorderMon` | `$CE1D` | 1 | Stores credits border mon. | `ram/wram.asm:1125` |
| `wCreditsLYOverride` | `$CE1E` | 1 | Stores credits ly override. | `ram/wram.asm:1126` |
| `wPrevDexEntryJumptableIndex` | `$CE1C` | 1 | pokedex. | `ram/wram.asm:1130` |
| `wPrevDexEntryBackup` | `$CE1D` | alias | Stores prev dex entry backup. | `ram/wram.asm:1131` |
| `wPokedexStatus` | `$CE1D` | 1 | Stores pokedex status. | `ram/wram.asm:1132` |
| `wUnusedPokedexByte` | `$CE1E` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:1133` |
| `wPokegearCard` | `$CE1C` | 1 | pokegear. | `ram/wram.asm:1137` |
| `wPokegearMapRegion` | `$CE1D` | 1 | Buffer/data field for pokegear map region. | `ram/wram.asm:1138` |
| `wUnusedPokegearByte` | `$CE1E` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:1139` |
| `wPackJumptableIndex` | `$CE1C` | 1 | pack. | `ram/wram.asm:1143` |
| `wCurPocket` | `$CE1D` | 1 | Stores cur pocket. | `ram/wram.asm:1144` |
| `wPackUsedItem` | `$CE1E` | 1 | Stores pack used item. | `ram/wram.asm:1145` |
| `wTrainerCardBadgeFrameCounter` | `$CE1C` | 1 | trainer card badges. | `ram/wram.asm:1149` |
| `wTrainerCardBadgeTileID` | `$CE1D` | 1 | Stores trainer card badge tile id. | `ram/wram.asm:1150` |
| `wTrainerCardBadgeAttributes` | `$CE1E` | 1 | Stores trainer card badge attributes. | `ram/wram.asm:1151` |
| `wSlotsDelay` | `$CE1C` | 1 | slot machine. | `ram/wram.asm:1155` |
| `wUnusedSlotReelIconDelay` | `$CE1E` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:1157` |
| `wCardFlipCursorY` | `$CE1C` | 1 | card flip. | `ram/wram.asm:1161` |
| `wCardFlipCursorX` | `$CE1D` | 1 | Stores card flip cursor x. | `ram/wram.asm:1162` |
| `wCardFlipWhichCard` | `$CE1E` | 1 | Stores card flip which card. | `ram/wram.asm:1163` |
| `wMemoryGameCardChoice` | `$CE1C` | 1 | unused memory game. | `ram/wram.asm:1167` |
| `wMagnetTrainOffset` | `$CE1C` | 1 | magnet train. | `ram/wram.asm:1171` |
| `wMagnetTrainPosition` | `$CE1D` | 1 | Stores magnet train position. | `ram/wram.asm:1172` |
| `wMagnetTrainWaitCounter` | `$CE1E` | 1 | Stores magnet train wait counter. | `ram/wram.asm:1173` |
| `wHoldingUnownPuzzlePiece` | `$CE1C` | 1 | unown puzzle data. | `ram/wram.asm:1177` |
| `wUnownPuzzleCursorPosition` | `$CE1D` | 1 | Stores unown puzzle cursor position. | `ram/wram.asm:1178` |
| `wUnownPuzzleHeldPiece` | `$CE1E` | 1 | Stores unown puzzle held piece. | `ram/wram.asm:1179` |
| `wBattleTransitionCounter` | `$CE1C` | 1 | battle transitions. | `ram/wram.asm:1183` |
| `wBattleTransitionSineWaveOffset` | `$CE1D` | alias | Stores battle transition sine wave offset. | `ram/wram.asm:1184` |
| `wBattleTransitionSpinQuadrant` | `$CE1D` | 1 | Stores battle transition spin quadrant. | `ram/wram.asm:1185` |
| `wUnusedBillsPCData` | `$CE1C-$CE1E` | 3 | bill's pc. | `ram/wram.asm:1189` |
| `wDebugColorRGBJumptableIndex` | `$CE1C` | 1 | debug mon color picker. | `ram/wram.asm:1193` |
| `wDebugColorCurColor` | `$CE1D` | 1 | Stores debug color cur color. | `ram/wram.asm:1194` |
| `wDebugColorCurMon` | `$CE1E` | 1 | Stores debug color cur mon. | `ram/wram.asm:1195` |
| `wDebugTilesetCurPalette` | `$CE1C` | 1 | debug tileset color picker. | `ram/wram.asm:1199` |
| `wDebugTilesetRGBJumptableIndex` | `$CE1D` | 1 | Stores debug tileset rgb jumptable index. | `ram/wram.asm:1200` |
| `wDebugTilesetCurColor` | `$CE1E` | 1 | Stores debug tileset cur color. | `ram/wram.asm:1201` |
| `wFrameCounter` | `$CE1C` | alias | miscellaneous. | `ram/wram.asm:1205` |
| `wMomBankDigitCursorPosition` | `$CE1C` | alias | miscellaneous. | `ram/wram.asm:1206` |
| `wNamingScreenLetterCase` | `$CE1C` | alias | miscellaneous. | `ram/wram.asm:1207` |
| `wHallOfFameMonCounter` | `$CE1C` | alias | miscellaneous. | `ram/wram.asm:1208` |
| `wTradeDialog` | `$CE1C` | 1 | miscellaneous. | `ram/wram.asm:1209` |
| `wFrameCounter2` | `$CE1D` | alias | Stores frame counter2. | `ram/wram.asm:1211` |
| `wPrinterQueueLength` | `$CE1D` | alias | Buffer/data field for printer queue length. | `ram/wram.asm:1212` |
| `wUnusedSGB1eColorOffset` | `$CE1D` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:1213` |
| `wRequested2bppSize` | `$CE1F` | 1 | Stores requested2bpp size. | `ram/wram.asm:1217` |
| `wRequested2bppSource` | `$CE20-$CE21` | 2 | Stores requested2bpp source. | `ram/wram.asm:1218` |
| `wRequested2bppDest` | `$CE22-$CE23` | 2 | Stores requested2bpp dest. | `ram/wram.asm:1219` |
| `wRequested1bppSize` | `$CE24` | 1 | Stores requested1bpp size. | `ram/wram.asm:1221` |
| `wRequested1bppSource` | `$CE25-$CE26` | 2 | Stores requested1bpp source. | `ram/wram.asm:1222` |
| `wRequested1bppDest` | `$CE27-$CE28` | 2 | Stores requested1bpp dest. | `ram/wram.asm:1223` |
| `wSecondsSince` | `$CE29` | 1 | Stores seconds since. | `ram/wram.asm:1225` |
| `wMinutesSince` | `$CE2A` | 1 | Stores minutes since. | `ram/wram.asm:1226` |
| `wHoursSince` | `$CE2B` | 1 | Stores hours since. | `ram/wram.asm:1227` |
| `wDaysSince` | `$CE2C` | 1 | Stores days since. | `ram/wram.asm:1228` |
| `wPlayerBGMapOffsetX` | `$CE39` | 1 | used in FollowNotExact; unit is pixels. | `ram/wram.asm:1232` |
| `wPlayerBGMapOffsetY` | `$CE3A` | 1 | used in FollowNotExact; unit is pixels. | `ram/wram.asm:1233` |
| `wPlayerStepVectorX` | `$CE3B` | 1 | Stores player step vector x. | `ram/wram.asm:1235` |
| `wPlayerStepVectorY` | `$CE3C` | 1 | Stores player step vector y. | `ram/wram.asm:1236` |
| `wPlayerStepFlags` | `$CE3D` | 1 | Stores player step flags. | `ram/wram.asm:1237` |
| `wPlayerStepDirection` | `$CE3E` | 1 | Stores player step direction. | `ram/wram.asm:1238` |
| `wPlayerNextMovement` | `$CE3F` | 1 | Stores player next movement. | `ram/wram.asm:1240` |
| `wPlayerMovement` | `$CE40` | 1 | Stores player movement. | `ram/wram.asm:1241` |
| `wMovementObject` | `$CE43` | 1 | Stores movement object. | `ram/wram.asm:1245` |
| `wMovementDataBank` | `$CE44` | 1 | Stores movement data bank. | `ram/wram.asm:1246` |
| `wMovementDataAddress` | `$CE45-$CE46` | 2 | Pointer/address for Movement Data Address. | `ram/wram.asm:1247` |
| `wIndexedMovement2Pointer` | `$CE47-$CE48` | 2 | Pointer/address for Indexed Movement2 Pointer. | `ram/wram.asm:1248` |
| `wContinueReadingMovement` | `$CE4B` | 1 | Stores continue reading movement. | `ram/wram.asm:1252` |
| `wObjectPriorities` | `$CE4C-$CE58` | 13 | Stores object priorities. | `ram/wram.asm:1255` |
| `wMovementPointer` | `$CE4C-$CE4D` | 2 | Pointer/address for Movement Pointer. | `ram/wram.asm:1258` |
| `wTempObjectCopyMapObjectIndex` | `$CE51` | 1 | Stores temp object copy map object index. | `ram/wram.asm:1260` |
| `wTempObjectCopySprite` | `$CE52` | 1 | Stores temp object copy sprite. | `ram/wram.asm:1261` |
| `wTempObjectCopySpriteVTile` | `$CE53` | 1 | Stores temp object copy sprite v tile. | `ram/wram.asm:1262` |
| `wTempObjectCopyPalette` | `$CE54` | 1 | Stores temp object copy palette. | `ram/wram.asm:1263` |
| `wTempObjectCopyMovement` | `$CE55` | 1 | Stores temp object copy movement. | `ram/wram.asm:1264` |
| `wTempObjectCopyRange` | `$CE56` | 1 | Stores temp object copy range. | `ram/wram.asm:1265` |
| `wTempObjectCopyX` | `$CE57` | 1 | Stores temp object copy x. | `ram/wram.asm:1266` |
| `wTempObjectCopyY` | `$CE58` | 1 | Stores temp object copy y. | `ram/wram.asm:1267` |
| `wTempObjectCopyRadius` | `$CE59` | 1 | Stores temp object copy radius. | `ram/wram.asm:1268` |
| `wTileDown` | `$CE5B` | 1 | Stores tile down. | `ram/wram.asm:1273` |
| `wTileUp` | `$CE5C` | 1 | Stores tile up. | `ram/wram.asm:1274` |
| `wTileLeft` | `$CE5D` | 1 | Stores tile left. | `ram/wram.asm:1275` |
| `wTileRight` | `$CE5E` | 1 | Stores tile right. | `ram/wram.asm:1276` |
| `wTilePermissions` | `$CE5F` | 1 | Stores tile permissions. | `ram/wram.asm:1278` |
| `wMenuMetadata` | `$CE60-$CE6F` | 16 | Alias for the start of Menu Metadata block. | `ram/wram.asm:1280` |
| `wWindowStackPointer` | `$CE60-$CE61` | 2 | Pointer/address for Window Stack Pointer. | `ram/wram.asm:1281` |
| `wMenuJoypad` | `$CE62` | 1 | Stores menu joypad. | `ram/wram.asm:1282` |
| `wMenuSelection` | `$CE63` | 1 | Stores menu selection. | `ram/wram.asm:1283` |
| `wMenuSelectionQuantity` | `$CE64` | 1 | Stores menu selection quantity. | `ram/wram.asm:1284` |
| `wWhichIndexSet` | `$CE65` | 1 | Stores which index set. | `ram/wram.asm:1285` |
| `wScrollingMenuCursorPosition` | `$CE66` | 1 | Stores scrolling menu cursor position. | `ram/wram.asm:1286` |
| `wWindowStackSize` | `$CE67` | 1 | Stores window stack size. | `ram/wram.asm:1287` |
| `wMenuMetadataEnd` | `$CE70` | alias | End marker for Menu Metadata. | `ram/wram.asm:1289` |
| `wMenuHeader` | `$CE70-$CE7F` | 16 | menu header. | `ram/wram.asm:1292` |
| `wMenuFlags` | `$CE70` | 1 | menu header. | `ram/wram.asm:1293` |
| `wMenuBorderTopCoord` | `$CE71` | 1 | Stores menu border top coord. | `ram/wram.asm:1294` |
| `wMenuBorderLeftCoord` | `$CE72` | 1 | Stores menu border left coord. | `ram/wram.asm:1295` |
| `wMenuBorderBottomCoord` | `$CE73` | 1 | Stores menu border bottom coord. | `ram/wram.asm:1296` |
| `wMenuBorderRightCoord` | `$CE74` | 1 | Stores menu border right coord. | `ram/wram.asm:1297` |
| `wMenuDataPointer` | `$CE75-$CE76` | 2 | Pointer/address for Menu Data Pointer. | `ram/wram.asm:1298` |
| `wMenuCursorPosition` | `$CE77` | 1 | Stores menu cursor position. | `ram/wram.asm:1299` |
| `wMenuHeaderEnd` | `$CE80` | alias | End marker for Menu Header. | `ram/wram.asm:1301` |
| `wMenuData` | `$CE80-$CE8F` | 16 | Alias for the start of Menu Data block. | `ram/wram.asm:1303` |
| `wMenuDataFlags` | `$CE80` | 1 | Stores menu data flags. | `ram/wram.asm:1304` |
| `wMenuDataItems` | `$CE81` | 1 | Vertical Menu/DoNthMenu/SetUpMenu. | `ram/wram.asm:1308` |
| `wMenuDataIndicesPointer` | `$CE82-$CE83` | 2 | Pointer/address for Menu Data Indices Pointer. | `ram/wram.asm:1309` |
| `wMenuDataDisplayFunctionPointer` | `$CE84-$CE85` | 2 | Pointer/address for Menu Data Display Function Pointer. | `ram/wram.asm:1310` |
| `wMenuDataPointerTableAddr` | `$CE86-$CE87` | 2 | Pointer/address for Menu Data Pointer Table Addr. | `ram/wram.asm:1311` |
| `wMenuData_2DMenuDimensions` | `$CE81` | 1 | 2D Menu. | `ram/wram.asm:1315` |
| `wMenuData_2DMenuSpacing` | `$CE82` | 1 | UNCLEAR: Menu Data 2 D Menu Spacing; the label is only a placeholder/field name. | `ram/wram.asm:1316` |
| `wMenuData_2DMenuItemStringsBank` | `$CE83` | 1 | UNCLEAR: Menu Data 2 D Menu Item Strings Bank; the label is only a placeholder/field name. | `ram/wram.asm:1317` |
| `wMenuData_2DMenuItemStringsAddr` | `$CE84-$CE85` | 2 | UNCLEAR: Menu Data 2 D Menu Item Strings Addr; the label is only a placeholder/field name. | `ram/wram.asm:1318` |
| `wMenuData_2DMenuFunctionBank` | `$CE86` | 1 | UNCLEAR: Menu Data 2 D Menu Function Bank; the label is only a placeholder/field name. | `ram/wram.asm:1319` |
| `wMenuData_2DMenuFunctionAddr` | `$CE87-$CE88` | 2 | UNCLEAR: Menu Data 2 D Menu Function Addr; the label is only a placeholder/field name. | `ram/wram.asm:1320` |
| `wMenuData_ScrollingMenuHeight` | `$CE81` | 1 | Scrolling Menu. | `ram/wram.asm:1324` |
| `wMenuData_ScrollingMenuWidth` | `$CE82` | 1 | Buffer/data field for menu data scrolling menu width. | `ram/wram.asm:1325` |
| `wMenuData_ScrollingMenuItemFormat` | `$CE83` | 1 | Buffer/data field for menu data scrolling menu item format. | `ram/wram.asm:1326` |
| `wMenuData_ItemsPointerBank` | `$CE84` | 1 | Pointer/address for Menu Data Items Pointer Bank. | `ram/wram.asm:1327` |
| `wMenuData_ItemsPointerAddr` | `$CE85-$CE86` | 2 | Pointer/address for Menu Data Items Pointer Addr. | `ram/wram.asm:1328` |
| `wMenuData_ScrollingMenuFunction1` | `$CE87-$CE89` | 3 | Buffer/data field for menu data scrolling menu function1. | `ram/wram.asm:1329` |
| `wMenuData_ScrollingMenuFunction2` | `$CE8A-$CE8C` | 3 | Buffer/data field for menu data scrolling menu function2. | `ram/wram.asm:1330` |
| `wMenuData_ScrollingMenuFunction3` | `$CE8D-$CE8F` | 3 | Buffer/data field for menu data scrolling menu function3. | `ram/wram.asm:1331` |
| `wMenuDataEnd` | `$CE90` | alias | End marker for Menu Data. | `ram/wram.asm:1333` |
| `wMoreMenuData` | `$CE90-$CE9F` | 16 | Alias for the start of More Menu Data block. | `ram/wram.asm:1335` |
| `w2DMenuData` | `$CE90-$CE97` | 8 | Alias for the start of 2 D Menu Data block. | `ram/wram.asm:1336` |
| `w2DMenuCursorInitY` | `$CE90` | 1 | UNCLEAR: 2 D Menu Cursor Init Y; the label is only a placeholder/field name. | `ram/wram.asm:1337` |
| `w2DMenuCursorInitX` | `$CE91` | 1 | UNCLEAR: 2 D Menu Cursor Init X; the label is only a placeholder/field name. | `ram/wram.asm:1338` |
| `w2DMenuNumRows` | `$CE92` | 1 | UNCLEAR: 2 D Menu Num Rows; the label is only a placeholder/field name. | `ram/wram.asm:1339` |
| `w2DMenuNumCols` | `$CE93` | 1 | UNCLEAR: 2 D Menu Num Cols; the label is only a placeholder/field name. | `ram/wram.asm:1340` |
| `w2DMenuFlags1` | `$CE94` | 1 | UNCLEAR: 2 D Menu Flags1; the label is only a placeholder/field name. | `ram/wram.asm:1341` |
| `w2DMenuFlags2` | `$CE95` | 1 | UNCLEAR: 2 D Menu Flags2; the label is only a placeholder/field name. | `ram/wram.asm:1351` |
| `w2DMenuCursorOffsets` | `$CE96` | 1 | UNCLEAR: 2 D Menu Cursor Offsets; the label is only a placeholder/field name. | `ram/wram.asm:1352` |
| `wMenuJoypadFilter` | `$CE97` | 1 | Stores menu joypad filter. | `ram/wram.asm:1353` |
| `w2DMenuDataEnd` | `$CE98` | alias | End marker for 2 D Menu Data. | `ram/wram.asm:1354` |
| `wMenuCursorY` | `$CE98` | 1 | Stores menu cursor y. | `ram/wram.asm:1355` |
| `wMenuCursorX` | `$CE99` | 1 | Stores menu cursor x. | `ram/wram.asm:1356` |
| `wCursorOffCharacter` | `$CE9A` | 1 | Stores cursor off character. | `ram/wram.asm:1357` |
| `wCursorCurrentTile` | `$CE9B-$CE9C` | 2 | Stores cursor current tile. | `ram/wram.asm:1358` |
| `wMoreMenuDataEnd` | `$CEA0` | alias | End marker for More Menu Data. | `ram/wram.asm:1360` |
| `wOverworldDelay` | `$CEA0` | 1 | Stores overworld delay. | `ram/wram.asm:1362` |
| `wTextDelayFrames` | `$CEA1` | 1 | Stores text delay frames. | `ram/wram.asm:1363` |
| `wVBlankOccurred` | `$CEA2` | 1 | Stores v blank occurred. | `ram/wram.asm:1364` |
| `wBetaTitleSequenceOpeningType` | `$CEA3` | 1 | Stores beta title sequence opening type. | `ram/wram.asm:1366` |
| `wDefaultSpawnpoint` | `$CEA4` | 1 | Stores default spawnpoint. | `ram/wram.asm:1370` |
| `wBufferMonNickname` | `$CEA5-$CEAF` | 11 | mon buffer. | `ram/wram.asm:1374` |
| `wBufferMonOT` | `$CEB0-$CEBA` | 11 | Buffer/data field for buffer mon ot. | `ram/wram.asm:1375` |
| `wBufferMon` | `$CEBB-$CEEA` | 48 | Buffer/data field for buffer mon. | `ram/wram.asm:1376` |
| `wMagnetTrainDirection` | `$CEA5` | 1 | magnet train. | `ram/wram.asm:1380` |
| `wMagnetTrainInitPosition` | `$CEA6` | 1 | Stores magnet train init position. | `ram/wram.asm:1381` |
| `wMagnetTrainHoldPosition` | `$CEA7` | 1 | Stores magnet train hold position. | `ram/wram.asm:1382` |
| `wMagnetTrainFinalPosition` | `$CEA8` | 1 | Stores magnet train final position. | `ram/wram.asm:1383` |
| `wMagnetTrainPlayerSpriteInitX` | `$CEA9` | 1 | Stores magnet train player sprite init x. | `ram/wram.asm:1384` |
| `wCreditsPos` | `$CEA5-$CEA6` | 2 | credits. | `ram/wram.asm:1388` |
| `wCreditsTimer` | `$CEA7` | 1 | Stores credits timer. | `ram/wram.asm:1389` |
| `wTempMail` | `$CEA5-$CED3` | 47 | mail temp storage. | `ram/wram.asm:1393` |
| `wBugContestResults` | `$CEA5-$CEA8` | 4 | bug-catching contest. | `ram/wram.asm:1397` |
| `wBugContestWinnersEnd` | `$CEB1-$CEB4` | 4 | End marker for Bug Contest Winners. | `ram/wram.asm:1401` |
| `wBugContestWinnerName` | `$CEB9-$CEC3` | 11 | Buffer/data field for bug contest winner name. | `ram/wram.asm:1404` |
| `wMartItem1BCD` | `$CEA5-$CEA7` | 3 | mart items. | `ram/wram.asm:1408` |
| `wMartItem2BCD` | `$CEA8-$CEAA` | 3 | Stores mart item2 bcd. | `ram/wram.asm:1409` |
| `wMartItem3BCD` | `$CEAB-$CEAD` | 3 | Stores mart item3 bcd. | `ram/wram.asm:1410` |
| `wMartItem4BCD` | `$CEAE-$CEB0` | 3 | Stores mart item4 bcd. | `ram/wram.asm:1411` |
| `wMartItem5BCD` | `$CEB1-$CEB3` | 3 | Stores mart item5 bcd. | `ram/wram.asm:1412` |
| `wMartItem6BCD` | `$CEB4-$CEB6` | 3 | Stores mart item6 bcd. | `ram/wram.asm:1413` |
| `wMartItem7BCD` | `$CEB7-$CEB9` | 3 | Stores mart item7 bcd. | `ram/wram.asm:1414` |
| `wMartItem8BCD` | `$CEBA-$CEBC` | 3 | Stores mart item8 bcd. | `ram/wram.asm:1415` |
| `wMartItem9BCD` | `$CEBD-$CEBF` | 3 | Stores mart item9 bcd. | `ram/wram.asm:1416` |
| `wMartItem10BCD` | `$CEC0-$CEC2` | 3 | Stores mart item10 bcd. | `ram/wram.asm:1417` |
| `wTownMapPlayerIconLandmark` | `$CEA5` | 1 | town map data. | `ram/wram.asm:1421` |
| `wTownMapCursorLandmark` | `$CEA5` | 1 | Buffer/data field for town map cursor landmark. | `ram/wram.asm:1423` |
| `wTownMapCursorObjectPointer` | `$CEA6-$CEA7` | 2 | Pointer/address for Town Map Cursor Object Pointer. | `ram/wram.asm:1424` |
| `wTownMapCursorCoordinates` | `$CEA5-$CEA6` | 2 | Buffer/data field for town map cursor coordinates. | `ram/wram.asm:1426` |
| `wStartFlypoint` | `$CEA7` | 1 | Stores start flypoint. | `ram/wram.asm:1427` |
| `wEndFlypoint` | `$CEA8` | 1 | Stores end flypoint. | `ram/wram.asm:1428` |
| `wPhoneScriptBank` | `$CEA5` | 1 | phone call data. | `ram/wram.asm:1433` |
| `wPhoneCaller` | `$CEA6-$CEA7` | 2 | Stores phone caller. | `ram/wram.asm:1434` |
| `wCurRadioLine` | `$CEA5` | 1 | radio data. | `ram/wram.asm:1438` |
| `wNextRadioLine` | `$CEA6` | 1 | Stores next radio line. | `ram/wram.asm:1439` |
| `wRadioTextDelay` | `$CEA7` | 1 | Stores radio text delay. | `ram/wram.asm:1440` |
| `wNumRadioLinesPrinted` | `$CEA8` | 1 | Stores num radio lines printed. | `ram/wram.asm:1441` |
| `wOaksPKMNTalkSegmentCounter` | `$CEA9` | 1 | Stores oaks pkmn talk segment counter. | `ram/wram.asm:1442` |
| `wRadioText` | `$CEAF-$CED6` | 40 | Buffer/data field for radio text. | `ram/wram.asm:1444` |
| `wLuckyNumberDigitsBuffer` | `$CEA5-$CEA9` | 5 | lucky number show. | `ram/wram.asm:1448` |
| `wMovementBufferCount` | `$CEA5` | 1 | movement buffer data. | `ram/wram.asm:1452` |
| `wMovementBufferObject` | `$CEA6` | 1 | Buffer/data field for movement buffer object. | `ram/wram.asm:1453` |
| `wUnusedMovementBufferBank` | `$CEA7` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:1454` |
| `wUnusedMovementBufferPointer` | `$CEA8-$CEA9` | 2 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:1455` |
| `wMovementBuffer` | `$CEAA-$CEE0` | 55 | Buffer/data field for movement buffer. | `ram/wram.asm:1456` |
| `wWhichBoxMonToPrint` | `$CEA5` | 1 | box printing. | `ram/wram.asm:1460` |
| `wFinishedPrintingBox` | `$CEA6` | 1 | Stores finished printing box. | `ram/wram.asm:1461` |
| `wAddrOfBoxToPrint` | `$CEA7-$CEA8` | 2 | Pointer/address for Addr Of Box To Print. | `ram/wram.asm:1462` |
| `wBankOfBoxToPrint` | `$CEA9` | 1 | Stores bank of box to print. | `ram/wram.asm:1463` |
| `wWhichBoxToPrint` | `$CEAA` | 1 | Stores which box to print. | `ram/wram.asm:1464` |
| `wPrintedUnownTileSource` | `$CEA5-$CEB4` | 16 | Unown printing. | `ram/wram.asm:1468` |
| `wPrintedUnownTileDest` | `$CEB5-$CEC4` | 16 | Stores printed unown tile dest. | `ram/wram.asm:1469` |
| `wPlaceBallsDirection` | `$CEA6` | 1 | Stores place balls direction. | `ram/wram.asm:1474` |
| `wTrainerHUDTiles` | `$CEA7-$CEAA` | 4 | Stores trainer hud tiles. | `ram/wram.asm:1475` |
| `wExperienceGained` | `$CEA5-$CEA7` | 3 | battle exp gain. | `ram/wram.asm:1479` |
| `wEarthquakeMovementDataBuffer` | `$CEA5-$CEA9` | 5 | earthquake data buffer. | `ram/wram.asm:1483` |
| `wSwitchItemBuffer` | `$CEA5-$CEA6` | 2 | switching items in pack. | `ram/wram.asm:1487` |
| `wSwitchMonBuffer` | `$CEA5` | alias | switching pokemon in party | may store a name, partymon, or mail. | `ram/wram.asm:1492` |
| `wMonMailMessageBuffer` | `$CEA5-$CEC5` | 33 | giving pokemon mail. | `ram/wram.asm:1503` |
| `wBoxNameBuffer` | `$CEA5-$CEAD` | 9 | bill's pc. | `ram/wram.asm:1508` |
| `wBillsPCTempListIndex` | `$CEA6` | 1 | Stores bills pc temp list index. | `ram/wram.asm:1511` |
| `wBillsPCTempBoxCount` | `$CEA7` | 1 | Stores bills pc temp box count. | `ram/wram.asm:1512` |
| `wTempPokedexSeenCount` | `$CEA5` | 1 | prof. oak's pc. | `ram/wram.asm:1517` |
| `wTempPokedexCaughtCount` | `$CEA6` | 1 | Stores temp pokedex caught count. | `ram/wram.asm:1518` |
| `wDecoNameBuffer` | `$CEA5-$CEB1` | 13 | player's room pc. | `ram/wram.asm:1523` |
| `wNumOwnedDecoCategories` | `$CEA5` | 1 | Stores num owned deco categories. | `ram/wram.asm:1525` |
| `wOwnedDecoCategories` | `$CEA6-$CEB5` | 16 | Stores owned deco categories. | `ram/wram.asm:1526` |
| `wCurTradePartyMon` | `$CEA5` | 1 | trade. | `ram/wram.asm:1531` |
| `wCurOTTradePartyMon` | `$CEA6` | 1 | Stores cur ot trade party mon. | `ram/wram.asm:1532` |
| `wBufferTrademonNickname` | `$CEA7-$CEB1` | 11 | Buffer/data field for buffer trademon nickname. | `ram/wram.asm:1533` |
| `wLinkBattleRecordBuffer` | `$CEA5` | alias | link battle record data. | `ram/wram.asm:1537` |
| `wLinkBattleRecordName` | `$CEA5-$CEAF` | 11 | link battle record data. | `ram/wram.asm:1538` |
| `wLinkBattleRecordWins` | `$CEB0-$CEB1` | 2 | Stores link battle record wins. | `ram/wram.asm:1539` |
| `wLinkBattleRecordLosses` | `$CEB2-$CEB3` | 2 | Stores link battle record losses. | `ram/wram.asm:1540` |
| `wLinkBattleRecordDraws` | `$CEB4-$CEB5` | 2 | Stores link battle record draws. | `ram/wram.asm:1541` |
| `wTempDayOfWeek` | `$CEA5` | alias | miscellaneous. | `ram/wram.asm:1545` |
| `wPrevPartyLevel` | `$CEA5` | alias | miscellaneous. | `ram/wram.asm:1546` |
| `wCurBeatUpPartyMon` | `$CEA5` | alias | miscellaneous. | `ram/wram.asm:1547` |
| `wUnownPuzzleCornerTile` | `$CEA5` | alias | miscellaneous. | `ram/wram.asm:1548` |
| `wKeepSevenBiasChance` | `$CEA5` | alias | miscellaneous. | `ram/wram.asm:1549` |
| `wPokeFluteCuredSleep` | `$CEA5` | alias | miscellaneous. | `ram/wram.asm:1550` |
| `wTempRestorePPItem` | `$CEA5` | 1 | miscellaneous. | `ram/wram.asm:1551` |
| `wDebugColorIsTrainer` | `$CEA5` | 1 | debug color picker. | `ram/wram.asm:1556` |
| `wDebugColorIsShiny` | `$CEA6` | 1 | Stores debug color is shiny. | `ram/wram.asm:1557` |
| `wDebugColorCurTMHM` | `$CEA7` | 1 | Stores debug color cur tmhm. | `ram/wram.asm:1558` |
| `wDebugRoomCurPage` | `$CEA5` | 1 | debug room paged values. | `ram/wram.asm:1563` |
| `wDebugRoomCurValue` | `$CEA6` | 1 | Stores debug room cur value. | `ram/wram.asm:1564` |
| `wDebugRoomAFunction` | `$CEA7-$CEA8` | 2 | UNCLEAR: Debug Room A Function; the label is only a placeholder/field name. | `ram/wram.asm:1565` |
| `wDebugRoomStartFunction` | `$CEA9-$CEAA` | 2 | Stores debug room start function. | `ram/wram.asm:1566` |
| `wDebugRoomSelectFunction` | `$CEAB-$CEAC` | 2 | Stores debug room select function. | `ram/wram.asm:1567` |
| `wDebugRoomAutoFunction` | `$CEAD-$CEAE` | 2 | Stores debug room auto function. | `ram/wram.asm:1568` |
| `wDebugRoomPageCount` | `$CEAF` | 1 | Stores debug room page count. | `ram/wram.asm:1569` |
| `wDebugRoomPagesPointer` | `$CEB0-$CEB1` | 2 | Pointer/address for Debug Room Pages Pointer. | `ram/wram.asm:1570` |
| `wSeenTrainerBank` | `$CEA5` | 1 | trainer data. | `ram/wram.asm:1580` |
| `wSeenTrainerDistance` | `$CEA6` | 1 | Stores seen trainer distance. | `ram/wram.asm:1581` |
| `wSeenTrainerDirection` | `$CEA7` | 1 | Stores seen trainer direction. | `ram/wram.asm:1582` |
| `wTempTrainer` | `$CEA8-$CEB4` | 13 | Alias for the start of Temp Trainer block. | `ram/wram.asm:1583` |
| `wTempTrainerEventFlag` | `$CEA8-$CEA9` | 2 | Stores temp trainer event flag. | `ram/wram.asm:1584` |
| `wTempTrainerClass` | `$CEAA` | 1 | Stores temp trainer class. | `ram/wram.asm:1585` |
| `wTempTrainerID` | `$CEAB` | 1 | Stores temp trainer id. | `ram/wram.asm:1586` |
| `wSeenTextPointer` | `$CEAC-$CEAD` | 2 | Pointer/address for Seen Text Pointer. | `ram/wram.asm:1587` |
| `wWinTextPointer` | `$CEAE-$CEAF` | 2 | Pointer/address for Win Text Pointer. | `ram/wram.asm:1588` |
| `wLossTextPointer` | `$CEB0-$CEB1` | 2 | Pointer/address for Loss Text Pointer. | `ram/wram.asm:1589` |
| `wScriptAfterPointer` | `$CEB2-$CEB3` | 2 | Pointer/address for Script After Pointer. | `ram/wram.asm:1590` |
| `wRunningTrainerBattleScript` | `$CEB4` | 1 | Stores running trainer battle script. | `ram/wram.asm:1591` |
| `wTempTrainerEnd` | `$CEB5` | alias | End marker for Temp Trainer. | `ram/wram.asm:1592` |
| `wMenuItemsList` | `$CEA5-$CEB4` | 16 | menu items list. | `ram/wram.asm:1596` |
| `wMenuItemsListEnd` | `$CEB5` | alias | End marker for Menu Items List. | `ram/wram.asm:1597` |
| `wCurFruitTree` | `$CEA5` | 1 | fruit tree data. | `ram/wram.asm:1601` |
| `wCurFruit` | `$CEA6` | 1 | Stores cur fruit. | `ram/wram.asm:1602` |
| `wItemBallData` | `$CEA5-$CEA6` | 2 | item ball data. | `ram/wram.asm:1606` |
| `wItemBallItemID` | `$CEA5` | 1 | item ball data. | `ram/wram.asm:1607` |
| `wItemBallQuantity` | `$CEA6` | 1 | Stores item ball quantity. | `ram/wram.asm:1608` |
| `wItemBallDataEnd` | `$CEA7` | alias | End marker for Item Ball Data. | `ram/wram.asm:1609` |
| `wHiddenItemData` | `$CEA5-$CEA7` | 3 | hidden item data. | `ram/wram.asm:1613` |
| `wHiddenItemEvent` | `$CEA5-$CEA6` | 2 | hidden item data. | `ram/wram.asm:1614` |
| `wHiddenItemID` | `$CEA7` | 1 | Stores hidden item id. | `ram/wram.asm:1615` |
| `wHiddenItemDataEnd` | `$CEA8` | alias | End marker for Hidden Item Data. | `ram/wram.asm:1616` |
| `wElevatorData` | `$CEA5-$CEA8` | 4 | elevator data. | `ram/wram.asm:1620` |
| `wElevatorPointerBank` | `$CEA5` | 1 | elevator data. | `ram/wram.asm:1621` |
| `wElevatorPointer` | `$CEA6-$CEA7` | 2 | Pointer/address for Elevator Pointer. | `ram/wram.asm:1622` |
| `wElevatorOriginFloor` | `$CEA8` | 1 | Stores elevator origin floor. | `ram/wram.asm:1623` |
| `wElevatorDataEnd` | `$CEA9` | alias | End marker for Elevator Data. | `ram/wram.asm:1624` |
| `wCurCoordEvent` | `$CEA5` | alias | coord event data. | `ram/wram.asm:1628` |
| `wCurCoordEventSceneID` | `$CEA5` | 1 | Persistent scene-script state byte for Cur Coord Event. | `ram/wram.asm:1629` |
| `wCurCoordEventMapY` | `$CEA6` | 1 | Buffer/data field for cur coord event map y. | `ram/wram.asm:1630` |
| `wCurCoordEventMapX` | `$CEA7` | 1 | Buffer/data field for cur coord event map x. | `ram/wram.asm:1631` |
| `wCurCoordEventScriptAddr` | `$CEA9-$CEAA` | 2 | Pointer/address for Cur Coord Event Script Addr. | `ram/wram.asm:1633` |
| `wCurBGEvent` | `$CEA5` | alias | BG event data. | `ram/wram.asm:1637` |
| `wCurBGEventYCoord` | `$CEA5` | 1 | BG event data. | `ram/wram.asm:1638` |
| `wCurBGEventXCoord` | `$CEA6` | 1 | Stores cur bg event x coord. | `ram/wram.asm:1639` |
| `wCurBGEventType` | `$CEA7` | 1 | Stores cur bg event type. | `ram/wram.asm:1640` |
| `wCurBGEventScriptAddr` | `$CEA8-$CEA9` | 2 | Pointer/address for Cur BG Event Script Addr. | `ram/wram.asm:1641` |
| `wMartType` | `$CEA5` | 1 | mart data. | `ram/wram.asm:1645` |
| `wMartPointerBank` | `$CEA6` | 1 | Pointer/address for Mart Pointer Bank. | `ram/wram.asm:1646` |
| `wMartPointer` | `$CEA7-$CEA8` | 2 | Pointer/address for Mart Pointer. | `ram/wram.asm:1647` |
| `wMartJumptableIndex` | `$CEA9` | 1 | Stores mart jumptable index. | `ram/wram.asm:1648` |
| `wBargainShopFlags` | `$CEAA-$CEAB` | 2 | Stores bargain shop flags. | `ram/wram.asm:1649` |
| `wCurInput` | `$CEA5` | alias | player movement data. | `ram/wram.asm:1653` |
| `wFacingTileID` | `$CEA5` | 1 | player movement data. | `ram/wram.asm:1654` |
| `wWalkingIntoNPC` | `$CEA6` | 1 | Stores walking into npc. | `ram/wram.asm:1655` |
| `wWalkingIntoLand` | `$CEA7` | 1 | Stores walking into land. | `ram/wram.asm:1656` |
| `wWalkingIntoEdgeWarp` | `$CEA8` | 1 | Stores walking into edge warp. | `ram/wram.asm:1657` |
| `wMovementAnimation` | `$CEA9` | 1 | Stores movement animation. | `ram/wram.asm:1658` |
| `wWalkingDirection` | `$CEAA` | 1 | Stores walking direction. | `ram/wram.asm:1659` |
| `wFacingDirection` | `$CEAB` | 1 | Stores facing direction. | `ram/wram.asm:1660` |
| `wWalkingX` | `$CEAC` | 1 | Stores walking x. | `ram/wram.asm:1661` |
| `wWalkingY` | `$CEAD` | 1 | Stores walking y. | `ram/wram.asm:1662` |
| `wWalkingTileCollision` | `$CEAE` | 1 | Stores walking tile collision. | `ram/wram.asm:1663` |
| `wPlayerTurningDirection` | `$CEB5` | 1 | Stores player turning direction. | `ram/wram.asm:1665` |
| `wJumpStdScriptBuffer` | `$CEA6-$CEA8` | 3 | Buffer/data field for jump std script buffer. | `ram/wram.asm:1670` |
| `wCheckedTime` | `$CEA5` | 1 | phone script data. | `ram/wram.asm:1674` |
| `wPhoneListIndex` | `$CEA6` | 1 | Stores phone list index. | `ram/wram.asm:1675` |
| `wNumAvailableCallers` | `$CEA7` | 1 | Stores num available callers. | `ram/wram.asm:1676` |
| `wAvailableCallers` | `$CEA8-$CEB1` | 10 | Stores available callers. | `ram/wram.asm:1677` |
| `wCallerContact` | `$CEA6-$CEB1` | 12 | Stores caller contact. | `ram/wram.asm:1682` |
| `wMenuCursorPositionBackup` | `$CEAC` | 1 | Stores menu cursor position backup. | `ram/wram.asm:1687` |
| `wMenuScrollPositionBackup` | `$CEAD` | 1 | Stores menu scroll position backup. | `ram/wram.asm:1688` |
| `wPoisonStepData` | `$CEA5-$CEAB` | 7 | poison step data. | `ram/wram.asm:1692` |
| `wPoisonStepFlagSum` | `$CEA5` | 1 | poison step data. | `ram/wram.asm:1693` |
| `wPoisonStepPartyFlags` | `$CEA6-$CEAB` | 6 | Stores poison step party flags. | `ram/wram.asm:1694` |
| `wPoisonStepDataEnd` | `$CEAC` | alias | End marker for Poison Step Data. | `ram/wram.asm:1695` |
| `wBoxAlignment` | `$CEF3` | 1 | Stores box alignment. | `ram/wram.asm:1700` |
| `wFarDecompressPicPointer` | `$CEF4-$CEF5` | 2 | Pointer/address for Far Decompress Pic Pointer. | `ram/wram.asm:1701` |
| `wFXAnimID` | `$CEF6-$CEF7` | 2 | Stores fx anim id. | `ram/wram.asm:1702` |
| `wPlaceBallsX` | `$CEF8` | 1 | Stores place balls x. | `ram/wram.asm:1705` |
| `wPlaceBallsY` | `$CEF9` | 1 | Stores place balls y. | `ram/wram.asm:1706` |
| `wTileAnimationTimer` | `$CEFA` | 1 | Stores tile animation timer. | `ram/wram.asm:1707` |
| `wBGP` | `$CEFB` | 1 | palette backups?. | `ram/wram.asm:1710` |
| `wOBP0` | `$CEFC` | 1 | Stores obp0. | `ram/wram.asm:1711` |
| `wOBP1` | `$CEFD` | 1 | Stores obp1. | `ram/wram.asm:1712` |
| `wBattleAfterAnim` | `$CEFE` | 1 | Stores battle after anim. | `ram/wram.asm:1714` |
| `wMonOrItemNameBuffer` | `$CF00-$CF0A` | 11 | Two NAME_LENGTH-sized buffers for formatted item/Pokémon names. | `ram/wram.asm:1718` |
| `wTMHMMoveNameBackup` | `$CF16-$CF22` | 13 | Buffer/data field for tmhm move name backup. | `ram/wram.asm:1720` |
| `wStringBuffer1` | `$CF23-$CF35` | 19 | General-purpose text/string buffer 1. | `ram/wram.asm:1722` |
| `wStringBuffer2` | `$CF36-$CF48` | 19 | General-purpose text/string buffer 2; also used for RTC/timeset staging. | `ram/wram.asm:1723` |
| `wStringBuffer3` | `$CF49-$CF5B` | 19 | General-purpose text/string buffer 3. | `ram/wram.asm:1724` |
| `wStringBuffer4` | `$CF5C-$CF6E` | 19 | General-purpose text/string buffer 4. | `ram/wram.asm:1725` |
| `wStringBuffer5` | `$CF6F-$CF7B` | 13 | Move-name-sized text buffer. | `ram/wram.asm:1726` |
| `wBattleMenuCursorPosition` | `$CF7C` | 1 | Stores battle menu cursor position. | `ram/wram.asm:1728` |
| `wCurBattleMon` | `$CF7E` | 1 | Index of the player's currently active party mon. | `ram/wram.asm:1732` |
| `wCurMoveNum` | `$CF7F` | 1 | Current move slot index being used/selected. | `ram/wram.asm:1736` |
| `wLastPocket` | `$CF80` | 1 | Stores last pocket. | `ram/wram.asm:1738` |
| `wPartyMenuCursor` | `$CF81` | 1 | Stores party menu cursor. | `ram/wram.asm:1740` |
| `wItemsPocketCursor` | `$CF82` | 1 | Stores items pocket cursor. | `ram/wram.asm:1741` |
| `wKeyItemsPocketCursor` | `$CF83` | 1 | Stores key items pocket cursor. | `ram/wram.asm:1742` |
| `wBallsPocketCursor` | `$CF84` | 1 | Stores balls pocket cursor. | `ram/wram.asm:1743` |
| `wTMHMPocketCursor` | `$CF85` | 1 | Stores tmhm pocket cursor. | `ram/wram.asm:1744` |
| `wItemsPocketScrollPosition` | `$CF87` | 1 | Stores items pocket scroll position. | `ram/wram.asm:1748` |
| `wKeyItemsPocketScrollPosition` | `$CF88` | 1 | Stores key items pocket scroll position. | `ram/wram.asm:1749` |
| `wBallsPocketScrollPosition` | `$CF89` | 1 | Stores balls pocket scroll position. | `ram/wram.asm:1750` |
| `wTMHMPocketScrollPosition` | `$CF8A` | 1 | Stores tmhm pocket scroll position. | `ram/wram.asm:1751` |
| `wSwitchMon` | `$CF8B` | alias | Stores switch mon. | `ram/wram.asm:1753` |
| `wSwitchItem` | `$CF8B` | alias | Stores switch item. | `ram/wram.asm:1754` |
| `wSwappingMove` | `$CF8B` | 1 | Stores swapping move. | `ram/wram.asm:1755` |
| `wMenuScrollPosition` | `$CF8C-$CF8F` | 4 | Stores menu scroll position. | `ram/wram.asm:1758` |
| `wQueuedScriptBank` | `$CF90` | 1 | Stores queued script bank. | `ram/wram.asm:1760` |
| `wQueuedScriptAddr` | `$CF91-$CF92` | 2 | Pointer/address for Queued Script Addr. | `ram/wram.asm:1761` |
| `wPredefID` | `$CF93` | 1 | Stores predef id. | `ram/wram.asm:1763` |
| `wPredefHL` | `$CF94-$CF95` | 2 | Stores predef hl. | `ram/wram.asm:1764` |
| `wPredefAddress` | `$CF96-$CF97` | 2 | Pointer/address for Predef Address. | `ram/wram.asm:1765` |
| `wFarCallBC` | `$CF98-$CF99` | 2 | UNCLEAR: Far Call BC; the label is only a placeholder/field name. | `ram/wram.asm:1766` |
| `wNumMoves` | `$CF9B` | 1 | Stores num moves. | `ram/wram.asm:1769` |
| `wFieldMoveSucceeded` | `$CF9C` | alias | UNCLEAR: Field Move Succeeded; the label is only a placeholder/field name. | `ram/wram.asm:1771` |
| `wItemEffectSucceeded` | `$CF9C` | alias | Stores item effect succeeded. | `ram/wram.asm:1772` |
| `wBattlePlayerAction` | `$CF9C` | alias | Stores battle player action. | `ram/wram.asm:1773` |
| `wSolvedUnownPuzzle` | `$CF9C` | 1 | 0 - use move | 1 - use item | 2 - switch. | `ram/wram.asm:1777` |
| `wStateFlags` | `$CF9D` | 1 | Stores state flags. | `ram/wram.asm:1780` |
| `wBattleResult` | `$CFA1` | 1 | Stores battle result. | `ram/wram.asm:1789` |
| `wUsingItemWithSelect` | `$CFA3` | 1 | Stores using item with select. | `ram/wram.asm:1796` |
| `wCurMartCount` | `$CFA4` | 1 | mart data. | `ram/wram.asm:1800` |
| `wCurMartItems` | `$CFA5-$CFB3` | 15 | Stores cur mart items. | `ram/wram.asm:1801` |
| `wCurElevatorCount` | `$CFA4` | 1 | elevator data. | `ram/wram.asm:1805` |
| `wCurElevatorFloors` | `$CFA5-$CFB3` | 15 | Stores cur elevator floors. | `ram/wram.asm:1806` |
| `wCurMessageScrollPosition` | `$CFA4` | 1 | mailbox data. | `ram/wram.asm:1810` |
| `wCurMessageIndex` | `$CFA5` | 1 | Stores cur message index. | `ram/wram.asm:1811` |
| `wMailboxCount` | `$CFA6` | 1 | Stores mailbox count. | `ram/wram.asm:1812` |
| `wMailboxItems` | `$CFA7-$CFB0` | 10 | Stores mailbox items. | `ram/wram.asm:1813` |
| `wListPointer` | `$CFB4-$CFB5` | 2 | Pointer/address for List Pointer. | `ram/wram.asm:1816` |
| `wUnusedNamesPointer` | `$CFB6-$CFB7` | 2 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:1817` |

## Detailed WRAM1 (`$D000-$DFFF`, bank 1) tables

### WRAM 1

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wItemAttributesPointer` | `$D000-$D001` | 2 | Pointer/address for Item Attributes Pointer. | `ram/wram.asm:1822` |
| `wCurItem` | `$D002` | 1 | Stores cur item. | `ram/wram.asm:1824` |
| `wCurItemQuantity` | `$D003` | alias | Stores cur item quantity. | `ram/wram.asm:1825` |
| `wMartItemID` | `$D003` | 1 | Stores mart item id. | `ram/wram.asm:1826` |
| `wCurPartySpecies` | `$D004` | 1 | Stores cur party species. | `ram/wram.asm:1829` |
| `wCurPartyMon` | `$D005` | 1 | Stores cur party mon. | `ram/wram.asm:1831` |
| `wWhichHPBar` | `$D007` | 1 | Stores which hp bar. | `ram/wram.asm:1837` |
| `wPokemonWithdrawDepositParameter` | `$D008` | 1 | Stores pokemon withdraw deposit parameter. | `ram/wram.asm:1843` |
| `wItemQuantityChange` | `$D009` | 1 | Stores item quantity change. | `ram/wram.asm:1850` |
| `wItemQuantity` | `$D00A` | 1 | Stores item quantity. | `ram/wram.asm:1851` |
| `wTempMon` | `$D00B-$D03A` | 48 | Stores temp mon. | `ram/wram.asm:1853` |
| `wSpriteFlags` | `$D03B` | 1 | Stores sprite flags. | `ram/wram.asm:1855` |
| `wHandlePlayerStep` | `$D03C` | 1 | Stores handle player step. | `ram/wram.asm:1857` |
| `wPartyMenuActionText` | `$D03E` | 1 | Buffer/data field for party menu action text. | `ram/wram.asm:1861` |
| `wItemAttributeValue` | `$D03F` | 1 | Stores item attribute value. | `ram/wram.asm:1863` |
| `wCurPartyLevel` | `$D040` | 1 | Stores cur party level. | `ram/wram.asm:1865` |
| `wScrollingMenuListSize` | `$D041` | 1 | Buffer/data field for scrolling menu list size. | `ram/wram.asm:1867` |
| `wLinkMode` | `$D042` | 1 | Stores link mode. | `ram/wram.asm:1869` |
| `wNextWarp` | `$D043` | 1 | 0 not in link battle | 1 link battle | used when following a map warp. | `ram/wram.asm:1874` |
| `wNextMapGroup` | `$D044` | 1 | Buffer/data field for next map group. | `ram/wram.asm:1875` |
| `wNextMapNumber` | `$D045` | 1 | Buffer/data field for next map number. | `ram/wram.asm:1876` |
| `wPrevWarp` | `$D046` | 1 | Stores prev warp. | `ram/wram.asm:1877` |
| `wPrevMapGroup` | `$D047` | 1 | Buffer/data field for prev map group. | `ram/wram.asm:1878` |
| `wPrevMapNumber` | `$D048` | 1 | Buffer/data field for prev map number. | `ram/wram.asm:1879` |
| `wUnusedAddOutdoorSpritesReturnValue` | `$D05A` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:1883` |
| `wBGMapAnchor` | `$D05B-$D05C` | 2 | Buffer/data field for bg map anchor. | `ram/wram.asm:1885` |
| `wUsedSprites` | `$D05D-$D074` | 24 | Alias for the start of Used Sprites block. | `ram/wram.asm:1887` |
| `wUsedSpritesEnd` | `$D075-$D07C` | 8 | End marker for Used Sprites. | `ram/wram.asm:1888` |
| `wOverworldMapAnchor` | `$D07D-$D07E` | 2 | Buffer/data field for overworld map anchor. | `ram/wram.asm:1891` |
| `wPlayerMetatileY` | `$D07F` | 1 | Stores player metatile y. | `ram/wram.asm:1892` |
| `wPlayerMetatileX` | `$D080` | 1 | Stores player metatile x. | `ram/wram.asm:1893` |
| `wMapPartial` | `$D081-$D085` | 5 | Alias for the start of Map Partial block. | `ram/wram.asm:1895` |
| `wMapAttributesBank` | `$D081` | 1 | Stores map attributes bank. | `ram/wram.asm:1896` |
| `wMapTileset` | `$D082` | 1 | Buffer/data field for map tileset. | `ram/wram.asm:1897` |
| `wEnvironment` | `$D083` | 1 | Stores environment. | `ram/wram.asm:1898` |
| `wMapAttributesPointer` | `$D084-$D085` | 2 | Pointer/address for Map Attributes Pointer. | `ram/wram.asm:1899` |
| `wMapPartialEnd` | `$D086` | alias | End marker for Map Partial. | `ram/wram.asm:1900` |
| `wMapAttributes` | `$D086-$D091` | 12 | Alias for the start of Map Attributes block. | `ram/wram.asm:1902` |
| `wMapBorderBlock` | `$D086` | 1 | Buffer/data field for map border block. | `ram/wram.asm:1903` |
| `wMapHeight` | `$D087` | 1 | width/height are in blocks (2x2 walkable tiles, 4x4 graphics tiles). | `ram/wram.asm:1905` |
| `wMapWidth` | `$D088` | 1 | Buffer/data field for map width. | `ram/wram.asm:1906` |
| `wMapBlocksBank` | `$D089` | 1 | Stores map blocks bank. | `ram/wram.asm:1907` |
| `wMapBlocksPointer` | `$D08A-$D08B` | 2 | Pointer/address for Map Blocks Pointer. | `ram/wram.asm:1908` |
| `wMapScriptsBank` | `$D08C` | 1 | Stores map scripts bank. | `ram/wram.asm:1909` |
| `wMapScriptsPointer` | `$D08D-$D08E` | 2 | Pointer/address for Map Scripts Pointer. | `ram/wram.asm:1910` |
| `wMapEventsPointer` | `$D08F-$D090` | 2 | Pointer/address for Map Events Pointer. | `ram/wram.asm:1911` |
| `wMapConnections` | `$D091` | 1 | bit set. | `ram/wram.asm:1913` |
| `wMapAttributesEnd` | `$D092` | alias | End marker for Map Attributes. | `ram/wram.asm:1914` |
| `wNorthMapConnection` | `$D092-$D09D` | 12 | Buffer/data field for north map connection. | `ram/wram.asm:1916` |
| `wSouthMapConnection` | `$D09E-$D0A9` | 12 | Buffer/data field for south map connection. | `ram/wram.asm:1917` |
| `wWestMapConnection` | `$D0AA-$D0B5` | 12 | Buffer/data field for west map connection. | `ram/wram.asm:1918` |
| `wEastMapConnection` | `$D0B6-$D0C1` | 12 | Buffer/data field for east map connection. | `ram/wram.asm:1919` |
| `wTileset` | `$D0C2-$D0D0` | 15 | Alias for the start of Tileset block. | `ram/wram.asm:1921` |
| `wTilesetBank` | `$D0C2` | 1 | Stores tileset bank. | `ram/wram.asm:1922` |
| `wTilesetAddress` | `$D0C3-$D0C4` | 2 | Pointer/address for Tileset Address. | `ram/wram.asm:1923` |
| `wTilesetBlocksBank` | `$D0C5` | 1 | Stores tileset blocks bank. | `ram/wram.asm:1924` |
| `wTilesetBlocksAddress` | `$D0C6-$D0C7` | 2 | Pointer/address for Tileset Blocks Address. | `ram/wram.asm:1925` |
| `wTilesetCollisionBank` | `$D0C8` | 1 | Stores tileset collision bank. | `ram/wram.asm:1926` |
| `wTilesetCollisionAddress` | `$D0C9-$D0CA` | 2 | Pointer/address for Tileset Collision Address. | `ram/wram.asm:1927` |
| `wTilesetAnim` | `$D0CB-$D0CC` | 2 | bank 3f. | `ram/wram.asm:1928` |
| `wTilesetPalettes` | `$D0CF-$D0D0` | 2 | bank 3f. | `ram/wram.asm:1930` |
| `wTilesetEnd` | `$D0D1` | alias | End marker for Tileset. | `ram/wram.asm:1931` |
| `wEvolvableFlags` | `$D0D1` | 1 | Stores evolvable flags. | `ram/wram.asm:1934` |
| `wForceEvolution` | `$D0D2` | 1 | Stores force evolution. | `ram/wram.asm:1936` |
| `wHPBuffer1` | `$D0D3-$D0D4` | 2 | general-purpose HP buffers. | `ram/wram.asm:1940` |
| `wHPBuffer2` | `$D0D5-$D0D6` | 2 | Buffer/data field for hp buffer2. | `ram/wram.asm:1941` |
| `wHPBuffer3` | `$D0D7-$D0D8` | 2 | Buffer/data field for hp buffer3. | `ram/wram.asm:1942` |
| `wCurHPAnimMaxHP` | `$D0D3-$D0D4` | 2 | HP bar animations. | `ram/wram.asm:1946` |
| `wCurHPAnimOldHP` | `$D0D5-$D0D6` | 2 | Stores cur hp anim old hp. | `ram/wram.asm:1947` |
| `wCurHPAnimNewHP` | `$D0D7-$D0D8` | 2 | Stores cur hp anim new hp. | `ram/wram.asm:1948` |
| `wCurHPAnimPal` | `$D0D9` | 1 | Stores cur hp anim pal. | `ram/wram.asm:1949` |
| `wCurHPBarPixels` | `$D0DA` | 1 | Stores cur hp bar pixels. | `ram/wram.asm:1950` |
| `wNewHPBarPixels` | `$D0DB` | 1 | Stores new hp bar pixels. | `ram/wram.asm:1951` |
| `wCurHPAnimDeltaHP` | `$D0DC-$D0DD` | 2 | Stores cur hp anim delta hp. | `ram/wram.asm:1952` |
| `wCurHPAnimLowHP` | `$D0DE` | 1 | Stores cur hp anim low hp. | `ram/wram.asm:1953` |
| `wCurHPAnimHighHP` | `$D0DF` | 1 | Stores cur hp anim high hp. | `ram/wram.asm:1954` |
| `wEnemyAIMoveScores` | `$D0D3-$D0D6` | 4 | move AI. | `ram/wram.asm:1958` |
| `wEnemyEffectivenessVsPlayerMons` | `$D0D3` | 1 | switch AI. | `ram/wram.asm:1962` |
| `wPlayerEffectivenessVsEnemyMons` | `$D0D4` | 1 | Stores player effectiveness vs enemy mons. | `ram/wram.asm:1963` |
| `wBattleHUDTiles` | `$D0D3-$D0D8` | 6 | battle HUD. | `ram/wram.asm:1967` |
| `wFinalCatchRate` | `$D0D3` | 1 | thrown ball data. | `ram/wram.asm:1971` |
| `wThrownBallWobbleCount` | `$D0D4` | 1 | Stores thrown ball wobble count. | `ram/wram.asm:1972` |
| `wEvolutionOldSpecies` | `$D0D3` | 1 | evolution data. | `ram/wram.asm:1976` |
| `wEvolutionNewSpecies` | `$D0D4` | 1 | Stores evolution new species. | `ram/wram.asm:1977` |
| `wEvolutionPicOffset` | `$D0D5` | 1 | Stores evolution pic offset. | `ram/wram.asm:1978` |
| `wEvolutionCanceled` | `$D0D6` | 1 | Stores evolution canceled. | `ram/wram.asm:1979` |
| `wExpToNextLevel` | `$D0D3-$D0D5` | 3 | experience. | `ram/wram.asm:1983` |
| `wPPUpPPBuffer` | `$D0D3-$D0D6` | 4 | PP Up. | `ram/wram.asm:1987` |
| `wMonIDDigitsBuffer` | `$D0D3-$D0D7` | 5 | lucky number show. | `ram/wram.asm:1991` |
| `wMonSubmenuCount` | `$D0D3` | 1 | mon submenu. | `ram/wram.asm:1995` |
| `wMonSubmenuItems` | `$D0D4-$D0DC` | 9 | Stores mon submenu items. | `ram/wram.asm:1996` |
| `wFieldMoveData` | `$D0D3-$D0D9` | 7 | field move data. | `ram/wram.asm:2000` |
| `wFieldMoveJumptableIndex` | `$D0D3` | 1 | field move data. | `ram/wram.asm:2001` |
| `wEscapeRopeOrDigType` | `$D0D4` | alias | Stores escape rope or dig type. | `ram/wram.asm:2002` |
| `wSurfingPlayerState` | `$D0D4` | alias | Stores surfing player state. | `ram/wram.asm:2003` |
| `wFishingRodUsed` | `$D0D4` | 1 | Stores fishing rod used. | `ram/wram.asm:2004` |
| `wCutWhirlpoolOverworldBlockAddr` | `$D0D5-$D0D6` | 2 | Pointer/address for Cut Whirlpool Overworld Block Addr. | `ram/wram.asm:2005` |
| `wCutWhirlpoolReplacementBlock` | `$D0D7` | 1 | Stores cut whirlpool replacement block. | `ram/wram.asm:2006` |
| `wCutWhirlpoolAnimationType` | `$D0D8` | alias | Stores cut whirlpool animation type. | `ram/wram.asm:2007` |
| `wStrengthSpecies` | `$D0D8` | alias | Stores strength species. | `ram/wram.asm:2008` |
| `wFishingResult` | `$D0D8` | 1 | Stores fishing result. | `ram/wram.asm:2009` |
| `wFieldMoveDataEnd` | `$D0DA` | alias | End marker for Field Move Data. | `ram/wram.asm:2011` |
| `wCurMapScriptBank` | `$D0D3` | 1 | hidden items. | `ram/wram.asm:2015` |
| `wRemainingBGEventCount` | `$D0D4` | 1 | Stores remaining bg event count. | `ram/wram.asm:2016` |
| `wBottomRightYCoord` | `$D0D5` | 1 | Stores bottom right y coord. | `ram/wram.asm:2017` |
| `wBottomRightXCoord` | `$D0D6` | 1 | Stores bottom right x coord. | `ram/wram.asm:2018` |
| `wHealMachineAnimType` | `$D0D3` | 1 | heal machine anim. | `ram/wram.asm:2022` |
| `wHealMachineTempOBP1` | `$D0D4` | 1 | Stores heal machine temp obp1. | `ram/wram.asm:2023` |
| `wHealMachineAnimState` | `$D0D5` | 1 | Stores heal machine anim state. | `ram/wram.asm:2024` |
| `wCurDecoration` | `$D0D3` | 1 | decorations. | `ram/wram.asm:2028` |
| `wSelectedDecorationSide` | `$D0D4` | 1 | Stores selected decoration side. | `ram/wram.asm:2029` |
| `wSelectedDecoration` | `$D0D5` | 1 | Stores selected decoration. | `ram/wram.asm:2030` |
| `wOtherDecoration` | `$D0D6` | 1 | Stores other decoration. | `ram/wram.asm:2031` |
| `wChangedDecorations` | `$D0D7` | 1 | Stores changed decorations. | `ram/wram.asm:2032` |
| `wCurDecorationCategory` | `$D0D8` | 1 | Stores cur decoration category. | `ram/wram.asm:2033` |
| `wPCItemQuantityChange` | `$D0D3` | 1 | withdraw/deposit items. | `ram/wram.asm:2037` |
| `wPCItemQuantity` | `$D0D4` | 1 | Stores pc item quantity. | `ram/wram.asm:2038` |
| `wCurMailAuthorID` | `$D0D3-$D0D4` | 2 | mail. | `ram/wram.asm:2042` |
| `wCurMailIndex` | `$D0D5` | 1 | Stores cur mail index. | `ram/wram.asm:2043` |
| `wKurtApricornCount` | `$D0D3` | 1 | kurt. | `ram/wram.asm:2047` |
| `wKurtApricornItems` | `$D0D4-$D0DD` | 10 | Stores kurt apricorn items. | `ram/wram.asm:2048` |
| `wTreeMonCoordScore` | `$D0D3` | 1 | tree mons. | `ram/wram.asm:2052` |
| `wTreeMonOTIDScore` | `$D0D4` | 1 | Stores tree mon otid score. | `ram/wram.asm:2053` |
| `wRestartClockCurDivision` | `$D0D3` | 1 | restart clock. | `ram/wram.asm:2057` |
| `wRestartClockPrevDivision` | `$D0D4` | 1 | Stores restart clock prev division. | `ram/wram.asm:2058` |
| `wRestartClockUpArrowYCoord` | `$D0D5` | 1 | Stores restart clock up arrow y coord. | `ram/wram.asm:2059` |
| `wRestartClockDay` | `$D0D6` | 1 | Stores restart clock day. | `ram/wram.asm:2060` |
| `wRestartClockHour` | `$D0D7` | 1 | Stores restart clock hour. | `ram/wram.asm:2061` |
| `wRestartClockMin` | `$D0D8` | 1 | Stores restart clock min. | `ram/wram.asm:2062` |
| `wLinkBattleRNPreamble` | `$D0DC-$D0E2` | 7 | Stores link battle rn preamble. | `ram/wram.asm:2067` |
| `wLinkBattleRNs` | `$D0E3-$D0EC` | 10 | Stores link battle r ns. | `ram/wram.asm:2068` |
| `wSkipMovesBeforeLevelUp` | `$D0D3` | alias | miscellaneous bytes. | `ram/wram.asm:2072` |
| `wRegisteredPhoneNumbers` | `$D0D3` | alias | miscellaneous bytes. | `ram/wram.asm:2073` |
| `wListMovesLineSpacing` | `$D0D3` | 1 | miscellaneous bytes. | `ram/wram.asm:2074` |
| `wSwitchMonTo` | `$D0D4` | 1 | Stores switch mon to. | `ram/wram.asm:2075` |
| `wSwitchMonFrom` | `$D0D5` | 1 | Stores switch mon from. | `ram/wram.asm:2076` |
| `wCurEnemyItem` | `$D0DA` | 1 | Stores cur enemy item. | `ram/wram.asm:2078` |
| `wBuySellItemPrice` | `$D0D3` | alias | miscellaneous words. | `ram/wram.asm:2082` |
| `wTempMysteryGiftTimer` | `$D0D3` | alias | miscellaneous words. | `ram/wram.asm:2083` |
| `wMagikarpLength` | `$D0D3-$D0D4` | 2 | miscellaneous words. | `ram/wram.asm:2084` |
| `wTempEnemyMonSpecies` | `$D0ED` | 1 | Stores temp enemy mon species. | `ram/wram.asm:2087` |
| `wTempBattleMonSpecies` | `$D0EE` | 1 | Stores temp battle mon species. | `ram/wram.asm:2088` |
| `wOTLinkBattleRNData` | `$D0EF-$D0FF` | 17 | Buffer/data field for ot link battle rn data. | `ram/wram.asm:2091` |
| `wEnemyMon` | `$D0EF-$D10E` | 32 | Enemy-side active battler struct (battle_struct, 32 bytes). | `ram/wram.asm:2093` |
| `wEnemyMonBaseStats` | `$D10F-$D113` | 5 | Stores enemy mon base stats. | `ram/wram.asm:2094` |
| `wEnemyMonCatchRate` | `$D114` | 1 | Stores enemy mon catch rate. | `ram/wram.asm:2095` |
| `wEnemyMonBaseExp` | `$D115` | 1 | Stores enemy mon base exp. | `ram/wram.asm:2096` |
| `wEnemyMonEnd` | `$D116` | alias | End marker for Enemy Mon. | `ram/wram.asm:2097` |
| `wBattleMode` | `$D116` | 1 | 0=overworld, 1=wild battle, 2=trainer battle. | `ram/wram.asm:2100` |
| `wTempWildMonSpecies` | `$D117` | 1 | Stores temp wild mon species. | `ram/wram.asm:2106` |
| `wOtherTrainerClass` | `$D118` | 1 | Stores other trainer class. | `ram/wram.asm:2108` |
| `wBattleType` | `$D119` | 1 | Subtype of battle (tutorial, roaming legend, Safari-style, etc.). | `ram/wram.asm:2114` |
| `wOtherTrainerID` | `$D11B` | 1 | Stores other trainer id. | `ram/wram.asm:2118` |
| `wForcedSwitch` | `$D11C` | 1 | Stores forced switch. | `ram/wram.asm:2123` |
| `wTrainerClass` | `$D11D` | 1 | Stores trainer class. | `ram/wram.asm:2125` |
| `wUnownLetter` | `$D11E` | 1 | Stores unown letter. | `ram/wram.asm:2127` |
| `wMoveSelectionMenuType` | `$D11F` | 1 | Stores move selection menu type. | `ram/wram.asm:2129` |
| `wCurBaseData` | `$D120-$D13F` | 32 | corresponds to the data/pokemon/base_stats/*.asm contents. | `ram/wram.asm:2132` |
| `wBaseDexNo` | `$D120` | 1 | corresponds to the data/pokemon/base_stats/*.asm contents. | `ram/wram.asm:2133` |
| `wBaseStats` | `$D121` | alias | Stores base stats. | `ram/wram.asm:2134` |
| `wBaseHP` | `$D121` | 1 | Stores base hp. | `ram/wram.asm:2135` |
| `wBaseAttack` | `$D122` | 1 | Stores base attack. | `ram/wram.asm:2136` |
| `wBaseDefense` | `$D123` | 1 | Stores base defense. | `ram/wram.asm:2137` |
| `wBaseSpeed` | `$D124` | 1 | Stores base speed. | `ram/wram.asm:2138` |
| `wBaseSpecialAttack` | `$D125` | 1 | Stores base special attack. | `ram/wram.asm:2139` |
| `wBaseSpecialDefense` | `$D126` | 1 | Stores base special defense. | `ram/wram.asm:2140` |
| `wBaseType` | `$D127` | alias | Stores base type. | `ram/wram.asm:2141` |
| `wBaseType1` | `$D127` | 1 | Stores base type1. | `ram/wram.asm:2142` |
| `wBaseType2` | `$D128` | 1 | Stores base type2. | `ram/wram.asm:2143` |
| `wBaseCatchRate` | `$D129` | 1 | Stores base catch rate. | `ram/wram.asm:2144` |
| `wBaseExp` | `$D12A` | 1 | Stores base exp. | `ram/wram.asm:2145` |
| `wBaseItems` | `$D12B` | alias | Stores base items. | `ram/wram.asm:2146` |
| `wBaseItem1` | `$D12B` | 1 | Stores base item1. | `ram/wram.asm:2147` |
| `wBaseItem2` | `$D12C` | 1 | Stores base item2. | `ram/wram.asm:2148` |
| `wBaseGender` | `$D12D` | 1 | Stores base gender. | `ram/wram.asm:2149` |
| `wBaseUnknown1` | `$D12E` | 1 | Stores base unknown1. | `ram/wram.asm:2150` |
| `wBaseEggSteps` | `$D12F` | 1 | Stores base egg steps. | `ram/wram.asm:2151` |
| `wBaseUnknown2` | `$D130` | 1 | Stores base unknown2. | `ram/wram.asm:2152` |
| `wBasePicSize` | `$D131` | 1 | Stores base pic size. | `ram/wram.asm:2153` |
| `wBaseUnusedFrontpic` | `$D132-$D133` | 2 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:2154` |
| `wBaseUnusedBackpic` | `$D134-$D135` | 2 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:2155` |
| `wBaseGrowthRate` | `$D136` | 1 | Stores base growth rate. | `ram/wram.asm:2156` |
| `wBaseEggGroups` | `$D137` | 1 | Stores base egg groups. | `ram/wram.asm:2157` |
| `wBaseTMHM` | `$D138-$D13F` | 8 | Stores base tmhm. | `ram/wram.asm:2158` |
| `wCurBaseDataEnd` | `$D140` | alias | End marker for Cur Base Data. | `ram/wram.asm:2159` |
| `wCurDamage` | `$D141-$D142` | 2 | Stores cur damage. | `ram/wram.asm:2164` |
| `wMornEncounterRate` | `$D145` | 1 | Stores morn encounter rate. | `ram/wram.asm:2168` |
| `wDayEncounterRate` | `$D146` | 1 | Stores day encounter rate. | `ram/wram.asm:2169` |
| `wNiteEncounterRate` | `$D147` | 1 | Stores nite encounter rate. | `ram/wram.asm:2170` |
| `wWaterEncounterRate` | `$D148` | 1 | Stores water encounter rate. | `ram/wram.asm:2171` |
| `wListMoves_MoveIndicesBuffer` | `$D149-$D14C` | 4 | Buffer/data field for list moves move indices buffer. | `ram/wram.asm:2172` |
| `wPutativeTMHMMove` | `$D14D` | 1 | Stores putative tmhm move. | `ram/wram.asm:2173` |
| `wInitListType` | `$D14E` | 1 | Stores init list type. | `ram/wram.asm:2174` |
| `wWildMon` | `$D14F` | 1 | Stores wild mon. | `ram/wram.asm:2175` |
| `wBattleHasJustStarted` | `$D150` | 1 | Stores battle has just started. | `ram/wram.asm:2176` |
| `wNamedObjectIndex` | `$D151` | alias | Stores named object index. | `ram/wram.asm:2178` |
| `wTextDecimalByte` | `$D151` | alias | Buffer/data field for text decimal byte. | `ram/wram.asm:2179` |
| `wTempByteValue` | `$D151` | alias | Stores temp byte value. | `ram/wram.asm:2180` |
| `wNumSetBits` | `$D151` | alias | Stores num set bits. | `ram/wram.asm:2181` |
| `wTypeMatchup` | `$D151` | alias | Stores type matchup. | `ram/wram.asm:2182` |
| `wCurType` | `$D151` | alias | Stores cur type. | `ram/wram.asm:2183` |
| `wTempSpecies` | `$D151` | alias | Stores temp species. | `ram/wram.asm:2184` |
| `wTempIconSpecies` | `$D151` | alias | Stores temp icon species. | `ram/wram.asm:2185` |
| `wTempTMHM` | `$D151` | alias | Stores temp tmhm. | `ram/wram.asm:2186` |
| `wTempPP` | `$D151` | alias | Stores temp pp. | `ram/wram.asm:2187` |
| `wNextBoxOrPartyIndex` | `$D151` | alias | Stores next box or party index. | `ram/wram.asm:2188` |
| `wChosenCableClubRoom` | `$D151` | alias | Stores chosen cable club room. | `ram/wram.asm:2189` |
| `wBreedingCompatibility` | `$D151` | alias | Stores breeding compatibility. | `ram/wram.asm:2190` |
| `wMoveGrammar` | `$D151` | alias | Stores move grammar. | `ram/wram.asm:2191` |
| `wApplyStatLevelMultipliersToEnemy` | `$D151` | alias | Stores apply stat level multipliers to enemy. | `ram/wram.asm:2192` |
| `wUsePPUp` | `$D151` | 1 | Stores use pp up. | `ram/wram.asm:2193` |
| `wFailedToFlee` | `$D152` | 1 | Stores failed to flee. | `ram/wram.asm:2196` |
| `wNumFleeAttempts` | `$D153` | 1 | Stores num flee attempts. | `ram/wram.asm:2197` |
| `wMonTriedToEvolve` | `$D154` | 1 | Stores mon tried to evolve. | `ram/wram.asm:2198` |
| `wROMBankBackup` | `$D155` | 1 | Stores rom bank backup. | `ram/wram.asm:2200` |
| `wFarByte` | `$D156` | alias | Stores far byte. | `ram/wram.asm:2201` |
| `wTempBank` | `$D156` | 1 | Stores temp bank. | `ram/wram.asm:2202` |
| `wTimeOfDay` | `$D157` | 1 | Stores time of day. | `ram/wram.asm:2204` |
| `wMapStatus` | `$D159` | 1 | Alias for the start of Map Status block. | `ram/wram.asm:2208` |
| `wMapEventStatus` | `$D15A` | 1 | Stores map event status. | `ram/wram.asm:2209` |
| `wScriptFlags` | `$D15B` | 1 | Stores script flags. | `ram/wram.asm:2211` |
| `wEnabledPlayerEvents` | `$D15D` | 1 | Stores enabled player events. | `ram/wram.asm:2216` |
| `wScriptMode` | `$D15E` | 1 | Stores script mode. | `ram/wram.asm:2224` |
| `wScriptRunning` | `$D15F` | 1 | Stores script running. | `ram/wram.asm:2225` |
| `wScriptBank` | `$D160` | 1 | Stores script bank. | `ram/wram.asm:2226` |
| `wScriptPos` | `$D161-$D162` | 2 | Stores script pos. | `ram/wram.asm:2227` |
| `wScriptStackSize` | `$D163` | 1 | Stores script stack size. | `ram/wram.asm:2229` |
| `wScriptStack` | `$D164-$D172` | 15 | Stores script stack. | `ram/wram.asm:2230` |
| `wScriptVar` | `$D173` | 1 | Stores script var. | `ram/wram.asm:2231` |
| `wScriptDelay` | `$D174` | 1 | Stores script delay. | `ram/wram.asm:2232` |
| `wDeferredScriptBank` | `$D175` | alias | Stores deferred script bank. | `ram/wram.asm:2234` |
| `wScriptTextBank` | `$D175` | 1 | Stores script text bank. | `ram/wram.asm:2235` |
| `wDeferredScriptAddr` | `$D176` | alias | Pointer/address for Deferred Script Addr. | `ram/wram.asm:2237` |
| `wScriptTextAddr` | `$D176-$D177` | 2 | Pointer/address for Script Text Addr. | `ram/wram.asm:2238` |
| `wWildEncounterCooldown` | `$D179` | 1 | Stores wild encounter cooldown. | `ram/wram.asm:2241` |
| `wXYComparePointer` | `$D17A-$D17B` | 2 | Pointer/address for XY Compare Pointer. | `ram/wram.asm:2243` |
| `wXYCompareFlags` | `$D17C-$D17F` | 4 | Stores xy compare flags. | `ram/wram.asm:2244` |
| `wBattleScriptFlags` | `$D180` | 1 | Stores battle script flags. | `ram/wram.asm:2246` |
| `wPlayerSpriteSetupFlags` | `$D182` | 1 | Stores player sprite setup flags. | `ram/wram.asm:2248` |
| `wMapReentryScriptQueueFlag` | `$D183` | 1 | Stores map reentry script queue flag. | `ram/wram.asm:2251` |
| `wMapReentryScriptBank` | `$D184` | 1 | Stores map reentry script bank. | `ram/wram.asm:2252` |
| `wMapReentryScriptAddress` | `$D185-$D186` | 2 | Pointer/address for Map Reentry Script Address. | `ram/wram.asm:2253` |
| `wTimeCyclesSinceLastCall` | `$D18B` | 1 | Stores time cycles since last call. | `ram/wram.asm:2257` |
| `wReceiveCallDelay_MinsRemaining` | `$D18C` | 1 | Stores receive call delay mins remaining. | `ram/wram.asm:2258` |
| `wReceiveCallDelay_StartTime` | `$D18D-$D18F` | 3 | Stores receive call delay start time. | `ram/wram.asm:2259` |
| `wBugContestMinsRemaining` | `$D193` | 1 | Stores bug contest mins remaining. | `ram/wram.asm:2263` |
| `wBugContestSecsRemaining` | `$D194` | 1 | Stores bug contest secs remaining. | `ram/wram.asm:2264` |
| `wMapStatusEnd` | `$D197-$D198` | 2 | End marker for Map Status. | `ram/wram.asm:2268` |
| `wOptions` | `$D199` | 1 | Alias for the start of Options block. | `ram/wram.asm:2272` |
| `wSaveFileExists` | `$D19A` | 1 | Stores save file exists. | `ram/wram.asm:2282` |
| `wTextboxFrame` | `$D19B` | 1 | Buffer/data field for textbox frame. | `ram/wram.asm:2283` |
| `wTextboxFlags` | `$D19C` | 1 | Stores textbox flags. | `ram/wram.asm:2287` |
| `wGBPrinterBrightness` | `$D19D` | 1 | Stores gb printer brightness. | `ram/wram.asm:2291` |
| `wOptions2` | `$D19E` | 1 | Stores options2. | `ram/wram.asm:2299` |
| `wOptionsEnd` | `$D1A1` | alias | End marker for Options. | `ram/wram.asm:2305` |

### Game Data

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wGameData` | `$D1A1-$DE64` | 3268 | Saved WRAMX game-data block copied to SRAM bank 1. | `ram/wram.asm:2310` |
| `wPlayerData` | `$D1A1-$D951` | 1969 | Alias for all saved player-data sub-blocks. | `ram/wram.asm:2311` |
| `wPlayerData1` | `$D1A1-$D3C6` | 550 | Alias for the start of Player Data1 block. | `ram/wram.asm:2312` |
| `wPlayerID` | `$D1A1-$D1A2` | 2 | Stores player id. | `ram/wram.asm:2313` |
| `wPlayerName` | `$D1A3-$D1AD` | 11 | Buffer/data field for player name. | `ram/wram.asm:2315` |
| `wMomsName` | `$D1AE-$D1B8` | 11 | Buffer/data field for moms name. | `ram/wram.asm:2316` |
| `wRivalName` | `$D1B9-$D1C3` | 11 | Buffer/data field for rival name. | `ram/wram.asm:2317` |
| `wRedsName` | `$D1C4-$D1CE` | 11 | Buffer/data field for reds name. | `ram/wram.asm:2318` |
| `wGreensName` | `$D1CF-$D1D9` | 11 | Buffer/data field for greens name. | `ram/wram.asm:2319` |
| `wSavedAtLeastOnce` | `$D1DA` | 1 | Stores saved at least once. | `ram/wram.asm:2321` |
| `wSpawnAfterChampion` | `$D1DB` | 1 | Stores spawn after champion. | `ram/wram.asm:2322` |
| `wStartDay` | `$D1DC` | 1 | Saved day offset applied to RTC day count. | `ram/wram.asm:2325` |
| `wStartHour` | `$D1DD` | 1 | Saved hour offset applied to RTC hour value. | `ram/wram.asm:2326` |
| `wStartMinute` | `$D1DE` | 1 | Saved minute offset applied to RTC minute value. | `ram/wram.asm:2327` |
| `wStartSecond` | `$D1DF` | 1 | Saved second offset applied to RTC second value. | `ram/wram.asm:2328` |
| `wRTC` | `$D1E0-$D1E3` | 4 | Saved RTC snapshot written when the save file is staged. | `ram/wram.asm:2330` |
| `wDSTBackupDay` | `$D1E4` | 1 | Stores dst backup day. | `ram/wram.asm:2332` |
| `wDSTBackupHours` | `$D1E5` | 1 | Stores dst backup hours. | `ram/wram.asm:2333` |
| `wDSTBackupMinutes` | `$D1E6` | 1 | Stores dst backup minutes. | `ram/wram.asm:2334` |
| `wDSTBackupSeconds` | `$D1E7` | 1 | Stores dst backup seconds. | `ram/wram.asm:2335` |
| `wDST` | `$D1E8` | 1 | Daylight-savings-time flag byte. | `ram/wram.asm:2337` |
| `wGameTimeCap` | `$D1EA` | 1 | Stores game time cap. | `ram/wram.asm:2343` |
| `wGameTimeHours` | `$D1EB-$D1EC` | 2 | Stores game time hours. | `ram/wram.asm:2344` |
| `wGameTimeMinutes` | `$D1ED` | 1 | Stores game time minutes. | `ram/wram.asm:2345` |
| `wGameTimeSeconds` | `$D1EE` | 1 | Stores game time seconds. | `ram/wram.asm:2346` |
| `wGameTimeFrames` | `$D1EF` | 1 | Stores game time frames. | `ram/wram.asm:2347` |
| `wCurDay` | `$D1F2` | 1 | Current in-game weekday/day counter after RTC + start offset. | `ram/wram.asm:2351` |
| `wObjectFollow_Leader` | `$D1F4` | 1 | Stores object follow leader. | `ram/wram.asm:2355` |
| `wObjectFollow_Follower` | `$D1F5` | 1 | Stores object follow follower. | `ram/wram.asm:2356` |
| `wCenteredObject` | `$D1F6` | 1 | Stores centered object. | `ram/wram.asm:2357` |
| `wFollowerMovementQueueLength` | `$D1F7` | 1 | Buffer/data field for follower movement queue length. | `ram/wram.asm:2358` |
| `wFollowMovementQueue` | `$D1F8-$D1FC` | 5 | Buffer/data field for follow movement queue. | `ram/wram.asm:2359` |
| `wObjectStructs` | `$D1FD` | alias | Loaded runtime object structs for player + NPCs (13 x 40-byte object_struct). | `ram/wram.asm:2362` |
| `wPlayerStruct` | `$D1FD-$D224` | 40 | player is object struct 0. | `ram/wram.asm:2363` |
| `wObject1Struct` | `$D225-$D24C` | 40 | Loaded runtime object struct 1. | `ram/wram.asm:2366` |
| `wObject2Struct` | `$D24D-$D274` | 40 | Loaded runtime object struct 2. | `ram/wram.asm:2366` |
| `wObject3Struct` | `$D275-$D29C` | 40 | Loaded runtime object struct 3. | `ram/wram.asm:2366` |
| `wObject4Struct` | `$D29D-$D2C4` | 40 | Loaded runtime object struct 4. | `ram/wram.asm:2366` |
| `wObject5Struct` | `$D2C5-$D2EC` | 40 | Loaded runtime object struct 5. | `ram/wram.asm:2366` |
| `wObject6Struct` | `$D2ED-$D314` | 40 | Loaded runtime object struct 6. | `ram/wram.asm:2366` |
| `wObject7Struct` | `$D315-$D33C` | 40 | Loaded runtime object struct 7. | `ram/wram.asm:2366` |
| `wObject8Struct` | `$D33D-$D364` | 40 | Loaded runtime object struct 8. | `ram/wram.asm:2366` |
| `wObject9Struct` | `$D365-$D38C` | 40 | Loaded runtime object struct 9. | `ram/wram.asm:2366` |
| `wObject10Struct` | `$D38D-$D3B4` | 40 | Loaded runtime object struct 10. | `ram/wram.asm:2366` |
| `wObject11Struct` | `$D3B5-$D3DC` | 40 | Loaded runtime object struct 11. | `ram/wram.asm:2366` |
| `wObject12Struct` | `$D3DD-$D404` | 40 | Loaded runtime object struct 12. | `ram/wram.asm:2366` |
| `wPlayerData1End` | `$D3C7` | alias | End marker for Player Data1. | `ram/wram.asm:2370` |
| `wPlayerData2` | `$D3C7-$D570` | 426 | Alias for the start of Player Data2 block. | `ram/wram.asm:2371` |
| `wCmdQueue` | `$D405-$D41C` | 24 | Buffer/data field for cmd queue. | `ram/wram.asm:2374` |
| `wMapObjects` | `$D445` | alias | Static map object templates loaded for the current map (16 x 16-byte map_object). | `ram/wram.asm:2378` |
| `wPlayerObject` | `$D445-$D454` | 16 | player is map object 0. | `ram/wram.asm:2379` |
| `wMap1Object` | `$D455-$D464` | 16 | Static current-map object template 1. | `ram/wram.asm:2382` |
| `wMap2Object` | `$D465-$D474` | 16 | Static current-map object template 2. | `ram/wram.asm:2382` |
| `wMap3Object` | `$D475-$D484` | 16 | Static current-map object template 3. | `ram/wram.asm:2382` |
| `wMap4Object` | `$D485-$D494` | 16 | Static current-map object template 4. | `ram/wram.asm:2382` |
| `wMap5Object` | `$D495-$D4A4` | 16 | Static current-map object template 5. | `ram/wram.asm:2382` |
| `wMap6Object` | `$D4A5-$D4B4` | 16 | Static current-map object template 6. | `ram/wram.asm:2382` |
| `wMap7Object` | `$D4B5-$D4C4` | 16 | Static current-map object template 7. | `ram/wram.asm:2382` |
| `wMap8Object` | `$D4C5-$D4D4` | 16 | Static current-map object template 8. | `ram/wram.asm:2382` |
| `wMap9Object` | `$D4D5-$D4E4` | 16 | Static current-map object template 9. | `ram/wram.asm:2382` |
| `wMap10Object` | `$D4E5-$D4F4` | 16 | Static current-map object template 10. | `ram/wram.asm:2382` |
| `wMap11Object` | `$D4F5-$D504` | 16 | Static current-map object template 11. | `ram/wram.asm:2382` |
| `wMap12Object` | `$D505-$D514` | 16 | Static current-map object template 12. | `ram/wram.asm:2382` |
| `wMap13Object` | `$D515-$D524` | 16 | Static current-map object template 13. | `ram/wram.asm:2382` |
| `wMap14Object` | `$D525-$D534` | 16 | Static current-map object template 14. | `ram/wram.asm:2382` |
| `wMap15Object` | `$D535-$D544` | 16 | Static current-map object template 15. | `ram/wram.asm:2382` |
| `wObjectMasks` | `$D545-$D554` | 16 | Stores object masks. | `ram/wram.asm:2385` |
| `wVariableSprites` | `$D555-$D564` | 16 | Stores variable sprites. | `ram/wram.asm:2387` |
| `wUnusedReanchorBGMapFlags` | `$D565` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:2389` |
| `wTimeOfDayPal` | `$D568` | 1 | Stores time of day pal. | `ram/wram.asm:2391` |
| `wTimeOfDayPalFlags` | `$D56D` | 1 | Stores time of day pal flags. | `ram/wram.asm:2393` |
| `wTimeOfDayPalset` | `$D56E` | 1 | Stores time of day palset. | `ram/wram.asm:2394` |
| `wCurTimeOfDay` | `$D56F` | 1 | Stores cur time of day. | `ram/wram.asm:2395` |
| `wPlayerData2End` | `$D571` | alias | End marker for Player Data2. | `ram/wram.asm:2399` |
| `wPlayerData3` | `$D571-$D951` | 993 | Alias for the start of Player Data3 block. | `ram/wram.asm:2400` |
| `wStatusFlags` | `$D571` | 1 | Stores status flags. | `ram/wram.asm:2401` |
| `wStatusFlags2` | `$D572` | 1 | Stores status flags2. | `ram/wram.asm:2403` |
| `wMoney` | `$D573-$D575` | 3 | Stores money. | `ram/wram.asm:2405` |
| `wMomsMoney` | `$D576-$D578` | 3 | Stores moms money. | `ram/wram.asm:2406` |
| `wMomSavingMoney` | `$D579` | 1 | Stores mom saving money. | `ram/wram.asm:2408` |
| `wCoins` | `$D57A-$D57B` | 2 | Stores coins. | `ram/wram.asm:2415` |
| `wBadges` | `$D57C` | alias | Stores badges. | `ram/wram.asm:2417` |
| `wJohtoBadges` | `$D57C` | 1 | Stores johto badges. | `ram/wram.asm:2418` |
| `wKantoBadges` | `$D57D` | 1 | Stores kanto badges. | `ram/wram.asm:2419` |
| `wTMsHMs` | `$D57E-$D5B6` | 57 | Stores t ms h ms. | `ram/wram.asm:2421` |
| `wNumItems` | `$D5B7` | 1 | Stores num items. | `ram/wram.asm:2423` |
| `wItems` | `$D5B8-$D5E0` | 41 | Stores items. | `ram/wram.asm:2424` |
| `wNumKeyItems` | `$D5E1` | 1 | Stores num key items. | `ram/wram.asm:2426` |
| `wKeyItems` | `$D5E2-$D5FB` | 26 | Stores key items. | `ram/wram.asm:2427` |
| `wNumBalls` | `$D5FC` | 1 | Stores num balls. | `ram/wram.asm:2429` |
| `wBalls` | `$D5FD-$D615` | 25 | Stores balls. | `ram/wram.asm:2430` |
| `wNumPCItems` | `$D616` | 1 | Stores num pc items. | `ram/wram.asm:2432` |
| `wPCItems` | `$D617-$D67B` | 101 | Stores pc items. | `ram/wram.asm:2433` |
| `wPokegearFlags` | `$D67C` | 1 | Stores pokegear flags. | `ram/wram.asm:2435` |
| `wRadioTuningKnob` | `$D67D` | 1 | Stores radio tuning knob. | `ram/wram.asm:2442` |
| `wLastDexMode` | `$D67E` | 1 | Stores last dex mode. | `ram/wram.asm:2443` |
| `wWhichRegisteredItem` | `$D680` | 1 | Stores which registered item. | `ram/wram.asm:2445` |
| `wRegisteredItem` | `$D681` | 1 | Stores registered item. | `ram/wram.asm:2446` |
| `wPlayerState` | `$D682` | 1 | Stores player state. | `ram/wram.asm:2448` |
| `wHallOfFameCount` | `$D683` | 1 | Stores hall of fame count. | `ram/wram.asm:2450` |
| `wTradeFlags` | `$D685` | 1 | Stores trade flags. | `ram/wram.asm:2452` |
| `wMooMooBerries` | `$D6A7` | 1 | Stores moo moo berries. | `ram/wram.asm:2456` |
| `wUndergroundSwitchPositions` | `$D6A8` | 1 | Stores underground switch positions. | `ram/wram.asm:2457` |
| `wPokecenter2FSceneID` | `$D6B7` | 1 | Persistent scene-script state byte for Pokecenter2 F. | `ram/wram.asm:2461` |
| `wTradeCenterSceneID` | `$D6B8` | 1 | Persistent scene-script state byte for Trade Center. | `ram/wram.asm:2462` |
| `wColosseumSceneID` | `$D6B9` | 1 | Persistent scene-script state byte for Colosseum. | `ram/wram.asm:2463` |
| `wTimeCapsuleSceneID` | `$D6BA` | 1 | Persistent scene-script state byte for Time Capsule. | `ram/wram.asm:2464` |
| `wPowerPlantSceneID` | `$D6BB` | 1 | Persistent scene-script state byte for Power Plant. | `ram/wram.asm:2465` |
| `wCeruleanGymSceneID` | `$D6BC` | 1 | Persistent scene-script state byte for Cerulean Gym. | `ram/wram.asm:2466` |
| `wRoute25SceneID` | `$D6BD` | 1 | Persistent scene-script state byte for Route25. | `ram/wram.asm:2467` |
| `wTrainerHouseB1FSceneID` | `$D6BE` | 1 | Persistent scene-script state byte for Trainer House B1 F. | `ram/wram.asm:2468` |
| `wVictoryRoadGateSceneID` | `$D6BF` | 1 | Persistent scene-script state byte for Victory Road Gate. | `ram/wram.asm:2469` |
| `wSaffronMagnetTrainStationSceneID` | `$D6C0` | 1 | Persistent scene-script state byte for Saffron Magnet Train Station. | `ram/wram.asm:2470` |
| `wRoute16GateSceneID` | `$D6C1` | 1 | Persistent scene-script state byte for Route16 Gate. | `ram/wram.asm:2471` |
| `wRoute17Route18GateSceneID` | `$D6C2` | 1 | Persistent scene-script state byte for Route17 Route18 Gate. | `ram/wram.asm:2472` |
| `wIndigoPlateauPokecenter1FSceneID` | `$D6C3` | 1 | Persistent scene-script state byte for Indigo Plateau Pokecenter1 F. | `ram/wram.asm:2473` |
| `wWillsRoomSceneID` | `$D6C4` | 1 | Persistent scene-script state byte for Wills Room. | `ram/wram.asm:2474` |
| `wKogasRoomSceneID` | `$D6C5` | 1 | Persistent scene-script state byte for Kogas Room. | `ram/wram.asm:2475` |
| `wBrunosRoomSceneID` | `$D6C6` | 1 | Persistent scene-script state byte for Brunos Room. | `ram/wram.asm:2476` |
| `wKarensRoomSceneID` | `$D6C7` | 1 | Persistent scene-script state byte for Karens Room. | `ram/wram.asm:2477` |
| `wLancesRoomSceneID` | `$D6C8` | 1 | Persistent scene-script state byte for Lances Room. | `ram/wram.asm:2478` |
| `wHallOfFameSceneID` | `$D6C9` | 1 | Persistent scene-script state byte for Hall Of Fame. | `ram/wram.asm:2479` |
| `wRoute27SceneID` | `$D6CA` | 1 | Persistent scene-script state byte for Route27. | `ram/wram.asm:2480` |
| `wNewBarkTownSceneID` | `$D6CB` | 1 | Persistent scene-script state byte for New Bark Town. | `ram/wram.asm:2481` |
| `wElmsLabSceneID` | `$D6CC` | 1 | Persistent scene-script state byte for Elms Lab. | `ram/wram.asm:2482` |
| `wPlayersHouse1FSceneID` | `$D6CD` | 1 | Persistent scene-script state byte for Players House1 F. | `ram/wram.asm:2483` |
| `wRoute29SceneID` | `$D6CE` | 1 | Persistent scene-script state byte for Route29. | `ram/wram.asm:2484` |
| `wCherrygroveCitySceneID` | `$D6CF` | 1 | Persistent scene-script state byte for Cherrygrove City. | `ram/wram.asm:2485` |
| `wMrPokemonsHouseSceneID` | `$D6D0` | 1 | Persistent scene-script state byte for Mr Pokemons House. | `ram/wram.asm:2486` |
| `wRoute32SceneID` | `$D6D1` | 1 | Persistent scene-script state byte for Route32. | `ram/wram.asm:2487` |
| `wRoute35NationalParkGateSceneID` | `$D6D2` | 1 | Persistent scene-script state byte for Route35 National Park Gate. | `ram/wram.asm:2488` |
| `wRoute36NationalParkGateSceneID` | `$D6D3` | 1 | Persistent scene-script state byte for Route36 National Park Gate. | `ram/wram.asm:2489` |
| `wAzaleaTownSceneID` | `$D6D4` | 1 | Persistent scene-script state byte for Azalea Town. | `ram/wram.asm:2490` |
| `wGoldenrodGymSceneID` | `$D6D5` | 1 | Persistent scene-script state byte for Goldenrod Gym. | `ram/wram.asm:2491` |
| `wGoldenrodMagnetTrainStationSceneID` | `$D6D6` | 1 | Persistent scene-script state byte for Goldenrod Magnet Train Station. | `ram/wram.asm:2492` |
| `wOlivineCitySceneID` | `$D6D7` | 1 | Persistent scene-script state byte for Olivine City. | `ram/wram.asm:2493` |
| `wRoute34SceneID` | `$D6D8` | 1 | Persistent scene-script state byte for Route34. | `ram/wram.asm:2494` |
| `wEcruteakTinTowerEntranceSceneID` | `$D6D9` | 1 | Persistent scene-script state byte for Ecruteak Tin Tower Entrance. | `ram/wram.asm:2495` |
| `wEcruteakPokecenter1FSceneID` | `$D6DA` | 1 | Persistent scene-script state byte for Ecruteak Pokecenter1 F. | `ram/wram.asm:2496` |
| `wMahoganyTownSceneID` | `$D6DB` | 1 | Persistent scene-script state byte for Mahogany Town. | `ram/wram.asm:2497` |
| `wRoute43GateSceneID` | `$D6DC` | 1 | Persistent scene-script state byte for Route43 Gate. | `ram/wram.asm:2498` |
| `wMountMoonSceneID` | `$D6DD` | 1 | Persistent scene-script state byte for Mount Moon. | `ram/wram.asm:2499` |
| `wSproutTower3FSceneID` | `$D6DE` | 1 | Persistent scene-script state byte for Sprout Tower3 F. | `ram/wram.asm:2500` |
| `wBurnedTower1FSceneID` | `$D6DF` | 1 | Persistent scene-script state byte for Burned Tower1 F. | `ram/wram.asm:2501` |
| `wBurnedTowerB1FSceneID` | `$D6E0` | 1 | Persistent scene-script state byte for Burned Tower B1 F. | `ram/wram.asm:2502` |
| `wRadioTower5FSceneID` | `$D6E1` | 1 | Persistent scene-script state byte for Radio Tower5 F. | `ram/wram.asm:2503` |
| `wRuinsOfAlphOutsideSceneID` | `$D6E2` | 1 | Persistent scene-script state byte for Ruins Of Alph Outside. | `ram/wram.asm:2504` |
| `wRuinsOfAlphResearchCenterSceneID` | `$D6E3` | 1 | Persistent scene-script state byte for Ruins Of Alph Research Center. | `ram/wram.asm:2505` |
| `wRuinsOfAlphInnerChamberSceneID` | `$D6E4` | 1 | Persistent scene-script state byte for Ruins Of Alph Inner Chamber. | `ram/wram.asm:2506` |
| `wMahoganyMart1FSceneID` | `$D6E5` | 1 | Persistent scene-script state byte for Mahogany Mart1 F. | `ram/wram.asm:2507` |
| `wTeamRocketBaseB1FSceneID` | `$D6E6` | 1 | Persistent scene-script state byte for Team Rocket Base B1 F. | `ram/wram.asm:2508` |
| `wTeamRocketBaseB2FSceneID` | `$D6E7` | 1 | Persistent scene-script state byte for Team Rocket Base B2 F. | `ram/wram.asm:2509` |
| `wTeamRocketBaseB3FSceneID` | `$D6E8` | 1 | Persistent scene-script state byte for Team Rocket Base B3 F. | `ram/wram.asm:2510` |
| `wGoldenrodUndergroundSwitchRoomEntrancesSceneID` | `$D6E9` | 1 | Persistent scene-script state byte for Goldenrod Underground Switch Room Entrances. | `ram/wram.asm:2511` |
| `wSilverCaveRoom3SceneID` | `$D6EA` | 1 | Persistent scene-script state byte for Silver Cave Room3. | `ram/wram.asm:2512` |
| `wVictoryRoadSceneID` | `$D6EB` | 1 | Persistent scene-script state byte for Victory Road. | `ram/wram.asm:2513` |
| `wDragonsDenB1FSceneID` | `$D6EC` | 1 | Persistent scene-script state byte for Dragons Den B1 F. | `ram/wram.asm:2514` |
| `wOlivinePortSceneID` | `$D6ED` | 1 | Persistent scene-script state byte for Olivine Port. | `ram/wram.asm:2515` |
| `wVermilionPortSceneID` | `$D6EE` | 1 | Persistent scene-script state byte for Vermilion Port. | `ram/wram.asm:2516` |
| `wFastShip1FSceneID` | `$D6EF` | 1 | Persistent scene-script state byte for Fast Ship1 F. | `ram/wram.asm:2517` |
| `wFastShipB1FSceneID` | `$D6F0` | 1 | Persistent scene-script state byte for Fast Ship B1 F. | `ram/wram.asm:2518` |
| `wMountMoonSquareSceneID` | `$D6F1` | 1 | Persistent scene-script state byte for Mount Moon Square. | `ram/wram.asm:2519` |
| `wEventFlags` | `$D7B7-$D81A` | 100 | Stores event flags. | `ram/wram.asm:2523` |
| `wUnusedLinkCommunicationByte` | `$D81B` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:2525` |
| `wGameTimerPaused` | `$D81C` | 1 | Stores game timer paused. | `ram/wram.asm:2527` |
| `wJoypadDisable` | `$D81E` | 1 | Stores joypad disable. | `ram/wram.asm:2533` |
| `wCurBox` | `$D820` | 1 | Stores cur box. | `ram/wram.asm:2542` |
| `wBoxNames` | `$D823-$D8A0` | 126 | Buffer/data field for box names. | `ram/wram.asm:2546` |
| `wBikeFlags` | `$D8A3` | 1 | Stores bike flags. | `ram/wram.asm:2550` |
| `wCurMapSceneScriptPointer` | `$D8A5-$D8A6` | 2 | Pointer for current-map Cur Map Scene Script Pointer. | `ram/wram.asm:2557` |
| `wCurCaller` | `$D8A7-$D8A8` | 2 | Stores cur caller. | `ram/wram.asm:2559` |
| `wCurMapWarpEventCount` | `$D8A9` | 1 | Stores cur map warp event count. | `ram/wram.asm:2560` |
| `wCurMapWarpEventsPointer` | `$D8AA-$D8AB` | 2 | Pointer for current-map Cur Map Warp Events Pointer. | `ram/wram.asm:2561` |
| `wCurMapCoordEventCount` | `$D8AC` | 1 | Stores cur map coord event count. | `ram/wram.asm:2562` |
| `wCurMapCoordEventsPointer` | `$D8AD-$D8AE` | 2 | Pointer for current-map Cur Map Coord Events Pointer. | `ram/wram.asm:2563` |
| `wCurMapBGEventCount` | `$D8AF` | 1 | Stores cur map bg event count. | `ram/wram.asm:2564` |
| `wCurMapBGEventsPointer` | `$D8B0-$D8B1` | 2 | Pointer for current-map Cur Map BG Events Pointer. | `ram/wram.asm:2565` |
| `wCurMapObjectEventCount` | `$D8B2` | 1 | Stores cur map object event count. | `ram/wram.asm:2566` |
| `wCurMapObjectEventsPointer` | `$D8B3-$D8B4` | 2 | Pointer for current-map Cur Map Object Events Pointer. | `ram/wram.asm:2567` |
| `wCurMapSceneScriptCount` | `$D8B5` | 1 | Stores cur map scene script count. | `ram/wram.asm:2568` |
| `wCurMapSceneScriptsPointer` | `$D8B6-$D8B7` | 2 | Pointer for current-map Cur Map Scene Scripts Pointer. | `ram/wram.asm:2569` |
| `wCurMapCallbackCount` | `$D8B8` | 1 | Stores cur map callback count. | `ram/wram.asm:2570` |
| `wCurMapCallbacksPointer` | `$D8B9-$D8BA` | 2 | Pointer for current-map Cur Map Callbacks Pointer. | `ram/wram.asm:2571` |
| `wDecoBed` | `$D8BD` | 1 | Sprite id of each decoration. | `ram/wram.asm:2576` |
| `wDecoCarpet` | `$D8BE` | 1 | Stores deco carpet. | `ram/wram.asm:2577` |
| `wDecoPlant` | `$D8BF` | 1 | Stores deco plant. | `ram/wram.asm:2578` |
| `wDecoPoster` | `$D8C0` | 1 | Stores deco poster. | `ram/wram.asm:2579` |
| `wDecoConsole` | `$D8C1` | 1 | Stores deco console. | `ram/wram.asm:2580` |
| `wDecoLeftOrnament` | `$D8C2` | 1 | Stores deco left ornament. | `ram/wram.asm:2581` |
| `wDecoRightOrnament` | `$D8C3` | 1 | Stores deco right ornament. | `ram/wram.asm:2582` |
| `wDecoBigDoll` | `$D8C4` | 1 | Stores deco big doll. | `ram/wram.asm:2583` |
| `wWhichMomItem` | `$D8C5` | 1 | Items bought from Mom. | `ram/wram.asm:2586` |
| `wWhichMomItemSet` | `$D8C6` | 1 | Stores which mom item set. | `ram/wram.asm:2587` |
| `wMomItemTriggerBalance` | `$D8C7-$D8C9` | 3 | Stores mom item trigger balance. | `ram/wram.asm:2588` |
| `wDailyResetTimer` | `$D8CA-$D8CB` | 2 | Stores daily reset timer. | `ram/wram.asm:2590` |
| `wDailyFlags1` | `$D8CC` | 1 | Stores daily flags1. | `ram/wram.asm:2591` |
| `wDailyFlags2` | `$D8CD` | 1 | Stores daily flags2. | `ram/wram.asm:2592` |
| `wTimerEventStartDay` | `$D8D1` | 1 | Stores timer event start day. | `ram/wram.asm:2594` |
| `wFruitTreeFlags` | `$D8D5-$D8D8` | 4 | Stores fruit tree flags. | `ram/wram.asm:2597` |
| `wLuckyNumberDayTimer` | `$D8DB-$D8DC` | 2 | Stores lucky number day timer. | `ram/wram.asm:2601` |
| `wSpecialPhoneCallID` | `$D8DF` | 1 | Stores special phone call id. | `ram/wram.asm:2603` |
| `wBugContestStartTime` | `$D8E3-$D8E6` | 4 | day, hour, min, sec. | `ram/wram.asm:2605` |
| `wUnusedTwoDayTimerOn` | `$D8E7` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:2606` |
| `wUnusedTwoDayTimer` | `$D8E8` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:2607` |
| `wUnusedTwoDayTimerStartDate` | `$D8E9` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:2608` |
| `wStepCount` | `$D921` | 1 | Stores step count. | `ram/wram.asm:2612` |
| `wPoisonStepCount` | `$D922` | 1 | Stores poison step count. | `ram/wram.asm:2613` |
| `wHappinessStepCount` | `$D925` | 1 | Stores happiness step count. | `ram/wram.asm:2615` |
| `wParkBallsRemaining` | `$D927` | alias | Stores park balls remaining. | `ram/wram.asm:2618` |
| `wSafariBallsRemaining` | `$D927` | 1 | Stores safari balls remaining. | `ram/wram.asm:2619` |
| `wSafariTimeRemaining` | `$D928-$D929` | 2 | Stores safari time remaining. | `ram/wram.asm:2620` |
| `wPhoneList` | `$D92A-$D934` | 11 | Buffer/data field for phone list. | `ram/wram.asm:2622` |
| `wLuckyNumberShowFlag` | `$D94B` | 1 | Stores lucky number show flag. | `ram/wram.asm:2626` |
| `wLuckyIDNumber` | `$D94D-$D94E` | 2 | Stores lucky id number. | `ram/wram.asm:2628` |
| `wRepelEffect` | `$D94F` | 1 | If a Repel is in use, it contains the nr of steps it's still active. | `ram/wram.asm:2630` |
| `wBikeStep` | `$D950-$D951` | 2 | Stores bike step. | `ram/wram.asm:2631` |
| `wPlayerData3End` | `$D952` | alias | End marker for Player Data3. | `ram/wram.asm:2633` |
| `wPlayerDataEnd` | `$D952` | alias | End marker for Player Data. | `ram/wram.asm:2634` |
| `wCurMapData` | `$D952-$D985` | 52 | Current-map runtime state saved into SRAM with the main save. | `ram/wram.asm:2636` |
| `wVisitedSpawns` | `$D952-$D955` | 4 | Stores visited spawns. | `ram/wram.asm:2638` |
| `wDigWarpNumber` | `$D956` | 1 | Stores dig warp number. | `ram/wram.asm:2640` |
| `wDigMapGroup` | `$D957` | 1 | Buffer/data field for dig map group. | `ram/wram.asm:2641` |
| `wDigMapNumber` | `$D958` | 1 | Buffer/data field for dig map number. | `ram/wram.asm:2642` |
| `wBackupWarpNumber` | `$D959` | 1 | used on maps like second floor pokécenter, which are reused, so we know which | map to return to. | `ram/wram.asm:2646` |
| `wBackupMapGroup` | `$D95A` | 1 | Buffer/data field for backup map group. | `ram/wram.asm:2647` |
| `wBackupMapNumber` | `$D95B` | 1 | Buffer/data field for backup map number. | `ram/wram.asm:2648` |
| `wLastSpawnMapGroup` | `$D95F` | 1 | Buffer/data field for last spawn map group. | `ram/wram.asm:2652` |
| `wLastSpawnMapNumber` | `$D960` | 1 | Buffer/data field for last spawn map number. | `ram/wram.asm:2653` |
| `wWarpNumber` | `$D963` | 1 | Stores warp number. | `ram/wram.asm:2657` |
| `wMapGroup` | `$D964` | 1 | Current map group ID. | `ram/wram.asm:2658` |
| `wMapNumber` | `$D965` | 1 | Current map number within the map group. | `ram/wram.asm:2659` |
| `wYCoord` | `$D966` | 1 | Player Y tile coordinate on the current map. | `ram/wram.asm:2660` |
| `wXCoord` | `$D967` | 1 | Player X tile coordinate on the current map. | `ram/wram.asm:2661` |
| `wScreenSave` | `$D968-$D985` | 30 | Stores screen save. | `ram/wram.asm:2662` |
| `wCurMapDataEnd` | `$D986` | alias | End marker for current-map runtime save state. | `ram/wram.asm:2664` |

### Party

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wPokemonData` | `$D986-$DE64` | 1247 | Party/Pokédex/day-care data block copied to SRAM. | `ram/wram.asm:2669` |
| `wPartyCount` | `$D986` | 1 | Stores party count. | `ram/wram.asm:2670` |
| `wPartySpecies` | `$D987-$D98C` | 6 | Stores party species. | `ram/wram.asm:2671` |
| `wPartyEnd` | `$D98D` | 1 | End marker for Party. | `ram/wram.asm:2672` |
| `wPartyMons` | `$D98E` | alias | Party monster array; 6 party_struct entries (48 bytes each). | `ram/wram.asm:2674` |
| `wPartyMon1` | `$D98E-$D9BD` | 48 | Party monster 1 (party_struct, 48 bytes). | `ram/wram.asm:2677` |
| `wPartyMon2` | `$D9BE-$D9ED` | 48 | Party monster 2 (party_struct, 48 bytes). | `ram/wram.asm:2677` |
| `wPartyMon3` | `$D9EE-$DA1D` | 48 | Party monster 3 (party_struct, 48 bytes). | `ram/wram.asm:2677` |
| `wPartyMon4` | `$DA1E-$DA4D` | 48 | Party monster 4 (party_struct, 48 bytes). | `ram/wram.asm:2677` |
| `wPartyMon5` | `$DA4E-$DA7D` | 48 | Party monster 5 (party_struct, 48 bytes). | `ram/wram.asm:2677` |
| `wPartyMon6` | `$DA7E-$DAAD` | 48 | Party monster 6 (party_struct, 48 bytes). | `ram/wram.asm:2677` |
| `wPartyMonOTs` | `$DAAE` | alias | Original-trainer names for party monsters. | `ram/wram.asm:2680` |
| `wPartyMon1OT` | `$DAAE-$DAB8` | 11 | Original-trainer name for party monster 1. | `ram/wram.asm:2683` |
| `wPartyMon2OT` | `$DAB9-$DAC3` | 11 | Original-trainer name for party monster 2. | `ram/wram.asm:2683` |
| `wPartyMon3OT` | `$DAC4-$DACE` | 11 | Original-trainer name for party monster 3. | `ram/wram.asm:2683` |
| `wPartyMon4OT` | `$DACF-$DAD9` | 11 | Original-trainer name for party monster 4. | `ram/wram.asm:2683` |
| `wPartyMon5OT` | `$DADA-$DAE4` | 11 | Original-trainer name for party monster 5. | `ram/wram.asm:2683` |
| `wPartyMon6OT` | `$DAE5-$DAEF` | 11 | Original-trainer name for party monster 6. | `ram/wram.asm:2683` |
| `wPartyMonNicknames` | `$DAF0-$DB31` | 66 | Nicknames for party monsters. | `ram/wram.asm:2686` |
| `wPartyMon1Nickname` | `$DAF0-$DAFA` | 11 | Nickname for party monster 1. | `ram/wram.asm:2689` |
| `wPartyMon2Nickname` | `$DAFB-$DB05` | 11 | Nickname for party monster 2. | `ram/wram.asm:2689` |
| `wPartyMon3Nickname` | `$DB06-$DB10` | 11 | Nickname for party monster 3. | `ram/wram.asm:2689` |
| `wPartyMon4Nickname` | `$DB11-$DB1B` | 11 | Nickname for party monster 4. | `ram/wram.asm:2689` |
| `wPartyMon5Nickname` | `$DB1C-$DB26` | 11 | Nickname for party monster 5. | `ram/wram.asm:2689` |
| `wPartyMon6Nickname` | `$DB27-$DB31` | 11 | Nickname for party monster 6. | `ram/wram.asm:2689` |
| `wPartyMonNicknamesEnd` | `$DB32-$DB47` | 22 | End marker for Party Mon Nicknames. | `ram/wram.asm:2691` |
| `wPokedexCaught` | `$DB48-$DB67` | 32 | Bitfield of caught Pokédex species. | `ram/wram.asm:2695` |
| `wEndPokedexCaught` | `$DB68` | alias | Stores end pokedex caught. | `ram/wram.asm:2696` |
| `wPokedexSeen` | `$DB68-$DB87` | 32 | Bitfield of seen Pokédex species. | `ram/wram.asm:2698` |
| `wEndPokedexSeen` | `$DB88` | alias | Stores end pokedex seen. | `ram/wram.asm:2699` |
| `wUnownDex` | `$DB88-$DBA1` | 26 | Stores unown dex. | `ram/wram.asm:2701` |
| `wUnlockedUnowns` | `$DBA2` | 1 | Stores unlocked unowns. | `ram/wram.asm:2702` |
| `wFirstUnownSeen` | `$DBA3` | 1 | Stores first unown seen. | `ram/wram.asm:2703` |
| `wDayCareMan` | `$DBA4` | 1 | Stores day care man. | `ram/wram.asm:2705` |
| `wBreedMon1Nickname` | `$DBA5-$DBAF` | 11 | Buffer/data field for breed mon1 nickname. | `ram/wram.asm:2712` |
| `wBreedMon1OT` | `$DBB0-$DBBA` | 11 | Stores breed mon1 ot. | `ram/wram.asm:2713` |
| `wBreedMon1` | `$DBBB-$DBDA` | 32 | Stores breed mon1. | `ram/wram.asm:2714` |
| `wDayCareLady` | `$DBDB` | 1 | Stores day care lady. | `ram/wram.asm:2716` |
| `wStepsToEgg` | `$DBDC` | 1 | Stores steps to egg. | `ram/wram.asm:2721` |
| `wBreedMotherOrNonDitto` | `$DBDD` | 1 | Stores breed mother or non ditto. | `ram/wram.asm:2723` |
| `wBreedMon2Nickname` | `$DBDE-$DBE8` | 11 | Buffer/data field for breed mon2 nickname. | `ram/wram.asm:2728` |
| `wBreedMon2OT` | `$DBE9-$DBF3` | 11 | Stores breed mon2 ot. | `ram/wram.asm:2729` |
| `wBreedMon2` | `$DBF4-$DC13` | 32 | Stores breed mon2. | `ram/wram.asm:2730` |
| `wEggMonNickname` | `$DC14-$DC1E` | 11 | Buffer/data field for egg mon nickname. | `ram/wram.asm:2732` |
| `wEggMonOT` | `$DC1F-$DC29` | 11 | Stores egg mon ot. | `ram/wram.asm:2733` |
| `wEggMon` | `$DC2A-$DC49` | 32 | Stores egg mon. | `ram/wram.asm:2734` |
| `wBugContestSecondPartySpecies` | `$DC4A` | 1 | Stores bug contest second party species. | `ram/wram.asm:2736` |
| `wContestMon` | `$DC4B-$DC7A` | 48 | Stores contest mon. | `ram/wram.asm:2737` |
| `wSwarmMapGroup` | `$DC7B` | 1 | Buffer/data field for swarm map group. | `ram/wram.asm:2739` |
| `wSwarmMapNumber` | `$DC7C` | 1 | Buffer/data field for swarm map number. | `ram/wram.asm:2740` |
| `wFishingSwarmFlag` | `$DC7D` | 1 | Stores fishing swarm flag. | `ram/wram.asm:2741` |
| `wRoamMon1` | `$DC7E-$DC84` | 7 | Stores roam mon1. | `ram/wram.asm:2743` |
| `wRoamMon2` | `$DC85-$DC8B` | 7 | Stores roam mon2. | `ram/wram.asm:2744` |
| `wRoamMon3` | `$DC8C-$DC92` | 7 | Stores roam mon3. | `ram/wram.asm:2745` |
| `wRoamMons_CurMapNumber` | `$DC93` | 1 | Buffer/data field for roam mons cur map number. | `ram/wram.asm:2747` |
| `wRoamMons_CurMapGroup` | `$DC94` | 1 | Buffer/data field for roam mons cur map group. | `ram/wram.asm:2748` |
| `wRoamMons_LastMapNumber` | `$DC95` | 1 | Buffer/data field for roam mons last map number. | `ram/wram.asm:2749` |
| `wRoamMons_LastMapGroup` | `$DC96` | 1 | Buffer/data field for roam mons last map group. | `ram/wram.asm:2750` |
| `wBestMagikarpLengthFeet` | `$DC97` | 1 | Stores best magikarp length feet. | `ram/wram.asm:2752` |
| `wBestMagikarpLengthInches` | `$DC98` | 1 | Stores best magikarp length inches. | `ram/wram.asm:2753` |
| `wMagikarpRecordHoldersName` | `$DC99-$DCA3` | 11 | Buffer/data field for magikarp record holders name. | `ram/wram.asm:2754` |
| `wPokedexShowPointerAddr` | `$DCA4-$DCA5` | 2 | Pointer/address for Pokedex Show Pointer Addr. | `ram/wram.asm:2757` |
| `wPokedexShowPointerBank` | `$DCA6` | 1 | Pointer/address for Pokedex Show Pointer Bank. | `ram/wram.asm:2758` |
| `wUnusedEggHatchFlag` | `$DCA4` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/wram.asm:2762` |
| `wOTPartyData` | `$DCA4-$DE64` | 449 | enemy party. | `ram/wram.asm:2766` |
| `wOTPlayerName` | `$DCA4-$DCAE` | 11 | enemy party. | `ram/wram.asm:2767` |
| `wOTPlayerID` | `$DCAF-$DCB0` | 2 | Stores ot player id. | `ram/wram.asm:2768` |
| `wOTPartyCount` | `$DCB9` | 1 | Stores ot party count. | `ram/wram.asm:2770` |
| `wOTPartySpecies` | `$DCBA-$DCBF` | 6 | Stores ot party species. | `ram/wram.asm:2771` |
| `wOTPartyEnd` | `$DCC0` | 1 | End marker for OT Party. | `ram/wram.asm:2772` |
| `wOTPartyMons` | `$DCC1` | alias | ot party mons. | `ram/wram.asm:2777` |
| `wOTPartyMon1` | `$DCC1-$DCF0` | 48 | Opponent/trade party monster 1 (party_struct, 48 bytes). | `ram/wram.asm:2780` |
| `wOTPartyMon2` | `$DCF1-$DD20` | 48 | Opponent/trade party monster 2 (party_struct, 48 bytes). | `ram/wram.asm:2780` |
| `wOTPartyMon3` | `$DD21-$DD50` | 48 | Opponent/trade party monster 3 (party_struct, 48 bytes). | `ram/wram.asm:2780` |
| `wOTPartyMon4` | `$DD51-$DD80` | 48 | Opponent/trade party monster 4 (party_struct, 48 bytes). | `ram/wram.asm:2780` |
| `wOTPartyMon5` | `$DD81-$DDB0` | 48 | Opponent/trade party monster 5 (party_struct, 48 bytes). | `ram/wram.asm:2780` |
| `wOTPartyMon6` | `$DDB1-$DDE0` | 48 | Opponent/trade party monster 6 (party_struct, 48 bytes). | `ram/wram.asm:2780` |
| `wOTPartyMonOTs` | `$DDE1` | alias | Stores ot party mon o ts. | `ram/wram.asm:2783` |
| `wOTPartyMon1OT` | `$DDE1-$DDEB` | 11 | Original-trainer name for opponent/trade party monster 1. | `ram/wram.asm:2786` |
| `wOTPartyMon2OT` | `$DDEC-$DDF6` | 11 | Original-trainer name for opponent/trade party monster 2. | `ram/wram.asm:2786` |
| `wOTPartyMon3OT` | `$DDF7-$DE01` | 11 | Original-trainer name for opponent/trade party monster 3. | `ram/wram.asm:2786` |
| `wOTPartyMon4OT` | `$DE02-$DE0C` | 11 | Original-trainer name for opponent/trade party monster 4. | `ram/wram.asm:2786` |
| `wOTPartyMon5OT` | `$DE0D-$DE17` | 11 | Original-trainer name for opponent/trade party monster 5. | `ram/wram.asm:2786` |
| `wOTPartyMon6OT` | `$DE18-$DE22` | 11 | Original-trainer name for opponent/trade party monster 6. | `ram/wram.asm:2786` |
| `wOTPartyMonNicknames` | `$DE23` | alias | Buffer/data field for ot party mon nicknames. | `ram/wram.asm:2789` |
| `wOTPartyMon1Nickname` | `$DE23-$DE2D` | 11 | Nickname for opponent/trade party monster 1. | `ram/wram.asm:2792` |
| `wOTPartyMon2Nickname` | `$DE2E-$DE38` | 11 | Nickname for opponent/trade party monster 2. | `ram/wram.asm:2792` |
| `wOTPartyMon3Nickname` | `$DE39-$DE43` | 11 | Nickname for opponent/trade party monster 3. | `ram/wram.asm:2792` |
| `wOTPartyMon4Nickname` | `$DE44-$DE4E` | 11 | Nickname for opponent/trade party monster 4. | `ram/wram.asm:2792` |
| `wOTPartyMon5Nickname` | `$DE4F-$DE59` | 11 | Nickname for opponent/trade party monster 5. | `ram/wram.asm:2792` |
| `wOTPartyMon6Nickname` | `$DE5A-$DE64` | 11 | Nickname for opponent/trade party monster 6. | `ram/wram.asm:2792` |
| `wOTPartyDataEnd` | `$DE65` | alias | End marker for OT Party Data. | `ram/wram.asm:2794` |
| `wDudeNumItems` | `$DCC1` | 1 | catch tutorial dude pack. | `ram/wram.asm:2798` |
| `wDudeItems` | `$DCC2-$DCCA` | 9 | Stores dude items. | `ram/wram.asm:2799` |
| `wDudeNumKeyItems` | `$DCCB` | 1 | Stores dude num key items. | `ram/wram.asm:2801` |
| `wDudeKeyItems` | `$DCCC-$DCDE` | 19 | Stores dude key items. | `ram/wram.asm:2802` |
| `wDudeNumBalls` | `$DCDF` | 1 | Stores dude num balls. | `ram/wram.asm:2804` |
| `wDudeBalls` | `$DCE0-$DCE8` | 9 | Stores dude balls. | `ram/wram.asm:2805` |
| `wPokemonDataEnd` | `$DE65` | alias | End marker for the saved Pokémon-data block. | `ram/wram.asm:2808` |
| `wGameDataEnd` | `$DE65` | alias | End marker for Game Data. | `ram/wram.asm:2809` |

### Stack

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `wStackBottom` | `$DE67-$DF62` | 252 | Stores stack bottom. | `ram/wram.asm:2815` |
| `wStackTop` | `$DF63` | 1 | Stores stack top. | `ram/wram.asm:2817` |

## Detailed HRAM (`$FF80-$FFFE`) tables

### OAM DMA stub

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `hTransferShadowOAM` | `$FF80-$FF89` | 10 | HRAM-resident OAM DMA routine copied from ROM; writes `HIGH(wShadowOAM)` to `rDMA` and waits for completion. | `engine/gfx/load_push_oam.asm:13-27` |

### HRAM

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `hRTCDayHi` | `$FF8F` | 1 | Stores rtc day hi. | `ram/hram.asm:5` |
| `hRTCDayLo` | `$FF90` | 1 | Stores rtc day lo. | `ram/hram.asm:6` |
| `hRTCHours` | `$FF91` | 1 | Stores rtc hours. | `ram/hram.asm:7` |
| `hRTCMinutes` | `$FF92` | 1 | Stores rtc minutes. | `ram/hram.asm:8` |
| `hRTCSeconds` | `$FF93` | 1 | Stores rtc seconds. | `ram/hram.asm:9` |
| `hHours` | `$FF96` | 1 | Stores hours. | `ram/hram.asm:13` |
| `hMinutes` | `$FF98` | 1 | Stores minutes. | `ram/hram.asm:15` |
| `hSeconds` | `$FF9A` | 1 | Stores seconds. | `ram/hram.asm:17` |
| `hVBlankCounter` | `$FF9D` | 1 | Stores v blank counter. | `ram/hram.asm:22` |
| `hBlackOutBGMapThird` | `$FF9E` | 1 | Buffer/data field for black out bg map third. | `ram/hram.asm:24` |
| `hROMBank` | `$FF9F` | 1 | Stores rom bank. | `ram/hram.asm:26` |
| `hVBlank` | `$FFA0` | 1 | Stores v blank. | `ram/hram.asm:27` |
| `hMapEntryMethod` | `$FFA1` | 1 | Buffer/data field for map entry method. | `ram/hram.asm:28` |
| `hMenuReturn` | `$FFA2` | 1 | Stores menu return. | `ram/hram.asm:30` |
| `hUnusedByte` | `$FFA3` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/hram.asm:31` |
| `hJoypadReleased` | `$FFA4` | 1 | Buttons released this frame (raw VBlank joypad read). | `ram/hram.asm:33` |
| `hJoypadPressed` | `$FFA5` | 1 | Buttons pressed this frame (raw VBlank joypad read). | `ram/hram.asm:34` |
| `hJoypadDown` | `$FFA6` | 1 | Buttons currently held (raw VBlank joypad read). | `ram/hram.asm:35` |
| `hJoypadSum` | `$FFA7` | 1 | OR of all buttons pressed since the accumulator was cleared. | `ram/hram.asm:36` |
| `hJoyReleased` | `$FFA8` | 1 | Released buttons after mirroring through GetJoypad. | `ram/hram.asm:37` |
| `hJoyPressed` | `$FFA9` | 1 | Pressed buttons after mirroring through GetJoypad. | `ram/hram.asm:38` |
| `hJoyDown` | `$FFAA` | 1 | Buttons currently held after mirroring through GetJoypad. | `ram/hram.asm:39` |
| `hJoyLast` | `$FFAB` | 1 | Previous mirrored joypad state used by text/menu code. | `ram/hram.asm:40` |
| `hInMenu` | `$FFAC` | 1 | Stores in menu. | `ram/hram.asm:42` |
| `hPrinter` | `$FFAE` | 1 | Stores printer. | `ram/hram.asm:46` |
| `hGraphicStartTile` | `$FFAF` | 1 | Stores graphic start tile. | `ram/hram.asm:47` |
| `hMoveMon` | `$FFB0` | 1 | Stores move mon. | `ram/hram.asm:48` |
| `hMapObjectIndex` | `$FFB1` | 1 | Stores map object index. | `ram/hram.asm:51` |
| `hObjectStructIndex` | `$FFB2` | 1 | Stores object struct index. | `ram/hram.asm:52` |
| `hConnectionStripLength` | `$FFB1` | 1 | Stores connection strip length. | `ram/hram.asm:54` |
| `hConnectedMapWidth` | `$FFB2` | 1 | Buffer/data field for connected map width. | `ram/hram.asm:55` |
| `hEnemyMonSpeed` | `$FFB3-$FFB4` | 2 | Stores enemy mon speed. | `ram/hram.asm:58` |
| `hMultiplicand` | `$FFB6-$FFB8` | 3 | Stores multiplicand. | `ram/hram.asm:66` |
| `hMultiplier` | `$FFB9` | 1 | Stores multiplier. | `ram/hram.asm:67` |
| `hProduct` | `$FFB5-$FFB8` | 4 | result of Multiply. | `ram/hram.asm:70` |
| `hDividend` | `$FFB5-$FFB8` | 4 | inputs to Divide. | `ram/hram.asm:73` |
| `hDivisor` | `$FFB9` | 1 | Stores divisor. | `ram/hram.asm:74` |
| `hQuotient` | `$FFB5-$FFB8` | 4 | results of Divide. | `ram/hram.asm:77` |
| `hRemainder` | `$FFB9` | 1 | Stores remainder. | `ram/hram.asm:78` |
| `hMathBuffer` | `$FFBA-$FFBE` | 5 | Buffer/data field for math buffer. | `ram/hram.asm:81` |
| `hPrintNumBuffer` | `$FFB5-$FFBE` | 10 | PrintNum scratch space. | `ram/hram.asm:85` |
| `hMGExchangedByte` | `$FFB5` | 1 | Mystery Gift. | `ram/hram.asm:89` |
| `hMGExchangedWord` | `$FFB6-$FFB7` | 2 | Stores mg exchanged word. | `ram/hram.asm:90` |
| `hMGNumBits` | `$FFB8` | 1 | Stores mg num bits. | `ram/hram.asm:91` |
| `hMGChecksum` | `$FFB9-$FFBA` | 2 | Stores mg checksum. | `ram/hram.asm:92` |
| `hMGUnusedMsgLength` | `$FFBC` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/hram.asm:94` |
| `hMGRole` | `$FFBD` | 1 | Stores mg role. | `ram/hram.asm:95` |
| `hMGStatusFlags` | `$FFBE` | 1 | Stores mg status flags. | `ram/hram.asm:96` |
| `hUsedSpriteIndex` | `$FFBF` | 1 | Stores used sprite index. | `ram/hram.asm:100` |
| `hUsedSpriteTile` | `$FFC0` | 1 | Stores used sprite tile. | `ram/hram.asm:101` |
| `hCurSpriteXCoord` | `$FFBF` | 1 | Stores cur sprite x coord. | `ram/hram.asm:103` |
| `hCurSpriteYCoord` | `$FFC0` | 1 | Stores cur sprite y coord. | `ram/hram.asm:104` |
| `hCurSpriteXPixel` | `$FFC1` | 1 | Stores cur sprite x pixel. | `ram/hram.asm:105` |
| `hCurSpriteYPixel` | `$FFC2` | 1 | Stores cur sprite y pixel. | `ram/hram.asm:106` |
| `hCurSpriteTile` | `$FFC3` | 1 | Stores cur sprite tile. | `ram/hram.asm:107` |
| `hCurSpriteOAMFlags` | `$FFC4` | 1 | Stores cur sprite oam flags. | `ram/hram.asm:108` |
| `hMoneyTemp` | `$FFC5-$FFC7` | 3 | Stores money temp. | `ram/hram.asm:112` |
| `hMGJoypadPressed` | `$FFC5` | 1 | Stores mg joypad pressed. | `ram/hram.asm:114` |
| `hMGJoypadReleased` | `$FFC6` | 1 | Stores mg joypad released. | `ram/hram.asm:115` |
| `hMGPrevTIMA` | `$FFC7` | 1 | Stores mg prev tima. | `ram/hram.asm:116` |
| `hLCDCPointer` | `$FFC8` | 1 | Pointer/address for LCDC Pointer. | `ram/hram.asm:119` |
| `hLYOverrideStart` | `$FFC9` | 1 | Start marker for LY Override. | `ram/hram.asm:120` |
| `hLYOverrideEnd` | `$FFCA` | 1 | End marker for LY Override. | `ram/hram.asm:121` |
| `hSerialReceivedNewData` | `$FFCC` | 1 | Buffer/data field for serial received new data. | `ram/hram.asm:125` |
| `hSerialConnectionStatus` | `$FFCD` | 1 | Stores serial connection status. | `ram/hram.asm:126` |
| `hSerialIgnoringInitialData` | `$FFCE` | 1 | Buffer/data field for serial ignoring initial data. | `ram/hram.asm:127` |
| `hSerialSend` | `$FFCF` | 1 | Stores serial send. | `ram/hram.asm:128` |
| `hSerialReceive` | `$FFD0` | 1 | Stores serial receive. | `ram/hram.asm:129` |
| `hSCX` | `$FFD1` | 1 | Stores scx. | `ram/hram.asm:131` |
| `hSCY` | `$FFD2` | 1 | Stores scy. | `ram/hram.asm:132` |
| `hWX` | `$FFD3` | 1 | Stores wx. | `ram/hram.asm:133` |
| `hWY` | `$FFD4` | 1 | Stores wy. | `ram/hram.asm:134` |
| `hTilesPerCycle` | `$FFD5` | 1 | Stores tiles per cycle. | `ram/hram.asm:135` |
| `hBGMapMode` | `$FFD6` | 1 | Stores bg map mode. | `ram/hram.asm:136` |
| `hBGMapThird` | `$FFD7` | 1 | Buffer/data field for bg map third. | `ram/hram.asm:137` |
| `hBGMapAddress` | `$FFD8-$FFD9` | 2 | Pointer/address for BG Map Address. | `ram/hram.asm:138` |
| `hOAMUpdate` | `$FFDA` | 1 | Stores oam update. | `ram/hram.asm:140` |
| `hSPBuffer` | `$FFDB-$FFDC` | 2 | Buffer/data field for sp buffer. | `ram/hram.asm:142` |
| `hBGMapUpdate` | `$FFDD` | 1 | Buffer/data field for bg map update. | `ram/hram.asm:144` |
| `hBGMapTileCount` | `$FFDE` | 1 | Stores bg map tile count. | `ram/hram.asm:145` |
| `hMapAnims` | `$FFE0` | 1 | Buffer/data field for map anims. | `ram/hram.asm:149` |
| `hTileAnimFrame` | `$FFE1` | 1 | Stores tile anim frame. | `ram/hram.asm:150` |
| `hLastTalked` | `$FFE2` | 1 | Stores last talked. | `ram/hram.asm:152` |
| `hRandomAdd` | `$FFE3` | 1 | Primary 8-bit RNG accumulator updated from DIV. | `ram/hram.asm:154` |
| `hRandomSub` | `$FFE4` | 1 | Secondary 8-bit RNG accumulator updated from DIV. | `ram/hram.asm:155` |
| `hUnusedBackup` | `$FFE5` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/hram.asm:157` |
| `hBattleTurn` | `$FFE6` | 1 | Stores battle turn. | `ram/hram.asm:159` |
| `hCGBPalUpdate` | `$FFE7` | 1 | Stores cgb pal update. | `ram/hram.asm:163` |
| `hCGB` | `$FFE8` | 1 | Stores cgb. | `ram/hram.asm:164` |
| `hSGB` | `$FFE9` | 1 | Stores sgb. | `ram/hram.asm:165` |
| `hDebugRoomMenuPage` | `$FFEA` | 1 | Stores debug room menu page. | `ram/hram.asm:168` |

## Detailed SRAM tables

### Scratch (SRAM bank 0)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sScratch` | `$A000` | alias | Stores scratch. | `ram/sram.asm:4` |
| `sDecompressScratch` | `$A000-$A5FF` | 1536 | Stores decompress scratch. | `ram/sram.asm:5` |
| `sDecompressBuffer` | `$A188-$A497` | 784 | Buffer/data field for decompress buffer. | `ram/sram.asm:10` |

### SRAM Bank 0 (SRAM bank 0)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sPartyMail` | `$A600` | alias | Stores party mail. | `ram/sram.asm:16` |
| `sPartyMon1Mail` | `$A600-$A62E` | 47 | Saved party-mon mail struct for party slot 1. | `ram/sram.asm:19` |
| `sPartyMon2Mail` | `$A62F-$A65D` | 47 | Saved party-mon mail struct for party slot 2. | `ram/sram.asm:19` |
| `sPartyMon3Mail` | `$A65E-$A68C` | 47 | Saved party-mon mail struct for party slot 3. | `ram/sram.asm:19` |
| `sPartyMon4Mail` | `$A68D-$A6BB` | 47 | Saved party-mon mail struct for party slot 4. | `ram/sram.asm:19` |
| `sPartyMon5Mail` | `$A6BC-$A6EA` | 47 | Saved party-mon mail struct for party slot 5. | `ram/sram.asm:19` |
| `sPartyMon6Mail` | `$A6EB-$A719` | 47 | Saved party-mon mail struct for party slot 6. | `ram/sram.asm:19` |
| `sPartyMailBackup` | `$A71A` | alias | Stores party mail backup. | `ram/sram.asm:22` |
| `sPartyMon1MailBackup` | `$A71A-$A748` | 47 | Backup saved party-mon mail struct for party slot 1. | `ram/sram.asm:25` |
| `sPartyMon2MailBackup` | `$A749-$A777` | 47 | Backup saved party-mon mail struct for party slot 2. | `ram/sram.asm:25` |
| `sPartyMon3MailBackup` | `$A778-$A7A6` | 47 | Backup saved party-mon mail struct for party slot 3. | `ram/sram.asm:25` |
| `sPartyMon4MailBackup` | `$A7A7-$A7D5` | 47 | Backup saved party-mon mail struct for party slot 4. | `ram/sram.asm:25` |
| `sPartyMon5MailBackup` | `$A7D6-$A804` | 47 | Backup saved party-mon mail struct for party slot 5. | `ram/sram.asm:25` |
| `sPartyMon6MailBackup` | `$A805-$A833` | 47 | Backup saved party-mon mail struct for party slot 6. | `ram/sram.asm:25` |
| `sMailboxCount` | `$A834` | 1 | Stores mailbox count. | `ram/sram.asm:28` |
| `sMailboxes` | `$A835` | alias | Stores mailboxes. | `ram/sram.asm:29` |
| `sMailbox1` | `$A835-$A863` | 47 | Mailbox mail struct 1. | `ram/sram.asm:32` |
| `sMailbox2` | `$A864-$A892` | 47 | Mailbox mail struct 2. | `ram/sram.asm:32` |
| `sMailbox3` | `$A893-$A8C1` | 47 | Mailbox mail struct 3. | `ram/sram.asm:32` |
| `sMailbox4` | `$A8C2-$A8F0` | 47 | Mailbox mail struct 4. | `ram/sram.asm:32` |
| `sMailbox5` | `$A8F1-$A91F` | 47 | Mailbox mail struct 5. | `ram/sram.asm:32` |
| `sMailbox6` | `$A920-$A94E` | 47 | Mailbox mail struct 6. | `ram/sram.asm:32` |
| `sMailbox7` | `$A94F-$A97D` | 47 | Mailbox mail struct 7. | `ram/sram.asm:32` |
| `sMailbox8` | `$A97E-$A9AC` | 47 | Mailbox mail struct 8. | `ram/sram.asm:32` |
| `sMailbox9` | `$A9AD-$A9DB` | 47 | Mailbox mail struct 9. | `ram/sram.asm:32` |
| `sMailbox10` | `$A9DC-$AA0A` | 47 | Mailbox mail struct 10. | `ram/sram.asm:32` |
| `sMailboxCountBackup` | `$AA0B` | 1 | Stores mailbox count backup. | `ram/sram.asm:35` |
| `sMailboxesBackup` | `$AA0C` | alias | Stores mailboxes backup. | `ram/sram.asm:36` |
| `sMailbox1Backup` | `$AA0C-$AA3A` | 47 | Backup mailbox mail struct 1. | `ram/sram.asm:39` |
| `sMailbox2Backup` | `$AA3B-$AA69` | 47 | Backup mailbox mail struct 2. | `ram/sram.asm:39` |
| `sMailbox3Backup` | `$AA6A-$AA98` | 47 | Backup mailbox mail struct 3. | `ram/sram.asm:39` |
| `sMailbox4Backup` | `$AA99-$AAC7` | 47 | Backup mailbox mail struct 4. | `ram/sram.asm:39` |
| `sMailbox5Backup` | `$AAC8-$AAF6` | 47 | Backup mailbox mail struct 5. | `ram/sram.asm:39` |
| `sMailbox6Backup` | `$AAF7-$AB25` | 47 | Backup mailbox mail struct 6. | `ram/sram.asm:39` |
| `sMailbox7Backup` | `$AB26-$AB54` | 47 | Backup mailbox mail struct 7. | `ram/sram.asm:39` |
| `sMailbox8Backup` | `$AB55-$AB83` | 47 | Backup mailbox mail struct 8. | `ram/sram.asm:39` |
| `sMailbox9Backup` | `$AB84-$ABB2` | 47 | Backup mailbox mail struct 9. | `ram/sram.asm:39` |
| `sMailbox10Backup` | `$ABB3-$ABE1` | 47 | Backup mailbox mail struct 10. | `ram/sram.asm:39` |
| `sMysteryGiftData` | `$ABE2` | alias | Buffer/data field for mystery gift data. | `ram/sram.asm:42` |
| `sMysteryGiftItem` | `$ABE2` | 1 | Stores mystery gift item. | `ram/sram.asm:43` |
| `sMysteryGiftUnlocked` | `$ABE3` | 1 | Stores mystery gift unlocked. | `ram/sram.asm:44` |
| `sBackupMysteryGiftItem` | `$ABE4` | 1 | Alias for the start of Backup Mystery Gift Item block. | `ram/sram.asm:45` |
| `sNumDailyMysteryGiftPartnerIDs` | `$ABE5` | 1 | Stores num daily mystery gift partner i ds. | `ram/sram.asm:46` |
| `sDailyMysteryGiftPartnerIDs` | `$ABE6-$ABEF` | 10 | Stores daily mystery gift partner i ds. | `ram/sram.asm:47` |
| `sMysteryGiftDecorationsReceived` | `$ABF0-$ABF5` | 6 | Stores mystery gift decorations received. | `ram/sram.asm:48` |
| `sMysteryGiftTimer` | `$ABFA-$ABFB` | 2 | Stores mystery gift timer. | `ram/sram.asm:50` |
| `sMysteryGiftTrainerHouseFlag` | `$ABFD` | 1 | Stores mystery gift trainer house flag. | `ram/sram.asm:52` |
| `sMysteryGiftPartnerName` | `$ABFE-$AC08` | 11 | Buffer/data field for mystery gift partner name. | `ram/sram.asm:53` |
| `sMysteryGiftUnusedFlag` | `$AC09` | 1 | UNCLEAR: reserved/unused symbol; no stronger purpose comment in source. | `ram/sram.asm:54` |
| `sMysteryGiftTrainer` | `$AC0A-$AC2F` | 38 | Stores mystery gift trainer. | `ram/sram.asm:55` |
| `sBackupMysteryGiftItemEnd` | `$AC30-$AC5F` | 48 | End marker for Backup Mystery Gift Item. | `ram/sram.asm:56` |
| `sRTCStatusFlags` | `$AC60` | 1 | Stores rtc status flags. | `ram/sram.asm:60` |
| `sLuckyNumberDay` | `$AC68` | 1 | Stores lucky number day. | `ram/sram.asm:62` |
| `sLuckyIDNumber` | `$AC69-$AC6A` | 2 | Stores lucky id number. | `ram/sram.asm:63` |

### Backup Save 1 (SRAM bank 0)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sBackupPlayerData3` | `$AC6B-$B04B` | 993 | Buffer/data field for backup player data3. | `ram/sram.asm:68` |
| `sBackupPokemonData` | `$B04C-$B52A` | 1247 | Buffer/data field for backup pokemon data. | `ram/sram.asm:69` |
| `sBackupPlayerData1` | `$B52B-$B750` | 550 | Buffer/data field for backup player data1. | `ram/sram.asm:70` |

### SRAM Stack (SRAM bank 0)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sStackTop` | `$B751-$B752` | 2 | Stores stack top. | `ram/sram.asm:75` |
| `sRTCHaltCheckValue` | `$B753-$B754` | 2 | Stores rtc halt check value. | `ram/sram.asm:76` |

### SRAM Window Stack (SRAM bank 0)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sWindowStackBottom` | `$B800-$BFFE` | 2047 | Stores window stack bottom. | `ram/sram.asm:81` |
| `sWindowStackTop` | `$BFFF` | 1 | Stores window stack top. | `ram/sram.asm:83` |

### Save (SRAM bank 1)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sOptions` | `$A000-$A007` | 8 | Stores options. | `ram/sram.asm:89` |
| `sCheckValue1` | `$A008` | 1 | loaded with SAVE_CHECK_VALUE_1, used to check save corruption. | `ram/sram.asm:91` |
| `sGameData` | `$A009-$ACCC` | 3268 | Main save payload in SRAM bank 1. | `ram/sram.asm:93` |
| `sPlayerData` | `$A009` | alias | Buffer/data field for player data. | `ram/sram.asm:94` |
| `sPlayerData1` | `$A009-$A22E` | 550 | Buffer/data field for player data1. | `ram/sram.asm:95` |
| `sPlayerData2` | `$A22F-$A3D8` | 426 | Buffer/data field for player data2. | `ram/sram.asm:96` |
| `sPlayerData3` | `$A3D9-$A7B9` | 993 | Buffer/data field for player data3. | `ram/sram.asm:97` |
| `sCurMapData` | `$A7BA-$A7ED` | 52 | Buffer/data field for cur map data. | `ram/sram.asm:98` |
| `sPokemonData` | `$A7EE-$ACCC` | 1247 | Buffer/data field for pokemon data. | `ram/sram.asm:99` |
| `sGameDataEnd` | `$ACCD` | alias | End marker for Game Data. | `ram/sram.asm:100` |
| `sChecksum` | `$ACCD-$ACCE` | 2 | 16-bit additive checksum over sGameData. | `ram/sram.asm:102` |
| `sCheckValue2` | `$ACCF` | 1 | loaded with SAVE_CHECK_VALUE_2, used to check save corruption. | `ram/sram.asm:104` |

### Active Box (SRAM bank 1)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sBox` | `$ACD0-$B11D` | 1102 | Currently active PC box (curbox layout, no 2-byte padding). | `ram/sram.asm:109` |

### Link Battle Data (SRAM bank 1)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sLinkBattleStats` | `$B11E-$B169` | 76 | Persistent link-battle win/loss/draw history. | `ram/sram.asm:114` |
| `sLinkBattleWins` | `$B11E-$B11F` | 2 | Stores link battle wins. | `ram/sram.asm:115` |
| `sLinkBattleLosses` | `$B120-$B121` | 2 | Stores link battle losses. | `ram/sram.asm:116` |
| `sLinkBattleDraws` | `$B122-$B123` | 2 | Stores link battle draws. | `ram/sram.asm:117` |
| `sLinkBattleRecord` | `$B124` | alias | Stores link battle record. | `ram/sram.asm:119` |
| `sLinkBattleRecord1` | `$B124-$B131` | 14 | Persistent link-battle record slot 1. | `ram/sram.asm:122` |
| `sLinkBattleRecord2` | `$B132-$B13F` | 14 | Persistent link-battle record slot 2. | `ram/sram.asm:122` |
| `sLinkBattleRecord3` | `$B140-$B14D` | 14 | Persistent link-battle record slot 3. | `ram/sram.asm:122` |
| `sLinkBattleRecord4` | `$B14E-$B15B` | 14 | Persistent link-battle record slot 4. | `ram/sram.asm:122` |
| `sLinkBattleRecord5` | `$B15C-$B169` | 14 | Persistent link-battle record slot 5. | `ram/sram.asm:122` |
| `sLinkBattleStatsEnd` | `$B16A` | alias | End marker for Link Battle Stats. | `ram/sram.asm:124` |

### SRAM Hall of Fame (SRAM bank 1)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sHallOfFame` | `$B16A-$BCE5` | 2940 | 30-entry Hall of Fame log stored in SRAM bank 1. | `ram/sram.asm:129` |
| `sHallOfFame1` | `$B16A-$B1CB` | 98 | Hall of Fame team slot 1. | `ram/sram.asm:132` |
| `sHallOfFame2` | `$B1CC-$B22D` | 98 | Hall of Fame team slot 2. | `ram/sram.asm:132` |
| `sHallOfFame3` | `$B22E-$B28F` | 98 | Hall of Fame team slot 3. | `ram/sram.asm:132` |
| `sHallOfFame4` | `$B290-$B2F1` | 98 | Hall of Fame team slot 4. | `ram/sram.asm:132` |
| `sHallOfFame5` | `$B2F2-$B353` | 98 | Hall of Fame team slot 5. | `ram/sram.asm:132` |
| `sHallOfFame6` | `$B354-$B3B5` | 98 | Hall of Fame team slot 6. | `ram/sram.asm:132` |
| `sHallOfFame7` | `$B3B6-$B417` | 98 | Hall of Fame team slot 7. | `ram/sram.asm:132` |
| `sHallOfFame8` | `$B418-$B479` | 98 | Hall of Fame team slot 8. | `ram/sram.asm:132` |
| `sHallOfFame9` | `$B47A-$B4DB` | 98 | Hall of Fame team slot 9. | `ram/sram.asm:132` |
| `sHallOfFame10` | `$B4DC-$B53D` | 98 | Hall of Fame team slot 10. | `ram/sram.asm:132` |
| `sHallOfFame11` | `$B53E-$B59F` | 98 | Hall of Fame team slot 11. | `ram/sram.asm:132` |
| `sHallOfFame12` | `$B5A0-$B601` | 98 | Hall of Fame team slot 12. | `ram/sram.asm:132` |
| `sHallOfFame13` | `$B602-$B663` | 98 | Hall of Fame team slot 13. | `ram/sram.asm:132` |
| `sHallOfFame14` | `$B664-$B6C5` | 98 | Hall of Fame team slot 14. | `ram/sram.asm:132` |
| `sHallOfFame15` | `$B6C6-$B727` | 98 | Hall of Fame team slot 15. | `ram/sram.asm:132` |
| `sHallOfFame16` | `$B728-$B789` | 98 | Hall of Fame team slot 16. | `ram/sram.asm:132` |
| `sHallOfFame17` | `$B78A-$B7EB` | 98 | Hall of Fame team slot 17. | `ram/sram.asm:132` |
| `sHallOfFame18` | `$B7EC-$B84D` | 98 | Hall of Fame team slot 18. | `ram/sram.asm:132` |
| `sHallOfFame19` | `$B84E-$B8AF` | 98 | Hall of Fame team slot 19. | `ram/sram.asm:132` |
| `sHallOfFame20` | `$B8B0-$B911` | 98 | Hall of Fame team slot 20. | `ram/sram.asm:132` |
| `sHallOfFame21` | `$B912-$B973` | 98 | Hall of Fame team slot 21. | `ram/sram.asm:132` |
| `sHallOfFame22` | `$B974-$B9D5` | 98 | Hall of Fame team slot 22. | `ram/sram.asm:132` |
| `sHallOfFame23` | `$B9D6-$BA37` | 98 | Hall of Fame team slot 23. | `ram/sram.asm:132` |
| `sHallOfFame24` | `$BA38-$BA99` | 98 | Hall of Fame team slot 24. | `ram/sram.asm:132` |
| `sHallOfFame25` | `$BA9A-$BAFB` | 98 | Hall of Fame team slot 25. | `ram/sram.asm:132` |
| `sHallOfFame26` | `$BAFC-$BB5D` | 98 | Hall of Fame team slot 26. | `ram/sram.asm:132` |
| `sHallOfFame27` | `$BB5E-$BBBF` | 98 | Hall of Fame team slot 27. | `ram/sram.asm:132` |
| `sHallOfFame28` | `$BBC0-$BC21` | 98 | Hall of Fame team slot 28. | `ram/sram.asm:132` |
| `sHallOfFame29` | `$BC22-$BC83` | 98 | Hall of Fame team slot 29. | `ram/sram.asm:132` |
| `sHallOfFame30` | `$BC84-$BCE5` | 98 | Hall of Fame team slot 30. | `ram/sram.asm:132` |
| `sHallOfFameEnd` | `$BCE6` | alias | End marker for Hall Of Fame. | `ram/sram.asm:134` |

### Backup Save 2 (SRAM bank 1)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sBackupPlayerData2` | `$BCE6-$BE8F` | 426 | Buffer/data field for backup player data2. | `ram/sram.asm:139` |

### Boxes 1-7 (SRAM bank 2)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sBox1` | `$A000-$A44F` | 1104 | Stored PC box 1 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox2` | `$A450-$A89F` | 1104 | Stored PC box 2 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox3` | `$A8A0-$ACEF` | 1104 | Stored PC box 3 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox4` | `$ACF0-$B13F` | 1104 | Stored PC box 4 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox5` | `$B140-$B58F` | 1104 | Stored PC box 5 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox6` | `$B590-$B9DF` | 1104 | Stored PC box 6 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox7` | `$B9E0-$BE2F` | 1104 | Stored PC box 7 (1104 bytes including padding). | `ram/sram.asm:148` |

### Boxes 8-14 (SRAM bank 3)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sBox8` | `$A000-$A44F` | 1104 | Stored PC box 8 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox9` | `$A450-$A89F` | 1104 | Stored PC box 9 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox10` | `$A8A0-$ACEF` | 1104 | Stored PC box 10 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox11` | `$ACF0-$B13F` | 1104 | Stored PC box 11 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox12` | `$B140-$B58F` | 1104 | Stored PC box 12 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox13` | `$B590-$B9DF` | 1104 | Stored PC box 13 (1104 bytes including padding). | `ram/sram.asm:148` |
| `sBox14` | `$B9E0-$BE2F` | 1104 | Stored PC box 14 (1104 bytes including padding). | `ram/sram.asm:148` |

### Backup Save 3 (SRAM bank 3)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `sBackupOptions` | `$A000-$A007` | 8 | Stores backup options. | `ram/sram.asm:169` |
| `sBackupCheckValue1` | `$A008` | 1 | loaded with SAVE_CHECK_VALUE_1, used to check save corruption. | `ram/sram.asm:170` |
| `sBackupCurMapData` | `$A009-$A03C` | 52 | Buffer/data field for backup cur map data. | `ram/sram.asm:171` |
| `sBackupChecksum` | `$A03D-$A03E` | 2 | Stores backup checksum. | `ram/sram.asm:172` |
| `sBackupCheckValue2` | `$A03F` | 1 | loaded with SAVE_CHECK_VALUE_2, used to check save corruption. | `ram/sram.asm:173` |

## Detailed VRAM tables

### VRAM0 (VRAM bank 0)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `vTiles0` | `$8000-$87FF` | 2048 | VRAM bank 0 tile data block 0 ($8000-$87FF). | `ram/vram.asm:3` |
| `vTiles1` | `$8800-$8FFF` | 2048 | VRAM bank 0 tile data block 1 ($8800-$8FFF). | `ram/vram.asm:4` |
| `vTiles2` | `$9000-$97FF` | 2048 | VRAM bank 0 tile data block 2 ($9000-$97FF). | `ram/vram.asm:5` |
| `vBGMap0` | `$9800-$9BFF` | 1024 | VRAM bank 0 BG map 0 ($9800-$9BFF). | `ram/vram.asm:6` |
| `vBGMap1` | `$9C00-$9FFF` | 1024 | VRAM bank 0 BG map 1 ($9C00-$9FFF). | `ram/vram.asm:7` |

### VRAM1 (VRAM bank 1)

| Label | Address/range | Size | Purpose | Source |
| --- | --- | ---: | --- | --- |
| `vTiles3` | `$8000-$87FF` | 2048 | VRAM bank 1 tile data block 0 ($8000-$87FF). | `ram/vram.asm:12` |
| `vTiles4` | `$8800-$8FFF` | 2048 | VRAM bank 1 tile data block 1 ($8800-$8FFF). | `ram/vram.asm:13` |
| `vTiles5` | `$9000-$97FF` | 2048 | VRAM bank 1 tile data block 2 ($9000-$97FF). | `ram/vram.asm:14` |
| `vBGMap2` | `$9800-$9BFF` | 1024 | VRAM bank 1 BG map 2 ($9800-$9BFF). | `ram/vram.asm:15` |
| `vBGMap3` | `$9C00-$9FFF` | 1024 | VRAM bank 1 BG map 3 ($9C00-$9FFF). | `ram/vram.asm:16` |
