# Platform interface design

Goal: define the smallest .NET-only boundary between translated pokegold game logic and any host backend (Sokol.NET first, but also SDL2, Raylib, browser canvas/WebGL, mobile). The boundary should keep Game Boy-specific rendering, audio, RTC, and timing semantics in core code, while the platform only handles presentation, device I/O, persistence, and lifecycle. This follows the project-wide rule to keep bytes, bank state, and scheduling behavior explicit rather than hiding them inside a framework. (docs/conventions/memory-model.md:7-10,206-227,425-438; docs/conventions/translation-patterns.md:429-509,1165-1345)

## 1. Ground truth from source

- VBlank is the frame heartbeat and already acts like the ROM's scheduler: it updates scroll/window registers, runs one prioritized VRAM job, performs OAM DMA, clears `wVBlankOccurred`, polls joypad, and ticks audio in that exact order. (home\vblank.asm:53-150; docs/recon/execution-flow.md:145-176)
- LCD STAT is used for per-scanline raster effects via `wLYOverrides` and `hLCDCPointer`; this is not a simple "draw final BG/OAM state once per frame" game. (home\lcd.asm:3-23; ram\wram.asm:629-642; ram\hram.asm:119-145; docs/recon/hazards.md:79-91)
- Joypad is polled once per VBlank, and the game computes press/release edges itself into `hJoypadPressed`, `hJoypadReleased`, `hJoyPressed`, and friends. (home\vblank.asm:139; home\joypad.asm:16-156; docs/recon/execution-flow.md:163-166,211-214)
- Audio is register-shaped internally: the sound engine ticks once per frame and writes Game Boy audio registers and wave RAM directly. (home\vblank.asm:141-145; audio\engine.asm:84-211,243-507; docs/recon/source-map.md:1738-1748)
- OAM DMA is a hardware transaction copied into HRAM at boot and invoked during VBlank. (home\init.asm:86-90; engine\gfx\load_push_oam.asm:1-27; docs/recon/hazards.md:93-99)
- SRAM open/close is not host persistence; it is cartridge protocol and RTC latch state. `OpenSRAM` raises `rRTCLATCH`, `CloseSRAM` drops it, and save code opens/closes SRAM repeatedly inside one logical save. (home\sram.asm:1-23; home\time.asm:6-12,21-59,205-250; engine\menus\save.asm:396-535,851-987; docs/recon/hazards.md:113-132)
- Serial/printer code is timing-shaped, branches on clock mode, uses busy loops, frame timeouts, and even VC-specific patches. (home\serial.asm:13-64,122-342; home\printer.asm:5-41; engine\printer\printer.asm:1-117; engine\printer\printer_serial.asm:158-277,445-627; docs/recon/hazards.md:237-245)

## 2. Design summary

| Concern | Decision | Why |
|---|---|---|
| Display | Submit a final **160x144 RGB555 framebuffer** to the platform. | Per-scanline LCD behavior means the Game Boy renderer must stay in core; the platform should only present pixels. (home\lcd.asm:3-23; docs/recon/hazards.md:79-91) |
| Audio | Submit **PCM samples** to the platform, not Game Boy register writes. | Register synthesis is Game Boy APU logic, so it belongs in core just like VRAM/OAM composition. (audio\engine.asm:84-211,243-507) |
| Input | Platform returns the current raw **8-button bitmask** only. | The game already does edge detection and mirroring. (home\joypad.asm:16-156) |
| Save/RTC | Platform loads/saves one **battery image**: raw 32 KiB SRAM + opaque core-defined RTC state blob. | Platform should persist bytes, not reimplement MBC3 latch/normalize rules. (home\sram.asm:1-23; home\time.asm:21-59,61-120,205-250) |
| Clock | Expose a .NET `TimeProvider`. | RTC and frame pacing need host time, but the core should own the scheduling and MBC3 model. (docs/recon/execution-flow.md:23-26; docs/recon/hazards.md:113-132) |
| Serial | Keep serial as an **optional raw byte endpoint** separate from display/audio/input. | High-level `TradeAsync`/`PrintAsync` APIs would be the wrong abstraction. (home\serial.asm:13-342; docs/recon/hazards.md:237-245) |
| Frame timing | The **game host** owns frame pacing; the platform only exposes time and lifecycle/events. | This preserves "platform is servant, not framework". (home\vblank.asm:1-38,53-150; docs/recon/execution-flow.md:143-176) |

## 3. Display / framebuffer

Use a final framebuffer interface, not a tile/OAM submission API.

Why:

