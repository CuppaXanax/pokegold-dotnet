# Hazards and translation traps

This note is repo-grounded and intended for future Claude instances with zero chat context. When a point is not explicit in-tree, it is marked `UNCLEAR:` instead of guessed.

## Quick triage

| Topic | Difficulty | Why it is risky |
|---|---|---|
| HRAM OAM-DMA trampoline | CRITICAL | Real hardware blocks normal ROM/WRAM execution during OAM DMA. |
| VRAM / LCD timing / STAT tricks | CRITICAL | Code polls `rLY`/`rSTAT`, changes interrupt masks mid-frame, and performs per-scanline register writes. |
| Serial / printer timing | HARD | Link/printer logic mixes interrupts, frame delays, and device-specific handshakes. |
| RTC / MBC3 state | HARD | Requires latch semantics, HALT/CARRY handling, day overflow rules, and SRAM/RTC register banking. |
| Flag-coupled arithmetic/control flow | HARD | Many routines depend on carry/zero from non-compare instructions. |
| Stack / register tricks | MEDIUM | Several hot paths repurpose `sp` as a bulk-copy pointer or synthesize calls with `push` + `jp hl`. |
| BCD / carry-chain arithmetic | MEDIUM | `daa`, `adc`, `sbc`, `rla`, and `rr` encode decimal and multi-byte arithmetic in CPU flags. |
| WRAM/speed/illegal-opcode quirks | EASY-MEDIUM | Mostly absent, but some code appears to rely on default hardware state. |

## 1) HRAM trampolines

### 1.1 OAM DMA trampoline exists and is load-bearing
- `home/init.asm` banks to the routine and copies it during startup (`home/init.asm:86-89`).
- The actual code is defined in ROM, copied byte-for-byte into HRAM, then executed from HRAM as `hTransferShadowOAM` (`engine/gfx/load_push_oam.asm:1-27`).
- VBlank calls the HRAM copy, not the ROM copy (`home/vblank.asm:114-118,172,245,371`).

**Why it exists:** on Game Boy hardware, OAM DMA blocks the CPU from normal memory; only HRAM is safely accessible during the transfer. The tiny loop in HRAM writes `rDMA`, burns a fixed delay with `dec a / jr nz`, then returns (`engine/gfx/load_push_oam.asm:17-26`).

**C# difficulty:** **CRITICAL.** A plain memory copy is not enough if you want faithful behavior. You need an explicit DMA model or at least a scheduler rule saying “OAM copy happens atomically here, and normal CPU-side memory access is unavailable during that window.”

### 1.2 Other HRAM trampolines
I found **one** executable HRAM block: `LOAD "OAM DMA", HRAM` (`engine/gfx/load_push_oam.asm:16-27`). The normal HRAM file is data-only (`ram/hram.asm:1-175`).

**C# difficulty:** **EASY** if you model only this one special case. `UNCLEAR:` I did not find another executable HRAM trampoline in the inspected tree.

## 2) Self-modifying code

I did **not** find a confirmed case of code writing back into ROM-resident instructions. The closest thing is dynamic code **copying** into HRAM for OAM DMA (`engine/gfx/load_push_oam.asm:1-27`), not in-place opcode patching. Raw bank switching is implemented as hardware register writes, not ROM mutation (`home/header.asm:12-15`).

**Why it matters:** original GB cartridges cannot normally rewrite ROM, so true self-modifying code would be surprising here.

**C# difficulty:** **EASY** for the cases I could confirm: represent behavior, not writable code pages. `UNCLEAR:` I did not exhaustively prove global absence, but I found no confirmed ROM-patching pattern in the inspected scope.

## 3) Flag-register dependencies

### 3.1 RNG uses stale carry unless the caller/interrupted code cleared it first
Both the normal RNG and one VBlank handler do:
- read `rDIV`
- `adc b`
- later `sbc b`

without an explicit carry clear first (`home/random.asm:14-29`; `home/vblank.asm:67-82,338-348`).

**Why it exists:** on LR35902, `adc`/`sbc` consume the current carry flag. In `Random`, that carry comes from the caller; in VBlank, it comes from whatever instruction was interrupted.

