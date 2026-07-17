namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Render
open PokeGold.Game.Ui

type FlyDestinationScene(content: Content, destinations: FlyPoint list, onResult: FlyPoint option -> unit) =
    let palette = TextRenderer.palette
    let input = EdgeDetector()
    let destinations = destinations |> List.toArray
    let labels =
        destinations
        |> Array.map (fun point -> point.Landmark.Replace("LANDMARK_", "").Replace("_", " "))
    let entries = Array.append labels [| "CANCEL" |]
    let visibleRows = min 12 entries.Length
    let mutable menu = MenuList.create entries.Length visibleRows true

    member _.Destinations = destinations |> Array.toList

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update buttons

            menu <-
                if edges.Up then MenuList.moveUp menu
                elif edges.Down then MenuList.moveDown menu
                else menu

            if edges.A then
                if menu.Cursor = destinations.Length then onResult None else onResult (Some destinations.[menu.Cursor])
                Pop
            elif edges.B then
                onResult None
                Pop
            else
                Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawBox fb content.Font palette 0 0 20 (visibleRows + 4)
            WindowRenderer.drawString fb content.Font palette 1 1 "FLY TO"

            for row in 0 .. visibleRows - 1 do
                let index = menu.Top + row

                if index < entries.Length then
                    let y = row + 3
                    if index = menu.Cursor then WindowRenderer.drawCursor fb content.Font palette 1 y
                    WindowRenderer.drawString fb content.Font palette 2 y entries.[index]