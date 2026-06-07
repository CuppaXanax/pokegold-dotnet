module PokeGold.Tests.ActorTests

open Xunit
open PokeGold.Game.Overworld

let private lookup =
    function
    | "AZALEATOWN_RIVAL" -> Some 9
    | "MOM1" -> Some 0
    | _ -> None

[<Fact>]
let ``actor resolver recognizes player operands`` () =
    Assert.Equal(Some ActorId.Player, Actor.resolve lookup None "PLAYER")
    Assert.Equal(Some ActorId.Player, Actor.resolve lookup None "0")
    Assert.Equal(Some ActorId.Player, Actor.resolve lookup None "-1")

[<Fact>]
let ``actor resolver recognizes last talked operands`` () =
    let last = Some(ActorId.Object 4)
    Assert.Equal(last, Actor.resolve lookup last "LAST_TALKED")
    Assert.Equal(last, Actor.resolve lookup last "-2")
    Assert.Equal(None, Actor.resolve lookup None "LAST_TALKED")

[<Fact>]
let ``actor resolver converts one-based script object ids to zero-based actors`` () =
    Assert.Equal(Some(ActorId.Object 0), Actor.resolve lookup None "1")
    Assert.Equal(Some(ActorId.Object 6), Actor.resolve lookup None "7")

[<Fact>]
let ``actor resolver resolves object constants through map data lookup`` () =
    Assert.Equal(Some(ActorId.Object 9), Actor.resolve lookup None "AZALEATOWN_RIVAL")
    Assert.Equal(Some 9, Actor.resolveObjectIndex lookup None "AZALEATOWN_RIVAL")
    Assert.Equal(None, Actor.resolve lookup None "NOT_A_REAL_OBJECT")
    Assert.Equal(None, Actor.resolveObjectIndex lookup None "PLAYER")
