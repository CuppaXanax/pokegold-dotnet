#r "N:\dev\scratch\slop-ware\pokegold.worktrees\agents-fun-compiler-high-level-port\engine-dotnet\src\PokeGold.Game\bin\Debug\net8.0\PokeGold.Game.dll"
open PokeGold.Game.Battle
open PokeGold.Game.Data

let moveWithAcc name effect power typ acc = { Name = name; Effect = effect; Power = power; Type = typ; Accuracy = acc; Pp = 35; EffectChance = 0 }
let ty = TypeChart.value
let mon name t1 t2 level hp atk def spd =
    { Species = { Dex = 0; Name = name; Hp = 1; Attack = 1; Defense = 1; Speed = 1; SpAttack = 1; SpDefense = 1; Type1 = t1; Type2 = t2; CatchRate = 45; BaseExp = 64; GrowthRate = 0 }
      Level = level; MaxHp = hp; Hp = hp; Attack = atk; Defense = def; Speed = spd; SpAttack = atk; SpDefense = def; Moves = []; Pp = []; Status = Healthy; AtkStage = 0; DefStage = 0; SpdStage = 0; SpAtkStage = 0; SpDefStage = 0; AccStage = 0; EvaStage = 0; Gender = Unknown; Volatile = VolatileStatus.empty }
let lowAccMove = moveWithAcc "LOWMOVE" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") 10
let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ lowAccMove ] }
let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ moveWithAcc "TACKLE" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") 35 ] }
let seed = 1u
let after = Battle.create user foe seed |> Battle.chooseMove 0
printfn "enemy hp=%d player hp=%d messages=%A" after.Enemy.Hp after.Player.Hp after.Messages
printfn "firstDraw=%d" (fst (Rng.next (Rng.create seed)))
