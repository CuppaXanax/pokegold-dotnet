# Translation patterns for pokegold -> C#

This document synthesizes `docs\recon\build-system.md`, `source-map.md`, `memory-map.md`, `data-formats.md`, `execution-flow.md`, `hazards.md`, `glitches.md`, and `decomp-conventions.md`, then grounds each rule in real ASM from the repo.

Target assumption: preserve retail `pokegold.gbc` behavior, including glitches and timing-shaped side effects where they are player-visible or script-visible.

## Translation defaults

- Preserve **bytes first, objects second**. Keep WRAM/SRAM/HRAM/VRAM-backed state as byte-addressable storage, then layer typed accessors on top.
- Preserve **bank identity** anywhere the ASM preserves bank identity.
- Preserve **carry/zero semantics** anywhere control flow or RNG depends on them.
- Preserve **bytecode/text streams as bytes**. Decode on execution/render, not at load time.
- Prefer `byte`, `ushort`, `Span<byte>`, `ReadOnlySpan<byte>`, and explicit helper methods over widened `int` math.

**CONTENTIOUS:** for hot arithmetic/control-flow paths, decide early whether the project will keep a tiny LR35902-style flag model (`Zero`, `Carry`, `HalfCarry`, `Subtract`) or hand-port flags per routine. The recon docs strongly favor a small shared flag model because stale carry and `daa` are load-bearing.

---

## Control flow patterns

### `farcall` / `callfar`

**ASM idiom**
- Load `A = BANK(target)` and `HL = target`, `rst FarCall`.
- Runtime helper saves current bank, switches, calls through `HL`, restores bank, and preserves flags.

**Proposed C# idiom**
```csharp
public T FarCall<T>(byte bank, Func<GoldState, T> callee)
{
    var previousBank = CurrentRomBank;
    SwitchRomBank(bank);
    try { return callee(this); }
    finally { SwitchRomBank(previousBank); }
}
```

**Rationale**
- Centralizes bank save/switch/restore and keeps bank-visible behavior explicit.
- Do not inline raw bank writes everywhere in translated code.
- If a callee communicates via flags/boolean result, that contract must survive the wrapper.

**Real example**
- Macro form: `macros\farcall.asm:7-27`
```asm
MACRO farcall
	ld a, BANK(\1)
	ld hl, \1
	rst FarCall
ENDM
```
- Runtime form: `home\farcall.asm:1-31`
```asm
ld [wTempBank], a
ldh a, [hROMBank]
push af
ld a, [wTempBank]
rst Bankswitch
call FarCall_JumpToHL
...
pop bc
ld a, b
rst Bankswitch
ret
```
- Callsite: `home\init.asm:116-123`
```asm
callfar InitCGBPals
...
farcall StartClock
```
- C# shape at a callsite:
```csharp
FarCall(BankOf(InitCgbPals), s => { InitCgbPals(s); return 0; });
FarCall(BankOf(StartClock), s => { StartClock(s); return 0; });
```

### `predef` / `predef_jump`

**ASM idiom**
- `predef` does not call a symbol directly.
- It converts a symbolic name into a `PredefPointers` index, resolves `(address, bank)`, then dispatches indirectly.

**Proposed C# idiom**
```csharp
public readonly record struct PredefEntry(byte Bank, Action<GoldState> Handler);

private static readonly PredefEntry[] Predefs = [ /* table order matters */ ];

public void RunPredef(byte id) => FarCall(Predefs[id].Bank, s => { Predefs[id].Handler(s); return 0; });
```

**Rationale**
- `predef` is an indexed ABI. Preserve the table and numeric IDs.
- Translating every callsite to a direct method call erases a real dispatch layer used throughout the ROM.

**Real example**
- Macros: `macros\predef.asm:3-17`
```asm
ld a, (\1Predef - PredefPointers) / 3
call Predef
```
- Runtime helper: `home\predef.asm:1-52`
```asm
ld [wPredefID], a
ldh a, [hROMBank]
push af
...
ld hl, .Return
push hl
...
push hl
...
ret
```
- Table: `data\predef_pointers.asm:9-33`
```asm
PredefPointers::
	add_predef LearnMove
	...
	add_predef StartBattle
```
- Callsite: `engine\overworld\scripting.asm:1065-1071`
```asm
Script_startbattle:
	call BufferScreen
	predef StartBattle
```
- Tail-jump callsite: `home\tilemap.asm:187-201`
```asm
.sgb
	predef_jump LoadSGBLayout
```

### RST vectors

**ASM idiom**
- `rst $00/$08/$10/$28` are real entry helpers in this repo.
- `rst $18/$20/$38` are trap/self-loop entries.

