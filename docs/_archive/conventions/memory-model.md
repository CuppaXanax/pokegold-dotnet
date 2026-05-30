# Memory model design

Goal: reimplement Gold's memory as raw, banked, byte-addressable storage with typed C# views layered on top. The source of truth should stay in bytes so save layout, bank switching, DMA timing, and memory-layout-dependent glitches remain observable. (docs/recon/memory-map.md:7-15,20-22,38-49,54-59,69-94; docs/recon/hazards.md:9-12,20-27,72-99,101-118; docs/recon/glitches.md:3-25,90-117,363-389)

`UNCLEAR:` the request names `engine/pokemon/stats.asm`, but this repo's matching file is `engine/pokemon/mon_stats.asm`. This doc uses `mon_stats.asm` plus `home/battle.asm` and `engine/pokemon/health.asm` for concrete party-struct access patterns. (engine/pokemon/mon_stats.asm:126-170; home/battle.asm:1-16; engine/pokemon/health.asm:25-53)

## 1. Overall memory architecture

Use **separate physical backing stores per hardware region**, plus one bus facade that maps 16-bit CPU addresses into those stores. Do **not** make one flat mutable `byte[65536]` the canonical model: Gold depends on banked ROM/SRAM/VRAM windows, HRAM-resident DMA code, and timing-sensitive I/O/VRAM behavior. A bus is still necessary, but it should be a view over explicit region storage, not the storage itself. (docs/recon/memory-map.md:7-15,54-59,69-94,1772-2103; docs/recon/build-system.md:215-223,352-360; docs/conventions/translation-patterns.md:350-365,387-402,429-450,511-570)

```csharp
public sealed class GoldMemory
{
    public byte[] Rom { get; } = new byte[0x80 * 0x4000];   // 128 x 16 KiB
    public byte[] Vram { get; } = new byte[2 * 0x2000];     // 2 x 8 KiB
    public byte[] Sram { get; } = new byte[4 * 0x2000];     // 4 x 8 KiB
    public byte[] Wram0 { get; } = new byte[0x1000];
    public byte[] WramX { get; } = new byte[8 * 0x1000];    // see CONTENTIOUS note
    public byte[] Oam { get; } = new byte[0xA0];

    public HramFile Hram { get; } = new();
    public IoRegisterFile Io { get; } = new();

    public byte CurrentRomBank;
    public byte CurrentVramBank;
    public byte CurrentSramBank;
    public byte CurrentWramBank;
    public bool SramEnabled;
}
```

Translated routines should use two access styles:

1. **Bus reads/writes** for generic code, DMA, and memory-mapped hardware behavior.
2. **Typed region views** (`ref byte`, `Span<byte>`, `ref struct`) for named WRAM/HRAM/SRAM symbols and packed structs.

That matches the already-decided translation style: thin typed accessors backed by one span/byte store, not detached C# object graphs. (docs/conventions/translation-patterns.md:314-329,350-365,821-844)

```csharp
public byte ReadByte(ushort address) => address switch
{
    < 0x4000 => Rom[address],
    < 0x8000 => Rom[MapRomX(CurrentRomBank, address)],
    < 0xA000 => Vram[(CurrentVramBank * 0x2000) + (address - 0x8000)],
    < 0xC000 => ReadSramWindow(address),
    < 0xD000 => Wram0[address - 0xC000],
    < 0xE000 => WramX[(CurrentWramBank * 0x1000) + (address - 0xD000)],
    < 0xFE00 => ReadEchoOrUnused(address),
    < 0xFEA0 => Oam[address - 0xFE00],
    < 0xFF00 => ReadUnusable(address),
    < 0xFF80 => Io.Read((byte)(address - 0xFF00)),
    < 0xFFFF => Hram.Read((byte)(address - 0xFF80)),
    _        => Io.ReadIe()
};
```

## 2. ROM access model

Represent ROM as a flat 2 MiB byte array, but keep **bank selection semantics** explicit. Bank 0 is fixed at `$0000-$3FFF`; `$4000-$7FFF` is a switchable ROMX window whose logical bank number is part of live machine state (`hROMBank`, `rROMB`). `GetFarByte`/`GetFarWord` save the old bank, switch, read, then restore. The low-level bank helper does not guard against MBC3's “bank 0 in ROMX means bank 1” rule; the hardware semantics are the contract. (docs/recon/build-system.md:215-223; docs/recon/memory-map.md:54-59; docs/recon/hazards.md:101-109; home/header.asm:11-15; home/copy.asm:17-55)

