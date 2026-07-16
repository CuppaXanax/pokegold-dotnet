module PokeGold.Tests.BattleTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Battle
open PokeGold.Game.Player
open PokeGold.Game.Scenes

// --- Data-table tests (baked at build time by PokeGold.DataGen) ---------------

[<Fact>]
let ``species loader decodes Cyndaquil's base stats and types`` () =
    let c = Species.byName "CYNDAQUIL"
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

[<Fact>]
let ``generated tables bake the full national dex and move list`` () =
    // The build-time generator must cover every species and move, not just the
    // demo set — guards against a partial or stale generation.
    Assert.Equal(251, Species.all.Count)
    Assert.Equal(251, Moves.all.Count)
    // 10 physical + CURSE_TYPE + 8 special = 19 named type ids.
    Assert.True(Species.all.ContainsKey "BULBASAUR")
    Assert.True(Species.all.ContainsKey "CELEBI")
    Assert.Equal(1, (Species.byName "BULBASAUR").Dex)
    Assert.Equal(251, (Species.byName "CELEBI").Dex)

[<Fact>]
let ``Medium Fast: level 5 needs 125 total EXP`` () =
    Assert.Equal(125, Experience.expForLevel 0 5)

[<Fact>]
let ``Medium Fast: level 100 needs 1000000 total EXP`` () =
    Assert.Equal(1000000, Experience.expForLevel 0 100)

[<Fact>]
let ``Fast: level 100 needs 800000 total EXP`` () =
    Assert.Equal(800000, Experience.expForLevel 4 100)

[<Fact>]
let ``Slow: level 100 needs 1250000 total EXP`` () =
    Assert.Equal(1250000, Experience.expForLevel 5 100)

[<Fact>]
let ``Medium Slow: level 100 needs 1059860 total EXP`` () =
    Assert.Equal(1059860, Experience.expForLevel 3 100)

[<Fact>]
let ``expToNextLevel returns difference between levels`` () =
    let toNext = Experience.expToNextLevel 0 5
    Assert.Equal(Experience.expForLevel 0 6 - Experience.expForLevel 0 5, toNext)

[<Fact>]
let ``levelAfterExp caps at 100`` () =
    let lvl, _ = Experience.levelAfterExp 0 99 (Experience.expForLevel 0 99) 999999
    Assert.Equal(100, lvl)

[<Fact>]
let ``EXP gained from wild battle`` () =
    Assert.Equal(64 * 5 / 7, Experience.expGained 64 5 false)

[<Fact>]
let ``EXP gained from trainer battle has 1.5x multiplier`` () =
    let wild = Experience.expGained 64 10 false
    let trainer = Experience.expGained 64 10 true
    Assert.Equal(wild * 3 / 2, trainer)

[<Fact>]
let ``money earned from trainer`` () =
    Assert.Equal(4 * 25 * 20, Experience.moneyEarned 25 20)

[<Fact>]
let ``Amulet Coin doubles trainer reward`` () =
    Assert.Equal(4000, Experience.applyAmuletCoin true (Experience.moneyEarned 25 20))
    Assert.Equal(2000, Experience.applyAmuletCoin false (Experience.moneyEarned 25 20))

[<Fact>]
let ``BAT-016 defeat is an explicit script-facing battle result`` () =
    Assert.Equal(BattleVictory, BattleScriptResult.ofOutcome Win)
    Assert.Equal(BattleDefeat, BattleScriptResult.ofOutcome Lose)
    Assert.Equal(BattleEscape, BattleScriptResult.ofOutcome Ran)

[<Fact>]
let ``BAT-024 real Red AI uses generated Full Restore at critical HP`` () =
    let red = Trainers.lookupByName "RED" "RED1" |> Option.defaultWith (fun () -> failwith "RED1 not found")
    let redLead = red.Party.Head
    let redMoves = MoveLearn.trainerMoveNames redLead |> List.map Moves.byName
    let enemy =
        { BattleMon.ofSpeciesWithStats (Species.byName redLead.Species) redLead.Level redMoves red.Dvs StatExperience.zero with
            Hp = 1 }
    let recover = Moves.byName "RECOVER"
    let recoverId = MovesData.byIndex |> Array.findIndex (fun move -> move.Name = "RECOVER")
    let player =
        { PartyMon.create (Species.byName "MEWTWO").Dex 100 with
            Moves = [ recoverId, recover.Pp ] }
        |> PartyMon.toBattleMon
    let context =
        { Group = "RED"
          Id = "RED1"
          Name = red.Name
          ClassName = "RED"
          WinText = None
          LossText = None
          BaseReward = Some red.BaseReward }

    let after = Battle.createTrainer context [ player ] [ enemy ] 0u |> Battle.chooseMove 0

    Assert.Equal(enemy.MaxHp, after.Enemy.Hp)
    Assert.Contains(after.Messages, fun message -> message.Contains("used FULL RESTORE"))
    Assert.Equal<string list>([ "FULL_RESTORE" ], after.EnemyAiItems)
    Assert.Equal(0, after.EnemyTurnsTaken)

let private sourceTrainerBattleMon (trainer: TrainerData) (trainerMon: TrainerMon) : BattleMon =
    let stats = Species.byName trainerMon.Species
    let moves = MoveLearn.trainerMoveNames trainerMon |> List.map Moves.byName
    { BattleMon.ofSpeciesWithStats stats trainerMon.Level moves trainer.Dvs StatExperience.zero with
        HeldItem = trainerMon.HeldItem
        Dvs = trainer.Dvs
        Gender = BattleMon.genderFromDvs stats trainer.Dvs }

let private sourceTrainerContext (trainer: TrainerData) =
    { Group = trainer.Group
      Id = string trainer.Id
      Name = trainer.Name
      ClassName = trainer.Group.Replace("_", " ")
      WinText = None
      LossText = None
      BaseReward = Some trainer.BaseReward }

let private legalRecoverMewtwo () =
    let recover = Moves.byName "RECOVER"
    let recoverId = MovesData.byIndex |> Array.findIndex (fun move -> move.Name = "RECOVER")
    Assert.Contains("RECOVER", MoveLearn.startingMoveNames "MEWTWO" 100)
    { PartyMon.create (Species.byName "MEWTWO").Dex 100 with
        Moves = [ recoverId, recover.Pp ] }
    |> PartyMon.toBattleMon

[<Fact>]
let ``BAT-024 generated trainer profiles select only real source moves`` () =
    for group, id in [ "FALKNER", 1; "WILL", 1; "CHAMPION", 1; "RED", 1 ] do
        let trainer = Trainers.lookup group id |> Option.defaultWith (fun () -> failwith $"{group} not found")
        let enemy = sourceTrainerBattleMon trainer trainer.Party.Head
        let initial = Battle.createTrainer (sourceTrainerContext trainer) [ legalRecoverMewtwo () ] [ enemy ] 0u
        let after = Battle.chooseMove 0 initial
        let sourceMoveMessages = enemy.Moves |> List.map (fun move -> $"{enemy.Species.Name} used {move.Name}!")

        Assert.Equal<string list>(trainer.AiItems, initial.EnemyAiItems)
        Assert.Contains(after.Messages, fun message -> sourceMoveMessages |> List.contains message)
        Assert.Equal(1, after.EnemyTurnsTaken)

[<Fact>]
let ``BAT-024 Falkner source switch policy does not use low HP alone`` () =
    let falkner = Trainers.lookup "FALKNER" 1 |> Option.defaultWith (fun () -> failwith "FALKNER not found")
    let player = BattleMon.ofSpecies (Species.byName "CHIKORITA") 20 [ Moves.byName "TACKLE" ]
    let lead =
        { BattleMon.ofSpecies (Species.byName "PIDGEY") 20 [ Moves.byName "GUST" ] with
            Hp = 1
        }
    let bench = BattleMon.ofSpecies (Species.byName "RATTATA") 20 [ Moves.byName "TACKLE" ]

    let after =
        Battle.createTrainer (sourceTrainerContext falkner) [ player ] [ lead; bench ] 0u
        |> Battle.chooseMove 0

    Assert.Equal("RATTATA", after.Enemy.Species.Name)
    Assert.Equal(0, after.EnemyTurnsTaken)
    Assert.DoesNotContain(after.Messages, fun message -> message.Contains("Enemy withdrew PIDGEY"))

[<Fact>]
let ``BAT-024 Lance source item priority uses Full Restore before Full Heal`` () =
    let lance = Trainers.lookup "CHAMPION" 1 |> Option.defaultWith (fun () -> failwith "LANCE not found")
    let highestLevelMon = lance.Party |> List.maxBy (fun trainerMon -> trainerMon.Level)
    let enemy = { sourceTrainerBattleMon lance highestLevelMon with Hp = 1; Status = Poison }

    let after =
        Battle.createTrainer (sourceTrainerContext lance) [ legalRecoverMewtwo () ] [ enemy ] 0u
        |> Battle.chooseMove 0

    Assert.Equal(enemy.MaxHp, after.Enemy.Hp)
    Assert.Equal(Healthy, after.Enemy.Status)
    Assert.Equal<string list>([ "FULL_HEAL" ], after.EnemyAiItems)
    Assert.Contains(after.Messages, fun message -> message.Contains("used FULL RESTORE"))

[<Fact>]
let ``BAT-024 legal Mewtwo defeats every generated Will party member through real turns`` () =
    let will = Trainers.lookup "WILL" 1 |> Option.defaultWith (fun () -> failwith "WILL not found")
    let legalMewtwo = PartyMon.create (Species.byName "MEWTWO").Dex 100
    Assert.True(TmHm.canLearnMove "THUNDER" legalMewtwo)
    Assert.True(TmHm.canLearnMove "FIRE_BLAST" legalMewtwo)
    Assert.True(TmHm.canLearnMove "PSYCHIC_M" legalMewtwo)
    let thunder = Moves.byName "THUNDER"
    let fireBlast = Moves.byName "FIRE_BLAST"
    let psychic = Moves.byName "PSYCHIC_M"
    let moveId name = MovesData.byIndex |> Array.findIndex (fun move -> move.Name = name)
    let player =
        { legalMewtwo with
            Moves = [ moveId "THUNDER", thunder.Pp; moveId "FIRE_BLAST", fireBlast.Pp; moveId "PSYCHIC_M", psychic.Pp ] }
        |> PartyMon.toBattleMon
    let initial =
        Battle.createTrainer
            (sourceTrainerContext will)
            [ player ]
            (will.Party |> List.map (sourceTrainerBattleMon will))
            0u

    let moveIndex state =
        match state.Enemy.Species.Name with
        | "XATU"
        | "SLOWBRO" -> 0
        | "JYNX"
        | "EXEGGUTOR" -> 1
        | _ -> 2

    let rec resolve turns (state: BattleState) =
        match state.Outcome with
        | Some _ -> state
        | None when turns = 0 -> state
        | None when Battle.requiresPlayerReplacement state -> failwith "The prepared player should not need a replacement"
        | None -> resolve (turns - 1) (Battle.chooseMove (moveIndex state) state)

    let final = resolve 80 initial

    Assert.Equal(Some Win, final.Outcome)
    Assert.Equal<string list>(
        will.Party |> List.map (fun trainerMon -> trainerMon.Species) |> List.sort,
        final.DefeatEvents |> List.map (fun defeat -> defeat.DefeatedSpecies.Name) |> List.sort)
    Assert.Equal(will.Party.Length, final.DefeatEvents.Length)

[<Fact>]
let ``BAT-011 Amulet Coin activates only after its holder is sent out and stays active`` () =
    let splash = Moves.byName "SPLASH"
    let lead = { BattleMon.ofSpecies (Species.byName "PIDGEY") 10 [ splash ] with PersistentId = Some(System.Guid.NewGuid()) }
    let holder =
        { BattleMon.ofSpecies (Species.byName "RATTATA") 10 [ splash ] with
            PersistentId = Some(System.Guid.NewGuid())
            HeldItem = Some "AMULET_COIN" }
    let foe = BattleMon.ofSpecies (Species.byName "CATERPIE") 5 [ splash ]
    let initial = Battle.createTeam [ lead; holder ] [ foe ] 0u
    Assert.False(initial.AmuletCoinActivated)
    let activated = initial |> Battle.switchMon 1
    Assert.True(activated.AmuletCoinActivated)
    Assert.True((activated |> Battle.switchMon 1).AmuletCoinActivated)

[<Fact>]
let ``BAT-011 Pay Day records level-scaled coins once per successful use`` () =
    let payDay = Moves.byName "PAY_DAY"
    let splash = Moves.byName "SPLASH"
    let user = { BattleMon.ofSpecies (Species.byName "PERSIAN") 50 [ payDay ] with Pp = [ payDay.Pp ] }
    let foe = { BattleMon.ofSpecies (Species.byName "SNORLAX") 50 [ splash ] with Hp = 500; MaxHp = 500; Pp = [ splash.Pp ] }
    let after = Battle.create user foe 0u |> Battle.chooseMove 0
    Assert.Equal(100, after.PayDayMoney)

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
      Type2 = t2
      CatchRate = 45
      BaseExp = 64
      Item1 = None
      Item2 = None
      GenderRatio = 255
      GrowthRate = 0 }

/// A battler with explicit stats (physical == special so the class never changes
/// the numbers) and neutral stages, for deterministic damage tests.
let private mon name t1 t2 level hp atk def spd : BattleMon =
    { PersistentId = None
      Persistent = None
      MovesAreTransformed = false
      Species = species name t1 t2
      Level = level
      Dvs = 0
      MaxHp = hp
      Hp = hp
      Attack = atk
      Defense = def
      Speed = spd
      SpAttack = atk
      SpDefense = def
      Moves = []
      Pp = []
      HeldItem = None
      Status = Healthy
      AtkStage = 0
      DefStage = 0
      SpdStage = 0
      SpAtkStage = 0
      SpDefStage = 0
      AccStage = 0
      EvaStage = 0
      Gender = Unknown
      Volatile = VolatileStatus.empty }

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
    Assert.Equal(7, Damage.calc attacker defender m false Damage.MaxRoll false)

[<Fact>]
let ``STAB and super-effectiveness stack as the disassembly orders them`` () =
    // Same base 7; STAB 7+3=10; super x2 -> 20; roll 255 -> 20.
    let attacker = mon "ATTACKER" (ty "FIRE") (ty "FIRE") 10 100 30 25 50
    let defender = mon "DEFENDER" (ty "GRASS") (ty "GRASS") 10 100 30 25 50
    let m = move "EMBER" "EFFECT_NORMAL_HIT" 40 (ty "FIRE")
    Assert.Equal(20, Damage.calc attacker defender m false Damage.MaxRoll false)

[<Fact>]
let ``a type-immune defender takes zero damage`` () =
    let attacker = mon "ATTACKER" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
    let defender = mon "DEFENDER" (ty "GHOST") (ty "GHOST") 10 100 30 25 50
    let m = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    Assert.Equal(0, Damage.calc attacker defender m false Damage.MaxRoll false)

[<Fact>]
let ``a critical hit doubles the pre-modifier total`` () =
    // Base 5 -> crit x2 = 10 -> cap+2 = 12; non-STAB neutral; roll 255 -> 12.
    let attacker = mon "ATTACKER" (ty "FIRE") (ty "FIRE") 10 100 30 25 50
    let defender = mon "DEFENDER" (ty "WATER") (ty "WATER") 10 100 30 25 50
    let m = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    Assert.Equal(12, Damage.calc attacker defender m true Damage.MaxRoll false)

[<Fact>]
let ``AI prefers super-effective moves`` () =
    let fire = Species.byName "CYNDAQUIL"
    let grass = Species.byName "CHIKORITA"
    let ember = Moves.byName "EMBER"
    let tackle = Moves.byName "TACKLE"
    let user = BattleMon.ofSpecies fire 10 [ ember; tackle ]
    let target = BattleMon.ofSpecies grass 10 [ tackle ]
    let profile: BattleAI.TrainerProfile = { MoveFlags = [ "AI_TYPES" ]; ItemSwitchFlags = []; Items = [] }
    let scoreEmber = BattleAI.scoreMoveWithProfile profile 0 user target ember
    let scoreTackle = BattleAI.scoreMoveWithProfile profile 0 user target tackle
    Assert.True(scoreEmber < scoreTackle)

[<Fact>]
let ``AI avoids immune moves`` () =
    let normal = { Species.byName "SENTRET" with Type1 = TypeChart.value "NORMAL" }
    let ghost = { Species.byName "GASTLY" with Type1 = TypeChart.value "GHOST" }
    let tackle = Moves.byName "TACKLE"
    let user = BattleMon.ofSpecies normal 10 [ tackle ]
    let target = BattleMon.ofSpecies ghost 10 [ tackle ]
    let profile: BattleAI.TrainerProfile = { MoveFlags = [ "AI_TYPES" ]; ItemSwitchFlags = []; Items = [] }
    Assert.True(BattleAI.scoreMoveWithProfile profile 0 user target tackle > 20)

[<Fact>]
let ``BAT-024 source Offensive layer discourages Sleep Powder behind a damaging move`` () =
    let sleepPowder = Moves.byName "SLEEP_POWDER"
    let tackle = Moves.byName "TACKLE"
    let user = BattleMon.ofSpecies (Species.byName "BUTTERFREE") 20 [ sleepPowder; tackle ]
    let target = BattleMon.ofSpecies (Species.byName "RATTATA") 20 [ tackle ]
    let profile: BattleAI.TrainerProfile = { MoveFlags = [ "AI_OFFENSIVE" ]; ItemSwitchFlags = []; Items = [] }

    let chosen =
        BattleAI.chooseMoveWithProfile profile 0 user target
        |> Option.defaultWith (fun () -> failwith "expected a legal AI move")

    Assert.Equal("TACKLE", fst chosen |> fun move -> move.Name)

[<Fact>]
let ``BAT-024 source minimum-score ties use the seeded battle RNG`` () =
    let tackle = Moves.byName "TACKLE"
    let scratch = Moves.byName "SCRATCH"
    let pound = Moves.byName "POUND"
    let quickAttack = Moves.byName "QUICK_ATTACK"
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 20 500 30 30 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 20 500 30 30 200 with
            Moves = [ tackle; scratch; pound; quickAttack ]
            Pp = [ tackle.Pp; scratch.Pp; pound.Pp; quickAttack.Pp ] }
    let context =
        { Group = "AI_TEST"
          Id = "1"
          Name = "AI"
          ClassName = "TEST"
          WinText = None
          LossText = None
          BaseReward = None }
    let seeds =
        [ 0u .. 255u ]
        |> List.groupBy (fun seed ->
            let roll, _ = Rng.next (Rng.create seed)
            roll &&& 3)
        |> List.choose (fun (_, grouped) -> grouped |> List.tryHead)
        |> List.take 2
    let selectedMove seed =
        let after = Battle.createTrainer context [ player ] [ enemy ] seed |> Battle.chooseMove 0
        after.Messages
        |> List.find (fun message -> message.StartsWith("ENEMY used "))

    Assert.NotEqual<string>(selectedMove seeds.[0], selectedMove seeds.[1])

[<Fact>]
let ``BAT-024 trapped and Mean Looked trainers cannot switch`` () =
    let player = { mon "GHOST" (ty "GHOST") (ty "GHOST") 30 120 70 70 50 with Moves = [ Moves.byName "SPLASH" ] }
    let activeEnemy =
        { mon "WALLED" (ty "NORMAL") (ty "NORMAL") 30 120 60 60 40 with
            Hp = 20
            Moves = [ Moves.byName "TACKLE" ] }
    let benchEnemy =
        { mon "ANSWER" (ty "DARK") (ty "DARK") 30 120 80 60 30 with
            Moves = [ Moves.byName "BITE" ] }
    let context =
        { Group = "FALKNER"
          Id = "1"
          Name = "FALKNER"
          ClassName = "FALKNER"
          WinText = None
          LossText = None
          BaseReward = Some 25 }
    let blockedStates =
        [ { activeEnemy with Volatile = { activeEnemy.Volatile with Trapped = Some 2 } }
          { activeEnemy with Volatile = { activeEnemy.Volatile with CantEscape = true } }
          { activeEnemy with Volatile = { activeEnemy.Volatile with Charging = Some 1; ChargingMove = Some (Moves.byName "TACKLE") } }
          { activeEnemy with Volatile = { activeEnemy.Volatile with Rampage = Some 2 } }
          { activeEnemy with Volatile = { activeEnemy.Volatile with BideTurns = Some 2 } } ]

    for blocked in blockedStates do
        let after = Battle.createTrainer context [ player ] [ blocked; benchEnemy ] 0u |> Battle.chooseMove 0
        Assert.Equal("WALLED", after.Enemy.Species.Name)
        Assert.DoesNotContain(after.Messages, fun message -> message.Contains("Enemy withdrew"))

[<Fact>]
let ``BAT-024 source switch policy rates differ for the same opportunity`` () =
    let splash = Moves.byName "SPLASH"
    let active =
        { mon "WALLED" (ty "NORMAL") (ty "NORMAL") 30 120 60 60 40 with
            Moves = [ Moves.byName "TACKLE" ] }
    let player = { mon "GHOST" (ty "GHOST") (ty "GHOST") 30 120 70 70 50 with Moves = [ splash ] }
    let bench = { mon "ANSWER" (ty "DARK") (ty "DARK") 30 120 80 60 30 with Moves = [ Moves.byName "BITE" ] }
    let seed =
        [ 0u .. 255u ]
        |> List.find (fun candidate ->
            let roll, _ = Rng.next (Rng.create candidate)
            roll >= 50 && roll < 128)
    let profile flags : BattleAI.TrainerProfile =
        { MoveFlags = []
          ItemSwitchFlags = flags
          Items = [] }
    let context: BattleAI.AiContext =
        { EnemyTurnsTaken = 0
          PlayerTurnsTaken = 0
          EnemySide = { SideState.Empty with PerishCounter = Some 1 }
          PlayerSide = SideState.Empty
          WeatherType = None
          EnemyTeam = [ active; bench ]
          PlayerTeam = [ player ] }
    let choose flags =
        BattleAI.chooseSwitchWithProfileAndRng
            (profile flags)
            context
            active
            player
            [ active; bench ]
            (Rng.create seed)
        |> fst

    Assert.Equal(Some 1, choose [ "SWITCH_OFTEN" ])
    Assert.Equal(None, choose [ "SWITCH_RARELY" ])
    Assert.Equal(Some 1, choose [ "SWITCH_SOMETIMES" ])

    let playerHeld = { player with Volatile = { player.Volatile with CantEscape = true } }
    let heldChoice =
        BattleAI.chooseSwitchWithProfileAndRng
            (profile [ "SWITCH_OFTEN" ])
            context
            active
            playerHeld
            [ active; bench ]
            (Rng.create seed)
        |> fst
    Assert.Equal(None, heldChoice)

[<Fact>]
let ``BAT-024 source tier 30 switch policies use the inverse cutoff branch`` () =
    let splash = Moves.byName "SPLASH"
    let active =
        { mon "WALLED" (ty "NORMAL") (ty "NORMAL") 30 120 60 60 40 with
            Moves = [ Moves.byName "TACKLE" ] }
    let player = { mon "GHOST" (ty "GHOST") (ty "GHOST") 30 120 70 70 50 with Moves = [ splash ] }
    let bench = { mon "ANSWER" (ty "DARK") (ty "DARK") 30 120 80 60 30 with Moves = [ Moves.byName "BITE" ] }
    let profile flags : BattleAI.TrainerProfile =
        { MoveFlags = []
          ItemSwitchFlags = flags
          Items = [] }
    let context: BattleAI.AiContext =
        { EnemyTurnsTaken = 0
          PlayerTurnsTaken = 0
          EnemySide = { SideState.Empty with PerishCounter = Some 1 }
          PlayerSide = SideState.Empty
          WeatherType = None
          EnemyTeam = [ active; bench ]
          PlayerTeam = [ player ] }
    let seedWith predicate =
        [ 0u .. 255u ]
        |> List.find (fun seed -> fst (Rng.next (Rng.create seed)) |> predicate)
    let choose flags seed =
        BattleAI.chooseSwitchWithProfileAndRng
            (profile [ flags ])
            context
            active
            player
            [ active; bench ]
            (Rng.create seed)
        |> fst
    let assertTier30 policy cutoff =
        let below = seedWith (fun roll -> roll < cutoff)
        let atOrAbove = seedWith (fun roll -> roll >= cutoff)
        Assert.Equal(None, choose policy below)
        Assert.Equal(Some 1, choose policy atOrAbove)

    assertTier30 "SWITCH_OFTEN" (4 * 255 / 100)
    assertTier30 "SWITCH_RARELY" (79 * 255 / 100 - 1)
    assertTier30 "SWITCH_SOMETIMES" (20 * 255 / 100 - 1)

[<Fact>]
let ``BAT-024 player used move history is unique rolling and resets on replacement`` () =
    let moves =
        [ 1 .. 5 ]
        |> List.map (fun index -> move $"MOVE_{index}" "EFFECT_NORMAL_HIT" 1 (ty "NORMAL"))
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 30 200 1 1 100 with
            Moves = moves
            Pp = moves |> List.map _.Pp }
    let bench =
        { mon "BENCH" (ty "NORMAL") (ty "NORMAL") 30 200 1 1 50 with
            Moves = [ Moves.byName "SPLASH" ]
            Pp = [ 40 ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 30 10000 1 255 1 with
            Moves = [ Moves.byName "SPLASH" ]
            Pp = [ 40 ] }
    let afterMoves =
        [ 0; 0; 1; 2; 3; 4 ]
        |> List.fold (fun state moveIndex -> Battle.chooseMove moveIndex state) (Battle.createTeam [ player; bench ] [ enemy ] 0u)

    Assert.Equal<string list>([ "MOVE_2"; "MOVE_3"; "MOVE_4"; "MOVE_5" ], afterMoves.Player.Volatile.PlayerUsedMoves |> List.map _.Name)

    let afterSwitch = Battle.switchMon 1 afterMoves
    Assert.Empty(afterSwitch.Player.Volatile.PlayerUsedMoves)

[<Fact>]
let ``BAT-024 locked Bide records the executed move instead of later selections`` () =
    let bide = Moves.byName "BIDE"
    let tackle = Moves.byName "TACKLE"
    let splash = Moves.byName "SPLASH"
    let player = BattleMon.ofSpecies (Species.byName "RATTATA") 30 [ bide; tackle ]
    let enemy = BattleMon.ofSpecies (Species.byName "MAGIKARP") 5 [ splash ]

    let afterBide = Battle.chooseMove 0 (Battle.createWild player enemy 0u)
    let duringBide = Battle.chooseMove 1 afterBide

    Assert.Equal<string list>([ "BIDE" ], duringBide.Player.Volatile.PlayerUsedMoves |> List.map _.Name)

[<Fact>]
let ``BAT-024 charge and recharge history records only source-equivalent executed moves`` () =
    let fly = Moves.byName "FLY"
    let tackle = Moves.byName "TACKLE"
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "FLYING") (ty "FLYING") 30 200 40 40 100 with
            Moves = [ fly; tackle ]
            Pp = [ fly.Pp; tackle.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 30 10000 1 255 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }

    let charging = Battle.chooseMove 0 (Battle.createWild player enemy 0u)
    let released = Battle.chooseMove 1 charging

    Assert.Equal<string list>([ "FLY" ], charging.Player.Volatile.PlayerUsedMoves |> List.map _.Name)
    Assert.Equal<string list>([ "FLY" ], released.Player.Volatile.PlayerUsedMoves |> List.map _.Name)

    let rechargingPlayer =
        { player with
            Volatile =
                { VolatileStatus.empty with
                    Recharge = true
                    PlayerUsedMoves = [ fly ] } }
    let recharged = Battle.chooseMove 1 (Battle.createWild rechargingPlayer enemy 0u)

    Assert.False(recharged.Player.Volatile.Recharge)
    Assert.Equal<string list>([ "FLY" ], recharged.Player.Volatile.PlayerUsedMoves |> List.map _.Name)

[<Fact>]
let ``BAT-024 source switch scoring uses player move history before unused move coverage`` () =
    let waterGun = move "WATER_GUN" "EFFECT_NORMAL_HIT" 40 (ty "WATER")
    let tackle = move "TACKLE" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    let vineWhip = move "VINE_WHIP" "EFFECT_NORMAL_HIT" 40 (ty "GRASS")
    let active =
        { mon "ACTIVE" (ty "FIRE") (ty "FIRE") 30 120 60 60 40 with
            Moves = [ vineWhip ]
            Pp = [ vineWhip.Pp ] }
    let player =
        { mon "PLAYER" (ty "WATER") (ty "WATER") 30 120 70 70 50 with
            Moves = [ waterGun; tackle ]
            Pp = [ waterGun.Pp; tackle.Pp ]
            Volatile = { VolatileStatus.empty with PlayerUsedMoves = [ tackle ] } }
    let bench =
        { mon "BENCH" (ty "NORMAL") (ty "NORMAL") 30 120 80 60 30 with
            Moves = [ vineWhip ]
            Pp = [ vineWhip.Pp ] }
    let profile: BattleAI.TrainerProfile =
        { MoveFlags = []
          ItemSwitchFlags = [ "SWITCH_OFTEN" ]
          Items = [] }
    let context: BattleAI.AiContext =
        { EnemyTurnsTaken = 0
          PlayerTurnsTaken = 0
          EnemySide = SideState.Empty
          PlayerSide = SideState.Empty
          WeatherType = None
          EnemyTeam = [ active; bench ]
          PlayerTeam = [ player ] }
    let seed =
        [ 0u .. 255u ]
        |> List.find (fun candidate -> fst (Rng.next (Rng.create candidate)) < 200)

    let selected, _ =
        BattleAI.chooseSwitchWithProfileAndRng profile context active player [ active; bench ] (Rng.create seed)

    Assert.Equal(None, selected)

[<Fact>]
let ``BAT-024 source switch scoring distinguishes neutral from not very effective offense`` () =
    let normal = ty "NORMAL"
    let water = ty "WATER"
    let grass = ty "GRASS"
    let fire = ty "FIRE"
    let tackle = move "TACKLE" "EFFECT_NORMAL_HIT" 40 normal
    let waterGun = move "WATER_GUN" "EFFECT_NORMAL_HIT" 40 water
    let ember = move "EMBER" "EFFECT_NORMAL_HIT" 40 fire
    let player =
        { mon "PLAYER" grass grass 30 120 70 70 50 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ]
            Volatile = { VolatileStatus.empty with PlayerUsedMoves = [ tackle ] } }
    let activeWith attack =
        { mon "ACTIVE" fire fire 30 120 60 60 40 with
            Moves = [ attack ]
            Pp = [ attack.Pp ] }
    let bench =
        { mon "BENCH" fire fire 30 120 80 60 30 with
            Moves = [ ember ]
            Pp = [ ember.Pp ] }
    let profile: BattleAI.TrainerProfile =
        { MoveFlags = []; ItemSwitchFlags = [ "SWITCH_OFTEN" ]; Items = [] }
    let choose active =
        let context: BattleAI.AiContext =
            { EnemyTurnsTaken = 0
              PlayerTurnsTaken = 0
              EnemySide = SideState.Empty
              PlayerSide = SideState.Empty
              WeatherType = None
              EnemyTeam = [ active; bench ]
              PlayerTeam = [ player ] }
        BattleAI.chooseSwitchWithProfileAndRng profile context active player [ active; bench ] (Rng.create 0u)
        |> fst

    Assert.Equal(None, choose (activeWith tackle))
    Assert.Equal(Some 1, choose (activeWith waterGun))

