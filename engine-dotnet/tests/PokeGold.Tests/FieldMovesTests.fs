module PokeGold.Tests.FieldMovesTests

open Xunit
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player

// TODO: expand this to a full field-use integration test once HM tile logic lands.

[<Fact>]
let ``FieldMoves.canUse returns false without the required badge`` () =
    let mon = { PartyMon.create 155 5 with Moves = [ 15, 30 ] }
    let world = World.empty

    Assert.False(FieldMoves.canUse "CUT" world [ mon ])

[<Fact>]
let ``FieldMoves.canUse returns true when badge and move are present`` () =
    let mon = { PartyMon.create 155 5 with Moves = [ 15, 30 ] }
    let world = World.setFlag "ENGINE_HIVEBADGE" World.empty

    Assert.True(FieldMoves.canUse "CUT" world [ mon ])

[<Fact>]
let ``CUT collision ID is 0x12`` () =
    Assert.Equal(0x12uy, FieldMoves.CollCutTree)

[<Fact>]
let ``canUse CUT requires HIVEBADGE`` () =
    let world = World.empty
    Assert.False(FieldMoves.canUse "CUT" world [])

    let worldWithBadge = World.setFlag "ENGINE_HIVEBADGE" world
    Assert.False(FieldMoves.canUse "CUT" worldWithBadge [])

[<Fact>]
let ``Cut succeeds on cut tree with badge and move`` () =
    let world = World.setFlag "ENGINE_HIVEBADGE" World.empty
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove "CUT" [] }

    match FieldMoves.tryCut 0x12uy world [ mon ] with
    | FieldMoves.Used _ -> ()
    | FieldMoves.NotUsable reason -> Assert.Fail(reason)

[<Fact>]
let ``Cut fails without badge`` () =
    match FieldMoves.tryCut 0x12uy World.empty [] with
    | FieldMoves.NotUsable _ -> ()
    | FieldMoves.Used _ -> Assert.Fail("should fail without badge")

[<Fact>]
let ``Repel.blocks suppresses weak encounters when lead mon is strong enough`` () =
    let lead = PartyMon.create 155 10
    let player = { PlayerStateOps.initial with Party = [ lead ]; RepelSteps = 50 }

    Assert.True(PokeGold.Game.Player.Repel.blocks player 5)
    Assert.False(PokeGold.Game.Player.Repel.blocks player 15)
