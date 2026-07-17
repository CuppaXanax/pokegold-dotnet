namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Text
open PokeGold.Game.Render

/// A speech text box layered over whatever scene is beneath it. It advances its
/// typewriter each frame and pops itself off the stack once the text is done.
/// Because `Game` renders the stack bottom-to-top and this scene only draws the
/// bottom six rows, the overworld stays visible above the box.
type TextBoxScene(font: Font, initial: TextBoxState, ?onDone: unit -> Transition) =
    let mutable state = initial
    let onDone = defaultArg onDone (fun () -> Pop)

    /// Open a text box for the given source text (with embedded `<…>` tokens).
    /// When `speed` is given (frames per glyph), the box types at that rate;
    /// otherwise the default `TypewriterDelay` is used.
    static member Of(content: Content, text: string, ?speed: int, ?onDone: unit -> Transition) : TextBoxScene =
        match speed with
        | Some s -> TextBoxScene(content.Font, TextBox.ofStringWithSpeed s text, ?onDone = onDone)
        | None   -> TextBoxScene(content.Font, TextBox.ofString text, ?onDone = onDone)

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            state <- TextBox.tick buttons state
            if state.Done then onDone() else Stay

        member _.Render(fb: Framebuffer) = TextRenderer.draw fb font state