**Proposed C# idiom**
```csharp
private void ResetVector() => Start();
private void FarCallVector() => FarCallHl();
private void BankSwitchVector(byte bank) => SwitchRomBank(bank);
private void JumpTableVector(byte index, ReadOnlySpan<Action<GoldState>> table) => table[index](this);
```

**Rationale**
- Preserve the semantic roles of the vectors, not their raw addresses.
- Trap vectors should stay obviously invalid/unreachable in normal gameplay code.

**Real example**
- `home\header.asm:3-38`
```asm
SECTION "rst0", ROM0[$0000]
	di
	jp Start

SECTION "rst8", ROM0[$0008]
FarCall::
	jp FarCall_hl

SECTION "rst10", ROM0[$0010]
Bankswitch::
	ldh [hROMBank], a
	ld [rROMB], a
	ret
```

**UNCLEAR:** `home\header.asm:23-35` physically occupies `$0028-$0035`, so this repo has no distinct `rst $30` helper. Do not invent one in C# unless later evidence requires it.

### Jump tables (`dw Label` + `jp hl`)

**ASM idiom**
- Load an index into `A`.
- Point `HL` at a word table.
- `rst JumpTable` fetches the target word and `jp hl`.

**Proposed C# idiom**
```csharp
private static readonly Action<GoldState>[] ItemEffects =
[
    PokeBallEffect,
    PokeBallEffect,
    NoEffect,
    // ...
];

ItemEffects[curItem - 1](this);
```

**Rationale**
- Indexed arrays preserve opcode/item ordering and are easy to length-check.
- Prefer arrays over dictionaries; the numeric index is part of the ROM ABI.

**Real example**
- Generic helper: `home\header.asm:24-35`
```asm
JumpTable::
	push de
	ld e, a
	ld d, 0
	add hl, de
	add hl, de
	ld a, [hli]
	ld h, [hl]
	ld l, a
	pop de
	jp hl
```
- Real table: `engine\items\item_effects.asm:1-18`
```asm
_DoItemEffect::
	...
	ld hl, ItemEffects
	rst JumpTable
	ret

ItemEffects:
	table_width 2
	dw PokeBallEffect
	dw PokeBallEffect
```

### Synthetic calls (`push return_label` + `jp hl`)

**ASM idiom**
- Build an indirect call manually because LR35902 has no `call hl`.
- Push the return label, then `jp hl`.

**Proposed C# idiom**
```csharp
var handler = VBlankHandlers[hVBlank & VBlankMask];
handler(this);
GameTimer();
```

**Rationale**
- This is indirect dispatch, not stack corruption.
- In C#, a direct delegate/method call is the correct translation unless the routine intentionally exposes stack state.

**Real example**
- `home\vblank.asm:15-32`
```asm
ldh a, [hVBlank]
...
ld hl, VBlankHandlers
...
ld de, .return
push de
jp hl
.return:
	call GameTimer
```
- Another example: `engine\items\item_effects.asm:262-269`
```asm
ld a, [hli]
ld h, [hl]
ld l, a
ld de, .skip_or_return_from_ball_fn
push de
jp hl
```

### Conditional returns (`ret z`, `ret c`, `ret nc`)

**ASM idiom**
- `ret z` is a tiny early return.
- `ret c` / `ret nc` often mean “return true/false” or “stop/continue” on a carry protocol.

**Proposed C# idiom**
```csharp
if (frames == 0)
    return;

if (TryGetSpecialMapMusic(out var music))
    return music;

if (command >= EndTurnCommand)
    return;
```

**Rationale**
- Translate the contract, not the mnemonic.
- When carry is a boolean channel, use a named `bool` or small enum instead of burying the meaning.

**Real example**
- `ret z`: `home\audio.asm:272-277`
```asm
SkipMusic::
.loop
	and a
	ret z
	dec a
```
- `ret c`: `home\audio.asm:437-441`
```asm
GetMapMusic_MaybeSpecial::
	call SpecialMapMusic
	ret c
	call GetMapMusic
```
- `ret nc`: `engine\battle\effect_commands.asm:97-99`
```asm
cp endturn_command
ret nc
```

---

## Memory access patterns

### HRAM reads/writes (`ldh`)

**ASM idiom**
- `ldh [hFoo], a`, `ldh a, [hFoo]` for HRAM.
- Also used for high I/O registers like `rSB`, `rSC`, `rDIV`.

**Proposed C# idiom**
```csharp
public ref byte HRandomAdd => ref HRam[(int)HramOffset.RandomAdd];

HRandomAdd = CpuMath.Add8(HRandomAdd, Io.Div, carryIn: Flags.Carry).Value;
```

