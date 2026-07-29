module PokeGold.Tests.PackUseGiveTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Scenes

// ── Test fixtures ──────────────────────────────────────────────────────────────

/// A damaged mon at slot 0 (half HP), healthy mon at slot 1.
let private makeParty () : Party =
    let m0 = { PartyMon.create 4 10 with Hp = 10; MaxHp = 35 }   // Charmander L10, damaged
    let m1 = { PartyMon.create 1  5 with Hp = 21; MaxHp = 21 }   // Bulbasaur L5, full HP
    [ m0; m1 ]

let private makePlayer () : PlayerState =
    let bag =
        Bag.empty
        |> Bag.add "POTION"    5   // HP +20, Item pocket
        |> Bag.add "ANTIDOTE"  3   // status cure — deferred
        |> Bag.add "POKE_BALL" 10  // Ball pocket
    { PlayerStateOps.initial with Bag = bag; Party = makeParty () }

// ── Pure applyGive ─────────────────────────────────────────────────────────────

[<Fact>]
let ``applyGive sets HeldItem on target slot`` () =
    let p  = makePlayer ()
    let p' = PackUseGive.applyGive "POTION" 0 p
    Assert.Equal(Some "POTION", p'.Party.[0].HeldItem)

[<Fact>]
let ``applyGive decrements the bag by 1`` () =
    let p  = makePlayer ()
    let p' = PackUseGive.applyGive "POTION" 0 p
    Assert.Equal(4, Bag.count "POTION" p'.Bag)

[<Fact>]
let ``applyGive swap returns old held item to bag`` () =
    let p0 =
        let mon = { (makePlayer ()).Party.[0] with HeldItem = Some "ANTIDOTE" }
        let party = mon :: (makePlayer ()).Party.Tail
        { makePlayer () with Party = party }
    let p' = PackUseGive.applyGive "POTION" 0 p0
    // Old ANTIDOTE should be returned to bag (+1), POTION decremented (-1).
    Assert.Equal(Some "POTION", p'.Party.[0].HeldItem)
    Assert.Equal(4, Bag.count "POTION" p'.Bag)
    Assert.Equal(4, Bag.count "ANTIDOTE" p'.Bag)  // 3 original + 1 returned

[<Fact>]
let ``applyGive clears attached mail when replacing a mail held item`` () =
    let p0 =
        let mon =
            { (makePlayer ()).Party.[0] with
                HeldItem = Some "FLOWER_MAIL"
                Mail = Some { Item = "FLOWER_MAIL"; Message = "DARK CAVE leads"; SenderName = "RANDY"; SenderId = 1001; Species = 21 } }
        { makePlayer () with Party = mon :: (makePlayer ()).Party.Tail }

    let p' = PackUseGive.applyGive "POTION" 0 p0
    Assert.Equal(Some "POTION", p'.Party.[0].HeldItem)
    Assert.True(p'.Party.[0].Mail.IsNone)

[<Fact>]
let ``applyGive same item already held: no duplicate return`` () =
    let p0 =
        let mon = { (makePlayer ()).Party.[0] with HeldItem = Some "POTION" }
        let party = mon :: (makePlayer ()).Party.Tail
        { makePlayer () with Party = party }
    let p' = PackUseGive.applyGive "POTION" 0 p0
    // Held item is already POTION — no return to bag; just decrement.
    Assert.Equal(Some "POTION", p'.Party.[0].HeldItem)
    Assert.Equal(4, Bag.count "POTION" p'.Bag)

// ── Pure applyHpHeal ───────────────────────────────────────────────────────────

[<Fact>]
let ``applyHpHeal POTION heals 20 HP`` () =
    let p  = makePlayer ()
    let p' = PackUseGive.applyHpHeal "POTION" 0 p
    Assert.True(p'.IsSome)
    Assert.Equal(30, p'.Value.Party.[0].Hp)  // 10 + 20 = 30

[<Fact>]
let ``applyHpHeal clamps at MaxHp`` () =
    let p  = makePlayer ()
    // Slot 0: Hp=10 MaxHp=35; HYPER_POTION heals 200 — should clamp to 35.
    let p' = PackUseGive.applyHpHeal "HYPER_POTION" 0
                { p with Bag = Bag.add "HYPER_POTION" 1 p.Bag }
    Assert.True(p'.IsSome)
    Assert.Equal(35, p'.Value.Party.[0].Hp)

[<Fact>]
let ``applyHpHeal MAX_POTION fully heals`` () =
    let p  = makePlayer ()
    let p' = PackUseGive.applyHpHeal "MAX_POTION" 0
                { p with Bag = Bag.add "MAX_POTION" 1 p.Bag }
    Assert.True(p'.IsSome)
    Assert.Equal(35, p'.Value.Party.[0].Hp)  // = MaxHp

[<Fact>]
let ``applyHpHeal decrements the bag`` () =
    let p  = makePlayer ()
    let p' = PackUseGive.applyHpHeal "POTION" 0 p
    Assert.True(p'.IsSome)
    Assert.Equal(4, Bag.count "POTION" p'.Value.Bag)

[<Fact>]
let ``applyHpHeal returns None when mon is at full HP`` () =
    let p  = makePlayer ()
    // Slot 1 is at full HP (21/21).
    let result = PackUseGive.applyHpHeal "POTION" 1 p
    Assert.True(result.IsNone)

[<Fact>]
let ``applyHpHeal does not decrement bag when mon is at full HP`` () =
    let p  = makePlayer ()
    let _  = PackUseGive.applyHpHeal "POTION" 1 p
    Assert.Equal(5, Bag.count "POTION" p.Bag)  // original unchanged

// ── Status-heal item use ──────────────────────────────────────────────────────

[<Fact>]
let ``applyStatusCure clears matching source statuses and consumes the item`` () =
    for item, status in
        [ "ANTIDOTE", "PSN"
          "BURN_HEAL", "BRN"
          "ICE_HEAL", "FRZ"
          "AWAKENING", "SLP:3"
          "PARLYZ_HEAL", "PAR"
          "FULL_HEAL", "PSN" ] do
        let mon = { PartyMon.create 4 10 with Status = status }
        let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add item 1 Bag.empty }

        match PackUseGive.applyStatusCure item 0 player with
        | Some healed ->
            Assert.Equal("", healed.Party.[0].Status)
            Assert.Equal(0, Bag.count item healed.Bag)
        | None -> Assert.Fail(sprintf "%s should cure %s" item status)

[<Fact>]
let ``applyStatusCure rejects a nonmatching status without consuming the item`` () =
    let mon = { PartyMon.create 4 10 with Status = "BRN" }
    let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "ANTIDOTE" 1 Bag.empty }

    Assert.True(PackUseGive.applyStatusCure "ANTIDOTE" 0 player |> Option.isNone)
    Assert.Equal(1, Bag.count "ANTIDOTE" player.Bag)

[<Fact>]
let ``applyFullRestore heals status even when HP is already full`` () =
    let mon = { PartyMon.create 4 10 with Status = "PSN" }
    let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "FULL_RESTORE" 1 Bag.empty }

    match PackUseGive.applyFullRestore 0 player with
    | Some healed ->
        Assert.Equal(healed.Party.[0].MaxHp, healed.Party.[0].Hp)
        Assert.Equal("", healed.Party.[0].Status)
        Assert.Equal(0, Bag.count "FULL_RESTORE" healed.Bag)
    | None -> Assert.Fail("FULL_RESTORE should cure a full-HP poisoned party mon")

[<Fact>]
let ``applyFullRestore rejects healthy and fainted targets without consumption`` () =
    let healthy = PartyMon.create 4 10
    let fainted = { PartyMon.create 4 10 with Hp = 0; Status = "PSN" }

    for mon in [ healthy; fainted ] do
        let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "FULL_RESTORE" 1 Bag.empty }

        Assert.True(PackUseGive.applyFullRestore 0 player |> Option.isNone)
        Assert.Equal(1, Bag.count "FULL_RESTORE" player.Bag)

[<Fact>]
let ``applyRepel sets step counter and removes item`` () =
    let player = { PlayerStateOps.initial with Bag = Bag.add "REPEL" 1 Bag.empty }

    match PackUseGive.applyRepel "REPEL" player with
    | Some updated ->
        Assert.Equal(100, updated.RepelSteps)
        Assert.Equal(0, Bag.count "REPEL" updated.Bag)
    | None -> Assert.Fail("should have applied")

[<Fact>]
let ``applyTmHm teaches HM without consuming it`` () =
    let player =
        { PlayerStateOps.initial with
            Party = [ PartyMon.create (Species.byName "TOTODILE").Dex 10 ]
            Bag = Bag.add "HM_SURF" 1 Bag.empty }

    match PackUseGive.applyTmHm "HM_SURF" 0 player with
    | Some updated ->
        Assert.Equal(1, Bag.count "HM_SURF" updated.Bag)
        Assert.True(updated.Party.[0].Moves.Length > player.Party.[0].Moves.Length)
    | None -> Assert.Fail("HM_SURF should teach SURF")

[<Fact>]
let ``applyTmHm consumes TM after teaching it`` () =
    let player =
        { PlayerStateOps.initial with
            Party = [ PartyMon.create (Species.byName "PIKACHU").Dex 10 ]
            Bag = Bag.add "TM01" 1 Bag.empty }

    match PackUseGive.applyTmHm "TM01" 0 player with
    | Some updated -> Assert.Equal(0, Bag.count "TM01" updated.Bag)
    | None -> Assert.Fail("TM01 should teach DYNAMICPUNCH")

[<Fact>]
let ``BAT-014 accepted stone evolution consumes one stone and cancellation can defer mutation`` () =
    let gloom = PartyMon.create (Species.byName "GLOOM").Dex 20
    let player =
        { PlayerStateOps.initial with
            Party = [ gloom ]
            Bag = Bag.add "LEAF_STONE" 2 Bag.empty
            DexSeen = Set.singleton gloom.SpeciesId
            DexOwn = Set.singleton gloom.SpeciesId }
    let candidate = PackUseGive.prepareEvolution "LEAF_STONE" 0 player |> Option.get
    Assert.Equal(gloom.SpeciesId, player.Party.Head.SpeciesId)
    Assert.Equal(2, Bag.count "LEAF_STONE" player.Bag)
    let attempted = PackUseGive.consumeEvolutionStone "LEAF_STONE" player
    let evolved = PackUseGive.applyEvolution "LEAF_STONE" 0 candidate attempted
    Assert.Equal((Species.byName "VILEPLUME").Dex, evolved.Party.Head.SpeciesId)
    Assert.Equal(1, Bag.count "LEAF_STONE" evolved.Bag)
    Assert.Contains(gloom.SpeciesId, evolved.DexSeen)
    Assert.Contains(gloom.SpeciesId, evolved.DexOwn)
    Assert.Contains(evolved.Party.Head.SpeciesId, evolved.DexSeen)
    Assert.Contains(evolved.Party.Head.SpeciesId, evolved.DexOwn)

[<Fact>]
let ``BAT-014 incompatible or Everstone-held mon cannot consume a stone`` () =
    let cyndaquil = PartyMon.create (Species.byName "CYNDAQUIL").Dex 20
    let player = { PlayerStateOps.initial with Party = [ cyndaquil ]; Bag = Bag.add "LEAF_STONE" 1 Bag.empty }
    Assert.True(PackUseGive.prepareEvolution "LEAF_STONE" 0 player |> Option.isNone)
    let gloom = { PartyMon.create (Species.byName "GLOOM").Dex 20 with HeldItem = Some "EVERSTONE" }
    Assert.True(PackUseGive.prepareEvolution "LEAF_STONE" 0 { player with Party = [ gloom ] } |> Option.isNone)

[<Fact>]
let ``BAT-015 accepted evolution retains prior dex entry and registers target`` () =
    let gloom = PartyMon.create (Species.byName "GLOOM").Dex 20
    let player =
        { PlayerStateOps.initial with
            Party = [ gloom ]
            Bag = Bag.add "SUN_STONE" 1 Bag.empty
            DexSeen = Set.singleton gloom.SpeciesId
            DexOwn = Set.singleton gloom.SpeciesId }
    let candidate = PackUseGive.prepareEvolution "SUN_STONE" 0 player |> Option.get
    let evolved = player |> PackUseGive.consumeEvolutionStone "SUN_STONE" |> PackUseGive.applyEvolution "SUN_STONE" 0 candidate
    let target = (Species.byName "BELLOSSOM").Dex
    Assert.Equal<Set<int>>(Set.ofList [ gloom.SpeciesId; target ], evolved.DexSeen)
    Assert.Equal<Set<int>>(Set.ofList [ gloom.SpeciesId; target ], evolved.DexOwn)

// ── Deferred field-item helpers ──────────────────────────────────────────────

let private tackleId = MovesData.byIndex |> Array.findIndex (fun move -> move.Name = "TACKLE")
let private tacklePp = MovesData.byIndex.[tackleId].Pp

[<Fact>]
let ``applyRareCandy levels, learns, and consumes only below level 100`` () =
    let mon = PartyMon.create 4 10
    let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "RARE_CANDY" 1 Bag.empty }

    match PackUseGive.applyRareCandy 0 player with
    | Some(updated, _) ->
        Assert.Equal(11, updated.Party.[0].Level)
        Assert.Equal(0, Bag.count "RARE_CANDY" updated.Bag)
    | None -> Assert.Fail("Rare Candy should level a level-10 mon")

    let capped = { player with Party = [ PartyMon.create 4 100 ] }
    Assert.True(PackUseGive.applyRareCandy 0 capped |> Option.isNone)
    Assert.Equal(1, Bag.count "RARE_CANDY" capped.Bag)

[<Fact>]
let ``applyVitamin HP_UP raises stat experience and rejects the source cap`` () =
    let mon = { PartyMon.create 4 10 with Hp = 10 }
    let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "HP_UP" 1 Bag.empty }

    match PackUseGive.applyVitamin "HP_UP" 0 player with
    | Some updated ->
        Assert.Equal(10, updated.Party.[0].StatExp.Hp)
        Assert.Equal(0, Bag.count "HP_UP" updated.Bag)
    | None -> Assert.Fail("HP UP should apply below 100 stat experience")

    let capped = { player with Party = [ { mon with StatExp = { mon.StatExp with Hp = 100 } } ] }
    Assert.True(PackUseGive.applyVitamin "HP_UP" 0 capped |> Option.isNone)
    Assert.Equal(1, Bag.count "HP_UP" capped.Bag)

[<Fact>]
let ``applyEther restores selected PP and rejects a full move`` () =
    let mon = { PartyMon.create 4 10 with Moves = [ tackleId, 1 ] }
    let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "ETHER" 1 Bag.empty }

    match PackUseGive.applyEther "ETHER" 0 0 player with
    | Some updated ->
        Assert.Equal(min tacklePp 11, snd updated.Party.[0].Moves.Head)
        Assert.Equal(0, Bag.count "ETHER" updated.Bag)
    | None -> Assert.Fail("Ether should restore missing PP")

    let full = { player with Party = [ { mon with Moves = [ tackleId, tacklePp ] } ] }
    Assert.True(PackUseGive.applyEther "ETHER" 0 0 full |> Option.isNone)
    Assert.Equal(1, Bag.count "ETHER" full.Bag)

[<Fact>]
let ``applyElixer restores all PP and rejects a full moveset`` () =
    let mon = { PartyMon.create 4 10 with Moves = [ tackleId, 1 ] }
    let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "ELIXER" 1 Bag.empty }

    match PackUseGive.applyElixer "ELIXER" 0 player with
    | Some updated ->
        Assert.Equal(min tacklePp 11, snd updated.Party.[0].Moves.Head)
        Assert.Equal(0, Bag.count "ELIXER" updated.Bag)
    | None -> Assert.Fail("Elixer should restore missing PP")

    let full = { player with Party = [ { mon with Moves = [ tackleId, tacklePp ] } ] }
    Assert.True(PackUseGive.applyElixer "ELIXER" 0 full |> Option.isNone)
    Assert.Equal(1, Bag.count "ELIXER" full.Bag)

[<Fact>]
let ``applyPpUp raises stored PP ceiling approximation and rejects its cap`` () =
    let mon = { PartyMon.create 4 10 with Moves = [ tackleId, tacklePp ] }
    let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "PP_UP" 1 Bag.empty }
    let ceiling = tacklePp + tacklePp * 3 / 5

    match PackUseGive.applyPpUp "PP_UP" 0 0 player with
    | Some updated ->
        Assert.Equal(tacklePp + tacklePp / 5, snd updated.Party.[0].Moves.Head)
        Assert.Equal(0, Bag.count "PP_UP" updated.Bag)
    | None -> Assert.Fail("PP UP should raise a move below its PP Up ceiling")

    let capped = { player with Party = [ { mon with Moves = [ tackleId, ceiling ] } ] }
    Assert.True(PackUseGive.applyPpUp "PP_UP" 0 0 capped |> Option.isNone)
    Assert.Equal(1, Bag.count "PP_UP" capped.Bag)

[<Fact>]
let ``applyRevive restores a fainted mon and rejects a conscious target`` () =
    let mon = { PartyMon.create 4 10 with Hp = 0 }
    let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "REVIVE" 1 Bag.empty }

    match PackUseGive.applyRevive "REVIVE" 0 player with
    | Some updated ->
        Assert.Equal(max 1 (mon.MaxHp / 2), updated.Party.[0].Hp)
        Assert.Equal(0, Bag.count "REVIVE" updated.Bag)
    | None -> Assert.Fail("Revive should restore a fainted mon")

    let conscious = { player with Party = [ PartyMon.create 4 10 ] }
    Assert.True(PackUseGive.applyRevive "REVIVE" 0 conscious |> Option.isNone)
    Assert.Equal(1, Bag.count "REVIVE" conscious.Bag)

[<Fact>]
let ``applyMaxRevive fully restores a fainted mon and rejects a conscious target`` () =
    let mon = { PartyMon.create 4 10 with Hp = 0 }
    let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "MAX_REVIVE" 1 Bag.empty }

    match PackUseGive.applyRevive "MAX_REVIVE" 0 player with
    | Some updated ->
        Assert.Equal(mon.MaxHp, updated.Party.[0].Hp)
        Assert.Equal(0, Bag.count "MAX_REVIVE" updated.Bag)
    | None -> Assert.Fail("Max Revive should restore a fainted mon")

    let conscious = { player with Party = [ PartyMon.create 4 10 ] }
    Assert.True(PackUseGive.applyRevive "MAX_REVIVE" 0 conscious |> Option.isNone)
    Assert.Equal(1, Bag.count "MAX_REVIVE" conscious.Bag)

// ── isHpHeal coverage ─────────────────────────────────────────────────────────

[<Fact>]
let ``isHpHeal true for POTION SUPER_POTION HYPER_POTION MAX_POTION`` () =
    for id in [ "POTION"; "SUPER_POTION"; "HYPER_POTION"; "MAX_POTION" ] do
        Assert.True(PackUseGive.isHpHeal id, sprintf "%s should be an HP heal item" id)

[<Fact>]
let ``isHpHeal true for drinks and berries`` () =
    for id in [ "FRESH_WATER"; "SODA_POP"; "LEMONADE"; "MOOMOO_MILK"; "BERRY_JUICE"; "BERRY"; "GOLD_BERRY" ] do
        Assert.True(PackUseGive.isHpHeal id, sprintf "%s should be an HP heal item" id)

[<Fact>]
let ``isHpHeal false for deferred items`` () =
    for id in [ "ANTIDOTE"; "REVIVE"; "MOON_STONE"; "RARE_CANDY"; "TM01"; "HP_UP"; "ETHER" ] do
        Assert.False(PackUseGive.isHpHeal id, sprintf "%s should NOT be an HP heal item" id)

[<Fact>]
let ``isFishingRod identifies fishing key items`` () =
    Assert.True(PackUseGive.isFishingRod "OLD_ROD")
    Assert.True(PackUseGive.isFishingRod "GOOD_ROD")
    Assert.True(PackUseGive.isFishingRod "SUPER_ROD")
    Assert.False(PackUseGive.isFishingRod "POTION")

// ── Scene integration: GIVE ────────────────────────────────────────────────────

let private makePackScene () =
    let mutable captured: PlayerState option = None
    let scene = PackScene(Content(), makePlayer (), fun p -> captured <- Some p)
    scene, fun () -> captured

let private update (s: PackScene) b = (s :> Scene).Update(b)
let private press b (s: PackScene) = let t = update s b in update s Buttons.none |> ignore; t
let private pressA s = press { Buttons.none with A = true } s
let private pressDown s = press { Buttons.none with Down = true } s

/// Navigate to the action menu for the item at cursor 0 in ITEM pocket.
/// Returns true when the GIVE transition (Push partyScene) is obtained.
let private openGive (scene: PackScene) : Scene =
    // Open action menu for POTION (cursor=0 in Item pocket).
    update scene { Buttons.none with A = true } |> ignore
    update scene Buttons.none |> ignore
    // Action order: [USE, GIVE, TOSS, CANCEL] — GIVE is at index 1.
    update scene { Buttons.none with Down = true } |> ignore
    update scene Buttons.none |> ignore
    // Press A on GIVE.
    let t = update scene { Buttons.none with A = true }
    update scene Buttons.none |> ignore
    match t with
    | Push s -> s
    | _ -> failwithf "Expected Push from GIVE, got %A" t

let private openUse (scene: PackScene) : Scene =
    // Open action menu for POTION (cursor=0 in Item pocket).
    update scene { Buttons.none with A = true } |> ignore
    update scene Buttons.none |> ignore
    // Action order: [USE, GIVE, TOSS, CANCEL] — USE is at index 0.
    let t = update scene { Buttons.none with A = true }
    update scene Buttons.none |> ignore
    match t with
    | Push s -> s
    | _ -> failwithf "Expected Push from USE, got %A" t

[<Fact>]
let ``PackScene GIVE pushes PartyScene`` () =
    let scene, _ = makePackScene ()
    let pushed = openGive scene
    Assert.IsType<PartyScene>(pushed) |> ignore

[<Fact>]
let ``PackScene GIVE via picker sets HeldItem and decrements bag`` () =
    let scene, getCaptured = makePackScene ()
    let pushed = openGive scene
    let party  = pushed :?> PartyScene
    // Press A on slot 0 (the damaged Charmander).
    let t = (party :> Scene).Update({ Buttons.none with A = true })
    Assert.Equal(Pop, t)
    // onChange should have been called; check the updated state.
    let updated = getCaptured ()
    Assert.True(updated.IsSome, "onChange should have been called after GIVE")
    Assert.Equal(Some "POTION", updated.Value.Party.[0].HeldItem)
    Assert.Equal(4, Bag.count "POTION" updated.Value.Bag)

[<Fact>]
let ``PackScene GIVE CurrentPlayer reflects HeldItem after picker`` () =
    let scene, _ = makePackScene ()
    let pushed   = openGive scene
    let party    = pushed :?> PartyScene
    (party :> Scene).Update({ Buttons.none with A = true }) |> ignore
    Assert.Equal(Some "POTION", scene.CurrentPlayer.Party.[0].HeldItem)

// ── Scene integration: USE (HP-heal) ──────────────────────────────────────────

[<Fact>]
let ``PackScene USE POTION on damaged mon pushes PartyScene`` () =
    let scene, _ = makePackScene ()
    let pushed = openUse scene
    Assert.IsType<PartyScene>(pushed) |> ignore

[<Fact>]
let ``PackScene USE POTION via picker heals HP and decrements bag`` () =
    let scene, getCaptured = makePackScene ()
    let pushed = openUse scene
    let party  = pushed :?> PartyScene
    // Slot 0: Hp=10, MaxHp=35 → after POTION → Hp=30
    let t = (party :> Scene).Update({ Buttons.none with A = true })
    Assert.Equal(Pop, t)
    let updated = getCaptured ()
    Assert.True(updated.IsSome, "onChange should have been called after USE")
    Assert.Equal(30, updated.Value.Party.[0].Hp)
    Assert.Equal(4, Bag.count "POTION" updated.Value.Bag)

[<Fact>]
let ``PackScene USE POTION on full-HP mon pops without consuming item`` () =
    let scene, getCaptured = makePackScene ()
    let pushed = openUse scene
    let party  = pushed :?> PartyScene
    // Navigate to slot 1 (full HP).
    (party :> Scene).Update({ Buttons.none with Down = true }) |> ignore
    let t = (party :> Scene).Update({ Buttons.none with A = true })
    Assert.Equal(Pop, t)
    // onChange must NOT be called; bag unchanged.
    Assert.True(getCaptured().IsNone, "onChange must not fire when mon is at full HP")
    Assert.Equal(5, Bag.count "POTION" scene.CurrentPlayer.Bag)

[<Fact>]
let ``PackScene USE ETHER reaches the move picker and restores selected PP`` () =
    let mon = { PartyMon.create 4 10 with Moves = [ tackleId, 1 ] }
    let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "ETHER" 1 Bag.empty }
    let mutable captured: PlayerState option = None
    let scene = PackScene(Content(), player, fun updated -> captured <- Some updated)

    update scene { Buttons.none with A = true } |> ignore
    update scene Buttons.none |> ignore
    let transition = update scene { Buttons.none with A = true }
    update scene Buttons.none |> ignore

    match transition with
    | Push (:? PartyScene as party) ->
        match (party :> Scene).Update({ Buttons.none with A = true }) with
        | Stay ->
            (party :> Scene).Update(Buttons.none) |> ignore
            Assert.Equal(Pop, (party :> Scene).Update({ Buttons.none with A = true }))
        | result -> Assert.Fail(sprintf "Expected move-picker state, got %A" result)
    | result -> Assert.Fail(sprintf "Expected PartyScene Push, got %A" result)

    Assert.True(captured.IsSome)
    Assert.Equal(min tacklePp 11, snd scene.CurrentPlayer.Party.[0].Moves.Head)
    Assert.Equal(0, Bag.count "ETHER" scene.CurrentPlayer.Bag)

[<Fact>]
let ``PackScene USE ESCAPE_ROPE dispatches its overworld callback`` () =
    let player = { PlayerStateOps.initial with Bag = Bag.empty |> Bag.add "ESCAPE_ROPE" 1 }
    let mutable used = false
    let scene = PackScene(Content(), player, ignore, onEscapeRope = fun () -> used <- true)

    update scene { Buttons.none with A = true } |> ignore
    update scene Buttons.none |> ignore
    Assert.Equal(Pop, update scene { Buttons.none with A = true })
    Assert.True(used)

// ── Scene integration: status-heal USE ───────────────────────────────────────

[<Fact>]
let ``PackScene USE ANTIDOTE pushes PartyScene and cures a poisoned mon`` () =
    let antidotePlayer =
        let bag = Bag.empty |> Bag.add "ANTIDOTE" 3
        let poisoned = { PartyMon.create 4 10 with Status = "PSN" }
        { PlayerStateOps.initial with Bag = bag; Party = [ poisoned ] }
    let mutable captured: PlayerState option = None
    let scene = PackScene(Content(), antidotePlayer, fun p -> captured <- Some p)
    // A → action menu for ANTIDOTE. Actions = [USE, GIVE, TOSS, CANCEL].
    update scene { Buttons.none with A = true } |> ignore
    update scene Buttons.none |> ignore
    // Press A on USE (index 0).
    let t = update scene { Buttons.none with A = true }
    update scene Buttons.none |> ignore
    match t with
    | Push (:? PartyScene as party) ->
        Assert.Equal(Pop, (party :> Scene).Update({ Buttons.none with A = true }))
    | _ -> Assert.Fail(sprintf "Expected PartyScene Push, got %A" t)

    Assert.Equal("", scene.CurrentPlayer.Party.[0].Status)
    Assert.Equal(2, Bag.count "ANTIDOTE" scene.CurrentPlayer.Bag)
    Assert.True(captured.IsSome)

[<Fact>]
let ``PackScene USE FULL_RESTORE cures a full-HP poisoned mon`` () =
    let mon = { PartyMon.create 4 10 with Status = "PSN" }
    let player = { PlayerStateOps.initial with Party = [ mon ]; Bag = Bag.add "FULL_RESTORE" 1 Bag.empty }
    let mutable captured: PlayerState option = None
    let scene = PackScene(Content(), player, fun updated -> captured <- Some updated)

    update scene { Buttons.none with A = true } |> ignore
    update scene Buttons.none |> ignore
    let transition = update scene { Buttons.none with A = true }
    update scene Buttons.none |> ignore

    match transition with
    | Push (:? PartyScene as party) ->
        Assert.Equal(Pop, (party :> Scene).Update({ Buttons.none with A = true }))
    | _ -> Assert.Fail(sprintf "Expected PartyScene Push, got %A" transition)

    Assert.Equal("", scene.CurrentPlayer.Party.[0].Status)
    Assert.Equal(0, Bag.count "FULL_RESTORE" scene.CurrentPlayer.Bag)
    Assert.True(captured.IsSome)
