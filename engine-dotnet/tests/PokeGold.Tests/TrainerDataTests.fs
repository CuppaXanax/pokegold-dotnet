module PokeGold.Tests.TrainerDataTests

open Xunit
open PokeGold.Game.Data

[<Fact>]
let ``trainer lookup decodes Falkner's first party`` () =
    let trainer = Trainers.lookup "FALKNER" 1 |> Option.defaultWith (fun () -> failwith "FALKNER not found")

    Assert.Equal("FALKNER", trainer.Group)
    Assert.Equal(1, trainer.Id)
    Assert.Equal("FALKNER", trainer.Name)
    Assert.Equal(25, trainer.BaseReward)
    Assert.Equal(2, trainer.Party.Length)
    Assert.Equal("PIDGEY", trainer.Party.[0].Species)
    Assert.Equal(7, trainer.Party.[0].Level)
    Assert.Equal("PIDGEOTTO", trainer.Party.[1].Species)
    Assert.Equal(9, trainer.Party.[1].Level)

[<Fact>]
let ``trainer lookup decodes Bug Catcher party levels and species`` () =
    let trainer = Trainers.lookup "BUG_CATCHER" 1 |> Option.defaultWith (fun () -> failwith "BUG_CATCHER not found")

    Assert.Equal("BUG_CATCHER", trainer.Group)
    Assert.Equal(1, trainer.Id)
    Assert.Equal("DON", trainer.Name)
    Assert.Equal(4, trainer.BaseReward)
    Assert.Equal<string list>([ "CATERPIE"; "CATERPIE" ], trainer.Party |> List.map (fun mon -> mon.Species))
    Assert.Equal<int list>([ 3; 3 ], trainer.Party |> List.map (fun mon -> mon.Level))