**Rationale**
- HRAM is real byte-addressable memory with aliasing and timing significance.
- Use typed `ref byte` accessors backed by one `Span<byte>`; do not replace HRAM with unrelated auto-properties.

**Real example**
- `home\random.asm:16-26`
```asm
ldh a, [rDIV]
ld b, a
ldh a, [hRandomAdd]
adc b
ldh [hRandomAdd], a
```
- `home\serial.asm:13-30`
```asm
ldh a, [hSerialConnectionStatus]
...
ldh a, [rSB]
ldh [hSerialReceive], a
...
ldh [rSC], a
```

### WRAM globals (`wFoo`)

**ASM idiom**
- Plain `ld [wFoo], a`, `ld a, [wFoo]`, `inc [hl]` on WRAM-backed globals.

**Proposed C# idiom**
```csharp
public ref byte WScriptMode => ref WRam1[(int)Wram1Offset.ScriptMode];

WScriptMode = (byte)ScriptMode.Read;
```

**Rationale**
- Preserve byte size, aliasing, and exact storage layout.
- High-level wrappers are fine, but they should be thin veneers over WRAM-backed bytes.

**Real example**
- `engine\overworld\scripting.asm:13-17,39-55`
```asm
ld a, [wScriptMode]
ld hl, .modes
rst JumpTable
...
ld a, SCRIPT_READ
ld [wScriptMode], a
```
- `engine\pokemon\health.asm:31-50`
```asm
ld hl, MON_STATUS
add hl, de
xor a
ld [hli], a
ld [hl], a
...
ld [bc], a
```

### SRAM access (`OpenSRAM` / `CloseSRAM`)

**ASM idiom**
- Open with bank in `A`, enable SRAM/RTC, perform reads/writes, then close.
- `OpenSRAM` / `CloseSRAM` also participate in the RTC latch protocol.

**Proposed C# idiom**
```csharp
using var sram = OpenSram(BankOfMailboxCount);
var mailbox = sram.Bytes;
mailbox[offset]--;
```

**Rationale**
- Scope-based open/close makes the banking/latch protocol hard to forget.
- Do not flatten SRAM/RTC into ordinary always-open objects.

**Real example**
- Helper: `home\sram.asm:1-23`
```asm
OpenSRAM::
	push af
	ld a, 1
	ld [rRTCLATCH], a
	ld a, RAMG_SRAM_ENABLE
	ld [rRAMG], a
	pop af
	ld [rRAMB], a
```
- Caller: `engine\pokemon\mail.asm:84-112`
```asm
ld a, BANK(sMailboxCount)
call OpenSRAM
...
ld hl, sPartyMail
call AddNTimes
...
call CopyBytes
...
call CloseSRAM
```

### VRAM writes (timing-constrained)

**ASM idiom**
- Wait for a safe LCD window.
- Often `di`, poll `rLY`/`rSTAT`, then write VRAM in tightly controlled bursts.

**Proposed C# idiom**
```csharp
public bool TryCopyTilemapAtOnce()
{
    if (!Video.WaitUntilLyAtLeast(0x7f))
        return false;

    using var gate = Interrupts.Suspend();
    Video.CopyBgMapFromBuffers();
    return true;
}
```

**Rationale**
- VRAM is not ordinary RAM in the original program.
- Route VRAM writes through a scheduler/video service that knows LCD mode and frame budget.

**Real example**
- `home\tilemap.asm:71-137`
```asm
.wait
	ldh a, [rLY]
	cp $80 - 1
	jr c, .wait
	di
	...
.loop
	pop de
.loop@
	ldh a, [c]
	and b
	jr nz, .loop@
	ld [hl], e
	inc l
	ld [hl], d
```

### Hardware register I/O (`rLCDC`, `rSTAT`, `rLY`, `rSB`, ...)

**ASM idiom**
- Direct reads/writes to memory-mapped hardware registers.
- Reads and writes often have side effects or timing assumptions.

**Proposed C# idiom**
```csharp
var ly = Io.Read(IoReg.LY);
var lcdc = Io.Read(IoReg.LCDC);
Io.Write(IoReg.LCDC, (byte)(lcdc & ~LCDC_ON));
Io.Write(IoReg.SC, (byte)(SC_START | SC_EXTERNAL));
```

**Rationale**
- Put side effects in an I/O-register facade, not scattered plain fields.
- This is the right place to model timer/LCD/serial behavior.

