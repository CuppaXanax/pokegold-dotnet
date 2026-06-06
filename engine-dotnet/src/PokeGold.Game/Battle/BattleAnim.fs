namespace PokeGold.Game.Battle

open PokeGold.Game.Data

type AnimEffect =
    | HitFlash | FireBurst | WaterSplash | ElectricZap | GrassLeaf
    | IceCrystal | PsychicWave | PoisonCloud | GroundShake
    | NormalHit | StatusEffect | NoAnim

module BattleAnim =
    let effectForMove (move: MoveData) : AnimEffect =
        if move.Power = 0 then StatusEffect
        else
            match TypeChart.nameOfType move.Type with
            | "FIRE" -> FireBurst
            | "WATER" -> WaterSplash
            | "ELECTRIC" -> ElectricZap
            | "GRASS" | "BUG" -> GrassLeaf
            | "ICE" -> IceCrystal
            | "PSYCHIC_TYPE" -> PsychicWave
            | "POISON" -> PoisonCloud
            | "GROUND" | "ROCK" -> GroundShake
            | "NORMAL" -> NormalHit
            | _ -> HitFlash

    let duration (effect: AnimEffect) : int =
        match effect with
        | NoAnim -> 0 | StatusEffect -> 15 | HitFlash | NormalHit -> 10 | _ -> 20

    let tintColor (effect: AnimEffect) : byte * byte * byte * byte =
        match effect with
        | FireBurst -> (255uy, 100uy, 0uy, 128uy)
        | WaterSplash -> (0uy, 100uy, 255uy, 128uy)
        | ElectricZap -> (255uy, 255uy, 0uy, 160uy)
        | GrassLeaf -> (0uy, 200uy, 50uy, 128uy)
        | IceCrystal -> (150uy, 220uy, 255uy, 128uy)
        | PsychicWave -> (200uy, 50uy, 255uy, 128uy)
        | PoisonCloud -> (160uy, 0uy, 200uy, 128uy)
        | GroundShake -> (180uy, 140uy, 80uy, 100uy)
        | NormalHit | HitFlash -> (255uy, 255uy, 255uy, 160uy)
        | StatusEffect -> (255uy, 255uy, 200uy, 60uy)
        | NoAnim -> (0uy, 0uy, 0uy, 0uy)
