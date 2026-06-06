namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render

/// A simple keyboard scene for entering the player's name.
type NamingScene(font: Font, prompt: string, onComplete: string -> Transition) =
    let keyboard = "ABCDEFGHIJKLMNOPQRSTUVWXYZ .,"
    let cols = 9
    let rows = (keyboard.Length + cols - 1) / cols
    let mutable name = ""
    let mutable kx = 0
    let mutable ky = 0
    let mutable prev = Buttons.none
    let palette = TextRenderer.palette

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edge now was = now && not was

            if edge buttons.Right prev.Right then
                kx <- min (cols - 1) (kx + 1)
                prev <- buttons
                Stay
            elif edge buttons.Left prev.Left then
                kx <- max 0 (kx - 1)
                prev <- buttons
                Stay
            elif edge buttons.Down prev.Down then
                ky <- min (rows - 1) (ky + 1)
                prev <- buttons
                Stay
            elif edge buttons.Up prev.Up then
                ky <- max 0 (ky - 1)
                prev <- buttons
                Stay
            elif edge buttons.A prev.A then
                let idx = ky * cols + kx
                if idx < keyboard.Length && name.Length < 7 then
                    name <- name + string keyboard.[idx]
                prev <- buttons
                Stay
            elif edge buttons.B prev.B then
                if name.Length > 0 then
                    name <- name.[..name.Length - 2]
                prev <- buttons
                Stay
            elif edge buttons.Start prev.Start then
                if name.Length > 0 then
                    prev <- buttons
                    onComplete name
                else
                    prev <- buttons
                    Stay
            else
                prev <- buttons
                Stay

        member _.Render(fb: Framebuffer) =
            fb.Clear(0uy, 0uy, 0uy, 255uy)
            WindowRenderer.drawString fb font palette 1 1 prompt
            WindowRenderer.drawString fb font palette 3 3 name

            for row in 0 .. rows - 1 do
                for col in 0 .. cols - 1 do
                    let idx = row * cols + col
                    if idx < keyboard.Length then
                        let prefix = if col = kx && row = ky then ">" else " "
                        WindowRenderer.drawString fb font palette (1 + col * 2) (6 + row * 2) (prefix + string keyboard.[idx])
