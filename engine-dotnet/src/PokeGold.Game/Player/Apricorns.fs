namespace PokeGold.Game.Player

/// Apricorn → Poké Ball conversion table (Kurt's workshop).
module Apricorns =
    /// Map apricorn item name to the ball it produces.
    let conversion =
        Map.ofList [
            "RED_APRICORN",    "LEVEL_BALL"
            "BLU_APRICORN",    "LURE_BALL"
            "YLW_APRICORN",    "MOON_BALL"
            "GRN_APRICORN",    "FRIEND_BALL"
            "PNK_APRICORN",    "LOVE_BALL"
            "BLK_APRICORN",    "HEAVY_BALL"
            "WHT_APRICORN",    "FAST_BALL" ]

    /// Convert an apricorn: remove it from bag, add the ball.
    let convert (apricorn: string) (player: PlayerState) : PlayerState option =
        match Map.tryFind apricorn conversion with
        | Some ball when Bag.count apricorn player.Bag > 0 ->
            let bag = player.Bag |> Bag.remove apricorn 1 |> Bag.add ball 1
            Some { player with Bag = bag }
        | _ -> None
