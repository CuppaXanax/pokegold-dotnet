namespace PokeGold.Game.Player

open PokeGold.Game.Data

/// A bag organised into four pockets (insertion-order preserved within each pocket).
type Bag =
    { Items: (string * int) list
      Balls: (string * int) list
      KeyItems: (string * int) list
      TmHm: (string * int) list }

module Bag =

    [<Literal>]
    let MaxStack = 99

    let empty = { Items = []; Balls = []; KeyItems = []; TmHm = [] }

    let private pocketOf (itemId: string) : Pocket =
        match ItemsData.byId |> Map.tryFind itemId with
        | Some d -> d.Pocket
        | None -> Pocket.Item  // unknown items default to item pocket

    let private pocketList (pocket: Pocket) (bag: Bag) =
        match pocket with
        | Pocket.Item -> bag.Items
        | Pocket.Ball -> bag.Balls
        | Pocket.KeyItem -> bag.KeyItems
        | Pocket.TmHm -> bag.TmHm

    let private withPocket (pocket: Pocket) (lst: (string * int) list) (bag: Bag) =
        match pocket with
        | Pocket.Item -> { bag with Items = lst }
        | Pocket.Ball -> { bag with Balls = lst }
        | Pocket.KeyItem -> { bag with KeyItems = lst }
        | Pocket.TmHm -> { bag with TmHm = lst }

    let private addToPocket (itemId: string) (qty: int) (lst: (string * int) list) =
        match lst |> List.tryFindIndex (fun (id, _) -> id = itemId) with
        | Some i ->
            lst |> List.mapi (fun j (id, q) ->
                if j = i then (id, min MaxStack (q + qty)) else (id, q))
        | None -> lst @ [(itemId, min MaxStack qty)]

    let private removeFromPocket (itemId: string) (qty: int) (lst: (string * int) list) =
        lst |> List.choose (fun (id, q) ->
            if id = itemId then
                let left = q - qty
                if left > 0 then Some (id, left) else None
            else Some (id, q))

    /// Add qty of itemId to the appropriate pocket.
    let add (itemId: string) (qty: int) (bag: Bag) : Bag =
        let pocket = pocketOf itemId
        let lst = pocketList pocket bag
        withPocket pocket (addToPocket itemId qty lst) bag

    /// Remove qty of itemId from its pocket (removes entry if count reaches 0).
    let remove (itemId: string) (qty: int) (bag: Bag) : Bag =
        let pocket = pocketOf itemId
        let lst = pocketList pocket bag
        withPocket pocket (removeFromPocket itemId qty lst) bag

    /// Current count of an item across all pockets.
    let count (itemId: string) (bag: Bag) : int =
        let all = bag.Items @ bag.Balls @ bag.KeyItems @ bag.TmHm
        all |> List.tryFind (fun (id, _) -> id = itemId) |> Option.map snd |> Option.defaultValue 0

    /// Build a pocketed Bag from a flat item→qty map (used for v2 save migration).
    let ofFlat (flat: Map<string, int>) : Bag =
        flat |> Map.fold (fun b itemId qty -> add itemId qty b) empty

    /// Flatten the pocketed bag to an item→qty map (for flat-bag consumers).
    let toFlat (bag: Bag) : Map<string, int> =
        let all = bag.Items @ bag.Balls @ bag.KeyItems @ bag.TmHm
        all |> Map.ofList