**Real example**
- `home\lcd.asm:29-50`
```asm
ldh a, [rLCDC]
bit B_LCDC_ENABLE, a
...
ldh a, [rLY]
cp LY_VBLANK + 1
...
ldh [rLCDC], a
```
- `home\serial.asm:17-30`
```asm
ldh a, [rSB]
ldh [hSerialReceive], a
ldh a, [hSerialSend]
ldh [rSB], a
ld a, SC_START | SC_EXTERNAL
ldh [rSC], a
```

### Banked ROM data access

**ASM idiom**
- Save current ROM bank, switch to the target bank, read bytes/words, restore bank.
- Common helpers: `GetFarByte`, `GetFarWord`, `GetScriptByte`, `TX_FAR` text reads.

**Proposed C# idiom**
```csharp
public byte ReadFarByte(byte bank, ushort address)
{
    var previousBank = CurrentRomBank;
    SwitchRomBank(bank);
    try { return ReadRomByte(address); }
    finally { SwitchRomBank(previousBank); }
}
```

**Rationale**
- Centralizes MBC3 bank behavior.
- Keeps bank-sensitive reads explicit, which matters for debugging and parity work.

**Real example**
- Generic helpers: `home\copy.asm:17-55`
```asm
GetFarByte::
	ld [wTempBank], a
	ldh a, [hROMBank]
	push af
	ld a, [wTempBank]
	rst Bankswitch
	ld a, [hl]
	...
	pop af
	rst Bankswitch
```
- Script fetch: `home\map.asm:1482-1510`
```asm
GetScriptByte::
	ldh a, [hROMBank]
	push af
	ld a, [wScriptBank]
	rst Bankswitch
	ld a, [bc]
	...
	pop af
	rst Bankswitch
```
- Far text: `home\text.asm:668-690`
```asm
TextCommand_FAR::
	ldh a, [hROMBank]
	push af
	...
	ld [rROMB], a
	call DoTextUntilTerminator
	...
	ld [rROMB], a
```

**CONTENTIOUS:** once all ROM is in managed memory, it is tempting to erase bank boundaries entirely. Keep a bank-aware API anyway; the original code treats bank identity as observable state.

---

## Arithmetic patterns

### Flag-register-dependent code

**ASM idiom**
- Arithmetic instructions set flags.
- Later control flow or later arithmetic consumes those flags without recomputing them.

**Proposed C# idiom**
```csharp
var sec = CpuMath.Add8(wStartSecond, hRtcSeconds, carryIn: false);
sec = CpuMath.Sub8(sec.Value, 60, carryIn: false);
if (!sec.Carry)
    sec = CpuMath.Add8(sec.Value, 60, carryIn: false);

var carryToMinutes = !sec.Carry; // ccf
var min = CpuMath.Add8(wStartMinute, hRtcMinutes, carryIn: carryToMinutes);
```

**Rationale**
- A tiny `Alu8Result` helper is safer than re-deriving carry from widened integers.
- This is exactly where mistranslations silently change gameplay or timers.

**Real example**
- `home\time.asm:122-168`
```asm
ld a, [wStartSecond]
add c
sub 60
jr nc, .updatesec
add 60
...
ccf
ld a, [wStartMinute]
adc c
```

### BCD arithmetic (`daa`)

**ASM idiom**
- Perform normal add/subtract, then `daa` to normalize packed BCD.

**Proposed C# idiom**
```csharp
var add = CpuMath.Add8(value, 1, carryIn: false);
var bcd = CpuMath.DecimalAdjustAfterAdd(add.Value, add.HalfCarry, add.Carry);
```

**Rationale**
- `daa` is not equivalent to parsing an integer, adding, and reformatting.
- Keep packed BCD as packed BCD where the original code does.

**Real example**
- `home\audio.asm:443-469`
```asm
ld a, [wUnusedBCDNumber]
cp 100
jr nc, .max
add 1
daa
ld b, a
swap a
and $f
```

### Multi-byte carry-chain math (`adc` / `sbc`)

**ASM idiom**
- Compute byte 0, then propagate carry/borrow through higher bytes with `adc` / `sbc`.

**Proposed C# idiom**
```csharp
var lo = CpuMath.Add8(lhs2, rhs2, carryIn: false);
var mid = CpuMath.Add8(lhs1, rhs1, carryIn: lo.Carry);
var hi = CpuMath.Add8(lhs0, rhs0, carryIn: mid.Carry);
```

**Rationale**
- Do not replace these routines with casual `int` math unless you have proven identical truncation and flag behavior.
- Carry chains are the native 8-bit arithmetic model of the codebase.

