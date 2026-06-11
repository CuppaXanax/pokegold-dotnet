namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

type private WeekdayPhase =
    | Choosing
    | Confirming

/// Faithful-enough weekday setup scene for the Gen 2 clock flow. The original
/// uses Up to advance and Down to go back through the weekdays, then asks for
/// confirmation before returning to the script.
type WeekdayScene(content: Content, initialWeekday: int, onConfirm: int -> unit) =
    let names = [| "SUNDAY"; "MONDAY"; "TUESDAY"; "WEDNESDAY"; "THURSDAY"; "FRIDAY"; "SATURDAY" |]
    let input = EdgeDetector()
    let palette = TextRenderer.palette
    let mutable weekday = ((initialWeekday % 7) + 7) % 7
    let mutable phase = Choosing

    let nextDay () =
        weekday <- (weekday + 1) % names.Length

    let previousDay () =
        weekday <- (weekday + names.Length - 1) % names.Length

    member _.Weekday = weekday

    interface Scene with
        member _.Update(buttons: Buttons) =
            let edges = input.Update buttons

            match phase with
            | Choosing ->
                if edges.Up then
                    nextDay()
                    Stay
                elif edges.Down then
                    previousDay()
                    Stay
                elif edges.A || edges.Start then
                    phase <- Confirming
                    Stay
                else
                    Stay
            | Confirming ->
                if edges.A || edges.Start then
                    onConfirm weekday
                    Pop
                elif edges.B then
                    phase <- Choosing
                    Stay
                else
                    Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawBox fb content.Font palette 0 11 20 6
            WindowRenderer.drawString fb content.Font palette 1 12 "WHAT DAY IS IT?"

            WindowRenderer.drawBox fb content.Font palette 8 3 11 5
            WindowRenderer.drawString fb content.Font palette 10 5 names.[weekday]

            match phase with
            | Choosing ->
                WindowRenderer.drawString fb content.Font palette 1 14 "UP/DOWN:CHANGE"
                WindowRenderer.drawString fb content.Font palette 1 15 "A:OK"
            | Confirming ->
                WindowRenderer.drawString fb content.Font palette 1 14 ("IS IT " + names.[weekday] + "?")
                WindowRenderer.drawString fb content.Font palette 1 15 "A:YES  B:NO"
