namespace PokeGold.Game.Ui

/// Which colour band the HP bar should display, matching GSC's three tiers.
type HpBand = Green | Yellow | Red

/// Pure HP-bar mathematics for the GSC 6-tile (48-pixel) bar.
module HpBar =

    /// Width of the HP bar in pixels (6 tiles × 8 px).
    [<Literal>]
    let BarPx = 48

    /// Number of filled pixels for a bar at `curHp / maxHp`.
    ///
    /// Uses ceiling division so that any curHp > 0 yields at least 1 pixel.
    /// Returns 0 when curHp ≤ 0 or maxHp ≤ 0. Clamped to 0..BarPx.
    let fill (curHp: int) (maxHp: int) : int =
        if maxHp <= 0 || curHp <= 0 then
            0
        else
            let px = (curHp * BarPx + maxHp - 1) / maxHp // ceiling division
            max 1 (min BarPx px)

    /// Colour band for the HP bar given the current/maximum HP.
    ///
    /// GSC thresholds (by ratio):  > 50 % → Green  |  > 20 % → Yellow  |  else → Red
    let band (curHp: int) (maxHp: int) : HpBand =
        if maxHp <= 0 then
            Red
        else
            let ratio = float curHp / float maxHp
            if ratio > 0.5 then Green
            elif ratio > 0.2 then Yellow
            else Red