```csharp
private static int MapRomX(byte requestedBank, ushort address)
{
    byte effectiveBank = requestedBank == 0 ? (byte)1 : requestedBank; // MBC3 quirk
    return (effectiveBank * 0x4000) + (address - 0x4000);
}

public byte ReadFarByte(byte bank, ushort address)
{
    byte previous = CurrentRomBank;
    CurrentRomBank = bank;
    try { return ReadByte(address); }
    finally { CurrentRomBank = previous; }
}
```

Recommended rule: keep all far reads **bank-aware even though the ROM is flat in host memory**. That keeps parity work debuggable and matches existing translation guidance. (docs/conventions/translation-patterns.md:511-570)

**CONTENTIOUS:** it is technically possible to erase bank boundaries after loading the ROM, but Gold's code treats bank identity as observable runtime state (`hROMBank` saves/restores, farcall helpers, text/script fetch helpers). Preserve the banked API anyway. (docs/conventions/translation-patterns.md:528-570; docs/recon/execution-flow.md:313-327)

## 3. WRAM representation

Use WRAM as raw bytes plus typed views, not as a hierarchy of copied C# objects. Gold stores large contiguous blobs that are later copied byte-for-byte into SRAM (`wPokemonData`, `wCurMapData`, `wPlayerData*`), and many routines compute field addresses by `base + slot * struct_length + field_offset`. A span-backed view matches that shape directly. (ram/wram.asm:2669-2692; docs/recon/memory-map.md:20-22,24-27,38-43,1664-1689; home/battle.asm:1-16)

Recommended shape:

- `Wram0View` and `WramBank1View` expose named globals as offsets into `Wram0` / the current WRAMX bank.
- Packed structs (`party_struct`, `battle_struct`, `curbox`) become `ref struct` views over `Span<byte>` slices.
- Single-byte fields may return `ref byte`.
- Multi-byte fields should use explicit get/set helpers because endianness is mixed inside the same struct. (docs/conventions/translation-patterns.md:350-365,821-844; macros/ram.asm:7-42,76-124; constants/pokemon_data_constants.asm:75-123)

```csharp
public ref struct PartyMonView
{
    private Span<byte> _data;

    public PartyMonView(Span<byte> data) => _data = data;

    public ref byte Species => ref _data[0x00];
    public ref byte HeldItem => ref _data[0x01];
    public ref byte Status => ref _data[0x20];

    public ushort OriginalTrainerId
    {
        get => BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(0x06, 2));
        set => BinaryPrimitives.WriteUInt16LittleEndian(_data.Slice(0x06, 2), value);
    }

    public uint Experience
    {
        get => (uint)(_data[0x08] << 16 | _data[0x09] << 8 | _data[0x0A]);
        set
        {
            _data[0x08] = (byte)(value >> 16);
            _data[0x09] = (byte)(value >> 8);
            _data[0x0A] = (byte)value;
        }
    }

    public ushort CurrentHp
    {
        get => BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(0x22, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_data.Slice(0x22, 2), value);
    }

    public ushort MaxHp
    {
        get => BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(0x24, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_data.Slice(0x24, 2), value);
    }
}
```

**CONTENTIOUS:** avoid `[StructLayout(LayoutKind.Sequential, Pack = 1)]` overlays for mon/save structs. Gold's important structs mix little-endian words, big-endian words, 3-byte integers, filler bytes, and parallel arrays. A span-backed view is slightly more verbose but much safer for byte-accuracy and glitch preservation. (docs/recon/data-formats.md:55-124; macros/ram.asm:7-42,99-124)

## 4. HRAM representation

Keep HRAM as one contiguous 127-byte backing store with typed accessors. Gold uses HRAM both as ordinary fast scratch/state (`hRandomAdd`, joypad mirrors, `hLCDCPointer`, math scratch) and as the only executable code copied into RAM: the OAM DMA stub at `$FF80-$FF89`. That means the bytes themselves matter, even if C# ultimately implements the call as a semantic method. (ram/hram.asm:1-175; docs/recon/memory-map.md:15,30-34,69-74,1774-1806; docs/recon/hazards.md:18-32,93-99)

