namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Text
open PokeGold.Game.Render
open PokeGold.Game.Battle

/// The wild-battle scene. Drains the battle's message queue through the M5
/// typewriter box (word-wrapped, each message waiting for a button), then shows
/// the move menu. Selecting a move resolves a full turn and enqueues its
/// messages; the scene pops when the battle ends.
type BattleScene(font: Font, initial: BattleState) =
    let mutable state = initial
    let mutable queue : string list = initial.Messages
    let mutable box : TextBoxState option = None
    let mutable cursor = 0
    let mutable prev = Buttons.none

    /// Build the demo encounter: the player's CYNDAQUIL vs a wild PIDGEY.
    static member StartDemo(content: Content) : BattleScene =
        let player =
            BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE"; Moves.byName "LEER" ]

        let enemy = BattleMon.ofSpecies (Species.byName "PIDGEY") 3 [ Moves.byName "TACKLE" ]
        BattleScene(content.Font, Battle.create player enemy 0x1234u)

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edge (now: bool) (was: bool) = now && not was
            let startedWithBox = box.IsSome

            // Advance the active message box.
            match box with
            | Some b ->
                let b2 = TextBox.tick buttons b
                box <- (if b2.Done then None else Some b2)
            | None -> ()

            let result =
                if box.IsSome then
                    Stay
                else
                    match queue with
                    | msg :: rest ->
                        // Start the next queued message.
                        queue <- rest
                        box <- Some(TextBox.ofString (BattleScene.wrap msg))
                        Stay
                    | [] ->
                        if Battle.isOver state then Pop
                        elif startedWithBox then
                            // Don't read the dismiss press as a menu action this frame.
                            Stay
                        else
                            let moves = state.Player.Moves

                            if BattleMon.mustStruggle state.Player then
                                // All PP exhausted — auto-Struggle (pass index 0, ignored).
                                state <- Battle.chooseMove 0 state
                                queue <- state.Messages
                            elif edge buttons.Down prev.Down then
                                cursor <- min (moves.Length - 1) (cursor + 1)
                            elif edge buttons.Up prev.Up then
                                cursor <- max 0 (cursor - 1)
                            elif edge buttons.A prev.A then
                                if BattleMon.canUseMove cursor state.Player then
                                    state <- Battle.chooseMove cursor state
                                    queue <- state.Messages
                                // else: 0 PP — do nothing (move blocked)
                            elif edge buttons.B prev.B then
                                state <- Battle.run state
                                queue <- state.Messages

                            Stay

            prev <- buttons
            result

        member _.Render(fb: Framebuffer) =
            BattleRenderer.drawField fb font state

            match box with
            | Some b -> TextRenderer.draw fb font b
            | None -> if not (Battle.isOver state) then BattleRenderer.drawMenu fb font state.Player.Moves state.Player.Pp cursor

    /// Word-wrap a battle message into the two-line box, inserting `<LINE>` for
    /// the second line and `<CONT>` (scroll) for any further lines, and a
    /// trailing `<PROMPT>` so each message waits for the player.
    static member private wrap(msg: string) : string =
        let words = msg.Split(' ')
        let sb = System.Text.StringBuilder()
        let mutable lineLen = 0
        let mutable lineIndex = 0

        for w in words do
            let need = if lineLen = 0 then w.Length else lineLen + 1 + w.Length

            if need > TextBox.InnerW && lineLen > 0 then
                sb.Append(if lineIndex = 0 then "<LINE>" else "<CONT>") |> ignore
                lineIndex <- lineIndex + 1
                lineLen <- 0

            if lineLen > 0 then
                sb.Append(' ') |> ignore
                lineLen <- lineLen + 1

            sb.Append(w) |> ignore
            lineLen <- lineLen + w.Length

        sb.Append("<PROMPT>") |> ignore
        sb.ToString()