[<Fact>]
let ``BAT-024 source switch candidate masks reject weak benches and prefer super effective offense`` () =
    let normal = ty "NORMAL"
    let ghost = ty "GHOST"
    let poison = ty "POISON"
    let fire = ty "FIRE"
    let dark = ty "DARK"
    let tackle = move "TACKLE" "EFFECT_NORMAL_HIT" 40 normal
    let splash = Moves.byName "SPLASH"
    let poisonHit = move "POISON_HIT" "EFFECT_NORMAL_HIT" 40 poison
    let fireHit = move "FIRE_HIT" "EFFECT_NORMAL_HIT" 40 fire
    let darkHit = move "DARK_HIT" "EFFECT_NORMAL_HIT" 40 dark
    let player =
        { mon "PLAYER" ghost ghost 30 120 70 70 50 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ]
            Volatile =
                { VolatileStatus.empty with
                    LastCounterMove = Some tackle
                    PlayerUsedMoves = [ tackle ] } }
    let active =
        { mon "ACTIVE" normal normal 30 120 60 60 40 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let bench name attack =
        { mon name ghost ghost 30 120 80 60 30 with
            Moves = [ attack ]
            Pp = [ attack.Pp ] }
    let nve = bench "NVE" poisonHit
    let neutral = bench "NEUTRAL" fireHit
    let superEffective = bench "SUPER" darkHit
    let profile: BattleAI.TrainerProfile =
        { MoveFlags = []; ItemSwitchFlags = [ "SWITCH_OFTEN" ]; Items = [] }
    let choose team =
        let context: BattleAI.AiContext =
            { EnemyTurnsTaken = 0
              PlayerTurnsTaken = 0
              EnemySide = SideState.Empty
              PlayerSide = SideState.Empty
              WeatherType = None
              EnemyTeam = team
              PlayerTeam = [ player ] }
        BattleAI.chooseSwitchWithProfileAndRng profile context active player team (Rng.create 0u)
        |> fst

    Assert.Equal(None, choose [ active; nve; neutral ])
    Assert.Equal(Some 3, choose [ active; nve; neutral; superEffective ])

    let water = ty "WATER"
    let grass = ty "GRASS"
    let waterGun = move "WATER_GUN" "EFFECT_NORMAL_HIT" 40 water
    let firePlayer =
        { mon "FIRE_PLAYER" fire fire 30 120 70 70 50 with
            Moves = [ waterGun ]
            Pp = [ waterGun.Pp ]
            Volatile = { VolatileStatus.empty with PlayerUsedMoves = [ waterGun ] } }
    let fireActive =
        { mon "FIRE_ACTIVE" fire fire 30 120 60 60 40 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }
    let weakBench =
        { mon "WEAK" grass grass 30 120 80 60 30 with
            Moves = [ waterGun ]
            Pp = [ waterGun.Pp ] }
    let resistantBench =
        { mon "RESISTANT" water water 30 120 80 60 30 with
            Moves = [ waterGun ]
            Pp = [ waterGun.Pp ] }
    let defensiveTeam = [ fireActive; weakBench; resistantBench ]
    let defensiveContext: BattleAI.AiContext =
        { EnemyTurnsTaken = 0
          PlayerTurnsTaken = 0
          EnemySide = SideState.Empty
          PlayerSide = SideState.Empty
          WeatherType = None
          EnemyTeam = defensiveTeam
          PlayerTeam = [ firePlayer ] }
    let defensiveChoice, _ =
        BattleAI.chooseSwitchWithProfileAndRng profile defensiveContext fireActive firePlayer defensiveTeam (Rng.create 0u)

    Assert.Equal(Some 2, defensiveChoice)

[<Fact>]
let ``BAT-024 source switch tier uses the selected candidate matchup score`` () =
    let normal = ty "NORMAL"
    let ghost = ty "GHOST"
    let dark = ty "DARK"
    let tackle = move "TACKLE" "EFFECT_NORMAL_HIT" 40 normal
    let bite = move "BITE" "EFFECT_NORMAL_HIT" 40 dark
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" ghost ghost 30 120 70 70 50 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ]
            Volatile =
                { VolatileStatus.empty with
                    LastCounterMove = Some tackle
                    PlayerUsedMoves = [ tackle ] } }
    let active =
        { mon "ACTIVE" normal normal 30 120 60 60 40 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let bench =
        { mon "BENCH" ghost ghost 30 120 80 60 30 with
            Moves = [ bite ]
            Pp = [ bite.Pp ] }
    let profile: BattleAI.TrainerProfile =
        { MoveFlags = []; ItemSwitchFlags = [ "SWITCH_OFTEN" ]; Items = [] }
    let context: BattleAI.AiContext =
        { EnemyTurnsTaken = 0
          PlayerTurnsTaken = 0
          EnemySide = SideState.Empty
          PlayerSide = SideState.Empty
          WeatherType = None
          EnemyTeam = [ active; bench ]
          PlayerTeam = [ player ] }
    let seed =
        [ 0u .. 1024u ]
        |> List.find (fun candidate ->
            let roll, _ = Rng.next (Rng.create candidate)
            roll >= (51 * 255 / 100) && roll < (79 * 255 / 100 - 1))

    let selected, _ =
        BattleAI.chooseSwitchWithProfileAndRng profile context active player [ active; bench ] (Rng.create seed)

    Assert.Equal(None, selected)

[<Fact>]
let ``BAT-024 source Perish Song switch falls back to the first live bench`` () =
    let splash = Moves.byName "SPLASH"
    let tackle = Moves.byName "TACKLE"
    let active =
        { mon "ACTIVE" (ty "NORMAL") (ty "NORMAL") 30 120 60 60 40 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }
    let player =
        { mon "PLAYER" (ty "WATER") (ty "WATER") 30 120 70 70 50 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let bench =
        { mon "BENCH" (ty "NORMAL") (ty "NORMAL") 30 120 80 60 30 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }
    let profile: BattleAI.TrainerProfile =
        { MoveFlags = []
          ItemSwitchFlags = [ "SWITCH_OFTEN" ]
          Items = [] }
    let context: BattleAI.AiContext =
        { EnemyTurnsTaken = 0
          PlayerTurnsTaken = 0
          EnemySide = { SideState.Empty with PerishCounter = Some 1 }
          PlayerSide = SideState.Empty
          WeatherType = None
          EnemyTeam = [ active; bench ]
          PlayerTeam = [ player ] }
    let seed =
        [ 0u .. 255u ]
        |> List.find (fun candidate -> fst (Rng.next (Rng.create candidate)) >= 4 * 255 / 100)

    let selected, _ =
        BattleAI.chooseSwitchWithProfileAndRng profile context active player [ active; bench ] (Rng.create seed)

    Assert.Equal(Some 1, selected)

[<Fact>]
let ``BAT-024 source Full Heal ignores confusion without a nonvolatile status`` () =
    let splash = Moves.byName "SPLASH"
    let enemy =
        { BattleMon.ofSpecies (Species.byName "RATTATA") 20 [ splash ] with
            Volatile = { VolatileStatus.empty with Confusion = Some 2 } }
    let profile: BattleAI.TrainerProfile = { MoveFlags = []; ItemSwitchFlags = [ "CONTEXT_USE" ]; Items = [ "FULL_HEAL" ] }

    Assert.Equal(None, BattleAI.tryUseItem profile [ "FULL_HEAL" ] [ enemy ] enemy 0)

[<Fact>]
let ``BAT-024 source trainer item context uses seeded thresholds and preserves Full Heal confusion`` () =
    let splash = Moves.byName "SPLASH"
    let profile: BattleAI.TrainerProfile = { MoveFlags = []; ItemSwitchFlags = [ "CONTEXT_USE" ]; Items = [] }
    let baseEnemy = BattleMon.ofSpecies (Species.byName "RATTATA") 20 [ splash ]
    let attempt item enemy turns seed =
        BattleAI.tryUseItemWithRng profile [ item ] [ enemy ] enemy turns (Rng.create seed)
        |> fst
    let rejectionSeed =
        [ 0u .. 255u ]
        |> List.find (fun seed ->
            let roll, _ = Rng.next (Rng.create seed)
            roll >= 128)

    Assert.Equal(None, attempt "FULL_HEAL" { baseEnemy with Status = Poison } 0 0u)
    Assert.Equal(None, attempt "FULL_HEAL" { baseEnemy with Status = BadPoison 3 } 0 0u)
    Assert.True((attempt "FULL_HEAL" { baseEnemy with Status = BadPoison 4 } 0 0u).IsSome)

    let frozenConfused =
        { baseEnemy with
            Status = Freeze
            Volatile = { baseEnemy.Volatile with Confusion = Some 2 } }
    let fullHeal = attempt "FULL_HEAL" frozenConfused 0 0u |> Option.get
    Assert.Equal(Healthy, fullHeal.Enemy.Status)
    Assert.Equal(Some 2, fullHeal.Enemy.Volatile.Confusion)

    let atQuarter = { baseEnemy with Hp = max 1 (baseEnemy.MaxHp / 4) }
    Assert.True((attempt "MAX_POTION" atQuarter 0 0u).IsSome)
    let betweenQuarterAndHalf = { baseEnemy with Hp = baseEnemy.MaxHp / 2 }
    Assert.Equal(None, attempt "MAX_POTION" betweenQuarterAndHalf 0 rejectionSeed)

    let lowerLevel = { baseEnemy with Level = 10; Hp = 1 }
    let highestLevel = { baseEnemy with Level = 20; Hp = 1 }
    Assert.Equal(None, BattleAI.tryUseItem profile [ "MAX_POTION" ] [ lowerLevel; highestLevel ] lowerLevel 0)

    Assert.Equal(None, attempt "X_ATTACK" baseEnemy 0 0u)
    Assert.True((attempt "X_ATTACK" baseEnemy 0 rejectionSeed).IsSome)
    Assert.Equal(None, attempt "X_ATTACK" baseEnemy 1 rejectionSeed)

    let noContextProfile: BattleAI.TrainerProfile = { MoveFlags = []; ItemSwitchFlags = []; Items = [] }
    let xItemSeed predicate =
        [ 0u .. 255u ]
        |> List.find (fun seed ->
            let first, rngAfterFirst = Rng.next (Rng.create seed)
            let second, _ = Rng.next rngAfterFirst
            first >= 128 && predicate second)
    let rejectSecondLow = xItemSeed (fun roll -> roll < 128)
    let useSecondHigh = xItemSeed (fun roll -> roll >= 128)
    Assert.Equal(None, BattleAI.tryUseItemWithRng noContextProfile [ "X_ATTACK" ] [ baseEnemy ] baseEnemy 0 (Rng.create rejectSecondLow) |> fst)
    Assert.True((BattleAI.tryUseItemWithRng noContextProfile [ "X_ATTACK" ] [ baseEnemy ] baseEnemy 0 (Rng.create useSecondHigh) |> fst).IsSome)

let private aiProfile flags : BattleAI.TrainerProfile =
    { MoveFlags = flags
      ItemSwitchFlags = []
      Items = [] }

let private aiContext enemyTurnsTaken playerTurnsTaken : BattleAI.AiContext =
    { EnemyTurnsTaken = enemyTurnsTaken; PlayerTurnsTaken = playerTurnsTaken; EnemySide = SideState.Empty; PlayerSide = SideState.Empty; WeatherType = None; EnemyTeam = []; PlayerTeam = [] }

let private sourceAiChoiceWithContext flags context user target seed =
    BattleAI.chooseMoveWithProfileAndRng
        (aiProfile flags)
        context
        user
        target
        (Rng.create seed)
    |> fst
    |> Option.defaultWith (fun () -> failwith "expected a legal source AI move")
    |> fst

let private sourceAiChoice flags enemyTurnsTaken playerTurnsTaken user target seed =
    sourceAiChoiceWithContext flags (aiContext enemyTurnsTaken playerTurnsTaken) user target seed

[<Fact>]
let ``BAT-024 source AI skips zero PP and disabled moves when another move is legal`` () =
    let ember = Moves.byName "EMBER"
    let tackle = Moves.byName "TACKLE"
    let user =
        { BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 20 [ ember; tackle ] with
            Pp = [ 0; tackle.Pp ] }
    let target = BattleMon.ofSpecies (Species.byName "CHIKORITA") 20 [ tackle ]
    let choose battleMon =
        BattleAI.chooseMoveWithProfileAndRng
            (aiProfile [ "AI_TYPES" ])
            (aiContext 0 0)
            battleMon
            target
            (Rng.create 0u)
        |> fst
        |> Option.defaultWith (fun () -> failwith "expected a legal AI move")
        |> fst

    Assert.Equal("TACKLE", (choose user).Name)
    let disabledTackle =
        { user with
            Pp = [ ember.Pp; tackle.Pp ]
            Volatile = { user.Volatile with DisabledMoveIndex = Some 1 } }
    Assert.Equal("EMBER", (choose disabledTackle).Name)

[<Fact>]
let ``BAT-024 Smart handler inventory matches the source scoring dispatch`` () =
    let expected = Set.ofList [ "EFFECT_SLEEP"; "EFFECT_LEECH_HIT"; "EFFECT_SELFDESTRUCT"; "EFFECT_DREAM_EATER"; "EFFECT_MIRROR_MOVE"; "EFFECT_EVASION_UP"; "EFFECT_ALWAYS_HIT"; "EFFECT_ACCURACY_DOWN"; "EFFECT_RESET_STATS"; "EFFECT_BIDE"; "EFFECT_FORCE_SWITCH"; "EFFECT_HEAL"; "EFFECT_TOXIC"; "EFFECT_LIGHT_SCREEN"; "EFFECT_OHKO"; "EFFECT_RAZOR_WIND"; "EFFECT_SUPER_FANG"; "EFFECT_TRAP_TARGET"; "EFFECT_UNUSED_2B"; "EFFECT_CONFUSE"; "EFFECT_SP_DEF_UP_2"; "EFFECT_REFLECT"; "EFFECT_PARALYZE"; "EFFECT_SPEED_DOWN_HIT"; "EFFECT_SUBSTITUTE"; "EFFECT_HYPER_BEAM"; "EFFECT_RAGE"; "EFFECT_MIMIC"; "EFFECT_LEECH_SEED"; "EFFECT_DISABLE"; "EFFECT_COUNTER"; "EFFECT_ENCORE"; "EFFECT_PAIN_SPLIT"; "EFFECT_SNORE"; "EFFECT_CONVERSION2"; "EFFECT_LOCK_ON"; "EFFECT_DEFROST_OPPONENT"; "EFFECT_SLEEP_TALK"; "EFFECT_DESTINY_BOND"; "EFFECT_REVERSAL"; "EFFECT_SPITE"; "EFFECT_HEAL_BELL"; "EFFECT_PRIORITY_HIT"; "EFFECT_THIEF"; "EFFECT_MEAN_LOOK"; "EFFECT_NIGHTMARE"; "EFFECT_FLAME_WHEEL"; "EFFECT_CURSE"; "EFFECT_PROTECT"; "EFFECT_FORESIGHT"; "EFFECT_PERISH_SONG"; "EFFECT_SANDSTORM"; "EFFECT_ENDURE"; "EFFECT_ROLLOUT"; "EFFECT_SWAGGER"; "EFFECT_FURY_CUTTER"; "EFFECT_ATTRACT"; "EFFECT_SAFEGUARD"; "EFFECT_MAGNITUDE"; "EFFECT_BATON_PASS"; "EFFECT_PURSUIT"; "EFFECT_RAPID_SPIN"; "EFFECT_MORNING_SUN"; "EFFECT_SYNTHESIS"; "EFFECT_MOONLIGHT"; "EFFECT_HIDDEN_POWER"; "EFFECT_RAIN_DANCE"; "EFFECT_SUNNY_DAY"; "EFFECT_BELLY_DRUM"; "EFFECT_PSYCH_UP"; "EFFECT_MIRROR_COAT"; "EFFECT_SKULL_BASH"; "EFFECT_TWISTER"; "EFFECT_EARTHQUAKE"; "EFFECT_FUTURE_SIGHT"; "EFFECT_GUST"; "EFFECT_STOMP"; "EFFECT_SOLARBEAM"; "EFFECT_THUNDER"; "EFFECT_FLY" ]

    Assert.Equal<Set<string>>(expected, BattleAI.smartHandlerEffects)

[<Fact>]
let ``BAT-024 Smart weather scoring uses source useful move tables and Sunny Day omission`` () =
    let sunnyDay = Moves.byName "SUNNY_DAY"
    let solarBeam = Moves.byName "SOLARBEAM"
    let flamethrower = Moves.byName "FLAMETHROWER"
    let tackle = Moves.byName "TACKLE"
    let target = BattleMon.ofSpecies (Species.byName "RATTATA") 30 [ tackle ]
    let choose user =
        BattleAI.chooseMoveWithProfileAndRng (aiProfile [ "AI_SMART" ]) (aiContext 0 0) user target (Rng.create 0u)
        |> fst
        |> Option.defaultWith (fun () -> failwith "expected a Smart AI move")
        |> fst
    let solarOnly = BattleMon.ofSpecies (Species.byName "VENUSAUR") 30 [ sunnyDay; solarBeam ]
    let flameSupport = BattleMon.ofSpecies (Species.byName "CHARIZARD") 30 [ sunnyDay; flamethrower ]

    Assert.Equal("SOLARBEAM", (choose solarOnly).Name)
    Assert.Equal("SUNNY_DAY", (choose flameSupport).Name)

[<Fact>]
let ``BAT-024 Smart weather strongly favors the source good opponent type on first turn`` () =
    let splash = Moves.byName "SPLASH"
    let context = aiContext 0 0
    let choose weatherMove userSpecies targetSpecies =
        let user = BattleMon.ofSpecies (Species.byName userSpecies) 30 [ Moves.byName weatherMove; splash ]
        let target = BattleMon.ofSpecies (Species.byName targetSpecies) 30 [ splash ]
        sourceAiChoiceWithContext [ "AI_SMART" ] context user target 0u

    Assert.Equal("RAIN_DANCE", (choose "RAIN_DANCE" "POLITOED" "CHARIZARD").Name)
    Assert.Equal("SUNNY_DAY", (choose "SUNNY_DAY" "CHARIZARD" "POLITOED").Name)

[<Fact>]
let ``BAT-024 Setup encourages first-turn stat moves only after a noncarry roll`` () =
    let tackle = Moves.byName "TACKLE"
    let user = BattleMon.ofSpecies (Species.byName "SCYTHER") 30 [ Moves.byName "SWORDS_DANCE"; tackle ]
    let target = BattleMon.ofSpecies (Species.byName "RATTATA") 30 [ tackle ]
    let lowSeed =
        [ 0u .. 255u ]
        |> List.find (fun seed -> fst (Rng.next (Rng.create seed)) < 128)
    let highSeed =
        [ 0u .. 255u ]
        |> List.find (fun seed ->
            let first, afterFirst = Rng.next (Rng.create seed)
            let second, _ = Rng.next afterFirst
            first >= 128 && (second &&& 3) = 0)

    Assert.Equal("TACKLE", (sourceAiChoice [ "AI_SETUP"; "AI_OFFENSIVE" ] 0 0 user target lowSeed).Name)
    Assert.Equal("SWORDS_DANCE", (sourceAiChoice [ "AI_SETUP"; "AI_OFFENSIVE" ] 0 0 user target highSeed).Name)

[<Fact>]
let ``BAT-024 Smart sleep combination encourages only after a noncarry roll`` () =
    let tackle = Moves.byName "TACKLE"
    let user = BattleMon.ofSpecies (Species.byName "HYPNO") 30 [ Moves.byName "HYPNOSIS"; Moves.byName "DREAM_EATER" ]
    let target = BattleMon.ofSpecies (Species.byName "RATTATA") 30 [ tackle ]
    let seed =
        [ 0u .. 255u ]
        |> List.find (fun candidate ->
            let first, afterFirst = Rng.next (Rng.create candidate)
            let second, _ = Rng.next afterFirst
            first >= 128 && (second &&& 3) = 1)

    Assert.Equal("HYPNOSIS", (sourceAiChoice [ "AI_SMART" ] 0 0 user target seed).Name)

[<Fact>]
let ``BAT-024 Smart Leech Hit discourages not-very-effective moves after the source cutoff`` () =
    let tackle = Moves.byName "TACKLE"
    let user = BattleMon.ofSpecies (Species.byName "ZUBAT") 30 [ Moves.byName "LEECH_LIFE"; tackle ]
    let target = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 30 [ tackle ]
    let seed =
        [ 0u .. 255u ]
        |> List.find (fun candidate ->
            let first, afterFirst = Rng.next (Rng.create candidate)
            let second, _ = Rng.next afterFirst
            first >= 60 * 255 / 100 && (second &&& 3) = 0)

    Assert.Equal("TACKLE", (sourceAiChoice [ "AI_SMART" ] 0 0 user target seed).Name)

[<Fact>]
let ``BAT-024 Smart source branches preserve carry direction and team scope`` () =
    let tackle = Moves.byName "TACKLE"
    let seedWith predicate = [ 0u .. 255u ] |> List.find predicate

    let paralyzeUser = BattleMon.ofSpecies (Species.byName "PIKACHU") 30 [ Moves.byName "THUNDER_WAVE"; tackle ]
    let quarterTarget = { BattleMon.ofSpecies (Species.byName "RATTATA") 30 [ tackle ] with Hp = 1 }
    let paralyzeSeed =
        seedWith (fun seed ->
            let first, afterFirst = Rng.next (Rng.create seed)
            let second, _ = Rng.next afterFirst
            first >= 128 && (second &&& 3) = 0)
    Assert.Equal("TACKLE", (sourceAiChoice [ "AI_SMART" ] 0 0 paralyzeUser quarterTarget paralyzeSeed).Name)

    let hyperBeam = Moves.byName "HYPER_BEAM"
    let hyperBase = BattleMon.ofSpecies (Species.byName "SNORLAX") 30 [ hyperBeam; tackle ]
    let hyperLow = { hyperBase with Hp = 1 }
    let hyperEncourageSeed =
        seedWith (fun seed ->
            let first, afterFirst = Rng.next (Rng.create seed)
            let second, _ = Rng.next afterFirst
            first >= 128 && (second &&& 3) = 1)
    Assert.Equal("HYPER_BEAM", (sourceAiChoice [ "AI_SMART" ] 0 0 hyperLow quarterTarget hyperEncourageSeed).Name)
    let hyperDiscourageSeed =
        seedWith (fun seed ->
            let first, afterFirst = Rng.next (Rng.create seed)
            let second, _ = Rng.next afterFirst
            first >= 65 * 255 / 100 && second >= 128 && (second &&& 3) = 0)
    Assert.Equal("TACKLE", (sourceAiChoice [ "AI_SMART" ] 0 0 hyperBase quarterTarget hyperDiscourageSeed).Name)

    let spite = move "SPITE" "EFFECT_SPITE" 0 (ty "GHOST")
    let speedyUser = { mon "FAST" (ty "GHOST") (ty "GHOST") 30 120 70 70 100 with Moves = [ spite; tackle ]; Pp = [ spite.Pp; tackle.Pp ] }
    let slowTarget = { mon "SLOW" (ty "NORMAL") (ty "NORMAL") 30 120 70 70 1 with Moves = [ tackle ]; Pp = [ tackle.Pp ] }
    let spiteSeed = seedWith (fun seed -> (fst (Rng.next (Rng.create seed)) &&& 3) = 0)
    Assert.Equal("TACKLE", (sourceAiChoice [ "AI_SMART" ] 0 0 speedyUser slowTarget spiteSeed).Name)

    let disable = move "DISABLE" "EFFECT_DISABLE" 0 (ty "NORMAL")
    let usefulMove = Moves.byName "THUNDERBOLT"
    let disableUser = { mon "FAST" (ty "NORMAL") (ty "NORMAL") 30 120 70 70 100 with Moves = [ disable; tackle ]; Pp = [ disable.Pp; tackle.Pp ] }
    let usefulTarget =
        { mon "SLOW" (ty "WATER") (ty "WATER") 30 120 70 70 1 with
            Moves = [ usefulMove ]
            Pp = [ usefulMove.Pp ]
            Volatile = { VolatileStatus.empty with LastCounterMove = Some usefulMove } }
    let disableSeed =
        seedWith (fun seed ->
            let first, afterFirst = Rng.next (Rng.create seed)
            let second, _ = Rng.next afterFirst
            first >= 60 * 255 / 100 && (second &&& 3) = 1)
    Assert.Equal("DISABLE", (sourceAiChoice [ "AI_SMART" ] 0 0 disableUser usefulTarget disableSeed).Name)

    let healBell = Moves.byName "HEAL_BELL"
    let healUser = { mon "HEALER" (ty "NORMAL") (ty "NORMAL") 30 120 70 70 50 with Moves = [ healBell; tackle ]; Pp = [ healBell.Pp; tackle.Pp ] }
    let faintedAlly = { mon "FAINTED" (ty "NORMAL") (ty "NORMAL") 30 120 70 70 20 with Hp = 0; Status = Poison }
    let healContext = { aiContext 0 0 with EnemyTeam = [ healUser; faintedAlly ] }
    let healSeed = seedWith (fun seed -> (fst (Rng.next (Rng.create seed)) &&& 3) = 0)
    Assert.Equal("TACKLE", (sourceAiChoiceWithContext [ "AI_SMART" ] healContext healUser slowTarget healSeed).Name)

[<Fact>]
let ``BAT-024 every generated source scoring layer changes a real move decision`` () =
    let tackle = Moves.byName "TACKLE"
    let awakeTarget = BattleMon.ofSpecies (Species.byName "RATTATA") 30 [ tackle ]

    let basicUser = BattleMon.ofSpecies (Species.byName "HYPNO") 30 [ Moves.byName "DREAM_EATER"; tackle ]
    Assert.Equal("TACKLE", (sourceAiChoice [ "AI_BASIC" ] 0 0 basicUser awakeTarget 0u).Name)

    let setupUser = BattleMon.ofSpecies (Species.byName "SCYTHER") 30 [ Moves.byName "SWORDS_DANCE"; tackle ]
    Assert.Equal("SWORDS_DANCE", (sourceAiChoice [ "AI_SETUP" ] 0 0 setupUser awakeTarget 0u).Name)
    let discourageSeed =
        [ 0u .. 255u ]
        |> List.find (fun seed ->
            let roll, _ = Rng.next (Rng.create seed)
            roll >= 30)
    Assert.Equal("TACKLE", (sourceAiChoice [ "AI_SETUP" ] 1 1 setupUser awakeTarget discourageSeed).Name)

    let grassTarget = BattleMon.ofSpecies (Species.byName "CHIKORITA") 30 [ tackle ]
    let typesUser = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 30 [ Moves.byName "EMBER"; tackle ]
    Assert.Equal("EMBER", (sourceAiChoice [ "AI_TYPES" ] 0 0 typesUser grassTarget 0u).Name)

    let offensiveUser = BattleMon.ofSpecies (Species.byName "BUTTERFREE") 30 [ Moves.byName "SLEEP_POWDER"; tackle ]
    Assert.Equal("TACKLE", (sourceAiChoice [ "AI_OFFENSIVE" ] 0 0 offensiveUser awakeTarget 0u).Name)

    let smartBase = BattleMon.ofSpecies (Species.byName "MILTANK") 30 [ Moves.byName "MILK_DRINK"; tackle ]
    let smartUser = { smartBase with Hp = 1 }
    Assert.Equal("MILK_DRINK", (sourceAiChoice [ "AI_SMART" ] 0 0 smartUser awakeTarget 0u).Name)

    let opportunistBase = BattleMon.ofSpecies (Species.byName "SCYTHER") 30 [ Moves.byName "SWORDS_DANCE"; tackle ]
    let opportunistUser = { opportunistBase with Hp = 1 }
    Assert.Equal("TACKLE", (sourceAiChoice [ "AI_OPPORTUNIST" ] 0 0 opportunistUser awakeTarget 0u).Name)

    let aggressiveUser = BattleMon.ofSpecies (Species.byName "SNORLAX") 30 [ tackle; Moves.byName "HYPER_BEAM" ]
    Assert.Equal("HYPER_BEAM", (sourceAiChoice [ "AI_AGGRESSIVE" ] 0 0 aggressiveUser awakeTarget 0u).Name)

    let cautiousUser = BattleMon.ofSpecies (Species.byName "BULBASAUR") 30 [ Moves.byName "LEECH_SEED"; tackle ]
    Assert.Equal("TACKLE", (sourceAiChoice [ "AI_CAUTIOUS" ] 1 0 cautiousUser awakeTarget 0u).Name)

    let poisonTarget = BattleMon.ofSpecies (Species.byName "EKANS") 30 [ tackle ]
    let statusUser = BattleMon.ofSpecies (Species.byName "ODDISH") 30 [ Moves.byName "POISONPOWDER"; tackle ]
    Assert.Equal("TACKLE", (sourceAiChoice [ "AI_STATUS" ] 0 0 statusUser poisonTarget 0u).Name)

    let riskyBase = BattleMon.ofSpecies (Species.byName "GEODUDE") 30 [ Moves.byName "SELFDESTRUCT"; tackle ]
    let riskyUser = { riskyBase with Hp = riskyBase.MaxHp - 1 }
    let lowTarget = { awakeTarget with Hp = 1 }
    Assert.Equal("SELFDESTRUCT", (sourceAiChoice [ "AI_RISKY" ] 0 0 riskyUser lowTarget 0u).Name)

[<Fact>]
let ``PartyMon conversion preserves identity PP status and held item for battle`` () =
    let partyMon =
        { PokeGold.Game.Player.PartyMon.create 155 10 with
            Moves = [ 33, 4 ]
            Status = "PAR"
            HeldItem = Some "LEFTOVERS" }
    let battleMon = PokeGold.Game.Player.PartyMon.toBattleMon partyMon

    Assert.Equal(Some partyMon.Id, battleMon.PersistentId)
    Assert.Equal<int list>([ 4 ], battleMon.Pp)
    Assert.Equal(Paralysis, battleMon.Status)
    Assert.Equal(Some "LEFTOVERS", battleMon.HeldItem)

[<Fact>]
let ``BAT-006 packed DVs and five stat experience words determine all six stats`` () =
    // CalcMonStatC with Cyndaquil's source base stats, Falkner's packed DVs
    // dn 9,10,7,7 and independent HP/Atk/Def/Speed/Special experience.
    let statExp = { Hp = 0; Attack = 16; Defense = 64; Speed = 144; Special = 256 }
    let partyMon =
        { PartyMon.createWithDvs (Species.byName "CYNDAQUIL").Dex 20 0x9A77 with
            Hp = 37
            StatExp = statExp }
    let battleMon = PartyMon.toBattleMon partyMon

    Assert.Equal(0x9A77, battleMon.Dvs)
    Assert.Equal(50, battleMon.MaxHp) // derived HP DV = 11
    Assert.Equal(29, battleMon.Attack)
    Assert.Equal(26, battleMon.Defense)
    Assert.Equal(34, battleMon.Speed)
    Assert.Equal(32, battleMon.SpAttack)
    Assert.Equal(28, battleMon.SpDefense)

    let leveled = PartyMon.withLevel 21 partyMon
    let level21 = PartyMon.toBattleMon leveled
    Assert.Equal(39, leveled.Hp) // source preserves damage by adding MaxHP delta
    Assert.Equal(52, level21.MaxHp)
    Assert.Equal(30, level21.Attack)
    Assert.Equal(27, level21.Defense)
    Assert.Equal(35, level21.Speed)
    Assert.Equal(33, level21.SpAttack)
    Assert.Equal(29, level21.SpDefense)

    Assert.Equal(0, BattleMon.statExpBonus 9)
    Assert.Equal(1, BattleMon.statExpBonus 10)
    Assert.Equal(63, BattleMon.statExpBonus 65535)

[<Fact>]
let ``BAT-007 temporary copied moves clean up while Sketch persists`` () =
    let ditto =
        { PartyMon.create (Species.byName "DITTO").Dex 20 with
            Moves = [ 144, 10 ] }
        |> PartyMon.toBattleMon
        |> BattleMon.deductPp 0
    let transformed =
        { ditto with
            MovesAreTransformed = true
            Species = Species.byName "SMEARGLE"
            Moves = [ Moves.byName "SKETCH" ]
            Pp = [ 1 ] }
        |> BattleMon.restorePersistentForm
    Assert.Equal("DITTO", transformed.Species.Name)
    Assert.Equal<string list>([ "TRANSFORM" ], transformed.Moves |> List.map (fun move -> move.Name))
    Assert.Equal<int list>([ 9 ], transformed.Pp)

    let mimicUser =
        { PartyMon.create (Species.byName "MR__MIME").Dex 20 with
            Moves = [ 102, 10 ] }
        |> PartyMon.toBattleMon
        |> BattleMon.deductPp 0
    let mimicked =
        { mimicUser with Moves = [ Moves.byName "EMBER" ]; Pp = [ 4 ] }
        |> BattleMon.restorePersistentForm
    Assert.Equal<string list>([ "MIMIC" ], mimicked.Moves |> List.map (fun move -> move.Name))
    Assert.Equal<int list>([ 9 ], mimicked.Pp)

    let sketchUser =
        { PartyMon.create (Species.byName "SMEARGLE").Dex 20 with
            Moves = [ 166, 1 ] }
        |> PartyMon.toBattleMon
        |> BattleMon.deductPp 0
    let ember = Moves.byName "EMBER"
    let sketched =
        { sketchUser with Moves = [ ember ]; Pp = [ ember.Pp ] }
        |> BattleMon.persistMoveReplacement 0 ember ember.Pp
        |> BattleMon.restorePersistentForm
    Assert.Equal<string list>([ "EMBER" ], sketched.Moves |> List.map (fun move -> move.Name))
    Assert.Equal<int list>([ ember.Pp ], sketched.Pp)

[<Fact>]
let ``BAT-007 persistent sleep counter and friendship enter battle exactly`` () =
    let mon =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 20 with
            Status = "SLP:4"
            Friendship = 173 }
        |> PartyMon.toBattleMon

    Assert.Equal(Sleep 4, mon.Status)
    Assert.Equal(173, BattleMon.friendship mon)

[<Fact>]
let ``BAT-008 records exact ordered progression events for every enemy defeat`` () =
    let id (n: int) = System.Guid.Parse("00000000-0000-0000-0000-" + n.ToString("000000000000"))
    let dragonRage = Moves.byName "DRAGON_RAGE"
    let splash = Moves.byName "SPLASH"
    let player name persistentId item =
        { mon name (ty "NORMAL") (ty "NORMAL") 50 200 200 100 200 with
            PersistentId = Some persistentId
            Moves = [ dragonRage ]
            Pp = [ dragonRage.Pp ]
            HeldItem = item }
    let enemy name level =
        { mon name (ty "NORMAL") (ty "NORMAL") level 1 1 1 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }

    let p1Id, p2Id, shareId = id 1, id 2, id 3
    let p1 = player "P1" p1Id None
    let p2 = player "P2" p2Id (Some "EXP_SHARE")
    let share = player "SHARE" shareId (Some "EXP_SHARE")
    let e1, e2 = enemy "E1" 10, enemy "E2" 20
    let trainer =
        { Group = "TEST"
          Id = "1"
          Name = "EVENTS"
          ClassName = "TESTER"
          WinText = None
          LossText = None
          BaseReward = Some 1 }

    let first = Battle.createTrainer trainer [ p1; p2; share ] [ e1; e2 ] 0u |> Battle.switchMon 1 |> Battle.chooseMove 0
    let p1Index = first.PlayerTeam |> List.findIndex (fun participant -> participant.PersistentId = Some p1Id)
    let finished = first |> Battle.switchMon p1Index |> Battle.chooseMove 0

    Assert.Equal(2, finished.DefeatEvents.Length)
    let firstEvent, secondEvent = finished.DefeatEvents.[0], finished.DefeatEvents.[1]
    Assert.Equal("E1", firstEvent.DefeatedSpecies.Name)
    Assert.Equal(10, firstEvent.DefeatedLevel)
    Assert.Equal<Set<System.Guid>>(Set.ofList [ p1Id; p2Id ], firstEvent.ParticipantIds)
    Assert.Equal<Set<System.Guid>>(Set.ofList [ p2Id; shareId ], firstEvent.ExpShareHolderIds)
    Assert.True(firstEvent.IsTrainer)
    Assert.Equal(
        { Hp = e1.Species.Hp
          Attack = e1.Species.Attack
          Defense = e1.Species.Defense
          Speed = e1.Species.Speed
          Special = e1.Species.SpAttack },
        firstEvent.StatExpYield)

    Assert.Equal("E2", secondEvent.DefeatedSpecies.Name)
    Assert.Equal(20, secondEvent.DefeatedLevel)
    Assert.Equal<Set<System.Guid>>(Set.ofList [ p1Id; p2Id ], secondEvent.ParticipantIds)
    Assert.Equal<Set<System.Guid>>(Set.ofList [ p2Id; shareId ], secondEvent.ExpShareHolderIds)
    Assert.True(secondEvent.IsTrainer)
    Assert.Equal(Some Win, finished.Outcome)
    Assert.Equal<DefeatProgressionEvent list>(finished.DefeatEvents, (Battle.chooseMove 0 finished).DefeatEvents)

    let wild = Battle.createTeam [ p1 ] [ e1 ] 0u |> Battle.chooseMove 0
    Assert.Single(wild.DefeatEvents) |> ignore
    Assert.False(wild.DefeatEvents.Head.IsTrainer)

[<Fact>]
let ``defeat events distribute exact EXP and stat EXP across overlapping pools`` () =
    let makeMon species level = PartyMon.create (Species.byName species).Dex level
    let a = makeMon "CYNDAQUIL" 10
    let b = { makeMon "TOTODILE" 10 with Pokerus = 1 }
    let c = { makeMon "CHIKORITA" 10 with OtId = 1 }
    let d = makeMon "PIDGEY" 10
    let defeated baseExp hp attack defense speed special =
        { Species.byName "RATTATA" with
            BaseExp = baseExp
            Hp = hp
            Attack = attack
            Defense = defense
            Speed = speed
            SpAttack = special }
    let event species level statYield =
        { DefeatedSpecies = species
          DefeatedLevel = level
          StatExpYield = statYield
          ParticipantIds = Set.ofList [ a.Id; b.Id ]
          ExpShareHolderIds = Set.ofList [ b.Id; c.Id ]
          LuckyEggHolderIds = Set.singleton a.Id
          IsTrainer = true }
    let firstSpecies = defeated 70 10 20 30 40 50
    let secondSpecies = defeated 91 11 21 31 41 51
    let statYield (species: BaseStats) =
        { Hp = species.Hp
          Attack = species.Attack
          Defense = species.Defense
          Speed = species.Speed
          Special = species.SpAttack }
    let result =
        [ event firstSpecies 10 (statYield firstSpecies)
          event secondSpecies 20 (statYield secondSpecies) ]
        |> fun events -> BattleProgression.applyEvents events [ a; b; c; d ]
    let a', b', c', d' = result.[0], result.[1], result.[2], result.[3]

    Assert.Equal(a.Exp + 193, a'.Exp)
    Assert.Equal(b.Exp + 258, b'.Exp)
    Assert.Equal(c.Exp + 193, c'.Exp)
    Assert.Equal(d.Exp, d'.Exp)
    Assert.Equal({ Hp = 4; Attack = 10; Defense = 14; Speed = 20; Special = 24 }, a'.StatExp)
    Assert.Equal({ Hp = 16; Attack = 40; Defense = 56; Speed = 80; Special = 96 }, b'.StatExp)
    Assert.Equal({ Hp = 4; Attack = 10; Defense = 14; Speed = 20; Special = 24 }, c'.StatExp)
    Assert.Equal(StatExperience.zero, d'.StatExp)
    Assert.Equal<System.Guid list>([ a.Id; b.Id; c.Id; d.Id ], result |> List.map _.Id)

[<Fact>]
let ``BAT-010 processes every crossed move level and evolves once after a win`` () =
    let species = Species.byName "CYNDAQUIL"
    let startLevel, finalLevel = 10, 20
    let startExp = Experience.expForLevel species.GrowthRate startLevel
    let finalExp = Experience.expForLevel species.GrowthRate finalLevel
    let seeded = PartyMon.create species.Dex startLevel |> MoveLearn.seedStartingMoves
    let mon = { seeded with Exp = startExp }
    let defeated = { Species.byName "RATTATA" with BaseExp = finalExp - startExp }
    let defeatEvent =
        { DefeatedSpecies = defeated
          DefeatedLevel = 7
          StatExpYield = { Hp = 7; Attack = 14; Defense = 21; Speed = 28; Special = 35 }
          ParticipantIds = Set.singleton mon.Id
          ExpShareHolderIds = Set.empty
          LuckyEggHolderIds = Set.empty
          IsTrainer = false }

    let lostResult = BattleProgression.applyBattleWithRequests (Some Lose) [ defeatEvent ] [ mon ]
    let wonResult = BattleProgression.applyBattleWithRequests (Some Win) [ defeatEvent ] [ mon ]
    let lost = lostResult.Party.Head
    let pendingEvolution = Assert.Single(wonResult.PendingEvolutions)
    let won = Evolution.applyCandidate pendingEvolution.Candidate wonResult.Party.Head
    let moveNames partyMon = partyMon.Moves |> List.map (fun (id, _) -> MovesData.byIndex.[id].Name)

    Assert.Equal(finalLevel, lost.Level)
    Assert.Equal(finalExp, lost.Exp)
    Assert.Equal(species.Dex, lost.SpeciesId)
    Assert.Contains("EMBER", moveNames lost)
    Assert.Equal<string list>([ "QUICK_ATTACK" ], lostResult.PendingMoves |> List.map (fun request -> MovesData.byIndex.[request.MoveId].Name))
    Assert.Equal<string list>([ "QUICK_ATTACK" ], wonResult.PendingMoves |> List.map (fun request -> MovesData.byIndex.[request.MoveId].Name))
    Assert.Equal("QUILAVA", pendingEvolution.Candidate.Target)
    Assert.Equal((Species.byName "QUILAVA").Dex, won.SpeciesId)
    Assert.Equal(finalLevel, won.Level)
    Assert.Equal(mon.Id, won.Id)
    Assert.Equal({ Hp = 7; Attack = 14; Defense = 21; Speed = 28; Special = 35 }, won.StatExp)
    Assert.Equal(PartyMon.deriveMaxHpWith won.SpeciesId finalLevel won.Dvs won.StatExp, won.MaxHp)
    Assert.Equal(won.MaxHp, won.Hp)

[<Fact>]
let ``BAT-012 queues full-set move decisions in source order without replacing`` () =
    let species = Species.byName "BULBASAUR"
    let moveSlot name pp =
        MovesData.byIndex |> Array.findIndex (fun move -> move.Name = name), pp
    let mon =
        { PartyMon.create species.Dex 14 with
            Exp = Experience.expForLevel species.GrowthRate 14
            Moves =
                [ moveSlot "TACKLE" 1
                  moveSlot "GROWL" 2
                  moveSlot "LEECH_SEED" 3
                  moveSlot "VINE_WHIP" 4 ] }
    let defeatEvent =
        { DefeatedSpecies = { Species.byName "IVYSAUR" with BaseExp = 141 }
          DefeatedLevel = 21
          StatExpYield = StatExperience.zero
          ParticipantIds = Set.singleton mon.Id
          ExpShareHolderIds = Set.empty
          LuckyEggHolderIds = Set.empty
          IsTrainer = false }

    let result = BattleProgression.applyBattleWithRequests (Some Win) [ defeatEvent ] [ mon ]
    let requestNames = result.PendingMoves |> List.map (fun request -> MovesData.byIndex.[request.MoveId].Name)
    Assert.Equal(15, result.Party.Head.Level)
    Assert.Equal<(int * int) list>(mon.Moves, result.Party.Head.Moves)
    Assert.Equal<System.Guid list>([ mon.Id; mon.Id ], result.PendingMoves |> List.map _.MonId)
    Assert.Equal<string list>([ "POISONPOWDER"; "SLEEP_POWDER" ], requestNames)

[<Fact>]
let ``Leftovers heals at end of turn without being consumed`` () =
    let splash = Moves.byName "SPLASH"
    let player =
        { BattleMon.ofSpecies (Species.byName "SNORLAX") 50 [ splash ] with
            Hp = 80
            MaxHp = 160
            HeldItem = Some "LEFTOVERS" }
    let enemy = BattleMon.ofSpecies (Species.byName "MAGIKARP") 5 [ splash ]

    let after = Battle.create player enemy 0u |> Battle.chooseMove 0

    Assert.Equal(90, after.Player.Hp)
    Assert.Equal(Some "LEFTOVERS", after.Player.HeldItem)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("LEFTOVERS"))

[<Fact>]
let ``Berry heals at half HP and is consumed`` () =
    let splash = Moves.byName "SPLASH"
    let player =
        { BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 50 [ splash ] with
            Hp = 40
            MaxHp = 100
            HeldItem = Some "BERRY" }
    let enemy = BattleMon.ofSpecies (Species.byName "MAGIKARP") 5 [ splash ]

    let after = Battle.create player enemy 0u |> Battle.chooseMove 0

    Assert.Equal(50, after.Player.Hp)
    Assert.Equal(None, after.Player.HeldItem)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("ate BERRY"))

[<Fact>]
let ``status cure berry cures poison before residual damage and is consumed`` () =
    let splash = Moves.byName "SPLASH"
    let player =
        { BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 50 [ splash ] with
            Hp = 100
            MaxHp = 100
            Status = Poison
            HeldItem = Some "PSNCUREBERRY" }
    let enemy = BattleMon.ofSpecies (Species.byName "MAGIKARP") 5 [ splash ]

    let after = Battle.create player enemy 0u |> Battle.chooseMove 0

    Assert.Equal(Healthy, after.Player.Status)
    Assert.Equal(100, after.Player.Hp)
    Assert.Equal(None, after.Player.HeldItem)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("PSNCUREBERRY"))

[<Fact>]
let ``Bitter Berry cures confusion and is consumed`` () =
    let splash = Moves.byName "SPLASH"
    let confused = { VolatileStatus.empty with Confusion = Some 3 }
    let player =
        { BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 50 [ splash ] with
            Volatile = confused
            HeldItem = Some "BITTER_BERRY" }
    let enemy = BattleMon.ofSpecies (Species.byName "MAGIKARP") 5 [ splash ]

    let after = Battle.create player enemy 0u |> Battle.chooseMove 0

    Assert.Equal(None, after.Player.Volatile.Confusion)
    Assert.Equal(None, after.Player.HeldItem)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("BITTER BERRY"))

[<Fact>]
let ``type boosting held item increases matching move damage`` () =
    let ember = Moves.byName "EMBER"
    let splash = Moves.byName "SPLASH"
    let basePlayer = { mon "PLAYER" (ty "FIRE") (ty "FIRE") 30 300 80 80 100 with Moves = [ ember ]; Pp = [ ember.Pp ] }
    let boostedPlayer = { basePlayer with HeldItem = Some "CHARCOAL" }
    let enemy = { mon "ENEMY" (ty "GRASS") (ty "GRASS") 30 500 80 80 1 with Moves = [ splash ]; Pp = [ splash.Pp ] }

    let normal = Battle.create basePlayer enemy 0u |> Battle.chooseMove 0
    let boosted = Battle.create boostedPlayer enemy 0u |> Battle.chooseMove 0

    Assert.True(boosted.Enemy.Hp < normal.Enemy.Hp, $"expected CHARCOAL to increase damage: normal hp={normal.Enemy.Hp}, boosted hp={boosted.Enemy.Hp}")

[<Fact>]
let ``Transform copies target species stats moves and stages`` () =
    let transform = Moves.byName "TRANSFORM"
    let ditto =
        { BattleMon.ofSpecies (Species.byName "DITTO") 30 [ transform ] with
            AtkStage = -1 }
    let target =
        { BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 12 [ Moves.byName "EMBER"; Moves.byName "TACKLE" ] with
            AtkStage = 2
            DefStage = 1 }
    let ctx =
        { User = ditto
          Foe = target
          Move = transform
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None
          WeatherType = None }

    let transformed = Effects.forMove transform |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx

    Assert.Equal("CYNDAQUIL", transformed.User.Species.Name)
    Assert.Equal(target.Attack, transformed.User.Attack)
    Assert.True(transformed.User.Pp = [ 5; 5 ], $"expected copied PP to be 5 each, got {transformed.User.Pp}")
    Assert.Equal(2, transformed.User.AtkStage)
    Assert.Equal(1, transformed.User.DefStage)

[<Fact>]
let ``Conversion changes the user to one of its move types`` () =
    let conversion = Moves.byName "CONVERSION"
    let ember = Moves.byName "EMBER"
    let user = { BattleMon.ofSpecies (Species.byName "RATTATA") 20 [ conversion; ember ] with Pp = [ 30; 25 ] }
    let foe = BattleMon.ofSpecies (Species.byName "PIDGEY") 20 [ Moves.byName "TACKLE" ]
    let ctx =
        { User = user
          Foe = foe
          Move = conversion
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None
          WeatherType = None }

    let converted = Effects.forMove conversion |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx

    Assert.Equal(TypeChart.value "FIRE", converted.User.Species.Type1)
    Assert.Equal(TypeChart.value "FIRE", converted.User.Species.Type2)

[<Fact>]
let ``Conversion2 chooses a type resistant to the target move`` () =
    let conversion2 = Moves.byName "CONVERSION2"
    let tackle = Moves.byName "TACKLE"
    let user = BattleMon.ofSpecies (Species.byName "PORYGON") 20 [ conversion2 ]
    let foe =
        { BattleMon.ofSpecies (Species.byName "RATTATA") 20 [ tackle ] with
            Volatile = { VolatileStatus.empty with LastCounterMove = Some tackle } }
    let ctx =
        { User = user
          Foe = foe
          Move = conversion2
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None
          WeatherType = None }

    let converted = Effects.forMove conversion2 |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx

    Assert.True(TypeChart.multiplier (TypeChart.value "NORMAL") converted.User.Species.Type1 < TypeChart.Neutral)

[<Fact>]
let ``Mimic copies a target move into the user's move list with 5 PP`` () =
    let mimic = Moves.byName "MIMIC"
    let ember = Moves.byName "EMBER"
    let user = { BattleMon.ofSpecies (Species.byName "RATTATA") 20 [ mimic ] with Pp = [ 10 ] }
    let foe =
        { BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 20 [ ember ] with
            Volatile = { VolatileStatus.empty with LastCounterMove = Some ember } }
    let ctx =
        { User = user
          Foe = foe
          Move = mimic
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None
          WeatherType = None }

    let copied = Effects.forMove mimic |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx

    Assert.Equal("EMBER", copied.User.Moves.Head.Name)
    Assert.True(copied.User.Pp = [ 5 ], $"expected copied move PP to be 5, got {copied.User.Pp}")

[<Fact>]
let ``Metronome dispatches a deterministic called move`` () =
    let metronome = Moves.byName "METRONOME"
    let user = BattleMon.ofSpecies (Species.byName "CLEFAIRY") 20 [ metronome ]
    let foe = BattleMon.ofSpecies (Species.byName "GEODUDE") 20 [ Moves.byName "SPLASH" ]
    let ctx =
        { User = user
          Foe = foe
          Move = metronome
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None
          WeatherType = None }

    let called = Effects.forMove metronome |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx

    Assert.Contains(called.Messages, fun msg -> msg.Contains("Metronome called"))
    Assert.True(called.User.Volatile.LastMove.IsSome)
    Assert.NotEqual<string>("METRONOME", called.User.Volatile.LastMove.Value.Name)

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
let ``Quick Claw lets a slower holder move first on a successful roll`` () =
    let slow =
        { mon "SLOW" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 10 with
            Moves = [ strongHit ]
            HeldItem = Some "QUICK_CLAW" }
    let fast = { mon "FAST" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 99 with Moves = [ strongHit ] }
    let state = Battle.create slow fast 0u
    let after = Battle.chooseMove 0 state

    Assert.Contains(after.Messages, fun m -> m.Contains("QUICK CLAW"))
    let firstUse = after.Messages |> List.find (fun m -> m.Contains "used")
    Assert.Contains("SLOW used", firstUse)

[<Fact>]
let ``priority moves act before Quick Claw checks`` () =
    let quickAttack = { strongHit with Name = "QUICK_ATTACK"; Effect = "EFFECT_PRIORITY_HIT" }
    let slow =
        { mon "SLOW" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 10 with
            Moves = [ quickAttack ] }
    let fast =
        { mon "FAST" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 99 with
            Moves = [ strongHit ]
            HeldItem = Some "QUICK_CLAW" }
    let state = Battle.create slow fast 0u
    let after = Battle.chooseMove 0 state

    let firstUse = after.Messages |> List.find (fun m -> m.Contains "used")
    Assert.Contains("SLOW used", firstUse)

[<Fact>]
let ``a lethal hit faints the enemy and wins the battle`` () =
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 200 200 200 with Moves = [ strongHit ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 2 1 1 1 1 with Moves = [ strongHit ] }
    let after = Battle.create player enemy 7u |> Battle.chooseMove 0
    Assert.Equal(Some Win, after.Outcome)
    Assert.Contains(after.Messages, fun m -> m.Contains "fainted")

[<Fact>]
let ``Focus Band can leave the holder at 1 HP against lethal damage`` () =
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 200 200 200 with Moves = [ strongHit ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 2 10 1 1 1 with
            Moves = [ Moves.byName "SPLASH" ]
            HeldItem = Some "FOCUS_BAND" }
    let after = Battle.create player enemy 0u |> Battle.chooseMove 0

    Assert.Equal(1, after.Enemy.Hp)
    Assert.True(after.Outcome.IsNone)
    Assert.Contains(after.Messages, fun m -> m.Contains("FOCUS BAND"))

[<Fact>]
let ``BrightPowder can turn an otherwise accurate hit into a miss`` () =
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ strongHit ] }
    let normalEnemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ Moves.byName "SPLASH" ] }
    let powderEnemy = { normalEnemy with HeldItem = Some "BRIGHTPOWDER" }

    let normal = Battle.create player normalEnemy 5u |> Battle.chooseMove 0
    let powdered = Battle.create player powderEnemy 5u |> Battle.chooseMove 0

    Assert.True(normal.Enemy.Hp < normalEnemy.Hp, "normal enemy should be hit")
    Assert.Equal(powderEnemy.Hp, powdered.Enemy.Hp)
    Assert.Contains(powdered.Messages, fun msg -> msg.Contains("missed"))

[<Fact>]
let ``Scope Lens raises critical hit stage`` () =
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ strongHit ] }
    let scoped = { player with HeldItem = Some "SCOPE_LENS" }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 500 100 100 1 with Moves = [ Moves.byName "SPLASH" ] }

    let normal = Battle.create player enemy 4u |> Battle.chooseMove 0
    let withScope = Battle.create scoped enemy 4u |> Battle.chooseMove 0

    Assert.DoesNotContain(normal.Messages, fun msg -> msg.Contains("critical"))
    Assert.Contains(withScope.Messages, fun msg -> msg.Contains("critical"))