1. The ROM writes VRAM, BG maps, palettes, and OAM in hardware-shaped phases, and some visible effects depend on scanline-time state changes, not just end-of-frame memory contents. (home\vblank.asm:96-118; home\lcd.asm:3-23; docs/recon/hazards.md:72-91)
2. `wLYOverrides` is 144 bytes long specifically for per-scanline LCD register overrides. If the platform accepted only tile maps plus sprites, it would need to understand STAT timing, LY, register side effects, palette uploads, and OAM DMA ordering anyway. At that point the platform would secretly be the Game Boy PPU. (ram\wram.asm:629-642; docs/recon/memory-map.md:520-525; docs/recon/hazards.md:79-91)
3. The already-decided memory model keeps VRAM/OAM/palette data raw and timing-aware inside core code. The renderer should live next to that memory model, not inside the host backend. (docs/conventions/memory-model.md:206-227; docs/conventions/translation-patterns.md:429-450)

Therefore:

- Core emulation/translation owns VRAM, OAM, palettes, `LY`, `STAT`, and scanline composition.
- A core-side renderer produces a resolved 160x144 framebuffer in Game Boy color format.
- The platform only receives `ReadOnlySpan<ushort>` RGB555 pixels and presents them.

`Present` should be called once per completed Game Boy frame, after the core has finished the frame's VBlank-time work and finalized the image for that frame. The platform may display it immediately or on the next host vsync, but it must not reinterpret the buffer as tiles, sprites, or PPU state. (home\vblank.asm:53-150; docs/recon/execution-flow.md:145-176)

**CONTENTIOUS:** the platform does **not** receive VRAM, OAM, or BG maps directly. That is a bigger interface and pushes byte-accurate Game Boy video semantics into every backend.

## 4. Audio

Choose **platform option B**: core synthesizes PCM; platform plays PCM.

Why:

- The sound engine is already Game Boy register-facing: `_UpdateSound` walks 8 channel structs, updates note state, writes `rAUD*` registers, and loads wave RAM. (audio\engine.asm:84-211,243-507)
- If the platform accepted register writes and synthesized audio itself, every backend would need a Game Boy APU implementation. That is the opposite of a minimal backend boundary.
- PCM is the common denominator across desktop, browser, and mobile audio APIs.

Recommended contract:

- Stereo, interleaved, signed 16-bit PCM (`short`).
- Platform chooses the sample rate; core reads it from `IAudioOutput.SampleRate` and resamples accordingly.
- Recommend `48000` Hz by default. At Game Boy frame rate (`4194304 / 70224 ~= 59.72750057 Hz`), that is about `803.65` stereo frames per emulated frame, so the core audio mixer should carry fractional-sample error internally instead of assuming a constant integer frame size.
- Keep a small queue, roughly 2-3 Game Boy frames of audio, to avoid crackle without adding large latency.

`IAudioOutput` therefore needs only queue-style methods/properties, not callbacks. The backend must never pull samples by calling back into game logic. (home\vblank.asm:141-145; audio\engine.asm:84-211)

**CONTENTIOUS:** do not surface Game Boy audio registers in `IPlatform`. That would make the host backend part of the emulation core.

## 5. Input

The platform should provide only the current raw button state as one 8-bit mask:

- `A`
- `B`
- `Select`
- `Start`
- `Right`
- `Left`
- `Up`
- `Down`

That exactly matches what `UpdateJoypad` reconstructs from `rJOYP`, and it preserves the existing edge-detection logic in core code. The platform should not do debouncing or press/release tracking. (home\joypad.asm:16-104,106-156; docs/recon/memory-map.md:1799-1806)

Important consequence: `GetButtons()` should return the latest raw state snapshot after `PumpEvents()`. The core calls it once per emulated VBlank and computes deltas in the translated `UpdateJoypad` / `GetJoypad` logic. (home\vblank.asm:139; home\joypad.asm:74-95,133-156)

## 6. Save persistence and RTC

Persist one whole **battery image**:

- `Sram`: exact 32 KiB battery-backed SRAM (`4 * 0x2000`). (docs/recon/data-formats.md:545-559; docs/recon/memory-map.md:1967-2080)
- `RtcState`: opaque serialized state owned by the core MBC3/RTC implementation.

Why the RTC blob is opaque:

- The game does not treat RTC as just "current wall clock time". It latches registers, normalizes days modulo 140, preserves/clears specific bits manually, and records overflow/halt status in SRAM. (home\time.asm:21-59,61-120,205-250; engine\rtc\rtc.asm:91-162)
- `OpenSRAM` / `CloseSRAM` are part of the latch protocol. A backend should not know or care about that state machine. (home\sram.asm:1-23; docs/recon/hazards.md:113-132)

So the platform contract is:

- load one battery image at startup/resume
- save one battery image on request
- do not inspect or reinterpret the bytes

