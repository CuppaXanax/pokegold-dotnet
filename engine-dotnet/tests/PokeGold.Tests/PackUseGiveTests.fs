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

[<Fact>]
let ``applyRepel sets step counter and removes item`` () =
    let player = { PlayerStateOps.initial with Bag = Bag.add "REPEL" 1 Bag.empty }

    match PackUseGive.applyRepel "REPEL" player with
    | Some updated ->
        Assert.Equal(100, updated.RepelSteps)
        Assert.Equal(0, Bag.count "REPEL" updated.Bag)
    | None -> Assert.Fail("should have applied")

// ── isHpHeal coverage ─────────────────────────────────────────────────────────

[<Fact>]
let ``isHpHeal true for POTION SUPER_POTION HYPER_POTION MAX_POTION FULL_RESTORE`` () =
    for id in [ "POTION"; "SUPER_POTION"; "HYPER_POTION"; "MAX_POTION"; "FULL_RESTORE" ] do
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

// ── Scene integration: USE deferred item routes to gated message ──────────────

[<Fact>]
let ``PackScene USE ANTIDOTE (deferred status cure) pushes TextBoxScene`` () =
    let antidotePlayer =
        let bag = Bag.empty |> Bag.add "ANTIDOTE" 3
        { PlayerStateOps.initial with Bag = bag; Party = makeParty () }
    let mutable captured: PlayerState option = None
    let scene = PackScene(Content(), antidotePlayer, fun p -> captured <- Some p)
    // A → action menu for ANTIDOTE. Actions = [USE, GIVE, TOSS, CANCEL].
    update scene { Buttons.none with A = true } |> ignore
    update scene Buttons.none |> ignore
    // Press A on USE (index 0).
    let t = update scene { Buttons.none with A = true }
    update scene Buttons.none |> ignore
    // Should push something — a TextBoxScene, not a PartyScene.
    match t with
    | Push s ->
        Assert.IsNotType<PartyScene>(s)
    | _ ->
        Assert.Fail(sprintf "Expected Push, got %A" t)
    // Bag must be unchanged.
    Assert.Equal(3, Bag.count "ANTIDOTE" scene.CurrentPlayer.Bag)