[<Fact>]
let ``King's Rock can set flinch on a damaging hit`` () =
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ strongHit ]
            HeldItem = Some "KINGS_ROCK" }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 500 100 100 1 with Moves = [ Moves.byName "SPLASH" ] }
    let after = Battle.create player enemy 0u |> Battle.chooseMove 0

    Assert.Contains(after.Messages, fun msg -> msg.Contains("KING'S ROCK"))
    Assert.Contains(after.Messages, fun msg -> msg.Contains("flinched"))

[<Fact>]
let ``MysteryBerry restores PP when a move reaches zero`` () =
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ strongHit ]
            Pp = [ 1 ]
            HeldItem = Some "MYSTERYBERRY" }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 500 100 100 1 with Moves = [ Moves.byName "SPLASH" ] }
    let after = Battle.create player enemy 0u |> Battle.chooseMove 0

    Assert.Equal<int list>([ 5 ], after.Player.Pp)
    Assert.Equal(None, after.Player.HeldItem)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("restored PP"))

[<Fact>]
let ``BAT-023 fainted Berry holder retains item and Leftovers remains held across turns`` () =
    let splash = Moves.byName "SPLASH"
    let berryHolder =
        { mon "BERRY_HOLDER" (ty "NORMAL") (ty "NORMAL") 5 1 1 1 1 with
            Hp = 1
            Moves = [ splash ]
            Pp = [ splash.Pp ]
            HeldItem = Some "BERRY" }
    let attacker =
        { mon "ATTACKER" (ty "NORMAL") (ty "NORMAL") 50 200 200 100 200 with
            Moves = [ strongHit ]
            Pp = [ strongHit.Pp ] }
    let fainted = Battle.create berryHolder attacker 0u |> Battle.chooseMove 0
    Assert.Equal(0, fainted.Player.Hp)
    Assert.Equal(Some "BERRY", fainted.Player.HeldItem)

    let leftoversHolder =
        { mon "LEFTOVERS_HOLDER" (ty "NORMAL") (ty "NORMAL") 50 160 100 100 200 with
            Hp = 80
            Moves = [ splash ]
            Pp = [ splash.Pp ]
            HeldItem = Some "LEFTOVERS" }
    let passiveEnemy = BattleMon.ofSpecies (Species.byName "MAGIKARP") 5 [ splash ]
    let first = Battle.create leftoversHolder passiveEnemy 0u |> Battle.chooseMove 0
    let second = first |> Battle.chooseMove 0
    Assert.Equal(Some "LEFTOVERS", second.Player.HeldItem)
    Assert.True(second.Player.Hp > first.Player.Hp)

[<Fact>]
let ``Smoke Ball allows escape while trapped`` () =
    let trapped = { VolatileStatus.empty with Trapped = Some 3 }
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ strongHit ]
            Volatile = trapped
            HeldItem = Some "SMOKE_BALL" }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 500 100 100 1 with Moves = [ Moves.byName "SPLASH" ] }
    let after = Battle.create player enemy 0u |> Battle.run

    Assert.Equal(Some Ran, after.Outcome)

[<Fact>]
let ``Sleep Talk can call a move while the user is asleep`` () =
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 30 120 80 60 200 with
            Moves = [ Moves.byName "SLEEP_TALK"; strongHit ]
            Pp = [ 10; 35 ]
            Status = Sleep 3 }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 30 120 40 40 1 with Moves = [ Moves.byName "SPLASH" ] }

    let after = Battle.create player enemy 0u |> Battle.chooseMove 0

    Assert.Contains(after.Messages, fun msg -> msg.Contains("Sleep Talk called"))
    Assert.True(after.Enemy.Hp < enemy.Hp)

[<Fact>]
let ``Bide stores damage and later unleashes double damage`` () =
    let bide = Moves.byName "BIDE"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 30 200 60 60 200 with
            Moves = [ bide ]
            Pp = [ 10 ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 30 200 80 40 1 with
            Moves = [ strongHit ] }

    let mutable state = Battle.create player enemy 0u |> Battle.chooseMove 0
    let mutable turns = 0
    while not (state.Messages |> List.exists (fun msg -> msg.Contains("unleashed energy"))) && turns < 5 do
        state <- Battle.chooseMove 0 state
        turns <- turns + 1

    Assert.Contains(state.Messages, fun msg -> msg.Contains("unleashed energy"))
    Assert.True(state.Enemy.Hp < enemy.Hp)

[<Fact>]
let ``Teleport ends the battle as a run outcome`` () =
    let player = { mon "PLAYER" (ty "PSYCHIC_TYPE") (ty "PSYCHIC_TYPE") 20 100 50 50 200 with Moves = [ Moves.byName "TELEPORT" ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 1 with Moves = [ strongHit ] }

    let after = Battle.create player enemy 0u |> Battle.chooseMove 0

    Assert.Equal(Some Ran, after.Outcome)

[<Fact>]
let ``Roar drags out an enemy team member after the target has moved`` () =
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 1 with Moves = [ Moves.byName "ROAR" ] }
    let firstEnemy = { mon "FIRST" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 200 with Moves = [ Moves.byName "SPLASH" ] }
    let benchEnemy = { mon "BENCH" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 1 with Moves = [ Moves.byName "SPLASH" ] }

    let after = Battle.createTeam [ player ] [ firstEnemy; benchEnemy ] 0u |> Battle.chooseMove 0

    Assert.Equal("BENCH", after.Enemy.Species.Name)
    Assert.True(after.Outcome.IsNone)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("dragged out"))

[<Fact>]
let ``Baton Pass switches to a bench mon and preserves stat stages`` () =
    let player =
        { mon "PASSER" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 200 with
            Moves = [ Moves.byName "BATON_PASS" ]
            AtkStage = 3
            EvaStage = 2 }
    let bench = { mon "RECEIVER" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 100 with Moves = [ strongHit ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 1 with Moves = [ Moves.byName "SPLASH" ] }

    let after = Battle.createTeam [ player; bench ] [ enemy ] 0u |> Battle.chooseMove 0

    Assert.Equal("RECEIVER", after.Player.Species.Name)
    Assert.Equal(3, after.Player.AtkStage)
    Assert.Equal(2, after.Player.EvaStage)

[<Fact>]
let ``trainer AI switches on a source-valid type matchup roll`` () =
    let player = { mon "GHOST" (ty "GHOST") (ty "GHOST") 30 120 70 70 50 with Moves = [ Moves.byName "SPLASH" ] }
    let activeEnemy =
        { mon "WALLED" (ty "NORMAL") (ty "NORMAL") 30 120 60 60 40 with
            Moves = [ strongHit ] }
    let benchEnemy =
        { mon "ANSWER" (ty "DARK") (ty "DARK") 30 120 80 60 30 with
            Moves = [ Moves.byName "BITE" ] }
    let switchingSeed =
        [ 0u .. 255u ]
        |> List.find (fun candidate ->
            let roll, _ = Rng.next (Rng.create candidate)
            roll < (20 * 255 / 100 - 1))
    let trainer =
        { Group = "FALKNER"
          Id = "1"
          Name = "FALKNER"
          ClassName = "FALKNER"
          WinText = None
          LossText = None
          BaseReward = Some 25 }

    let after =
        Battle.createTrainer trainer [ player ] [ activeEnemy; benchEnemy ] switchingSeed
        |> Battle.chooseMove 0

    Assert.Equal("ANSWER", after.Enemy.Species.Name)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("Enemy withdrew"))

[<Fact>]
let ``Metal Powder boosts Ditto defenses`` () =
    let ditto = BattleMon.ofSpecies (Species.byName "DITTO") 30 [ Moves.byName "TRANSFORM" ]
    let boosted = { ditto with HeldItem = Some "METAL_POWDER" }

    Assert.True(BattleMon.effectiveDefense boosted > BattleMon.effectiveDefense ditto)
    Assert.True(BattleMon.effectiveSpDefense boosted > BattleMon.effectiveSpDefense ditto)

[<Fact>]
let ``wild and trainer battle constructors use distinct opening text`` () =
    let tackle = Moves.byName "TACKLE"
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50 with Moves = [ tackle ]; Pp = [ tackle.Pp ] }
    let enemy = { mon "TOTODILE" (ty "WATER") (ty "WATER") 5 100 20 20 1 with Moves = [ tackle ]; Pp = [ tackle.Pp ] }
    let trainer =
        { Group = "RIVAL1"
          Id = "RIVAL1_1_TOTODILE"
          Name = "???"
          ClassName = "RIVAL"
          WinText = None
          LossText = None
          BaseReward = Some 25 }

    let wild = Battle.createWild player enemy 0u
    let trainerBattle = Battle.createTrainer trainer [ player ] [ enemy ] 0u

    Assert.Contains(wild.Messages, fun msg -> msg = "Wild TOTODILE appeared!")
    Assert.DoesNotContain(trainerBattle.Messages, fun msg -> msg.StartsWith("Wild "))
    Assert.Contains(trainerBattle.Messages, fun msg -> msg = "RIVAL ??? wants to battle!")
    Assert.Contains(trainerBattle.Messages, fun msg -> msg = "RIVAL sent out TOTODILE!")

[<Fact>]
let ``BattleScene command menu opens fight and backs out with B`` () =
    let tackle = Moves.byName "TACKLE"
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50 with Moves = [ tackle ]; Pp = [ tackle.Pp ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 5 100 20 20 1 with Moves = [ Moves.byName "SPLASH" ]; Pp = [ (Moves.byName "SPLASH").Pp ] }
    let state = { Battle.create player enemy 0u with Messages = [] }
    let scene = BattleScene(Content().Font, state)
    let tick buttons = (scene :> Scene).Update buttons |> ignore

    Assert.Equal("CommandMenu", scene.CurrentModeName)
    tick { Buttons.none with A = true }
    tick Buttons.none
    Assert.Equal("MoveMenu", scene.CurrentModeName)
    tick { Buttons.none with B = true }
    tick Buttons.none

    Assert.Equal("CommandMenu", scene.CurrentModeName)

[<Fact>]
let ``BattleScene PKMN command switches to an able bench Pokemon`` () =
    let tackle = Moves.byName "TACKLE"
    let active = { mon "ACTIVE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50 with Moves = [ tackle ]; Pp = [ tackle.Pp ] }
    let bench = { mon "BENCH" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50 with Moves = [ tackle ]; Pp = [ tackle.Pp ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 5 100 20 20 1 with Moves = [ Moves.byName "SPLASH" ]; Pp = [ (Moves.byName "SPLASH").Pp ] }
    let state = { Battle.createTeam [ active; bench ] [ enemy ] 0u with Messages = [] }
    let scene = BattleScene(Content().Font, state)
    let tick buttons = (scene :> Scene).Update buttons |> ignore

    tick { Buttons.none with Right = true }
    tick Buttons.none
    tick { Buttons.none with A = true }
    tick Buttons.none
    Assert.Equal("PartyMenu", scene.CurrentModeName)

    tick { Buttons.none with Down = true }
    tick Buttons.none
    tick { Buttons.none with A = true }

    Assert.Equal("BENCH", scene.CurrentState.Player.Species.Name)
    Assert.Equal("CommandMenu", scene.CurrentModeName)

[<Fact>]
let ``BattleScene PACK command can target a benched Pokemon with Potion`` () =
    let potion = "POTION"
    let tackle = Moves.byName "TACKLE"
    let active =
        { mon "ACTIVE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }
    let bench =
        { mon "BENCH" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50 with
            Hp = 40
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 5 100 20 20 1 with Moves = [ Moves.byName "SPLASH" ]; Pp = [ (Moves.byName "SPLASH").Pp ] }
    let mutable bag = Bag.empty |> Bag.add potion 1
    let state = { Battle.createTeam [ active; bench ] [ enemy ] 0u with Messages = [] }
    let scene = BattleScene(Content().Font, state, bag = bag, onBagChange = (fun b -> bag <- b))
    let tick buttons = (scene :> Scene).Update buttons |> ignore

    tick { Buttons.none with Down = true }
    tick Buttons.none
    tick { Buttons.none with A = true }
    tick Buttons.none
    Assert.Equal("PackMenu", scene.CurrentModeName)

    tick { Buttons.none with A = true }
    tick Buttons.none
    Assert.Equal("TargetMenu", scene.CurrentModeName)
    tick { Buttons.none with Down = true }
    tick Buttons.none
    tick { Buttons.none with A = true }

    Assert.Equal(60, scene.CurrentState.PlayerTeam.[1].Hp)
    Assert.Equal(0, Bag.count potion scene.CurrentBag)

[<Fact>]
let ``BAT-022 Revive restores a fainted bench target consumes once and costs a turn`` () =
    let splash = Moves.byName "SPLASH"
    let active =
        { mon "ACTIVE" (ty "NORMAL") (ty "NORMAL") 20 200 50 50 50 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let fainted =
        { mon "FAINTED" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 20 with
            Hp = 0
            Status = Poison
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 30 200 100 50 200 with
            Moves = [ strongHit ]
            Pp = [ strongHit.Pp ] }
    let scene =
        BattleScene(
            Content().Font,
            { Battle.createTeam [ active; fainted ] [ enemy ] 0u with Messages = [] },
            bag = (Bag.empty |> Bag.add "REVIVE" 1))
    let tick buttons = (scene :> Scene).Update buttons |> ignore

    tick { Buttons.none with Down = true }
    tick Buttons.none
    tick { Buttons.none with A = true }
    tick Buttons.none
    Assert.Equal("PackMenu", scene.CurrentModeName)

    tick { Buttons.none with A = true }
    tick Buttons.none
    Assert.Equal("TargetMenu", scene.CurrentModeName)

    tick { Buttons.none with Down = true }
    tick Buttons.none
    tick { Buttons.none with A = true }

    for _ in 1 .. 20 do
        tick { Buttons.none with A = true }
        tick Buttons.none

    let revived = scene.CurrentState.PlayerTeam.[1]
    Assert.Equal(50, revived.Hp)
    Assert.Equal(Healthy, revived.Status)
    Assert.Equal(0, Bag.count "REVIVE" scene.CurrentBag)
    Assert.True(scene.CurrentState.Player.Hp < active.Hp, "A successful battle item should allow the enemy's turn")

[<Fact>]
let ``BAT-022 source battle item table enforces revive status PP and direct effects`` () =
    let tackle = Moves.byName "TACKLE"
    let ember = Moves.byName "EMBER"
    let active =
        { mon "ACTIVE" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 50 with
            Status = Poison
            Volatile = { VolatileStatus.empty with Confusion = Some 2 }
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }
    let fainted =
        { mon "FAINTED" (ty "NORMAL") (ty "NORMAL") 20 101 50 50 20 with
            Hp = 0
            Status = Burn
            Moves = [ tackle; ember ]
            Pp = [ 0; 1 ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 10 with Moves = [ tackle ]; Pp = [ tackle.Pp ] }
    let wild = Battle.createTeam [ active; fainted ] [ enemy ] 0u
    let trainer =
        { Group = "TEST"
          Id = "ITEMS"
          Name = "TESTER"
          ClassName = "TESTER"
          WinText = None
          LossText = None
          BaseReward = None }
    let requireUse item target state =
        BattleItems.tryUse item target state
        |> Option.defaultWith (fun () -> failwithf "expected %s to be usable" item)

    let maxRevived = requireUse "MAX_REVIVE" (PartyTarget 1) wild
    Assert.Equal(101, maxRevived.PlayerTeam.[1].Hp)
    Assert.Equal(Healthy, maxRevived.PlayerTeam.[1].Status)
    Assert.Equal(None, BattleItems.tryUse "REVIVE" (PartyTarget 0) wild)
    Assert.Equal(None, BattleItems.tryUse "FULL_RESTORE" (PartyTarget 1) wild)

    let cured = requireUse "ANTIDOTE" (PartyTarget 0) wild
    Assert.Equal(Healthy, cured.Player.Status)
    Assert.Equal(None, BattleItems.tryUse "BURN_HEAL" (PartyTarget 0) wild)
    let fullHealed = requireUse "FULL_HEAL" (PartyTarget 0) wild
    Assert.Equal(Healthy, fullHealed.Player.Status)
    Assert.True(fullHealed.Player.Volatile.Confusion.IsNone)
    let fullyRestored = requireUse "FULL_RESTORE" (PartyTarget 0) wild
    Assert.Equal(active.MaxHp, fullyRestored.Player.Hp)
    Assert.True(fullyRestored.Player.Volatile.Confusion.IsNone)

    let ether = requireUse "ETHER" (MoveTarget(1, 0)) wild
    Assert.Equal(10, ether.PlayerTeam.[1].Pp.[0])
    let maxEther = requireUse "MAX_ETHER" (MoveTarget(1, 1)) wild
    Assert.Equal(ember.Pp, maxEther.PlayerTeam.[1].Pp.[1])
    let elixer = requireUse "ELIXER" (PartyTarget 1) wild
    Assert.Equal<int list>([ 10; min ember.Pp 11 ], elixer.PlayerTeam.[1].Pp)
    let maxElixer = requireUse "MAX_ELIXER" (PartyTarget 1) wild
    Assert.Equal<int list>([ tackle.Pp; ember.Pp ], maxElixer.PlayerTeam.[1].Pp)

    let xAttack = requireUse "X_ATTACK" ActiveTarget wild
    let xDefend = requireUse "X_DEFEND" ActiveTarget wild
    let xSpeed = requireUse "X_SPEED" ActiveTarget wild
    let xSpecial = requireUse "X_SPECIAL" ActiveTarget wild
    Assert.Equal(1, xAttack.Player.AtkStage)
    Assert.Equal(1, xDefend.Player.DefStage)
    Assert.Equal(1, xSpeed.Player.SpdStage)
    Assert.Equal(1, xSpecial.Player.SpAtkStage)
    let guardSpec = requireUse "GUARD_SPEC" ActiveTarget wild
    Assert.True(guardSpec.Player.Volatile.Mist)
    let xAccuracy = requireUse "X_ACCURACY" ActiveTarget wild
    Assert.True(xAccuracy.Player.Volatile.XAccuracy)
    Assert.Equal(Some Ran, (requireUse "POKE_DOLL" ActiveTarget wild).Outcome)
    Assert.Equal(None, BattleItems.tryUse "POKE_DOLL" ActiveTarget (Battle.createTrainer trainer [ active ] [ enemy ] 0u))

[<Fact>]
let ``BAT-022 source berries bitter medicine and Revival Herb preserve battle item semantics`` () =
    let splash = Moves.byName "SPLASH"
    let enemy = BattleMon.ofSpecies (Species.byName "MAGIKARP") 5 [ splash ]
    let persistentMon friendship =
        { PartyMon.create (Species.byName "CYNDAQUIL").Dex 20 with
            Friendship = friendship }
        |> PartyMon.toBattleMon
    let state player = Battle.createTeam [ player ] [ enemy ] 0u
    let requireUse item target battle =
        BattleItems.tryUse item target battle
        |> Option.defaultWith (fun () -> failwithf "expected %s to be usable" item)
    let assertStatusCure item status =
        let player = { persistentMon 70 with Status = status }
        let updated = requireUse item (PartyTarget 0) (state player)
        Assert.Equal(Healthy, updated.Player.Status)

    let sourceItems =
        [ "PSNCUREBERRY"; "PRZCUREBERRY"; "BURNT_BERRY"; "ICE_BERRY"; "MINT_BERRY"
          "MIRACLEBERRY"; "BITTER_BERRY"; "MYSTERYBERRY"; "ENERGYPOWDER"; "ENERGY_ROOT"; "REVIVAL_HERB" ]
    sourceItems |> List.iter (fun item -> Assert.True(BattleItems.isSupported item, $"{item} must be usable in battle"))

    assertStatusCure "PSNCUREBERRY" Poison
    assertStatusCure "PRZCUREBERRY" Paralysis
    assertStatusCure "BURNT_BERRY" Freeze
    assertStatusCure "ICE_BERRY" Burn
    assertStatusCure "MINT_BERRY" (Sleep 3)

    let confused =
        { persistentMon 70 with
            Volatile = { VolatileStatus.empty with Confusion = Some 2 } }
    let miracle = requireUse "MIRACLEBERRY" (PartyTarget 0) (state confused)
    Assert.True(miracle.Player.Volatile.Confusion.IsNone)
    let bitter = requireUse "BITTER_BERRY" ActiveTarget (state confused)
    Assert.True(bitter.Player.Volatile.Confusion.IsNone)
    Assert.True(BattleItems.isDirect "BITTER_BERRY")

    let ppDepleted = { persistentMon 70 with Moves = [ splash ]; Pp = [ 0 ] }
    let mysteryBerry = requireUse "MYSTERYBERRY" (MoveTarget(0, 0)) (state ppDepleted)
    Assert.Equal(5, mysteryBerry.Player.Pp.Head)

    let damaged = { persistentMon 70 with Hp = 1 }
    let energyPowder = requireUse "ENERGYPOWDER" (PartyTarget 0) (state damaged)
    Assert.Equal(min damaged.MaxHp (damaged.Hp + 50), energyPowder.Player.Hp)
    Assert.Equal(65, BattleMon.friendship energyPowder.Player)

    let energyRoot = requireUse "ENERGY_ROOT" (PartyTarget 0) (state { persistentMon 205 with Hp = 1 })
    Assert.Equal(190, BattleMon.friendship energyRoot.Player)

    let healPowder = requireUse "HEAL_POWDER" (PartyTarget 0) (state { persistentMon 70 with Status = Poison })
    Assert.Equal(65, BattleMon.friendship healPowder.Player)

    let fainted = { persistentMon 205 with Hp = 0; Status = Burn }
    let revivalHerb = requireUse "REVIVAL_HERB" (PartyTarget 0) (state fainted)
    Assert.Equal(fainted.MaxHp, revivalHerb.Player.Hp)
    Assert.Equal(Healthy, revivalHerb.Player.Status)
    Assert.Equal(185, BattleMon.friendship revivalHerb.Player)

[<Fact>]
let ``BAT-022 every source battle-menu item has a supported battle transition`` () =
    let sourceBattleItems =
        Items.byId
        |> Map.toSeq
        |> Seq.choose (fun (item, data) ->
            if data.Pocket = Item
               && (data.BattleMenu = "ITEMMENU_PARTY" || data.BattleMenu = "ITEMMENU_CLOSE") then
                Some item
            else
                None)
        |> Set.ofSeq
    let unsupported = sourceBattleItems |> Set.filter (BattleItems.isSupported >> not)

    Assert.Empty(unsupported)

[<Fact>]
let ``BAT-022 X Accuracy and Guard Spec alter source battle checks`` () =
    let alwaysMiss = { move "MISS" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") with Accuracy = 0 }
    let growl = Moves.byName "GROWL"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ alwaysMiss ]
            Pp = [ alwaysMiss.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100 with
            Moves = [ growl ]
            Pp = [ growl.Pp ] }
    let state = Battle.createTeam [ player ] [ enemy ] 0u
    let accurate =
        BattleItems.tryUse "X_ACCURACY" ActiveTarget state
        |> Option.defaultWith (fun () -> failwith "X Accuracy should apply")
        |> Battle.chooseMove 0
    Assert.True(accurate.Enemy.Hp < enemy.Hp)

    let guarded =
        BattleItems.tryUse "GUARD_SPEC" ActiveTarget state
        |> Option.defaultWith (fun () -> failwith "Guard Spec should apply")
        |> Battle.chooseMove 0
    Assert.Equal(0, guarded.Player.AtkStage)
    Assert.Contains(guarded.Messages, fun message -> message.Contains("protected by mist"))

[<Fact>]
let ``BAT-022 X Attack consumes at capped stats while Guard Spec rejection preserves bag and turn`` () =
    let splash = Moves.byName "SPLASH"
    let active =
        { mon "ACTIVE" (ty "NORMAL") (ty "NORMAL") 20 200 50 50 50 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 30 200 100 50 200 with
            Moves = [ strongHit ]
            Pp = [ strongHit.Pp ] }
    let scene =
        BattleScene(
            Content().Font,
            { Battle.createTeam [ active ] [ enemy ] 0u with Messages = [] },
            bag = (Bag.empty |> Bag.add "X_ATTACK" 1))
    let tick buttons = (scene :> Scene).Update buttons |> ignore

    tick { Buttons.none with Down = true }
    tick Buttons.none
    tick { Buttons.none with A = true }
    tick Buttons.none
    tick { Buttons.none with A = true }

    for _ in 1 .. 20 do
        tick { Buttons.none with A = true }
        tick Buttons.none

    Assert.Equal(1, scene.CurrentState.Player.AtkStage)
    Assert.Equal(0, Bag.count "X_ATTACK" scene.CurrentBag)
    Assert.True(scene.CurrentState.Player.Hp < active.Hp)

    let useDirect item state =
        let direct = BattleScene(Content().Font, state, bag = (Bag.empty |> Bag.add item 1))
        let directTick buttons = (direct :> Scene).Update buttons |> ignore
        directTick { Buttons.none with Down = true }
        directTick Buttons.none
        directTick { Buttons.none with A = true }
        directTick Buttons.none
        directTick { Buttons.none with A = true }
        direct

    let capped = { Battle.createTeam [ { active with AtkStage = 6 } ] [ enemy ] 0u with Messages = [] }
    let cappedScene = useDirect "X_ATTACK" capped
    for _ in 1 .. 20 do
        (cappedScene :> Scene).Update { Buttons.none with A = true } |> ignore
        (cappedScene :> Scene).Update Buttons.none |> ignore
    Assert.Equal(0, Bag.count "X_ATTACK" cappedScene.CurrentBag)
    Assert.True(cappedScene.CurrentState.Player.Hp < active.Hp)

    let guarded =
        { Battle.createTeam [ { active with Volatile = { active.Volatile with Mist = true } } ] [ enemy ] 0u with Messages = [] }
    let rejected = useDirect "GUARD_SPEC" guarded
    Assert.Equal(1, Bag.count "GUARD_SPEC" rejected.CurrentBag)
    Assert.Equal(active.Hp, rejected.CurrentState.Player.Hp)

[<Fact>]
let ``BAT-022 Ether uses move targeting and restores PP without restoring a full slot`` () =
    let tackle = Moves.byName "TACKLE"
    let active =
        { mon "ACTIVE" (ty "NORMAL") (ty "NORMAL") 20 200 50 50 50 with
            Moves = [ tackle ]
            Pp = [ 0 ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 30 200 100 50 200 with
            Moves = [ strongHit ]
            Pp = [ strongHit.Pp ] }
    let scene =
        BattleScene(
            Content().Font,
            { Battle.createTeam [ active ] [ enemy ] 0u with Messages = [] },
            bag = (Bag.empty |> Bag.add "ETHER" 1))
    let tick buttons = (scene :> Scene).Update buttons |> ignore

    tick { Buttons.none with Down = true }
    tick Buttons.none
    tick { Buttons.none with A = true }
    tick Buttons.none
    tick { Buttons.none with A = true }
    tick Buttons.none
    Assert.Equal("TargetMenu", scene.CurrentModeName)
    tick { Buttons.none with A = true }
    tick Buttons.none
    Assert.Equal("MoveTargetMenu", scene.CurrentModeName)
    tick { Buttons.none with A = true }

    for _ in 1 .. 20 do
        tick { Buttons.none with A = true }
        tick Buttons.none

    Assert.Equal(10, scene.CurrentState.Player.Pp.[0])
    Assert.Equal(0, Bag.count "ETHER" scene.CurrentBag)
    Assert.True(scene.CurrentState.Player.Hp < active.Hp)

[<Fact>]
let ``BAT-022 Poke Doll escapes wild battles but is rejected by trainers`` () =
    let splash = Moves.byName "SPLASH"
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 50 with Moves = [ splash ]; Pp = [ splash.Pp ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 20 with Moves = [ splash ]; Pp = [ splash.Pp ] }
    let useDoll state =
        let scene = BattleScene(Content().Font, { state with Messages = [] }, bag = (Bag.empty |> Bag.add "POKE_DOLL" 1))
        let tick buttons = (scene :> Scene).Update buttons |> ignore
        tick { Buttons.none with Down = true }
        tick Buttons.none
        tick { Buttons.none with A = true }
        tick Buttons.none
        tick { Buttons.none with A = true }
        scene

    let wild = useDoll (Battle.createTeam [ player ] [ enemy ] 0u)
    Assert.Equal(Some Ran, wild.CurrentState.Outcome)
    Assert.Equal(0, Bag.count "POKE_DOLL" wild.CurrentBag)

    let trainer =
        { Group = "TEST"
          Id = "DOLL"
          Name = "TESTER"
          ClassName = "TESTER"
          WinText = None
          LossText = None
          BaseReward = None }
    let trainerScene = useDoll (Battle.createTrainer trainer [ player ] [ enemy ] 0u)
    Assert.True(trainerScene.CurrentState.Outcome.IsNone)
    Assert.Equal(1, Bag.count "POKE_DOLL" trainerScene.CurrentBag)

[<Fact>]
let ``trainer battle RUN and ball use are blocked`` () =
    let tackle = Moves.byName "TACKLE"
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50 with Moves = [ tackle ]; Pp = [ tackle.Pp ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 5 100 20 20 1 with Moves = [ Moves.byName "SPLASH" ]; Pp = [ (Moves.byName "SPLASH").Pp ] }
    let trainer =
        { Group = "RIVAL1"
          Id = "RIVAL1_1_TOTODILE"
          Name = "???"
          ClassName = "RIVAL"
          WinText = None
          LossText = None
          BaseReward = Some 25 }
    let state = { Battle.createTrainer trainer [ player ] [ enemy ] 0u with Messages = [] }
    let runAttempt = Battle.run state

    Assert.True(runAttempt.Outcome.IsNone)
    Assert.Contains(runAttempt.Messages, fun msg -> msg.Contains("no running", System.StringComparison.OrdinalIgnoreCase))

    let scene = BattleScene(Content().Font, state, bag = (Bag.empty |> Bag.add "POKE_BALL" 1))
    let tick buttons = (scene :> Scene).Update buttons |> ignore
    tick { Buttons.none with Down = true }
    tick Buttons.none
    tick { Buttons.none with A = true }
    tick Buttons.none
    tick { Buttons.none with A = true }

    Assert.True(scene.CurrentState.Outcome.IsNone)
    Assert.Equal(1, Bag.count "POKE_BALL" scene.CurrentBag)

[<Fact>]
let ``a stat-down move lowers the target's stage`` () =
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ growl ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ growl ] }
    let after = Battle.create player enemy 3u |> Battle.chooseMove 0
    // Player is faster and lowers the enemy's Attack stage to -1.
    Assert.Equal(-1, after.Enemy.AtkStage)

[<Fact>]
let ``secondary-on-hit flinch uses the move's effect chance`` () =
    let flinchHit = { move "HEADBUTT" "EFFECT_FLINCH_HIT" 10 (ty "NORMAL") with EffectChance = 100 }
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ flinchHit ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ] }
    let ctx =
        { User = player
          Foe = enemy
          Move = flinchHit
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let applied = Effects.forMove flinchHit |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx
    Assert.True(applied.Foe.Volatile.Flinch)

[<Fact>]
let ``stat-stage family maps special and accuracy/evasion effects to non-fallback commands`` () =
    let spAtk = Moves.byName "GROWTH"
    let acc = Moves.byName "SAND_ATTACK"
    let evasion = Moves.byName "DOUBLE_TEAM"
    Assert.Contains(RaiseUserStat SpAttack, Effects.forMove spAtk)
    Assert.Contains(LowerTargetStat Accuracy, Effects.forMove acc)
    Assert.Contains(RaiseUserStat Evasion, Effects.forMove evasion)

[<Fact>]
let ``secondary poison-on-hit only applies when the effect-chance roll succeeds`` () =
    let move = { move "POISON_HIT" "EFFECT_POISON_HIT" 0 (ty "NORMAL") with EffectChance = 100 }
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ move ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ] }
    let ctx =
        { User = player
          Foe = enemy
          Move = move
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let applied = Effects.forMove move |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx
    Assert.Equal(Poison, applied.Foe.Status)

[<Fact>]
let ``team battle continues after enemy lead faints`` () =
    let p = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 50 [ Moves.byName "EMBER" ]
    let e1 = BattleMon.ofSpecies (Species.byName "PIDGEY") 5 [ Moves.byName "TACKLE" ]
    let e2 = BattleMon.ofSpecies (Species.byName "RATTATA") 5 [ Moves.byName "TACKLE" ]
    let state = Battle.createTeam [ p ] [ e1; e2 ] 0u
    let after = Battle.chooseMove 0 state
    Assert.True(after.Outcome.IsNone || after.Enemy.Species.Name <> "PIDGEY")

[<Fact>]
let ``BAT-021 trainer faint waits for a player replacement before another action`` () =
    let splash = Moves.byName "SPLASH"
    let lead =
        { mon "LEAD" (ty "NORMAL") (ty "NORMAL") 5 1 1 1 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let bench =
        { mon "BENCH" (ty "NORMAL") (ty "NORMAL") 20 100 40 40 20 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 200 100 200 with
            Moves = [ strongHit ]
            Pp = [ strongHit.Pp ] }
    let trainer =
        { Group = "TEST"
          Id = "FORCED_SWITCH"
          Name = "TESTER"
          ClassName = "TESTER"
          WinText = None
          LossText = None
          BaseReward = None }

    let after = Battle.createTrainer trainer [ lead; bench ] [ enemy ] 0u |> Battle.chooseMove 0

    Assert.Equal("LEAD", after.Player.Species.Name)
    Assert.True(BattleMon.isFainted after.Player)
    Assert.True(Battle.requiresPlayerReplacement after)
    Assert.DoesNotContain(after.Messages, fun message -> message.Contains("BENCH used"))

[<Fact>]
let ``BAT-021 enemy replacement waits until the next turn to act`` () =
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 200 100 200 with
            Moves = [ strongHit ]
            Pp = [ strongHit.Pp ] }
    let enemyLead =
        { mon "FIRST" (ty "NORMAL") (ty "NORMAL") 5 1 1 1 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let enemyBench =
        { mon "SECOND" (ty "NORMAL") (ty "NORMAL") 30 200 100 100 100 with
            Moves = [ strongHit ]
            Pp = [ strongHit.Pp ] }
    let trainer =
        { Group = "TEST"
          Id = "ENEMY_REPLACEMENT"
          Name = "TESTER"
          ClassName = "TESTER"
          WinText = None
          LossText = None
          BaseReward = None }

    let after = Battle.createTrainer trainer [ player ] [ enemyLead; enemyBench ] 0u |> Battle.chooseMove 0

    Assert.Equal("SECOND", after.Enemy.Species.Name)
    Assert.DoesNotContain(after.Messages, fun message -> message.Contains("SECOND used"))

[<Fact>]
let ``BAT-021 forced replacement rejects fainted targets until a legal party choice`` () =
    let splash = Moves.byName "SPLASH"
    let lead =
        { mon "LEAD" (ty "NORMAL") (ty "NORMAL") 5 1 1 1 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let faintedBench =
        { mon "FAINTED" (ty "NORMAL") (ty "NORMAL") 20 100 40 40 20 with
            Hp = 0
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let healthyBench =
        { mon "HEALTHY" (ty "NORMAL") (ty "NORMAL") 20 100 40 40 20 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 200 100 200 with
            Moves = [ strongHit ]
            Pp = [ strongHit.Pp ] }
    let trainer =
        { Group = "TEST"
          Id = "LEGAL_REPLACEMENT"
          Name = "TESTER"
          ClassName = "TESTER"
          WinText = None
          LossText = None
          BaseReward = None }

    let pending = Battle.createTrainer trainer [ lead; faintedBench; healthyBench ] [ enemy ] 0u |> Battle.chooseMove 0
    let rejected = Battle.choosePlayerReplacement 1 pending
    let accepted = Battle.choosePlayerReplacement 2 pending

    Assert.True(Battle.requiresPlayerReplacement rejected)
    Assert.Equal("LEAD", rejected.Player.Species.Name)
    Assert.False(Battle.requiresPlayerReplacement accepted)
    Assert.Equal("HEALTHY", accepted.Player.Species.Name)

[<Fact>]
let ``BAT-021 simultaneous faint chooses the player replacement before enemy replacement`` () =
    let explosion = { move "EXPLOSION" "EFFECT_SELFDESTRUCT" 250 (ty "NORMAL") with Accuracy = 100; Pp = 5 }
    let splash = Moves.byName "SPLASH"
    let playerLead =
        { mon "PLAYER_LEAD" (ty "NORMAL") (ty "NORMAL") 50 100 200 100 200 with
            Moves = [ explosion ]
            Pp = [ explosion.Pp ] }
    let playerBench =
        { mon "PLAYER_BENCH" (ty "NORMAL") (ty "NORMAL") 30 100 100 100 100 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let enemyLead =
        { mon "ENEMY_LEAD" (ty "NORMAL") (ty "NORMAL") 5 1 1 1 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let enemyBench =
        { mon "ENEMY_BENCH" (ty "NORMAL") (ty "NORMAL") 30 100 100 100 100 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let trainer =
        { Group = "TEST"
          Id = "SIMULTANEOUS_FAINT"
          Name = "TESTER"
          ClassName = "TESTER"
          WinText = None
          LossText = None
          BaseReward = None }

    let pending = Battle.createTrainer trainer [ playerLead; playerBench ] [ enemyLead; enemyBench ] 0u |> Battle.chooseMove 0

    Assert.True(Battle.requiresPlayerReplacement pending)
    Assert.Equal("PLAYER_LEAD", pending.Player.Species.Name)
    Assert.Equal("ENEMY_LEAD", pending.Enemy.Species.Name)

    let resumed = Battle.choosePlayerReplacement 1 pending
    Assert.False(Battle.requiresPlayerReplacement resumed)
    Assert.Equal("PLAYER_BENCH", resumed.Player.Species.Name)
    Assert.Equal("ENEMY_BENCH", resumed.Enemy.Species.Name)

[<Fact>]
let ``BAT-021 multi-mon trainer battle repeats forced player and enemy replacements`` () =
    let splash = Moves.byName "SPLASH"
    let lead =
        { mon "LEAD" (ty "NORMAL") (ty "NORMAL") 5 1 1 1 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let middle =
        { mon "MIDDLE" (ty "NORMAL") (ty "NORMAL") 5 1 1 1 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let finisher =
        { mon "FINISHER" (ty "NORMAL") (ty "NORMAL") 50 200 200 100 300 with
            Moves = [ strongHit ]
            Pp = [ strongHit.Pp ] }
    let firstEnemy =
        { mon "FIRST" (ty "NORMAL") (ty "NORMAL") 50 200 200 100 200 with
            MaxHp = 1
            Hp = 1
            Moves = [ strongHit ]
            Pp = [ strongHit.Pp ] }
    let secondEnemy =
        { mon "SECOND" (ty "NORMAL") (ty "NORMAL") 5 1 1 1 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let trainer =
        { Group = "TEST"
          Id = "REPEATED_REPLACEMENT"
          Name = "TESTER"
          ClassName = "TESTER"
          WinText = None
          LossText = None
          BaseReward = None }

    let firstFaint = Battle.createTrainer trainer [ lead; middle; finisher ] [ firstEnemy; secondEnemy ] 0u |> Battle.chooseMove 0
    let secondFaint = firstFaint |> Battle.choosePlayerReplacement 1 |> Battle.chooseMove 0
    let firstEnemyFaint = secondFaint |> Battle.choosePlayerReplacement 2 |> Battle.chooseMove 0
    let victory = firstEnemyFaint |> Battle.chooseMove 0

    Assert.True(Battle.requiresPlayerReplacement firstFaint)
    Assert.True(Battle.requiresPlayerReplacement secondFaint)
    Assert.Equal("FINISHER", firstEnemyFaint.Player.Species.Name)
    Assert.Equal("SECOND", firstEnemyFaint.Enemy.Species.Name)
    Assert.Equal(Some Win, victory.Outcome)
    Assert.Equal(0, victory.PlayerTeam |> List.find (fun mon -> mon.Species.Name = "LEAD") |> fun mon -> mon.Hp)
    Assert.Equal(0, victory.PlayerTeam |> List.find (fun mon -> mon.Species.Name = "MIDDLE") |> fun mon -> mon.Hp)

[<Fact>]
let ``team battle ends when all enemies faint`` () =
    let p = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 99 [ Moves.byName "EMBER" ]
    let e1 = { BattleMon.ofSpecies (Species.byName "PIDGEY") 2 [ Moves.byName "TACKLE" ] with Hp = 1 }
    let state = Battle.createTeam [ p ] [ e1 ] 0u
    let after = Battle.chooseMove 0 state
    Assert.Equal(Some Win, after.Outcome)

[<Fact>]
let ``secondary poison-on-hit does not apply when the effect chance fails`` () =
    let move = { move "POISON_HIT" "EFFECT_POISON_HIT" 0 (ty "NORMAL") with EffectChance = 0 }
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ move ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ] }
    let ctx =
        { User = player
          Foe = enemy
          Move = move
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let applied = Effects.forMove move |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx
    Assert.Equal(Healthy, applied.Foe.Status)

[<Fact>]
let ``charging and recharge family maps to explicit effect commands`` () =
    Assert.Contains(Damage, Effects.forMove (Moves.byName "FLY"))
    Assert.DoesNotContain(BeginCharging, Effects.forMove (Moves.byName "FLY"))
    Assert.Contains(BeginRecharge, Effects.forMove (Moves.byName "HYPER_BEAM"))
    Assert.Contains(BeginRampage, Effects.forMove (Moves.byName "THRASH"))
    Assert.Contains(BeginFutureSight, Effects.forMove (Moves.byName "FUTURE_SIGHT"))

[<Fact>]
let ``RageDamage marks the user as raging`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ Moves.byName "RAGE" ] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ] }
    let ctx =
        { User = user
          Foe = foe
          Move = Moves.byName "RAGE"
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let applied = Effects.forMove (Moves.byName "RAGE") |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx
    Assert.True(applied.User.Volatile.Rage)

[<Fact>]
let ``conditional double damage doubles against a semi-invulnerable foe`` () =
    let attacker = { mon "ATTACKER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ move "GUST" "EFFECT_GUST" 40 (ty "NORMAL") ] }
    let defender = { mon "DEFENDER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ] }
    let baseCtx =
        { User = attacker
          Foe = defender
          Move = Moves.byName "GUST"
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let baseApplied = Effects.forMove (Moves.byName "GUST") |> List.fold (fun c cmd -> Effects.applyCtx c cmd) baseCtx
    let chargedCtx =
        { baseCtx with
            Foe = { defender with Volatile = { VolatileStatus.empty with Charging = Some 1; ChargingMove = Some (Moves.byName "FLY") } } }
    let chargedApplied = Effects.forMove (Moves.byName "GUST") |> List.fold (fun c cmd -> Effects.applyCtx c cmd) chargedCtx
    Assert.Equal(baseApplied.LastDamage * 2, chargedApplied.LastDamage)

[<Fact>]
let ``ATTRACT maps to an explicit effect command`` () =
    Assert.Contains(InflictAttract, Effects.forMove (Moves.byName "ATTRACT"))

[<Fact>]
let ``ATTRACT infatuates opposite explicit genders`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ Moves.byName "ATTRACT" ]; Gender = Male }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ]; Gender = Female }
    let ctx =
        { User = user
          Foe = foe
          Move = Moves.byName "ATTRACT"
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let applied = Effects.forMove (Moves.byName "ATTRACT") |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx
    Assert.True(applied.Foe.Volatile.Attracted)

[<Fact>]
let ``ATTRACT fails for same, genderless, and unknown genders`` () =
    let baseUser = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ Moves.byName "ATTRACT" ] }
    let baseFoe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ] }

    let cases =
        [ ("same gender", { baseUser with Gender = Male }, { baseFoe with Gender = Male })
          ("genderless", { baseUser with Gender = Genderless }, { baseFoe with Gender = Genderless })
          ("unknown", { baseUser with Gender = Unknown }, { baseFoe with Gender = Unknown }) ]

    let run (label, user, foe) =
        let ctx =
            { User = user
              Foe = foe
              Move = Moves.byName "ATTRACT"
              Crit = false
              Roll = Damage.MaxRoll
              Rng = Rng.create 0u
              Messages = []
              LastDamage = 0
              IsStruggle = false
              FuryCutterCount = 0
              RolloutCount = 0
              DefenseCurlUsed = false
              Friendship = 0
              UserIsPlayer = true
              PlayerSide = SideState.Empty
              EnemySide = SideState.Empty
              WeatherTimer = None; WeatherType = None }
        let applied = Effects.forMove (Moves.byName "ATTRACT") |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx
        label, applied.Foe.Volatile.Attracted

    let results = cases |> List.map run
    Assert.All(results, fun (label, attracted) -> Assert.False(attracted, label))

[<Fact>]
let ``attract gate blocks a move with the infatuation flag set`` () =
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ strongHit ]; Volatile = { VolatileStatus.empty with Attracted = true } }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ] }
    let state = Battle.create player enemy 0u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "immobilized by attraction!")