**C# difficulty:** **HARD.** If you reimplement this as normal integer math without preserving the incoming carry semantics, you will change RNG streams.

### 3.2 Time math inverts carry with `ccf` instead of recomputing it
`FixTime` adds seconds/minutes/hours, subtracts the radix (`60` or `24`), then uses `ccf` to flip the borrow bit into the carry needed for the next `adc` (`home/time.asm:126-168`).

**Why it exists:** `sub 60` leaves carry in the opposite sense of “did we overflow to the next unit?”. `ccf` converts that into the carry the next stage expects.

**C# difficulty:** **MEDIUM.** The logic is straightforward once understood, but it is very easy to mistranslate if you write the routine from the comments instead of the flags.

### 3.3 Carry-return APIs are used as a scheduling protocol
`UpdateBGMapBuffer` and `UpdateCGBPals` return carry on success (`home/video.asm:1-72`; `home/palettes.asm:3-55`). `VBlank_Normal` immediately branches on that carry to decide whether there is time for more work this frame (`home/vblank.asm:96-109`).

**Why it exists:** carry is being used as a tiny out-of-band return channel for the frame budget.

**C# difficulty:** **MEDIUM.** Port the meaning, not the instruction sequence: these routines are effectively returning “I consumed the VBlank budget.”

## 4) Timing-dependent code

### 4.1 Full-screen BG map copies are hand-timed against `rLY` and `rSTAT`
`CopyTilemapAtOnce` waits until late scanlines, disables interrupts, then polls `rSTAT` before every 2-byte VRAM write (`home/tilemap.asm:55-137`). Save-menu and phone-ring variants use different `rLY` thresholds (`engine/menus/savemenu_copytilemapatonce.asm:6-84`; `engine/phone/phonering_copytilemapatonce.asm:9-87`).

**Why it exists:** VRAM writes are only safe in specific LCD modes. This code is explicitly scheduled around those windows.

**C# difficulty:** **CRITICAL.** A renderer that treats VRAM as always-writable RAM will miss the real synchronization constraints these routines assume.

### 4.2 Per-scanline LCD effects are done with STAT interrupts and LY override tables
The LCD interrupt reads `rLY`, indexes `wLYOverrides`, and writes a chosen LCD register through `hLCDCPointer` (`home/lcd.asm:3-23`; `ram/hram.asm:119-145`; `ram/wram.asm:629-642`). Cutscene and credits VBlank handlers explicitly switch interrupt masks to `IE_STAT`, request STAT, run sound, then restore `IE_DEFAULT` (`home/vblank.asm:175-210,287-306`).

**Why it exists:** this is how the game gets raster effects like per-line scroll changes.

**C# difficulty:** **CRITICAL.** You need at least a scanline-aware abstraction. A pure “draw once per frame from final state” renderer will not match this behavior.

### 4.3 VBlank is the main scheduler, not just a graphics interrupt
The main VBlank handler prioritizes BG-buffer updates, palette pushes, tile transfers, OAM DMA, joypad, and sound in a strict order, with comments explicitly saying there is only time for one of some jobs per VBlank (`home/vblank.asm:1-145`).

**Why it exists:** the ROM is using the hardware frame budget as its global heartbeat.

**C# difficulty:** **HARD.** A simple game-loop port can preserve gameplay, but not the original ordering guarantees unless you model them deliberately.

## 5) DMA quirks

The OAM DMA path is deliberately tiny and lives in HRAM because the CPU cannot keep executing from ordinary memory during DMA (`engine/gfx/load_push_oam.asm:13-27`). VBlank gates the call with `hOAMUpdate` so some paths can suppress sprite DMA when other work is in flight (`home/vblank.asm:114-118`; `ram/hram.asm:136-145`).

**Why it exists:** OAM DMA is a hardware transaction with side effects on memory accessibility and frame timing.

**C# difficulty:** **CRITICAL.** This is one of the few places where “just copy 160 bytes sometime this frame” is not a faithful replacement.

## 6) MBC3 ROM banking quirks

