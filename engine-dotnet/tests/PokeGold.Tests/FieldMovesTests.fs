module PokeGold.Tests.FieldMovesTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Scenes
open PokeGold.Game.Core

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
    | FieldMoves.Used("CUT", _) -> ()
    | FieldMoves.NotUsable reason -> Assert.Fail(reason)
    | other -> Assert.Fail(sprintf "unexpected result %A" other)

[<Fact>]
let ``Cut fails without badge`` () =
    match FieldMoves.tryCut 0x12uy World.empty [] with
    | FieldMoves.NotUsable _ -> ()
    | FieldMoves.Used _ -> Assert.Fail("should fail without badge")

[<Theory>]
[<InlineData("CUT", "ENGINE_HIVEBADGE", 0x12)>]
[<InlineData("SURF", "ENGINE_FOGBADGE", 0x29)>]
[<InlineData("WHIRLPOOL", "ENGINE_GLACIERBADGE", 0x24)>]
[<InlineData("WATERFALL", "ENGINE_RISINGBADGE", 0x33)>]
let ``tile field moves require matching terrain`` (move: string) (badge: string) (collId: int) =
    let world = World.empty |> World.setFlag badge
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove move [] }

    match FieldMoves.tryUse move (byte collId) "Route36" world [ mon ] with
    | FieldMoves.Used(used, _) -> Assert.Equal(move, used)
    | FieldMoves.NotUsable reason -> Assert.Fail(reason)

[<Theory>]
[<InlineData("CUT", "ENGINE_HIVEBADGE")>]
[<InlineData("SURF", "ENGINE_FOGBADGE")>]
[<InlineData("STRENGTH", "ENGINE_PLAINBADGE")>]
[<InlineData("FLASH", "ENGINE_ZEPHYRBADGE")>]
[<InlineData("WHIRLPOOL", "ENGINE_GLACIERBADGE")>]
[<InlineData("WATERFALL", "ENGINE_RISINGBADGE")>]
let ``HM blockers report missing badge before move use`` (move: string) (badge: string) =
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove move [] }

    match FieldMoves.tryUse move FieldMoves.CollSurf "Route36" World.empty [ mon ] with
    | FieldMoves.NotUsable reason -> Assert.Contains(badge.Replace("ENGINE_", "").Replace("BADGE", "BADGE"), reason)
    | FieldMoves.Used _ -> Assert.Fail("should require badge")

[<Fact>]
let ``Fly requires a known fly point`` () =
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove "FLY" [] }
    let badgeOnly = World.empty |> World.setFlag "ENGINE_STORMBADGE"

    match FieldMoves.tryUse "FLY" 0uy "GoldenrodCity" badgeOnly [ mon ] with
    | FieldMoves.NotUsable reason -> Assert.Contains("FLY destination", reason)
    | FieldMoves.Used _ -> Assert.Fail("should require a flypoint")

    let withFlypoint = badgeOnly |> World.setFlag "ENGINE_FLYPOINT_GOLDENROD"
    match FieldMoves.tryUse "FLY" 0uy "GoldenrodCity" withFlypoint [ mon ] with
    | FieldMoves.Used("FLY", _) -> ()
    | other -> Assert.Fail(sprintf "expected FLY to be usable, got %A" other)

[<Fact>]
let ``PartyScene dispatches detected HM field move`` () =
    let mutable fieldMove = None
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove "CUT" [] }
    let player = { PlayerStateOps.initial with Party = [ mon ] }
    let scene =
        PartyScene(
            Content(),
            player,
            ignore,
            onFieldMove = (fun move -> fieldMove <- Some move; Stay))

    let update buttons = (scene :> Scene).Update buttons
    update { Buttons.none with A = true } |> ignore
    update Buttons.none |> ignore

    for _ in 1 .. 3 do
        update { Buttons.none with Down = true } |> ignore
        update Buttons.none |> ignore

    update { Buttons.none with A = true } |> ignore

    Assert.Equal(Some "CUT", fieldMove)

[<Fact>]
let ``Repel.blocks suppresses weak encounters when lead mon is strong enough`` () =
    let lead = PartyMon.create 155 10
    let player = { PlayerStateOps.initial with Party = [ lead ]; RepelSteps = 50 }

    Assert.True(PokeGold.Game.Player.Repel.blocks player 5)
    Assert.False(PokeGold.Game.Player.Repel.blocks player 15)