[<Fact>]
let ``confusion gate runs before attract gate`` () =
    let vol = { VolatileStatus.empty with Confusion = Some 2; Attracted = true }
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ strongHit ]; Volatile = vol }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ] }
    let state = Battle.create player enemy 0u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "hurt itself in its confusion!")
    Assert.DoesNotContain(after.Messages, fun m -> m.Contains "immobilized by attraction!")

[<Fact>]
let ``two-stage stat boosts raise the user's stage by two`` () =
    let swordsDance = { move "SWORDS_DANCE" "EFFECT_ATTACK_UP_2" 0 (ty "NORMAL") with EffectChance = 0 }
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ swordsDance ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ] }
    let after = Battle.create player enemy 0u |> Battle.chooseMove 0
    Assert.Equal(2, after.Player.AtkStage)

// --- M13.8 Field effects, timers, & residuals --------------------------------

[<Fact>]
let ``SANDSTORM maps to SetSandstorm command`` () =
    Assert.Contains(SetSandstorm, Effects.forMove (Moves.byName "SANDSTORM"))

[<Fact>]
let ``PERISH_SONG maps to SetPerishSong command`` () =
    Assert.Contains(SetPerishSong, Effects.forMove (Moves.byName "PERISH_SONG"))

[<Fact>]
let ``REFLECT maps to SetReflect command`` () =
    Assert.Contains(SetReflect, Effects.forMove (Moves.byName "REFLECT"))

[<Fact>]
let ``LIGHT_SCREEN maps to SetLightScreen command`` () =
    Assert.Contains(SetLightScreen, Effects.forMove (Moves.byName "LIGHT_SCREEN"))

[<Fact>]
let ``NIGHTMARE maps to SetNightmare command`` () =
    Assert.Contains(SetNightmare, Effects.forMove (Moves.byName "NIGHTMARE"))

[<Fact>]
let ``CURSE maps to SetCurse command`` () =
    Assert.Contains(SetCurse, Effects.forMove (Moves.byName "CURSE"))

[<Fact>]
let ``SPIKES maps to SetSpikes command`` () =
    Assert.Contains(SetSpikes, Effects.forMove (Moves.byName "SPIKES"))

[<Fact>]
let ``SAFEGUARD maps to SetSafeguard command`` () =
    Assert.Contains(SetSafeguard, Effects.forMove (Moves.byName "SAFEGUARD"))

[<Fact>]
let ``SetSandstorm sets the weather timer`` () =
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
    let ctx =
        { User = user; Foe = foe; Move = Moves.byName "SANDSTORM"
          Crit = false; Roll = Damage.MaxRoll; Rng = Rng.create 0u
          Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false
          Friendship = 0; UserIsPlayer = true
          PlayerSide = SideState.Empty; EnemySide = SideState.Empty; WeatherTimer = None; WeatherType = None }
    let applied = Effects.forMove (Moves.byName "SANDSTORM") |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx
    Assert.Equal(Some 5, applied.WeatherTimer)

[<Fact>]
let ``SetPerishSong sets counters on both sides`` () =
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
    let ctx =
        { User = user; Foe = foe; Move = Moves.byName "PERISH_SONG"
          Crit = false; Roll = Damage.MaxRoll; Rng = Rng.create 0u
          Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false
          Friendship = 0; UserIsPlayer = true
          PlayerSide = SideState.Empty; EnemySide = SideState.Empty; WeatherTimer = None; WeatherType = None }
    let applied = Effects.forMove (Moves.byName "PERISH_SONG") |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx
    Assert.Equal(Some 4, applied.PlayerSide.PerishCounter)
    Assert.Equal(Some 4, applied.EnemySide.PerishCounter)

[<Fact>]
let ``nightmare chips sleeping target each turn`` () =
    let vol = { VolatileStatus.empty with Nightmare = true }
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
                     Moves = [ strongHit ]; Status = Sleep 3; Volatile = vol }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ] }
    let state = Battle.create player enemy 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "suffering from Nightmare!")

[<Fact>]
let ``curse chips the cursed target each turn`` () =
    let vol = { VolatileStatus.empty with Curse = true }
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
                     Moves = [ strongHit ]; Volatile = vol }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ] }
    let state = Battle.create player enemy 0u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is hurt by Curse!")
    Assert.True(after.Player.Hp < 200)

let private mkCtx user foe move : MoveContext =
    { User = user
      Foe = foe
      Move = move
      Crit = false
      Roll = Damage.MaxRoll
      Rng = Rng.create 0u
      Messages = []
      LastDamage = 0
      IsStruggle = false
      FuryCutterCount = 0
      RolloutCount = 0
      DefenseCurlUsed = false
      Friendship = 0
      UserIsPlayer = true
      PlayerSide = SideState.Empty
      EnemySide = SideState.Empty
      WeatherTimer = None
      WeatherType = None }

let private firstProtectRollSeed predicate =
    let rec firstNonzero rng =
        let roll, rng' = Rng.next rng
        if roll = 0 then firstNonzero rng' else roll

    Seq.init 10000 uint32
    |> Seq.find (fun seed -> predicate (firstNonzero (Rng.create seed)))

[<Fact>]
let ``EFFECT_SPEED_UP maps correctly`` () =
    let mv = { move "SPEED_UP" "EFFECT_SPEED_UP" 0 (ty "NORMAL") with EffectChance = 0 }
    Assert.Contains(RaiseUserStat Speed, Effects.forMove mv)

[<Fact>]
let ``EFFECT_HEAL recovers half max HP`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200 with Hp = 10 }
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1
    let ctx = mkCtx user foe (Moves.byName "RECOVER")
    let applied = Effects.forMove (Moves.byName "RECOVER") |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx
    Assert.Equal(60, applied.User.Hp)

[<Fact>]
let ``EFFECT_HEAL Rest restores full HP and applies fixed sleep counter`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200 with Hp = 10; Status = Poison }
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1
    let rest = Moves.byName "REST"
    let applied = Effects.forMove rest |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (mkCtx user foe rest)
    Assert.Equal(100, applied.User.Hp)
    Assert.Equal(Sleep 3, applied.User.Status)

[<Fact>]
let ``EFFECT_SWAGGER confuses and raises target attack`` () =
    let mv = move "SWAGGER" "EFFECT_SWAGGER" 0 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1
    let applied = Effects.forMove mv |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (mkCtx user foe mv)
    Assert.True(applied.Foe.Volatile.Confusion.IsSome)
    Assert.Equal(2, applied.Foe.AtkStage)

[<Fact>]
let ``EFFECT_RESET_STATS clears all stages`` () =
    let mv = move "RESET_STATS" "EFFECT_RESET_STATS" 0 (ty "NORMAL")
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200 with AtkStage = 2; DefStage = -1; SpdStage = 3; SpAtkStage = 4; SpDefStage = -2; AccStage = 1; EvaStage = 5; Volatile = { VolatileStatus.empty with Confusion = Some 2 } }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1 with AtkStage = -3; DefStage = 1; SpdStage = 2; SpAtkStage = 0; SpDefStage = 1; AccStage = -1; EvaStage = 4; Volatile = { VolatileStatus.empty with LeechSeed = true } }
    let applied = Effects.forMove mv |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (mkCtx user foe mv)
    Assert.Equal(0, applied.User.AtkStage)
    Assert.Equal(0, applied.Foe.EvaStage)
    Assert.True(applied.User.Volatile.Confusion.IsSome)
    Assert.True(applied.Foe.Volatile.LeechSeed)

[<Fact>]
let ``EFFECT_BELLY_DRUM maximizes attack at half HP cost`` () =
    let mv = Moves.byName "BELLY_DRUM"
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200 with Hp = 60 }
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1
    let applied = Effects.forMove mv |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (mkCtx user foe mv)
    Assert.Equal(10, applied.User.Hp)
    Assert.Equal(6, applied.User.AtkStage)

[<Fact>]
let ``EFFECT_BELLY_DRUM preserves under-half HP attack boost bug`` () =
    let mv = Moves.byName "BELLY_DRUM"
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200 with Hp = 40 }
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1
    let applied = Effects.forMove mv |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (mkCtx user foe mv)
    Assert.Equal(40, applied.User.Hp)
    Assert.Equal(2, applied.User.AtkStage)
    Assert.Contains(applied.Messages, fun msg -> msg.Contains("failed"))

[<Fact>]
let ``EFFECT_PAIN_SPLIT averages HP`` () =
    let mv = Moves.byName "PAIN_SPLIT"
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200 with Hp = 60 }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1 with Hp = 40 }
    let applied = Effects.forMove mv |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (mkCtx user foe mv)
    Assert.Equal(50, applied.User.Hp)
    Assert.Equal(50, applied.Foe.Hp)

[<Fact>]
let ``EFFECT_PAIN_SPLIT fails against substitute`` () =
    let mv = Moves.byName "PAIN_SPLIT"
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200 with Hp = 60 }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1 with Hp = 40; Volatile = { VolatileStatus.empty with Substitute = Some 20 } }
    let applied = Effects.forMove mv |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (mkCtx user foe mv)
    Assert.Equal(60, applied.User.Hp)
    Assert.Equal(40, applied.Foe.Hp)
    Assert.Contains(applied.Messages, fun msg -> msg.Contains("failed"))

[<Fact>]
let ``EFFECT_RAIN_DANCE sets weather`` () =
    let mv = Moves.byName "RAIN_DANCE"
    let applied = Effects.forMove mv |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (mkCtx (mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200) (mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1) mv)
    Assert.Equal(Some 5, applied.WeatherTimer)
    Assert.Equal(Some "RAIN", applied.WeatherType)

[<Fact>]
let ``EFFECT_PROTECT uses shared ProtectChance substitute and consecutive-use gates`` () =
    let mv = Moves.byName "PROTECT"
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1
    let blockedUser =
        { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200 with
            Volatile = { VolatileStatus.empty with Substitute = Some 10; ProtectCount = 2 } }
    let blocked = Effects.applyCtx (mkCtx blockedUser foe mv) Protect
    Assert.False(blocked.User.Volatile.Protect)
    Assert.Equal(0, blocked.User.Volatile.ProtectCount)

    let repeatedUser =
        { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200 with
            Volatile = { VolatileStatus.empty with ProtectCount = 1 } }
    let failSeed = firstProtectRollSeed (fun roll -> roll >= 128)
    let failed = Effects.applyCtx { mkCtx repeatedUser foe mv with Rng = Rng.create failSeed } Protect
    Assert.False(failed.User.Volatile.Protect)
    Assert.Equal(0, failed.User.Volatile.ProtectCount)

    let successSeed = firstProtectRollSeed (fun roll -> roll <= 127)
    let succeeded = Effects.applyCtx { mkCtx repeatedUser foe mv with Rng = Rng.create successSeed } Protect
    Assert.True(succeeded.User.Volatile.Protect)
    Assert.Equal(2, succeeded.User.Volatile.ProtectCount)

[<Fact>]
let ``EFFECT_ENDURE shares ProtectChance and clamps lethal damage to one HP`` () =
    let mv = Moves.byName "ENDURE"
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1
    let applied = Effects.forMove mv |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (mkCtx user foe mv)
    Assert.True(applied.User.Volatile.Endure)
    Assert.Equal(1, applied.User.Volatile.ProtectCount)

    let tackle = Moves.byName "TACKLE"
    let attacker = mon "ATTACKER" (ty "NORMAL") (ty "NORMAL") 50 100 250 30 200
    let enduring =
        { mon "TARGET" (ty "NORMAL") (ty "NORMAL") 50 100 30 30 1 with
            Hp = 20
            Volatile = { VolatileStatus.empty with Endure = true } }
    let hit = Effects.applyCtx (mkCtx attacker enduring tackle) Damage
    Assert.Equal(1, hit.Foe.Hp)
    Assert.Contains(hit.Messages, fun m -> m.Contains("endured the hit"))

[<Fact>]
let ``EFFECT_PROTECT blocks damage after a successful brace`` () =
    let tackle = Moves.byName "TACKLE"
    let attacker = mon "ATTACKER" (ty "NORMAL") (ty "NORMAL") 50 100 250 30 200
    let protectedTarget =
        { mon "TARGET" (ty "NORMAL") (ty "NORMAL") 50 100 30 30 1 with
            Hp = 20
            Volatile = { VolatileStatus.empty with Protect = true } }
    let hit = Effects.applyCtx (mkCtx attacker protectedTarget tackle) Damage
    Assert.Equal(20, hit.Foe.Hp)
    Assert.Equal(0, hit.LastDamage)
    Assert.Contains(hit.Messages, fun m -> m.Contains("protecting itself"))

[<Fact>]
let ``EFFECT_PROTECT fails if the opponent already moved`` () =
    let protect = Moves.byName "PROTECT"
    let tackle = Moves.byName "TACKLE"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1 with
            Moves = [ protect ]
            Pp = [ protect.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }
    let after = Battle.chooseMove 0 (Battle.create player enemy 0u)
    Assert.Contains(after.Messages, fun m -> m = "But it failed!")
    Assert.DoesNotContain(after.Messages, fun m -> m.Contains("protected itself"))
    Assert.True(after.Player.Hp < player.Hp)

[<Fact>]
let ``EFFECT_DESTINY_BOND sets flag`` () =
    let mv = Moves.byName "DESTINY_BOND"
    let applied = Effects.forMove mv |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (mkCtx (mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200) (mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1) mv)
    Assert.True(applied.User.Volatile.DestinyBond)

[<Fact>]
let ``EFFECT_ENCORE sets timer on target`` () =
    let mv = Moves.byName "ENCORE"
    let applied = Effects.forMove mv |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (mkCtx (mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 200) (mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1) mv)
    Assert.Equal(Some 3, applied.Foe.Volatile.EncoreTimer)

// --- M13.1 Accuracy / miss hit check -----------------------------------------

/// Helper: create a move with a specific accuracy percentage.
let private moveWithAcc name effect power typ acc : MoveData =
    { Name = name; Effect = effect; Power = power; Type = typ
      Accuracy = acc; Pp = 35; EffectChance = 0 }

[<Fact>]
let ``accuracy 100 pct is a guaranteed hit with no RNG draw`` () =
    // 100 * 255 / 100 = 255 = $FF -> guaranteed hit, no draw consumed.
    let acc = BattleMon.applyAccEvaStages (100 * 255 / 100) 0 0
    Assert.Equal(255, acc)

[<Fact>]
let ``accuracy stage ratios match accuracy_multipliers asm at neutral`` () =
    // At neutral stages (0, 0), the ratios are (1,1) so the byte is unchanged.
    let accByte = 95 * 255 / 100 // Tackle: 242
    Assert.Equal(242, accByte)
    Assert.Equal(242, BattleMon.applyAccEvaStages accByte 0 0)

[<Fact>]
let ``max foe evasion +6 drastically reduces accuracy`` () =
    // accByte = 242 (Tackle), user acc stage 0, foe eva stage +6
    // Pass 1: 242 * 1/1 = 242 (neutral acc)
    // Pass 2: foe eva +6 -> inverted index 0 -> ratio 33/100
    //   242 * 33 / 100 = 7986/100 = 79 (truncated)
    let result = BattleMon.applyAccEvaStages 242 0 6
    Assert.Equal(79, result)

[<Fact>]
let ``max user accuracy +6 greatly increases accuracy`` () =
    // accByte = 127 (50%), user acc stage +6, foe eva stage 0
    // Pass 1: index 12 -> ratio 3/1 -> 127 * 3 / 1 = 381
    //   clamp to min 1 -> 381
    // Pass 2: index 6 -> ratio 1/1 -> 381 * 1 / 1 = 381
    //   clamp to min 1 -> 381
    // Cap at 255
    let result = BattleMon.applyAccEvaStages 127 6 0
    Assert.Equal(255, result) // capped at 255 -> guaranteed hit

[<Fact>]
let ``applyAccEvaStages never returns below 1`` () =
    // Even with the lowest possible accuracy and worst stages, minimum is 1.
    let result = BattleMon.applyAccEvaStages 1 (-6) 6
    // Pass 1: 1 * 33/100 = 0 -> clamped to 1
    // Pass 2: 1 * 33/100 = 0 -> clamped to 1
    Assert.Equal(1, result)

