namespace PokeGold.Game.Player

/// Pure helpers for the player's money. Money is stored as a plain `int` on
/// `PlayerState.Money`; these functions enforce the GSC cap and floor.
module Money =

    /// Maximum money the player can hold (GSC MAX_MONEY, 3-byte BCD cap = 999 999).
    [<Literal>]
    let maxMoney = 999_999

    /// Add `amount` to `money`, clamping at maxMoney. Result is never below 0.
    let give (money: int) (amount: int) : int =
        min maxMoney (max 0 (money + amount))

    /// Subtract `amount` from `money`, flooring at 0.
    let take (money: int) (amount: int) : int =
        max 0 (money - amount)

    /// True when `money` is sufficient to cover `cost`.
    let canAfford (money: int) (cost: int) : bool = money >= cost

    /// Total purchase cost for `qty` items at `price` each.
    let buyTotal (price: int) (qty: int) : int = price * qty

    /// Sell price for `qty` items with buy price `buyPrice`
    /// (integer floor of buyPrice*qty/2). Verified against
    /// engine/items/buy_sell_toss.asm Sell_HalvePrice which srl-halves the product.
    let sellPrice (buyPrice: int) (qty: int) : int = (buyPrice * qty) / 2
