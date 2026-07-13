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

[<Fact>]
let ``BAT-002 source starting moves skip duplicates and retain the latest four`` () =
    Assert.Equal<string list>(
        [ "QUICK_ATTACK"; "HYPER_FANG"; "FOCUS_ENERGY"; "PURSUIT" ],
        MoveLearn.startingMoveNames "RATTATA" 27)

    Assert.Equal<string list>([ "HARDEN" ], MoveLearn.startingMoveNames "METAPOD" 7)

[<Fact>]
let ``BAT-002 synthetic item plus moves trainer keeps explicit source slots`` () =
    let trainer : TrainerData = { Group = "SYNTHETIC"; Id = 1; Name = "SYNTHETIC"; PartyType = TrainerPartyType.ItemMoves; Party = [ { Species = "RAICHU"; Level = 14; HeldItem = Some "BERRY"; ExplicitMoves = [ "THUNDER_WAVE"; "QUICK_ATTACK"; "NO_MOVE"; "NO_MOVE" ] } ]; BaseReward = 1; Dvs = 0x8888; AiMoveFlags = []; AiItemSwitchFlags = []; AiItems = [] }

    Assert.Equal<string list>(
        [ "THUNDER_WAVE"; "QUICK_ATTACK" ],
        MoveLearn.trainerMoveNames trainer.Party.Head)
