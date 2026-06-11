namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Render
open PokeGold.Game.Ui

type private MomBankMode =
    | TopMenu
    | Deposit of amount: int
    | Withdraw of amount: int
    | Message of text: string

/// Player-facing Bank of Mom flow. It covers the recurring money contract:
/// deposit, withdraw, toggle saving preference, and persistent savings state.
type MomBankScene(content: Content, initialPlayer: PlayerState, initialSaving: bool, onChange: PlayerState -> unit, onSavingChange: bool -> unit) =
    let palette = TextRenderer.palette
    let input = EdgeDetector()
    let mutable player = initialPlayer
    let mutable saving = initialSaving
    let mutable mode = TopMenu
    let mutable menu = MenuList.create 4 4 true

    let menuLabel i =
        match i with
        | 0 -> "WITHDRAW"
        | 1 -> "DEPOSIT"
        | 2 -> if saving then "STOP SAVE" else "SAVE"
        | _ -> "QUIT"

    let clampAmount maxAmount amount =
        if maxAmount <= 0 then 0
        else max 100 (min maxAmount amount)

    let adjustAmount maxAmount amount edges =
        let delta =
            if edges.Up then 100
            elif edges.Down then -100
            elif edges.Right then 1000
            elif edges.Left then -1000
            else 0

        clampAmount maxAmount (amount + delta)

    let depositMax () =
        min player.Money (Money.maxMoney - player.MomSavings)

    let withdrawMax () =
        min player.MomSavings (Money.maxMoney - player.Money)

    member _.CurrentPlayer = player
    member _.SavingMoney = saving
    member _.ModeName =
        match mode with
        | TopMenu -> "TopMenu"
        | Deposit _ -> "Deposit"
        | Withdraw _ -> "Withdraw"
        | Message _ -> "Message"

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update buttons

            match mode with
            | TopMenu ->
                menu <-
                    if edges.Up then MenuList.moveUp menu
                    elif edges.Down then MenuList.moveDown menu
                    else menu

                if edges.A then
                    match menu.Cursor with
                    | 0 ->
                        let maxAmount = withdrawMax()
                        if maxAmount <= 0 then mode <- Message "You haven't saved any money."
                        else mode <- Withdraw(clampAmount maxAmount 100)
                        Stay
                    | 1 ->
                        let maxAmount = depositMax()
                        if maxAmount <= 0 then mode <- Message "You don't have money to save."
                        else mode <- Deposit(clampAmount maxAmount 100)
                        Stay
                    | 2 ->
                        saving <- not saving
                        onSavingChange saving
                        mode <- Message(if saving then "Mom will save money." else "Mom stopped saving.")
                        Stay
                    | _ -> Pop
                elif edges.B then Pop
                else Stay

            | Deposit amount ->
                let maxAmount = depositMax()
                let amount = adjustAmount maxAmount amount edges
                mode <- Deposit amount

                if edges.A && amount > 0 then
                    player <-
                        { player with
                            Money = Money.take player.Money amount
                            MomSavings = Money.give player.MomSavings amount }
                    onChange player
                    mode <- Message "Saved with Mom."
                    Stay
                elif edges.B then
                    mode <- TopMenu
                    Stay
                else Stay

            | Withdraw amount ->
                let maxAmount = withdrawMax()
                let amount = adjustAmount maxAmount amount edges
                mode <- Withdraw amount

                if edges.A && amount > 0 then
                    player <-
                        { player with
                            Money = Money.give player.Money amount
                            MomSavings = Money.take player.MomSavings amount }
                    onChange player
                    mode <- Message "Took money from Mom."
                    Stay
                elif edges.B then
                    mode <- TopMenu
                    Stay
                else Stay

            | Message _ ->
                if edges.A || edges.B then
                    mode <- TopMenu
                    Stay
                else Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawBox fb content.Font palette 0 0 20 18
            WindowRenderer.drawString fb content.Font palette 1 1 "MOM'S BANK"
            WindowRenderer.drawString fb content.Font palette 1 3 (sprintf "HELD  $%d" player.Money)
            WindowRenderer.drawString fb content.Font palette 1 4 (sprintf "SAVED $%d" player.MomSavings)
            WindowRenderer.drawString fb content.Font palette 1 5 ("SAVE: " + (if saving then "ON" else "OFF"))

            match mode with
            | TopMenu ->
                for i in 0 .. 3 do
                    if i = menu.Cursor then WindowRenderer.drawCursor fb content.Font palette 1 (8 + i * 2)
                    WindowRenderer.drawString fb content.Font palette 2 (8 + i * 2) (menuLabel i)
            | Deposit amount ->
                WindowRenderer.drawString fb content.Font palette 1 8 "DEPOSIT HOW MUCH?"
                WindowRenderer.drawString fb content.Font palette 1 10 (sprintf "$%d" amount)
                WindowRenderer.drawString fb content.Font palette 1 12 "UP/DOWN 100"
                WindowRenderer.drawString fb content.Font palette 1 13 "LEFT/RIGHT 1000"
            | Withdraw amount ->
                WindowRenderer.drawString fb content.Font palette 1 8 "WITHDRAW HOW MUCH?"
                WindowRenderer.drawString fb content.Font palette 1 10 (sprintf "$%d" amount)
                WindowRenderer.drawString fb content.Font palette 1 12 "UP/DOWN 100"
                WindowRenderer.drawString fb content.Font palette 1 13 "LEFT/RIGHT 1000"
            | Message text ->
                WindowRenderer.drawString fb content.Font palette 1 9 text
                WindowRenderer.drawString fb content.Font palette 1 12 "A/B:OK"
