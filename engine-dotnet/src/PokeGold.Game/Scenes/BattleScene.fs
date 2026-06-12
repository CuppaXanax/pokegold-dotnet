namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Text
open PokeGold.Game.Render
open PokeGold.Game.Battle
open PokeGold.Game.Player

type private BattleMenuMode =
    | MoveMenu
    | ItemMenu

/// The wild-battle scene. Drains the battle's message queue through the M5
/// typewriter box (word-wrapped, each message waiting for a button), then shows
/// the move menu. Selecting a move resolves a full turn and enqueues its
/// messages; the scene pops when the battle ends.
type BattleScene(font: Font, initial: BattleState, ?onBattleEnd: BattleState -> unit, ?bag: Bag, ?onBagChange: Bag -> unit, ?onCatch: BattleMon -> unit) =
    let mutable state = initial
    let mutable queue : string list = initial.Messages
    let onBattleEnd = defaultArg onBattleEnd (fun _ -> ())
    let onBagChange = defaultArg onBagChange ignore
    let onCatch = defaultArg onCatch ignore
    let mutable bag = defaultArg bag Bag.empty
    let mutable box : TextBoxState option = None
    let mutable cursor = 0
    let mutable itemCursor = 0
    let mutable mode = MoveMenu
    let mutable prev = Buttons.none
    let mutable animFrames = 0
    let mutable currentAnim = NoAnim

    /// Build the demo encounter: the player's CYNDAQUIL vs a wild PIDGEY.
    static member StartDemo(content: Content) : BattleScene =
        let player =
            BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE"; Moves.byName "LEER" ]

        let enemy = BattleMon.ofSpecies (Species.byName "PIDGEY") 3 [ Moves.byName "TACKLE" ]
        BattleScene(content.Font, Battle.create player enemy 0x1234u)

    member _.CurrentBag = bag
    member _.CurrentState = state

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

    member private _.SetBattlePlayer(mon: BattleMon) =
        state <-
            { state with
                Player = mon
                PlayerTeam = state.PlayerTeam |> List.mapi (fun i existing -> if i = 0 then mon else existing) }

    member private this.TryUseHealingItem(item: string) : bool =
        let current = state.Player
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
            this.SetBattlePlayer healed
            bag <- Bag.remove item 1 bag
            onBagChange bag
            let itemName = item.Replace("_", " ")
            queue <- [ $"{current.Species.Name} used {itemName}!" ]
            true

    member private _.TryUseBall(ball: string) =
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

    interface Scene with
        member this.Update(buttons: Buttons) : Transition =
            let edge (now: bool) (was: bool) = now && not was
            let startedWithBox = box.IsSome

            if animFrames > 0 then
                animFrames <- animFrames - 1
                prev <- buttons
                Stay
            else
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
                            if Battle.isOver state then
                                onBattleEnd state
                                Pop
                            elif startedWithBox then
                                // Don't read the dismiss press as a menu action this frame.
                                Stay
                            else
                                match mode with
                                | ItemMenu ->
                                    let items = this.BattleItems
                                    if items.IsEmpty then
                                        mode <- MoveMenu
                                        queue <- [ "No usable battle items!" ]
                                    elif edge buttons.B prev.B || edge buttons.Left prev.Left then
                                        mode <- MoveMenu
                                    elif edge buttons.Down prev.Down then
                                        itemCursor <- min (items.Length - 1) (itemCursor + 1)
                                    elif edge buttons.Up prev.Up then
                                        itemCursor <- max 0 (itemCursor - 1)
                                    elif edge buttons.A prev.A then
                                        let item = items.[itemCursor]
                                        mode <- MoveMenu
                                        itemCursor <- 0
                                        if bag.Balls |> List.exists (fun (id, qty) -> id = item && qty > 0) then
                                            this.TryUseBall item
                                        else
                                            this.TryUseHealingItem item |> ignore
                                    Stay

                                | MoveMenu ->
                                    let moves = state.Player.Moves

                                    if BattleMon.mustStruggle state.Player then
                                        let move = moves |> List.tryHead |> Option.defaultValue (Moves.byName "STRUGGLE")
                                        state <- Battle.chooseMove 0 state
                                        queue <- state.Messages
                                        currentAnim <- BattleAnim.effectForMove move
                                        animFrames <- BattleAnim.duration currentAnim
                                    elif edge buttons.Down prev.Down then
                                        cursor <- min (moves.Length - 1) (cursor + 1)
                                    elif edge buttons.Up prev.Up then
                                        cursor <- max 0 (cursor - 1)
                                    elif edge buttons.Right prev.Right then
                                        mode <- ItemMenu
                                        itemCursor <- 0
                                    elif edge buttons.A prev.A then
                                        if BattleMon.canUseMove cursor state.Player then
                                            let move = moves.[cursor]
                                            state <- Battle.chooseMove cursor state
                                            queue <- state.Messages
                                            currentAnim <- BattleAnim.effectForMove move
                                            animFrames <- BattleAnim.duration currentAnim
                                        // else: 0 PP — do nothing (move blocked)
                                    elif edge buttons.B prev.B then
                                        state <- Battle.run state
                                        queue <- state.Messages

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
                    | MoveMenu -> BattleRenderer.drawMenu fb font state.Player.Moves state.Player.Pp cursor
                    | ItemMenu -> BattleRenderer.drawItemMenu fb font this.BattleItems itemCursor

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
