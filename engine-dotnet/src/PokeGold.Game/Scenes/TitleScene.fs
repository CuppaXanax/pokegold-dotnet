namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// The title screen shown before the overworld starts.
/// Renders the real Ho-Oh sprite and Pokemon Gold logo from the disassembly
/// assets, with a blinking "PRESS START" prompt.
type TitleScene(content: Content, onStart: unit -> Transition) =
    let mutable frame = 0
    let input = EdgeDetector()
    let textPal = TextRenderer.palette

    // Load title assets as tile arrays
    let hoohTiles = Image.loadTiles "gfx/title/hooh_gold.png"
    let logoTopTiles = Image.loadTiles "gfx/title/logo_top_gold.png"
    let logoBotTiles = Image.loadTiles "gfx/title/logo_bottom_gold.png"

    // Ho-Oh palette: gold/brown/dark from the title_fg.pal (palette 1)
    let hoohPal =
        Palette.ofColors
            [ Palette.rgb555 31 31 31   // lightest (white)
              Palette.rgb555 31 31 0    // gold
              Palette.rgb555 26 22 0    // dark gold
              Palette.rgb555 0 0 0 ]    // black

    // Logo palette: blue sky background tones from title_bg_gold.pal
    let logoPal =
        Palette.ofColors
            [ Palette.rgb555 31 31 31   // white
              Palette.rgb555 18 23 31   // light blue
              Palette.rgb555 15 20 31   // mid blue
              Palette.rgb555 0 0 0 ]    // black

    // Background color: the blue sky from the title
    let bgColor = Palette.rgb555 15 20 31

    // Layout constants (160x144 screen)
    // Logo top: 160x24 at y=0
    // Ho-Oh: 88x64 centered at y=24
    // Logo bottom: 160x48 at y=88
    // "PRESS START" text at row 17 (y=136)
    let logoTopY = 0
    let hoohX = (160 - 88) / 2  // centered = 36
    let hoohY = 24
    let logoBotY = 88

    /// Draw a tile array as a grid with given tiles-wide, at pixel (ox, oy).
    let drawTileGrid (fb: Framebuffer) (pal: Palette) (tiles: Tile[]) (tilesWide: int) (ox: int) (oy: int) =
        for i in 0..tiles.Length - 1 do
            let tx = (i % tilesWide) * 8
            let ty = (i / tilesWide) * 8
            Graphics.drawTile fb pal (ox + tx) (oy + ty) tiles.[i]

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

            // Logo top (160px wide = 20 tiles)
            drawTileGrid fb logoPal logoTopTiles 20 0 logoTopY

            // Ho-Oh sprite (88px wide = 11 tiles), centered
            drawTileGrid fb hoohPal hoohTiles 11 hoohX hoohY

            // Logo bottom (160px wide = 20 tiles)
            drawTileGrid fb logoPal logoBotTiles 20 0 logoBotY

            // Blinking "PRESS START"
            let blink = frame % (BlinkFrames * 2) < BlinkFrames
            if blink then
                WindowRenderer.drawString fb content.Font textPal 3 17 "PRESS  START"
