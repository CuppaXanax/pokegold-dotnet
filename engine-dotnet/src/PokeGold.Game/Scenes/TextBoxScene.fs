namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Text
open PokeGold.Game.Render

/// A speech text box layered over whatever scene is beneath it. It advances its
/// typewriter each frame and pops itself off the stack once the text is done.
/// Because `Game` renders the stack bottom-to-top and this scene only draws the
/// bottom six rows, the overworld stays visible above the box.
type TextBoxScene(font: Font, initial: TextBoxState) =
    let mutable state = initial

    /// Open a text box for the given source text (with embedded `<…>` tokens).
    static member Of(content: Content, text: string) : TextBoxScene =
        TextBoxScene(content.Font, TextBox.ofString text)

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            state <- TextBox.tick buttons state
            if state.Done then Pop else Stay

        member _.Render(fb: Framebuffer) = TextRenderer.draw fb font state