`Bankswitch` is just a raw write of `a` into both `hROMBank` and `rROMB` (`home/header.asm:12-15`). Far calls save the old bank, switch, jump, then restore (`home/farcall.asm:1-28`). Some hot paths bypass `rst Bankswitch` and write `hROMBank`/`rROMB` inline (`home/audio.asm:9-19,33-43`; `home/text.asm:668-690`; `home/battle.asm:157-177`).

**Why it exists:** bank switching is hardware, not a linker/runtime abstraction.

**Translation risk:** on MBC3, selecting ROM bank 0 for the switchable bank window maps bank 1, not bank 0. This code does **not** guard against that in `Bankswitch`; it relies on hardware semantics.

**C# difficulty:** **MEDIUM.** Centralize banked-ROM reads/writes and implement the MBC3 quirk once. `UNCLEAR:` I did not find a callsite that intentionally requests ROMX bank 0, but the low-level switch routine itself assumes hardware behavior.

## 7) RTC handling

### 7.1 RTC latch semantics are stateful and partially hidden in `OpenSRAM`/`CloseSRAM`
The canonical latch pulse is `0` then `1` (`home/time.asm:6-12`). But `OpenSRAM` only writes `1` to `rRTCLATCH`; `CloseSRAM` resets it to `0` for next time (`home/sram.asm:1-23`).

**Why it exists:** MBC3 RTC latching is edge-triggered. This code assumes “close leaves latch low, next open raises it.”

**C# difficulty:** **HARD.** If you flatten RTC reads into ordinary getters/setters, you may accidentally remove this hidden state machine.

### 7.2 Day overflow / HALT / CARRY handling is game-specific, not raw hardware state
`FixDays` normalizes the day count modulo 140 and records overflow status (`home/time.asm:61-120`). `_FixDays` and `_GetClock` treat RTC HALT/CARRY bits as exceptional conditions, set status flags, and possibly force a reset flow (`engine/rtc/rtc.asm:103-137`). `ClockContinue` checks those saved status flags and may clear daily timers (`engine/rtc/rtc.asm:139-162,263-281`). `RestartClock` is the user-visible recovery path after RTC overflow (`engine/rtc/restart_clock.asm:38-108`).

**Why it exists:** the game is not exposing raw MBC3 RTC state; it is layering gameplay rules and recovery UX on top.

**C# difficulty:** **HARD.** You need a persistent RTC model plus the game’s normalization/status rules.

### 7.3 Setting the RTC preserves and clears specific bits manually
`SetClock` explicitly reads `RTC_DH`, preserves the HALT bit block it comments as “totally pointless,” then clears HALT before writing the final day-high byte (`home/time.asm:205-250`).

**Why it exists:** this is a very hardware-shaped write protocol, not a normal date-time assignment.

**C# difficulty:** **MEDIUM.** The behavior is implementable, but only if you treat RTC registers as registers, not just as a `DateTime`.

## 8) PC-relative / control-flow tricks

I did not find true “compute target from current PC” arithmetic, but I did find a recurring synthetic-call idiom:
- load target into `hl`
- push a local return label
- `jp hl`

Examples: `home/vblank.asm:18-31`, `engine/gfx/cgb_layouts.asm:18-29`, `engine/pokegear/pokegear.asm:240-255`, `engine/items/item_effects.asm:262-276`, `engine/games/slot_machine.asm:1388-1405,1484-1498,1877-1886`.

**Why it exists:** it gives “call through jump table” behavior without using a real `call hl` instruction (which the CPU does not have).

**C# difficulty:** **MEDIUM.** The C# equivalent is just a function dispatch, but it is easy to misread this as weird stack corruption if you do not recognize the pattern.

## 9) Stack manipulation

### 9.1 `sp` is repointed at non-stack memory for bulk copy
The BG-map copy code stores the real stack in `hSPBuffer`, sets `sp = hl`, then uses repeated `pop de` as a very fast stream load while writing VRAM (`home/tilemap.asm:13-17,98-137`; `engine/menus/savemenu_copytilemapatonce.asm:45-84`; `engine/phone/phonering_copytilemapatonce.asm:48-87`). The 1bpp/2bpp request handlers do the same trick for tile data (`home/video.asm:249-309,318-370`).

