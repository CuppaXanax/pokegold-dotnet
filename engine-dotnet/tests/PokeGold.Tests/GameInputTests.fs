module PokeGold.Tests.GameInputTests

open Xunit
open PokeGold.Game
open PokeGold.Game.Core

// ── Buttons masking helpers (pure) ───────────────────────────────────────────

[<Fact>]
let ``intersect keeps only buttons held in both frames`` () =
    let a = { Buttons.none with A = true; Start = true; Down = true }
    let b = { Buttons.none with A = true; Down = false; Start = true }
    let r = Buttons.intersect a b
    Assert.True(r.A)
    Assert.True(r.Start)
    Assert.False(r.Down)
    Assert.False(r.B)

[<Fact>]
let ``except clears every button present in the mask`` () =
    let a = { Buttons.none with A = true; B = true; Up = true }
    let mask = { Buttons.none with A = true; Up = true }
    let r = Buttons.except a mask
    Assert.False(r.A)   // masked
    Assert.False(r.Up)  // masked
    Assert.True(r.B)    // not masked, still held

[<Fact>]
let ``except with an empty mask is identity`` () =
    let a = { Buttons.none with A = true; Left = true }
    Assert.Equal(a, Buttons.except a Buttons.none)

// ── Game-level debounce: the press that opens a scene must not leak into it ───

/// The active (top) scene type, read through the debug command bridge.
let private topScene (g: Game) = g.RunDebugCommand "scene"

let private startOverworld () =
    let g = Game()
    g.Tick({ Buttons.none with Start = true })
    g.Tick(Buttons.none)
    g

[<Fact>]
let ``holding Start keeps the start menu open instead of flickering`` () =
    let g = startOverworld ()
    let start = { Buttons.none with Start = true }
    Assert.Equal("OverworldScene", topScene g)

    g.Tick(start)                                    // rising edge opens the menu
    Assert.Equal("StartMenuScene", topScene g)

    g.Tick(start)                                    // Start STILL held → masked
    Assert.Equal("StartMenuScene", topScene g)       // must not close (the flicker)

    g.Tick(start)                                    // and again while held
    Assert.Equal("StartMenuScene", topScene g)

[<Fact>]
let ``releasing then re-pressing Start closes the start menu`` () =
    let g = startOverworld ()
    let start = { Buttons.none with Start = true }

    g.Tick(start)                                    // open
    Assert.Equal("StartMenuScene", topScene g)
    g.Tick(Buttons.none)                             // release (mask clears)
    Assert.Equal("StartMenuScene", topScene g)
    g.Tick(start)                                    // fresh press closes
    Assert.Equal("OverworldScene", topScene g)

[<Fact>]
let ``the A that opens a submenu does not bleed into it`` () =
    // Open the start menu, then press A on the default entry (POKéDEX) to push a
    // child scene. Holding A across the transition must not act inside the child:
    // the systemic input mask hides the carried-over A until it's released.
    let g = startOverworld ()
    g.Tick({ Buttons.none with Start = true })       // open start menu
    g.Tick(Buttons.none)                             // release Start
    Assert.Equal("StartMenuScene", topScene g)

    g.Tick({ Buttons.none with A = true })           // A opens POKéDEX child
    let child = topScene g
    Assert.True(child <> "StartMenuScene")           // a child scene is now on top

    g.Tick({ Buttons.none with A = true })           // A still held → masked
    Assert.Equal(child, topScene g)                  // child stays open, no bleed
