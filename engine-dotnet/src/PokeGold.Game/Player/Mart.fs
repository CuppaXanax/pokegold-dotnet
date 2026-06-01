namespace PokeGold.Game.Player

open PokeGold.Game.Data

/// Pure Poké Mart transaction logic — independent of rendering.
/// All money arithmetic uses Money; all bag mutations use Bag.
/// These functions are the unit-testable core of the mart system.
module Mart =

    /// Why a buy was refused.
    type BuyError = | CantAfford

    /// Why a sell was refused.
    type SellError =
        /// Item is a key item, has CantSelect flag set, or has price 0.
        | CantSell
        /// The bag holds fewer than the requested quantity.
        | NotInBag

    /// True when `itemId` can be sold in a Poké Mart.
    /// Refuses key items and items with price 0 (price 0 covers HMs and
    /// items with no buy value). Regular items, balls, and TMs are sellable.
    let canSell (itemId: string) : bool =
        match ItemsData.byId |> Map.tryFind itemId with
        | None -> false
        | Some data ->
            data.Pocket <> Pocket.KeyItem &&
            data.Price > 0

    /// Attempt to buy `qty` of `itemId` (at `price` each) from `money` and `bag`.
    /// Returns (newMoney, newBag) on success, or BuyError on failure.
    let buy (itemId: string) (price: int) (qty: int) (money: int) (bag: Bag) : Result<int * Bag, BuyError> =
        let total = Money.buyTotal price qty
        if not (Money.canAfford money total) then Error CantAfford
        else Ok(Money.take money total, Bag.add itemId qty bag)

    /// Attempt to sell `qty` of `itemId` (originally priced at `buyPrice`) from
    /// `bag`, crediting `money`. Returns (newMoney, newBag) or SellError.
    let sell (itemId: string) (buyPrice: int) (qty: int) (money: int) (bag: Bag) : Result<int * Bag, SellError> =
        if not (canSell itemId) then Error CantSell
        elif Bag.count itemId bag < qty then Error NotInBag
        else
            let earned = Money.sellPrice buyPrice qty
            Ok(Money.give money earned, Bag.remove itemId qty bag)
