module PokeGold.Tests.MoveLearnTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Player

[<Fact>]
let ``movesAtLevel returns moves learned at that level`` () =
    let moves = MoveLearn.movesAtLevel "BULBASAUR" 7
    Assert.Contains("LEECH_SEED", moves)

[<Fact>]
let ``tryLearnMove adds move when under 4 moves`` () =
    let result = MoveLearn.tryLearnMove "TACKLE" []
    Assert.True(result.Length > 0)

[<Fact>]
let ``tryLearnMove does not duplicate existing move`` () =
    let tackleIdx = MovesData.byIndex |> Array.tryFindIndex (fun move -> move.Name = "TACKLE")

    match tackleIdx with
    | Some idx ->
        let existing = [ idx, 35 ]
        let result = MoveLearn.tryLearnMove "TACKLE" existing
        Assert.Equal(1, result.Length)
    | None -> ()

[<Fact>]
let ``seedStartingMoves gives Cyndaquil TACKLE and LEER at level 5`` () =
    let mon = PartyMon.create (Species.byName "CYNDAQUIL").Dex 5
    let seeded = MoveLearn.seedStartingMoves mon

    let hasMove name =
        seeded.Moves
        |> List.exists (fun (moveId, _) -> MovesData.byIndex.[moveId].Name = name)

    Assert.True(seeded.Moves.Length > 0, "should have starting moves")
    Assert.True(hasMove "TACKLE", "should include TACKLE")
    Assert.True(hasMove "LEER", "should include LEER")