**Why it exists:** `pop` is a cheap 16-bit memory fetch on this CPU, and these are hot copy loops.

**C# difficulty:** **MEDIUM.** Easy to replace with array copies, but you must recognize that the original code is not using the stack conventionally.

### 9.2 Far-call code abuses the stack to preserve flags
`FarCall_hl` saves the old bank, calls through `hl`, then intentionally pops into `bc` instead of `af` so the callee’s flags survive. It spills `bc` through WRAM to put the registers back later (`home/farcall.asm:9-28`).

**Why it exists:** the CPU only has one flags register, so preserving `f` across a banked call is awkward.

**C# difficulty:** **MEDIUM.** You do not need a literal stack trick in C#, but you do need to preserve the semantic contract if other translated code depends on returned flags/results.

## 10) BCD arithmetic

Packed BCD is still a real runtime format here. `PrintBCDNumber` assumes each byte contains two decimal digits and prints high/low nibbles directly (`home/print_bcd.asm:1-80`). Actual BCD adjustment uses `daa` in at least two places: `PlaceBCDNumberSprite` (`home/audio.asm:443-469`) and slot-machine debug code (`engine/games/slot_machine.asm:238-252`).

**Why it exists:** the LR35902 has dedicated decimal-adjust support, and old Pokémon code uses BCD for money/counters/UI.

**C# difficulty:** **MEDIUM.** C# has no `daa`; you must re-express decimal adjust rules explicitly and keep packed-BCD storage where the original code expects it.

## 11) Carry-flag arithmetic

The arithmetic helpers are full of carry-chained multi-byte math:
- multiply uses `adc` and `rla` across a 4-byte intermediate (`engine/math/math.asm:13-79`)
- divide uses `sbc`, `rla`, `srl`, and `rr` across a shifting dividend/divisor state (`engine/math/math.asm:82-189`)
- experience math uses chained `sbc`/`adc` across 3-byte values (`engine/pokemon/experience.asm:83-152`)
- timer code subtracts seconds, then uses the borrowed carry in `sbc` for minutes (`engine/overworld/time.asm:118-134`)

**Why it exists:** this CPU is 8-bit; large arithmetic is built out of carry propagation.

**C# difficulty:** **MEDIUM.** Translate these as explicit multi-byte helpers, not as ad hoc rewrites.

## 12) Register / memory-layout tricks

A particularly assembly-shaped example is the multiplier scratch layout: `_Multiply` uses `hMultiplicand - 1` as an extra high byte/carry spill slot, relying on contiguous HRAM layout (`engine/math/math.asm:6-11,37-41,61-63`).

There are also many routines that treat one register pair as both pointer and state carrier; for example the scanline code uses `hLCDCPointer` as an indirect selector for which LCD register to write (`home/lcd.asm:5-18`), and jump-table dispatch routinely reuses `hl` first as table pointer and then as code pointer (`home/vblank.asm:18-31`; `engine/gfx/cgb_layouts.asm:18-29`).

**C# difficulty:** **MEDIUM.** Usually these become obvious fields/locals once decoded, but they are easy to mistranslate if you read them literally.

## 13) Interrupt timing / interrupt-sensitive code

Interrupt control is part of game logic, not just safety boilerplate:
- startup does global `di`, manually clears `rIF`/`rIE`, then reenables later (`home/init.asm:28-49,142-146`)
- whole-screen tilemap copies bracket direct VRAM access with `di`/`ei` (`home/tilemap.asm:71-90`; `engine/menus/savemenu_copytilemapatonce.asm:18-42`; `engine/phone/phonering_copytilemapatonce.asm:21-45`)
- cutscene/credits VBlank temporarily replace the normal interrupt mask with `IE_STAT`, deliberately request STAT, run sound, then restore defaults (`home/vblank.asm:186-210,287-306,383-406`)
- printer flows save/replace `rIE`, clear `rIF`, switch VBlank mode to `VBLANK_SERIAL`, and restore everything afterward (`engine/printer/printer.asm:55-75,139-150,220-230`) 

