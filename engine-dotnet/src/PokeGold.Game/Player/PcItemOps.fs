namespace PokeGold.Game.Player

/// Pure PC-item and mailbox operations for the Player's PC.
/// All functions take and return PlayerState (immutable).
/// Guards enforce GSC rules: bag/stash counts, 99-cap per stack, mailbox capacity 10.
module PcItemOps =

    [<Literal>]
    let MaxStack = 99

    // ── Stash helpers ─────────────────────────────────────────────────────────

    let private countInStash (itemId: string) (stash: (string * int) list) : int =
        stash
        |> List.tryFind (fun (id, _) -> id = itemId)
        |> Option.map snd
        |> Option.defaultValue 0

    let private addToStash (itemId: string) (qty: int) (stash: (string * int) list) : (string * int) list =
        match stash |> List.tryFindIndex (fun (id, _) -> id = itemId) with
        | Some i ->
            stash |> List.mapi (fun j (id, q) ->
                if j = i then (id, min MaxStack (q + qty)) else (id, q))
        | None -> stash @ [(itemId, min MaxStack qty)]

    let private removeFromStash (itemId: string) (qty: int) (stash: (string * int) list) : (string * int) list =
        stash |> List.choose (fun (id, q) ->
            if id = itemId then
                let left = q - qty
                if left > 0 then Some (id, left) else None
            else Some (id, q))

    // ── Item ops ──────────────────────────────────────────────────────────────

    /// Move qty of itemId from the bag into the PC item stash.
    /// Fails if the bag has fewer than qty.
    let depositItem (itemId: string) (qty: int) (player: PlayerState) : Result<PlayerState, string> =
        if Bag.count itemId player.Bag < qty then
            Error(sprintf "Not enough %s in bag!" itemId)
        else
            let newBag   = Bag.remove itemId qty player.Bag
            let newStash = addToStash itemId qty player.Pc.PcItems
            Ok { player with Bag = newBag; Pc = { player.Pc with PcItems = newStash } }

    /// Move qty of itemId from the PC item stash back into the bag.
    /// Fails if the stash has fewer than qty.
    let withdrawItem (itemId: string) (qty: int) (player: PlayerState) : Result<PlayerState, string> =
        if countInStash itemId player.Pc.PcItems < qty then
            Error(sprintf "Not enough %s in PC!" itemId)
        else
            let newStash = removeFromStash itemId qty player.Pc.PcItems
            let newBag   = Bag.add itemId qty player.Bag
            Ok { player with Bag = newBag; Pc = { player.Pc with PcItems = newStash } }

    /// Remove up to qty of itemId from the PC item stash (silently clamps; never errors).
    let tossItem (itemId: string) (qty: int) (player: PlayerState) : PlayerState =
        let actual   = min qty (countInStash itemId player.Pc.PcItems)
        let newStash = removeFromStash itemId actual player.Pc.PcItems
        { player with Pc = { player.Pc with PcItems = newStash } }

    // ── Mailbox ops ───────────────────────────────────────────────────────────

    /// Append a mail message to the player's PC mailbox.
    /// Fails with "MAILBOX is full!" if already at Storage.mailboxCapacity (10).
    let storeMail (mail: Mail) (player: PlayerState) : Result<PlayerState, string> =
        if player.Pc.Mailbox.Length >= Storage.mailboxCapacity then
            Error "MAILBOX is full!"
        else
            Ok { player with Pc = { player.Pc with Mailbox = player.Pc.Mailbox @ [mail] } }

    /// Return the mail at index, or None if out of range.
    let readMail (index: int) (player: PlayerState) : Mail option =
        if index < 0 || index >= player.Pc.Mailbox.Length then None
        else Some player.Pc.Mailbox.[index]

    /// Remove the mail at index from the mailbox (silently ignore out-of-range).
    /// NOTE: In GSC "take" moves mail back to a held-item slot on the party;
    /// for this slice we only remove from the mailbox — party mail-holding is
    /// not yet modelled, so the held-item attachment is a no-op.
    let takeMail (index: int) (player: PlayerState) : PlayerState =
        if index < 0 || index >= player.Pc.Mailbox.Length then player
        else
            let newMailbox =
                player.Pc.Mailbox
                |> List.indexed
                |> List.filter (fun (i, _) -> i <> index)
                |> List.map snd
            { player with Pc = { player.Pc with Mailbox = newMailbox } }