**Real example**
- `engine\math\math.asm:19-41`
```asm
ldh a, [hMathBuffer + 4]
ld c, a
ldh a, [hMultiplicand + 2]
add c
...
ldh a, [hMultiplicand + 1]
adc c
...
ldh a, [hMultiplicand - 1]
adc c
```
- `engine\math\math.asm:93-109`
```asm
ldh a, [hDividend + 1]
sub c
...
ldh a, [hDividend + 0]
sbc c
jr c, .next
```

### Multiplication / division (`engine\math\math.asm`)

**ASM idiom**
- Dedicated shift/add and shift/subtract engines over HRAM scratch variables.
- Uses overlapping scratch fields and flag-sensitive rotates.

**Proposed C# idiom**
```csharp
public static void Multiply(ref MathState s) { /* literal port of _Multiply */ }
public static void Divide(ref MathState s)   { /* literal port of _Divide   */ }
```

**Rationale**
- These are not mere convenience helpers; they encode the project's byte-level math semantics.
- Keep a dedicated `MathState` or direct HRAM-backed view rather than replacing them with `*` and `/`.

**Real example**
- `engine\math\math.asm:1-80` defines `_Multiply`.
- `engine\math\math.asm:82-189` defines `_Divide`.

### Bit rotation (`rl`, `rr`, `rla`, `rra`)

**ASM idiom**
- Rotate through carry or rotate within the byte.
- Often chained across multiple bytes.

**Proposed C# idiom**
```csharp
(value, carry) = CpuMath.RotateLeftThroughCarry(value, carry);
(value, carry) = CpuMath.RotateRightThroughCarry(value, carry);
```

**Rationale**
- Rotates are not interchangeable with `<<` / `>>`.
- Carry-in and carry-out are part of the observable result.

**Real example**
- `engine\math\math.asm:49-63`
```asm
ldh a, [hMultiplicand + 2]
add a
...
ldh a, [hMultiplicand + 1]
rla
...
ldh a, [hMultiplicand - 1]
rla
```
- `engine\math\math.asm:163-169`
```asm
ldh a, [hDivisor]
srl a
...
ldh a, [hMathBuffer + 0]
rr a
```

---

## Data patterns

### Lookup tables

**ASM idiom**
- Flat constant tables, often fixed width, indexed by a byte ID or stage value.

**Proposed C# idiom**
```csharp
public readonly record struct StatRatio(byte Numerator, byte Denominator);

private static ReadOnlySpan<StatRatio> StatMultipliers =>
[
    new(25, 100), new(28, 100), new(33, 100),
    new(40, 100), new(50, 100), new(66, 100),
    new(1, 1), new(15, 10), new(2, 1),
    new(25, 10), new(3, 1), new(35, 10), new(4, 1),
];
```

**Rationale**
- `ReadOnlySpan<T>` keeps data immutable and index-addressable.
- Preserve exact ordering and element width; the index is the protocol.

**Real example**
- `data\battle\stat_multipliers.asm:1-20`
```asm
db  25, 100 ; -6
db  28, 100 ; -5
...
db   1,   1 ;  0
db   4,   1 ; +6
```

### String/text encoding

**ASM idiom**
- Two layers: `TX_*` command stream and inline charmap bytes.
- `@` and `TX_END` are both `$50`, but they terminate different layers.
- `<DONE>` and `<PROMPT>` are distinct inline control bytes, not the same as `TX_END`.

**Proposed C# idiom**
```csharp
while (true)
{
    var b = FetchTextByte(ref pc);
    if (b == TX_END)
        break;
    ExecuteTextCommand(b, ref pc, ref renderer);
}
```

**Rationale**
- Keep text as raw bytes until execution/render.
- Do not normalize everything to `string`; control bytes and glitches depend on raw token identity.

**Real example**
- Inline tokens: `constants\charmap.asm:5-35`
```asm
charmap "@",        $50
charmap "<DONE>",   $57
charmap "<PROMPT>", $58
```
- Macros: `macros\scripts\text.asm:25-31,165-170`
```asm
MACRO done
	db "<DONE>"
ENDM
...
MACRO text_end
	db TX_END
ENDM
```
- Interpreter: `home\text.asm:590-691`
```asm
DoTextUntilTerminator::
	ld a, [hli]
	cp TX_END
	ret z
	call .TextCommand
	jr DoTextUntilTerminator
```

### Struct access (`rsreset` / `rb` / `rw` offsets)

**ASM idiom**
- Symbolic byte offsets describe packed structs.
- Runtime code adds offsets to base pointers and reads/writes big-endian words manually.

**Proposed C# idiom**
```csharp
public enum MonOffset : int
{
    Species = 0x00,
    Status  = 0x20,
    Hp      = 0x22,
    MaxHp   = 0x24,
}

public static ushort ReadBeWord(ReadOnlySpan<byte> s, MonOffset offset) =>
    BinaryPrimitives.ReadUInt16BigEndian(s[(int)offset..]);
```

