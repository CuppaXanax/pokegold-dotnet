namespace PokeGold.Game.Data

/// Source-generated in-game NPC trade definitions.
module NpcTrades =
  let trades : NpcTradeData list = NpcTradesData.all |> Array.toList

  let tryFind (constant: string) : NpcTradeData option =
    NpcTradesData.byConstant |> Map.tryFind constant
