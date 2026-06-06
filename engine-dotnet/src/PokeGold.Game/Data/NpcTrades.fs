namespace PokeGold.Game.Data

/// In-game NPC trade definitions.
module NpcTrades =
    type NpcTrade =
        { Id: int
          Give: string
          Receive: string
          Nickname: string
          Level: int }

    /// Known in-game trades, hardcoded from data/events/npc_trades.asm.
    let trades =
        [ { Id = 1; Give = "DROWZEE"; Receive = "MACHOP"; Nickname = "MUSCLE"; Level = 10 }
          { Id = 2; Give = "BELLSPROUT"; Receive = "ONIX"; Nickname = "ROCKY"; Level = 20 }
          { Id = 3; Give = "KRABBY"; Receive = "VOLTORB"; Nickname = "VOLTY"; Level = 25 }
          { Id = 4; Give = "DRAGONAIR"; Receive = "RHYDON"; Nickname = "DON"; Level = 40 }
          { Id = 5; Give = "GLOOM"; Receive = "RAPIDASH"; Nickname = "RUNNY"; Level = 15 }
          { Id = 6; Give = "CHANSEY"; Receive = "AERODACTYL"; Nickname = "AEROY"; Level = 30 } ]
