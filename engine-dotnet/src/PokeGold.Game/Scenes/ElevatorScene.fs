namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// One source `elevfloor` destination embedded in an elevator map script.
type ElevatorFloor =
    { Name: string
      Map: string
      Warp: int }

/// Source-style floor selector. B cancels; selecting the current floor is a no-op.
type ElevatorScene(content: Content, floors: ElevatorFloor list, currentFloor: int, onResult: ElevatorFloor option -> unit) =
    let input = EdgeDetector()
    let palette = TextRenderer.palette
    let floors = floors |> List.toArray
    let visibleRows = min 7 floors.Length
    let mutable menu =
        MenuList.create floors.Length (max 1 visibleRows) true
        |> MenuList.moveTo currentFloor

    let floorLabel (name: string) =
        name.Replace("FLOOR_", "").Replace("_", "")

    member _.Cursor = menu.Cursor

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update buttons

            menu <-
                if edges.Up then MenuList.moveUp menu
                elif edges.Down then MenuList.moveDown menu
                else menu

            if edges.A then
                if menu.Cursor = currentFloor then onResult None
                else onResult (Some floors.[menu.Cursor])
                Pop
            elif edges.B then
                onResult None
                Pop
            else
                Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawBox fb content.Font palette 1 1 18 (visibleRows + 4)
            WindowRenderer.drawString fb content.Font palette 2 2 ("NOW ON " + floorLabel floors.[currentFloor].Name)

            for row in 0 .. visibleRows - 1 do
                let index = menu.Top + row
                if index < floors.Length then
                    let y = row + 4
                    if index = menu.Cursor then WindowRenderer.drawCursor fb content.Font palette 2 y
                    WindowRenderer.drawString fb content.Font palette 3 y (floorLabel floors.[index].Name)