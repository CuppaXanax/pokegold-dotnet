namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// The title screen shown before the overworld starts.
/// Renders the real Ho-Oh sprite and Pokemon Gold logo from the disassembly
/// assets using the logo.tilemap for correct tile arrangement.
type TitleScene(content: Content, onStart: unit -> Transition) =
    let mutable frame = 0
    let input = EdgeDetector()
    let textPal = TextRenderer.palette

    // Load tile sheets (NOT pre-composed images — raw tile data)
    let hoohTiles = Image.loadTiles "gfx/title/hooh_gold.png"
    let logoTopTiles = Image.loadTiles "gfx/title/logo_top_gold.png"
    let logoBotTiles = Image.loadTiles "gfx/title/logo_bottom_gold.png"

    // The disassembly copies logo.tilemap directly to vBGMap0 until $ff.  It is a
    // 32×18 Game Boy BG map, not a packed 20×18 visible-screen map.
    let tilemap = Assets.readBytes "gfx/title/logo.tilemap"

    // Ho-Oh palette: gold tones from title_fg.pal palette 1
    let hoohPal =
        Palette.ofColors
            [ Palette.rgb555 31 31 31   // 0: transparent (skipped in sprite draw)
              Palette.rgb555 31 31 0    // 1: gold
              Palette.rgb555 26 22 0    // 2: dark gold
              Palette.rgb555 0 0 0 ]    // 3: black

    let bgPalettes =
        [| Palette.ofColors
               [ Palette.rgb555 31 31 31
                 Palette.rgb555 18 23 31
                 Palette.rgb555 15 20 31
                 Palette.rgb555 0 0 0 ]
           Palette.ofColors
               [ Palette.rgb555 31 21 0
                 Palette.rgb555 12 14 12
                 Palette.rgb555 15 20 31
                 Palette.rgb555 0 0 17 ]
           Palette.ofColors
               [ Palette.rgb555 31 31 31
                 Palette.rgb555 31 0 0
                 Palette.rgb555 15 20 31
                 Palette.rgb555 0 0 0 ]
           Palette.ofColors
               [ Palette.rgb555 31 31 31
                 Palette.rgb555 29 25 0
                 Palette.rgb555 15 20 31
                 Palette.rgb555 17 10 1 ]
           Palette.ofColors
               [ Palette.rgb555 31 31 31
                 Palette.rgb555 23 26 31
                 Palette.rgb555 18 23 31
                 Palette.rgb555 0 0 0 ] |]

    let bgColor = Palette.rgb555 15 20 31

    [<Literal>]
    let BgMapWidth = 32

    let tilemapLength =
        match tilemap |> Array.tryFindIndex ((=) 0xffuy) with
        | Some i -> i
        | None -> tilemap.Length

    // OAM data for the Gold title Ho-Oh.  It is an 8×16 OBJ animation at
    // depixel 12,11; the PNG is raw OBJ tile memory, not a pre-composed image.
    let hoohOamX = 12 * 8
    let hoohOamY = 11 * 8

    let hoohFrames =
        [| [| (-4, -1, 0x00); (-3, -2, 0x02); (-3, 0, 0x04); (-2, -3, 0x06); (-2, -1, 0x08); (-2, 1, 0x0a); (-1, -3, 0x0c); (-1, -1, 0x0e); (-1, 1, 0x10); (0, -3, 0x12); (0, -1, 0x14); (0, 1, 0x16); (1, -3, 0x18); (1, -1, 0x1a); (1, 1, 0x1c); (2, -1, 0x1e); (2, 1, 0x20); (3, -2, 0x22); (3, 0, 0x24) |]
           [| (-4, -1, 0x00); (-3, -2, 0x02); (-3, 0, 0x04); (-2, -1, 0x26); (-2, 1, 0x0a); (-1, -3, 0x28); (-1, -1, 0x2a); (-1, 1, 0x10); (0, -1, 0x2c); (0, 1, 0x16); (1, -1, 0x30); (1, 1, 0x1c); (2, -1, 0x1e); (2, 1, 0x20); (3, -2, 0x22); (3, 0, 0x24) |]
           [| (-4, -1, 0x00); (-3, -2, 0x02); (-3, 0, 0x32); (-2, -1, 0x34); (-2, 1, 0x36); (-1, -1, 0x38); (-1, 1, 0x3a); (0, -1, 0x3c); (0, 1, 0x3e); (1, -1, 0x30); (1, 1, 0x1c); (2, -1, 0x1e); (2, 1, 0x20); (3, -2, 0x22); (3, 0, 0x24) |]
           [| (-4, -1, 0x00); (-3, -2, 0x02); (-3, 0, 0x04); (-2, -1, 0x40); (-2, 1, 0x42); (-2, 3, 0x44); (-1, -1, 0x46); (-1, 1, 0x48); (-1, 3, 0x4a); (0, -1, 0x4c); (0, 1, 0x4e); (1, -1, 0x30); (1, 1, 0x1c); (2, -1, 0x1e); (2, 1, 0x20); (3, -2, 0x22); (3, 0, 0x24) |]
           [| (-4, -1, 0x00); (-3, -2, 0x02); (-3, 0, 0x04); (-2, -1, 0x50); (-2, 1, 0x0a); (-1, -3, 0x52); (-1, -1, 0x54); (-1, 1, 0x10); (0, -3, 0x56); (0, -1, 0x2e); (0, 1, 0x16); (1, -1, 0x30); (1, 1, 0x1c); (2, -1, 0x1e); (2, 1, 0x20); (3, -2, 0x22); (3, 0, 0x24) |] |]

    let hoohFrameDurations = [| 10; 9; 10; 10; 9; 10 |]
    let hoohFrameOrder = [| 0; 1; 2; 3; 2; 4 |]

    /// Resolve signed BG tile IDs as used by LCDC's $8800 tile-data mode.
    /// $00-$7f address vTiles2 (logo_bottom); $80-$ff address vTiles1 (logo_top).
    let resolveTile (id: int) : Tile option =
        if id >= 128 && id - 128 < logoTopTiles.Length then Some logoTopTiles.[id - 128]
        elif id >= 0 && id < logoBotTiles.Length then Some logoBotTiles.[id]
        else None

    let paletteForTile (col: int) (row: int) =
        let pal =
            if row >= 12 && row < 17 then 4
            elif row = 6 && col >= 5 && col < 15 then 3
            elif row < 7 then 1
            else 0

        bgPalettes.[pal]

    let drawTileTransparent (fb: Framebuffer) (palette: Palette) (x: int) (y: int) (tile: Tile) =
        for row in 0..7 do
            for col in 0..7 do
                let idx = int tile.Pixels.[row * 8 + col]
                if idx > 0 && idx < palette.Colors.Length then
                    let c = palette.Colors.[idx]
                    fb.SetPixel(x + col, y + row, c.R, c.G, c.B, c.A)

    let drawHoOhSprite (fb: Framebuffer) =
        let cycleLength = hoohFrameDurations |> Array.sum
        let cycleFrame = frame % cycleLength
        let mutable acc = 0
        let mutable frameIndex = 0

        while frameIndex < hoohFrameDurations.Length - 1 && cycleFrame >= acc + hoohFrameDurations.[frameIndex] do
            acc <- acc + hoohFrameDurations.[frameIndex]
            frameIndex <- frameIndex + 1

        let spriteFrame = hoohFrames.[hoohFrameOrder.[frameIndex]]

        for xTile, yTile, tileId in spriteFrame do
            let x = hoohOamX + xTile * 8 - 8
            let y = hoohOamY + yTile * 8 - 16

            if tileId + 1 < hoohTiles.Length then
                drawTileTransparent fb hoohPal x y hoohTiles.[tileId]
                drawTileTransparent fb hoohPal x (y + 8) hoohTiles.[tileId + 1]

    [<Literal>]
    let BlinkFrames = 30

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)
            frame <- frame + 1
            if edges.A || edges.Start then onStart()
            else Stay

        member _.Render(fb: Framebuffer) =
            // Fill background with blue sky
            for y in 0..Display.Height - 1 do
                for x in 0..Display.Width - 1 do
                    fb.SetPixel(x, y, bgColor.R, bgColor.G, bgColor.B, bgColor.A)

            // Draw the visible 20 columns out of the 32-column hardware BG map.
            let rows = min 18 (tilemapLength / BgMapWidth)
            for r in 0..rows - 1 do
                for c in 0..19 do
                    let idx = r * BgMapWidth + c
                    if idx < tilemapLength then
                        let tileId = int tilemap.[idx]
                        match resolveTile tileId with
                        | Some tile -> Graphics.drawTile fb (paletteForTile c r) (c * 8) (r * 8) tile
                        | None -> ()

            drawHoOhSprite fb

            // Blinking "PRESS START"
            let blink = frame % (BlinkFrames * 2) < BlinkFrames
            if blink then
                WindowRenderer.drawString fb content.Font textPal 3 17 "PRESS  START"
