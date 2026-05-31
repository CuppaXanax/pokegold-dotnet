module PokeGold.Tests.StartMenuTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Scenes

// ── Helpers ───────────────────────────────────────────────────────────────────

/// Send one button-state frame to the scene and return the resulting Transition.
let private update (scene: StartMenuScene) (b: Buttons) : Transition =
    (scene :> Scene).Update(b)

/// Simulate a single button press: one frame with the button held (rising edge
/// fires), then one frame with no buttons (latch clears). Returns the transition
/// from the pressed frame.
let private press (b: Buttons) (scene: StartMenuScene) : Transition =
    let t = update scene b
    update scene Buttons.none |> ignore
    t

let private pressDown  s = press { Buttons.none with Down  = true } s
let private pressUp    s = press { Buttons.none with Up    = true } s
let private pressA     s = press { Buttons.none with A     = true } s
let private pressB     s = press { Buttons.none with B     = true } s
let private pressStart s = press { Buttons.none with Start = true } s

/// Build a scene that captures the `openEntry` argument in `captured` and returns Stay.
let private captureScene () =
    let mutable captured: StartEntry option = None
    let scene = StartMenuScene(Content(), fun e -> captured <- Some e; Stay)
    scene, (fun () -> captured)

// ── Cursor navigation ─────────────────────────────────────────────────────────

[<Fact>]
let ``StartMenu initial cursor is 0 (POKéDEX)`` () =
    let scene = StartMenuScene(Content(), fun _ -> Stay)
    Assert.Equal(0, scene.Cursor)

[<Fact>]
let ``StartMenu Down moves cursor from 0 to 1`` () =
    let scene = StartMenuScene(Content(), fun _ -> Stay)
    pressDown scene |> ignore
    Assert.Equal(1, scene.Cursor)

[<Fact>]
let ``StartMenu Down repeated moves through all entries`` () =
    let scene = StartMenuScene(Content(), fun _ -> Stay)
    for expected in 1 .. 5 do
        pressDown scene |> ignore
        Assert.Equal(expected, scene.Cursor)

[<Fact>]
let ``StartMenu Down wraps from EXIT (5) back to POKéDEX (0)`` () =
    let scene = StartMenuScene(Content(), fun _ -> Stay)
    for _ in 1 .. 6 do
        pressDown scene |> ignore
    Assert.Equal(0, scene.Cursor)

[<Fact>]
let ``StartMenu Up wraps from POKéDEX (0) to EXIT (5)`` () =
    let scene = StartMenuScene(Content(), fun _ -> Stay)
    pressUp scene |> ignore
    Assert.Equal(5, scene.Cursor)

[<Fact>]
let ``StartMenu Up decrements cursor`` () =
    let scene = StartMenuScene(Content(), fun _ -> Stay)
    pressDown scene |> ignore // cursor = 1
    pressDown scene |> ignore // cursor = 2
    pressUp   scene |> ignore // cursor = 1
    Assert.Equal(1, scene.Cursor)

[<Fact>]
let ``StartMenu navigation returns Stay`` () =
    let scene = StartMenuScene(Content(), fun _ -> Stay)
    Assert.Equal(Stay, pressDown scene)
    Assert.Equal(Stay, pressUp   scene)

// ── A button activates entries ─────────────────────────────────────────────────

[<Fact>]
let ``StartMenu A on POKéDEX (index 0) calls openEntry with Pokedex`` () =
    let scene, getCaptured = captureScene ()
    pressA scene |> ignore
    Assert.Equal(Some Pokedex, getCaptured())

[<Fact>]
let ``StartMenu A on POKéMON (index 1) calls openEntry with Pokemon`` () =
    let scene, getCaptured = captureScene ()
    pressDown scene |> ignore
    pressA scene |> ignore
    Assert.Equal(Some Pokemon, getCaptured())

[<Fact>]
let ``StartMenu A on PACK (index 2) calls openEntry with Pack`` () =
    let scene, getCaptured = captureScene ()
    pressDown scene |> ignore
    pressDown scene |> ignore
    pressA scene |> ignore
    Assert.Equal(Some Pack, getCaptured())

[<Fact>]
let ``StartMenu A on SAVE (index 3) calls openEntry with Save`` () =
    let scene, getCaptured = captureScene ()
    for _ in 1 .. 3 do pressDown scene |> ignore
    pressA scene |> ignore
    Assert.Equal(Some Save, getCaptured())

[<Fact>]
let ``StartMenu A on OPTION (index 4) calls openEntry with Option`` () =
    let scene, getCaptured = captureScene ()
    for _ in 1 .. 4 do pressDown scene |> ignore
    pressA scene |> ignore
    Assert.Equal(Some Option, getCaptured())

[<Fact>]
let ``StartMenu A on EXIT (index 5) returns Pop without calling openEntry`` () =
    let mutable called = false
    let scene = StartMenuScene(Content(), fun _ -> called <- true; Stay)
    for _ in 1 .. 5 do pressDown scene |> ignore
    let t = pressA scene
    Assert.Equal(Pop, t)
    Assert.False(called)

// ── B and Start close the menu ─────────────────────────────────────────────────

[<Fact>]
let ``StartMenu B returns Pop`` () =
    let scene = StartMenuScene(Content(), fun _ -> Stay)
    Assert.Equal(Pop, pressB scene)

[<Fact>]
let ``StartMenu Start returns Pop`` () =
    let scene = StartMenuScene(Content(), fun _ -> Stay)
    Assert.Equal(Pop, pressStart scene)

[<Fact>]
let ``StartMenu B does not call openEntry`` () =
    let mutable called = false
    let scene = StartMenuScene(Content(), fun _ -> called <- true; Stay)
    pressB scene |> ignore
    Assert.False(called)

// ── openEntry factory result is forwarded ────────────────────────────────────

[<Fact>]
let ``StartMenu A forwards Push transition from openEntry`` () =
    let child = TextBoxScene.Of(Content(), "test<DONE>") :> Scene
    let scene = StartMenuScene(Content(), fun _ -> Push child)
    let t = pressA scene
    Assert.Equal(Push child, t)

[<Fact>]
let ``StartMenu idle frame returns Stay`` () =
    let scene = StartMenuScene(Content(), fun _ -> Stay)
    Assert.Equal(Stay, update scene Buttons.none)

// ── Render: box border drawn at expected position ────────────────────────────

[<Fact>]
let ``StartMenu Render draws non-zero pixels at box top-left corner`` () =
    // The box starts at tile (Left=10, Top=0); its top-left corner is at pixel (80, 0).
    // Graphics.drawTile writes 8×8 RGBA pixels; the BoxTopLeft glyph (0x79) is
    // non-blank, so at least one pixel in that 8×8 block should be non-zero.
    let fb = Framebuffer()
    let scene = StartMenuScene(Content(), fun _ -> Stay)
    (scene :> Scene).Render(fb)

    let mutable anyNonZero = false
    for py in 0 .. 7 do
        for px in 80 .. 87 do
            let i = (py * Display.Width + px) * 4
            if fb.Pixels.[i] <> 0uy || fb.Pixels.[i + 1] <> 0uy || fb.Pixels.[i + 2] <> 0uy then
                anyNonZero <- true

    Assert.True(anyNonZero, "Expected BoxTopLeft border glyph to write non-zero pixels at tile (10, 0)")
