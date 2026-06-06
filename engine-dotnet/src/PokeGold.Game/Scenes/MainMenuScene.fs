namespace PokeGold.Game.Scenes

open PokeGold.Game.Audio
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render

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
            fb.Clear(0uy, 0uy, 0uy, 255uy)

            entries
            |> List.iteri (fun i entry ->
                let prefix = if i = cursor then ">" else " "
                WindowRenderer.drawString fb content.Font palette 4 (3 + i * 2) (prefix + entry))
