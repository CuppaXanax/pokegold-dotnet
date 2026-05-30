namespace PokeGold.Game.Text

open PokeGold.Game.Core

/// A speech text box that types its text out one glyph at a time, exactly like
/// the GSC overworld box: two visible lines of 18 characters, `<CONT>` scrolls
/// after a prompt, `<PARA>` clears after a prompt, `<PROMPT>`/`<DONE>` finish.
///
/// The state is immutable and advanced one frame at a time by the pure
/// `TextBox.tick`, so the typewriter, scrolling, and prompt-waiting are all
/// testable without a framebuffer.
type TextBoxState =
    { /// Tokens still to be processed.
      Tokens: TextToken list
      /// The two visible text lines, each `InnerW` character codes (space = blank).
      Lines: byte[][]
      /// Current text line (0 = top, 1 = bottom).
      Row: int
      /// Current column within the line.
      Col: int
      /// Frames remaining before the next token is consumed.
      Delay: int
      /// True while waiting for the player to confirm a prompt.
      Waiting: bool
      /// The control action deferred until the prompt is confirmed.
      Pending: TextToken option
      /// Confirm button state last frame (for press-edge detection).
      PrevConfirm: bool
      /// True once the box has finished.
      Done: bool }

module TextBox =

    /// Characters per text line (`TEXTBOX_INNERW`).
    [<Literal>]
    let InnerW = 18

    /// Visible text lines in the box.
    [<Literal>]
    let Lines = 2

    /// Frames between typed glyphs (medium text speed).
    [<Literal>]
    let TypewriterDelay = 3

    let private blankLine () : byte[] = Array.create InnerW Charmap.Space

    /// A fresh box for the given token stream.
    let create (tokens: TextToken list) : TextBoxState =
        { Tokens = tokens
          Lines = [| blankLine (); blankLine () |]
          Row = 0
          Col = 0
          Delay = 0
          Waiting = false
          Pending = None
          PrevConfirm = false
          Done = false }

    /// A fresh box for source text (with embedded `<…>` control tokens).
    let ofString (text: string) : TextBoxState =
        create (TextStream.ofString text)

    let private withGlyph (s: TextBoxState) (code: byte) : TextBoxState =
        let lines = Array.map Array.copy s.Lines

        if s.Col < InnerW then
            lines.[s.Row].[s.Col] <- code

        { s with
            Lines = lines
            Col = min (s.Col + 1) InnerW
            Delay = TypewriterDelay }

    /// Scroll the box up one line: the bottom line becomes the top, the bottom
    /// line is cleared, and the cursor returns to the start of the bottom line.
    let private scrolled (s: TextBoxState) : TextBoxState =
        { s with
            Lines = [| Array.copy s.Lines.[1]; blankLine () |]
            Row = 1
            Col = 0 }

    /// Clear the box and start a new paragraph at the top line.
    let private cleared (s: TextBoxState) : TextBoxState =
        { s with
            Lines = [| blankLine (); blankLine () |]
            Row = 0
            Col = 0 }

    /// Consume the next token from a non-waiting, non-delayed box.
    let private advance (s: TextBoxState) : TextBoxState =
        match s.Tokens with
        | [] -> { s with Done = true }
        | token :: rest ->
            let s = { s with Tokens = rest }

            match token with
            | Glyph c -> withGlyph s c
            | Line -> { s with Row = 1; Col = 0 }
            | Next -> { s with Row = min (s.Row + 1) (Lines - 1); Col = 0 }
            | LineFeed -> { s with Row = min (s.Row + 1) (Lines - 1) }
            | Scroll -> scrolled s
            | Cont -> { s with Waiting = true; Pending = Some Cont }
            | Para -> { s with Waiting = true; Pending = Some Para }
            | Prompt -> { s with Waiting = true; Pending = Some Prompt }
            | Done -> { s with Done = true }

    /// Apply a confirmed prompt's deferred action and resume typing.
    let private applyPending (pending: TextToken option) (s: TextBoxState) : TextBoxState =
        match pending with
        | Some Cont -> scrolled s
        | Some Para -> cleared s
        | Some Prompt -> { s with Done = true }
        | _ -> s

    /// Advance the box by one frame, consuming this frame's input.
    let tick (buttons: Buttons) (s: TextBoxState) : TextBoxState =
        if s.Done then
            s
        else
            let confirm = buttons.A || buttons.B
            let pressed = confirm && not s.PrevConfirm
            let s = { s with PrevConfirm = confirm }

            if s.Waiting then
                if pressed then
                    applyPending s.Pending { s with Waiting = false; Pending = None }
                else
                    s
            elif s.Delay > 0 then
                { s with Delay = s.Delay - 1 }
            else
                advance s
