namespace PokeGold.Game.Battle

open PokeGold.Game.Data

/// Basic Gen-2 style battle AI: score each available move and pick the highest.
/// Source: engine/battle/ai/{move,scoring,switch,items}.asm.
module BattleAI =

    type TrainerProfile = { MoveFlags: string list; ItemSwitchFlags: string list; Items: string list }

    type TrainerItemUse = { Item: string; Enemy: BattleMon }

    let private emptyProfile = { MoveFlags = []; ItemSwitchFlags = []; Items = [] }

    let profileForTrainer group id = Trainers.lookupByName group id |> Option.map (fun trainer -> { MoveFlags = trainer.AiMoveFlags; ItemSwitchFlags = trainer.AiItemSwitchFlags; Items = trainer.AiItems }) |> Option.defaultValue emptyProfile

    /// Score a single move for the enemy to use against the player.
    /// Higher score = better move choice.
    let scoreMove (user: BattleMon) (target: BattleMon) (move: MoveData) : int =
        let mutable score = 100

        if move.Power > 0 then
            score <- score + move.Power / 2

            if move.Type = user.Species.Type1 || move.Type = user.Species.Type2 then
                score <- score + 20

            let eff = Damage.effectivenessTimesTen move target
            if eff > 10 then score <- score + 40
            elif eff = 0 then score <- score - 200
            elif eff < 10 then score <- score - 20
        else
            match move.Effect with
            | "EFFECT_SLEEP" | "EFFECT_PARALYZE" | "EFFECT_TOXIC" | "EFFECT_CONFUSE" ->
                if target.Status = Healthy && target.Volatile.Confusion.IsNone then
                    score <- score + 30
                else
                    score <- score - 50
            | _ ->
                score <- score + 10

        if move.Power > 0 && target.Hp > 0 then
            let estDmg = move.Power * 2
            if estDmg > target.Hp * 3 then
                score <- score - 10

        score

    let private isSetupMove move =
        match move.Effect with
        | "EFFECT_ATTACK_UP"
        | "EFFECT_DEFENSE_UP"
        | "EFFECT_SPEED_UP"
        | "EFFECT_SP_ATK_UP"
        | "EFFECT_SP_DEF_UP"
        | "EFFECT_ACCURACY_UP"
        | "EFFECT_EVASION_UP"
        | "EFFECT_REFLECT"
        | "EFFECT_LIGHT_SCREEN"
        | "EFFECT_SAFEGUARD"
        | "EFFECT_FOCUS_ENERGY" -> true
        | _ -> false

    /// Source profile-aware score. The basic layer remains legal-move and
    /// type-aware; setup is only encouraged on the trainer's first turn.
    let scoreMoveWithProfile (profile: TrainerProfile) enemyTurnsTaken (user: BattleMon) (target: BattleMon) (move: MoveData) =
        let mutable score = scoreMove user target move

        if profile.MoveFlags |> List.contains "AI_SETUP" then
            if enemyTurnsTaken = 0 && isSetupMove move then
                score <- score + 25
            elif enemyTurnsTaken > 0 && isSetupMove move then
                score <- score - 15

        if profile.MoveFlags |> List.contains "AI_STATUS" then
            match move.Effect with
            | "EFFECT_SLEEP"
            | "EFFECT_PARALYZE"
            | "EFFECT_TOXIC"
            | "EFFECT_POISON"
            | "EFFECT_CONFUSE" when target.Status <> Healthy || target.Volatile.Confusion.IsSome ->
                score <- score - 50
            | _ -> ()

        score

    /// Pick a legal move using the generated trainer-class profile.
    let chooseMoveWithProfile (profile: TrainerProfile) enemyTurnsTaken (user: BattleMon) (target: BattleMon) : (MoveData * int) option =
        if user.Moves.IsEmpty then None
        else
            let indexed = user.Moves |> List.mapi (fun i m -> (m, i))
            let hasTrackedPp = not user.Pp.IsEmpty
            let ppAvailable =
                indexed
                |> List.filter (fun (_, i) ->
                    (not hasTrackedPp) || (i < user.Pp.Length && user.Pp.[i] > 0))
            let usable = ppAvailable |> List.filter (fun (_, i) -> user.Volatile.DisabledMoveIndex <> Some i)
            let candidates = if usable.IsEmpty then ppAvailable else usable

            if candidates.IsEmpty then None
            else
                candidates
                |> List.map (fun (m, i) -> (m, i, scoreMoveWithProfile profile enemyTurnsTaken user target m))
                |> List.sortByDescending (fun (_, _, s) -> s)
                |> List.tryHead
                |> Option.map (fun (m, i, _) -> (m, i))

    /// Pick the best move index for the enemy, given their available moves.
    /// Returns (MoveData, moveIndex) or None if must Struggle.
    let chooseMove (user: BattleMon) (target: BattleMon) : (MoveData * int) option =
        chooseMoveWithProfile emptyProfile 0 user target

    let private bestAvailableScore (user: BattleMon) (target: BattleMon) =
        chooseMove user target
        |> Option.map (fun (move, _) -> scoreMove user target move)
        |> Option.defaultValue 0

    let private bestAvailableScoreWithProfile profile enemyTurnsTaken user target =
        chooseMoveWithProfile profile enemyTurnsTaken user target
        |> Option.map (fun (move, _) -> scoreMoveWithProfile profile enemyTurnsTaken user target move)
        |> Option.defaultValue 0

    /// Source switch policy enables the matchup gate; classes without a source
    /// switch bit retain no automatic AI switch behavior.
    let chooseSwitchWithProfile (profile: TrainerProfile) enemyTurnsTaken (active: BattleMon) (target: BattleMon) (team: BattleMon list) : int option =
        let hasSwitchPolicy =
            profile.ItemSwitchFlags
            |> List.exists (fun flag -> flag = "SWITCH_OFTEN" || flag = "SWITCH_RARELY" || flag = "SWITCH_SOMETIMES")

        if profile <> emptyProfile && not hasSwitchPolicy then
            None
        else
            let activeScore = bestAvailableScoreWithProfile profile enemyTurnsTaken active target
            let activeLowHp = active.Hp * 4 <= active.MaxHp
            let activeIsWalled =
                active.Moves
                |> List.filter (fun move -> move.Power > 0)
                |> List.forall (fun move -> Damage.effectivenessTimesTen move target = 0)

            if not activeLowHp && not activeIsWalled then
                None
            else
                team
                |> List.mapi (fun index mon -> index, mon)
                |> List.filter (fun (index, mon) -> index > 0 && not (BattleMon.isFainted mon))
                |> List.map (fun (index, mon) -> index, mon, bestAvailableScoreWithProfile profile enemyTurnsTaken mon target)
                |> List.sortByDescending (fun (_, _, score) -> score)
                |> List.tryFind (fun (_, _, score) -> score > activeScore + 20)
                |> Option.map (fun (index, _, _) -> index)

    /// Pick a healthier bench mon when the active matchup is poor enough to
    /// justify spending the trainer's turn switching.
    let chooseSwitch (active: BattleMon) (target: BattleMon) (team: BattleMon list) : int option =
        chooseSwitchWithProfile emptyProfile 0 active target team

    let private highestLevel (team: BattleMon list) =
        team |> List.map (fun mon -> mon.Level) |> List.max

    let private sourceItemPriority =
        [ "FULL_RESTORE"; "MAX_POTION"; "HYPER_POTION"; "SUPER_POTION"; "POTION"
          "X_ACCURACY"; "FULL_HEAL"; "GUARD_SPEC"; "DIRE_HIT"; "X_ATTACK"
          "X_DEFEND"; "X_SPEED"; "X_SPECIAL" ]

    let private healEnemy item (enemy: BattleMon) : BattleMon option =
        match item with
        | "FULL_RESTORE" -> Some { enemy with Hp = enemy.MaxHp; Status = Healthy; Volatile = { enemy.Volatile with Confusion = None } }
        | "MAX_POTION" -> Some { enemy with Hp = enemy.MaxHp }
        | "HYPER_POTION" -> Some { enemy with Hp = min enemy.MaxHp (enemy.Hp + 200) }
        | "SUPER_POTION" -> Some { enemy with Hp = min enemy.MaxHp (enemy.Hp + 50) }
        | "POTION" -> Some { enemy with Hp = min enemy.MaxHp (enemy.Hp + 20) }
        | "FULL_HEAL" -> Some { enemy with Status = Healthy; Volatile = { enemy.Volatile with Confusion = None } }
        | "X_ACCURACY" -> Some { enemy with Volatile = { enemy.Volatile with XAccuracy = true } }
        | "GUARD_SPEC" -> Some { enemy with Volatile = { enemy.Volatile with Mist = true } }
        | "DIRE_HIT" -> Some { enemy with Volatile = { enemy.Volatile with FocusEnergy = true } }
        | "X_ATTACK" -> Some { enemy with AtkStage = min 6 (enemy.AtkStage + 1) }
        | "X_DEFEND" -> Some { enemy with DefStage = min 6 (enemy.DefStage + 1) }
        | "X_SPEED" -> Some { enemy with SpdStage = min 6 (enemy.SpdStage + 1) }
        | "X_SPECIAL" -> Some { enemy with SpAtkStage = min 6 (enemy.SpAtkStage + 1) }
        | _ -> None

    /// Select a legal source trainer item. Trainer class items are considered
    /// only for the class's highest-level active monster, matching AI_TryItem.
    let tryUseItem (profile: TrainerProfile) (remainingItems: string list) (enemyTeam: BattleMon list) (enemy: BattleMon) enemyTurnsTaken =
        if remainingItems.IsEmpty || enemy.Level <> highestLevel enemyTeam then
            None
        else
            let lowHp = enemy.Hp * 2 <= enemy.MaxHp
            let statused = enemy.Status <> Healthy || enemy.Volatile.Confusion.IsSome

            sourceItemPriority
            |> List.filter (fun item -> remainingItems |> List.contains item)
            |> List.tryPick (fun item ->
                let usable =
                    match item with
                    | "FULL_RESTORE" -> (lowHp || statused) && (enemy.Hp < enemy.MaxHp || statused)
                    | "FULL_HEAL" -> statused
                    | "MAX_POTION" | "HYPER_POTION" | "SUPER_POTION" | "POTION" -> lowHp && enemy.Hp < enemy.MaxHp
                    | "X_ACCURACY" -> enemyTurnsTaken = 0 && not enemy.Volatile.XAccuracy
                    | "GUARD_SPEC" -> enemyTurnsTaken = 0 && not enemy.Volatile.Mist
                    | "DIRE_HIT" -> enemyTurnsTaken = 0 && not enemy.Volatile.FocusEnergy
                    | "X_ATTACK" -> enemyTurnsTaken = 0 && enemy.AtkStage < 6
                    | "X_DEFEND" -> enemyTurnsTaken = 0 && enemy.DefStage < 6
                    | "X_SPEED" -> enemyTurnsTaken = 0 && enemy.SpdStage < 6
                    | "X_SPECIAL" -> enemyTurnsTaken = 0 && enemy.SpAtkStage < 6
                    | _ -> false

                if usable then
                    healEnemy item enemy |> Option.map (fun updated -> { Item = item; Enemy = updated })
                else
                    None)