```csharp
public sealed class HramFile
{
    private readonly byte[] _bytes = new byte[0x7F];

    public byte Read(byte offset) => _bytes[offset];
    public void Write(byte offset, byte value) => _bytes[offset] = value;

    public Span<byte> Bytes => _bytes;
    public Span<byte> TransferShadowOamCode => _bytes.AsSpan(0x00, 0x0A);
    public ref byte RomBank => ref _bytes[0x1F];
    public ref byte JoypadReleased => ref _bytes[0x24];
    public ref byte RandomAdd => ref _bytes[0x63];
    public ref byte RandomSub => ref _bytes[0x64];
}
```

Model the DMA stub in two layers:

- preserve the copied bytes in `HRAM[0x00..0x09]`
- route the actual "call HRAM code" behavior to a semantic `TransferShadowOam()` method that blocks non-HRAM access for the DMA window and copies shadow OAM into hardware OAM. (engine/gfx/load_push_oam.asm:1-27; docs/conventions/translation-patterns.md:1165-1185)

`UNCLEAR:` recon found only this one executable HRAM block. Do not build a general writable-code system unless more cases appear. (docs/recon/hazards.md:29-32)

Also preserve HRAM contiguity: Gold relies on adjacent scratch bytes, e.g. `_Multiply` uses `hMultiplicand - 1` as spill space. (ram/hram.asm:60-97; docs/recon/hazards.md:183-187)

## 5. SRAM representation

Represent SRAM as 4 physical 8 KiB banks plus live controller state: current bank, enabled/disabled state, and RTC latch/register selection. `OpenSRAM` / `CloseSRAM` are not just convenience functions; they are part of the cartridge protocol and the RTC latch state machine. (ram/sram.asm:1-175; home/sram.asm:1-23; docs/recon/memory-map.md:9-12,41-43,58-64,1967-2080; docs/recon/hazards.md:111-132)

```csharp
public ref struct SramWindow
{
    private GoldMemory _memory;
    private readonly int _bank;

    public SramWindow(GoldMemory memory, int bank)
    {
        _memory = memory;
        _bank = bank;
    }

    public Span<byte> Bytes => _memory.Sram.AsSpan(_bank * 0x2000, 0x2000);

    public void Dispose() => Close();

    public void Close()
    {
        _memory.Io.RtcLatch = 0;
        _memory.SramEnabled = false;
    }
}
```

Use it with a scoped `OpenSram(bank)` helper so callers cannot forget the close/latch-disable step. (docs/conventions/translation-patterns.md:387-427)

Gold's save layout matters:

- bank 1 `sGameData` (`$A009-$ACCC`) is checksummed
- `sChecksum` is stored immediately after it
- active box mirror `sBox` (`$ACD0-$B11D`) is **not** part of that checksum
- archived boxes live separately in banks 2-3 as 14 `box` records
- backup save fragments are split across banks 0, 1, and 3. (ram/sram.asm:87-109,137-173; docs/recon/memory-map.md:41-43,1971-1988,2049-2080; docs/recon/data-formats.md:541-603)

That separation is load-bearing for Bad Clone and other save corruption glitches; do not collapse the active box into a single transactional save object. (docs/recon/glitches.md:96-117,369-389)

## 6. VRAM representation

Represent VRAM as two physical 8 KiB banks and keep bank 0/1 selection explicit. Gold uses bank 0 for tile/pixel and BG-map data and bank 1 for CGB attribute/second-bank data. More importantly, VRAM is **timing-constrained**: many routines wait for specific `LY` / `STAT` windows before writing. (ram/vram.asm:1-18; docs/recon/memory-map.md:7-8,91-94,2083-2103; docs/recon/hazards.md:72-99)

Recommended split:

- `VramStore`: raw bytes, banked exactly like hardware.
- `VideoScheduler`: decides whether a write is legal now, delayed until VBlank/HBlank, or should fail/block.
- `Renderer`: reads snapshots/views of VRAM/OAM/palette state after scheduler-controlled writes, not from ad hoc gameplay-owned caches. (docs/conventions/translation-patterns.md:429-450; docs/recon/execution-flow.md:56-75)

```csharp
public bool TryWriteVram(ushort address, ReadOnlySpan<byte> source)
{
    if (!VideoScheduler.CanAccessVramNow())
        return false;

    source.CopyTo(Vram.AsSpan((CurrentVramBank * 0x2000) + (address - 0x8000)));
    return true;
}
```

