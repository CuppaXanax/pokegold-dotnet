namespace PokeGold.Game

/// A single RGBA color, byte-per-channel, matching the framebuffer's R,G,B,A
/// memory order.
[<Struct>]
type Rgba =
    { R: byte
      G: byte
      B: byte
      A: byte }

/// A Game Boy Color palette: an ordered list of colors indexed by a tile's
/// 2-bit pixel values (0..3 for standard tiles).
type Palette =
    { Colors: Rgba[] }

/// Palette construction and parsing.
///
/// Game Boy Color stores each channel as 5 bits (0..31, "RGB555"). We expand to
/// 8-bit with the standard bit-replication scale; a perceptual color curve can
/// be layered on later if needed.
module Palette =

    let inline private expand5 (v: int) : byte =
        let c = v &&& 0x1F
        byte ((c <<< 3) ||| (c >>> 2))

    /// Build an opaque RGBA color from 5-bit GBC channels (0..31).
    let rgb555 (r: int) (g: int) (b: int) : Rgba =
        { R = expand5 r; G = expand5 g; B = expand5 b; A = 255uy }

    /// Build a palette from a sequence of RGBA colors.
    let ofColors (colors: Rgba seq) : Palette = { Colors = Array.ofSeq colors }

    /// Parse a pret `.pal` text palette. Each color is a line `RGB r, g, b` with
    /// 0..31 channels; `;` begins a comment. Multiple colors accumulate in order.
    let parse (text: string) : Palette =
        let colors =
            text.Split([| '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.choose (fun raw ->
                let line =
                    let i = raw.IndexOf ';'
                    if i >= 0 then raw.Substring(0, i) else raw

                let t = line.Trim()

                if t.StartsWith("RGB", System.StringComparison.OrdinalIgnoreCase) then
                    let nums =
                        t.Substring(3).Split(
                            [| ','; ' '; '\t' |],
                            System.StringSplitOptions.RemoveEmptyEntries
                        )
                        |> Array.map (fun s -> int (s.Trim()))

                    if nums.Length >= 3 then Some(rgb555 nums.[0] nums.[1] nums.[2]) else None
                else
                    None)

        { Colors = colors }

    /// Parse a repo-relative `.pal` file.
    let load (relative: string) : Palette = parse (Assets.readText relative)
