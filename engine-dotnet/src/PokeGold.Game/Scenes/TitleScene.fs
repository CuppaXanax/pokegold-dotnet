namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// The title screen shown before the overworld starts.
type TitleScene(content: Content, overworld: Scene) =
    let mutable frame = 0
    let input = EdgeDetector()
    let palette = TextRenderer.palette

    [<Literal>]
    let TitleRow = 7

    [<Literal>]
    let PressRow = 15

    [<Literal>]
    let BlinkFrames = 30

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)
            frame <- frame + 1

            if edges.A || edges.Start then
                Replace(overworld)
            else
                Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawString fb content.Font palette 0 TitleRow "POKEMON GOLD VERSION"

            let blink = frame % (BlinkFrames * 2) < BlinkFrames
            if blink then
                WindowRenderer.drawString fb content.Font palette 5 PressRow "PRESS START"
