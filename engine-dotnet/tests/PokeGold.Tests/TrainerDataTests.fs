module PokeGold.Tests.TrainerDataTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Overworld.Script

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

[<Fact>]
let ``trainer lookup by constant resolves exact group id`` () =
    let trainer = Trainers.lookupByName "HIKER" "ANTHONY2" |> Option.defaultWith (fun () -> failwith "ANTHONY2 not found")

    Assert.Equal("HIKER", trainer.Group)
    Assert.Equal(5, trainer.Id)
    Assert.Equal("ANTHONY", trainer.Name)

[<Fact>]
let ``all generated loadtrainer operands resolve exactly`` () =
    let unresolved =
        [ for KeyValue(mapId, map) in MapsData.all do
              for command in map.Script.Commands do
                  match command with
                  | Loadtrainer(group, trainer) when Trainers.lookupByName group trainer |> Option.isNone ->
                      yield $"{mapId}: {group}, {trainer}"
                  | _ -> () ]

    Assert.True(List.isEmpty unresolved, "Unresolved loadtrainer operands: " + System.String.Join("; ", unresolved))
