namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Text
open PokeGold.Game.Render
open PokeGold.Game.Battle
open PokeGold.Game.Player
open PokeGold.Game.Debug

type private BattleMenuMode =
    | CommandMenu
    | MoveMenu
    | ItemMenu
    | ItemTargetMenu of item: string
    | PartyMenu of forced: bool

/// The battle scene shell. It owns the Gen-2 command flow (FIGHT/PKMN/PACK/RUN),
/// drains battle messages through the text box, and delegates turn resolution to
/// the pure battle engine.
type BattleScene(font: Font, initial: BattleState, ?onBattleEnd: BattleState -> unit, ?bag: Bag, ?onBagChange: Bag -> unit, ?onCatch: BattleMon -> unit) =
    let mutable state = initial
    let mutable queue : string list = initial.Messages
    let onBattleEnd = defaultArg onBattleEnd (fun _ -> ())
    let onBagChange = defaultArg onBagChange ignore
    let onCatch = defaultArg onCatch ignore
    let mutable bag = defaultArg bag Bag.empty
    let mutable box : TextBoxState option = None
    let mutable commandCursor = 0
    let mutable moveCursor = 0
    let mutable itemCursor = 0
    let mutable partyCursor = 0
    let mutable mode = CommandMenu
    let mutable prev = Buttons.none
    let mutable animFrames = 0
    let mutable currentAnim = NoAnim
    let mutable endNotified = false

    /// Build the demo encounter: the player's CYNDAQUIL vs a wild PIDGEY.
    static member StartDemo(content: Content) : BattleScene =
        let player =
            BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE"; Moves.byName "LEER" ]

        let enemy = BattleMon.ofSpecies (Species.byName "PIDGEY") 3 [ Moves.byName "TACKLE" ]
        BattleScene(content.Font, Battle.createWild player enemy 0x1234u)

    member _.CurrentBag = bag
    member _.CurrentState = state

    member _.CurrentModeName =
        match mode with
        | CommandMenu -> "CommandMenu"
        | MoveMenu -> "MoveMenu"
        | ItemMenu -> "PackMenu"
        | ItemTargetMenu _ -> "TargetMenu"
        | PartyMenu forced -> if forced then "ForcedSwitch" else "PartyMenu"

    member _.CommandCursor = commandCursor
    member _.MoveCursor = moveCursor
    member _.ItemCursor = itemCursor
    member _.PartyCursor = partyCursor

    member this.RuntimeSnapshot: RuntimeBattleSnapshot =
        let kind =
            match state.Kind with
            | WildBattle -> "Wild"
            | TrainerBattle ctx -> $"Trainer:{ctx.Group}:{ctx.Id}"

        let outcome =
            state.Outcome
            |> Option.map (function
                | Win -> "Win"
                | Lose -> "Lose"
                | Ran -> "Ran")

        { Kind = kind
          Mode = this.CurrentModeName
          PlayerSpecies = state.Player.Species.Name
          EnemySpecies = state.Enemy.Species.Name
          MessageActive = box.IsSome
          PendingMessages = queue
          Outcome = outcome }

    member private _.BattleItems =
        let hpRestoreItems =
            Set.ofList
                [ "POTION"; "SUPER_POTION"; "HYPER_POTION"; "MAX_POTION"; "FULL_RESTORE"
                  "FRESH_WATER"; "SODA_POP"; "LEMONADE"; "MOOMOO_MILK"; "BERRY_JUICE"
                  "RAGECANDYBAR"; "BERRY"; "GOLD_BERRY" ]

        let usableItems =
            bag.Items
            |> List.choose (fun (item, qty) ->
                if qty <= 0 then None
                elif Set.contains item hpRestoreItems then Some item
                else
                    match item with
                    | "ANTIDOTE" | "PARLYZ_HEAL" | "BURN_HEAL" | "ICE_HEAL" | "AWAKENING" | "FULL_HEAL" | "FULL_RESTORE" -> Some item
                    | _ -> None)

        let balls =
            bag.Balls
            |> List.choose (fun (item, qty) -> if qty > 0 then Some item else None)

        balls @ usableItems

    member private _.ItemLabel(item: string) =
        Items.byId
        |> Map.tryFind item
        |> Option.map (fun data -> data.Name)
        |> Option.defaultValue (item.Replace("_", " "))

    member private this.SetBattlePlayerTeam(team: BattleMon list) =
        let player =
            team
            |> List.tryHead
            |> Option.defaultValue state.Player

        state <- { state with Player = player; PlayerTeam = team }

    member private this.TryUseHealingItem(item: string) (targetIndex: int) : bool =
        if targetIndex < 0 || targetIndex >= state.PlayerTeam.Length then
            queue <- [ "That can't be used here!" ]
            false
        else
            let current = state.PlayerTeam.[targetIndex]
            let itemData = Items.byId |> Map.tryFind item

            let healHp (mon: BattleMon) =
                match itemData |> Option.map (fun data -> data.Param) with
                | Some amount when amount < 0 -> { mon with Hp = mon.MaxHp }
                | Some amount when amount > 0 && mon.Hp < mon.MaxHp -> { mon with Hp = min mon.MaxHp (mon.Hp + amount) }
                | _ -> mon

            let cureStatus (mon: BattleMon) =
                match item, mon.Status with
                | "ANTIDOTE", Poison
                | "ANTIDOTE", BadPoison _
                | "PARLYZ_HEAL", Paralysis
                | "BURN_HEAL", Burn
                | "ICE_HEAL", Freeze
                | "AWAKENING", Sleep _ -> { mon with Status = Healthy }
                | "FULL_HEAL", status when status <> Healthy -> { mon with Status = Healthy }
                | "FULL_RESTORE", status when status <> Healthy -> { mon with Status = Healthy }
                | _ -> mon

            let healed = healHp current |> cureStatus

            if healed = current then
                queue <- [ "It won't have any effect!" ]
                false
            else
                let team =
                    state.PlayerTeam
                    |> List.mapi (fun i mon -> if i = targetIndex then healed else mon)

                this.SetBattlePlayerTeam team
                bag <- Bag.remove item 1 bag
                onBagChange bag
                queue <- [ $"{this.ItemLabel item} was used on {current.Species.Name}!" ]
                true

    member private _.TryUseBall(ball: string) =
        if Battle.isTrainerBattle state then
            queue <- [ "The trainer blocked the BALL!" ]
        else
            bag <- Bag.remove ball 1 bag
            onBagChange bag

            let caught, wobbles, rng = Catch.tryCatch ball state.Enemy state.Rng
            if caught then
                onCatch state.Enemy
                state <- { state with Outcome = Some Win; Rng = rng }
                queue <- [ $"Gotcha! {state.Enemy.Species.Name} was caught!" ]
            else
                state <- { state with Rng = rng }
                queue <- [ $"{state.Enemy.Species.Name} broke free after {wobbles} shake(s)!" ]

    member private this.SelectPartyMon(forced: bool) =
        if partyCursor >= state.PlayerTeam.Length then
            if not forced then mode <- CommandMenu
        else
            let target = state.PlayerTeam.[partyCursor]
            if BattleMon.isFainted target then
                queue <- [ $"{target.Species.Name} has no energy left!" ]
            elif partyCursor = 0 then
                queue <- [ $"{target.Species.Name} is already in battle!" ]
            elif forced then
                state <- Battle.choosePlayerReplacement partyCursor state
                queue <- state.Messages
                mode <- CommandMenu
                partyCursor <- 0
            else
                state <- Battle.switchMon partyCursor state
                queue <- state.Messages
                mode <- CommandMenu
                partyCursor <- 0

    member private _.AfterBattleAction() =
        if state.Outcome.IsNone && Battle.requiresPlayerReplacement state then
            mode <- PartyMenu true
            partyCursor <- 0
        elif state.Outcome.IsNone then
            mode <- CommandMenu

    interface Scene with
        member this.Update(buttons: Buttons) : Transition =
            let edge (now: bool) (was: bool) = now && not was
            let startedWithBox = box.IsSome

            if animFrames > 0 then
                animFrames <- animFrames - 1
                prev <- buttons
                Stay
            else
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
                            queue <- rest
                            box <- Some(TextBox.ofString (BattleScene.wrap msg))
                            Stay
                        | [] ->
                            if Battle.isOver state then
                                if not endNotified then
                                    endNotified <- true
                                    onBattleEnd state
                                Pop
                            elif startedWithBox then
                                this.AfterBattleAction()
                                Stay
                            else
                                match mode with
                                | CommandMenu ->
                                    if edge buttons.Left prev.Left && commandCursor % 2 = 1 then
                                        commandCursor <- commandCursor - 1
                                    elif edge buttons.Right prev.Right && commandCursor % 2 = 0 then
                                        commandCursor <- commandCursor + 1
                                    elif edge buttons.Up prev.Up || edge buttons.Down prev.Down then
                                        commandCursor <- (commandCursor + 2) % 4
                                    elif edge buttons.A prev.A then
                                        match commandCursor with
                                        | 0 ->
                                            mode <- MoveMenu
                                            moveCursor <- 0
                                        | 1 ->
                                            mode <- PartyMenu false
                                            partyCursor <- 0
                                        | 2 ->
                                            let items = this.BattleItems
                                            if items.IsEmpty then
                                                queue <- [ "No usable battle items!" ]
                                            else
                                                mode <- ItemMenu
                                                itemCursor <- 0
                                        | _ ->
                                            state <- Battle.run state
                                            queue <- state.Messages
                                    Stay

                                | MoveMenu ->
                                    let moves = state.Player.Moves

                                    if BattleMon.mustStruggle state.Player then
                                        let move = moves |> List.tryHead |> Option.defaultValue (Moves.byName "STRUGGLE")
                                        state <- Battle.chooseMove 0 state
                                        queue <- state.Messages
                                        mode <- CommandMenu
                                        currentAnim <- BattleAnim.effectForMove move
                                        animFrames <- BattleAnim.durationForMove move
                                    elif edge buttons.B prev.B then
                                        mode <- CommandMenu
                                    elif edge buttons.Down prev.Down then
                                        moveCursor <- min (moves.Length - 1) (moveCursor + 1)
                                    elif edge buttons.Up prev.Up then
                                        moveCursor <- max 0 (moveCursor - 1)
                                    elif edge buttons.A prev.A then
                                        if BattleMon.canUseMove moveCursor state.Player then
                                            let move = moves.[moveCursor]
                                            state <- Battle.chooseMove moveCursor state
                                            queue <- state.Messages
                                            mode <- CommandMenu
                                            currentAnim <- BattleAnim.effectForMove move
                                            animFrames <- BattleAnim.durationForMove move
                                    Stay

                                | ItemMenu ->
                                    let items = this.BattleItems
                                    if items.IsEmpty then
                                        mode <- CommandMenu
                                        queue <- [ "No usable battle items!" ]
                                    elif edge buttons.B prev.B then
                                        mode <- CommandMenu
                                    elif edge buttons.Down prev.Down then
                                        itemCursor <- min (items.Length - 1) (itemCursor + 1)
                                    elif edge buttons.Up prev.Up then
                                        itemCursor <- max 0 (itemCursor - 1)
                                    elif edge buttons.A prev.A then
                                        let item = items.[itemCursor]
                                        if bag.Balls |> List.exists (fun (id, qty) -> id = item && qty > 0) then
                                            mode <- CommandMenu
                                            itemCursor <- 0
                                            this.TryUseBall item
                                        else
                                            mode <- ItemTargetMenu item
                                            partyCursor <- 0
                                    Stay

                                | ItemTargetMenu item ->
                                    if edge buttons.B prev.B then
                                        mode <- ItemMenu
                                    elif edge buttons.Down prev.Down then
                                        partyCursor <- min (state.PlayerTeam.Length - 1) (partyCursor + 1)
                                    elif edge buttons.Up prev.Up then
                                        partyCursor <- max 0 (partyCursor - 1)
                                    elif edge buttons.A prev.A then
                                        mode <- CommandMenu
                                        this.TryUseHealingItem item partyCursor |> ignore
                                        partyCursor <- 0
                                        itemCursor <- 0
                                    Stay

                                | PartyMenu forced ->
                                    let maxCursor = if forced then state.PlayerTeam.Length - 1 else state.PlayerTeam.Length
                                    if not forced && edge buttons.B prev.B then
                                        mode <- CommandMenu
                                    elif edge buttons.Down prev.Down then
                                        partyCursor <- min maxCursor (partyCursor + 1)
                                    elif edge buttons.Up prev.Up then
                                        partyCursor <- max 0 (partyCursor - 1)
                                    elif edge buttons.A prev.A then
                                        this.SelectPartyMon forced
                                    Stay

                prev <- buttons
                result

        member this.Render(fb: Framebuffer) =
            BattleRenderer.drawField fb font state

            if animFrames > 0 then
                let r, g, b, a = BattleAnim.tintColor currentAnim
                for y in 0 .. Display.Height - 1 do
                    for x in 0 .. Display.Width - 1 do
                        fb.BlendPixel(x, y, r, g, b, a)

            match box with
            | Some b -> TextRenderer.draw fb font b
            | None ->
                if not (Battle.isOver state) then
                    match mode with
                    | CommandMenu -> BattleRenderer.drawCommandMenu fb font commandCursor
                    | MoveMenu -> BattleRenderer.drawMenu fb font state.Player.Moves state.Player.Pp moveCursor
                    | ItemMenu -> BattleRenderer.drawItemMenu fb font this.BattleItems itemCursor
                    | ItemTargetMenu _ -> BattleRenderer.drawPartyMenu fb font state.PlayerTeam partyCursor false
                    | PartyMenu forced -> BattleRenderer.drawPartyMenu fb font state.PlayerTeam partyCursor (not forced)

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