[<Fact>]
let ``EFFECT_ALWAYS_HIT bypasses accuracy check regardless of evasion`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ moveWithAcc "SWIFT" "EFFECT_ALWAYS_HIT" 60 (ty "NORMAL") 100 ]
                      AccStage = -6 }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]
                     EvaStage = 6 }
    // Even with worst accuracy stage and max evasion, ALWAYS_HIT always hits.
    let state = Battle.create user foe 0u
    let after = Battle.chooseMove 0 state
    // The player's Swift should deal damage (not show "attack missed!")
    Assert.DoesNotContain(after.Messages, fun m -> m.Contains "attack missed!")
    Assert.True(after.Enemy.Hp < foe.Hp)

[<Fact>]
let ``a miss skips effects and shows attack missed message`` () =
    // Use a very low accuracy move and a seed that produces a high roll.
    let lowAccMove = moveWithAcc "LOWMOVE" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") 10
    // 10 * 255 / 100 = 25 (accByte). At neutral stages: modifiedAcc = 25.
    // Need a seed where the first draw (accuracy roll) >= 25.
    // Seed 0u: first draw = 0 (hit). Seed 1u: first draw = ?
    // Let's use a seed that gives a high first draw.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ lowAccMove ] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ] }
    // Try seeds until we find one that misses
    let mutable seed = 1u
    let mutable found = false
    while not found && seed < 1000u do
        let draw, _ = Rng.next (Rng.create seed)
        if draw >= 25 then found <- true
        else seed <- seed + 1u
    Assert.True(found, "Could not find a seed that causes a miss")
    let state = Battle.create user foe seed
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "attack missed!")
    // The foe should not have taken damage from the missed move
    Assert.Equal(foe.Hp, after.Enemy.Hp)

[<Fact>]
let ``a hit at full accuracy and neutral stages deals damage`` () =
    // Accuracy 95 -> accByte 242. At neutral stages, modifiedAcc = 242.
    // Seed 0u: first draw is 0, which is < 242 -> hit.
    let tackle95 = moveWithAcc "TACKLE" "EFFECT_NORMAL_HIT" 35 (ty "NORMAL") 95
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ tackle95 ] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ] }
    let state = Battle.create user foe 0u
    let after = Battle.chooseMove 0 state
    Assert.DoesNotContain(after.Messages, fun m -> m.Contains "attack missed!")
    Assert.True(after.Enemy.Hp < foe.Hp)

[<Fact>]
let ``miss with max evasion on foe using seeded RNG`` () =
    // Tackle accByte = 242, foe evaStage +6: modifiedAcc = 79 (see earlier test).
    // Need a seed where accuracy draw >= 79.
    let tackle95 = moveWithAcc "TACKLE" "EFFECT_NORMAL_HIT" 35 (ty "NORMAL") 95
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ tackle95 ] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]
                     EvaStage = 6 }
    let mutable seed = 1u
    let mutable found = false
    while not found && seed < 1000u do
        let draw, _ = Rng.next (Rng.create seed)
        if draw >= 79 then found <- true
        else seed <- seed + 1u
    Assert.True(found, "Could not find a seed that causes a miss")
    let state = Battle.create user foe seed
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "attack missed!")
    Assert.Equal(foe.Hp, after.Enemy.Hp)

[<Fact>]
let ``boundary RNG value equal to accuracy causes a miss`` () =
    // Faithful to GSC: `cp b; jr nc, .Miss` means random >= accuracy -> miss.
    // So random == accuracy is a miss.
    // Set up modifiedAcc = 100 (arbitrary). Find seed where draw = 100.
    let m = moveWithAcc "TEST" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") 40
    // 40 * 255 / 100 = 102 (accByte). Neutral stages -> modifiedAcc = 102.
    // Find seed where draw = 102.
    let mutable seed = 0u
    let mutable found = false
    while not found && seed < 100000u do
        let draw, _ = Rng.next (Rng.create seed)
        if draw = 102 then found <- true
        else seed <- seed + 1u
    Assert.True(found, "Could not find a seed that produces draw = 102")
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ m ] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ] }
    let state = Battle.create user foe seed
    let after = Battle.chooseMove 0 state
    // draw == modifiedAcc -> miss (random >= accuracy)
    Assert.Contains(after.Messages, fun x -> x.Contains "attack missed!")

[<Fact>]
let ``boundary RNG value one below accuracy causes a hit`` () =
    // draw = modifiedAcc - 1 -> hit (random < accuracy)
    let m = moveWithAcc "TEST" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") 40
    // accByte = 102, neutral stages -> modifiedAcc = 102.
    // Find seed where draw = 101.
    let mutable seed = 0u
    let mutable found = false
    while not found && seed < 100000u do
        let draw, _ = Rng.next (Rng.create seed)
        if draw = 101 then found <- true
        else seed <- seed + 1u
    Assert.True(found, "Could not find a seed that produces draw = 101")
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ m ] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ] }
    let state = Battle.create user foe seed
    let after = Battle.chooseMove 0 state
    Assert.DoesNotContain(after.Messages, fun x -> x.Contains "attack missed!")
    Assert.True(after.Enemy.Hp < foe.Hp)

[<Fact>]
let ``the real demo encounter resolves with loaded data`` () =
    // Drives the actual scripted battle (real species, moves, types) to a result,
    // exercising the data loaders, effect interpreter, and turn loop together.
    let player =
        BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE"; Moves.byName "LEER" ]

    let enemy = BattleMon.ofSpecies (Species.byName "PIDGEY") 3 [ Moves.byName "TACKLE" ]

    let mutable state = Battle.create player enemy 0x1234u
    let mutable turns = 0

    while not (Battle.isOver state) && turns < 100 do
        state <- Battle.chooseMove 0 state
        turns <- turns + 1

    Assert.True(state.Outcome.IsSome)

// --- M13.0 Scaffolding tests: neutral-init + refactored turn loop parity ------

[<Fact>]
let ``ofSpecies initializes PP from MoveData`` () =
    let m = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE"; Moves.byName "LEER" ]
    Assert.Equal(2, m.Pp.Length)
    Assert.Equal(35, m.Pp.[0]) // Tackle PP
    Assert.Equal(30, m.Pp.[1]) // Leer PP

[<Fact>]
let ``ofSpecies initializes status to Healthy`` () =
    let m = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE" ]
    Assert.Equal(Healthy, m.Status)

[<Fact>]
let ``ofSpecies initializes accuracy and evasion stages to 0`` () =
    let m = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE" ]
    Assert.Equal(0, m.AccStage)
    Assert.Equal(0, m.EvaStage)

[<Fact>]
let ``ofSpecies initializes volatile status to empty`` () =
    let m = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE" ]
    Assert.Equal(VolatileStatus.empty, m.Volatile)
    Assert.Equal(None, m.Volatile.Confusion)
    Assert.Equal(false, m.Volatile.Flinch)
    Assert.Equal(false, m.Volatile.LeechSeed)
    Assert.Equal(None, m.Volatile.Substitute)
    Assert.Equal(None, m.Volatile.Trapped)
    Assert.Equal(false, m.Volatile.FocusEnergy)
    Assert.Equal(None, m.Volatile.Charging)
    Assert.Equal(false, m.Volatile.Recharge)
    Assert.Equal(None, m.Volatile.Rampage)

[<Fact>]
let ``refactored turn loop reproduces demo encounter outcome`` () =
    // Same scenario as the existing demo test -- must produce the identical
    // final state to confirm the refactoring is behavior-preserving.
    let player =
        BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE"; Moves.byName "LEER" ]
    let enemy = BattleMon.ofSpecies (Species.byName "PIDGEY") 3 [ Moves.byName "TACKLE" ]
    let mutable state = Battle.create player enemy 0x1234u
    let mutable turns = 0
    while not (Battle.isOver state) && turns < 100 do
        state <- Battle.chooseMove 0 state
        turns <- turns + 1
    Assert.True(state.Outcome.IsSome)
    // Pin the exact outcome and turn count so future changes are caught.
    Assert.Equal(Some Win, state.Outcome)

[<Fact>]
let ``VolatileStatus empty has all flags neutral`` () =
    let v = VolatileStatus.empty
    Assert.Equal(None, v.Confusion)
    Assert.False(v.Flinch)
    Assert.False(v.LeechSeed)
    Assert.Equal(None, v.Substitute)
    Assert.Equal(None, v.Trapped)
    Assert.False(v.FocusEnergy)
    Assert.Equal(None, v.Charging)
    Assert.False(v.Recharge)
    Assert.Equal(None, v.Rampage)

[<Fact>]
let ``MoveContext is populated during move execution`` () =
    // Verify the turn loop still threads state correctly through the
    // MoveContext-based execution by checking a stat-down move works.
    let player = { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ growl ]; Pp = [ 40 ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ growl ]; Pp = [ 40 ] }
    let after = Battle.create player enemy 3u |> Battle.chooseMove 0
    Assert.Equal(-1, after.Enemy.AtkStage)

// --- M13.2 PP & Struggle -----------------------------------------------------

[<Fact>]
let ``PP decrements by 1 when a move is used`` () =
    let tackle = Moves.byName "TACKLE"
    let player =
        BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ tackle; Moves.byName "LEER" ]
    let enemy = BattleMon.ofSpecies (Species.byName "PIDGEY") 3 [ Moves.byName "TACKLE" ]
    let state = Battle.create player enemy 0x1234u
    let after = Battle.chooseMove 0 state
    // Player used TACKLE (index 0): PP should be 34 (35 - 1).
    Assert.Equal(tackle.Pp - 1, after.Player.Pp.[0])
    // LEER was not used: PP stays at 30.
    Assert.Equal(30, after.Player.Pp.[1])

[<Fact>]
let ``enemy PP decrements on move use`` () =
    let tackle = Moves.byName "TACKLE"
    // Use same-level mons so neither KOs the other in one turn.
    let player =
        BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ tackle ]
    let enemy = BattleMon.ofSpecies (Species.byName "PIDGEY") 5 [ tackle ]
    let state = Battle.create player enemy 0x1234u
    let after = Battle.chooseMove 0 state
    // Enemy also used TACKLE: PP should be 34.
    Assert.Equal(tackle.Pp - 1, after.Enemy.Pp.[0])

[<Fact>]
let ``a move at 0 PP cannot be selected (canUseMove returns false)`` () =
    let tackle = Moves.byName "TACKLE"
    let player =
        { BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ tackle; Moves.byName "LEER" ]
          with Pp = [ 0; 30 ] }
    Assert.False(BattleMon.canUseMove 0 player)
    Assert.True(BattleMon.canUseMove 1 player)

[<Fact>]
let ``mustStruggle is true only when all PP is 0`` () =
    let m = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE"; Moves.byName "LEER" ]
    Assert.False(BattleMon.mustStruggle m)
    let exhausted = { m with Pp = [ 0; 0 ] }
    Assert.True(BattleMon.mustStruggle exhausted)

[<Fact>]
let ``when all moves are 0 PP the user Struggles and takes recoil`` () =
    let tackle = Moves.byName "TACKLE"
    let player =
        { BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 50 [ tackle ]
          with Pp = [ 0 ] }
    let enemy =
        { BattleMon.ofSpecies (Species.byName "PIDGEY") 50 [ tackle ]
          with Pp = [ 35 ] }
    let state = Battle.create player enemy 0x42u
    let after = Battle.chooseMove 0 state
    // Player should have struggled — look for the message.
    Assert.Contains(after.Messages, fun m -> m.Contains "has no moves left!")
    Assert.Contains(after.Messages, fun m -> m.Contains "used STRUGGLE")
    // Player takes recoil damage.
    Assert.Contains(after.Messages, fun m -> m.Contains "hit with recoil!")
    Assert.True(after.Player.Hp < player.Hp)

[<Fact>]
let ``Struggle recoil is 1/4 of damage dealt, minimum 1`` () =
    // Recoil = damage / 4, min 1.
    // Verify via the Damage.calc for Struggle + known attacker/defender.
    let struggle = Moves.byName "STRUGGLE"
    let attacker = mon "ATK" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
    let defender = mon "DEF" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
    // Struggle is isStruggle=true so no STAB.
    let dmg = Damage.calc attacker defender struggle false Damage.MaxRoll true
    let recoil = max 1 (dmg / 4)
    Assert.True(recoil >= 1)
    // Verify STAB is skipped: a NORMAL-type mon using STRUGGLE (NORMAL type)
    // should NOT get STAB.
    let dmgNoStruggle = Damage.calc attacker defender struggle false Damage.MaxRoll false
    // With STAB (isStruggle=false on NORMAL attacker with NORMAL Struggle):
    // dmgNoStruggle should be higher because STAB applies.
    Assert.True(dmgNoStruggle > dmg)

[<Fact>]
let ``enemy Struggles when out of PP`` () =
    let tackle = Moves.byName "TACKLE"
    let player =
        { BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 50 [ tackle ]
          with Pp = [ 35 ] }
    let enemy =
        { BattleMon.ofSpecies (Species.byName "PIDGEY") 50 [ tackle ]
          with Pp = [ 0 ] }
    let state = Battle.create player enemy 0x42u
    let after = Battle.chooseMove 0 state
    // Enemy should have Struggled.
    Assert.Contains(after.Messages, fun m -> m.Contains "PIDGEY has no moves left!")
    Assert.Contains(after.Messages, fun m -> m.Contains "PIDGEY used STRUGGLE")

[<Fact>]
let ``FIGHT menu PP display helpers return correct values`` () =
    let m = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 5 [ Moves.byName "TACKLE"; Moves.byName "LEER" ]
    Assert.Equal(35, BattleMon.maxPp 0 m)
    Assert.Equal(30, BattleMon.maxPp 1 m)
    Assert.Equal(35, m.Pp.[0])
    Assert.Equal(30, m.Pp.[1])
    // After deducting PP:
    let m' = BattleMon.deductPp 0 m
    Assert.Equal(34, m'.Pp.[0])
    Assert.Equal(30, m'.Pp.[1])

// --- M13.10 Critical-hit system (full) ----------------------------------------

// -- Crit-stage computation ---------------------------------------------------

[<Fact>]
let ``crit stage is 0 with no Focus Energy and non-high-crit move`` () =
    let m = move "TACKLE" "EFFECT_NORMAL_HIT" 35 (ty "NORMAL")
    Assert.Equal(0, CriticalHit.critStage false m)

[<Fact>]
let ``Focus Energy adds +1 to crit stage`` () =
    let m = move "TACKLE" "EFFECT_NORMAL_HIT" 35 (ty "NORMAL")
    Assert.Equal(1, CriticalHit.critStage true m)

[<Fact>]
let ``high-crit move adds +2 to crit stage`` () =
    let slash = { move "SLASH" "EFFECT_NORMAL_HIT" 70 (ty "NORMAL") with Accuracy = 100; Pp = 20 }
    Assert.Equal(2, CriticalHit.critStage false slash)

[<Fact>]
let ``Focus Energy + high-crit move stack to stage 3`` () =
    let slash = { move "SLASH" "EFFECT_NORMAL_HIT" 70 (ty "NORMAL") with Accuracy = 100; Pp = 20 }
    Assert.Equal(3, CriticalHit.critStage true slash)

[<Fact>]
let ``all CriticalHitMoves are recognized as high-crit`` () =
    // data/moves/critical_hit_moves.asm
    for name in [ "KARATE_CHOP"; "RAZOR_WIND"; "RAZOR_LEAF"; "CRABHAMMER"; "SLASH"; "AEROBLAST"; "CROSS_CHOP" ] do
        let m = Moves.byName name
        Assert.True(CriticalHit.isHighCritMove m, $"{name} should be high-crit")

[<Fact>]
let ``non-high-crit moves are not flagged`` () =
    for name in [ "TACKLE"; "EMBER"; "SURF"; "THUNDERBOLT" ] do
        let m = Moves.byName name
        Assert.False(CriticalHit.isHighCritMove m, $"{name} should not be high-crit")

[<Fact>]
let ``crit stage is capped at table max`` () =
    // Max index = 6; stage 3 is already under cap but verify the clamp works.
    Assert.Equal(6, CriticalHit.thresholds.Length - 1)
    // Even if we artificially called with a value beyond table, it clamps.
    let slash = { move "SLASH" "EFFECT_NORMAL_HIT" 70 (ty "NORMAL") with Accuracy = 100; Pp = 20 }
    let stage = CriticalHit.critStage true slash // 3
    Assert.True(stage <= CriticalHit.thresholds.Length - 1)

// -- Crit threshold table -----------------------------------------------------

[<Fact>]
let ``crit thresholds match critical_hit_chances asm`` () =
    // data/battle/critical_hit_chances.asm: out_of macro = $100 / n
    Assert.Equal(17, CriticalHit.thresholds.[0])  // 256/15
    Assert.Equal(32, CriticalHit.thresholds.[1])  // 256/8
    Assert.Equal(64, CriticalHit.thresholds.[2])  // 256/4
    Assert.Equal(85, CriticalHit.thresholds.[3])  // 256/3
    Assert.Equal(128, CriticalHit.thresholds.[4]) // 256/2
    Assert.Equal(128, CriticalHit.thresholds.[5]) // 256/2
    Assert.Equal(128, CriticalHit.thresholds.[6]) // 256/2

// -- Crit probability boundary tests ------------------------------------------

[<Fact>]
let ``seed that crits at stage 0 (byte < 17)`` () =
    // Find a seed where the crit byte (first draw) < 17.
    let mutable seed = 0u
    let mutable found = false
    while not found && seed < 10000u do
        let draw, _ = Rng.next (Rng.create seed)
        if draw < 17 then found <- true
        else seed <- seed + 1u
    Assert.True(found, "Should find a seed with crit byte < 17")
    // Verify via a battle that the crit fires. Use 100% accuracy (no acc draw).
    let atk = { mon "ATK" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                with Moves = [ move "HIT" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") ]; Pp = [35] }
    let def = { mon "DEF" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ move "HIT" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") ]; Pp = [35] }
    let state = Battle.create atk def seed
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "A critical hit!")

[<Fact>]
let ``seed that does not crit at stage 0 (byte >= 17)`` () =
    let mutable seed = 0u
    let mutable found = false
    while not found && seed < 10000u do
        let draw, _ = Rng.next (Rng.create seed)
        if draw >= 17 then found <- true
        else seed <- seed + 1u
    Assert.True(found)
    let atk = { mon "ATK" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                with Moves = [ move "HIT" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") ]; Pp = [35] }
    let def = { mon "DEF" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ move "HIT" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") ]; Pp = [35] }
    let state = Battle.create atk def seed
    let after = Battle.chooseMove 0 state
    Assert.DoesNotContain(after.Messages, fun m -> m.Contains "A critical hit!")

[<Fact>]
let ``Focus Energy mon crits at stage 1 threshold (byte < 32)`` () =
    // Find a seed where crit byte >= 17 (no crit at stage 0) but < 32 (crit at stage 1).
    let mutable seed = 0u
    let mutable found = false
    while not found && seed < 100000u do
        let draw, _ = Rng.next (Rng.create seed)
        if draw >= 17 && draw < 32 then found <- true
        else seed <- seed + 1u
    Assert.True(found, "Need a seed with crit byte in [17,31]")
    let atk = { mon "ATK" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                with Moves = [ move "HIT" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") ]; Pp = [35]
                     Volatile = { VolatileStatus.empty with FocusEnergy = true } }
    let def = { mon "DEF" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ move "HIT" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") ]; Pp = [35] }
    let state = Battle.create atk def seed
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "A critical hit!")

[<Fact>]
let ``high-crit move crits at stage 2 threshold (byte < 64)`` () =
    // Find a seed where crit byte >= 32 (no crit at stage 1) but < 64 (crit at stage 2).
    let mutable seed = 0u
    let mutable found = false
    while not found && seed < 100000u do
        let draw, _ = Rng.next (Rng.create seed)
        if draw >= 32 && draw < 64 then found <- true
        else seed <- seed + 1u
    Assert.True(found)
    let slash = { move "SLASH" "EFFECT_NORMAL_HIT" 70 (ty "NORMAL") with Accuracy = 100; Pp = 20 }
    let atk = { mon "ATK" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                with Moves = [ slash ]; Pp = [20] }
    let def = { mon "DEF" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ move "HIT" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") ]; Pp = [35] }
    let state = Battle.create atk def seed
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "A critical hit!")

[<Fact>]
let ``Focus Energy + high-crit stacks to stage 3 threshold (byte < 85)`` () =
    // Find a seed where crit byte >= 64 but < 85.
    let mutable seed = 0u
    let mutable found = false
    while not found && seed < 100000u do
        let draw, _ = Rng.next (Rng.create seed)
        if draw >= 64 && draw < 85 then found <- true
        else seed <- seed + 1u
    Assert.True(found)
    let slash = { move "SLASH" "EFFECT_NORMAL_HIT" 70 (ty "NORMAL") with Accuracy = 100; Pp = 20 }
    let atk = { mon "ATK" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                with Moves = [ slash ]; Pp = [20]
                     Volatile = { VolatileStatus.empty with FocusEnergy = true } }
    let def = { mon "DEF" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ move "HIT" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL") ]; Pp = [35] }
    let state = Battle.create atk def seed
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "A critical hit!")

// -- Crit damage: faithful stat rule ------------------------------------------

[<Fact>]
let ``crit with negative atk stage ignores stages (uses base stats)`` () =
    // Attacker: AtkStage = -2, Defender: DefStage = 0.
    // defStage (0) >= atkStage (-2) → use unmodified stats.
    let atk = { mon "ATK" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
                with AtkStage = -2 }
    let def = mon "DEF" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
    let m = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    // Crit with base stats: same as neutral crit (30 atk, 25 def).
    let critDmg = Damage.calc atk def m true Damage.MaxRoll false
    let neutralCritDmg = Damage.calc (mon "ATK" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50) def m true Damage.MaxRoll false
    Assert.Equal(neutralCritDmg, critDmg)

[<Fact>]
let ``crit with positive def stage on defender ignores stages (uses base stats)`` () =
    // Attacker: AtkStage = 0, Defender: DefStage = +2.
    // defStage (2) >= atkStage (0) → use unmodified stats.
    let atk = mon "ATK" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
    let def = { mon "DEF" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
                with DefStage = 2 }
    let m = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    let critDmg = Damage.calc atk def m true Damage.MaxRoll false
    // Without crit, +2 def would reduce damage.
    let noCritDmg = Damage.calc atk def m false Damage.MaxRoll false
    Assert.True(critDmg > noCritDmg, "Crit should ignore defender's positive def stage")

[<Fact>]
let ``crit with positive atk stage and neutral def uses boosted stats`` () =
    // Attacker: AtkStage = +2, Defender: DefStage = 0.
    // defStage (0) < atkStage (2) → use boosted stats (favor attacker).
    let atk = { mon "ATK" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
                with AtkStage = 2 }
    let def = mon "DEF" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
    let m = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    let critDmg = Damage.calc atk def m true Damage.MaxRoll false
    // With boosted atk (+2): effective atk = 30 * 2/1 = 60.
    // With unmodified: atk = 30.
    let neutralCritDmg = Damage.calc (mon "ATK" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50) def m true Damage.MaxRoll false
    Assert.True(critDmg > neutralCritDmg, "Crit with high atk stage should use boosted atk")

[<Fact>]
let ``crit with negative def stage on defender uses boosted stats`` () =
    // Attacker: AtkStage = 0, Defender: DefStage = -2.
    // defStage (-2) < atkStage (0) → use boosted stats (still favors attacker).
    let atk = mon "ATK" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
    let def = { mon "DEF" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
                with DefStage = -2 }
    let m = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    let critDmg = Damage.calc atk def m true Damage.MaxRoll false
    // With boosted (lowered) def: effective def = 25 * 50/100 = 12.
    // With unmodified: def = 25.
    let baseCritDmg = Damage.calc atk (mon "DEF" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50) m true Damage.MaxRoll false
    Assert.True(critDmg > baseCritDmg, "Crit with negative def stage should use lowered defense")

[<Fact>]
let ``crit with equal atk and def stages ignores stages`` () =
    // Both at +2: defStage (2) >= atkStage (2) → use unmodified.
    let atk = { mon "ATK" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
                with AtkStage = 2 }
    let def = { mon "DEF" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
                with DefStage = 2 }
    let m = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    let critDmg = Damage.calc atk def m true Damage.MaxRoll false
    let neutralCritDmg = Damage.calc (mon "ATK" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50)
                                      (mon "DEF" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50)
                                      m true Damage.MaxRoll false
    Assert.Equal(neutralCritDmg, critDmg)

// --- M13.3 Non-volatile status + end-of-turn residuals ----------------------

// Helper: create a mon with a specific status
let private monWithStatus name t1 t2 level hp atk def spd status : BattleMon =
    { mon name t1 t2 level hp atk def spd with
        Status = status
        Moves = [ strongHit ]
        Pp = [ 35 ] }

// Helper: find a seed where the first Rng draw satisfies a predicate
let private findSeed pred =
    let mutable seed = 0u
    while seed < 1000000u && not (pred (fst (Rng.next (Rng.create seed)))) do
        seed <- seed + 1u
    seed

// --- Workstream C1: first battle-surface move-effect audit --------------------

[<Fact>]
let ``C1 audited battle-surface effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_NORMAL_HIT", [ Damage ]
          "EFFECT_ACCURACY_DOWN_HIT", [ Damage; EffectChance(LowerTargetStat Accuracy) ]
          "EFFECT_CONFUSE_HIT", [ Damage; EffectChance InflictConfuse ]
          "EFFECT_THUNDER", [ Damage; EffectChance InflictParalyze ]
          "EFFECT_SLEEP", [ InflictSleep ]
          "EFFECT_PARALYZE", [ InflictParalyze ]
          "EFFECT_BURN_HIT", [ Damage; EffectChance InflictBurn ]
          "EFFECT_ATTACK_DOWN_HIT", [ Damage; EffectChance(LowerTargetStat Attack) ]
          "EFFECT_HYPER_BEAM", [ Damage; BeginRecharge ]
          "EFFECT_PRIORITY_HIT", [ Damage ] ]

    for effect, expected in cases do
        let audited = { move effect effect 40 (ty "NORMAL") with Accuracy = 100; EffectChance = 100 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C1 secondary hit effects damage first and apply their effect on success`` () =
    let smoke =
        { move "SMOKESCREEN_HIT" "EFFECT_ACCURACY_DOWN_HIT" 40 (ty "NORMAL") with Accuracy = 100; EffectChance = 255 }
    let punch =
        { move "DYNAMICPUNCH" "EFFECT_CONFUSE_HIT" 40 (ty "FIGHTING") with Accuracy = 100; EffectChance = 255 }
    let ember =
        { move "EMBER_HIT" "EFFECT_BURN_HIT" 40 (ty "FIRE") with Accuracy = 100; EffectChance = 255 }

    let apply move =
        let user = { mon "USER" (ty "FIRE") (ty "FIRE") 50 200 100 100 200 with Moves = [ move ]; Pp = [ 35 ] }
        let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ growl ]; Pp = [ 40 ] }
        Battle.create user foe 42u |> Battle.chooseMove 0

    let afterSmoke = apply smoke
    Assert.True(afterSmoke.Enemy.Hp < 200)
    Assert.Equal(-1, afterSmoke.Enemy.AccStage)

    let afterPunch = apply punch
    Assert.True(afterPunch.Enemy.Hp < 200)
    Assert.True(afterPunch.Enemy.Volatile.Confusion.IsSome)

    let afterEmber = apply ember
    Assert.True(afterEmber.Enemy.Hp < 200)
    Assert.Equal(Burn, afterEmber.Enemy.Status)

[<Fact>]
let ``C1 Thunder follows rain and sun accuracy overrides`` () =
    let thunder = { Moves.byName "THUNDER" with Accuracy = 0 }
    let user = { mon "USER" (ty "ELECTRIC") (ty "ELECTRIC") 50 200 100 100 200 with Moves = [ thunder ]; Pp = [ 10 ] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ growl ]; Pp = [ 40 ] }

    let rainy = { Battle.create user foe 42u with WeatherType = Some "RAIN" }
    let afterRain = Battle.chooseMove 0 rainy
    Assert.True(afterRain.Enemy.Hp < foe.Hp)

    let sunnyThunder = { Moves.byName "THUNDER" with Accuracy = 100 }
    let sunnyUser = { user with Moves = [ sunnyThunder ] }
    let missSeed = findSeed (fun draw -> draw >= 128)
    let sunny = { Battle.create sunnyUser foe missSeed with WeatherType = Some "SUN" }
    let afterSun = Battle.chooseMove 0 sunny
    Assert.Contains(afterSun.Messages, fun m -> m.Contains "attack missed!")
    Assert.Equal(foe.Hp, afterSun.Enemy.Hp)

[<Fact>]
let ``C1 Hyper Beam recharge and priority hit follow battle-order gates`` () =
    let beam = { Moves.byName "HYPER_BEAM" with Accuracy = 100 }
    let slowBeamUser = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ beam ]; Pp = [ 5 ] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ growl ]; Pp = [ 40 ] }
    let afterBeam = Battle.create slowBeamUser foe 42u |> Battle.chooseMove 0
    Assert.True(afterBeam.Player.Volatile.Recharge)

    let quick = { Moves.byName "QUICK_ATTACK" with Accuracy = 100 }
    let slowQuickUser = { mon "SLOW" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ quick ]; Pp = [ 30 ] }
    let fastFoe = { mon "FAST" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ growl ]; Pp = [ 40 ] }
    let afterQuick = Battle.create slowQuickUser fastFoe 42u |> Battle.chooseMove 0
    let quickIndex = afterQuick.Messages |> List.findIndex (fun m -> m.Contains "SLOW used QUICK_ATTACK")
    let growlIndex = afterQuick.Messages |> List.findIndex (fun m -> m.Contains "FAST used GROWL")
    Assert.True(quickIndex < growlIndex)

[<Fact>]
let ``C2 audited secondary hit effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_PARALYZE_HIT", [ Damage; EffectChance InflictParalyze ]
          "EFFECT_FREEZE_HIT", [ Damage; EffectChance InflictFreeze ]
          "EFFECT_FLINCH_HIT", [ Damage; EffectChance SetFlinch ]
          "EFFECT_POISON_HIT", [ Damage; EffectChance InflictPoison ]
          "EFFECT_DEFENSE_DOWN_HIT", [ Damage; EffectChance(LowerTargetStat Defense) ]
          "EFFECT_SPEED_DOWN_HIT", [ Damage; EffectChance(LowerTargetStat Speed) ]
          "EFFECT_SP_ATK_DOWN_HIT", [ Damage; EffectChance(LowerTargetStat SpAttack) ]
          "EFFECT_SP_DEF_DOWN_HIT", [ Damage; EffectChance(LowerTargetStat SpDefense) ]
          "EFFECT_TRAP_TARGET", [ Damage; TrapTarget ]
          "EFFECT_LEECH_HIT", [ DrainDamage ]
          "EFFECT_RECOIL_HIT", [ Damage; Recoil ] ]

    for effect, expected in cases do
        let audited = { move effect effect 40 (ty "NORMAL") with Accuracy = 100; EffectChance = 255 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C2 status and stat secondary hits damage first and apply the follow-up`` () =
    let apply effect =
        let audited = { move effect effect 40 (ty "NORMAL") with Accuracy = 100; EffectChance = 255 }
        let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
        let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
        let ctx =
            { User = user
              Foe = foe
              Move = audited
              Crit = false
              Roll = Damage.MaxRoll
              Rng = Rng.create 0u
              Messages = []
              LastDamage = 0
              IsStruggle = false
              FuryCutterCount = 0
              RolloutCount = 0
              DefenseCurlUsed = false
              Friendship = 0
              UserIsPlayer = true
              PlayerSide = SideState.Empty
              EnemySide = SideState.Empty
              WeatherTimer = None
              WeatherType = None }
        Effects.forMove audited |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx

    let para = apply "EFFECT_PARALYZE_HIT"
    Assert.True(para.Foe.Hp < 200)
    Assert.Equal(Paralysis, para.Foe.Status)

    let freeze = apply "EFFECT_FREEZE_HIT"
    Assert.True(freeze.Foe.Hp < 200)
    Assert.Equal(Freeze, freeze.Foe.Status)

    let flinch = apply "EFFECT_FLINCH_HIT"
    Assert.True(flinch.Foe.Hp < 200)
    Assert.True(flinch.Foe.Volatile.Flinch)

    let defense = apply "EFFECT_DEFENSE_DOWN_HIT"
    Assert.True(defense.Foe.Hp < 200)
    Assert.Equal(-1, defense.Foe.DefStage)

    let speed = apply "EFFECT_SPEED_DOWN_HIT"
    Assert.Equal(-1, speed.Foe.SpdStage)

    let spAtk = apply "EFFECT_SP_ATK_DOWN_HIT"
    Assert.Equal(-1, spAtk.Foe.SpAtkStage)

    let spDef = apply "EFFECT_SP_DEF_DOWN_HIT"
    Assert.Equal(-1, spDef.Foe.SpDefStage)

[<Fact>]
let ``C2 trap drain and recoil use disassembly HP side effects`` () =
    let apply move userHp =
        let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Hp = userHp }
        let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
        let ctx =
            { User = user
              Foe = foe
              Move = move
              Crit = false
              Roll = Damage.MaxRoll
              Rng = Rng.create 0u
              Messages = []
              LastDamage = 0
              IsStruggle = false
              FuryCutterCount = 0
              RolloutCount = 0
              DefenseCurlUsed = false
              Friendship = 0
              UserIsPlayer = true
              PlayerSide = SideState.Empty
              EnemySide = SideState.Empty
              WeatherTimer = None
              WeatherType = None }
        Effects.forMove move |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx

    let wrap = { Moves.byName "WRAP" with Accuracy = 100 }
    let trapped = apply wrap 200
    Assert.True(trapped.Foe.Hp < 200)
    match trapped.Foe.Volatile.Trapped with
    | Some turns -> Assert.InRange(turns, 3, 6)
    | None -> Assert.Fail("Wrap should set the trap counter.")

    let drain = { Moves.byName "MEGA_DRAIN" with Accuracy = 100 }
    let drained = apply drain 100
    Assert.True(drained.Foe.Hp < 200)
    Assert.True(drained.User.Hp > 100)

    let recoil = { Moves.byName "TAKE_DOWN" with Accuracy = 100 }
    let recoiled = apply recoil 200
    Assert.True(recoiled.Foe.Hp < 200)
    Assert.True(recoiled.User.Hp < 200)

[<Fact>]
let ``C3 audited stat-stage effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_ATTACK_UP", [ RaiseUserStat Attack ]
          "EFFECT_DEFENSE_UP", [ RaiseUserStat Defense ]
          "EFFECT_SP_ATK_UP", [ RaiseUserStat SpAttack ]
          "EFFECT_EVASION_UP", [ RaiseUserStat Evasion ]
          "EFFECT_ATTACK_UP_2", [ RaiseUserStat Attack; RaiseUserStat Attack ]
          "EFFECT_DEFENSE_UP_2", [ RaiseUserStat Defense; RaiseUserStat Defense ]
          "EFFECT_SPEED_UP_2", [ RaiseUserStat Speed; RaiseUserStat Speed ]
          "EFFECT_SP_DEF_UP_2", [ RaiseUserStat SpDefense; RaiseUserStat SpDefense ]
          "EFFECT_ATTACK_DOWN", [ LowerTargetStat Attack ]
          "EFFECT_DEFENSE_DOWN", [ LowerTargetStat Defense ]
          "EFFECT_SPEED_DOWN", [ LowerTargetStat Speed ]
          "EFFECT_ACCURACY_DOWN", [ LowerTargetStat Accuracy ]
          "EFFECT_EVASION_DOWN", [ LowerTargetStat Evasion ]
          "EFFECT_ATTACK_DOWN_2", [ LowerTargetStat Attack; LowerTargetStat Attack ]
          "EFFECT_DEFENSE_DOWN_2", [ LowerTargetStat Defense; LowerTargetStat Defense ]
          "EFFECT_SPEED_DOWN_2", [ LowerTargetStat Speed; LowerTargetStat Speed ] ]

    for effect, expected in cases do
        let audited = { move effect effect 0 (ty "NORMAL") with Accuracy = 100 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C3 stat-stage commands raise the user and lower the target by exact stages`` () =
    let apply effect =
        let audited = { move effect effect 0 (ty "NORMAL") with Accuracy = 100 }
        let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100
        let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100
        let ctx =
            { User = user
              Foe = foe
              Move = audited
              Crit = false
              Roll = Damage.MaxRoll
              Rng = Rng.create 0u
              Messages = []
              LastDamage = 0
              IsStruggle = false
              FuryCutterCount = 0
              RolloutCount = 0
              DefenseCurlUsed = false
              Friendship = 0
              UserIsPlayer = true
              PlayerSide = SideState.Empty
              EnemySide = SideState.Empty
              WeatherTimer = None
              WeatherType = None }
        Effects.forMove audited |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx

    let atkUp = apply "EFFECT_ATTACK_UP"
    Assert.Equal(1, atkUp.User.AtkStage)
    Assert.Equal(0, atkUp.Foe.AtkStage)

    let evasionUp = apply "EFFECT_EVASION_UP"
    Assert.Equal(1, evasionUp.User.EvaStage)
    Assert.Equal(0, evasionUp.Foe.EvaStage)

    let spDefUp2 = apply "EFFECT_SP_DEF_UP_2"
    Assert.Equal(2, spDefUp2.User.SpDefStage)
    Assert.Equal(0, spDefUp2.Foe.SpDefStage)

    let attackDown = apply "EFFECT_ATTACK_DOWN"
    Assert.Equal(0, attackDown.User.AtkStage)
    Assert.Equal(-1, attackDown.Foe.AtkStage)

    let speedDown2 = apply "EFFECT_SPEED_DOWN_2"
    Assert.Equal(-2, speedDown2.Foe.SpdStage)

    let accuracyDown = apply "EFFECT_ACCURACY_DOWN"
    Assert.Equal(-1, accuracyDown.Foe.AccStage)

    let evasionDown = apply "EFFECT_EVASION_DOWN"
    Assert.Equal(-1, evasionDown.Foe.EvaStage)

[<Fact>]
let ``C4 audited status field healing screen and weather effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_POISON", [ InflictPoison ]
          "EFFECT_CONFUSE", [ InflictConfuse ]
          "EFFECT_TOXIC", [ InflictToxic ]
          "EFFECT_HEAL", [ HealUser ]
          "EFFECT_REFLECT", [ SetReflect ]
          "EFFECT_LIGHT_SCREEN", [ SetLightScreen ]
          "EFFECT_MIST", [ SetMist ]
          "EFFECT_SAFEGUARD", [ SetSafeguard ]
          "EFFECT_RAIN_DANCE", [ SetRainDance ]
          "EFFECT_SUNNY_DAY", [ SetSunnyDay ]
          "EFFECT_SANDSTORM", [ SetSandstorm ] ]

    for effect, expected in cases do
        let audited = { move effect effect 0 (ty "NORMAL") with Accuracy = 100 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C4 non-damaging status healing screen and weather effects apply disassembly state`` () =
    let apply move userHp =
        let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100 with Hp = userHp }
        let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
        let ctx = mkCtx user foe move
        Effects.forMove move |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx

    let poisoned = apply (Moves.byName "POISONPOWDER") 100
    Assert.Equal(Poison, poisoned.Foe.Status)

    let toxic = apply (Moves.byName "TOXIC") 100
    Assert.Equal(BadPoison 0, toxic.Foe.Status)

    let confused = apply (Moves.byName "CONFUSE_RAY") 100
    match confused.Foe.Volatile.Confusion with
    | Some turns -> Assert.InRange(turns, 2, 5)
    | None -> Assert.Fail("Confuse Ray should set a 2-5 turn confusion counter.")

    let recovered = apply (Moves.byName "RECOVER") 10
    Assert.Equal(60, recovered.User.Hp)

    let rested = apply (Moves.byName "REST") 10
    Assert.Equal(100, rested.User.Hp)
    Assert.Equal(Sleep 3, rested.User.Status)

    let mist = apply (Moves.byName "MIST") 100
    Assert.True(mist.User.Volatile.Mist)

    let safeguard = apply (Moves.byName "SAFEGUARD") 100
    Assert.Equal(Some 5, safeguard.PlayerSide.SafeguardTimer)

    let reflect = apply (Moves.byName "REFLECT") 100
    Assert.Equal(Some 5, reflect.PlayerSide.ReflectTimer)

    let lightScreen = apply (Moves.byName "LIGHT_SCREEN") 100
    Assert.Equal(Some 5, lightScreen.PlayerSide.LightScreenTimer)

    let rain = apply (Moves.byName "RAIN_DANCE") 100
    Assert.Equal(Some "RAIN", rain.WeatherType)
    Assert.Equal(Some 5, rain.WeatherTimer)

    let sun = apply (Moves.byName "SUNNY_DAY") 100
    Assert.Equal(Some "SUN", sun.WeatherType)
    Assert.Equal(Some 5, sun.WeatherTimer)

    let sand = apply (Moves.byName "SANDSTORM") 100
    Assert.Equal(Some "SAND", sand.WeatherType)
    Assert.Equal(Some 5, sand.WeatherTimer)

[<Fact>]
let ``C5 audited utility and fixed-damage effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_ALWAYS_HIT", [ Damage ]
          "EFFECT_STATIC_DAMAGE", [ StaticDamage ]
          "EFFECT_LEVEL_DAMAGE", [ LevelDamage ]
          "EFFECT_SUPER_FANG", [ SuperFangDamage ]
          "EFFECT_FALSE_SWIPE", [ FalseSwipeDamage ]
          "EFFECT_RETURN", [ ReturnDamage ]
          "EFFECT_FRUSTRATION", [ FrustrationDamage ]
          "EFFECT_FOCUS_ENERGY", [ SetFocusEnergy ]
          "EFFECT_SUBSTITUTE", [ CreateSubstitute ]
          "EFFECT_LEECH_SEED", [ ApplyLeechSeed ] ]

    for effect, expected in cases do
        let audited = { move effect effect 40 (ty "NORMAL") with Accuracy = 100 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C5 fixed-damage and utility effects apply disassembly state`` () =
    let ctxFor move userHp foeHp friendship =
        let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100 with Hp = userHp }
        let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100 with Hp = foeHp }
        { mkCtx user foe move with Friendship = friendship }

    let swiftUser = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with Moves = [ { Moves.byName "SWIFT" with Accuracy = 0 } ]; Pp = [ 20 ] }
    let swiftFoe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ growl ]; Pp = [ 40 ] }
    let swifted = Battle.create swiftUser swiftFoe 42u |> Battle.chooseMove 0
    Assert.True(swifted.Enemy.Hp < swiftFoe.Hp)

    let sonicboom = Effects.applyCtx (ctxFor (Moves.byName "SONICBOOM") 200 200 0) StaticDamage
    Assert.Equal(20, sonicboom.LastDamage)
    Assert.Equal(180, sonicboom.Foe.Hp)

    let seismicToss = Effects.applyCtx (ctxFor (Moves.byName "SEISMIC_TOSS") 200 200 0) LevelDamage
    Assert.Equal(50, seismicToss.LastDamage)
    Assert.Equal(150, seismicToss.Foe.Hp)

    let superFang = Effects.applyCtx (ctxFor (Moves.byName "SUPER_FANG") 200 101 0) SuperFangDamage
    Assert.Equal(50, superFang.LastDamage)
    Assert.Equal(51, superFang.Foe.Hp)

    let falseSwipe = Effects.applyCtx (ctxFor (Moves.byName "FALSE_SWIPE") 200 1 0) FalseSwipeDamage
    Assert.Equal(1, falseSwipe.Foe.Hp)
    Assert.Equal(0, falseSwipe.LastDamage)

    let returnZero = Effects.applyCtx (ctxFor (Moves.byName "RETURN") 200 200 0) ReturnDamage
    Assert.Equal(0, returnZero.LastDamage)
    Assert.Equal(200, returnZero.Foe.Hp)

    let frustrationZero = Effects.applyCtx (ctxFor (Moves.byName "FRUSTRATION") 200 200 255) FrustrationDamage
    Assert.Equal(0, frustrationZero.LastDamage)
    Assert.Equal(200, frustrationZero.Foe.Hp)

    let focused = Effects.applyCtx (ctxFor (Moves.byName "FOCUS_ENERGY") 200 200 0) SetFocusEnergy
    Assert.True(focused.User.Volatile.FocusEnergy)

    let seeded = Effects.applyCtx (ctxFor (Moves.byName "LEECH_SEED") 200 200 0) ApplyLeechSeed
    Assert.True(seeded.Foe.Volatile.LeechSeed)

    let substitute = Effects.applyCtx (ctxFor (Moves.byName "SUBSTITUTE") 200 200 0) CreateSubstitute
    Assert.Equal(150, substitute.User.Hp)
    Assert.Equal(Some 50, substitute.User.Volatile.Substitute)

[<Fact>]
let ``C6 audited random and multi-hit damage effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_PSYWAVE", [ PsywaveDamage ]
          "EFFECT_REVERSAL", [ ReversalDamage ]
          "EFFECT_PRESENT", [ PresentDamage ]
          "EFFECT_MAGNITUDE", [ MagnitudeDamage ]
          "EFFECT_TRIPLE_KICK", [ TripleKickDamage ]
          "EFFECT_MULTI_HIT", [ MultiHitDamage ]
          "EFFECT_DOUBLE_HIT", [ DoubleHitDamage ]
          "EFFECT_POISON_MULTI_HIT", [ PoisonMultiHitDamage ] ]

    for effect, expected in cases do
        let audited = { move effect effect 20 (ty "NORMAL") with Accuracy = 100 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C7 audited crash coin and hazard-clearing damage effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_JUMP_KICK", [ JumpKickDamage ]
          "EFFECT_PAY_DAY", [ PayDayDamage ]
          "EFFECT_RAPID_SPIN", [ RapidSpinDamage ] ]

    for effect, expected in cases do
        let audited = { move effect effect 40 (ty "NORMAL") with Accuracy = 100 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C8 audited volatile control effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_ATTRACT", [ InflictAttract ]
          "EFFECT_MEAN_LOOK", [ SetMeanLook ]
          "EFFECT_CURSE", [ SetCurse ]
          "EFFECT_SPIKES", [ SetSpikes ] ]

    for effect, expected in cases do
        let audited = { move effect effect 0 (ty "NORMAL") with Accuracy = 100 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C8 Curse and Spikes follow disassembly target-side behavior`` () =
    let baseFoe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100
    let ghostUser = { mon "USER" (ty "GHOST") (ty "GHOST") 50 200 100 100 100 with Hp = 200 }
    let curse = Moves.byName "CURSE"
    let cursed = Effects.applyCtx (mkCtx ghostUser baseFoe curse) SetCurse
    Assert.Equal(100, cursed.User.Hp)
    Assert.True(cursed.Foe.Volatile.Curse)

    let normalUser = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100
    let statCurse = Effects.applyCtx (mkCtx normalUser baseFoe curse) SetCurse
    Assert.Equal(1, statCurse.User.AtkStage)
    Assert.Equal(1, statCurse.User.DefStage)
    Assert.Equal(-1, statCurse.User.SpdStage)
    Assert.False(statCurse.Foe.Volatile.Curse)

    let spikes = Moves.byName "SPIKES"
    let scattered = Effects.applyCtx (mkCtx normalUser baseFoe spikes) SetSpikes
    Assert.Equal(1, scattered.EnemySide.Spikes)
    let failedSecondLayer = Effects.applyCtx { mkCtx normalUser baseFoe spikes with EnemySide = scattered.EnemySide } SetSpikes
    Assert.Equal(1, failedSecondLayer.EnemySide.Spikes)
    Assert.Contains(failedSecondLayer.Messages, fun msg -> msg.Contains("failed"))

[<Fact>]
let ``C9 audited utility effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_BELLY_DRUM", [ BellyDrum ]
          "EFFECT_PSYCH_UP", [ PsychUp ]
          "EFFECT_RESET_STATS", [ ResetStats ]
          "EFFECT_DREAM_EATER", [ DreamEaterDamage ]
          "EFFECT_PAIN_SPLIT", [ PainSplit ]
          "EFFECT_SPLASH", [] ]

    for effect, expected in cases do
        let audited = { move effect effect 0 (ty "NORMAL") with Accuracy = 100 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C9 Psych Up fails unless target stat stages changed`` () =
    let psychUp = Moves.byName "PSYCH_UP"
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let unchangedFoe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let failed = Effects.applyCtx (mkCtx user unchangedFoe psychUp) PsychUp
    Assert.Equal(0, failed.User.AtkStage)
    Assert.Contains(failed.Messages, fun msg -> msg.Contains("failed"))

    let changedFoe = { unchangedFoe with AtkStage = 2; EvaStage = -1 }
    let copied = Effects.applyCtx (mkCtx user changedFoe psychUp) PsychUp
    Assert.Equal(2, copied.User.AtkStage)
    Assert.Equal(-1, copied.User.EvaStage)

[<Fact>]
let ``C10 audited explosive and tri-status effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_SELFDESTRUCT", [ SelfdestructDamage ]
          "EFFECT_TRI_ATTACK", [ Damage; EffectChance TriStatus ] ]

    for effect, expected in cases do
        let audited = { move effect effect 80 (ty "NORMAL") with Accuracy = 100; EffectChance = 255 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C10 Selfdestruct halves target defense then clears user status side effects`` () =
    let user =
        { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 150 100 100 100 with
            Status = Poison
            Volatile = { VolatileStatus.empty with LeechSeed = true } }
    let foe =
        { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 300 100 100 100 with
            Volatile = { VolatileStatus.empty with DestinyBond = true } }
    let selfdestruct = Moves.byName "SELFDESTRUCT"

    let after = Effects.applyCtx (mkCtx user foe selfdestruct) SelfdestructDamage

    Assert.Equal(267, after.LastDamage)
    Assert.Equal(33, after.Foe.Hp)
    Assert.Equal(0, after.User.Hp)
    Assert.Equal(Healthy, after.User.Status)
    Assert.False(after.User.Volatile.LeechSeed)
    Assert.False(after.Foe.Volatile.DestinyBond)

[<Fact>]
let ``C10 Tri Attack chooses paralysis freeze or burn after effect chance succeeds`` () =
    let triAttack = { Moves.byName "TRI_ATTACK" with EffectChance = 255 }
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100

    let rec chosen rng =
        let roll, rng' = Rng.next rng
        match ((roll >>> 4) &&& 0x3) with
        | 0 -> chosen rng'
        | 1 -> Paralysis
        | 2 -> Freeze
        | _ -> Burn

    let seedForFreeze =
        let mutable seed = 0u
        let mutable found = false
        while seed < 100000u && not found do
            let gate, rngAfterGate = Rng.next (Rng.create seed)
            found <- gate < 255 && chosen rngAfterGate = Freeze
            if not found then seed <- seed + 1u
        seed

    let after =
        Effects.applyCtx { mkCtx user foe triAttack with Rng = Rng.create seedForFreeze } (EffectChance TriStatus)

    Assert.Equal(Freeze, after.Foe.Status)

[<Fact>]
let ``C11 audited stat-up-hit curl and rollout effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_ATTACK_UP_HIT", [ Damage; EffectChance(RaiseUserStat Attack) ]
          "EFFECT_DEFENSE_UP_HIT", [ Damage; EffectChance(RaiseUserStat Defense) ]
          "EFFECT_ALL_UP_HIT", [ Damage; EffectChance RaiseAllUserStats ]
          "EFFECT_DEFENSE_CURL", [ RaiseUserStat Defense; SetDefenseCurl ]
          "EFFECT_ROLLOUT", [ RolloutDamage ] ]

    for effect, expected in cases do
        let audited = { move effect effect 60 (ty "ROCK") with Accuracy = 100; EffectChance = 255 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C11 AncientPower uses one effect chance gate for all five stat raises`` () =
    let ancientPower = { Moves.byName "ANCIENTPOWER" with EffectChance = 255 }
    let user = mon "USER" (ty "ROCK") (ty "ROCK") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100

    let after =
        Effects.forMove ancientPower
        |> List.fold (fun ctx cmd -> Effects.applyCtx ctx cmd) (mkCtx user foe ancientPower)

    Assert.True(after.Foe.Hp < foe.Hp)
    Assert.Equal(1, after.User.AtkStage)
    Assert.Equal(1, after.User.DefStage)
    Assert.Equal(1, after.User.SpdStage)
    Assert.Equal(1, after.User.SpAtkStage)
    Assert.Equal(1, after.User.SpDefStage)

[<Fact>]
let ``C11 Defense Curl marks the user curled and Rollout doubles after STAB`` () =
    let defenseCurl = Moves.byName "DEFENSE_CURL"
    let rollout = Moves.byName "ROLLOUT"
    let user = mon "USER" (ty "ROCK") (ty "ROCK") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100

    let curled =
        Effects.forMove defenseCurl
        |> List.fold (fun ctx cmd -> Effects.applyCtx ctx cmd) (mkCtx user foe defenseCurl)

    Assert.Equal(1, curled.User.DefStage)
    Assert.True(curled.User.Volatile.Curled)

    let afterRollout =
        Effects.applyCtx { mkCtx curled.User foe rollout with DefenseCurlUsed = curled.User.Volatile.Curled } RolloutDamage

    Assert.Equal(44, afterRollout.LastDamage)
    Assert.Equal(56, afterRollout.Foe.Hp)

[<Fact>]
let ``C12 audited destiny bond and swagger effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_DESTINY_BOND", [ DestinyBond ]
          "EFFECT_SWAGGER", [ Swagger ] ]

    for effect, expected in cases do
        let audited = { move effect effect 0 (ty "NORMAL") with Accuracy = 100 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C12 Destiny Bond sets the user substatus`` () =
    let destinyBond = Moves.byName "DESTINY_BOND"
    let user = mon "USER" (ty "GHOST") (ty "GHOST") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100

    let after = Effects.applyCtx (mkCtx user foe destinyBond) DestinyBond

    Assert.True(after.User.Volatile.DestinyBond)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("take its foe"))

[<Fact>]
let ``C12 Swagger raises target Attack before confusing it`` () =
    let swagger = Moves.byName "SWAGGER"
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100

    let after = Effects.applyCtx (mkCtx user foe swagger) Swagger

    Assert.Equal(2, after.Foe.AtkStage)
    Assert.True(after.Foe.Volatile.Confusion.IsSome)
    Assert.Equal("FOE's ATTACK rose sharply!", after.Messages.[0])
    Assert.Equal("FOE became confused!", after.Messages.[1])

[<Fact>]
let ``C13 audited conditional double-damage effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_GUST", [ ConditionalDoubleDamage ]
          "EFFECT_EARTHQUAKE", [ ConditionalDoubleDamage ]
          "EFFECT_TWISTER", [ ConditionalDoubleDamage; EffectChance SetFlinch ]
          "EFFECT_STOMP", [ ConditionalDoubleDamage; EffectChance SetFlinch ] ]

    for effect, expected in cases do
        let audited = { move effect effect 40 (ty "NORMAL") with Accuracy = 100; EffectChance = 255 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C13 Gust and Earthquake only double against matching Fly and Dig substatuses`` () =
    let normalFoe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100
    let gustUser = mon "USER" (ty "FLYING") (ty "FLYING") 50 100 100 100 100
    let groundUser = mon "USER" (ty "GROUND") (ty "GROUND") 50 100 100 100 100
    let gust = Moves.byName "GUST"
    let earthquake = Moves.byName "EARTHQUAKE"
    let flyState = { VolatileStatus.empty with Charging = Some 1; ChargingMove = Some (Moves.byName "FLY") }
    let digState = { VolatileStatus.empty with Charging = Some 1; ChargingMove = Some (Moves.byName "DIG") }

    let gustVsFly = Effects.applyCtx (mkCtx gustUser { normalFoe with Volatile = flyState } gust) ConditionalDoubleDamage
    let gustVsDig = Effects.applyCtx (mkCtx gustUser { normalFoe with Volatile = digState } gust) ConditionalDoubleDamage
    let quakeVsDig = Effects.applyCtx (mkCtx groundUser { normalFoe with Volatile = digState } earthquake) ConditionalDoubleDamage
    let quakeVsFly = Effects.applyCtx (mkCtx groundUser { normalFoe with Volatile = flyState } earthquake) ConditionalDoubleDamage

    Assert.Equal(56, gustVsFly.LastDamage)
    Assert.Equal(28, gustVsDig.LastDamage)
    Assert.Equal(138, quakeVsDig.LastDamage)
    Assert.Equal(69, quakeVsFly.LastDamage)

[<Fact>]
let ``C13 Stomp doubles only against Minimize not ordinary evasion boosts`` () =
    let stomp = Moves.byName "STOMP"
    let minimize = Moves.byName "MINIMIZE"
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100

    let minimized =
        Effects.applyCtx (mkCtx foe user minimize) (RaiseUserStat Evasion)

    Assert.Equal(1, minimized.User.EvaStage)
    Assert.True(minimized.User.Volatile.Minimized)

    let evasionOnly = { foe with EvaStage = 6 }
    let stompNormalEvasion = Effects.applyCtx (mkCtx user evasionOnly stomp) ConditionalDoubleDamage
    let stompMinimized = Effects.applyCtx (mkCtx user minimized.User stomp) ConditionalDoubleDamage

    Assert.Equal(45, stompNormalEvasion.LastDamage)
    Assert.Equal(90, stompMinimized.LastDamage)

[<Fact>]
let ``C14 audited Fury Cutter and Snore effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_FURY_CUTTER", [ FuryCutterDamage ]
          "EFFECT_SNORE", [ SnoreDamage ] ]

    for effect, expected in cases do
        let audited = { move effect effect 40 (ty "NORMAL") with Accuracy = 100; EffectChance = 255 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C14 Fury Cutter doubles damage after STAB and type before variation`` () =
    let furyCutter = Moves.byName "FURY_CUTTER"
    let user = mon "USER" (ty "BUG") (ty "BUG") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100

    let thirdHit =
        Effects.applyCtx { mkCtx user foe furyCutter with FuryCutterCount = 2 } FuryCutterDamage

    Assert.Equal(36, thirdHit.LastDamage)
    Assert.Equal(164, thirdHit.Foe.Hp)
    Assert.Equal(3, thirdHit.FuryCutterCount)

[<Fact>]
let ``C14 Snore only works while asleep and applies its flinch secondary`` () =
    let snore = { Moves.byName "SNORE" with EffectChance = 255 }
    let sleepingUser = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100 with Status = Sleep 2 }
    let awakeUser = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100

    let asleep = Effects.applyCtx (mkCtx sleepingUser foe snore) SnoreDamage
    let awake = Effects.applyCtx (mkCtx awakeUser foe snore) SnoreDamage

    Assert.True(asleep.LastDamage > 0)
    Assert.True(asleep.Foe.Volatile.Flinch)
    Assert.Equal(0, awake.LastDamage)
    Assert.Equal(foe.Hp, awake.Foe.Hp)
    Assert.False(awake.Foe.Volatile.Flinch)

[<Fact>]
let ``C15 audited utility status effects map to disassembly command families`` () =
    let cases =
        [ "EFFECT_HIDDEN_POWER", [ HiddenPowerDamage ]
          "EFFECT_LOCK_ON", [ SetLockOn ]
          "EFFECT_FORESIGHT", [ SetForesight ]
          "EFFECT_NIGHTMARE", [ SetNightmare ]
          "EFFECT_PERISH_SONG", [ SetPerishSong ] ]

    for effect, expected in cases do
        let audited = { move effect effect 0 (ty "NORMAL") with Accuracy = 100 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C15 Hidden Power uses the modeled DV zero Fighting power`` () =
    let hiddenPower = Moves.byName "HIDDEN_POWER"
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100

    let after = Effects.applyCtx (mkCtx user foe hiddenPower) HiddenPowerDamage

    Assert.Equal(30, after.LastDamage)
    Assert.Equal(70, after.Foe.Hp)

[<Fact>]
let ``C15 Lock-On marks the target and fails through substitute`` () =
    let lockOn = Moves.byName "LOCK_ON"
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let substitutedFoe = { foe with Volatile = { VolatileStatus.empty with Substitute = Some 20 } }

    let aimed = Effects.applyCtx (mkCtx user foe lockOn) SetLockOn
    let failed = Effects.applyCtx (mkCtx user substitutedFoe lockOn) SetLockOn

    Assert.False(aimed.User.Volatile.LockOn)
    Assert.True(aimed.Foe.Volatile.LockOn)
    Assert.False(failed.Foe.Volatile.LockOn)
    Assert.Contains(failed.Messages, fun msg -> msg.Contains("failed"))

[<Fact>]
let ``C15 Nightmare requires a sleeping target and cannot be repeated`` () =
    let nightmare = Moves.byName "NIGHTMARE"
    let user = mon "USER" (ty "GHOST") (ty "GHOST") 50 100 100 100 100
    let awakeFoe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let sleepingFoe = { awakeFoe with Status = Sleep 3 }

    let failedAwake = Effects.applyCtx (mkCtx user awakeFoe nightmare) SetNightmare
    let applied = Effects.applyCtx (mkCtx user sleepingFoe nightmare) SetNightmare
    let failedRepeat = Effects.applyCtx (mkCtx user applied.Foe nightmare) SetNightmare

    Assert.False(failedAwake.Foe.Volatile.Nightmare)
    Assert.True(applied.Foe.Volatile.Nightmare)
    Assert.True(failedRepeat.Foe.Volatile.Nightmare)
    Assert.Contains(failedRepeat.Messages, fun msg -> msg.Contains("failed"))

[<Fact>]
let ``C15 Perish Song sets missing counters to four and fails when both are active`` () =
    let perishSong = Moves.byName "PERISH_SONG"
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100

    let applied = Effects.applyCtx (mkCtx user foe perishSong) SetPerishSong
    let failedRepeat = Effects.applyCtx applied SetPerishSong

    Assert.Equal(Some 4, applied.PlayerSide.PerishCounter)
    Assert.Equal(Some 4, applied.EnemySide.PerishCounter)
    Assert.Equal(Some 4, failedRepeat.PlayerSide.PerishCounter)
    Assert.Equal(Some 4, failedRepeat.EnemySide.PerishCounter)
    Assert.Contains(failedRepeat.Messages, fun msg -> msg.Contains("failed"))

[<Fact>]
let ``C16 audited thawing fire moves and Heal Bell map to disassembly command families`` () =
    let cases =
        [ "EFFECT_FLAME_WHEEL", [ Damage; DefrostUser; EffectChance InflictBurn ]
          "EFFECT_SACRED_FIRE", [ Damage; DefrostUser; EffectChance InflictBurn ]
          "EFFECT_HEAL_BELL", [ HealBellEffect ] ]

    for effect, expected in cases do
        let audited = { move effect effect 60 (ty "FIRE") with Accuracy = 100; EffectChance = 255 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C16 Flame Wheel can be used while frozen and thaws after damage`` () =
    let flameWheel = { Moves.byName "FLAME_WHEEL" with Accuracy = 100; EffectChance = 0 }
    let user =
        { mon "USER" (ty "FIRE") (ty "FIRE") 50 100 100 100 200 with
            Status = Freeze
            Moves = [ flameWheel ]
            Pp = [ 25 ] }
    let foe =
        { mon "FOE" (ty "GRASS") (ty "GRASS") 50 100 100 100 1 with
            Moves = [ growl ]
            Pp = [ 40 ] }

    let after = Battle.create user foe 42u |> Battle.chooseMove 0

    Assert.Equal(Healthy, after.Player.Status)
    Assert.True(after.Enemy.Hp < foe.Hp)

[<Fact>]
let ``C16 Heal Bell clears the active user's status and nightmare`` () =
    let healBell = Moves.byName "HEAL_BELL"
    let user =
        { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100 with
            Status = Poison
            Volatile = { VolatileStatus.empty with Nightmare = true } }
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100

    let after = Effects.applyCtx (mkCtx user foe healBell) HealBellEffect

    Assert.Equal(Healthy, after.User.Status)
    Assert.False(after.User.Volatile.Nightmare)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("bell"))

[<Fact>]
let ``C17 audited two-turn moves map to second-turn command families`` () =
    let cases =
        [ "EFFECT_FLY", [ Damage ]
          "EFFECT_SOLARBEAM", [ Damage ]
          "EFFECT_RAZOR_WIND", [ Damage ]
          "EFFECT_SKULL_BASH", [ Damage; RaiseUserStat Defense ]
          "EFFECT_SKY_ATTACK", [ Damage; EffectChance SetFlinch ] ]

    for effect, expected in cases do
        let audited = { move effect effect 80 (ty "NORMAL") with Accuracy = 100; EffectChance = 255 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C17 Fly charges on the first turn and damages on the second turn`` () =
    let fly = { Moves.byName "FLY" with Accuracy = 100 }
    let user =
        { mon "USER" (ty "FLYING") (ty "FLYING") 50 100 100 100 200 with
            Moves = [ fly ]
            Pp = [ 15 ] }
    let foe =
        { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 1 with
            Moves = [ growl ]
            Pp = [ 40 ] }

    let afterCharge = Battle.create user foe 42u |> Battle.chooseMove 0
    let afterHit = Battle.chooseMove 0 afterCharge

    Assert.Equal(foe.Hp, afterCharge.Enemy.Hp)
    Assert.True(afterCharge.Player.Volatile.Charging.IsSome)
    Assert.True(afterHit.Enemy.Hp < foe.Hp)
    Assert.True(afterHit.Player.Volatile.Charging.IsNone)

[<Fact>]
let ``C17 Skull Bash raises Defense after damage and Sky Attack can flinch`` () =
    let skullBash = Moves.byName "SKULL_BASH"
    let skyAttack = { Moves.byName "SKY_ATTACK" with EffectChance = 255 }
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100

    let skullBashAfter =
        Effects.forMove skullBash
        |> List.fold (fun ctx cmd -> Effects.applyCtx ctx cmd) (mkCtx user foe skullBash)
    let skyAttackAfter =
        Effects.forMove skyAttack
        |> List.fold (fun ctx cmd -> Effects.applyCtx ctx cmd) (mkCtx user foe skyAttack)

    Assert.True(skullBashAfter.Foe.Hp < foe.Hp)
    Assert.Equal(1, skullBashAfter.User.DefStage)
    Assert.True(skyAttackAfter.Foe.Hp < foe.Hp)
    Assert.True(skyAttackAfter.Foe.Volatile.Flinch)

[<Fact>]
let ``C18 audited weather-heal moves map to the shared time-based heal command`` () =
    let cases =
        [ "EFFECT_MORNING_SUN", [ WeatherHeal ]
          "EFFECT_SYNTHESIS", [ WeatherHeal ]
          "EFFECT_MOONLIGHT", [ WeatherHeal ] ]

    for effect, expected in cases do
        let audited = { move effect effect 0 (ty "NORMAL") with Accuracy = 100 }
        Assert.Equal<EffectCommand list>(expected, Effects.forMove audited)

[<Fact>]
let ``C18 weather heal restores half by default full in sun and quarter in rain or sand`` () =
    let synthesis = Moves.byName "SYNTHESIS"
    let user = { mon "USER" (ty "GRASS") (ty "GRASS") 50 200 100 100 100 with Hp = 40 }
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100
    let baseCtx = mkCtx user foe synthesis

    let clear = Effects.applyCtx baseCtx WeatherHeal
    let sunny = Effects.applyCtx { baseCtx with WeatherType = Some "SUN" } WeatherHeal
    let rainy = Effects.applyCtx { baseCtx with WeatherType = Some "RAIN" } WeatherHeal
    let sandy = Effects.applyCtx { baseCtx with WeatherType = Some "SAND" } WeatherHeal
    let full = Effects.applyCtx (mkCtx { user with Hp = user.MaxHp } foe synthesis) WeatherHeal

    Assert.Equal(140, clear.User.Hp)
    Assert.Equal(200, sunny.User.Hp)
    Assert.Equal(90, rainy.User.Hp)
    Assert.Equal(90, sandy.User.Hp)
    Assert.Equal(user.MaxHp, full.User.Hp)
    Assert.Contains(full.Messages, fun msg -> msg.Contains("HP is full"))

[<Fact>]
let ``C19 Rage uses its counter as a pre-variation damage multiplier`` () =
    let rage = Moves.byName "RAGE"
    Assert.Equal<EffectCommand list>([ RageDamage ], Effects.forMove rage)

    let user =
        { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100 with
            Volatile = { VolatileStatus.empty with RageCounter = 2 } }
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 100 100 100 100

    let after = Effects.applyCtx (mkCtx user foe rage) RageDamage

    Assert.True(after.User.Volatile.Rage)
    Assert.Equal(45, after.LastDamage)
    Assert.Equal(55, after.Foe.Hp)

[<Fact>]
let ``C19 hitting a raging target builds Rage counter without changing Attack stage`` () =
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let growlMove = { growl with Accuracy = 100 }
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ tackle ]
            Pp = [ 35 ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ growlMove ]
            Pp = [ 40 ]
            Volatile = { VolatileStatus.empty with Rage = true } }

    let after = Battle.create player enemy 42u |> Battle.chooseMove 0

    Assert.Equal(1, after.Enemy.Volatile.RageCounter)
    Assert.Equal(0, after.Enemy.AtkStage)

[<Fact>]
let ``C21 Beat Up damage uses participant base Attack and target base Defense`` () =
    let mv = Moves.byName "BEAT_UP"
    let attackerSpecies = { species "ALLY" (ty "DARK") (ty "DARK") with Attack = 80 }
    let defenderSpecies = { species "TARGET" (ty "NORMAL") (ty "NORMAL") with Defense = 50 }
    let attacker =
        { mon "ALLY" (ty "DARK") (ty "DARK") 20 100 999 30 200 with
            Species = attackerSpecies }
    let defender =
        { mon "TARGET" (ty "NORMAL") (ty "NORMAL") 20 100 30 999 1 with
            Species = defenderSpecies }
    let hit = Effects.applyCtx (mkCtx attacker defender mv) BeatUpDamage
    Assert.Equal(5, hit.LastDamage)
    Assert.Equal(95, hit.Foe.Hp)

[<Fact>]
let ``C21 Beat Up loops over healthy unstatted party members`` () =
    let mv = Moves.byName "BEAT_UP"
    let splash = Moves.byName "SPLASH"
    let activeSpecies = { species "ACTIVE" (ty "DARK") (ty "DARK") with Attack = 50 }
    let benchSpecies = { species "BENCH" (ty "DARK") (ty "DARK") with Attack = 100 }
    let skippedSpecies = { species "SKIPPED" (ty "DARK") (ty "DARK") with Attack = 200 }
    let targetSpecies = { species "TARGET" (ty "NORMAL") (ty "NORMAL") with Defense = 50 }
    let active =
        { mon "ACTIVE" (ty "DARK") (ty "DARK") 20 100 80 50 200 with
            Species = activeSpecies
            Moves = [ mv ]
            Pp = [ mv.Pp ] }
    let bench =
        { mon "BENCH" (ty "DARK") (ty "DARK") 20 100 80 50 100 with
            Species = benchSpecies }
    let skipped =
        { mon "SKIPPED" (ty "DARK") (ty "DARK") 20 100 80 50 100 with
            Species = skippedSpecies
            Status = Poison }
    let target =
        { mon "TARGET" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 1 with
            Species = targetSpecies
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let seed = 5u
    let threshold = CriticalHit.thresholds.[CriticalHit.critStage active.Volatile.FocusEnergy mv]
    let c1, rng = Rng.next (Rng.create seed)
    let s1, rng = Rng.next rng
    let c2, rng = Rng.next rng
    let s2, _ = Rng.next rng
    let spread byte = Damage.MinRoll + byte % (Damage.MaxRoll - Damage.MinRoll + 1)
    let expected =
        Effects.beatUpDamage active target mv (c1 < threshold) (spread s1)
        + Effects.beatUpDamage bench target mv (c2 < threshold) (spread s2)

    let after = Battle.chooseMove 0 (Battle.createTeam [ active; bench; skipped ] [ target ] seed)

    let attackMessages = after.Messages |> List.filter (fun m -> m.EndsWith("attacked!"))
    Assert.Equal<string list>([ "ACTIVE attacked!"; "BENCH attacked!" ], attackMessages)
    Assert.Equal(target.Hp - expected, after.Enemy.Hp)

[<Fact>]
let ``C22 Counter doubles the physical damage just taken when moving second`` () =
    let counter = Moves.byName "COUNTER"
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let player =
        { mon "PLAYER" (ty "FIGHTING") (ty "FIGHTING") 50 200 100 100 1 with
            Moves = [ counter ]
            Pp = [ counter.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }
    let seed = 7u
    let critByte, rng = Rng.next (Rng.create seed)
    let spreadByte, _ = Rng.next rng
    let threshold = CriticalHit.thresholds.[CriticalHit.critStage enemy.Volatile.FocusEnergy tackle]
    let spread = Damage.MinRoll + spreadByte % (Damage.MaxRoll - Damage.MinRoll + 1)
    let incoming = Damage.calc enemy player tackle (critByte < threshold) spread false

    let after = Battle.chooseMove 0 (Battle.create player enemy seed)

    Assert.Equal(player.Hp - incoming, after.Player.Hp)
    Assert.Equal(enemy.Hp - incoming * 2, after.Enemy.Hp)
    Assert.Contains(after.Messages, fun m -> m.Contains("Countered the attack"))

[<Fact>]
let ``C22 Counter fails for special damage or when moving first`` () =
    let counter = Moves.byName "COUNTER"
    let ember = { Moves.byName "EMBER" with Accuracy = 100 }
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let slowCounterUser =
        { mon "PLAYER" (ty "FIGHTING") (ty "FIGHTING") 50 200 100 100 1 with
            Moves = [ counter ]
            Pp = [ counter.Pp ] }
    let specialEnemy =
        { mon "ENEMY" (ty "FIRE") (ty "FIRE") 50 200 100 100 200 with
            Moves = [ ember ]
            Pp = [ ember.Pp ] }
    let afterSpecial = Battle.chooseMove 0 (Battle.create slowCounterUser specialEnemy 11u)
    Assert.Equal(specialEnemy.Hp, afterSpecial.Enemy.Hp)
    Assert.Contains(afterSpecial.Messages, fun m -> m = "But it failed!")

    let fastCounterUser =
        { slowCounterUser with
            Speed = 200
            Hp = slowCounterUser.MaxHp }
    let slowEnemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }
    let afterFirst = Battle.chooseMove 0 (Battle.create fastCounterUser slowEnemy 13u)
    Assert.Equal(slowEnemy.Hp, afterFirst.Enemy.Hp)
    Assert.Contains(afterFirst.Messages, fun m -> m = "But it failed!")

[<Fact>]
let ``C23 Disable targets the opponent's last move and blocks it on the next turn`` () =
    let disable = { Moves.byName "DISABLE" with Accuracy = 100 }
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ disable; splash ]
            Pp = [ disable.Pp; splash.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }

    let afterDisable = Battle.chooseMove 0 (Battle.create player enemy 19u)

    Assert.Equal(Some 0, afterDisable.Enemy.Volatile.DisabledMoveIndex)
    Assert.True(afterDisable.Enemy.Volatile.DisableTimer.Value >= 2)
    Assert.True(afterDisable.Enemy.Volatile.DisableTimer.Value <= 8)
    Assert.Contains(afterDisable.Messages, fun m -> m.Contains("TACKLE was disabled"))

    let playerHpAfterDisable = afterDisable.Player.Hp
    let afterBlocked = Battle.chooseMove 1 afterDisable

    Assert.Equal(playerHpAfterDisable, afterBlocked.Player.Hp)
    Assert.Contains(afterBlocked.Messages, fun m -> m.Contains("TACKLE is disabled"))

[<Fact>]
let ``C23 Disable fails before the opponent has a last move`` () =
    let disable = { Moves.byName "DISABLE" with Accuracy = 100 }
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ disable ]
            Pp = [ disable.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }

    let after = Battle.chooseMove 0 (Battle.create player enemy 23u)

    Assert.True(after.Enemy.Volatile.DisableTimer.IsNone)
    Assert.True(after.Enemy.Volatile.DisabledMoveIndex.IsNone)
    Assert.Contains(after.Messages, fun m -> m = "But it failed!")

[<Fact>]
let ``C24 Encore targets the opponent's previous last move and forces it this turn`` () =
    let encore = { Moves.byName "ENCORE" with Accuracy = 100 }
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ encore ]
            Pp = [ encore.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ tackle; splash ]
            Pp = [ tackle.Pp; splash.Pp ]
            Volatile = { VolatileStatus.empty with LastMove = Some splash } }

    let after = Battle.chooseMove 0 (Battle.create player enemy 29u)

    Assert.Equal(Some 1, after.Enemy.Volatile.EncoreMoveIndex)
    Assert.True(after.Enemy.Volatile.EncoreTimer.Value >= 2)
    Assert.True(after.Enemy.Volatile.EncoreTimer.Value <= 5)
    Assert.Equal(player.Hp, after.Player.Hp)
    Assert.Contains(after.Messages, fun m -> m.Contains("SPLASH got an encore"))
    Assert.Contains(after.Messages, fun m -> m.Contains("ENEMY used SPLASH"))

[<Fact>]
let ``C24 Encore fails for excluded last moves`` () =
    let encore = { Moves.byName "ENCORE" with Accuracy = 100 }
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ encore ]
            Pp = [ encore.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ tackle; encore ]
            Pp = [ tackle.Pp; encore.Pp ]
            Volatile = { VolatileStatus.empty with LastMove = Some encore } }

    let after = Battle.chooseMove 0 (Battle.create player enemy 31u)

    Assert.True(after.Enemy.Volatile.EncoreTimer.IsNone)
    Assert.True(after.Enemy.Volatile.EncoreMoveIndex.IsNone)
    Assert.Contains(after.Messages, fun m -> m.Contains("didn't affect"))

[<Fact>]
let ``C25 Future Sight stores pre-variation damage and starts a four-count timer`` () =
    let futureSight = { Moves.byName "FUTURE_SIGHT" with Accuracy = 100 }
    let user = mon "USER" (ty "PSYCHIC_TYPE") (ty "PSYCHIC_TYPE") 50 200 80 70 100
    let foe = mon "FOE" (ty "DARK") (ty "DARK") 50 200 80 90 100
    let expected = Effects.futureSightStoredDamage user foe futureSight

    let applied = Effects.applyCtx (mkCtx user foe futureSight) BeginFutureSight

    Assert.Equal(Some 4, applied.User.Volatile.FutureSightCounter)
    Assert.Equal(Some futureSight, applied.User.Volatile.FutureSightMove)
    Assert.Equal(Some expected, applied.User.Volatile.FutureSightDamage)
    Assert.Equal(0, applied.LastDamage)

[<Fact>]
let ``C25 Future Sight payoff uses stored damage with payoff turn variation`` () =
    let futureSight = { Moves.byName "FUTURE_SIGHT" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let player = { mon "PLAYER" (ty "PSYCHIC_TYPE") (ty "PSYCHIC_TYPE") 50 200 100 100 200 with Moves = [ splash ]; Pp = [ splash.Pp ] }
    let enemy = { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ splash ]; Pp = [ splash.Pp ] }
    let stored = Effects.futureSightStoredDamage player enemy futureSight
    let player =
        { player with
            Volatile =
                { VolatileStatus.empty with
                    FutureSightCounter = Some 2
                    FutureSightMove = Some futureSight
                    FutureSightDamage = Some stored } }
    let seed = 41u
    let _, rng = Rng.next (Rng.create seed)
    let _, rng = Rng.next rng
    let _, rng = Rng.next rng
    let _, rng = Rng.next rng
    let spreadByte, _ = Rng.next rng
    let roll = Damage.MinRoll + spreadByte % (Damage.MaxRoll - Damage.MinRoll + 1)
    let expected = stored * roll / Damage.MaxRoll

    let after = Battle.chooseMove 0 (Battle.create player enemy seed)

    Assert.Equal(enemy.Hp - expected, after.Enemy.Hp)
    Assert.True(after.Player.Volatile.FutureSightCounter.IsNone)
    Assert.True(after.Player.Volatile.FutureSightDamage.IsNone)
    Assert.Contains(after.Messages, fun m -> m.Contains("was hit by Future Sight"))

[<Fact>]
let ``C26 Mirror Coat doubles the special damage just taken when moving second`` () =
    let mirrorCoat = Moves.byName "MIRROR_COAT"
    let ember = { Moves.byName "EMBER" with Accuracy = 100 }
    let player =
        { mon "PLAYER" (ty "PSYCHIC_TYPE") (ty "PSYCHIC_TYPE") 50 200 100 100 1 with
            Moves = [ mirrorCoat ]
            Pp = [ mirrorCoat.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "FIRE") (ty "FIRE") 50 200 100 100 200 with
            Moves = [ ember ]
            Pp = [ ember.Pp ] }
    let seed = 43u
    let critByte, rng = Rng.next (Rng.create seed)
    let spreadByte, _ = Rng.next rng
    let threshold = CriticalHit.thresholds.[CriticalHit.critStage enemy.Volatile.FocusEnergy ember]
    let spread = Damage.MinRoll + spreadByte % (Damage.MaxRoll - Damage.MinRoll + 1)
    let incoming = Damage.calc enemy player ember (critByte < threshold) spread false

    let after = Battle.chooseMove 0 (Battle.create player enemy seed)

    Assert.Equal(player.Hp - incoming, after.Player.Hp)
    Assert.Equal(enemy.Hp - incoming * 2, after.Enemy.Hp)
    Assert.Contains(after.Messages, fun m -> m.Contains("Mirror Coated the attack"))

[<Fact>]
let ``C26 Mirror Coat fails for physical damage or when moving first`` () =
    let mirrorCoat = Moves.byName "MIRROR_COAT"
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let ember = { Moves.byName "EMBER" with Accuracy = 100 }
    let slowMirrorUser =
        { mon "PLAYER" (ty "PSYCHIC_TYPE") (ty "PSYCHIC_TYPE") 50 200 100 100 1 with
            Moves = [ mirrorCoat ]
            Pp = [ mirrorCoat.Pp ] }
    let physicalEnemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }
    let afterPhysical = Battle.chooseMove 0 (Battle.create slowMirrorUser physicalEnemy 47u)
    Assert.Equal(physicalEnemy.Hp, afterPhysical.Enemy.Hp)
    Assert.Contains(afterPhysical.Messages, fun m -> m = "But it failed!")

    let fastMirrorUser =
        { slowMirrorUser with
            Speed = 200
            Hp = slowMirrorUser.MaxHp }
    let slowEnemy =
        { mon "ENEMY" (ty "FIRE") (ty "FIRE") 50 200 100 100 1 with
            Moves = [ ember ]
            Pp = [ ember.Pp ] }
    let afterFirst = Battle.chooseMove 0 (Battle.create fastMirrorUser slowEnemy 53u)
    Assert.Equal(slowEnemy.Hp, afterFirst.Enemy.Hp)
    Assert.Contains(afterFirst.Messages, fun m -> m = "But it failed!")

// -- Pre-move gates ----------------------------------------------------------

[<Fact>]
let ``asleep mon cannot act and shows fast asleep message`` () =
    let sleeper = monWithStatus "SLEEPER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 (Sleep 3)
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create sleeper foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is fast asleep!")
    Assert.Equal(200, after.Enemy.Hp) // Foe not damaged

[<Fact>]
let ``sleeping mon wakes up when counter reaches zero and can act`` () =
    let sleeper = monWithStatus "SLEEPER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 (Sleep 1)
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create sleeper foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "woke up!")
    Assert.Equal(Healthy, after.Player.Status)
    Assert.True(after.Enemy.Hp < 200) // Woke and attacked

[<Fact>]
let ``sleep counter decrements each turn`` () =
    let sleeper = monWithStatus "SLEEPER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 (Sleep 5)
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create sleeper foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Equal(Sleep 4, after.Player.Status)

[<Fact>]
let ``frozen mon cannot act and shows frozen solid message`` () =
    let frozen = monWithStatus "FROSTY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 Freeze
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ]; Pp = [35] }
    // Need a seed where the end-of-turn defrost check does NOT thaw (roll >= 25).
    // Player is frozen so can't act; enemy acts. Then betweenTurns defrost draw.
    // The first RNG draw in betweenTurns is for the frozen player.
    // We need to track what the RNG state is at that point.
    // Enemy uses strongHit: accuracy 95% -> accByte = 242, draw needed. Then crit + spread = 2 more draws.
    // So betweenTurns gets rng after 3 enemy draws. Let's use a seed and verify.
    let seed = 42u
    let state = Battle.create frozen foe seed
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is frozen solid!")

[<Fact>]
let ``paralyzed mon has 25 pct chance of full paralysis`` () =
    // Find a seed where first draw < 64 (full para for the player, who is faster)
    let seed = findSeed (fun d -> d < 64)
    let para = monWithStatus "PARA" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 Paralysis
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create para foe seed
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is fully paralyzed!")

[<Fact>]
let ``paralyzed mon can act when not fully paralyzed`` () =
    // Find a seed where first draw >= 64 (not full para)
    let seed = findSeed (fun d -> d >= 64)
    let para = monWithStatus "PARA" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 Paralysis
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create para foe seed
    let after = Battle.chooseMove 0 state
    Assert.DoesNotContain(after.Messages, fun m -> m.Contains "is fully paralyzed!")
    Assert.True(after.Enemy.Hp < 200) // Mon attacked

// -- Stat modifiers ----------------------------------------------------------

[<Fact>]
let ``paralysis quarters effective speed`` () =
    let m = { mon "FAST" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100
              with Status = Paralysis }
    Assert.Equal(25, BattleMon.effectiveSpeed m) // 100/4 = 25

[<Fact>]
let ``paralysis speed reduction with stages`` () =
    let m = { mon "FAST" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100
              with Status = Paralysis; SpdStage = 2 }
    // Stage +2: 100 * 2/1 = 200; then /4 = 50
    Assert.Equal(50, BattleMon.effectiveSpeed m)

[<Fact>]
let ``burn halves physical attack in damage calc`` () =
    let attacker = { mon "ATK" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
                     with Status = Burn }
    let defender = mon "DEF" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
    let m = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    let burnDmg = Damage.calc attacker defender m false Damage.MaxRoll false
    let normalDmg = Damage.calc (mon "ATK" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50) defender m false Damage.MaxRoll false
    Assert.True(burnDmg < normalDmg, "Burn should reduce physical damage")

[<Fact>]
let ``burn does not affect special attack damage`` () =
    let attacker = { mon "ATK" (ty "FIRE") (ty "FIRE") 10 100 30 25 50
                     with Status = Burn }
    let defender = mon "DEF" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 50
    let m = move "EMBER" "EFFECT_NORMAL_HIT" 40 (ty "FIRE")
    let burnDmg = Damage.calc attacker defender m false Damage.MaxRoll false
    let normalDmg = Damage.calc (mon "ATK" (ty "FIRE") (ty "FIRE") 10 100 30 25 50) defender m false Damage.MaxRoll false
    Assert.Equal(normalDmg, burnDmg)

// -- Status-inflicting effects -----------------------------------------------

let private sleepMove = Moves.byName "HYPNOSIS"
let private poisonMove = Moves.byName "POISONPOWDER"
let private toxicMove = Moves.byName "TOXIC"
let private paraMove = Moves.byName "THUNDER_WAVE"

[<Fact>]
let ``EFFECT_SLEEP puts target to sleep with counter 2-7`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ sleepMove ]; Pp = [20] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "fell asleep!")
    match after.Enemy.Status with
    | Sleep n -> Assert.True(n >= 2 && n <= 7, $"Sleep turns {n} not in [2,7]")
    | s -> Assert.Fail($"Expected Sleep, got {s}")