**Rationale**
- Offset enums mirror the source exactly and keep layout stable.
- Avoid C# object graphs for these storage formats until parity is proven.

**Real example**
- Offset definitions: `constants\pokemon_data_constants.asm:75-107`
```asm
rsreset
DEF MON_SPECIES rb
...
DEF MON_HP      rw
DEF MON_MAXHP   rw
```
- Access pattern: `engine\pokemon\health.asm:25-53`
```asm
ld a, MON_SPECIES
call GetPartyParamLocation
...
ld hl, MON_MAXHP
add hl, de
...
ld [bc], a
```

### Bit flags (`_F`, `set`, `res`, `bit`)

**ASM idiom**
- `_F` constants are bit positions, not masks.
- Code uses `bit`, `set`, `res` against bytes in WRAM/HRAM.

**Proposed C# idiom**
```csharp
public static bool TestBit(byte value, int bit) => (value & (1 << bit)) != 0;
public static byte SetBit(byte value, int bit) => (byte)(value | (1 << bit));
public static byte ResetBit(byte value, int bit) => (byte)(value & ~(1 << bit));
```

**Rationale**
- Bit-position helpers preserve the source convention directly.
- `[Flags]` enums are fine for tooling/views, but the core runtime should still manipulate the underlying byte exactly.

**Real example**
- Bit definitions: `constants\ram_constants.asm:101-123`
```asm
DEF SCRIPTED_MOVEMENT_STATE_F EQU 7
...
const PLAYERSTEP_STOP_F ; 6
```
- Use sites: `engine\overworld\scripting.asm:47-49`, `home\map_objects.asm:415-417`
```asm
bit SCRIPTED_MOVEMENT_STATE_F, [hl]
...
set SCRIPTED_MOVEMENT_STATE_F, [hl]
```

### `const_def` / `const` enumerations

**ASM idiom**
- Auto-incremented numeric enums, sometimes with nonzero starts, negative starts, or custom increments.

**Proposed C# idiom**
```csharp
public enum ScriptMode : byte
{
    Off = 0,
    Read = 1,
    WaitMovement = 2,
    Wait = 3,
}

public enum WalkingDirection : sbyte
{
    Standing = -1,
    Down = 0,
    Up = 1,
    Left = 2,
    Right = 3,
}
```

**Rationale**
- Explicit enum underlying values preserve the ABI and holes.
- Use `sbyte` when the source uses negative enum values.

**Real example**
- `constants\ram_constants.asm:5-27,80-99,169-203`
```asm
const_def
const DEBUG_BATTLE_F
...
const_def -1
const STANDING
const DOWN
...
const_def
const SCRIPT_OFF
const SCRIPT_READ
```
- `macros\scripts\movement.asm:2-24`
```asm
const_def 0, 4
const movement_turn_head
...
const movement_step
```

---

## Script / bytecode patterns

### Event script opcodes

**ASM idiom**
- Event scripts are ROM bytecode.
- `GetScriptByte` fetches from `wScriptBank:wScriptPos`.
- `RunScriptCommand` dispatches through `ScriptCommandTable`.

**Proposed C# idiom**
```csharp
private delegate void ScriptHandler(ref ScriptContext ctx);
private static readonly ScriptHandler[] ScriptHandlers = [ /* opcode order */ ];

while (ctx.Mode == ScriptMode.Read)
{
    var opcode = ctx.GetScriptByte();
    ScriptHandlers[opcode](ref ctx);
}
```

**Rationale**
- Preserve explicit PC, bank, and script stack.
- Prefer handler arrays over giant semantic rewrites; opcode numbers matter.

**Real example**
- Opcode encoding: `macros\scripts\events.asm:1-40`
```asm
const scall_command ; $00
MACRO scall
	db scall_command
	dw \1
ENDM
```
- Interpreter: `engine\overworld\scripting.asm:10-25,58-67`
```asm
ScriptEvents::
	call StartScript
.loop
	ld a, [wScriptMode]
	ld hl, .modes
	rst JumpTable
	call CheckScript
	jr nz, .loop
...
RunScriptCommand:
	call GetScriptByte
	ld hl, ScriptCommandTable
	rst JumpTable
```
- Script PC fetch: `home\map.asm:1482-1510`
```asm
GetScriptByte::
	ld a, [wScriptBank]
	rst Bankswitch
	ld a, [bc]
	inc bc
```

### Battle command bytecode

**ASM idiom**
- `MoveEffectsPointers` maps `EFFECT_*` to a move script.
- `DoMove` copies that script into `wBattleScriptBuffer`.
- Then command bytes dispatch through `BattleCommandPointers` until `endmove` / `endturn`.

