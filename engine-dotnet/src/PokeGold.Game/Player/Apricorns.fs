namespace PokeGold.Game.Player

/// Apricorn → Poké Ball conversion table (Kurt's workshop).
module Apricorns =
    /// Kurt's picker scans the ApricornBalls table in this order.
    let ordered =
        [ "RED_APRICORN"
          "BLU_APRICORN"
          "YLW_APRICORN"
          "GRN_APRICORN"
          "WHT_APRICORN"
          "BLK_APRICORN"
          "PNK_APRICORN" ]

    let private pairs =
        [ "RED_APRICORN", "LEVEL_BALL"
          "BLU_APRICORN", "LURE_BALL"
          "YLW_APRICORN", "MOON_BALL"
          "GRN_APRICORN", "FRIEND_BALL"
          "WHT_APRICORN", "FAST_BALL"
          "BLK_APRICORN", "HEAVY_BALL"
          "PNK_APRICORN", "LOVE_BALL" ]

    /// Map apricorn item name to the ball it produces.
    let conversion = Map.ofList pairs

    /// Numeric item ids used by `SelectApricornForKurt`'s wScriptVar result.
    let private itemIds =
        Map.ofList
            [ "RED_APRICORN", 0x55
              "BLU_APRICORN", 0x59
              "YLW_APRICORN", 0x5c
              "GRN_APRICORN", 0x5d
              "WHT_APRICORN", 0x61
              "BLK_APRICORN", 0x63
              "PNK_APRICORN", 0x65 ]

    let itemId apricorn = itemIds.[apricorn]

    let available (bag: Bag) =
        ordered
        |> List.filter (fun apricorn -> Bag.count apricorn bag > 0)

    /// Convert an apricorn: remove it from bag, add the ball.
    let convert (apricorn: string) (player: PlayerState) : PlayerState option =
        match Map.tryFind apricorn conversion with
        | Some ball when Bag.count apricorn player.Bag > 0 ->
            let bag = player.Bag |> Bag.remove apricorn 1 |> Bag.add ball 1
            Some { player with Bag = bag }
        | _ -> None
