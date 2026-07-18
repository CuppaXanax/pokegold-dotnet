namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Ui

/// Transparent input gate while an overworld Pokepic remains visible.
type PokePicWaitScene() =
    let input = EdgeDetector()

    interface Scene with
        member _.Update(buttons: Buttons) =
            let edges = input.Update buttons
            if edges.A || edges.B then Pop else Stay

        member _.Render(_fb: Framebuffer) = ()