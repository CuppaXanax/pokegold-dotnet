namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

type ApricornSelectionScene(content: Content, apricorns: string list, onResult: string option -> unit) =
    let palette = TextRenderer.palette
    let input = EdgeDetector()

    let label item =
        match Map.tryFind item Items.byId with
        | Some data -> data.Name
        | None -> item.Replace("_", " ")

    let itemIds = apricorns |> List.toArray
    let entries = Array.append (apricorns |> List.map label |> List.toArray) [| "CANCEL" |]
    let mutable menu = MenuList.create entries.Length entries.Length true

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update buttons

            menu <-
                if edges.Up then MenuList.moveUp menu
                elif edges.Down then MenuList.moveDown menu
                else menu

            if edges.A then
                if menu.Cursor >= itemIds.Length then onResult None else onResult (Some itemIds.[menu.Cursor])
                Pop
            elif edges.B then
                onResult None
                Pop
            else Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawBox fb content.Font palette 2 2 16 (entries.Length + 4)
            WindowRenderer.drawString fb content.Font palette 3 3 "APRICORN"

            for i in 0 .. entries.Length - 1 do
                let row = 5 + i
                if i = menu.Cursor then WindowRenderer.drawCursor fb content.Font palette 3 row
                WindowRenderer.drawString fb content.Font palette 4 row entries.[i]
