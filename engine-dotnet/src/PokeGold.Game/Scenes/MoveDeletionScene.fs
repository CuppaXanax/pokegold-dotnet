namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Render
open PokeGold.Game.Ui

type private MoveDeletionMode =
    | Flowing
    | ChoosingMove of partyIndex: int * menu: MenuList

type MoveDeletionScene(content: Content, initialPlayer: PlayerState, onPlayer: PlayerState -> unit) =
    let palette = TextRenderer.palette
    let input = EdgeDetector()
    let mutable player = initialPlayer
    let mutable continuation: (unit -> Transition) option = None
    let mutable yesNoResult = 0
    let mutable selectedPartyIndex: int option = None
    let mutable selectedMoveIndex: int option = None
    let mutable mode = Flowing

    let text message =
        TextBoxScene.Of(content, message + "<DONE>") :> Scene

    let show message next =
        mode <- Flowing
        continuation <- Some next
        Push(text message)

    let ask next =
        mode <- Flowing
        continuation <- Some next
        Push(YesNoScene(content.Font, fun result -> yesNoResult <- result) :> Scene)

    let finish () =
        onPlayer player
        Pop

    let decline () =
        show "No? Come again." finish

    let moveName (moveId, _pp) =
        Moves.tryByIndex moveId
        |> Option.map (fun move -> move.Name)
        |> Option.defaultValue (sprintf "MOVE %d" moveId)

    let updateParty idx mon =
        player <-
            { player with
                Party =
                    player.Party
                    |> List.mapi (fun i existing -> if i = idx then mon else existing) }

    let deleteMove partyIdx moveIdx =
        let mon = List.item partyIdx player.Party
        let moves =
            mon.Moves
            |> List.indexed
            |> List.choose (fun (i, move) -> if i = moveIdx then None else Some move)

        updateParty partyIdx { mon with Moves = moves }
        show "Poof! The #MON forgot its move." finish

    let confirmDeletion partyIdx moveIdx =
        selectedMoveIndex <- Some moveIdx
        let move = List.item moveIdx (List.item partyIdx player.Party).Moves
        show (sprintf "Forget %s?" (moveName move)) (fun () ->
            ask (fun () ->
                if yesNoResult = 0 then
                    decline ()
                else
                    match selectedMoveIndex with
                    | Some idx -> deleteMove partyIdx idx
                    | None -> decline ()))

    let startMoveChoice partyIdx =
        let mon = List.item partyIdx player.Party

        if Breeding.isEgg mon then
            show "An EGG can't forget moves." finish
        elif mon.Moves.Length < 2 then
            show "That #MON knows only one move." finish
        else
            show "Which move should be forgotten?" (fun () ->
                mode <- ChoosingMove(partyIdx, MenuList.create (mon.Moves.Length + 1) (mon.Moves.Length + 1) true)
                Stay)

    let processPartyPick () =
        match selectedPartyIndex with
        | Some idx -> startMoveChoice idx
        | None -> decline ()

    let pickParty () =
        selectedPartyIndex <- None
        continuation <- Some processPartyPick
        Push(
            PartyScene(
                content,
                player,
                (fun p -> player <- p),
                onSelect = (fun idx ->
                    selectedPartyIndex <- Some idx
                    Pop)) :> Scene)

    let start () =
        show "I can make #MON forget moves." (fun () ->
            ask (fun () ->
                if yesNoResult = 0 then
                    decline ()
                else
                    show "Which #MON?" pickParty))

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            match continuation with
            | Some next ->
                continuation <- None
                next ()
            | None ->
                match mode with
                | Flowing -> start ()
                | ChoosingMove(partyIdx, menu) ->
                    let mon = List.item partyIdx player.Party
                    let entries = mon.Moves.Length + 1
                    let edges = input.Update buttons

                    let menu' =
                        if edges.Up then MenuList.moveUp menu
                        elif edges.Down then MenuList.moveDown menu
                        else menu

                    mode <- ChoosingMove(partyIdx, menu')

                    if edges.A then
                        if menu'.Cursor >= mon.Moves.Length then
                            decline ()
                        else
                            confirmDeletion partyIdx menu'.Cursor
                    elif edges.B then
                        decline ()
                    else
                        Stay

        member _.Render(fb: Framebuffer) =
            match mode with
            | Flowing -> ()
            | ChoosingMove(partyIdx, menu) ->
                let mon = List.item partyIdx player.Party
                let entries = [| yield! (mon.Moves |> List.map moveName); yield "CANCEL" |]
                WindowRenderer.drawBox fb content.Font palette 2 2 16 (entries.Length + 4)
                WindowRenderer.drawString fb content.Font palette 3 3 "MOVE?"

                for i in 0 .. entries.Length - 1 do
                    let row = 5 + i
                    if i = menu.Cursor then WindowRenderer.drawCursor fb content.Font palette 3 row
                    WindowRenderer.drawString fb content.Font palette 4 row entries.[i]
