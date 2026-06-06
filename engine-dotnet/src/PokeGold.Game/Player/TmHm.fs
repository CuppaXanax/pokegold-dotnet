namespace PokeGold.Game.Player

open PokeGold.Game.Data

module TmHm =

    /// Map TM/HM item IDs to the move they teach.
    let moveForItem (item: string) : string option =
        match item with
        | "HM_CUT" | "HM01" -> Some "CUT"
        | "HM_FLY" | "HM02" -> Some "FLY"
        | "HM_SURF" | "HM03" -> Some "SURF"
        | "HM_STRENGTH" | "HM04" -> Some "STRENGTH"
        | "HM_FLASH" | "HM05" -> Some "FLASH"
        | "HM_WHIRLPOOL" | "HM06" -> Some "WHIRLPOOL"
        | "HM_WATERFALL" | "HM07" -> Some "WATERFALL"
        | "TM01" -> Some "DYNAMICPUNCH"
        | "TM02" -> Some "HEADBUTT"
        | "TM03" -> Some "CURSE"
        | "TM04" -> Some "ROLLOUT"
        | "TM05" -> Some "ROAR"
        | "TM06" -> Some "TOXIC"
        | "TM07" -> Some "ZAP_CANNON"
        | "TM08" -> Some "ROCK_SMASH"
        | "TM09" -> Some "PSYCH_UP"
        | _ -> None

    /// Teach a TM/HM move to a party mon.
    let teach (moveName: string) (mon: PartyMon) : PartyMon option =
        match MovesData.byIndex |> Array.tryFindIndex (fun m -> m.Name = moveName) with
        | None -> None
        | Some _ ->
            let updatedMoves = MoveLearn.tryLearnMove moveName mon.Moves
            if updatedMoves = mon.Moves then
                None
            else
                Some { mon with Moves = updatedMoves }
