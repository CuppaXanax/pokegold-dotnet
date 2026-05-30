module PokeGold.Tests.BattleTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Battle

// --- Data-loader tests (parsing the disassembly in place) ---------------------

[<Fact>]
let ``species loader decodes Cyndaquil's base stats and types`` () =
    let c = Species.load "cyndaquil"
    Assert.Equal(39, c.Hp)
    Assert.Equal(52, c.Attack)
    Assert.Equal(43, c.Defense)
    Assert.Equal(65, c.Speed)
    Assert.Equal(60, c.SpAttack)
    Assert.Equal(50, c.SpDefense)
    Assert.Equal(TypeChart.value "FIRE", c.Type1)
    Assert.Equal(TypeChart.value "FIRE", c.Type2)

[<Fact>]
let ``move loader decodes Tackle's 7-byte record`` () =
    let t = Moves.byName "TACKLE"
    Assert.Equal("EFFECT_NORMAL_HIT", t.Effect)
    Assert.Equal(35, t.Power)
    Assert.Equal(TypeChart.value "NORMAL", t.Type)
    Assert.Equal(95, t.Accuracy)
    Assert.Equal(35, t.Pp)

[<Fact>]
let ``type ids place the physical/special split at 20`` () =
    Assert.Equal(0, TypeChart.value "NORMAL")
    Assert.Equal(20, TypeChart.value "FIRE")
    Assert.True(TypeChart.isPhysical (TypeChart.value "NORMAL"))
    Assert.False(TypeChart.isPhysical (TypeChart.value "FIRE"))

[<Fact>]
let ``type chart returns Gen-2 matchup multipliers`` () =
    let v = TypeChart.value
    Assert.Equal(20, TypeChart.multiplier (v "FIRE") (v "GRASS")) // super
    Assert.Equal(5, TypeChart.multiplier (v "FIRE") (v "WATER")) // not very
    Assert.Equal(0, TypeChart.multiplier (v "NORMAL") (v "GHOST")) // immune
    Assert.Equal(10, TypeChart.multiplier (v "NORMAL") (v "WATER")) // neutral

// --- Damage formula: worked examples (no crit, fixed roll) --------------------

let private ty = TypeChart.value

/// A synthetic species with chosen types; stat fields are unused by the damage
/// math directly (the BattleMon carries the derived stats).
let private species name t1 t2 : BaseStats =
    { Dex = 0
      Name = name
      Hp = 1
      Attack = 1
      Defense = 1
      Speed = 1
      SpAttack = 1
      SpDefense = 1
      Type1 = t1
      Type2 = t2 }

/// A battler with explicit stats (physical == special so the class never changes
/// the numbers) and neutral stages, for deterministic damage tests.
let private mon name t1 t2 level hp atk def spd : BattleMon =
    { Species = species name t1 t2
      Level = level
      MaxHp = hp
      Hp = hp
      Attack = atk
      Defense = def
      Speed = spd
      SpAttack = atk
      SpDefense = def
      Moves = []
      AtkStage = 0
      DefStage = 0
      SpdStage = 0
      SpAtkStage = 0
      SpDefStage = 0 }

let private move name effect power typ : MoveData =
    { Name = name
      Effect = effect
      Power = power
      Type = typ
      Accuracy = 100
      Pp = 35
      EffectChance = 0 }

[<Fact>]
let ``neutral non-STAB hit matches the hand-computed Gen-2 value`` () =
    // L10, power 40, atk 30, def 25:
    //  (2*10/5+2)=6; *40=240; *30=7200; /25=288; /50=5; cap+2=7; neutral; *255/255=7
    let attacker = mon "ATTACKER" (ty "FIRE") (ty "FIRE") 10 100 30 25 50
    let defender = mon "DEFENDER" (ty "WATER") (ty "WATER") 10 100 30 25 50
    let m = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    Assert.Equal(7, Damage.calc attacker defender m false Damage.MaxRoll)

[<Fact>]
let ``STAB and super-effectiveness stack as the disassembly orders them`` () =
    // Same base 7; STAB 7+3=10; super x2 -> 20; roll 255 -> 20.
    let attacker = mon "ATTACKER" (ty "FIRE") (ty "FIRE") 10 100 30 25 50
    let defender = mon "DEFENDER" (ty "GRASS") (ty "GRASS") 10 100 30 25 50
    let m = move "EMBER" "EFFECT_NORMAL_HIT" 40 (ty "FIRE")
    Assert.Equal(20, Damage.calc attacker defender m false Damage.MaxRoll)

[<Fact>]
let ``a type-immune defender takes zero damage`` () =
    let attacker = mon "ATTACKER" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
    let defender = mon "DEFENDER" (ty "GHOST") (ty "GHOST") 10 100 30 25 50
    let m = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    Assert.Equal(0, Damage.calc attacker defender m false Damage.MaxRoll)

[<Fact>]
let ``a critical hit doubles the pre-modifier total`` () =
    // Base 5 -> crit x2 = 10 -> cap+2 = 12; non-STAB neutral; roll 255 -> 12.
    let attacker = mon "ATTACKER" (ty "FIRE") (ty "FIRE") 10 100 30 25 50
    let defender = mon "DEFENDER" (ty "WATER") (ty "WATER") 10 100 30 25 50
    let m = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    Assert.Equal(12, Damage.calc attacker defender m true Damage.MaxRoll)

// --- Turn loop ----------------------------------------------------------------

let private strongHit = move "TACKLE" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
let private growl = move "GROWL" "EFFECT_ATTACK_DOWN" 0 (ty "NORMAL")

[<Fact>]
let ``the faster mon acts first`` () =
    let slow = { mon "SLOW" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 10 with Moves = [ strongHit ] }
    let fast = { mon "FAST" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 99 with Moves = [ strongHit ] }
    let state = Battle.create slow fast 1u
    let after = Battle.chooseMove 0 state
    // First action message after the player's choice should be the fast enemy's.
    let firstUse = after.Messages |> List.find (fun m -> m.Contains "used")
    Assert.Contains("FAST used", firstUse)

[<Fact>]
let ``a lethal hit faints the enemy and wins the battle`` () =
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 200 200 200 with Moves = [ strongHit ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 2 1 1 1 1 with Moves = [ strongHit ] }
    let after = Battle.create player enemy 7u |> Battle.chooseMove 0
    Assert.Equal(Some Win, after.Outcome)
    Assert.Contains(after.Messages, fun m -> m.Contains "fainted")

[<Fact>]
let ``a stat-down move lowers the target's stage`` () =
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ growl ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ growl ] }
    let after = Battle.create player enemy 3u |> Battle.chooseMove 0
    // Player is faster and lowers the enemy's Attack stage to -1.
    Assert.Equal(-1, after.Enemy.AtkStage)

[<Fact>]
let ``the real demo encounter resolves with loaded data`` () =
    // Drives the actual scripted battle (real species, moves, types) to a result,
    // exercising the data loaders, effect interpreter, and turn loop together.
    let player =
        BattleMon.ofSpecies (Species.load "cyndaquil") 5 [ Moves.byName "TACKLE"; Moves.byName "LEER" ]

    let enemy = BattleMon.ofSpecies (Species.load "pidgey") 3 [ Moves.byName "TACKLE" ]

    let mutable state = Battle.create player enemy 0x1234u
    let mutable turns = 0

    while not (Battle.isOver state) && turns < 100 do
        state <- Battle.chooseMove 0 state
        turns <- turns + 1

    Assert.True(state.Outcome.IsSome)
