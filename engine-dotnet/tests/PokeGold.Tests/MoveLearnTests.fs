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
