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
