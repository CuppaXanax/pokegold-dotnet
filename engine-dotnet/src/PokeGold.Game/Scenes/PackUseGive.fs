module PokeGold.Game.Scenes.PackUseGive

open PokeGold.Game.Data
open PokeGold.Game.Player

// ── HP-heal item taxonomy ──────────────────────────────────────────────────────
//
// The FieldMenu metadata does not distinguish HP-restore items from status-heals,
// revives, vitamins, evolution stones, TMs/HMs — all use "ITEMMENU_PARTY".
// Decision: maintain an explicit set of IDs whose field-use effect is HP restore.
// Deferred item categories (status cures, revives, vitamins, evo stones, elixirs,
// TMs/HMs, key-item field effects) route to the gated "Can't use that here yet."
// message and are enumerated in docs/m11-coverage.md.

/// Item IDs whose field effect is HP restoration.
/// Param > 0  → heal that many HP; Param < 0 → full heal (MAX_POTION / FULL_RESTORE).
/// Note: FULL_RESTORE also clears status in the original game; that part is deferred
/// — only the HP component is applied here.
let private hpRestoreIds =
    Set.ofList
        [ "POTION"; "SUPER_POTION"; "HYPER_POTION"; "MAX_POTION"; "FULL_RESTORE"
          "FRESH_WATER"; "SODA_POP"; "LEMONADE"; "MOOMOO_MILK"; "BERRY_JUICE"
          "RAGECANDYBAR"; "BERRY"; "GOLD_BERRY" ]

let private repelItems =
    Map.ofList [ "REPEL", 100; "SUPER_REPEL", 200; "MAX_REPEL", 250 ]

let private fishingRods = Set.ofList [ "OLD_ROD"; "GOOD_ROD"; "SUPER_ROD" ]

/// True when this item's field-USE is handled as an HP heal.
let isHpHeal (itemId: string) : bool = Set.contains itemId hpRestoreIds

/// True when this item is a TM/HM that can teach a move.
let isTmHm (itemId: string) : bool = TmHm.moveForItem itemId |> Option.isSome

/// True when this item is a repel item.
let isRepel (itemName: string) : bool = repelItems.ContainsKey itemName

/// True when this item is a fishing rod.
let isFishingRod (itemName: string) : bool = Set.contains itemName fishingRods

// ── Pure mutation helpers (unit-testable without the scene stack) ──────────────

/// Apply a GIVE action: give one copy of `itemId` to party slot `slotIdx`.
///   - Removes one of `itemId` from the bag.
///   - Sets that mon's HeldItem to Some itemId.
///   - If the mon already held a *different* item, returns that old item to the bag
///     (GSC swap behaviour).
let applyGive (itemId: string) (slotIdx: int) (player: PlayerState) : PlayerState =
    let mon = List.item slotIdx player.Party
    let bagAfterReturn =
        match mon.HeldItem with
        | Some oldId when oldId <> itemId -> Bag.add oldId 1 player.Bag
        | _                               -> player.Bag
    let newBag   = Bag.remove itemId 1 bagAfterReturn
    let newMon   = { mon with HeldItem = Some itemId }
    let newParty = player.Party |> List.mapi (fun i m -> if i = slotIdx then newMon else m)
    { player with Party = newParty; Bag = newBag }

/// Apply a USE (HP-heal) action to party slot `slotIdx`.
/// Returns None if the mon is already at full HP (item not consumed).
/// Returns Some updatedPlayer on a successful heal.
let applyHpHeal (itemId: string) (slotIdx: int) (player: PlayerState) : PlayerState option =
    let mon = List.item slotIdx player.Party
    if mon.Hp >= mon.MaxHp then
        None
    else
        let param =
            ItemsData.byId
            |> Map.tryFind itemId
            |> Option.map (fun d -> d.Param)
            |> Option.defaultValue 0
        let healAmt = if param < 0 then mon.MaxHp else param
        let newHp   = min mon.MaxHp (mon.Hp + healAmt)
        let newMon  = { mon with Hp = newHp }
        let newParty = player.Party |> List.mapi (fun i m -> if i = slotIdx then newMon else m)
        let newBag   = Bag.remove itemId 1 player.Bag
        Some { player with Party = newParty; Bag = newBag }

/// Apply a REPEL item: consume one copy and set the repel counter.
let applyRepel (itemName: string) (player: PlayerState) : PlayerState option =
    match Map.tryFind itemName repelItems with
    | Some steps ->
        let newBag = Bag.remove itemName 1 player.Bag
        Some { player with Bag = newBag; RepelSteps = steps }
    | None -> None

/// Apply a TM/HM item to a party slot.
/// Returns None if the move cannot be taught or the mon already knows it.
let applyTmHm (itemId: string) (slotIdx: int) (player: PlayerState) : PlayerState option =
    match TmHm.moveForItem itemId with
    | None -> None
    | Some moveName ->
        let mon = List.item slotIdx player.Party
        match TmHm.teach moveName mon with
        | None -> None
        | Some taughtMon ->
            let newParty = player.Party |> List.mapi (fun i m -> if i = slotIdx then taughtMon else m)
            let newBag =
                if TmHm.isHmItem itemId then player.Bag
                else Bag.remove itemId 1 player.Bag
            Some { player with Party = newParty; Bag = newBag }
