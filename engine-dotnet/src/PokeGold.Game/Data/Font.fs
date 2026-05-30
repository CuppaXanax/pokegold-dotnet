namespace PokeGold.Game.Data

open PokeGold.Game.Core

/// The text font: 8×8 glyph tiles indexed directly by Game Boy character code.
///
/// The ROM loads glyphs into VRAM so that a character's code is its tile id, so
/// our `Font.Glyphs.[code]` is the tile for that code. Two PNGs supply the
/// printable range used by English text:
///   * `gfx/font/font_extra.png` — codes $60–$7f (quotes, ellipsis, the box-
///     drawing border tiles, `PO`/`KE`, and space at $7f).
///   * `gfx/font/font.png`       — codes $80–$ff (A–Z, a–z, punctuation, digits).
/// Codes below $60 are control codes with no glyph; they map to a blank tile.
type Font =
    { /// 256 glyph tiles indexed by character code; unused codes are blank.
      Glyphs: Tile[] }

module Font =

    [<Literal>]
    let private ExtraBase = 0x60

    [<Literal>]
    let private MainBase = 0x80

    let private blank: Tile =
        { Pixels = Array.zeroCreate<byte> Tile.PixelCount }

    /// Load the text font from the two font PNGs.
    let load () : Font =
        let extra = Image.loadTiles "gfx/font/font_extra.png" // $60-$7f
        let main = Image.loadTiles "gfx/font/font.png" // $80-$ff
        let glyphs = Array.create 256 blank

        extra |> Array.iteri (fun i t -> glyphs.[ExtraBase + i] <- t)
        main |> Array.iteri (fun i t -> glyphs.[MainBase + i] <- t)

        { Glyphs = glyphs }

    /// The glyph tile for a character code.
    let glyph (font: Font) (code: byte) : Tile = font.Glyphs.[int code]
