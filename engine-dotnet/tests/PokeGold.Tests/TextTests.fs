module PokeGold.Tests.TextTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Text

// The text engine is a pure state machine: TextBox.tick advances an immutable
// TextBoxState one frame at a time. These tests drive it directly — no font or
// framebuffer needed.

let private code (s: string) = (Charmap.encode s).[0]

[<Fact>]
let ``charmap encodes letters, digits, space and control tokens`` () =
    Assert.Equal<byte[]>([| 0x80uy; 0x81uy |], Charmap.encode "AB") // A=$80, B=$81
    Assert.Equal<byte[]>([| 0xa0uy |], Charmap.encode "a") // a=$a0
    Assert.Equal<byte[]>([| 0xf6uy |], Charmap.encode "0") // 0=$f6
    Assert.Equal<byte[]>([| Charmap.Space |], Charmap.encode " ")
    Assert.Equal<byte[]>([| Charmap.Line |], Charmap.encode "<LINE>")
    Assert.Equal<byte[]>([| Charmap.Done |], Charmap.encode "<DONE>")

[<Fact>]
let ``charmap greedily prefers multi-character tokens`` () =
    // "'s" is a single code ($d4), not an apostrophe glyph followed by 's'.
    Assert.Equal<byte[]>([| 0xd4uy |], Charmap.encode "'s")

[<Fact>]
let ``decode maps control codes and stops at the terminator`` () =
    let tokens = TextStream.ofString "AB<LINE>C<DONE>"
    Assert.Equal<TextToken list>([ Glyph 0x80uy; Glyph 0x81uy; Line; Glyph 0x82uy; Done ], tokens)

[<Fact>]
let ``decode stops at the terminator code, ignoring trailing bytes`` () =
    let tokens = TextStream.decode (Charmap.encode "A@B")
    Assert.Equal<TextToken list>([ Glyph 0x80uy; Done ], tokens)

let private none = Buttons.none
let private pressA = { Buttons.none with A = true }

/// Run `n` frames with no input.
let private idle n box =
    let mutable s = box
    for _ in 1..n do
        s <- TextBox.tick none s
    s

[<Fact>]
let ``the typewriter reveals one glyph per delay period`` () =
    let box = TextBox.ofString "AB<DONE>"

    // First tick types 'A'; 'B' is not shown until the delay elapses.
    let afterA = TextBox.tick none box
    Assert.Equal(code "A", afterA.Lines.[0].[0])
    Assert.Equal(Charmap.Space, afterA.Lines.[0].[1])

    // 'A' placed + TypewriterDelay frames, then 'B' is typed.
    let afterB = idle (TextBox.TypewriterDelay + 2) box
    Assert.Equal(code "B", afterB.Lines.[0].[1])

[<Fact>]
let ``Line moves typing to the bottom text line`` () =
    let s = idle 50 (TextBox.ofString "A<LINE>B<DONE>")
    Assert.Equal(code "A", s.Lines.[0].[0])
    Assert.Equal(code "B", s.Lines.[1].[0])

[<Fact>]
let ``Cont waits for a button, then scrolls and continues`` () =
    // Type the first line and reach the <CONT> prompt.
    let waiting = idle 30 (TextBox.ofString "A<LINE>B<CONT>C<DONE>")
    Assert.True(waiting.Waiting)
    Assert.False(waiting.Done)
    // 'C' has not been typed yet (still waiting on the prompt).
    Assert.Equal(Charmap.Space, waiting.Lines.[1].[1])

    // Confirm: the bottom line scrolls to the top, then 'C' types on the bottom.
    let resumed = idle 10 (TextBox.tick pressA waiting)
    Assert.Equal(code "B", resumed.Lines.[0].[0]) // old bottom line scrolled up
    Assert.Equal(code "C", resumed.Lines.[1].[0]) // new text on the bottom line

[<Fact>]
let ``Para waits, then clears the box for a new paragraph`` () =
    let waiting = idle 30 (TextBox.ofString "AC<PARA>B<DONE>")
    Assert.True(waiting.Waiting)

    let resumed = idle 10 (TextBox.tick pressA waiting)
    Assert.Equal(code "B", resumed.Lines.[0].[0]) // new paragraph starts at the top
    Assert.Equal(Charmap.Space, resumed.Lines.[0].[1]) // the previous 'C' was cleared

[<Fact>]
let ``a box without prompts finishes on its own`` () =
    let s = idle 30 (TextBox.ofString "Hi<DONE>")
    Assert.True(s.Done)

[<Fact>]
let ``the real Azalea dialogue runs to completion`` () =
    // Drive the full sample: press A only while waiting (release between prompts
    // creates the button edge), and assert it terminates. Uses real resolved
    // in-game dialogue (multiple <PARA> page breaks) via the M9.4 text resolver.
    let text = PokeGold.Game.Overworld.Script.MapText.parseFile "maps/AzaleaTown.asm"
    let mutable s = TextBox.ofString text.["AzaleaTownGrampsTextBefore"]
    let mutable f = 0

    while not s.Done && f < 2000 do
        let b = if s.Waiting then pressA else none
        s <- TextBox.tick b s
        f <- f + 1

    Assert.True(s.Done)