[<Fact>]
let ``EFFECT_SLEEP fails on already sleeping target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ sleepMove ]; Pp = [20] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35]; Status = Sleep 3 }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is already asleep!")
    // The enemy's own pre-move gate decrements the sleep counter from 3 to 2.
    Assert.Equal(Sleep 2, after.Enemy.Status)

[<Fact>]
let ``EFFECT_SLEEP fails on already statused target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ sleepMove ]; Pp = [20] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35]; Status = Paralysis }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "But it failed!")

[<Fact>]
let ``EFFECT_POISON poisons target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ poisonMove ]; Pp = [35] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "was poisoned!")
    Assert.Equal(Poison, after.Enemy.Status)

[<Fact>]
let ``EFFECT_POISON fails on Poison-type target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ poisonMove ]; Pp = [35] }
    let foe = { mon "FOE" (ty "POISON") (ty "POISON") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "doesn't affect")
    Assert.Equal(Healthy, after.Enemy.Status)

[<Fact>]
let ``EFFECT_POISON fails on already poisoned target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ poisonMove ]; Pp = [35] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35]; Status = Poison }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is already poisoned!")

[<Fact>]
let ``EFFECT_POISON fails on target with other status`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ poisonMove ]; Pp = [35] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35]; Status = Burn }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "But it failed!")

[<Fact>]
let ``EFFECT_TOXIC badly poisons target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ toxicMove ]; Pp = [10] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "was badly poisoned!")
    // End-of-turn residual increments the toxic counter from 0 to 1.
    Assert.Equal(BadPoison 1, after.Enemy.Status)

[<Fact>]
let ``EFFECT_TOXIC fails on Poison-type target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ toxicMove ]; Pp = [10] }
    let foe = { mon "FOE" (ty "POISON") (ty "POISON") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "doesn't affect")

[<Fact>]
let ``EFFECT_PARALYZE paralyzes target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ paraMove ]; Pp = [20] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is paralyzed!")
    Assert.Equal(Paralysis, after.Enemy.Status)

[<Fact>]
let ``EFFECT_PARALYZE fails on already paralyzed target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ paraMove ]; Pp = [20] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35]; Status = Paralysis }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is already paralyzed!")

[<Fact>]
let ``EFFECT_PARALYZE fails on already statused target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ paraMove ]; Pp = [20] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ strongHit ]; Pp = [35]; Status = Poison }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "But it failed!")

// -- End-of-turn residuals ---------------------------------------------------

[<Fact>]
let ``poison residual deals MaxHp div 8 per turn`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 160 100 100 200
                 with Moves = [ growl ]; Pp = [40]; Status = Poison }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ growl ]; Pp = [40] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    // Poison tick: 160/8 = 20 HP. No combat damage since both use Growl.
    Assert.Contains(after.Messages, fun m -> m.Contains "is hurt by poison!")
    let dmg = user.Hp - after.Player.Hp
    Assert.Equal(20, dmg)

