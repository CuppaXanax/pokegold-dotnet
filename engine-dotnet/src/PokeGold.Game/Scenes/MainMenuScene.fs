namespace PokeGold.Game.Scenes

open PokeGold.Game.Audio
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// The main menu shown after the title screen.
type MainMenuScene(content: Content, sound: ISoundBoard, hasSave: bool, onNewGame: unit -> Transition, onContinue: unit -> Transition, onOptions: unit -> Transition) =
    let entries =
        [ if hasSave then "CONTINUE"
          "NEW GAME"
          "OPTIONS" ]
    let mutable cursor = 0
    let mutable prev = Buttons.none
    let palette = TextRenderer.palette

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edge now was = now && not was

            if edge buttons.Down prev.Down then
                cursor <- min (entries.Length - 1) (cursor + 1)
                prev <- buttons
                Stay
            elif edge buttons.Up prev.Up then
                cursor <- max 0 (cursor - 1)
                prev <- buttons
                Stay
            elif edge buttons.A prev.A then
                prev <- buttons
                match entries.[cursor] with
                | "CONTINUE" -> onContinue()
                | "NEW GAME" -> onNewGame()
                | "OPTIONS" -> onOptions()
                | _ -> Stay
            else
                prev <- buttons
                Stay

        member _.Render(fb: Framebuffer) =
            // Fill with a dark blue background (like the real GSC menu)
            let bg = Palette.rgb555 2 4 10
            for y in 0..Display.Height - 1 do
                for x in 0..Display.Width - 1 do
                    fb.SetPixel(x, y, bg.R, bg.G, bg.B, bg.A)

            // Draw a window box for the menu
            let boxW = 12
            let boxH = entries.Length * 2 + 2
            let boxX = (20 - boxW) / 2
            let boxY = (18 - boxH) / 2
            WindowRenderer.drawBox fb content.Font palette boxX boxY boxW boxH

            entries
            |> List.iteri (fun i entry ->
                let prefix = if i = cursor then ">" else " "
                WindowRenderer.drawString fb content.Font palette (boxX + 1) (boxY + 1 + i * 2) (prefix + entry))
