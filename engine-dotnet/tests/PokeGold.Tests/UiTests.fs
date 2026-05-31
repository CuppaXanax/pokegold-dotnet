module PokeGold.Tests.UiTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Ui

// ── MenuList ──────────────────────────────────────────────────────────────────

[<Fact>]
let ``MenuList create Count=0 yields cursor 0 top 0`` () =
    let ml = MenuList.create 0 5 false
    Assert.Equal(0, ml.Count)
    Assert.Equal(0, ml.Cursor)
    Assert.Equal(0, ml.Top)

[<Fact>]
let ``MenuList create Count<=Visible cursor stays at 0`` () =
    let ml = MenuList.create 3 5 false
    Assert.Equal(3, ml.Count)
    Assert.Equal(0, ml.Cursor)
    Assert.Equal(0, ml.Top)

[<Fact>]
let ``MenuList moveDown increments cursor`` () =
    let ml = MenuList.create 5 5 false |> MenuList.moveDown
    Assert.Equal(1, ml.Cursor)

[<Fact>]
let ``MenuList moveDown clamps at Count-1 when Wrap=false`` () =
    let ml =
        MenuList.create 3 5 false
        |> MenuList.moveDown
        |> MenuList.moveDown
        |> MenuList.moveDown // one past the end
    Assert.Equal(2, ml.Cursor)

[<Fact>]
let ``MenuList moveDown wraps from last to 0 when Wrap=true`` () =
    let ml =
        MenuList.create 3 5 true
        |> MenuList.moveDown
        |> MenuList.moveDown
        |> MenuList.moveDown // wraps
    Assert.Equal(0, ml.Cursor)

[<Fact>]
let ``MenuList moveUp clamps at 0 when Wrap=false`` () =
    let ml = MenuList.create 3 5 false |> MenuList.moveUp
    Assert.Equal(0, ml.Cursor)

[<Fact>]
let ``MenuList moveUp wraps from 0 to Count-1 when Wrap=true`` () =
    let ml = MenuList.create 3 5 true |> MenuList.moveUp
    Assert.Equal(2, ml.Cursor)

[<Fact>]
let ``MenuList operations are no-ops on empty list`` () =
    let ml = MenuList.create 0 5 true
    let ml2 = ml |> MenuList.moveDown |> MenuList.moveUp |> MenuList.moveTo 99
    Assert.Equal(0, ml2.Cursor)
    Assert.Equal(0, ml2.Top)

[<Fact>]
let ``MenuList scroll window keeps cursor visible when scrolling down`` () =
    // visible=2, count=5; scroll all the way down
    let ml =
        MenuList.create 5 2 false
        |> MenuList.moveDown  // cursor=1, visible window [0..1]
        |> MenuList.moveDown  // cursor=2, window must advance
        |> MenuList.moveDown  // cursor=3
        |> MenuList.moveDown  // cursor=4
    Assert.Equal(4, ml.Cursor)
    Assert.True(ml.Cursor >= ml.Top)
    Assert.True(ml.Cursor < ml.Top + ml.Visible)

[<Fact>]
let ``MenuList scroll window keeps cursor visible when wrapping down to top`` () =
    let ml =
        MenuList.create 5 2 true
        |> MenuList.moveDown
        |> MenuList.moveDown
        |> MenuList.moveDown
        |> MenuList.moveDown  // cursor = 4
        |> MenuList.moveDown  // wrap: cursor = 0; top must follow
    Assert.Equal(0, ml.Cursor)
    Assert.Equal(0, ml.Top)

[<Fact>]
let ``MenuList scroll window keeps cursor visible when wrapping up to bottom`` () =
    let ml = MenuList.create 5 2 true |> MenuList.moveUp // wrap: cursor = 4
    Assert.Equal(4, ml.Cursor)
    Assert.True(ml.Cursor >= ml.Top)
    Assert.True(ml.Cursor < ml.Top + ml.Visible)

[<Fact>]
let ``MenuList moveTo clamps out-of-range index`` () =
    let ml = MenuList.create 3 5 false |> MenuList.moveTo 99
    Assert.Equal(2, ml.Cursor)

[<Fact>]
let ``MenuList cursor invariant holds across many moves`` () =
    // Property-style: run 20 alternating moves on a list of 5 items (visible=3,
    // Wrap=true) and assert the invariant every step.
    let mutable ml = MenuList.create 5 3 true
    for i in 1..20 do
        ml <- if i % 3 = 0 then MenuList.moveUp ml else MenuList.moveDown ml
        if ml.Count > 0 then
            Assert.True(ml.Cursor >= 0)
            Assert.True(ml.Cursor < ml.Count)
            Assert.True(ml.Top <= ml.Cursor)
            Assert.True(ml.Cursor < ml.Top + ml.Visible)

// ── HpBar ─────────────────────────────────────────────────────────────────────

[<Fact>]
let ``HpBar fill 0 hp returns 0 pixels`` () =
    Assert.Equal(0, HpBar.fill 0 100)

