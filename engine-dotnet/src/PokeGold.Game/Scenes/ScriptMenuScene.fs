namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// Generic fallback for ROM script menus (`loadmenu` + `verticalmenu`/`2dmenu`).
/// The exact option text lives in menu headers we do not fully decode yet, but the
/// command now has a real UI/input path and returns the same 1-based selection
/// shape scripts expect. B/cancel returns 0.
type ScriptMenuScene(content: Content, menuLabel: string, onResult: int -> unit) =
    let palette = TextRenderer.palette
    let input = EdgeDetector()
    let entries = [| "OPTION 1"; "OPTION 2"; "CANCEL" |]
    let mutable menu = MenuList.create entries.Length entries.Length true

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update buttons

            menu <-
                if edges.Up then MenuList.moveUp menu
                elif edges.Down then MenuList.moveDown menu
                else menu

            if edges.A then
                if menu.Cursor = entries.Length - 1 then onResult 0 else onResult (menu.Cursor + 1)
                Pop
            elif edges.B then
                onResult 0
                Pop
            else Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawBox fb content.Font palette 2 2 16 10
            WindowRenderer.drawString fb content.Font palette 3 3 (menuLabel.Replace("_", " "))

            for i in 0 .. entries.Length - 1 do
                let row = 5 + i * 2
                if i = menu.Cursor then WindowRenderer.drawCursor fb content.Font palette 3 row
                WindowRenderer.drawString fb content.Font palette 4 row entries.[i]