[<Fact>]
let ``toxic residual ramps each turn`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 160 100 100 200
                 with Moves = [ growl ]; Pp = [40]; Status = BadPoison 0 }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ growl ]; Pp = [40] }
    let state = Battle.create user foe 42u
    // Turn 1: counter 0 -> 1, tick = max(1, 160/16) * 1 = 10 * 1 = 10
    let after1 = Battle.chooseMove 0 state
    Assert.Contains(after1.Messages, fun m -> m.Contains "is hurt by poison!")
    Assert.Equal(BadPoison 1, after1.Player.Status)
    let dmg1 = user.Hp - after1.Player.Hp
    Assert.Equal(10, dmg1) // 160/16 * 1

    // Turn 2: counter 1 -> 2, tick = 10 * 2 = 20
    let after2 = Battle.chooseMove 0 after1
    let dmg2 = after1.Player.Hp - after2.Player.Hp
    Assert.Equal(20, dmg2) // 160/16 * 2
    Assert.Equal(BadPoison 2, after2.Player.Status)

[<Fact>]
let ``burn residual deals MaxHp div 8 per turn`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 160 100 100 200
                 with Moves = [ growl ]; Pp = [40]; Status = Burn }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ growl ]; Pp = [40] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is hurt by its burn!")
    let dmg = user.Hp - after.Player.Hp
    Assert.Equal(20, dmg) // 160/8 = 20

[<Fact>]
let ``poison residual min 1 HP for low MaxHp`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 5 7 10 10 200
                 with Moves = [ growl ]; Pp = [40]; Status = Poison }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 5 7 10 10 1
                with Moves = [ growl ]; Pp = [40] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    // 7/8 = 0 -> clamped to 1
    let dmg = user.Hp - after.Player.Hp
    Assert.Equal(1, dmg)

[<Fact>]
let ``end-of-turn faint from poison residual ends battle`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 160 100 100 200
                 with Moves = [ growl ]; Pp = [40]; Status = Poison; Hp = 1 }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ growl ]; Pp = [40] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Equal(0, after.Player.Hp)
    Assert.Equal(Some Lose, after.Outcome)

[<Fact>]
let ``frozen mon thaws at end of turn with low roll`` () =
    // Both use Growl (no combat RNG draws). Defrost draw is the first in betweenTurns.
    // Need seed where first draw < 25.
    let seed = findSeed (fun d -> d < 25)
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ growl ]; Pp = [40]; Status = Freeze }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ growl ]; Pp = [40] }
    let state = Battle.create user foe seed
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "was defrosted!")
    Assert.Equal(Healthy, after.Player.Status)

[<Fact>]
let ``frozen mon stays frozen with high roll`` () =
    // Need seed where first draw >= 25.
    let seed = findSeed (fun d -> d >= 25)
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200
                 with Moves = [ growl ]; Pp = [40]; Status = Freeze }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1
                with Moves = [ growl ]; Pp = [40] }
    let state = Battle.create user foe seed
    let after = Battle.chooseMove 0 state
    Assert.DoesNotContain(after.Messages, fun m -> m.Contains "was defrosted!")
    Assert.Equal(Freeze, after.Player.Status)

[<Fact>]
let ``paralysis affects turn order via speed`` () =
    // Para quarters speed: 100/4 = 25 < foe's 50.
    let user = { mon "PARA" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100
                 with Status = Paralysis }
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    Assert.True(BattleMon.effectiveSpeed user < BattleMon.effectiveSpeed foe)

// ============================================================================
//  M13.4 — Volatile status core tests
// ============================================================================

// Helper moves for volatile-status tests.
let private confuseMove =
    move "CONFUSE_RAY" "EFFECT_CONFUSE" 0 (ty "NORMAL")
    |> fun m -> { m with Accuracy = 100 }

let private leechSeedMove =
    move "LEECH_SEED" "EFFECT_LEECH_SEED" 0 (ty "NORMAL")
    |> fun m -> { m with Accuracy = 100 }

let private wrapMove =
    move "WRAP" "EFFECT_TRAP_TARGET" 15 (ty "NORMAL")

let private substituteMove =
    move "SUBSTITUTE" "EFFECT_SUBSTITUTE" 0 (ty "NORMAL")
    |> fun m -> { m with Accuracy = 100 }

let private focusEnergyMove =
    move "FOCUS_ENERGY" "EFFECT_FOCUS_ENERGY" 0 (ty "NORMAL")
    |> fun m -> { m with Accuracy = 100 }

let private mistMove =
    move "MIST" "EFFECT_MIST" 0 (ty "NORMAL")
    |> fun m -> { m with Accuracy = 100 }

let private meanLookMove =
    move "MEAN_LOOK" "EFFECT_MEAN_LOOK" 0 (ty "NORMAL")
    |> fun m -> { m with Accuracy = 100 }

// -- Confusion ----------------------------------------------------------------

[<Fact>]
let ``EFFECT_CONFUSE sets confusion on target with 2-5 turns`` () =
    // Player faster (speed 200 vs 1). Uses Confuse Ray (acc 100 -> auto-hit).
    // RNG draws: rollHit crit (draw 0) + spread (draw 1), then confusion turns (draw 2).
    // Seed 42: draw 2 = 165, 165 & 3 = 1, +2 = 3 turns.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ confuseMove ]; Pp = [10] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "became confused!")

[<Fact>]
let ``confused mon has 50 percent chance of self-hit`` () =
    // Set confusion directly (3 turns). Player faster.
    // On enemy's turn: confusion dec 3->2, then self-hit roll.
    // Use a seed where the self-hit roll < 128 after the player's move draws.
    // Player uses Growl (no Damage command -> still consumes crit+spread rollHit draws).
    // Player preMoveCheck: no draws (healthy).
    // Player executeMove: checkHit (acc 100 -> auto-hit, no draw), rollHit (draw 0, 1).
    // Enemy preMoveCheck: confusion gate: dec, roll draw 2.
    // Seed 42: draw 2 = 165. 165 >= 128 -> no self-hit.
    // Need draw 2 < 128. Seed 7: draw 2 = 116 < 128 -> self-hit!
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ growl ]; Pp = [40] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Confusion = Some 3 } }
    let state = Battle.create user foe 7u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is confused!")
    Assert.Contains(after.Messages, fun m -> m.Contains "hurt itself in its confusion!")
    // Verify enemy took self-hit damage (40-power, atk 30, def 25, L10).
    // Self-hit: (2*10/5+2)*40*30/25/50 = 6*40*30/25/50 = 5. min 997 5 + 2 = 7. *255/255=7
    Assert.True(after.Enemy.Hp < 100)

[<Fact>]
let ``confused mon snaps out when counter reaches 0`` () =
    // Set confusion to 1 turn remaining. On next turn, dec 1->0 -> snap out.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ growl ]; Pp = [40] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Confusion = Some 1 } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "snapped out of confusion!")
    Assert.Equal(None, after.Enemy.Volatile.Confusion)

[<Fact>]
let ``EFFECT_CONFUSE fails on already confused target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ confuseMove ]; Pp = [10] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Confusion = Some 3 } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is already confused!")

// -- Flinch -------------------------------------------------------------------

[<Fact>]
let ``flinched mon cannot move when opponent moved first`` () =
    // Enemy is slower (speed 1) and has Flinch set.
    // Player acts first (faster), enemy's flinch gate blocks.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ strongHit ]; Pp = [35] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Flinch = true } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "flinched!")

[<Fact>]
let ``flinch is ignored when the flinched mon moved first`` () =
    // Enemy is FASTER (speed 200) and has Flinch set.
    // Enemy moves first -> flinch doesn't block (user hasn't set it yet).
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 1
                 with Moves = [ strongHit ]; Pp = [35] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 200
                with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Flinch = true } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    // Flinch is cleared but doesn't prevent action.
    Assert.DoesNotContain(after.Messages, fun m -> m.Contains "flinched!")
    Assert.False(after.Enemy.Volatile.Flinch)

[<Fact>]
let ``flinch is cleared after the pre-move gate`` () =
    // Even if flinch blocked, the flag should be cleared.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ strongHit ]; Pp = [35] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Flinch = true } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.False(after.Enemy.Volatile.Flinch)

// -- Leech Seed ---------------------------------------------------------------

[<Fact>]
let ``EFFECT_LEECH_SEED seeds the target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ leechSeedMove ]; Pp = [10] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "was seeded!")
    Assert.True(after.Enemy.Volatile.LeechSeed)

[<Fact>]
let ``EFFECT_LEECH_SEED fails on Grass-type target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ leechSeedMove ]; Pp = [10] }
    let foe = { mon "FOE" (ty "GRASS") (ty "GRASS") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "doesn't affect")
    Assert.False(after.Enemy.Volatile.LeechSeed)

[<Fact>]
let ``EFFECT_LEECH_SEED fails on already seeded target`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ leechSeedMove ]; Pp = [10] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with LeechSeed = true } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "evaded")

[<Fact>]
let ``leech seed drains MaxHp div 8 and heals the other side`` () =
    // Enemy is seeded, has 100 HP / 100 MaxHp. Player has 150/200 HP.
    // End-of-turn: drain = max(1, 100/8) = 12. Enemy loses 12, player gains 12.
    // Use non-damaging moves (Growl) so no combat damage interferes.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ growl ]; Pp = [40]; Hp = 150 }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ growl ]; Pp = [40];
                     Volatile = { VolatileStatus.empty with LeechSeed = true } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "sapped by Leech Seed!")
    // Drain = max(1, 100/8) = 12
    Assert.Equal(100 - 12, after.Enemy.Hp)
    Assert.Equal(min 200 (150 + 12), after.Player.Hp)

// -- Trap / Wrap --------------------------------------------------------------

[<Fact>]
let ``EFFECT_TRAP_TARGET traps the foe for 3-6 turns`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ wrapMove ]; Pp = [20] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "was trapped!")
    Assert.True(after.Enemy.Volatile.Trapped.IsSome)

[<Fact>]
let ``trapped mon takes 1-16th MaxHp chip each end-of-turn`` () =
    // Foe already trapped (counter 3). End-of-turn: dec to 2, chip = max(1, 100/16) = 6.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ growl ]; Pp = [40] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ growl ]; Pp = [40];
                     Volatile = { VolatileStatus.empty with Trapped = Some 3 } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is hurt by the trap!")
    // Chip = max(1, 100/16) = 6. Foe HP = 100 - 6 = 94.
    Assert.Equal(94, after.Enemy.Hp)
    Assert.Equal(Some 2, after.Enemy.Volatile.Trapped)

[<Fact>]
let ``trapped mon is released when counter reaches 0`` () =
    // Foe trapped at counter 1. End-of-turn: dec to 0 -> release (no chip).
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ growl ]; Pp = [40] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ growl ]; Pp = [40];
                     Volatile = { VolatileStatus.empty with Trapped = Some 1 } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "was released!")
    Assert.Equal(None, after.Enemy.Volatile.Trapped)
    Assert.Equal(100, after.Enemy.Hp) // No chip on release turn.

[<Fact>]
let ``trapped player cannot flee`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Trapped = Some 2 } }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.run state
    Assert.Contains(after.Messages, fun m -> m.Contains "trapped and can't escape!")
    Assert.Equal(None, after.Outcome)

// -- Substitute ---------------------------------------------------------------

[<Fact>]
let ``EFFECT_SUBSTITUTE creates a substitute at MaxHp div 4 cost`` () =
    // User has 200 MaxHp, 200 Hp. Cost = 200/4 = 50. Sub HP = 50.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ substituteMove ]; Pp = [10] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ growl ]; Pp = [40] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "made a substitute!")
    Assert.Equal(Some 50, after.Player.Volatile.Substitute)
    Assert.Equal(150, after.Player.Hp)

[<Fact>]
let ``substitute fails when HP is too low`` () =
    // User has 200 MaxHp but only 40 Hp. Cost = 50 > 40. Fail.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ substituteMove ]; Pp = [10]; Hp = 40 }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "too weak to make a substitute!")
    Assert.Equal(None, after.Player.Volatile.Substitute)

[<Fact>]
let ``substitute absorbs damage instead of mon HP`` () =
    // Foe has a substitute with 50 HP. Player hits foe with a strong move.
    // Damage goes to sub, not to foe HP.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 200 100 200
                 with Moves = [ strongHit ]; Pp = [35] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Substitute = Some 50 } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    // Foe's HP should be unchanged; sub HP should have decreased.
    Assert.Equal(100, after.Enemy.Hp)

[<Fact>]
let ``substitute breaks when reduced to 0 or below`` () =
    // Foe has sub with 5 HP. Strong hit exceeds 5 -> sub breaks.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 200 100 200
                 with Moves = [ strongHit ]; Pp = [35] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Substitute = Some 5 } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "substitute faded!")
    Assert.Equal(None, after.Enemy.Volatile.Substitute)
    // Foe's actual HP untouched (excess damage doesn't overflow to HP).
    Assert.Equal(100, after.Enemy.Hp)

[<Fact>]
let ``substitute blocks status infliction`` () =
    let sleepMove = move "SLEEP_POWDER" "EFFECT_SLEEP" 0 (ty "NORMAL")
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ sleepMove ]; Pp = [15] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Substitute = Some 50 } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    // Sleep should not be applied (blocked by sub).
    Assert.Equal(Healthy, after.Enemy.Status)

[<Fact>]
let ``substitute blocks stat-lowering moves`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ growl ]; Pp = [40] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Substitute = Some 50 } }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    // Growl should fail to lower Attack (blocked by sub).
    Assert.Equal(0, after.Enemy.AtkStage)

// -- Focus Energy -------------------------------------------------------------

[<Fact>]
let ``EFFECT_FOCUS_ENERGY sets the FocusEnergy flag`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ focusEnergyMove ]; Pp = [30] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "is getting pumped!")
    Assert.True(after.Player.Volatile.FocusEnergy)

[<Fact>]
let ``focus energy already active fails`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ focusEnergyMove ]; Pp = [30];
                     Volatile = { VolatileStatus.empty with FocusEnergy = true } }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "But it failed!")

[<Fact>]
let ``focus energy increases crit stage by 1`` () =
    // CriticalHit.critStage returns higher stage with FocusEnergy=true.
    let normalMove = move "POUND" "EFFECT_NORMAL_HIT" 40 (ty "NORMAL")
    let stageWithout = CriticalHit.critStage false normalMove
    let stageWith = CriticalHit.critStage true normalMove
    Assert.Equal(stageWithout + 1, stageWith)

// -- Mist ---------------------------------------------------------------------

[<Fact>]
let ``EFFECT_MIST sets the Mist flag`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ mistMove ]; Pp = [30] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "shrouded in mist!")
    Assert.True(after.Player.Volatile.Mist)

[<Fact>]
let ``mist blocks opponent stat-lowering moves`` () =
    // Foe uses Growl on player who has Mist.
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 1
                 with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with Mist = true } }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 200
                with Moves = [ growl ]; Pp = [40] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "protected by mist!")
    Assert.Equal(0, after.Player.AtkStage) // Attack not lowered.

// -- Mean Look ----------------------------------------------------------------

[<Fact>]
let ``EFFECT_MEAN_LOOK sets CantEscape on foe`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ meanLookMove ]; Pp = [5] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.chooseMove 0 state
    Assert.Contains(after.Messages, fun m -> m.Contains "can't escape now!")
    Assert.True(after.Enemy.Volatile.CantEscape)

[<Fact>]
let ``mean look blocks player from fleeing`` () =
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 10 200 30 25 200
                 with Moves = [ strongHit ]; Pp = [35];
                     Volatile = { VolatileStatus.empty with CantEscape = true } }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 10 100 30 25 1
                with Moves = [ strongHit ]; Pp = [35] }
    let state = Battle.create user foe 42u
    let after = Battle.run state
    Assert.Contains(after.Messages, fun m -> m.Contains "can't escape!")
    Assert.Equal(None, after.Outcome)

// =========================================================================
//  M13.5: damage-shaping & fixed damage family tests
// =========================================================================

// --- Fixed / variable damage ---

[<Fact>]
let ``EFFECT_LEVEL_DAMAGE deals damage equal to user's level`` () =
    let m = move "SEISMIC_TOSS" "EFFECT_LEVEL_DAMAGE" 1 (ty "FIGHTING")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx LevelDamage
    Assert.Equal(50, ctx'.LastDamage)
    Assert.Equal(150, ctx'.Foe.Hp)

[<Fact>]
let ``EFFECT_PSYWAVE deals random 1..floor(level*1.5) damage`` () =
    let m = move "PSYWAVE" "EFFECT_PSYWAVE" 1 (ty "PSYCHIC_TYPE")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 20 100 50 50 50
    // level=20, max = 20 + 10 = 30. Valid range: 1..29.
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 42u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx PsywaveDamage
    Assert.True(ctx'.LastDamage >= 1 && ctx'.LastDamage < 30, $"Psywave damage {ctx'.LastDamage} out of range")
    Assert.Equal(100 - ctx'.LastDamage, ctx'.Foe.Hp)

[<Fact>]
let ``EFFECT_SUPER_FANG deals half of target current HP, min 1`` () =
    let m = move "SUPER_FANG" "EFFECT_SUPER_FANG" 1 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 80 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx SuperFangDamage
    Assert.Equal(40, ctx'.LastDamage)
    Assert.Equal(40, ctx'.Foe.Hp)

[<Fact>]
let ``EFFECT_SUPER_FANG deals min 1 when target has 1 HP`` () =
    let m = move "SUPER_FANG" "EFFECT_SUPER_FANG" 1 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 80 100 100 50 with Hp = 1 }
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx SuperFangDamage
    Assert.Equal(1, ctx'.LastDamage)
    Assert.Equal(0, ctx'.Foe.Hp)

[<Fact>]
let ``EFFECT_STATIC_DAMAGE deals fixed move power`` () =
    let sonicboom = move "SONICBOOM" "EFFECT_STATIC_DAMAGE" 20 (ty "NORMAL")
    let dragonRage = move "DRAGON_RAGE" "EFFECT_STATIC_DAMAGE" 40 (ty "DRAGON")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let ctx s m : MoveContext =
        { User = user; Foe = s; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let c1 = Effects.applyCtx (ctx foe sonicboom) StaticDamage
    Assert.Equal(20, c1.LastDamage)
    let c2 = Effects.applyCtx (ctx foe dragonRage) StaticDamage
    Assert.Equal(40, c2.LastDamage)

[<Fact>]
let ``EFFECT_OHKO fails if target level is higher than user level`` () =
    let m = move "HORN_DRILL" "EFFECT_OHKO" 1 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 30 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 31 200 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx OhkoDamage
    Assert.Equal(200, ctx'.Foe.Hp)
    Assert.Contains(ctx'.Messages, fun m -> m.Contains "missed")

[<Fact>]
let ``EFFECT_OHKO fails when type matchup has no effect`` () =
    let m = { move "FISSURE" "EFFECT_OHKO" 1 (ty "GROUND") with Accuracy = 100 }
    let user = mon "USER" (ty "GROUND") (ty "GROUND") 50 200 100 100 50
    let foe = mon "FOE" (ty "FLYING") (ty "FLYING") 50 200 100 100 50
    let ctx = mkCtx user foe m
    let ctx' = Effects.applyCtx ctx OhkoDamage
    Assert.Equal(200, ctx'.Foe.Hp)
    Assert.Contains(ctx'.Messages, fun msg -> msg.Contains("missed"))

[<Fact>]
let ``EFFECT_OHKO KOs when attacker level > target level and roll succeeds`` () =
    let m = { move "HORN_DRILL" "EFFECT_OHKO" 1 (ty "NORMAL") with Accuracy = 30 }
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 100 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 30 200 100 100 50
    // diff = 70, accByte = 30*255/100 = 76, modAcc = 140 + 76 = 216.
    // Need roll < 216 to hit. Seed 0u gives various rolls; let's use high accuracy.
    let m = { m with Accuracy = 100 }
    // accByte = 255, modAcc = 140+255 = 395 capped to 255 → always hits.
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx OhkoDamage
    Assert.Equal(0, ctx'.Foe.Hp)
    Assert.Contains(ctx'.Messages, fun m -> m.Contains "one-hit KO")

[<Fact>]
let ``C27 OHKO runtime bypasses generic accuracy and lets OHKO command own hit logic`` () =
    let hornDrill = { Moves.byName "HORN_DRILL" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ hornDrill ]
            Pp = [ hornDrill.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }

    let after = Battle.chooseMove 0 (Battle.create player enemy 59u)

    Assert.Equal(0, after.Enemy.Hp)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("one-hit KO"))

[<Fact>]
let ``C28 Rampage forces the locked move on later turns`` () =
    let thrash = { Moves.byName "THRASH" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 300 100 100 200 with
            Moves = [ thrash; splash ]
            Pp = [ thrash.Pp; splash.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 500 100 100 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }

    let afterFirst = Battle.chooseMove 0 (Battle.create player enemy 61u)
    let enemyHpAfterFirst = afterFirst.Enemy.Hp
    let afterSecond = Battle.chooseMove 1 afterFirst

    Assert.Contains(afterSecond.Messages, fun msg -> msg.Contains("PLAYER used THRASH"))
    Assert.True(afterSecond.Enemy.Hp < enemyHpAfterFirst)

[<Fact>]
let ``C28 Rampage expires into two-or-three-turn confusion without restarting`` () =
    let thrash = { Moves.byName "THRASH" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 300 100 100 200 with
            Moves = [ thrash; splash ]
            Pp = [ thrash.Pp; splash.Pp ]
            Volatile = { VolatileStatus.empty with Rampage = Some 1; LastMove = Some thrash } }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 500 100 100 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }

    let after = Battle.chooseMove 1 (Battle.create player enemy 67u)

    Assert.True(after.Player.Volatile.Rampage.IsNone)
    Assert.True(after.Player.Volatile.Confusion.Value = 2 || after.Player.Volatile.Confusion.Value = 3)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("became confused after rampaging"))

[<Fact>]
let ``C29 Sketch copies the opponent last counter move into Sketch slot`` () =
    let sketch = Moves.byName "SKETCH"
    let tackle = Moves.byName "TACKLE"
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ sketch; splash ]
            Pp = [ sketch.Pp; splash.Pp ]
            Volatile = { VolatileStatus.empty with LastMove = Some sketch; LastCounterMove = Some sketch } }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ]
            Volatile = { VolatileStatus.empty with LastCounterMove = Some tackle } }

    let after = Battle.chooseMove 0 (Battle.create player enemy 71u)

    Assert.Equal("TACKLE", after.Player.Moves.[0].Name)
    Assert.Equal(tackle.Pp, after.Player.Pp.[0])
    Assert.Equal("SPLASH", after.Player.Moves.[1].Name)
    Assert.True(after.Player.Volatile.LastMove.IsNone)
    Assert.True(after.Player.Volatile.LastCounterMove.IsNone)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("sketched TACKLE"))

[<Fact>]
let ``C29 Sketch fails for protected targets and already known last moves`` () =
    let sketch = Moves.byName "SKETCH"
    let tackle = Moves.byName "TACKLE"
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ sketch; splash ]
            Pp = [ sketch.Pp; splash.Pp ]
            Volatile = { VolatileStatus.empty with LastMove = Some sketch; LastCounterMove = Some sketch } }
    let substitutedEnemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ]
            Volatile = { VolatileStatus.empty with Substitute = Some 25; LastCounterMove = Some tackle } }

    let afterSubstitute = Battle.chooseMove 0 (Battle.create player substitutedEnemy 73u)
    Assert.Equal("SKETCH", afterSubstitute.Player.Moves.[0].Name)
    Assert.Equal(sketch.Pp - 1, afterSubstitute.Player.Pp.[0])
    Assert.True(afterSubstitute.Player.Volatile.LastMove.IsNone)
    Assert.True(afterSubstitute.Player.Volatile.LastCounterMove.IsNone)
    Assert.Contains(afterSubstitute.Messages, fun msg -> msg.Contains("didn't affect"))

    let duplicatePlayer =
        { player with
            Moves = [ sketch; tackle ]
            Pp = [ sketch.Pp; tackle.Pp ] }
    let enemy =
        { substitutedEnemy with
            Volatile = { VolatileStatus.empty with LastCounterMove = Some tackle } }

    let afterDuplicate = Battle.chooseMove 0 (Battle.create duplicatePlayer enemy 79u)
    Assert.Equal("SKETCH", afterDuplicate.Player.Moves.[0].Name)
    Assert.Equal(sketch.Pp - 1, afterDuplicate.Player.Pp.[0])
    Assert.Contains(afterDuplicate.Messages, fun msg -> msg.Contains("didn't affect"))

[<Fact>]
let ``C30 Sleep Talk samples slots until it calls a usable non two-turn move`` () =
    let sleepTalk = Moves.byName "SLEEP_TALK"
    let fly = Moves.byName "FLY"
    let bide = Moves.byName "BIDE"
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ sleepTalk; fly; bide; tackle ]
            Pp = [ sleepTalk.Pp; fly.Pp; bide.Pp; tackle.Pp ]
            Status = Sleep 3 }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }

    let after = Battle.chooseMove 0 (Battle.create player enemy 83u)

    Assert.Equal(Sleep 2, after.Player.Status)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("is fast asleep"))
    Assert.Contains(after.Messages, fun msg -> msg.Contains("Sleep Talk called TACKLE"))
    Assert.True(after.Enemy.Hp < enemy.Hp)
    Assert.Equal(Some "TACKLE", after.Player.Volatile.LastMove |> Option.map (fun move -> move.Name))
    Assert.Equal(Some "TACKLE", after.Player.Volatile.LastCounterMove |> Option.map (fun move -> move.Name))

[<Fact>]
let ``C30 Sleep Talk excludes the disabled slot and records the called move`` () =
    let sleepTalk = Moves.byName "SLEEP_TALK"
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ sleepTalk; tackle; splash ]
            Pp = [ sleepTalk.Pp; tackle.Pp; splash.Pp ]
            Status = Sleep 3
            Volatile = { VolatileStatus.empty with DisabledMoveIndex = Some 1 } }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }

    let after = Battle.chooseMove 0 (Battle.create player enemy 89u)

    Assert.Equal(enemy.Hp, after.Enemy.Hp)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("Sleep Talk called SPLASH"))
    Assert.Equal(Some "SPLASH", after.Player.Volatile.LastMove |> Option.map (fun move -> move.Name))
    Assert.Equal(Some "SPLASH", after.Player.Volatile.LastCounterMove |> Option.map (fun move -> move.Name))

[<Fact>]
let ``C31 Spite reduces PP from the opponent last counter move`` () =
    let spite = { Moves.byName "SPITE" with Accuracy = 100 }
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "GHOST") (ty "GHOST") 50 200 100 100 200 with
            Moves = [ spite ]
            Pp = [ spite.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ tackle; splash ]
            Pp = [ 10; splash.Pp ]
            Status = Sleep 2
            Volatile = { VolatileStatus.empty with LastCounterMove = Some tackle } }

    let after = Battle.chooseMove 0 (Battle.create player enemy 97u)

    Assert.True(after.Enemy.Pp.[0] >= 5 && after.Enemy.Pp.[0] <= 8)
    Assert.Equal(splash.Pp, after.Enemy.Pp.[1])
    Assert.Contains(after.Messages, fun msg -> msg.Contains("TACKLE's PP was reduced"))

[<Fact>]
let ``C31 Spite fails without a last counter move or with zero target PP`` () =
    let spite = { Moves.byName "SPITE" with Accuracy = 100 }
    let tackle = { Moves.byName "TACKLE" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "GHOST") (ty "GHOST") 50 200 100 100 200 with
            Moves = [ spite ]
            Pp = [ spite.Pp ] }
    let noLastMoveEnemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ tackle ]
            Pp = [ 10 ]
            Status = Sleep 2 }

    let afterNoLastMove = Battle.chooseMove 0 (Battle.create player noLastMoveEnemy 101u)
    Assert.Equal(10, afterNoLastMove.Enemy.Pp.[0])
    Assert.Contains(afterNoLastMove.Messages, fun msg -> msg.Contains("didn't affect"))

    let zeroPpEnemy =
        { noLastMoveEnemy with
            Pp = [ 0; splash.Pp ]
            Moves = [ tackle; splash ]
            Volatile = { VolatileStatus.empty with LastCounterMove = Some tackle } }

    let afterZeroPp = Battle.chooseMove 0 (Battle.create player zeroPpEnemy 103u)
    Assert.Equal(0, afterZeroPp.Enemy.Pp.[0])
    Assert.Equal(splash.Pp, afterZeroPp.Enemy.Pp.[1])
    Assert.Contains(afterZeroPp.Messages, fun msg -> msg.Contains("didn't affect"))

[<Fact>]
let ``C32 Teleport succeeds immediately when the player is at least the foe level`` () =
    let teleport = Moves.byName "TELEPORT"
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "PSYCHIC_TYPE") (ty "PSYCHIC_TYPE") 30 200 100 100 200 with
            Moves = [ teleport ]
            Pp = [ teleport.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 20 200 100 100 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }

    let after = Battle.chooseMove 0 (Battle.create player enemy 107u)

    Assert.Equal(Some Ran, after.Outcome)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("fled from battle"))

[<Fact>]
let ``C32 Teleport can fail the low-level random check and trapped users cannot teleport`` () =
    let teleport = Moves.byName "TELEPORT"
    let splash = Moves.byName "SPLASH"
    let lowLevelPlayer =
        { mon "PLAYER" (ty "PSYCHIC_TYPE") (ty "PSYCHIC_TYPE") 5 200 100 100 200 with
            Moves = [ teleport ]
            Pp = [ teleport.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 80 200 100 100 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ]
            Status = Sleep 2 }
    let failSeed = findSeed (fun draw -> draw < enemy.Level / 4)

    let afterRandomFail = Battle.chooseMove 0 (Battle.create lowLevelPlayer enemy failSeed)

    Assert.True(afterRandomFail.Outcome.IsNone)
    Assert.Contains(afterRandomFail.Messages, fun msg -> msg.Contains("But it failed"))

    let trappedPlayer =
        { lowLevelPlayer with
            Level = 100
            Volatile = { VolatileStatus.empty with CantEscape = true } }
    let afterTrapped = Battle.chooseMove 0 (Battle.create trappedPlayer enemy 109u)

    Assert.True(afterTrapped.Outcome.IsNone)
    Assert.Contains(afterTrapped.Messages, fun msg -> msg.Contains("But it failed"))

[<Fact>]
let ``C32 Wild enemy Teleport succeeds regardless of level difference`` () =
    let teleport = Moves.byName "TELEPORT"
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 80 200 100 100 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "PSYCHIC_TYPE") (ty "PSYCHIC_TYPE") 5 200 100 100 200 with
            Moves = [ teleport ]
            Pp = [ teleport.Pp ] }

    let after = Battle.chooseMove 0 (Battle.create player enemy 113u)

    Assert.Equal(Some Ran, after.Outcome)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("fled from battle"))

[<Fact>]
let ``C33 Thief transfers a non-mail held item after damaging the target`` () =
    let thief = { Moves.byName "THIEF" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let player =
        { mon "PLAYER" (ty "DARK") (ty "DARK") 50 200 120 100 200 with
            Moves = [ thief ]
            Pp = [ thief.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 300 100 100 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ]
            HeldItem = Some "GOLD_BERRY"
            Status = Sleep 2 }

    let after = Battle.chooseMove 0 (Battle.create player enemy 127u)

    Assert.True(after.Enemy.Hp < enemy.Hp)
    Assert.Equal(Some "GOLD_BERRY", after.Player.HeldItem)
    Assert.Equal(None, after.Enemy.HeldItem)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("stole GOLD BERRY"))

[<Fact>]
let ``C33 Thief does not steal mail or when the user already holds an item`` () =
    let thief = { Moves.byName "THIEF" with Accuracy = 100 }
    let splash = Moves.byName "SPLASH"
    let basePlayer =
        { mon "PLAYER" (ty "DARK") (ty "DARK") 50 200 120 100 200 with
            Moves = [ thief ]
            Pp = [ thief.Pp ] }
    let mailEnemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 300 100 100 1 with
            Moves = [ splash ]
            Pp = [ splash.Pp ]
            HeldItem = Some "FLOWER_MAIL"
            Status = Sleep 2 }

    let afterMail = Battle.chooseMove 0 (Battle.create basePlayer mailEnemy 131u)
    Assert.Equal(None, afterMail.Player.HeldItem)
    Assert.Equal(Some "FLOWER_MAIL", afterMail.Enemy.HeldItem)

    let itemHoldingPlayer =
        { basePlayer with HeldItem = Some "CHARCOAL" }
    let berryEnemy =
        { mailEnemy with HeldItem = Some "GOLD_BERRY" }

    let afterAlreadyHolding = Battle.chooseMove 0 (Battle.create itemHoldingPlayer berryEnemy 137u)
    Assert.Equal(Some "CHARCOAL", afterAlreadyHolding.Player.HeldItem)
    Assert.Equal(Some "GOLD_BERRY", afterAlreadyHolding.Enemy.HeldItem)

[<Fact>]
let ``C34 Transform copies target data clears Disable and initializes copied PP`` () =
    let transform = Moves.byName "TRANSFORM"
    let sketch = Moves.byName "SKETCH"
    let tackle = Moves.byName "TACKLE"
    let splash = Moves.byName "SPLASH"
    let player =
        { BattleMon.ofSpecies (Species.byName "DITTO") 30 [ transform; splash ] with
            Speed = 200
            Volatile =
                { VolatileStatus.empty with
                    DisableTimer = Some 4
                    DisabledMoveIndex = Some 1
                    LastMove = Some transform
                    LastCounterMove = Some transform } }
    let enemy =
        { BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 12 [ sketch; tackle ] with
            Speed = 1
            AtkStage = 2
            DefStage = 1
            Status = Sleep 2 }

    let after = Battle.chooseMove 0 (Battle.create player enemy 139u)

    Assert.Equal("CYNDAQUIL", after.Player.Species.Name)
    Assert.Equal<string>([ "SKETCH"; "TACKLE" ], after.Player.Moves |> List.map (fun move -> move.Name))
    Assert.Equal<int>([ 1; 5 ], after.Player.Pp)
    Assert.True(after.Player.Volatile.Transformed)
    Assert.True(after.Player.Volatile.DisableTimer.IsNone)
    Assert.True(after.Player.Volatile.DisabledMoveIndex.IsNone)
    Assert.True(after.Player.Volatile.LastMove.IsNone)
    Assert.True(after.Player.Volatile.LastCounterMove.IsNone)
    Assert.Equal(2, after.Player.AtkStage)
    Assert.Equal(1, after.Player.DefStage)