Host persistence timing should be **debounced snapshot I/O**, not "flush on every `CloseSRAM`". The save code opens/closes SRAM many times inside one logical save, and box storage is intentionally copied in three phases through `wBoxPartialData`. Persisting at each `CloseSRAM` would make host I/O mirror cartridge latch traffic, not actual save intent. (engine\menus\save.asm:396-535,538-560,851-987,1038-1051)

Recommended flush triggers:

- after the core marks battery-backed state dirty for a while
- on explicit save completion
- on suspend/background
- on clean shutdown

**CONTENTIOUS:** host persistence is a whole-image snapshot boundary, not an emulated cartridge write boundary.

## 7. Real-time clock ownership

The core, not the platform, should own the MBC3 RTC model.

The platform should only provide host time through `TimeProvider`. The core RTC model should:

- advance from host time deltas
- preserve latched/unlatched register behavior
- preserve HALT/CARRY/day-high bits
- serialize its complete state into `RtcState`

This is the only design that respects `LatchClock`, `FixDays`, `SetClock`, `_GetClock`, and the saved status-flag flows. (home\time.asm:6-12,21-59,61-120,205-280; engine\rtc\rtc.asm:91-162)

## 8. Serial / link / printer

Keep serial out of the required minimum path, but reserve an optional low-level endpoint.

Why low-level:

- Cable link and printer code operate on `rSB`, `rSC`, internal vs external clock, serial interrupts, busy waits, and frame timeouts. (home\serial.asm:13-342; home\printer.asm:5-41; engine\printer\printer_serial.asm:158-277,445-627)
- The VC patches in `WaitLinkTransfer` are strong evidence that literal original timing does not map cleanly to modern transports. (home\serial.asm:284-341; docs/recon/hazards.md:239-245)
- A high-level backend API like `TradePokemonAsync` or `PrintPageAsync` would bake game-specific protocol assumptions into the platform boundary.

So the minimum acceptable hook is: when the core serial controller decides a transfer completes, it may ask an optional endpoint for the peer byte. That keeps scheduling, interrupt firing, and timeout behavior in core.

**CONTENTIOUS:** `ISerialEndpoint` is optional on `IPlatform`. A single-player build can supply `null` and let the translated serial logic follow its disconnected code paths.

`UNCLEAR:` Mystery Gift lives in the same broad subsystem as cable link in recon, but it is not obviously the same transport contract as ordinary serial cable/printer traffic. A future `IInfraredEndpoint` may be cleaner if/when that path is translated in detail. (docs/recon/source-map.md:3792-3806)

## 9. Frame timing and ownership of the main loop

The platform should **not** fire VBlank and should **not** own the game loop.

Instead:

- a host-side `GameHost` / `Runner` owns the loop
- it calls `platform.PumpEvents()` to refresh lifecycle/input state
- it uses `platform.Clock` to decide how many Game Boy frames to simulate
- it calls into translated game/core code for each emulated frame
- the core calls `Display.Present`, `Audio.Enqueue`, and `Input.GetButtons()` as needed

That preserves the rule that game logic calls into the platform, not the reverse. It also makes fast-forward/slow-motion a host policy, not a backend requirement.

Recommended pacing rule:

- normal speed targets `59.72750057` Hz
- fast-forward runs multiple emulated frames before one present
- slow-motion changes host pacing, not Game Boy-internal frame semantics

`PumpEvents()` should surface lifecycle flags such as suspend/resume/exit so the host can flush battery state without handing control to the backend framework. (home\vblank.asm:1-38,53-150; docs/recon/execution-flow.md:143-176)

## 10. Proposed C# surface

