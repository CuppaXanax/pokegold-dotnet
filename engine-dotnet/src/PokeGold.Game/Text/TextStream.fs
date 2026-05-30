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

    /// Decode a code stream into tokens, stopping at the first terminator/`<DONE>`
    /// (which is emitted as a final `Done`). Glyph codes (≥ $60) pass through;
    /// recognized control codes map to their action; any other control code is
    /// ignored (those are higher-level substitutions handled in later milestones).
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