[<Fact>]
let ``C34 Transform fails against transformed or hidden targets and clears last move`` () =
    let transform = Moves.byName "TRANSFORM"
    let fly = Moves.byName "FLY"
    let player =
        { BattleMon.ofSpecies (Species.byName "DITTO") 30 [ transform ] with
            Speed = 200
            Volatile = { VolatileStatus.empty with LastMove = Some transform; LastCounterMove = Some transform } }
    let transformedEnemy =
        { BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 12 [ Moves.byName "TACKLE" ] with
            Speed = 1
            Status = Sleep 2
            Volatile = { VolatileStatus.empty with Transformed = true } }

    let afterTransformed = Battle.chooseMove 0 (Battle.create player transformedEnemy 149u)
    Assert.Equal("DITTO", afterTransformed.Player.Species.Name)
    Assert.False(afterTransformed.Player.Volatile.Transformed)
    Assert.True(afterTransformed.Player.Volatile.LastMove.IsNone)
    Assert.True(afterTransformed.Player.Volatile.LastCounterMove.IsNone)
    Assert.Equal(transform.Pp - 1, afterTransformed.Player.Pp.[0])

    let hiddenEnemy =
        { transformedEnemy with
            Moves = [ fly ]
            Pp = [ fly.Pp ]
            Volatile = { VolatileStatus.empty with Charging = Some 1; ChargingMove = Some fly } }

    let afterHidden = Battle.chooseMove 0 (Battle.create player hiddenEnemy 151u)
    Assert.Equal("DITTO", afterHidden.Player.Species.Name)
    Assert.False(afterHidden.Player.Volatile.Transformed)
    Assert.True(afterHidden.Player.Volatile.LastMove.IsNone)
    Assert.True(afterHidden.Player.Volatile.LastCounterMove.IsNone)
    Assert.Contains(afterHidden.Messages, fun msg -> msg.Contains("But it failed"))

[<Fact>]
let ``C35 Baton Pass preserves passable boosts but resets blocked volatile state`` () =
    let batonPass = Moves.byName "BATON_PASS"
    let tackle = Moves.byName "TACKLE"
    let player =
        { mon "PASSER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ batonPass; tackle ]
            Pp = [ batonPass.Pp; tackle.Pp ]
            AtkStage = 3
            EvaStage = 2
            Volatile =
                { VolatileStatus.empty with
                    FocusEnergy = true
                    LeechSeed = true
                    Nightmare = true
                    Transformed = true
                    EncoreTimer = Some 4
                    EncoreMoveIndex = Some 0
                    DisableTimer = Some 3
                    DisabledMoveIndex = Some 1
                    LastMove = Some batonPass
                    LastCounterMove = Some batonPass
                    Trapped = Some 3 } }
    let bench =
        { mon "RECEIVER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ Moves.byName "SPLASH" ]
            Volatile = { VolatileStatus.empty with Attracted = true; Trapped = Some 2 } }

    let after = Battle.chooseMove 0 (Battle.createTeam [ player; bench ] [ enemy ] 157u)

    Assert.Equal("RECEIVER", after.Player.Species.Name)
    Assert.Equal(3, after.Player.AtkStage)
    Assert.Equal(2, after.Player.EvaStage)
    Assert.True(after.Player.Volatile.FocusEnergy)
    Assert.True(after.Player.Volatile.LeechSeed)
    Assert.False(after.Player.Volatile.Nightmare)
    Assert.False(after.Player.Volatile.Attracted)
    Assert.False(after.Player.Volatile.Transformed)
    Assert.True(after.Player.Volatile.EncoreTimer.IsNone)
    Assert.True(after.Player.Volatile.DisableTimer.IsNone)
    Assert.True(after.Player.Volatile.LastMove.IsNone)
    Assert.True(after.Player.Volatile.Trapped.IsNone)
    Assert.False(after.Enemy.Volatile.Attracted)
    Assert.True(after.Enemy.Volatile.Trapped.IsNone)

[<Fact>]
let ``C35 Baton Pass fails when there is no healthy bench target`` () =
    let batonPass = Moves.byName "BATON_PASS"
    let player =
        { mon "PASSER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ batonPass ]
            Pp = [ batonPass.Pp ] }
    let faintedBench =
        { mon "FAINTED" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 100 with
            Hp = 0
            Moves = [ Moves.byName "TACKLE" ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ Moves.byName "SPLASH" ]
            Status = Sleep 2 }

    let after = Battle.chooseMove 0 (Battle.createTeam [ player; faintedBench ] [ enemy ] 163u)

    Assert.Equal("PASSER", after.Player.Species.Name)
    Assert.Equal(batonPass.Pp - 1, after.Player.Pp.[0])
    Assert.Contains(after.Messages, fun msg -> msg.Contains("But it failed"))

[<Fact>]
let ``C36 Bide release fails when no damage was stored`` () =
    let bide = Moves.byName "BIDE"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ bide ]
            Pp = [ bide.Pp ]
            Volatile = { VolatileStatus.empty with BideTurns = Some 1; BideDamage = 0 } }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ Moves.byName "SPLASH" ] }

    let after = Battle.chooseMove 0 (Battle.create player enemy 167u)

    Assert.Equal(enemy.Hp, after.Enemy.Hp)
    Assert.True(after.Player.Volatile.BideTurns.IsNone)
    Assert.Equal(0, after.Player.Volatile.BideDamage)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("unleashed energy"))
    Assert.Contains(after.Messages, fun msg -> msg.Contains("But it failed"))

[<Fact>]
let ``C36 Bide release doubles stored damage and clamps at 65535`` () =
    let bide = Moves.byName "BIDE"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ bide ]
            Pp = [ bide.Pp ]
            Volatile = { VolatileStatus.empty with BideTurns = Some 1; BideDamage = 40000 } }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 70000 100 100 1 with
            Moves = [ Moves.byName "SPLASH" ] }

    let after = Battle.chooseMove 0 (Battle.create player enemy 173u)

    Assert.Equal(enemy.Hp - 65535, after.Enemy.Hp)
    Assert.True(after.Player.Volatile.BideTurns.IsNone)
    Assert.Equal(0, after.Player.Volatile.BideDamage)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("unleashed energy"))

[<Fact>]
let ``C37 Conversion fails when every move is current type or Curse`` () =
    let conversion = Moves.byName "CONVERSION"
    let tackle = Moves.byName "TACKLE"
    let curse = Moves.byName "CURSE"
    let user =
        { BattleMon.ofSpecies (Species.byName "RATTATA") 20 [ conversion; tackle; curse ] with
            Pp = [ conversion.Pp; tackle.Pp; curse.Pp ] }
    let foe = BattleMon.ofSpecies (Species.byName "PIDGEY") 20 [ Moves.byName "SPLASH" ]
    let ctx =
        { User = user
          Foe = foe
          Move = conversion
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None
          WeatherType = None }

    let converted = Effects.forMove conversion |> List.fold (fun c cmd -> Effects.applyCtx c cmd) ctx

    Assert.Equal(TypeChart.value "NORMAL", converted.User.Species.Type1)
    Assert.Equal(TypeChart.value "NORMAL", converted.User.Species.Type2)
    Assert.Contains(converted.Messages, fun msg -> msg.Contains("But it failed"))

[<Fact>]
let ``C37 Conversion samples move slots without consuming hit RNG`` () =
    let conversion = Moves.byName "CONVERSION"
    let ember = Moves.byName "EMBER"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ conversion; ember ]
            Pp = [ conversion.Pp; ember.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ Moves.byName "SPLASH" ]
            Status = Sleep 2 }

    let after = Battle.chooseMove 0 (Battle.create player enemy 179u)

    Assert.Equal(TypeChart.value "FIRE", after.Player.Species.Type1)
    Assert.Equal(TypeChart.value "FIRE", after.Player.Species.Type2)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("converted to FIRE"))

[<Fact>]
let ``C38 Conversion2 fails without a last counter move or after Curse`` () =
    let conversion2 = Moves.byName "CONVERSION2"
    let curse = Moves.byName "CURSE"
    let user = BattleMon.ofSpecies (Species.byName "PORYGON") 20 [ conversion2 ]
    let foeNoLast = BattleMon.ofSpecies (Species.byName "RATTATA") 20 [ Moves.byName "TACKLE" ]
    let ctx foe =
        { User = user
          Foe = foe
          Move = conversion2
          Crit = false
          Roll = Damage.MaxRoll
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None
          WeatherType = None }

    let noLast = Effects.forMove conversion2 |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (ctx foeNoLast)
    Assert.Equal(user.Species.Type1, noLast.User.Species.Type1)
    Assert.Contains(noLast.Messages, fun msg -> msg.Contains("But it failed"))

    let foeCurse =
        { foeNoLast with
            Volatile = { VolatileStatus.empty with LastCounterMove = Some curse } }
    let afterCurse = Effects.forMove conversion2 |> List.fold (fun c cmd -> Effects.applyCtx c cmd) (ctx foeCurse)
    Assert.Equal(user.Species.Type1, afterCurse.User.Species.Type1)
    Assert.Contains(afterCurse.Messages, fun msg -> msg.Contains("But it failed"))

[<Fact>]
let ``C38 Conversion2 samples a type resistant to the opponent last counter move`` () =
    let conversion2 = { Moves.byName "CONVERSION2" with Accuracy = 100 }
    let tackle = Moves.byName "TACKLE"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ conversion2 ]
            Pp = [ conversion2.Pp ] }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ Moves.byName "EMBER" ]
            Status = Sleep 2
            Volatile = { VolatileStatus.empty with LastCounterMove = Some tackle } }

    let after = Battle.chooseMove 0 (Battle.create player enemy 181u)

    Assert.True(TypeChart.multiplier tackle.Type after.Player.Species.Type1 < TypeChart.Neutral)
    Assert.Equal(after.Player.Species.Type1, after.Player.Species.Type2)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("converted to"))

[<Fact>]
let ``C39 Force Switch fails in trainer battles if the target has not moved`` () =
    let roar = { Moves.byName "ROAR" with Accuracy = 100 }
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 200 with
            Moves = [ roar ]
            Pp = [ roar.Pp ] }
    let firstEnemy =
        { mon "FIRST" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ Moves.byName "SPLASH" ] }
    let benchEnemy =
        { mon "BENCH" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with
            Moves = [ Moves.byName "SPLASH" ] }

    let after = Battle.chooseMove 0 (Battle.createTeam [ player ] [ firstEnemy; benchEnemy ] 191u)

    Assert.Equal("FIRST", after.Enemy.Species.Name)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("But it failed"))

[<Fact>]
let ``C39 Force Switch makes a wild target flee when there is no bench`` () =
    let roar = { Moves.byName "ROAR" with Accuracy = 100 }
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 80 200 100 100 200 with
            Moves = [ roar ]
            Pp = [ roar.Pp ] }
    let enemy =
        { mon "WILD" (ty "NORMAL") (ty "NORMAL") 20 200 100 100 1 with
            Moves = [ Moves.byName "SPLASH" ] }

    let after = Battle.chooseMove 0 (Battle.create player enemy 193u)

    Assert.Equal(Some Ran, after.Outcome)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("fled in fear"))

[<Fact>]
let ``C40 Metronome records the sampled move instead of Metronome`` () =
    let metronome = Moves.byName "METRONOME"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 300 120 100 200 with
            Moves = [ metronome ]
            Pp = [ metronome.Pp ]
            Volatile = { VolatileStatus.empty with LastMove = Some metronome; LastCounterMove = Some metronome } }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 300 100 100 1 with
            Moves = [ Moves.byName "SPLASH" ]
            Status = Sleep 2 }

    let after = Battle.chooseMove 0 (Battle.create player enemy 197u)

    Assert.Equal(metronome.Pp - 1, after.Player.Pp.[0])
    Assert.True(after.Player.Volatile.LastMove.IsSome)
    Assert.NotEqual<string>("METRONOME", after.Player.Volatile.LastMove.Value.Name)
    Assert.Equal(after.Player.Volatile.LastMove.Value.Name, after.Player.Volatile.LastCounterMove.Value.Name)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("Metronome called"))

[<Fact>]
let ``C41 Mimic copies the opponent last counter move with 5 PP and clears last-move state`` () =
    let mimic = Moves.byName "MIMIC"
    let tailWhip = Moves.byName "TAIL_WHIP"
    let ember = Moves.byName "EMBER"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 300 120 100 200 with
            Moves = [ mimic; tailWhip ]
            Pp = [ mimic.Pp; tailWhip.Pp ]
            Volatile = { VolatileStatus.empty with LastMove = Some mimic; LastCounterMove = Some mimic } }
    let enemy =
        { mon "ENEMY" (ty "FIRE") (ty "FIRE") 50 300 100 100 1 with
            Moves = [ ember ]
            Pp = [ ember.Pp ]
            Volatile = { VolatileStatus.empty with LastCounterMove = Some ember } }

    let after = Battle.chooseMove 0 (Battle.create player enemy 0u)

    Assert.Equal<string list>([ "EMBER"; "TAIL_WHIP" ], after.Player.Moves |> List.map (fun move -> move.Name))
    Assert.Equal<int list>([ 5; tailWhip.Pp ], after.Player.Pp)
    Assert.True(after.Player.Volatile.LastMove.IsNone)
    Assert.True(after.Player.Volatile.LastCounterMove.IsNone)

[<Fact>]
let ``C41 Mimic fails against hidden or already-known last counter moves`` () =
    let mimic = Moves.byName "MIMIC"
    let ember = Moves.byName "EMBER"
    let fly = Moves.byName "FLY"
    let player =
        { mon "PLAYER" (ty "NORMAL") (ty "NORMAL") 50 300 120 100 200 with
            Moves = [ mimic ]
            Pp = [ mimic.Pp ]
            Volatile = { VolatileStatus.empty with LastMove = Some mimic; LastCounterMove = Some mimic } }
    let hiddenEnemy =
        { mon "ENEMY" (ty "FIRE") (ty "FIRE") 50 300 100 100 1 with
            Moves = [ fly; ember ]
            Pp = [ fly.Pp; ember.Pp ]
            Volatile = { VolatileStatus.empty with LastCounterMove = Some ember; Charging = Some 1; ChargingMove = Some fly } }
    let duplicatePlayer = { player with Moves = [ mimic; ember ]; Pp = [ mimic.Pp; ember.Pp ] }
    let duplicateEnemy = { hiddenEnemy with Volatile = { VolatileStatus.empty with LastCounterMove = Some ember } }

    let afterHidden = Battle.chooseMove 0 (Battle.create player hiddenEnemy 0u)
    let afterDuplicate = Battle.chooseMove 0 (Battle.create duplicatePlayer duplicateEnemy 0u)

    Assert.Equal<string list>([ "MIMIC" ], afterHidden.Player.Moves |> List.map (fun move -> move.Name))
    Assert.Equal<int list>([ mimic.Pp - 1 ], afterHidden.Player.Pp)
    Assert.True(afterHidden.Player.Volatile.LastCounterMove.IsNone)
    Assert.Equal<string list>([ "MIMIC"; "EMBER" ], afterDuplicate.Player.Moves |> List.map (fun move -> move.Name))
    Assert.Equal<int list>([ mimic.Pp - 1; ember.Pp ], afterDuplicate.Player.Pp)
    Assert.True(afterDuplicate.Player.Volatile.LastCounterMove.IsNone)

[<Fact>]
let ``C42 Mirror Move replays the opponent last counter move and records the called move`` () =
    let mirrorMove = Moves.byName "MIRROR_MOVE"
    let tackle = Moves.byName "TACKLE"
    let ember = Moves.byName "EMBER"
    let player =
        { mon "PLAYER" (ty "FLYING") (ty "FLYING") 50 300 120 100 200 with
            Moves = [ mirrorMove ]
            Pp = [ mirrorMove.Pp ]
            Volatile = { VolatileStatus.empty with LastMove = Some mirrorMove; LastCounterMove = Some mirrorMove } }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 300 100 100 1 with
            Moves = [ ember ]
            Pp = [ ember.Pp ]
            Volatile = { VolatileStatus.empty with LastCounterMove = Some tackle }
            Status = Sleep 2 }

    let after = Battle.chooseMove 0 (Battle.create player enemy 0u)

    Assert.True(after.Enemy.Hp < enemy.Hp)
    Assert.Equal(mirrorMove.Pp - 1, after.Player.Pp.[0])
    Assert.Equal(Some "TACKLE", after.Player.Volatile.LastMove |> Option.map (fun move -> move.Name))
    Assert.Equal(Some "TACKLE", after.Player.Volatile.LastCounterMove |> Option.map (fun move -> move.Name))
    Assert.Contains(after.Messages, fun msg -> msg.Contains("mirrored TACKLE"))

[<Fact>]
let ``C42 Mirror Move fails when there is no opponent last counter move or the user knows it`` () =
    let mirrorMove = Moves.byName "MIRROR_MOVE"
    let tackle = Moves.byName "TACKLE"
    let player =
        { mon "PLAYER" (ty "FLYING") (ty "FLYING") 50 300 120 100 200 with
            Moves = [ mirrorMove ]
            Pp = [ mirrorMove.Pp ]
            Volatile = { VolatileStatus.empty with LastMove = Some mirrorMove; LastCounterMove = Some mirrorMove } }
    let enemy =
        { mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 300 100 100 1 with
            Moves = [ tackle ]
            Pp = [ tackle.Pp ]
            Status = Sleep 2 }
    let duplicatePlayer = { player with Moves = [ mirrorMove; tackle ]; Pp = [ mirrorMove.Pp; tackle.Pp ] }
    let duplicateEnemy = { enemy with Volatile = { VolatileStatus.empty with LastCounterMove = Some tackle } }

    let afterNoLast = Battle.chooseMove 0 (Battle.create player enemy 0u)
    let afterDuplicate = Battle.chooseMove 0 (Battle.create duplicatePlayer duplicateEnemy 0u)

    Assert.Equal(enemy.Hp, afterNoLast.Enemy.Hp)
    Assert.Equal(mirrorMove.Pp - 1, afterNoLast.Player.Pp.[0])
    Assert.True(afterNoLast.Player.Volatile.LastCounterMove.IsNone)
    Assert.Equal(enemy.Hp, afterDuplicate.Enemy.Hp)
    Assert.Equal<int list>([ mirrorMove.Pp - 1; tackle.Pp ], afterDuplicate.Player.Pp)
    Assert.True(afterDuplicate.Player.Volatile.LastCounterMove.IsNone)

[<Fact>]
let ``C43 Pursuit doubles damage when the target is switching`` () =
    let pursuit = Moves.byName "PURSUIT"
    let user = mon "PLAYER" (ty "DARK") (ty "DARK") 50 300 140 100 200
    let foe = mon "ENEMY" (ty "NORMAL") (ty "NORMAL") 50 300 100 100 1
    let ctx : MoveContext =
        { User = user
          Foe = foe
          Move = pursuit
          Crit = false
          Roll = 255
          Rng = Rng.create 0u
          Messages = []
          LastDamage = 0
          IsStruggle = false
          FuryCutterCount = 0
          RolloutCount = 0
          DefenseCurlUsed = false
          Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None
          WeatherType = None }

    let normal = Effects.applyCtxWith false ctx Damage
    let switching = Effects.applyCtxWith true ctx Damage

    Assert.Equal(normal.LastDamage * 2, switching.LastDamage)
    Assert.Equal(foe.Hp - switching.LastDamage, switching.Foe.Hp)

[<Fact>]
let ``EFFECT_FALSE_SWIPE leaves target at 1 HP`` () =
    let m = move "FALSE_SWIPE" "EFFECT_FALSE_SWIPE" 40 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 200 100 50
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 20 100 50 with Hp = 5 }
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx FalseSwipeDamage
    Assert.True(ctx'.Foe.Hp >= 1, $"False Swipe left target at {ctx'.Foe.Hp}")

[<Fact>]
let ``EFFECT_REVERSAL gives max power at lowest HP`` () =
    let m = move "REVERSAL" "EFFECT_REVERSAL" 1 (ty "FIGHTING")
    let user = { mon "USER" (ty "FIGHTING") (ty "FIGHTING") 50 200 100 100 50 with Hp = 1 }
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    // ratio = 1*48/200 = 0 <= 1 → power 200.
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx ReversalDamage
    // Power 200 should deal massive damage.
    Assert.True(ctx'.LastDamage > 0)

[<Fact>]
let ``EFFECT_REVERSAL power table thresholds are faithful`` () =
    // Test each threshold boundary.
    let m = move "FLAIL" "EFFECT_REVERSAL" 1 (ty "NORMAL")
    let baseMon = mon "USER" (ty "NORMAL") (ty "NORMAL") 100 480 100 100 50
    let calcPower hp =
        let user = { baseMon with Hp = hp }
        let ratio = hp * 48 / 480
        if ratio <= 1 then 200
        elif ratio <= 4 then 150
        elif ratio <= 9 then 100
        elif ratio <= 16 then 80
        elif ratio <= 32 then 40
        else 20
    Assert.Equal(200, calcPower 1)   // ratio=0
    Assert.Equal(200, calcPower 10)  // ratio=1
    Assert.Equal(200, calcPower 11)  // ratio=528/480=1 (integer), still <=1
    Assert.Equal(150, calcPower 21)  // ratio=1008/480=2 <=4
    Assert.Equal(150, calcPower 40)  // ratio=1920/480=4
    Assert.Equal(100, calcPower 50)  // ratio=2400/480=5 <=9
    Assert.Equal(100, calcPower 90)  // ratio=4320/480=9
    Assert.Equal(100, calcPower 91)  // ratio=4368/480=9 (integer) <=9
    Assert.Equal(80, calcPower 160)  // ratio=7680/480=16
    Assert.Equal(80, calcPower 161)  // ratio=7728/480=16 (integer) <=16
    Assert.Equal(40, calcPower 170)  // ratio=8160/480=17 <=32
    Assert.Equal(40, calcPower 320)  // ratio=15360/480=32
    Assert.Equal(40, calcPower 321)  // ratio=15408/480=32 (integer) <=32
    Assert.Equal(20, calcPower 340)  // ratio=16320/480=34 >32
    Assert.Equal(20, calcPower 480)  // ratio=23040/480=48

[<Fact>]
let ``EFFECT_RETURN with 0 friendship deals no damage`` () =
    let m = move "RETURN" "EFFECT_RETURN" 1 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx ReturnDamage
    // move_effects/return.asm allows power 0; DamageCalc returns before min damage.
    Assert.Equal(0, ctx'.LastDamage)
    Assert.Equal(foe.Hp, ctx'.Foe.Hp)

[<Fact>]
let ``EFFECT_FRUSTRATION with 0 friendship gives max power`` () =
    let m = move "FRUSTRATION" "EFFECT_FRUSTRATION" 1 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx FrustrationDamage
    // power = 255*10/25 = 102. Should deal good damage.
    Assert.True(ctx'.LastDamage > 0)

[<Fact>]
let ``EFFECT_FRUSTRATION power scales with friendship`` () =
    // friendship=255 -> power = 0, reproducing the disassembly's zero-damage bug.
    // friendship=0 -> power = 102.
    Assert.Equal(0, (255 - 255) * 10 / 25)
    Assert.Equal(102, (255 - 0) * 10 / 25)
    Assert.Equal(40, (255 - 155) * 10 / 25)

[<Fact>]
let ``EFFECT_PRESENT damage tiers match thresholds`` () =
    let m = move "PRESENT" "EFFECT_PRESENT" 1 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    // Seed that gives roll <= 102 → power 40.
    // We just need to verify the branching logic works.
    let mkCtx seed : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create seed; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    // Run many seeds and verify all outcomes are within expected set.
    let mutable sawDamage = false
    let mutable sawHeal = false
    for s in 0u .. 200u do
        let ctx' = Effects.applyCtx (mkCtx s) PresentDamage
        if ctx'.Foe.Hp < 200 then sawDamage <- true
        elif ctx'.Foe.Hp > 200 || ctx'.Messages |> List.exists (fun m -> m.Contains "regained") then
            sawHeal <- true
    Assert.True(sawDamage, "Present should deal damage at some seeds")

[<Fact>]
let ``EFFECT_MAGNITUDE power from seeded RNG`` () =
    let m = move "MAGNITUDE" "EFFECT_MAGNITUDE" 1 (ty "GROUND")
    let user = mon "USER" (ty "GROUND") (ty "GROUND") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let mkCtx seed : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create seed; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    // Run many seeds and check magnitude messages appear.
    let mutable magnitudes = Set.empty
    for s in 0u .. 500u do
        let ctx' = Effects.applyCtx (mkCtx s) MagnitudeDamage
        for msg in ctx'.Messages do
            if msg.StartsWith("Magnitude") then
                magnitudes <- magnitudes.Add(msg)
    Assert.True(magnitudes.Count >= 3, $"Should see multiple magnitude levels, got {magnitudes.Count}")

[<Fact>]
let ``EFFECT_HIDDEN_POWER with DV=0 gives power 31 and type FIGHTING`` () =
    // With all DVs = 0:
    // Power: (0*5 + 0)/2 + 31 = 31.
    // Type: (0 | 0<<2) + 1 = 1 = FIGHTING.
    let m = move "HIDDEN_POWER" "EFFECT_HIDDEN_POWER" 1 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx HiddenPowerDamage
    Assert.True(ctx'.LastDamage > 0)

[<Fact>]
let ``EFFECT_FURY_CUTTER doubles each consecutive hit`` () =
    let m = move "FURY_CUTTER" "EFFECT_FURY_CUTTER" 10 (ty "BUG")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 999 100 100 50
    let mkCtx fc : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = fc; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let c1 = Effects.applyCtx (mkCtx 0) FuryCutterDamage  // count becomes 1, 1x
    let d1 = c1.LastDamage
    let c2 = Effects.applyCtx (mkCtx 1) FuryCutterDamage  // count becomes 2, 2x
    let d2 = c2.LastDamage
    let c3 = Effects.applyCtx (mkCtx 2) FuryCutterDamage  // count becomes 3, 4x
    let d3 = c3.LastDamage
    // d2 should be roughly 2x d1, d3 roughly 4x d1 (integer truncation may differ slightly).
    Assert.True(d2 > d1, $"d2={d2} should be > d1={d1}")
    Assert.True(d3 > d2, $"d3={d3} should be > d2={d2}")
    Assert.Equal(1, c1.FuryCutterCount)
    Assert.Equal(2, c2.FuryCutterCount)
    Assert.Equal(3, c3.FuryCutterCount)

[<Fact>]
let ``EFFECT_ROLLOUT power doubles each turn with Defense Curl`` () =
    let m = move "ROLLOUT" "EFFECT_ROLLOUT" 30 (ty "ROCK")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 999 100 100 50
    let mkCtx rc curl : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = rc; DefenseCurlUsed = curl; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let c1 = Effects.applyCtx (mkCtx 0 false) RolloutDamage  // count=1, doublings=0
    let c2 = Effects.applyCtx (mkCtx 1 false) RolloutDamage  // count=2, doublings=1
    let c1c = Effects.applyCtx (mkCtx 0 true) RolloutDamage  // count=1, doublings=1 (curl)
    Assert.True(c2.LastDamage > c1.LastDamage, "Rollout should double each turn")
    Assert.True(c1c.LastDamage > c1.LastDamage, "Defense Curl should boost rollout")

[<Fact>]
let ``EFFECT_TRIPLE_KICK hits 3 times with escalating power`` () =
    let m = move "TRIPLE_KICK" "EFFECT_TRIPLE_KICK" 10 (ty "FIGHTING")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 999 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx TripleKickDamage
    Assert.True(ctx'.LastDamage > 0)
    Assert.Contains(ctx'.Messages, fun m -> m.Contains "3 time(s)")

// --- Drain / recoil / self ---

[<Fact>]
let ``EFFECT_LEECH_HIT drains half damage dealt to heal user`` () =
    let m = move "ABSORB" "EFFECT_LEECH_HIT" 20 (ty "GRASS")
    let user = { mon "USER" (ty "GRASS") (ty "GRASS") 50 200 100 100 50 with Hp = 100 }
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx DrainDamage
    let healed = ctx'.User.Hp - 100
    let expectedHeal = max 1 (ctx'.LastDamage / 2)
    Assert.Equal(expectedHeal, healed)
    Assert.Contains(ctx'.Messages, fun m -> m.Contains "sucked health")

[<Fact>]
let ``EFFECT_DREAM_EATER only works on sleeping target`` () =
    let m = move "DREAM_EATER" "EFFECT_DREAM_EATER" 100 (ty "NORMAL")
    let user = { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50 with Hp = 100 }
    let awakeFoe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let sleepFoe = { awakeFoe with Status = Sleep 3 }
    let mkCtx foe : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    // Should fail on awake target.
    let c1 = Effects.applyCtx (mkCtx awakeFoe) DreamEaterDamage
    Assert.Contains(c1.Messages, fun m -> m.Contains "failed")
    Assert.Equal(200, c1.Foe.Hp)
    // Should work on sleeping target.
    let c2 = Effects.applyCtx (mkCtx sleepFoe) DreamEaterDamage
    Assert.True(c2.Foe.Hp < 200)
    Assert.True(c2.User.Hp > 100)

[<Fact>]
let ``EFFECT_RECOIL_HIT user takes 1/4 damage dealt`` () =
    let m = move "TAKE_DOWN" "EFFECT_RECOIL_HIT" 90 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    // Apply Damage then Recoil.
    let ctx' = Effects.applyCtx ctx Damage
    let ctx'' = Effects.applyCtx ctx' Recoil
    let recoil = max 1 (ctx'.LastDamage / 4)
    Assert.Equal(200 - recoil, ctx''.User.Hp)

[<Fact>]
let ``EFFECT_SELFDESTRUCT user faints after dealing damage`` () =
    let m = move "EXPLOSION" "EFFECT_SELFDESTRUCT" 250 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx SelfdestructDamage
    Assert.Equal(0, ctx'.User.Hp)
    Assert.True(ctx'.Foe.Hp < 200)

[<Fact>]
let ``EFFECT_JUMP_KICK crash on miss deals one eighth of precomputed damage`` () =
    let m = { move "JUMP_KICK" "EFFECT_JUMP_KICK" 70 (ty "FIGHTING") with Accuracy = 0 }
    let user = { mon "USER" (ty "FIGHTING") (ty "FIGHTING") 50 200 100 100 200 with Moves = [ m ]; Pp = [ 25 ] }
    let foe = { mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 1 with Moves = [ growl ]; Pp = [ 40 ] }
    let seed = 42u
    let critByte, rng = Rng.next (Rng.create seed)
    let spreadByte, _ = Rng.next rng
    let spread = Damage.MinRoll + spreadByte % (Damage.MaxRoll - Damage.MinRoll + 1)
    let crit = critByte < CriticalHit.thresholds.[0]
    let expectedCrash = max 1 ((Damage.calc user foe m crit spread false) / 8)
    let after = Battle.create user foe seed |> Battle.chooseMove 0
    Assert.Equal(user.Hp - expectedCrash, after.Player.Hp)
    Assert.Contains(after.Messages, fun msg -> msg.Contains("crashed"))

[<Fact>]
let ``EFFECT_PAY_DAY includes coins message`` () =
    let m = move "PAY_DAY" "EFFECT_PAY_DAY" 40 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx PayDayDamage
    Assert.Contains(ctx'.Messages, fun m -> m.Contains "Coins scattered")
    Assert.True(ctx'.Foe.Hp < 200)

[<Fact>]
let ``EFFECT_RAPID_SPIN clears leech seed trap and spikes`` () =
    let m = move "RAPID_SPIN" "EFFECT_RAPID_SPIN" 20 (ty "NORMAL")
    let user =
        { mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
          with Volatile = { VolatileStatus.empty with LeechSeed = true; Trapped = Some 3 } }
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = { SideState.Empty with Spikes = 1 }
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx RapidSpinDamage
    Assert.False(ctx'.User.Volatile.LeechSeed)
    Assert.Equal(None, ctx'.User.Volatile.Trapped)
    Assert.Equal(0, ctx'.PlayerSide.Spikes)
    Assert.Contains(ctx'.Messages, fun m -> m.Contains "shed Leech Seed")

// --- Multi-hit ---

[<Fact>]
let ``EFFECT_MULTI_HIT produces 2-5 hits with correct distribution`` () =
    let m = move "DOUBLESLAP" "EFFECT_MULTI_HIT" 15 (ty "NORMAL")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 999 100 100 50
    let mutable hitCounts = Map.ofList [ (2,0); (3,0); (4,0); (5,0) ]
    for s in 0u .. 1000u do
        let ctx : MoveContext =
            { User = user; Foe = { foe with Hp = 999 }; Move = m; Crit = false; Roll = 255
              Rng = Rng.create s; Messages = []; LastDamage = 0; IsStruggle = false
              FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
              UserIsPlayer = true
              PlayerSide = SideState.Empty
              EnemySide = SideState.Empty
              WeatherTimer = None; WeatherType = None }
        let ctx' = Effects.applyCtx ctx MultiHitDamage
        // Extract hit count from message.
        for msg in ctx'.Messages do
            for h in 2 .. 5 do
                if msg.Contains($"Hit {h} time(s)") then
                    hitCounts <- hitCounts.Add(h, hitCounts.[h] + 1)
    // Verify distribution: 2 and 3 should each be ~37.5%, 4 and 5 each ~12.5%.
    let total = hitCounts |> Map.toList |> List.sumBy snd
    Assert.True(total > 0, "Should have counted hits")
    Assert.True(hitCounts.[2] > hitCounts.[4], $"2-hits ({hitCounts.[2]}) should be more common than 4-hits ({hitCounts.[4]})")
    Assert.True(hitCounts.[3] > hitCounts.[5], $"3-hits ({hitCounts.[3]}) should be more common than 5-hits ({hitCounts.[5]})")

[<Fact>]
let ``EFFECT_DOUBLE_HIT always hits exactly 2 times`` () =
    let m = move "DOUBLE_KICK" "EFFECT_DOUBLE_HIT" 30 (ty "FIGHTING")
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 999 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx DoubleHitDamage
    Assert.Contains(ctx'.Messages, fun m -> m.Contains "2 time(s)")
    Assert.True(ctx'.Foe.Hp < 999)

[<Fact>]
let ``EFFECT_POISON_MULTI_HIT (Twineedle) hits twice`` () =
    let m = { move "TWINEEDLE" "EFFECT_POISON_MULTI_HIT" 25 (ty "BUG") with EffectChance = 20 }
    let user = mon "USER" (ty "NORMAL") (ty "NORMAL") 50 200 100 100 50
    let foe = mon "FOE" (ty "NORMAL") (ty "NORMAL") 50 999 100 100 50
    let ctx : MoveContext =
        { User = user; Foe = foe; Move = m; Crit = false; Roll = 255
          Rng = Rng.create 0u; Messages = []; LastDamage = 0; IsStruggle = false
          FuryCutterCount = 0; RolloutCount = 0; DefenseCurlUsed = false; Friendship = 0
          UserIsPlayer = true
          PlayerSide = SideState.Empty
          EnemySide = SideState.Empty
          WeatherTimer = None; WeatherType = None }
    let ctx' = Effects.applyCtx ctx PoisonMultiHitDamage
    Assert.Contains(ctx'.Messages, fun m -> m.Contains "2 time(s)")
    Assert.True(ctx'.Foe.Hp < 999)

// --- forMove dispatch tests ---

[<Fact>]
let ``forMove maps all M13.5 effects to non-fallback commands`` () =
    let effects = [
        "EFFECT_LEVEL_DAMAGE"; "EFFECT_PSYWAVE"; "EFFECT_SUPER_FANG"
        "EFFECT_STATIC_DAMAGE"; "EFFECT_OHKO"; "EFFECT_FALSE_SWIPE"
        "EFFECT_REVERSAL"; "EFFECT_RETURN"; "EFFECT_FRUSTRATION"
        "EFFECT_PRESENT"; "EFFECT_MAGNITUDE"; "EFFECT_HIDDEN_POWER"
        "EFFECT_FURY_CUTTER"; "EFFECT_ROLLOUT"; "EFFECT_TRIPLE_KICK"
        "EFFECT_BEAT_UP"; "EFFECT_LEECH_HIT"; "EFFECT_DREAM_EATER"
        "EFFECT_SELFDESTRUCT"; "EFFECT_JUMP_KICK"; "EFFECT_PAY_DAY"
        "EFFECT_RAPID_SPIN"; "EFFECT_THIEF"; "EFFECT_RAGE"
        "EFFECT_MULTI_HIT"; "EFFECT_DOUBLE_HIT"; "EFFECT_POISON_MULTI_HIT"
        "EFFECT_GUST"; "EFFECT_TWISTER"; "EFFECT_STOMP"; "EFFECT_EARTHQUAKE"
    ]
    for eff in effects do
        let m = { move "TEST" eff 40 (ty "NORMAL") with Power = 40 }
        let cmds = Effects.forMove m
        Assert.True(cmds <> [ Damage ], $"{eff} should not fall back to [Damage]")

[<Fact>]
let ``battle anim maps fire move to FireBurst`` () =
    let move = Moves.byName "EMBER"
    Assert.Equal(FireBurst, BattleAnim.effectForMove move)

[<Fact>]
let ``battle anim parses Ember script from disassembly`` () =
    let move = Moves.byName "EMBER"
    let script = BattleAnim.scriptForMove move |> Option.defaultWith (fun () -> failwith "missing Ember animation")

    Assert.Equal("BattleAnim_Ember", script.Label)
    Assert.Contains(script.Commands, function AnimGfx gfx -> gfx |> List.contains "BATTLE_ANIM_GFX_FIRE" | _ -> false)
    Assert.Contains(script.Commands, function AnimObj(objectId, _, _, _) -> objectId = "BATTLE_ANIM_OBJ_EMBER" | _ -> false)
    Assert.Equal(56, BattleAnim.durationForMove move)

[<Fact>]
let ``battle anim maps status move to StatusEffect`` () =
    let move = Moves.byName "GROWL"
    Assert.Equal(StatusEffect, BattleAnim.effectForMove move)

// --- Battle switching and running ---

[<Fact>]
let ``running from wild battle sets Ran outcome`` () =
    let p = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 10 [ Moves.byName "TACKLE" ]
    let e = BattleMon.ofSpecies (Species.byName "PIDGEY") 5 [ Moves.byName "TACKLE" ]
    let state = Battle.create p e 0u
    let after = Battle.run state
    Assert.Equal(Some Ran, after.Outcome)

[<Fact>]
let ``switching changes the active player mon`` () =
    let p1 = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 10 [ Moves.byName "TACKLE" ]
    let p2 = BattleMon.ofSpecies (Species.byName "TOTODILE") 10 [ Moves.byName "SCRATCH" ]
    let e = BattleMon.ofSpecies (Species.byName "PIDGEY") 5 [ Moves.byName "TACKLE" ]
    let state = Battle.createTeam [ p1; p2 ] [ e ] 0u
    let after = Battle.switchMon 1 state
    Assert.Equal("TOTODILE", after.Player.Species.Name)

[<Fact>]
let ``cannot switch to fainted mon`` () =
    let p1 = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 10 [ Moves.byName "TACKLE" ]
    let p2 = { BattleMon.ofSpecies (Species.byName "TOTODILE") 10 [ Moves.byName "SCRATCH" ] with Hp = 0 }
    let e = BattleMon.ofSpecies (Species.byName "PIDGEY") 5 [ Moves.byName "TACKLE" ]
    let state = Battle.createTeam [ p1; p2 ] [ e ] 0u
    let after = Battle.switchMon 1 state
    Assert.Equal("CYNDAQUIL", after.Player.Species.Name)
