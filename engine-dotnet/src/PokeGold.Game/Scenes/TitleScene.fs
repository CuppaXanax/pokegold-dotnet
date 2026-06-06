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

    // Load the tilemap that arranges logo tiles on screen (20 tiles wide × 18 rows)
    let tilemap = Assets.readBytes "gfx/title/logo.tilemap"

    // Ho-Oh palette: gold tones from title_fg.pal palette 1
    let hoohPal =
        Palette.ofColors
            [ Palette.rgb555 31 31 31   // 0: transparent (skipped in sprite draw)
              Palette.rgb555 31 31 0    // 1: gold
              Palette.rgb555 26 22 0    // 2: dark gold
              Palette.rgb555 0 0 0 ]    // 3: black

    // Logo palette from title_bg_gold.pal
    let logoPal =
        Palette.ofColors
            [ Palette.rgb555 31 31 31   // white
              Palette.rgb555 18 23 31   // light blue
              Palette.rgb555 15 20 31   // mid blue
              Palette.rgb555 0 0 0 ]    // black

    let bgColor = Palette.rgb555 15 20 31

    // Ho-Oh dimensions: 88px wide = 11 tiles, centered on 160px screen
    let hoohTilesWide = 11
    let hoohX = (160 - 88) / 2  // 36
    let hoohY = 40              // positioned below logo top rows

    /// Resolve a tilemap ID to a tile from the appropriate sheet.
    /// IDs 0-79: logo_bottom tiles; 80: blank; 128-187: logo_top tiles
    let resolveTile (id: int) : Tile option =
        if id = 80 then None  // blank
        elif id >= 128 && id - 128 < logoTopTiles.Length then Some logoTopTiles.[id - 128]
        elif id >= 0 && id < logoBotTiles.Length then Some logoBotTiles.[id]
        else None

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

            // Draw Ho-Oh as a sprite (index 0 = transparent) FIRST (behind logo)
            for i in 0..hoohTiles.Length - 1 do
                let tx = (i % hoohTilesWide) * 8
                let ty = (i / hoohTilesWide) * 8
                let tile = hoohTiles.[i]
                for row in 0..7 do
                    for col in 0..7 do
                        let idx = int tile.Pixels.[row * 8 + col]
                        if idx > 0 && idx < hoohPal.Colors.Length then
                            let c = hoohPal.Colors.[idx]
                            fb.SetPixel(hoohX + tx + col, hoohY + ty + row, c.R, c.G, c.B, c.A)

            // Draw the logo using the tilemap (20 tiles wide, 18 rows visible)
            let rows = min 18 (tilemap.Length / 20)
            for r in 0..rows - 1 do
                for c in 0..19 do
                    let idx = r * 20 + c
                    if idx < tilemap.Length then
                        let tileId = int tilemap.[idx]
                        match resolveTile tileId with
                        | Some tile -> Graphics.drawTile fb logoPal (c * 8) (r * 8) tile
                        | None -> ()  // blank tile — sky shows through

            // Blinking "PRESS START"
            let blink = frame % (BlinkFrames * 2) < BlinkFrames
            if blink then
                WindowRenderer.drawString fb content.Font textPal 3 17 "PRESS  START"
