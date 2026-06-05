module PokeGold.Tests.OptionsTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Scenes
open PokeGold.Game.Text

// ── Options.textSpeedDelay mapping ────────────────────────────────────────────

[<Fact>]
let ``Options textSpeedDelay FAST (3) returns 1 frame`` () =
    Assert.Equal(1, Options.textSpeedDelay 3)

[<Fact>]
let ``Options textSpeedDelay MID (2) returns 3 frames`` () =
    Assert.Equal(3, Options.textSpeedDelay 2)

[<Fact>]
let ``Options textSpeedDelay SLOW (1) returns 5 frames`` () =
    Assert.Equal(5, Options.textSpeedDelay 1)

[<Fact>]
let ``Options textSpeedDelay unknown value defaults to 3 (MID)`` () =
    Assert.Equal(3, Options.textSpeedDelay 0)
    Assert.Equal(3, Options.textSpeedDelay 99)

// ── TextBox speed affects typewriter rate ─────────────────────────────────────

/// Run `n` frames with no input and return the resulting box.
let private idle n box =
    let mutable s = box
    for _ in 1..n do
        s <- TextBox.tick Buttons.none s
    s

/// Count the number of non-space glyphs in the first line of a box.
let private glyphsTyped (box: TextBoxState) =
    box.Lines.[0] |> Array.filter (fun b -> b <> Charmap.Space) |> Array.length

[<Fact>]
let ``FAST box types more glyphs than MID box in the same number of frames`` () =
    let text = "ABCDEFGHIJ<DONE>"
    let fastBox = TextBox.ofStringWithSpeed (Options.textSpeedDelay 3) text
    let midBox  = TextBox.ofStringWithSpeed (Options.textSpeedDelay 2) text
    let frames  = 15

    let fast = glyphsTyped (idle frames fastBox)
    let mid  = glyphsTyped (idle frames midBox)
    Assert.True(fast > mid, sprintf "FAST typed %d glyphs, MID typed %d in %d frames" fast mid frames)

[<Fact>]
let ``MID box types more glyphs than SLOW box in the same number of frames`` () =
    let text = "ABCDEFGHIJ<DONE>"
    let midBox  = TextBox.ofStringWithSpeed (Options.textSpeedDelay 2) text
    let slowBox = TextBox.ofStringWithSpeed (Options.textSpeedDelay 1) text
    let frames  = 20

    let mid  = glyphsTyped (idle frames midBox)
    let slow = glyphsTyped (idle frames slowBox)
    Assert.True(mid > slow, sprintf "MID typed %d glyphs, SLOW typed %d in %d frames" mid slow frames)

[<Fact>]
let ``FAST box has Speed field 1`` () =
    let box = TextBox.ofStringWithSpeed 1 "A<DONE>"
    Assert.Equal(1, box.Speed)

[<Fact>]
let ``default box Speed equals TypewriterDelay`` () =
    let box = TextBox.ofString "A<DONE>"
    Assert.Equal(TextBox.TypewriterDelay, box.Speed)

// ── OptionsScene cursor navigation ────────────────────────────────────────────

/// Send one button-state frame and return the Transition.
let private update (scene: OptionsScene) (b: Buttons) : Transition =
    (scene :> Scene).Update(b)

/// Press a button (one frame held + one frame released). Returns the pressed transition.
let private press (b: Buttons) (scene: OptionsScene) : Transition =
    let t = update scene b
    update scene Buttons.none |> ignore
    t

let private pressDown  s = press { Buttons.none with Down  = true } s
let private pressUp    s = press { Buttons.none with Up    = true } s
let private pressLeft  s = press { Buttons.none with Left  = true } s
let private pressRight s = press { Buttons.none with Right = true } s
let private pressB     s = press { Buttons.none with B     = true } s
let private pressStart s = press { Buttons.none with Start = true } s

let private defaultScene () =
    OptionsScene(Content(), PlayerStateOps.initial, fun _ -> ())

[<Fact>]
let ``OptionsScene initial cursor is 0 (TEXT SPEED)`` () =
    let scene = defaultScene ()
    Assert.Equal(0, scene.Cursor)

[<Fact>]
let ``OptionsScene Down moves cursor from 0 to 1`` () =
    let scene = defaultScene ()
    pressDown scene |> ignore
    Assert.Equal(1, scene.Cursor)

[<Fact>]
let ``OptionsScene Down then Up returns cursor to 0`` () =
    let scene = defaultScene ()
    pressDown scene |> ignore
    pressUp   scene |> ignore
    Assert.Equal(0, scene.Cursor)

[<Fact>]
let ``OptionsScene Down does not wrap (clamps at 2)`` () =
    let scene = defaultScene ()
    for _ in 1..5 do pressDown scene |> ignore
    Assert.Equal(2, scene.Cursor)

