namespace PokeGold.Game.Text

open PokeGold.Game.Core

/// One unit of a decoded text stream: either a glyph to type or a control action.
/// This is the high-level re-expression of the GSC text dictionary (`CheckDict`
/// in `home/text.asm`) for the subset that overworld text uses.
type TextToken =
    /// A printable glyph (character code ≥ $60) to type into the box.
    | Glyph of byte
    /// `<LINE>` ($4f): jump to the start of the bottom text line.
    | Line
    /// `<NEXT>` ($4e): move to the start of the next text line.
    | Next
    /// `<LF>` ($22): move down one row, same column start.
    | LineFeed
    /// `<CONT>` ($55): prompt, then scroll up and continue on the bottom line.
    | Cont
    /// `<SCROLL>` ($4c): scroll up and continue, without a prompt.
    | Scroll
    /// `<PARA>` ($51): prompt, then clear the box and start a new paragraph.
    | Para
    /// `<PROMPT>` ($58): prompt, then end the text box.
    | Prompt
    /// `<DONE>` ($57) or the `@` terminator: end the text box.
    | Done

/// Decoding a raw character-code stream into tokens.
module TextStream =

    /// Static dictionary expansions (`CheckDict`'s `print_name` entries in
    /// `home/text.asm`) for control codes below $60 that place a fixed glyph
    /// string rather than a single tile. The text engine substitutes these at
    /// print time, e.g. `#` ($54) expands to "POKé". The expansion is itself
    /// charmap-encoded so it resolves to real glyph tiles (P, O, K, é, …).
    /// Name placeholders (<PLAYER>, <RIVAL>, <MOM>, …) depend on save data and
    /// are handled by the caller, not here.
    let private dictExpansions: Map<byte, byte[]> =
        [ 0x24uy, "<PO><KE>" // <POKE>
          0x4auy, "<PK><MN>" // <PKMN>
          0x54uy, "POKé" // '#'
          0x56uy, "……" // <……>
          0x5buy, "PC" // <PC>
          0x5cuy, "TM" // <TM>
          0x5duy, "TRAINER" // <TRAINER>
          0x5euy, "ROCKET" ] // <ROCKET>
        |> List.map (fun (c, s) -> c, Charmap.encode s)
        |> Map.ofList

    /// Decode a code stream into tokens, stopping at the first terminator/`<DONE>`
    /// (which is emitted as a final `Done`). Glyph codes (≥ $60) pass through;
    /// recognized control codes map to their action; static dictionary codes are
    /// expanded into their glyph runs; any other control code is ignored (those are
    /// higher-level substitutions handled in later milestones).
    let decode (codes: byte[]) : TextToken list =
        let rec loop i acc =
            if i >= codes.Length then
                List.rev (Done :: acc)
            else
                let c = codes.[i]

                if c = Charmap.Terminator || c = Charmap.Done then
                    List.rev (Done :: acc)
                elif c >= 0x60uy then
                    loop (i + 1) (Glyph c :: acc)
                else
                    match Map.tryFind c dictExpansions with
                    | Some expansion ->
                        // Splice the expansion's glyphs in place of the dict code.
                        let glyphs = expansion |> Array.toList |> List.map Glyph |> List.rev
                        loop (i + 1) (glyphs @ acc)
                    | None ->

                    let token =
                        if c = Charmap.Line then Some Line
                        elif c = Charmap.Next then Some Next
                        elif c = Charmap.LineFeed then Some LineFeed
                        elif c = Charmap.Cont then Some Cont
                        elif c = Charmap.Scroll then Some Scroll
                        elif c = Charmap.Para then Some Para
                        elif c = Charmap.Prompt then Some Prompt
                        else None

                    match token with
                    | Some t -> loop (i + 1) (t :: acc)
                    | None -> loop (i + 1) acc

        loop 0 []

    /// Encode source text (with embedded `<…>` tokens) and decode it to tokens.
    let ofString (text: string) : TextToken list = decode (Charmap.encode text)