**CONTENTIOUS:** a gameplay-only port could ignore VRAM timing and still "work," but the requested byte-accurate model should keep VRAM access mediated. The original code explicitly budgets VRAM work against scanline timing and VBlank order. (docs/recon/hazards.md:72-99; docs/recon/execution-flow.md:56-75)

## 7. Struct access patterns with worked examples

### Example 1: party Pokémon access

Gold's canonical pattern is: start at `wPartyMons`, add the field offset, then add `wCurPartyMon * PARTYMON_STRUCT_LENGTH`. `GetPartyParamLocation` does exactly that, and `HealPartyMon` then walks from `MON_STATUS` and `MON_MAXHP` to rewrite bytes in place. (home/battle.asm:1-16; engine/pokemon/health.asm:25-53)

```csharp
public sealed class WramBank1View
{
    private readonly byte[] _bank1;

    public WramBank1View(byte[] bank1) => _bank1 = bank1;

    public ref byte PartyCount => ref _bank1[0x986];
    public Span<byte> PartySpecies => _bank1.AsSpan(0x987, 6);

    public PartyMonView GetPartyMon(int slot)
        => new(_bank1.AsSpan(0x98E + (slot * 48), 48));

    public Span<byte> GetPartyOtName(int slot)
        => _bank1.AsSpan(0xAAE + (slot * 11), 11);

    public Span<byte> GetPartyNickname(int slot)
        => _bank1.AsSpan(0xAF0 + (slot * 11), 11);
}

var mon = wram1.GetPartyMon(wCurPartyMon);
mon.Status = 0;
mon.CurrentHp = mon.MaxHp;
```

Why this shape is correct:

- the WRAM party blob is `count + visible species list + terminator + 6 x 48-byte party structs + OT strings + nickname strings`; it is not a single `PartyMon[]` object. (ram/wram.asm:2669-2692; docs/recon/data-formats.md:81-124; docs/recon/memory-map.md:20-22,1664-1689)
- `party_struct` is a `box_struct` plus 16 live bytes, with mixed endianness inside the same 48-byte record. OT ID is little-endian; EXP is 3-byte big-endian; HP/current stats are big-endian words. (macros/ram.asm:7-42; constants/pokemon_data_constants.asm:75-107; engine/pokemon/move_mon.asm:144-165; engine/pokemon/health.asm:37-50)
- the visible party species list and the hidden per-struct species byte must remain separate, because egg glitches depend on them diverging. Do **not** derive one from the other. (engine/pokemon/move_mon.asm:1121-1188; docs/recon/glitches.md:29-55)

Recommended helper set:

```csharp
public static ushort ReadBe16(ReadOnlySpan<byte> data, int offset) =>
    BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

public static ushort ReadLe16(ReadOnlySpan<byte> data, int offset) =>
    BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));

public static uint ReadBe24(ReadOnlySpan<byte> data, int offset) =>
    (uint)(data[offset] << 16 | data[offset + 1] << 8 | data[offset + 2]);
```

### Example 2: battle state access

The battle overlay starts at `wBattle` and begins with two 7-byte `move_struct`s (`wEnemyMoveStruct`, `wPlayerMoveStruct`), nickname buffers, then a `UNION` whose battle branch is `wBattleMon` and whose non-battle branch is intro-cutscene scratch. Later, `wEnemyMon` is itself inside another `UNION` that can instead hold link-battle RNG data. This is real aliasing, not a conceptual grouping. (ram/wram.asm:738-759,2090-2104; macros/ram.asm:76-97,245-253; docs/recon/memory-map.md:38-39,586-588)

```csharp
public ref struct MoveStructView
{
    private Span<byte> _data;
    public MoveStructView(Span<byte> data) => _data = data;

    public ref byte Animation => ref _data[0];
    public ref byte Effect => ref _data[1];
    public ref byte Power => ref _data[2];
    public ref byte Type => ref _data[3];
    public ref byte Accuracy => ref _data[4];
    public ref byte Pp => ref _data[5];
    public ref byte EffectChance => ref _data[6];
}

public ref struct BattleMonView
{
    private Span<byte> _data;
    public BattleMonView(Span<byte> data) => _data = data;

    public ref byte Species => ref _data[0x00];
    public ushort CurrentHp
    {
        get => BinaryPrimitives.ReadUInt16BigEndian(_data.Slice(0x0E, 2));
        set => BinaryPrimitives.WriteUInt16BigEndian(_data.Slice(0x0E, 2), value);
    }
}

public ref struct BattleOverlayView
{
    private Span<byte> _wram0;
    private Span<byte> _wram1;

    public BattleOverlayView(Span<byte> wram0, Span<byte> wram1)
    {
        _wram0 = wram0;
        _wram1 = wram1;
    }

    public MoveStructView EnemyMove => new(_wram0.Slice(0x0AA0, 7));
    public MoveStructView PlayerMove => new(_wram0.Slice(0x0AA7, 7));
    public BattleMonView PlayerBattleMon =>
        new(_wram0.Slice(0x0AA0 + 7 + 7 + 11 + 11, 32));
    public BattleMonView EnemyBattleMon => new(_wram1.Slice(0x00EF, 32));
}
```