[<Fact>]
let ``OptionsScene Up does not wrap (clamps at 0)`` () =
    let scene = defaultScene ()
    for _ in 1..5 do pressUp scene |> ignore
    Assert.Equal(0, scene.Cursor)

[<Fact>]
let ``OptionsScene navigation returns Stay`` () =
    let scene = defaultScene ()
    Assert.Equal(Stay, pressDown scene)
    Assert.Equal(Stay, pressUp   scene)

// ── OptionsScene Left/Right change values ────────────────────────────────────

[<Fact>]
let ``OptionsScene Right on TEXT SPEED increments TextSpeed`` () =
    let scene = defaultScene ()
    // default TextSpeed = 2 (MID); Right should go to 3 (FAST)
    pressRight scene |> ignore
    Assert.Equal(3, scene.CurrentOptions.TextSpeed)

[<Fact>]
let ``OptionsScene Left on TEXT SPEED decrements TextSpeed`` () =
    let scene = defaultScene ()
    // default TextSpeed = 2 (MID); Left should go to 1 (SLOW)
    pressLeft scene |> ignore
    Assert.Equal(1, scene.CurrentOptions.TextSpeed)

[<Fact>]
let ``OptionsScene Right on TEXT SPEED wraps from FAST (3) back to SLOW (1)`` () =
    let scene = defaultScene ()
    // Move to 3 (FAST) first
    pressRight scene |> ignore   // 2→3
    pressRight scene |> ignore   // 3→1 (wrap)
    Assert.Equal(1, scene.CurrentOptions.TextSpeed)

[<Fact>]
let ``OptionsScene Right on TEXT FRAME increments BoxBorder`` () =
    let scene = defaultScene ()
    pressDown scene |> ignore  // cursor → row 1 (TEXT FRAME)
    pressRight scene |> ignore
    Assert.Equal(1, scene.CurrentOptions.BoxBorder)

[<Fact>]
let ``OptionsScene Right on SOUND cycles from MONO to STEREO`` () =
    let scene = defaultScene ()
    pressDown scene |> ignore  // row 1
    pressDown scene |> ignore  // row 2 (SOUND)
    pressRight scene |> ignore
    Assert.Equal(1, scene.CurrentOptions.Sound)

[<Fact>]
let ``OptionsScene Right then Left returns value to original`` () =
    let scene = defaultScene ()
    pressRight scene |> ignore   // TextSpeed 2→3
    pressLeft  scene |> ignore   // TextSpeed 3→2
    Assert.Equal(2, scene.CurrentOptions.TextSpeed)

[<Fact>]
let ``OptionsScene Left/Right return Stay`` () =
    let scene = defaultScene ()
    Assert.Equal(Stay, pressLeft  scene)
    Assert.Equal(Stay, pressRight scene)

// ── OptionsScene B commits via onChange ──────────────────────────────────────

[<Fact>]
let ``OptionsScene B calls onChange with updated PlayerState`` () =
    let mutable committed: PlayerState option = None
    let scene = OptionsScene(Content(), PlayerStateOps.initial, fun p -> committed <- Some p)
    pressRight scene |> ignore    // change TextSpeed 2→3
    pressB scene |> ignore
    Assert.True(committed.IsSome)
    Assert.Equal(3, committed.Value.Options.TextSpeed)

[<Fact>]
let ``OptionsScene B returns Pop`` () =
    let scene = defaultScene ()
    Assert.Equal(Pop, pressB scene)

[<Fact>]
let ``OptionsScene Start returns Pop and commits`` () =
    let mutable committed = false
    let scene = OptionsScene(Content(), PlayerStateOps.initial, fun _ -> committed <- true)
    Assert.Equal(Pop, pressStart scene)
    Assert.True(committed)

[<Fact>]
let ``OptionsScene B with no changes commits original options`` () =
    let mutable committed: PlayerState option = None
    let scene = OptionsScene(Content(), PlayerStateOps.initial, fun p -> committed <- Some p)
    pressB scene |> ignore
    Assert.True(committed.IsSome)
    Assert.Equal(PlayerStateOps.initial.Options, committed.Value.Options)

// ── OptionsScene Render: non-zero pixels in box area ─────────────────────────

[<Fact>]
let ``OptionsScene Render draws non-zero pixels at box border`` () =
    let fb = Framebuffer()
    let scene = defaultScene ()
    (scene :> Scene).Render(fb)

    // Box starts at tile (Left=0, Top=6); pixel (0, 48) should have a border glyph.
    let mutable anyNonZero = false

    for py in 48 .. 55 do
        for px in 0 .. 7 do
            let i = (py * Display.Width + px) * 4
            if fb.Pixels.[i] <> 0uy || fb.Pixels.[i + 1] <> 0uy || fb.Pixels.[i + 2] <> 0uy then
                anyNonZero <- true

    Assert.True(anyNonZero, "Expected BoxTopLeft border glyph at tile (0, 6) to write non-zero pixels")
