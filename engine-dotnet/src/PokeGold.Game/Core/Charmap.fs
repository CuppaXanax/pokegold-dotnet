namespace PokeGold.Game.Core

open System.Text.RegularExpressions

/// The game's character map: how source text maps to Game Boy tile codes.
///
/// `constants/charmap.asm` is a list of `charmap "<token>", $code` lines. rgbds
/// itself encodes string literals by greedy longest-token matching, so a literal
/// like `"POKé"` becomes the single code `#` ($54) and control tokens like
/// `"<LINE>"` become one byte. We reproduce that here so our F# text literals —
/// including embedded `<LINE>`, `<CONT>`, `<PARA>`, `@`, … — encode to exactly
/// the byte stream the original ROM would store.
///
/// Only the first mapping for each token is kept; the English entries precede the
/// Japanese reassignments in the file, so the English set wins.
module Charmap =

    /// Control / terminator codes referenced by the text engine.
    [<Literal>]
    let Terminator = 0x50uy // '@'

    [<Literal>]
    let Line = 0x4Fuy // '<LINE>'

    [<Literal>]
    let Next = 0x4Euy // '<NEXT>'

    [<Literal>]
    let LineFeed = 0x22uy // '<LF>'

    [<Literal>]
    let Scroll = 0x4Cuy // '<SCROLL>'

    [<Literal>]
    let Cont = 0x55uy // '<CONT>'

    [<Literal>]
    let Para = 0x51uy // '<PARA>'

    [<Literal>]
    let Done = 0x57uy // '<DONE>'

    [<Literal>]
    let Prompt = 0x58uy // '<PROMPT>'

    [<Literal>]
    let Space = 0x7Fuy // ' '

    // Box-drawing glyphs (gfx/font/font_extra.png), used to draw the speech box.
    [<Literal>]
    let BoxTopLeft = 0x79uy // '┌'

    [<Literal>]
    let BoxHoriz = 0x7Auy // '─'

    [<Literal>]
    let BoxTopRight = 0x7Buy // '┐'

    [<Literal>]
    let BoxVert = 0x7Cuy // '│'

    [<Literal>]
    let BoxBottomLeft = 0x7Duy // '└'

    [<Literal>]
    let BoxBottomRight = 0x7Euy // '┘'

    let private rx = Regex("^\\s*charmap\\s+\"(.*)\",\\s*\\$([0-9a-fA-F]+)")

    /// Parse `charmap.asm` into a token→code map (first mapping per token wins),
    /// and the tokens sorted longest-first for greedy matching.
    let private parse (text: string) : Map<string, byte> * string[] =
        let map =
            text.Split([| '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
            |> Array.fold
                (fun acc raw ->
                    let m = rx.Match raw

                    if m.Success then
                        let token = m.Groups.[1].Value
                        let code = byte (System.Convert.ToInt32(m.Groups.[2].Value, 16))
                        if Map.containsKey token acc then acc else Map.add token code acc
                    else
                        acc)
                Map.empty

        let tokens =
            map |> Map.toArray |> Array.map fst |> Array.sortByDescending String.length

        map, tokens

    let private loaded = lazy (parse (Assets.readText "constants/charmap.asm"))

    /// The full token→code table.
    let table () : Map<string, byte> = fst loaded.Value

    /// Encode source text into Game Boy character codes by greedy longest-token
    /// matching, exactly as rgbds would. Unknown characters are skipped.
    let encode (text: string) : byte[] =
        let map, tokens = loaded.Value
        let result = System.Collections.Generic.List<byte>()
        let mutable i = 0

        while i < text.Length do
            let matched =
                tokens
                |> Array.tryFind (fun t -> t.Length > 0 && i + t.Length <= text.Length && text.Substring(i, t.Length) = t)

            match matched with
            | Some t ->
                result.Add(map.[t])
                i <- i + t.Length
            | None ->
                // No token matches here; skip this character so encoding is total.
                i <- i + 1

        result.ToArray()