**Why it exists:** many routines need exclusive access to VRAM/OAM timing or need a different interrupt topology for serial/LCD effects.

**C# difficulty:** **HARD.** If your port has no explicit interrupt model, you still need equivalent critical-section rules and ordering constraints.

## 14) WRAM bank switching

The project has many `WRAMX` allocations (`ram/wram.asm:1820-1836,2308-2320,2667-2680,2812-2818`), and one tileset-animation comment explicitly says the routine is “Called in WRAM bank 1” (`engine/tilesets/tileset_anims.asm:11-16`). I found the hardware register definition for `SVBK/WBK` (`constants/hardware.inc:686-690,1108`), but I did **not** find an actual runtime `rSVBK` write in the inspected scope.

**Interpretation:** the code appears to assume the default switched WRAM bank is already bank 1, rather than dynamically changing banks.

**C# difficulty:** **MEDIUM.** If you flatten WRAM in C#, this is easy. If you emulate hardware memory banking, preserve the default-bank assumption. `UNCLEAR:` I did not find a proof that bank switching is truly unused everywhere, only that I did not see a write in the inspected files.

## 15) Speed switching

The hardware constants exist (`constants/hardware.inc:574-582,1094`; `macros/legacy.asm:501-502`), but I found no `stop` instruction and no actual KEY1/speed-switch logic in the inspected ASM.

**C# difficulty:** **EASY** if this remains absent. `UNCLEAR:` another part of the tree could still hide it, but I found no active use.

## 16) Undocumented opcodes

I found **no confirmed use** of undocumented LR35902 opcodes. Searches for common illegal-opcode byte values only turned up raw data tables, e.g. palette/offset data in battle animation assets (`engine/battle_anims/bg_effects.asm:2067-2147`; `engine/battle_anims/functions.asm:2586-2587`).

**C# difficulty:** **EASY** unless a later pass finds a real executed case.

## 17) Boundary conditions and boot-state assumptions

### 17.1 Startup depends on boot ROM register state
`_Start` distinguishes CGB vs non-CGB by comparing the incoming `a` register against `BOOTUP_A_CGB`, then stores the result in `hCGB` (`home/init.asm:16-26`).

**Why it exists:** the official boot ROM leaves recognizable register values for the cartridge code.

**C# difficulty:** **MEDIUM.** A native reimplementation should initialize equivalent state explicitly instead of assuming an external boot ROM already did it.

### 17.2 RTC latch-close/open ordering is a hidden boundary condition
Because `OpenSRAM` only writes latch=`1` and `CloseSRAM` resets latch=`0`, correct RTC behavior depends on always leaving the latch low when closing (`home/sram.asm:1-23`; `home/time.asm:6-12`).

**C# difficulty:** **MEDIUM.** This is easy to miss because the dependency is split across helper routines.

## Serial / printer timing note (worth calling out separately)

Link transfer code is deeply timing-shaped:
- connection establishment spins on serial bytes, `rDIV`, and exact short busy loops (`home/serial.asm:13-64`)
- byte exchange uses both serial interrupts and frame-based timeouts, and even branches on the current `rIE` mask (`home/serial.asm:122-229`)
- `WaitLinkTransfer` has VC-only patched delay counts (`home/serial.asm:284-337`)
- printer send/receive is a mini interrupt-driven protocol machine keyed by `wPrinterOpcode` and direct `rSB`/`rSC` traffic (`engine/printer/printer_serial.asm:158-181,261-277,445-619`)

**Difficulty:** **HARD.** If the C# target is not cycle-accurate, this probably needs a higher-level protocol simulation rather than a literal instruction port.

## Bottom line

If you want a faithful C# port, the biggest blockers are:
1. **OAM DMA + HRAM execution**
2. **VRAM/LCD/STAT timing and scanline effects**
3. **RTC/MBC3 state semantics**
4. **Serial/printer timing**
5. **CPU-flag-dependent arithmetic/control flow**

Everything else is manageable once those architectural decisions are made.