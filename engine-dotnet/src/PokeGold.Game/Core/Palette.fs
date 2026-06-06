namespace PokeGold.Game.Core

open System.Text.RegularExpressions

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

    let private stripComment (raw: string) : string =
        let i = raw.IndexOf ';'
        if i >= 0 then raw.Substring(0, i) else raw

    let private parseRgbValues (line: string) : int[] =
        let t = line.Trim()

        if not (t.StartsWith("RGB", System.StringComparison.OrdinalIgnoreCase)) then
            [||]
        else
            Regex.Matches(t, "\d+")
            |> Seq.cast<Match>
            |> Seq.map (fun m -> int m.Value)
            |> Seq.toArray

    /// Parse a pret `.pal` text palette. Each color is a line `RGB r, g, b` with
    /// 0..31 channels; `;` begins a comment. Multiple colors accumulate in order.
    let parse (text: string) : Palette =
        let colors =
            text.Split([| '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.collect (fun raw ->
                let line = stripComment raw
                let nums = parseRgbValues line

                if nums.Length >= 3 then
                    nums
                    |> Array.chunkBySize 3
                    |> Array.choose (fun triplet ->
                        if triplet.Length = 3 then
                            Some(rgb555 triplet.[0] triplet.[1] triplet.[2])
                        else
                            None)
                else
                    [||])

        { Colors = colors }

    /// Parse a `.pal` text file into an array of CGB palette banks.
    /// Each bank is a group of 4 RGB colors; the repo's `.pal` assets usually
    /// write 4 colors on each `RGB ...` line, but this also accepts the older
    /// 4-line-per-palette format described in the disassembly notes.
    let parsePalBank (text: string) : Palette[] =
        let mutable palettes = ResizeArray<Palette>()
        let mutable current = ResizeArray<Rgba>()

        for raw in text.Split([| '\n'; '\r' |], System.StringSplitOptions.None) do
            let line = stripComment raw

            if System.String.IsNullOrWhiteSpace line then
                if current.Count > 0 then
                    palettes.Add({ Colors = current.ToArray() })
                    current.Clear()
            else
                let nums = parseRgbValues line

                if nums.Length >= 3 then
                    let colors =
                        nums
                        |> Array.chunkBySize 3
                        |> Array.choose (fun triplet ->
                            if triplet.Length = 3 then
                                Some(rgb555 triplet.[0] triplet.[1] triplet.[2])
                            else
                                None)

                    for color in colors do
                        current.Add color
                        if current.Count = 4 then
                            palettes.Add({ Colors = current.ToArray() })
                            current.Clear()

        if current.Count > 0 then
            palettes.Add({ Colors = current.ToArray() })

        palettes.ToArray()

    /// Parse a repo-relative `.pal` file.
    let load (relative: string) : Palette = parse (Assets.readText relative)
