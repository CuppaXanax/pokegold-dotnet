namespace PokeGold.Game.Scenes

open PokeGold.Game.Battle
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Render
open PokeGold.Game.Ui

type private NpcTradeMode =
    | ConfirmTrade
    | SelectParty of MenuList
    | TradeMessage of string

/// Source-data-backed NPC trade flow: accept or decline, choose a matching party
/// member, then receive the source-defined trade Pokémon.
type NpcTradeScene(content: Content, initialPlayer: PlayerState, trade: NpcTradeData, alreadyTraded: bool, onTrade: PlayerState -> unit) =
    let input = EdgeDetector()
    let palette = TextRenderer.palette
    let mutable player = initialPlayer
    let mutable confirmChoice = 0
    let mutable mode =
        if alreadyTraded then TradeMessage($"How is {trade.Nickname} doing?")
        else ConfirmTrade

    let speciesName dex =
        Species.all
        |> Map.tryPick (fun name stats -> if stats.Dex = dex then Some name else None)
        |> Option.defaultValue "UNKNOWN"

    let hasRequestedGender (mon: PartyMon) =
        match trade.Gender, Species.all |> Map.tryFind trade.Give with
        | "TRADE_GENDER_EITHER", _ -> true
        | "TRADE_GENDER_MALE", Some stats -> BattleMon.genderFromDvs stats mon.Dvs = Male
        | "TRADE_GENDER_FEMALE", Some stats -> BattleMon.genderFromDvs stats mon.Dvs = Female
        | _ -> false

    let selectTradeMon index =
        let offered = player.Party.[index]

        if speciesName offered.SpeciesId <> trade.Give || not (hasRequestedGender offered) then
            mode <- TradeMessage($"I'm looking for {trade.Give}.")
        else
            match Species.all |> Map.tryFind trade.Receive with
            | None ->
                mode <- TradeMessage("I can't make that trade.")
            | Some receivedSpecies ->
                let received =
                    MoveLearn.seedStartingMoves (PartyMon.createWithDvs receivedSpecies.Dex offered.Level trade.Dvs)
                    |> fun mon ->
                        { mon with
                            Nickname = trade.Nickname
                            HeldItem = if trade.HeldItem = "NO_ITEM" then None else Some trade.HeldItem
                            OtName = trade.OtName
                            OtId = trade.OtId }

                let remaining =
                    player.Party
                    |> List.mapi (fun partyIndex mon -> partyIndex, mon)
                    |> List.choose (fun (partyIndex, mon) -> if partyIndex = index then None else Some mon)

                player <-
                    { player with
                        Party = remaining @ [ received ]
                        DexSeen = Set.add received.SpeciesId player.DexSeen
                        DexOwn = Set.add received.SpeciesId player.DexOwn }
                onTrade player
                mode <- TradeMessage($"Traded {trade.Give} for {trade.Receive}!")

    let renderMessage fb (message: string) =
        WindowRenderer.drawBox fb content.Font palette 1 9 18 6
        let words = message.Split(' ')
        let mutable line = ""
        let mutable row = 10

        for word in words do
            let candidate = if line = "" then word else line + " " + word
            if candidate.Length > 16 then
                WindowRenderer.drawString fb content.Font palette 2 row line
                row <- row + 1
                line <- word
            else
                line <- candidate

        if line <> "" && row <= 13 then
            WindowRenderer.drawString fb content.Font palette 2 row line

    let renderConfirm fb =
        WindowRenderer.drawBox fb content.Font palette 1 2 18 7
        WindowRenderer.drawString fb content.Font palette 2 3 ($"{trade.OtName} wants {trade.Give}.")
        WindowRenderer.drawString fb content.Font palette 2 4 ($"Trade for {trade.Receive}?")
        WindowRenderer.drawString fb content.Font palette 4 6 "YES"
        WindowRenderer.drawString fb content.Font palette 4 7 "NO"
        WindowRenderer.drawCursor fb content.Font palette 2 (6 + confirmChoice)

    let renderParty fb (menu: MenuList) =
        let rows = max 4 (min 16 (player.Party.Length + 3))
        WindowRenderer.drawBox fb content.Font palette 0 0 20 rows
        WindowRenderer.drawString fb content.Font palette 2 1 ($"Choose {trade.Give}")

        player.Party
        |> List.iteri (fun index mon ->
            let row = index + 2
            if menu.Cursor = index then
                WindowRenderer.drawCursor fb content.Font palette 1 row
            WindowRenderer.drawString fb content.Font palette 2 row ($"{mon.Nickname} Lv{mon.Level}"))

        let cancelRow = player.Party.Length + 2
        if menu.Cursor = player.Party.Length then
            WindowRenderer.drawCursor fb content.Font palette 1 cancelRow
        WindowRenderer.drawString fb content.Font palette 2 cancelRow "CANCEL"

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update buttons

            match mode with
            | ConfirmTrade ->
                if edges.Up || edges.Down then
                    confirmChoice <- 1 - confirmChoice
                    Stay
                elif edges.A then
                    if confirmChoice = 0 then
                        mode <- SelectParty(MenuList.create (player.Party.Length + 1) (player.Party.Length + 1) true)
                    else
                        mode <- TradeMessage("Maybe next time.")
                    Stay
                elif edges.B then
                    mode <- TradeMessage("Maybe next time.")
                    Stay
                else
                    Stay
            | SelectParty menu ->
                let updatedMenu =
                    if edges.Up then MenuList.moveUp menu
                    elif edges.Down then MenuList.moveDown menu
                    else menu
                mode <- SelectParty updatedMenu

                if edges.A then
                    if updatedMenu.Cursor >= player.Party.Length then
                        mode <- TradeMessage("Maybe next time.")
                    else
                        selectTradeMon updatedMenu.Cursor
                    Stay
                elif edges.B then
                    mode <- TradeMessage("Maybe next time.")
                    Stay
                else
                    Stay
            | TradeMessage _ ->
                if edges.A || edges.B then Pop else Stay

        member _.Render(fb: Framebuffer) =
            match mode with
            | ConfirmTrade -> renderConfirm fb
            | SelectParty menu -> renderParty fb menu
            | TradeMessage message -> renderMessage fb message