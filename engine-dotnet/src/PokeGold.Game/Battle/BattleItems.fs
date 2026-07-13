namespace PokeGold.Game.Battle

open PokeGold.Game.Data

/// Target selected for a battle item.
type BattleItemTarget =
    | ActiveTarget
    | PartyTarget of partyIndex: int
    | MoveTarget of partyIndex: int * moveIndex: int

/// Pure, source-backed battle-item transitions. BattleScene owns target menus,
/// bag consumption, and the consumed player turn.
module BattleItems =
    let private hpRestoreItems =
        Set.ofList [ "POTION"; "SUPER_POTION"; "HYPER_POTION"; "MAX_POTION"
                     "FRESH_WATER"; "SODA_POP"; "LEMONADE"; "MOOMOO_MILK"
                     "BERRY_JUICE"; "RAGECANDYBAR"; "BERRY"; "GOLD_BERRY"
                     "ENERGYPOWDER"; "ENERGY_ROOT" ]

    let private statusCures : Map<string, StatusCondition -> bool> =
        Map.ofList [ "ANTIDOTE", (function Poison | BadPoison _ -> true | _ -> false)
                     "BURN_HEAL", (function Burn -> true | _ -> false)
                     "ICE_HEAL", (function Freeze -> true | _ -> false)
                     "AWAKENING", (function Sleep _ -> true | _ -> false)
                     "PARLYZ_HEAL", (function Paralysis -> true | _ -> false)
                     "PSNCUREBERRY", (function Poison | BadPoison _ -> true | _ -> false)
                     "PRZCUREBERRY", (function Paralysis -> true | _ -> false)
                     "BURNT_BERRY", (function Freeze -> true | _ -> false)
                     "ICE_BERRY", (function Burn -> true | _ -> false)
                     "MINT_BERRY", (function Sleep _ -> true | _ -> false)
                     "FULL_HEAL", (function Healthy -> false | _ -> true)
                     "HEAL_POWDER", (function Healthy -> false | _ -> true)
                     "MIRACLEBERRY", (function Healthy -> false | _ -> true) ]

    let private fullStatusCures = Set.ofList [ "FULL_HEAL"; "HEAL_POWDER"; "MIRACLEBERRY" ]
    let private reviveItems = Set.ofList [ "REVIVE"; "MAX_REVIVE"; "REVIVAL_HERB" ]
    let private selectedMovePpItems = Set.ofList [ "ETHER"; "MAX_ETHER"; "MYSTERYBERRY" ]
    let private allMovePpItems = Set.ofList [ "ELIXER"; "MAX_ELIXER" ]
    let private directItems = Set.ofList [ "X_ATTACK"; "X_DEFEND"; "X_SPEED"; "X_SPECIAL"; "X_ACCURACY"; "GUARD_SPEC"; "DIRE_HIT"; "POKE_DOLL"; "BITTER_BERRY" ]

    let isSupported item =
        Set.contains item hpRestoreItems
        || Map.containsKey item statusCures
        || item = "FULL_RESTORE"
        || Set.contains item reviveItems
        || Set.contains item selectedMovePpItems
        || Set.contains item allMovePpItems
        || Set.contains item directItems

    let requiresPartyTarget item =
        Set.contains item hpRestoreItems
        || Map.containsKey item statusCures
        || item = "FULL_RESTORE"
        || Set.contains item reviveItems
        || Set.contains item selectedMovePpItems
        || Set.contains item allMovePpItems

    let requiresMoveTarget item = Set.contains item selectedMovePpItems
    let isDirect item = Set.contains item directItems

    let private setPlayerTeam team state =
        let player = team |> List.tryHead |> Option.defaultValue state.Player
        { state with Player = player; PlayerTeam = team }

    let private updatePartyMon partyIndex update state =
        if partyIndex < 0 || partyIndex >= state.PlayerTeam.Length then
            None
        else
            match update state.PlayerTeam.[partyIndex] with
            | None -> None
            | Some updated ->
                state.PlayerTeam
                |> List.mapi (fun index current -> if index = partyIndex then updated else current)
                |> fun team -> setPlayerTeam team state
                |> Some

    let private restoreHp item (mon: BattleMon) : BattleMon option =
        if mon.Hp <= 0 || mon.Hp >= mon.MaxHp then
            None
        else
            Items.byId
            |> Map.tryFind item
            |> Option.map (fun data ->
                let amount =
                    match item with
                    | "ENERGYPOWDER" -> 50
                    | "ENERGY_ROOT" -> 200
                    | _ -> data.Param
                let hp = if amount < 0 then mon.MaxHp else min mon.MaxHp (mon.Hp + amount)
                { mon with Hp = hp })

    let private cureStatus item (mon: BattleMon) : BattleMon option =
        if mon.Hp <= 0 then
            None
        elif Set.contains item fullStatusCures then
            if mon.Status = Healthy && mon.Volatile.Confusion.IsNone then
                None
            else
                Some { mon with Status = Healthy; Volatile = { mon.Volatile with Confusion = None } }
        else
            statusCures
            |> Map.tryFind item
            |> Option.bind (fun cures -> if cures mon.Status then Some { mon with Status = Healthy } else None)

    let private fullRestore (mon: BattleMon) : BattleMon option =
        if mon.Hp <= 0 || (mon.Hp = mon.MaxHp && mon.Status = Healthy && mon.Volatile.Confusion.IsNone) then
            None
        else
            Some { mon with Hp = mon.MaxHp; Status = Healthy; Volatile = { mon.Volatile with Confusion = None } }

    let private revive item (mon: BattleMon) : BattleMon option =
        if not (BattleMon.isFainted mon) then
            None
        else
            let hp = if item = "REVIVE" then max 1 (mon.MaxHp / 2) else mon.MaxHp
            Some { mon with Hp = hp; Status = Healthy; Volatile = { mon.Volatile with Confusion = None } }

    let private bitterFriendshipDelta item friendship =
        match item with
        | "HEAL_POWDER" | "ENERGYPOWDER" -> if friendship < 200 then -5 else -10
        | "ENERGY_ROOT" -> if friendship < 200 then -10 else -15
        | "REVIVAL_HERB" -> if friendship < 200 then -15 else -20
        | _ -> 0

    let private applyBitterFriendship item (mon: BattleMon) =
        BattleMon.adjustFriendship (bitterFriendshipDelta item (BattleMon.friendship mon)) mon

    let private applyItem item update mon =
        update mon |> Option.map (applyBitterFriendship item)

    let private restorePp amount moveIndex (mon: BattleMon) : BattleMon option =
        if moveIndex < 0 || moveIndex >= mon.Moves.Length || moveIndex >= mon.Pp.Length then
            None
        else
            let maximum = mon.Moves.[moveIndex].Pp
            let restored = if amount < 0 then maximum else min maximum (mon.Pp.[moveIndex] + amount)
            if restored = mon.Pp.[moveIndex] then
                None
            else
                Some { mon with Pp = mon.Pp |> List.mapi (fun index current -> if index = moveIndex then restored else current) }

    let private restoreAllPp amount (mon: BattleMon) : BattleMon option =
        let pp =
            mon.Pp
            |> List.mapi (fun index current ->
                if index >= mon.Moves.Length then current
                elif amount < 0 then mon.Moves.[index].Pp
                else min mon.Moves.[index].Pp (current + amount))
        if pp = mon.Pp then None else Some { mon with Pp = pp }

    let private updateActive (update: BattleMon -> BattleMon option) (state: BattleState) =
        match update state.Player with
        | None -> None
        | Some player ->
            state.PlayerTeam
            |> List.mapi (fun index current -> if index = 0 then player else current)
            |> fun team -> { state with Player = player; PlayerTeam = team }
            |> Some

    let private raiseStat item (mon: BattleMon) : BattleMon option =
        let raiseStage stage update =
            // XItemEffect consumes before RaiseStat, including when the stat
            // is already capped and RaiseStat reports its failure text.
            if stage >= 6 then Some mon else Some(update (stage + 1) mon)
        match item with
        | "X_ATTACK" -> raiseStage mon.AtkStage (fun stage current -> { current with AtkStage = stage })
        | "X_DEFEND" -> raiseStage mon.DefStage (fun stage current -> { current with DefStage = stage })
        | "X_SPEED" -> raiseStage mon.SpdStage (fun stage current -> { current with SpdStage = stage })
        | "X_SPECIAL" -> raiseStage mon.SpAtkStage (fun stage current -> { current with SpAtkStage = stage })
        | _ -> None

    let private directUse item state =
        match item with
        | "X_ATTACK" | "X_DEFEND" | "X_SPEED" | "X_SPECIAL" -> updateActive (raiseStat item) state
        | "X_ACCURACY" ->
            updateActive
                (fun mon ->
                    if mon.Volatile.XAccuracy then None
                    else Some { mon with Volatile = { mon.Volatile with XAccuracy = true } })
                state
        | "GUARD_SPEC" ->
            updateActive
                (fun mon ->
                    if mon.Volatile.Mist then None
                    else Some { mon with Volatile = { mon.Volatile with Mist = true } })
                state
        | "DIRE_HIT" ->
            updateActive
                (fun mon ->
                    if mon.Volatile.FocusEnergy then None
                    else Some { mon with Volatile = { mon.Volatile with FocusEnergy = true } })
                state
        | "BITTER_BERRY" ->
            updateActive
                (fun mon ->
                    if mon.Volatile.Confusion.IsNone then None
                    else Some { mon with Volatile = { mon.Volatile with Confusion = None } })
                state
        | "POKE_DOLL" when not (Battle.isTrainerBattle state) -> Some { state with Outcome = Some Ran }
        | _ -> None

    let tryUse item target state =
        match item, target with
        | "FULL_RESTORE", PartyTarget partyIndex -> updatePartyMon partyIndex fullRestore state
        | item, PartyTarget partyIndex when Set.contains item reviveItems -> updatePartyMon partyIndex (applyItem item (revive item)) state
        | item, PartyTarget partyIndex when Set.contains item hpRestoreItems -> updatePartyMon partyIndex (applyItem item (restoreHp item)) state
        | item, PartyTarget partyIndex when Map.containsKey item statusCures -> updatePartyMon partyIndex (applyItem item (cureStatus item)) state
        | "ELIXER", PartyTarget partyIndex -> updatePartyMon partyIndex (restoreAllPp 10) state
        | "MAX_ELIXER", PartyTarget partyIndex -> updatePartyMon partyIndex (restoreAllPp -1) state
        | "ETHER", MoveTarget(partyIndex, moveIndex) -> updatePartyMon partyIndex (restorePp 10 moveIndex) state
        | "MAX_ETHER", MoveTarget(partyIndex, moveIndex) -> updatePartyMon partyIndex (restorePp -1 moveIndex) state
        | "MYSTERYBERRY", MoveTarget(partyIndex, moveIndex) -> updatePartyMon partyIndex (restorePp 5 moveIndex) state
        | item, ActiveTarget when Set.contains item directItems -> directUse item state
        | _ -> None