Important modeling rule: `BattleOverlayView`, `IntroCutsceneScratchView`, and `LinkBattleRnView` should all wrap the **same underlying bytes**. Do not copy battle state into a separate runtime object when battle starts. Union-style access is the correct translation of `UNION` / `NEXTU` in `ram/wram.asm`. (ram/wram.asm:745-759,2090-2098)

`UNCLEAR:` `docs/recon/memory-map.md` appears to contain address typos for `wBattleMon`/intro-following fields around this overlay. Trust the source order in `ram/wram.asm` over the generated table when they conflict. (docs/recon/memory-map.md:589-595; ram/wram.asm:742-759)

### Example 3: save data read/write

Gold saves the active box and main save payload separately. `ChangeBoxSaveGame` saves the outgoing active box, updates `wCurBox`, loads the new active box mirror, and only then runs the broader save flow. `SaveChecksum` checksums only `sGameData`, while load paths still restore `LoadBox` after checksum verification. `SaveBoxAddress` / `LoadBoxAddress` copy the active box through `wBoxPartialData` in **three phases** because the scratch buffer is 480 bytes and `sBox` is 1102 bytes. (engine/menus/save.asm:40-58,424-434,538-559,851-987,1038-1051; ram/wram.asm:172-176; ram/sram.asm:87-109)

```csharp
private const int SramBankSize = 0x2000;
private const int SGameDataOffset = 0x0009;
private const int SGameDataLength = 3268;
private const int SChecksumOffset = 0x0CCD;
private const int SActiveBoxOffset = 0x0CD0;
private static ReadOnlySpan<int> BoxChunkLengths => [480, 480, 0x8E];

public void SaveChecksum()
{
    using var sram1 = OpenSram(1);
    Span<byte> bank = sram1.Bytes;

    ushort sum = 0;
    foreach (byte b in bank.Slice(SGameDataOffset, SGameDataLength))
        sum += b;

    BinaryPrimitives.WriteUInt16LittleEndian(bank.Slice(SChecksumOffset, 2), sum);
}

public void SaveBox(BoxAddress target)
{
    int sourceOffset = SActiveBoxOffset;
    int targetOffset = target.Offset;

    foreach (int length in BoxChunkLengths)
    {
        CopySramToWram(bank: 1, sourceOffset, WBoxPartialData, length);
        CopyWramToSram(WBoxPartialData, target.Bank, targetOffset, length);
        sourceOffset += length;
        targetOffset += length;
    }
}
```

Preservation requirements:

- keep `sGameData` checksum boundaries exact; checksum is a straight 16-bit additive sum over bytes, stored little-endian. (engine/menus/save.asm:424-434,1038-1051)
- keep `sBox` outside that checksum. (ram/sram.asm:93-109; docs/recon/glitches.md:100-117,373-389)
- keep the **phase ordering** of box save/load visible to interruption/reset tests; otherwise Bad Clone disappears. (engine/menus/save.asm:40-58,851-987; docs/recon/glitches.md:96-117)

**CONTENTIOUS:** do not replace save/load with one atomic, fully transactional "save object" if glitch compatibility matters. The original implementation is intentionally phaseful and interruptible.

## 8. Endianness handling

Gold is **field-wise mixed-endian**, not globally big-endian or little-endian:

- OT ID: little-endian (`move_mon.asm` writes low byte then high byte)
- ROM pointers / `GetFarWord`: little-endian
- EXP: 3-byte big-endian
- HP and live stats in `party_struct` / `battle_struct`: big-endian
- save checksum word: little-endian. (engine/pokemon/move_mon.asm:144-165; home/copy.asm:38-55; engine/pokemon/health.asm:37-50; macros/ram.asm:7-42,76-97; engine/menus/save.asm:424-434)