```csharp
namespace Pokegold.Platform;

[Flags]
public enum GameBoyButtons : byte
{
    None   = 0,
    A      = 1 << 0,
    B      = 1 << 1,
    Select = 1 << 2,
    Start  = 1 << 3,
    Right  = 1 << 4,
    Left   = 1 << 5,
    Up     = 1 << 6,
    Down   = 1 << 7,
}

[Flags]
public enum PlatformSignals : byte
{
    None             = 0,
    ExitRequested    = 1 << 0,
    SuspendRequested = 1 << 1,
    ResumeRequested  = 1 << 2,
}

public enum SerialDeviceKind : byte
{
    LinkCable = 0,
    Printer   = 1,
}

public enum SerialClockMode : byte
{
    External = 0,
    Internal = 1,
}

public static class GameBoyPlatformConstants
{
    public const int ScreenWidth = 160;
    public const int ScreenHeight = 144;
    public const int BatterySramBytes = 4 * 0x2000;
    public const double FrameRate = 4194304d / 70224d;
}

public readonly record struct BatteryImage(
    ReadOnlyMemory<byte> Sram,
    ReadOnlyMemory<byte> RtcState);

public interface IPlatform : IAsyncDisposable
{
    IDisplay Display { get; }
    IAudioOutput Audio { get; }
    IInputSource Input { get; }
    IBatteryStore Battery { get; }
    TimeProvider Clock { get; }
    ISerialEndpoint? Serial { get; }

    PlatformSignals PumpEvents();
}

public interface IDisplay
{
    // rgb555Frame.Length must be 160 * 144.
    // Bits 0-4 = red, 5-9 = green, 10-14 = blue.
    void Present(ReadOnlySpan<ushort> rgb555Frame);
}

public interface IAudioOutput
{
    int SampleRate { get; }
    int Channels { get; }          // normally 2
    int TargetLatencyFrames { get; }
    int QueuedFrames { get; }

    void Enqueue(ReadOnlySpan<short> interleavedStereoPcm);
}

public interface IInputSource
{
    GameBoyButtons GetButtons();
}

public interface IBatteryStore
{
    ValueTask<BatteryImage?> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(BatteryImage image, CancellationToken cancellationToken = default);
}

public interface ISerialEndpoint
{
    SerialDeviceKind Kind { get; }
    bool IsConnected { get; }

    bool TryTransferByte(byte outbound, SerialClockMode clockMode, out byte inbound);
}
```

Why this split is preferable to one giant method bag:

- display/audio/input/persistence can be unit-tested independently
- `ISerialEndpoint` stays optional instead of bloating the single-player contract
- `IPlatform` is still the only object the host/game loop needs to carry around

## 11. Lifecycle and error handling

- `PumpEvents()` is expected to be non-throwing in normal operation; it just drains OS events and updates cached state.
- `GetButtons()`, `Present(...)`, and `Enqueue(...)` should also be non-throwing in normal operation.
- `LoadAsync()` returns `null` when no battery image exists yet.
- `SaveAsync()` may throw normal I/O exceptions; that is a host/runtime problem, not game logic.
- Serial disconnect is represented by `IsConnected == false` or `TryTransferByte(...) == false`, not by exceptions.

## 12. What is explicitly *not* in the interface

Not included on purpose:

- VRAM/OAM tile upload methods
- BG-map or sprite submission APIs
- Game Boy audio register write APIs
- per-button press/release events
- callbacks where the backend asks the game for the next frame/audio block
- `SleepUntilNextVBlank()` / backend-owned main loop control
- high-level trade/printer/gameplay protocol APIs

Those would all move Game Boy semantics or control flow into the platform layer and make backend swapping harder.

## 13. Sokol.NET implementation sketch

A Sokol.NET backend can map the interface cleanly:

- `IDisplay.Present(...)` -> upload the 160x144 RGB555 frame into a streaming texture each present, then draw one full-screen quad. On backends that do not like RGB555 upload directly, expand to RGBA8 inside the Sokol backend without changing the interface.
- `IAudioOutput.Enqueue(...)` -> append PCM into a small software queue feeding `sokol_audio`.
- `IInputSource.GetButtons()` -> return a cached `GameBoyButtons` mask built from `sokol_app` keyboard/gamepad/touch events.
- `IBatteryStore` -> desktop file I/O, browser IndexedDB/local storage wrapper, mobile app-data storage.
- `TimeProvider Clock` -> backed by `Stopwatch`/`TimeProvider.System`; Sokol-specific timing helpers stay inside the backend.
- `PumpEvents()` -> drain/translate Sokol app lifecycle events into `PlatformSignals` and cached input state.

This keeps the public contract NativeAOT-friendly and WebGL-friendly because it uses only enums, spans, memories, `TimeProvider`, and async persistence; no Sokol handle types leak into core code.

## 14. Bottom line

The minimum durable boundary is:

- **pixels in** (`Present`)
- **PCM in** (`Enqueue`)
- **raw buttons out** (`GetButtons`)
- **battery image load/save** (`Sram` + opaque `RtcState`)
- **clock + lifecycle** (`TimeProvider`, `PumpEvents`)
- **optional raw serial byte pipe** (`ISerialEndpoint`)

Everything Game Boy-specific and byte-accuracy-sensitive—PPU composition, APU synthesis, MBC3 RTC behavior, VBlank ordering, DMA semantics, and serial scheduling—should stay in translated core code, not in Sokol.NET or any other backend. (home\vblank.asm:53-150; home\lcd.asm:3-23; audio\engine.asm:84-211,243-507; home\sram.asm:1-23; home\time.asm:21-59,61-120,205-250; docs/conventions/memory-model.md:206-227,425-438)
