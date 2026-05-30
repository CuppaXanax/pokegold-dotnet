# engine-dotnet

A parallel F# + MonoGame engine that reimplements Pokémon Gold as a native game,
loading the existing repository assets (`../gfx`, `../maps`, `../data`, `../audio`, …)
directly as source. It does not touch or depend on the RGBDS build.

See `../docs/plan/README.md` for the vision and `../docs/plan/plan.md` for the
milestone plan.

## Layout

```
PokeGold.slnx
src/
  PokeGold.Game/   F# library — platform-agnostic game: data, scripts, systems, framebuffer.
  PokeGold.Host/   F# executable — MonoGame DesktopGL shell: window, loop, input, present.
tests/
  PokeGold.Tests/  xUnit tests for the game library.
```

The boundary rule: `PokeGold.Game` has no MonoGame dependency. It produces a 160×144
RGBA framebuffer each tick; `PokeGold.Host` uploads that to a texture and presents it
scaled (integer scale, nearest-neighbor, letterboxed).

## Run

```
dotnet run --project src/PokeGold.Host
```

A 640×576 window opens showing the 160×144 framebuffer scaled 4×. At M1 it renders a
hand-authored 2bpp tile sheet (vertical bands + checkerboard) tiled across the screen with a
cycling DMG-green palette — proving the tile decoder, palette, and blitter end to end.

## Test

```
dotnet test
```

## Assets

Repository assets live one level up and are shared, not copied. The engine reads them
in place (resolved relative to the repo root). No ROM is required.