Design rule: keep raw bytes canonical and centralize endian helpers. Avoid returning `ref ushort` or `ref uint` for packed fields; the host CPU endianness and the ROM's field endianness are different concerns.

```csharp
public static void WriteBe24(Span<byte> data, int offset, uint value)
{
    data[offset + 0] = (byte)(value >> 16);
    data[offset + 1] = (byte)(value >> 8);
    data[offset + 2] = (byte)value;
}
```

This is especially important for Pokémon structs, where `MON_OT_ID`, `MON_EXP`, `MON_HP`, and `MON_MAXHP` sit only a few bytes apart but use different encodings. (constants/pokemon_data_constants.asm:77-107; docs/recon/data-formats.md:57-96)

## 9. Memory aliasing and overlays

Model aliasing with **multiple views over one backing store**, not copy-in/copy-out. Gold uses `UNION` / `NEXTU` pervasively in RAM definitions, and many glitches or edge cases depend on stale bytes surviving mode changes. Good examples:

- `wBattleMon` overlays intro scratch in the `wBattle` block. (ram/wram.asm:745-759)
- `wEnemyMon` overlays link-battle RNG data. (ram/wram.asm:2090-2098)
- HRAM math scratch overlays multiply/divide/result buffers. (ram/hram.asm:60-97)
- active box save logic stages through `wBoxPartialData` because the scratch buffer is smaller than `sBox`. (ram/wram.asm:172-176; engine/menus/save.asm:851-987)

Recommended pattern:

```csharp
public BattleOverlayView Battle => new(Wram0, ActiveWramBank1);
public IntroScratchView Intro => new(Wram0);
public LinkBattleRnView LinkBattleRn => new(ActiveWramBank1);
```

Each view may expose different names and invariants, but they must address the same bytes.

**CONTENTIOUS:** debug-time mode assertions are fine (`if (!state.InBattle) throw ...`), but do not make the underlying storage mode-specific. The bytes should survive regardless of which semantic view is active.

## 10. Important design decisions to flag

- **CONTENTIOUS:** use separate physical arrays plus a bus facade, not one flat 64 KiB canonical array. Banked physical identity matters. (docs/recon/memory-map.md:7-15,54-59; docs/recon/build-system.md:215-223,354-360)
- **CONTENTIOUS:** allocate full hardware-sized WRAMX backing (`8 x 0x1000`) even though this ROM links WRAM bank 1 only. That keeps `rWBK` modeling honest and avoids painting the implementation into a corner. (docs/recon/build-system.md:356-360; docs/recon/memory-map.md:14,93)
- **CONTENTIOUS:** keep a bank-aware ROM API even with flat host-memory storage. (docs/conventions/translation-patterns.md:511-570; docs/recon/hazards.md:101-109)
- **CONTENTIOUS:** prefer span-backed views over `[StructLayout]` overlays for mon/save structs because mixed endianness and padding are pervasive. (docs/recon/data-formats.md:53-124; macros/ram.asm:7-42,99-124)
- **CONTENTIOUS:** model VRAM writes through a timing-aware scheduler, not direct unrestricted `byte[]` mutation. (docs/recon/hazards.md:72-99; docs/conventions/translation-patterns.md:429-450)
- **CONTENTIOUS:** keep save/box copy phases observable and interruptible so active-box desync glitches survive. (engine/menus/save.asm:40-58,851-987; docs/recon/glitches.md:96-117,369-389)
- `UNCLEAR:` only one executable HRAM trampoline is confirmed so far: OAM DMA. (docs/recon/hazards.md:29-32; engine/gfx/load_push_oam.asm:13-27)
- `UNCLEAR:` the recon memory-map table appears to have at least one generated address typo inside the `wBattle` overlay; verify against `ram/wram.asm` before hardcoding offsets. (docs/recon/memory-map.md:589-595; ram/wram.asm:742-759)

## Bottom line

The recommended model is: **raw banked bytes first, typed span-backed views second, timing/bank semantics always explicit**. That is the smallest C# design that still respects Gold's save layout, overlay aliasing, far reads, HRAM DMA behavior, and glitch-sensitive byte layout. (docs/conventions/translation-patterns.md:312-570,821-844,1165-1185; docs/recon/glitches.md:3-25)
