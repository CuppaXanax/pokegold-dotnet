namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Render

/// Minimal Pokégear stub scene: shows the Map / Phone / Radio tabs.
type PokegearScene(font: Font, _player: PlayerState) =
    let mutable cursor = 0
    let mutable prev = Buttons.none
    let tabs = [| "MAP"; "PHONE"; "RADIO" |]
    let palette = TextRenderer.palette

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edge now was = now && not was

            if edge buttons.B prev.B then
                prev <- buttons
                Pop
            elif edge buttons.Down prev.Down then
                cursor <- min (tabs.Length - 1) (cursor + 1)
                prev <- buttons
                Stay
            elif edge buttons.Up prev.Up then
                cursor <- max 0 (cursor - 1)
                prev <- buttons
                Stay
            elif edge buttons.A prev.A then
                // TODO: open the selected tab.
                prev <- buttons
                Stay
            else
                prev <- buttons
                Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawString fb font palette 1 1 "POKéGEAR"

            for i in 0 .. tabs.Length - 1 do
                let prefix = if i = cursor then ">" else " "
                WindowRenderer.drawString fb font palette 2 (4 + i * 2) (prefix + tabs.[i])