[<Fact>]
let ``HpBar fill full hp returns BarPx pixels`` () =
    Assert.Equal(HpBar.BarPx, HpBar.fill 100 100)
    Assert.Equal(HpBar.BarPx, HpBar.fill 48 48)

[<Fact>]
let ``HpBar fill 1 hp yields at least 1 pixel`` () =
    Assert.Equal(1, HpBar.fill 1 100)

[<Fact>]
let ``HpBar fill half hp is 24 pixels`` () =
    Assert.Equal(24, HpBar.fill 50 100)

[<Fact>]
let ``HpBar fill maxHp=0 returns 0`` () =
    Assert.Equal(0, HpBar.fill 100 0)
    Assert.Equal(0, HpBar.fill 0 0)

[<Fact>]
let ``HpBar fill negative curHp returns 0`` () =
    Assert.Equal(0, HpBar.fill -1 100)

[<Fact>]
let ``HpBar fill over-full clamps to BarPx`` () =
    Assert.Equal(HpBar.BarPx, HpBar.fill 200 100)

[<Fact>]
let ``HpBar band above 50 percent is Green`` () =
    Assert.Equal(Green, HpBar.band 51 100)
    Assert.Equal(Green, HpBar.band 100 100)

[<Fact>]
let ``HpBar band exactly 50 percent is Yellow`` () =
    Assert.Equal(Yellow, HpBar.band 50 100)

[<Fact>]
let ``HpBar band above 20 but at most 50 percent is Yellow`` () =
    Assert.Equal(Yellow, HpBar.band 21 100)
    Assert.Equal(Yellow, HpBar.band 30 100)

[<Fact>]
let ``HpBar band at or below 20 percent is Red`` () =
    Assert.Equal(Red, HpBar.band 20 100)
    Assert.Equal(Red, HpBar.band 1 100)
    Assert.Equal(Red, HpBar.band 0 100)

[<Fact>]
let ``HpBar band maxHp=0 is Red`` () =
    Assert.Equal(Red, HpBar.band 0 0)
    Assert.Equal(Red, HpBar.band 10 0)

// ── Window.boxGlyph (pure geometry) ───────────────────────────────────────────

[<Fact>]
let ``boxGlyph returns corner glyphs at the four corners`` () =
    Assert.Equal(Charmap.BoxTopLeft,     Window.boxGlyph 6 6 0 0)
    Assert.Equal(Charmap.BoxTopRight,    Window.boxGlyph 6 6 5 0)
    Assert.Equal(Charmap.BoxBottomLeft,  Window.boxGlyph 6 6 0 5)
    Assert.Equal(Charmap.BoxBottomRight, Window.boxGlyph 6 6 5 5)

[<Fact>]
let ``boxGlyph returns horizontal glyphs on top and bottom edges`` () =
    for col in 1..4 do
        Assert.Equal(Charmap.BoxHoriz, Window.boxGlyph 6 6 col 0)
        Assert.Equal(Charmap.BoxHoriz, Window.boxGlyph 6 6 col 5)

[<Fact>]
let ``boxGlyph returns vertical glyphs on left and right edges`` () =
    for row in 1..4 do
        Assert.Equal(Charmap.BoxVert, Window.boxGlyph 6 6 0 row)
        Assert.Equal(Charmap.BoxVert, Window.boxGlyph 6 6 5 row)

[<Fact>]
let ``boxGlyph returns Space for interior tiles`` () =
    for row in 1..4 do
        for col in 1..4 do
            Assert.Equal(Charmap.Space, Window.boxGlyph 6 6 col row)

[<Fact>]
let ``boxGlyph matches YesNoScene box geometry (6 x 6)`` () =
    // Verify the same tile assignments that YesNoScene previously computed inline.
    Assert.Equal(Charmap.BoxTopLeft,    Window.boxGlyph 6 6 0 0)
    Assert.Equal(Charmap.BoxHoriz,      Window.boxGlyph 6 6 2 0)
    Assert.Equal(Charmap.BoxVert,       Window.boxGlyph 6 6 0 2)
    Assert.Equal(Charmap.Space,         Window.boxGlyph 6 6 2 2)
    Assert.Equal(Charmap.BoxBottomRight,Window.boxGlyph 6 6 5 5)

[<Fact>]
let ``boxGlyph matches TextBox geometry (20 x 6)`` () =
    // Verify the same tile assignments that TextRenderer.drawBorder computed.
    Assert.Equal(Charmap.BoxTopLeft,     Window.boxGlyph 20 6 0 0)
    Assert.Equal(Charmap.BoxTopRight,    Window.boxGlyph 20 6 19 0)
    Assert.Equal(Charmap.BoxBottomLeft,  Window.boxGlyph 20 6 0 5)
    Assert.Equal(Charmap.BoxBottomRight, Window.boxGlyph 20 6 19 5)
    Assert.Equal(Charmap.BoxHoriz,       Window.boxGlyph 20 6 10 0)
    Assert.Equal(Charmap.BoxVert,        Window.boxGlyph 20 6 0 3)
    Assert.Equal(Charmap.Space,          Window.boxGlyph 20 6 10 3)