**Proposed C# idiom**
```csharp
LoadMoveScript(effectId, battle.ScriptBuffer);
while (true)
{
    var command = battle.ScriptBuffer[battle.ScriptPc++];
    if (command >= EndTurnCommand)
        return;
    BattleHandlers[command - 1](ref battle);
}
```

**Rationale**
- Preserve the working buffer because the ASM preserves it.
- Branch targets and script rewrites use the buffer address as mutable state.

**Real example**
- Command IDs: `macros\scripts\battle_commands.asm:7-188`
```asm
const_def 1
command checkturn ; 01
...
command moveanim  ; ab
...
command endmove   ; ff
command endturn   ; fe
```
- Script data: `data\moves\effects.asm:5-24`
```asm
NormalHit:
	checkobedience
	usedmovetext
	doturn
	critical
	...
	endmove
```
- Pointer table: `data\battle\effect_command_pointers.asm:5-80`
```asm
BattleCommandPointers:
	table_width 2
	dw BattleCommand_CheckTurn
	dw BattleCommand_CheckObedience
```
- Interpreter: `engine\battle\effect_commands.asm:51-119`
```asm
ld hl, MoveEffectsPointers
...
call GetFarWord
...
call GetFarByte
...
cp endturn_command
ret nc
...
ld hl, BattleCommandPointers
...
call GetFarWord
jp hl
```

### Movement scripts

**ASM idiom**
- Movement commands are bytes, not symbolic events at runtime.
- For many commands, low 2 bits encode direction and upper bits encode the command family.

**Proposed C# idiom**
```csharp
var raw = ctx.GetMovementByte();
var direction = raw & 0b11;
var family = raw & 0b1111_1100;
MovementHandlers[raw](ref ctx);
```

**Rationale**
- Preserve the raw encoded byte stream.
- Do not normalize map movement scripts into higher-level objects too early.

**Real example**
- Encoding DSL: `macros\scripts\movement.asm:1-24,111-145`
```asm
const_def 0, 4
const movement_step ; $0c
MACRO step
	db movement_step | \1
ENDM
...
MACRO step_end
	db movement_step_end
ENDM
```
- Real script data: `maps\NewBarkTown.asm:148-191`
```asm
NewBarkTown_TeacherRunsToYouMovement1:
	step LEFT
	step LEFT
	step LEFT
	step LEFT
	step_end
```
- Dispatch table: `engine\overworld\movement.asm:1-93`
```asm
MovementPointers:
	table_width 2
	dw Movement_turn_head_down
	...
	dw Movement_step_down
	...
	dw Movement_step_end
```
- Loader: `engine\overworld\scripting.asm:747-773`, `home\map_objects.asm:394-417`
```asm
Script_applymovement:
	...
	call GetMovementData
	...
	ld a, SCRIPT_WAIT_MOVEMENT
	ld [wScriptMode], a
```

### How bytecode interpreters should be structured in C#

**Recommended shape**
```csharp
public ref struct BytecodeVm<TState>
{
    public byte Bank;
    public ushort Pc;
    public Span<Frame> Stack;
    public TState State;
}
```

- Keep an explicit `(bank, pc)`.
- Keep an explicit stack when the ASM keeps one (`wScriptStack`, battle script buffer address, return labels).
- Use `static readonly` handler arrays keyed by opcode.
- Keep the byte stream raw; decode one opcode at a time.
- When the ASM copies into a working buffer first, keep that copy.

**Rationale**
- This matches event scripts, battle commands, text commands, and movement commands.
- It also keeps glitch behavior reachable instead of accidentally normalizing it away.

**CONTENTIOUS:** a giant `switch` can work, but handler arrays map more directly onto `table_width 2` + `dw` tables and make parity auditing easier.

---

## Special patterns

### OAM DMA trampoline

**ASM idiom**
- Copy a tiny routine from ROM to HRAM.
- Call the HRAM copy during VBlank to write `rDMA` and busy-wait.

**Proposed C# idiom**
```csharp
public void TransferShadowOam()
{
    Scheduler.BlockNonHramAccess(DmaCycles, () => Oam.CopyFrom(ShadowOam));
}
```

**Rationale**
- The important behavior is not “copy 160 bytes sometime.”
- The important behavior is “perform OAM DMA at this specific point, with DMA-side access restrictions.”

