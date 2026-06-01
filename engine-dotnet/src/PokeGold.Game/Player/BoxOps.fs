namespace PokeGold.Game.Player

/// Pure box-operation functions for Bill's PC storage.
/// All functions take and return updated PlayerState (immutable).
/// Guards enforce GSC rules: min-party (≥1), party capacity (≤6), box capacity (≤20).
module BoxOps =

    /// Maximum party size — GSC PARTY_LENGTH = 6.
    [<Literal>]
    let partyLength = 6

    /// Deposit party[partyIndex] into the current box.
    /// Fails if it would leave the party empty (party.Length ≤ 1) or
    /// if the current box is already at capacity (Storage.monsPerBox = 20).
    let deposit (partyIndex: int) (player: PlayerState) : Result<PlayerState, string> =
        let party = player.Party
        if partyIndex < 0 || partyIndex >= party.Length then
            Error "Invalid party slot."
        elif party.Length <= 1 then
            Error "Can't deposit the last POKeMON!"
        else
            let box = player.Pc.Boxes.[player.Pc.CurrentBox]
            if box.Mons.Length >= Storage.monsPerBox then
                Error "BOX is full!"
            else
                let mon      = List.item partyIndex party
                let newParty = party |> List.indexed |> List.filter (fun (i, _) -> i <> partyIndex) |> List.map snd
                let newBox   = { box with Mons = box.Mons @ [mon] }
                let newBoxes = player.Pc.Boxes |> Array.mapi (fun i b -> if i = player.Pc.CurrentBox then newBox else b)
                Ok { player with Party = newParty; Pc = { player.Pc with Boxes = newBoxes } }

    /// Withdraw box[boxIndex][monIndex] into the party.
    /// Fails if the party is already full (partyLength = 6).
    let withdraw (boxIndex: int) (monIndex: int) (player: PlayerState) : Result<PlayerState, string> =
        if player.Party.Length >= partyLength then
            Error "Your party is full!"
        elif boxIndex < 0 || boxIndex >= player.Pc.Boxes.Length then
            Error "Invalid box."
        else
            let box = player.Pc.Boxes.[boxIndex]
            if monIndex < 0 || monIndex >= box.Mons.Length then
                Error "No POKeMON there."
            else
                let mon       = List.item monIndex box.Mons
                let newBoxMons = box.Mons |> List.indexed |> List.filter (fun (i, _) -> i <> monIndex) |> List.map snd
                let newBox    = { box with Mons = newBoxMons }
                let newBoxes  = player.Pc.Boxes |> Array.mapi (fun i b -> if i = boxIndex then newBox else b)
                Ok { player with Party = player.Party @ [mon]; Pc = { player.Pc with Boxes = newBoxes } }

    /// Remove box[boxIndex][monIndex] from the box (release / set free).
    /// Out-of-range indices are silently ignored.
    let release (boxIndex: int) (monIndex: int) (player: PlayerState) : PlayerState =
        if boxIndex < 0 || boxIndex >= player.Pc.Boxes.Length then player
        else
            let box = player.Pc.Boxes.[boxIndex]
            if monIndex < 0 || monIndex >= box.Mons.Length then player
            else
                let newBoxMons = box.Mons |> List.indexed |> List.filter (fun (i, _) -> i <> monIndex) |> List.map snd
                let newBox   = { box with Mons = newBoxMons }
                let newBoxes = player.Pc.Boxes |> Array.mapi (fun i b -> if i = boxIndex then newBox else b)
                { player with Pc = { player.Pc with Boxes = newBoxes } }

    /// Set the active box to boxIndex (clamped to 0..numBoxes-1).
    /// Does not move any Pokémon.
    let switchBox (boxIndex: int) (player: PlayerState) : PlayerState =
        let idx = max 0 (min (Storage.numBoxes - 1) boxIndex)
        { player with Pc = { player.Pc with CurrentBox = idx } }

    /// Rename box[boxIndex]. Out-of-range index is silently ignored.
    let renameBox (boxIndex: int) (name: string) (player: PlayerState) : PlayerState =
        if boxIndex < 0 || boxIndex >= player.Pc.Boxes.Length then player
        else
            let newBoxes = player.Pc.Boxes |> Array.mapi (fun i b -> if i = boxIndex then { b with Name = name } else b)
            { player with Pc = { player.Pc with Boxes = newBoxes } }
