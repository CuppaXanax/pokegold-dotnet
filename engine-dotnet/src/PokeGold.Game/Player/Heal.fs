namespace PokeGold.Game.Player

open PokeGold.Game.Data

/// Pure party-healing transforms (mirrors GSC HealParty / RestoreAllPP in
/// engine/pokemon/health.asm): HP = MaxHp, status cleared, each move's PP
/// restored to the move's base PP from the data table.
module Heal =

    /// Restore a single party Pokémon: HP = MaxHp, status = "", every move's
    /// current PP reset to that move's base PP (looked up by numeric move id).
    /// Unknown move ids (id 0 or out-of-range) default to PP = 0.
    let healMon (mon: PartyMon) : PartyMon =
        let healedMoves =
            mon.Moves
            |> List.map (fun (moveId, _currentPp) ->
                let maxPp =
                    Moves.tryByIndex moveId
                    |> Option.map (fun m -> m.Pp)
                    |> Option.defaultValue 0
                (moveId, maxPp))
        { mon with
            Hp = mon.MaxHp
            Status = ""
            Moves = healedMoves }

    /// Restore all party members to full HP, cleared status, and full PP.
    let healParty (party: PartyMon list) : PartyMon list = List.map healMon party