**Real example**
- Copier and HRAM routine: `engine\gfx\load_push_oam.asm:1-27`
```asm
WriteOAMDMACodeToHRAM::
	ld c, LOW(hTransferShadowOAM)
	...
LOAD "OAM DMA", HRAM
hTransferShadowOAM::
	ld a, HIGH(wShadowOAM)
	ldh [rDMA], a
	ld a, OAM_COUNT
.wait
	dec a
	jr nz, .wait
	ret
```
- Install at boot: `home\init.asm:86-89`
- Use in VBlank: `home\vblank.asm:114-118`

### Stack tricks (`sp` as copy pointer, `push` + `jp hl`)

**ASM idiom**
- Repoint `sp` at source data and `pop` from it for fast copies.
- Or synthesize an indirect call by pushing a return label then `jp hl`.

**Proposed C# idiom**
```csharp
CopyBgMapPairs(sourceSpan, destinationSpan); // for sp-as-copy-pointer loops
handler(this);                               // for push+jp hl synthetic calls
```

**Rationale**
- Preserve the dataflow/control-flow meaning, not the literal abuse of the hardware stack.
- Exception: when a bug depends on a real explicit script/return stack, keep an explicit managed stack structure.

**Real example**
- `sp` as source pointer: `home\tilemap.asm:98-137`
```asm
ld [hSPBuffer], sp
ld sp, hl
...
pop de
ld [hl], e
inc l
ld [hl], d
```
- Synthetic call: `home\vblank.asm:27-29`, `engine\items\item_effects.asm:262-269`
```asm
ld de, .return
push de
jp hl
```

### VBlank budget management (carry-return scheduling)

**ASM idiom**
- Helper returns carry when it successfully consumed the frame budget.
- Caller branches immediately on carry and skips lower-priority work.

**Proposed C# idiom**
```csharp
if (TryUpdateBgMapBuffer())
    return;
if (TryUpdateCgbPals())
    return;
UpdateBgMap();
```

**Rationale**
- Name the contract after what it means: frame-budget consumption.
- Do not hide this inside a generic `void` helper.

**Real example**
- Producers: `home\video.asm:1-72`, `home\palettes.asm:3-55`
```asm
; Return carry on success.
...
scf
ret
```
- Consumer: `home\vblank.asm:96-109`
```asm
call UpdateBGMapBuffer
jr c, .done
call UpdatePalsIfCGB
jr c, .done
call UpdateBGMap
```

### RNG with stale carry

**ASM idiom**
- `adc b` / `sbc b` without clearing carry first.
- Carry comes from the caller or from the interrupted instruction stream.

**Proposed C# idiom**
```csharp
var add = CpuMath.Add8(HRandomAdd, Io.Div, carryIn: Flags.Carry);
HRandomAdd = add.Value;
Flags.Carry = add.Carry;

var sub = CpuMath.Sub8(HRandomSub, Io.Div, carryIn: Flags.Carry);
HRandomSub = sub.Value;
Flags.Carry = sub.Carry;
```

**Rationale**
- `Random.Shared` is not even close.
- The incoming carry is part of the RNG state.

**Real example**
- `home\random.asm:14-29`
```asm
ldh a, [rDIV]
ld b, a
ldh a, [hRandomAdd]
adc b
ldh [hRandomAdd], a
...
ldh a, [hRandomSub]
sbc b
```
- Same update in VBlank: `home\vblank.asm:71-82`

### `di` / `ei` critical sections

**ASM idiom**
- Explicitly disable and re-enable interrupts around timing-sensitive code.

**Proposed C# idiom**
```csharp
using var gate = Interrupts.Suspend();
Video.CopyTilemapAtOnce();
```

**Rationale**
- This is an interrupt/scheduler gate, not a thread lock.
- It must interact with VBlank/STAT scheduling rules.

**Real example**
- `home\tilemap.asm:76-90`
```asm
di
...
ldh [rVBK], a
...
ei
```
- Startup also treats interrupt state as meaningful: `home\init.asm:97-109,142-146`

---

## Project-wide guidance distilled from these patterns

1. Keep **bank-aware helpers** (`FarCall`, `ReadFarByte`, script/text PCs).
2. Keep **memory-backed byte storage** for WRAM/SRAM/HRAM/VRAM and expose typed accessors over it.
3. Keep a tiny **flag-aware ALU helper layer** for stale carry, `daa`, rotates, and carry chains.
4. Keep **handler arrays** for jump tables and bytecode interpreters.
5. Keep **text and script data as bytes**, not pre-decoded objects.
6. Route **VRAM/OAM/I/O register access** through timing-aware services.
7. Treat **glitches as required behavior**, not bugs to clean up.

If a future translator is unsure whether to "simplify" an ASM pattern, the default answer for pokegold should be **no** unless the simplified C# preserves the same bytes, flags, bank state, and visible scheduling behavior.