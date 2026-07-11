namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// The player's decision when a fifth move needs a slot.
/// The caller owns the actual replacement so it can install the new move with
/// its full base PP in the appropriate persistent or battle record.
type LearnMoveDecision =
    | ReplaceMove of index: int
    | DeclineMove

type private LearnMoveMode =
    | AskForget
    | ChooseMove
    | ConfirmStop
    | HmRejected

/// Source-shaped four-move decision flow from engine/pokemon/learn.asm.
type LearnMoveScene(font: Font, nickname: string, newMoveName: string, currentMoves: (int * int) list, onDecision: LearnMoveDecision -> unit) =
    let input = EdgeDetector()
    let palette = TextRenderer.palette
    let moves = currentMoves |> List.truncate 4
    let mutable mode = AskForget
    let mutable yes = true
    let mutable cursor = 0
    let mutable finished = false

    let moveName (moveId, _pp) =
        Moves.tryByIndex moveId
        |> Option.map _.Name
        |> Option.defaultValue (sprintf "MOVE %d" moveId)

    let isHm index =
        match moves |> List.tryItem index |> Option.map moveName with
        | Some ("CUT" | "FLY" | "SURF" | "STRENGTH" | "FLASH" | "WHIRLPOOL" | "WATERFALL") -> true
        | _ -> false

    let complete decision =
        if not finished then
            finished <- true
            onDecision decision
        Pop

    let beginYesNo nextMode =
        mode <- nextMode
        yes <- true
        Stay

    let updateYesNo edges onYes onNo =
        if edges.Up || edges.Down then
            yes <- not yes
            Stay
        elif edges.A then
            if yes then onYes () else onNo ()
        elif edges.B then
            onNo ()
        else
            Stay

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            if finished then Pop
            else
                let edges = input.Update buttons
                match mode with
                | AskForget ->
                    updateYesNo edges
                        (fun () -> mode <- ChooseMove; cursor <- 0; Stay)
                        (fun () -> beginYesNo ConfirmStop)
                | ConfirmStop ->
                    // YES gives up learning; NO returns to the forget prompt.
                    updateYesNo edges
                        (fun () -> complete DeclineMove)
                        (fun () -> beginYesNo AskForget)
                | HmRejected ->
                    if edges.A || edges.B then mode <- ChooseMove; Stay else Stay
                | ChooseMove ->
                    let entryCount = moves.Length + 1 // final row is CANCEL
                    if edges.Up then cursor <- (cursor + entryCount - 1) % entryCount; Stay
                    elif edges.Down then cursor <- (cursor + 1) % entryCount; Stay
                    elif edges.B || (edges.A && cursor = moves.Length) then beginYesNo ConfirmStop
                    elif edges.A && isHm cursor then mode <- HmRejected; Stay
                    elif edges.A then complete (ReplaceMove cursor)
                    else Stay

        member _.Render(fb: Framebuffer) =
            let drawYesNo prompt =
                WindowRenderer.drawBox fb font palette 1 1 18 9
                WindowRenderer.drawString fb font palette 2 2 prompt
                WindowRenderer.drawString fb font palette 12 5 "YES"
                WindowRenderer.drawString fb font palette 12 7 "NO"
                WindowRenderer.drawCursor fb font palette 11 (if yes then 5 else 7)

            match mode with
            | AskForget -> drawYesNo (sprintf "Forget a move for %s?" newMoveName)
            | ConfirmStop -> drawYesNo (sprintf "Stop %s learning it?" nickname)
            | HmRejected ->
                WindowRenderer.drawBox fb font palette 1 3 18 6
                WindowRenderer.drawString fb font palette 2 5 "HM moves can't be"
                WindowRenderer.drawString fb font palette 2 6 "forgotten now."
            | ChooseMove ->
                let entries = (moves |> List.map moveName) @ [ "CANCEL" ]
                WindowRenderer.drawBox fb font palette 2 1 16 (entries.Length + 4)
                WindowRenderer.drawString fb font palette 3 2 "FORGET WHICH?"
                entries
                |> List.iteri (fun i name ->
                    let row = 4 + i
                    if i = cursor then WindowRenderer.drawCursor fb font palette 3 row
                    WindowRenderer.drawString fb font palette 4 row name)
