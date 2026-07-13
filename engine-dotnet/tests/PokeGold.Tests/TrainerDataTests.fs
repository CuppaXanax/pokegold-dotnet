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
    Assert.Equal(TrainerPartyType.Moves, trainer.PartyType)
    Assert.Equal(0x9A77, trainer.Dvs)
    Assert.Equal(2, trainer.Party.Length)
    Assert.Equal("PIDGEY", trainer.Party.[0].Species)
    Assert.Equal(7, trainer.Party.[0].Level)
    Assert.Equal<string list>([ "TACKLE"; "MUD_SLAP"; "NO_MOVE"; "NO_MOVE" ], trainer.Party.[0].ExplicitMoves)
    Assert.Equal(None, trainer.Party.[0].HeldItem)
    Assert.Equal("PIDGEOTTO", trainer.Party.[1].Species)
    Assert.Equal(9, trainer.Party.[1].Level)
    Assert.Equal<string list>([ "TACKLE"; "MUD_SLAP"; "GUST"; "NO_MOVE" ], trainer.Party.[1].ExplicitMoves)
    Assert.Equal(None, trainer.Party.[1].HeldItem)

[<Fact>]
let ``trainer lookup decodes Bug Catcher party levels and species`` () =
    let trainer = Trainers.lookup "BUG_CATCHER" 1 |> Option.defaultWith (fun () -> failwith "BUG_CATCHER not found")

    Assert.Equal("BUG_CATCHER", trainer.Group)
    Assert.Equal(1, trainer.Id)
    Assert.Equal("DON", trainer.Name)
    Assert.Equal(4, trainer.BaseReward)
    Assert.Equal(TrainerPartyType.Normal, trainer.PartyType)
    Assert.Equal(0x9888, trainer.Dvs)
    Assert.Equal<string list>([ "CATERPIE"; "CATERPIE" ], trainer.Party |> List.map (fun mon -> mon.Species))
    Assert.Equal<int list>([ 3; 3 ], trainer.Party |> List.map (fun mon -> mon.Level))
    Assert.All(trainer.Party, fun mon -> Assert.Empty(mon.ExplicitMoves))
    Assert.All(trainer.Party, fun mon -> Assert.Equal(None, mon.HeldItem))

[<Fact>]
let ``trainer lookup preserves item party fields`` () =
    let trainer = Trainers.lookup "POKEFANM" 1 |> Option.defaultWith (fun () -> failwith "POKEFANM 1 not found")

    Assert.Equal(TrainerPartyType.Item, trainer.PartyType)
    Assert.Equal(0x9888, trainer.Dvs)
    Assert.Single(trainer.Party) |> ignore
    Assert.Equal("RAICHU", trainer.Party.Head.Species)
    Assert.Equal(14, trainer.Party.Head.Level)
    Assert.Equal(Some "BERRY", trainer.Party.Head.HeldItem)
    Assert.Empty(trainer.Party.Head.ExplicitMoves)

[<Fact>]
let ``all source trainer records preserve their declared layout`` () =
    let trainers = Trainers.all |> Map.toList |> List.map snd

    Assert.Equal(495, trainers.Length)
    Assert.Equal(389, trainers |> List.filter (fun trainer -> trainer.PartyType = TrainerPartyType.Normal) |> List.length)
    Assert.Equal(89, trainers |> List.filter (fun trainer -> trainer.PartyType = TrainerPartyType.Moves) |> List.length)
    Assert.Equal(17, trainers |> List.filter (fun trainer -> trainer.PartyType = TrainerPartyType.Item) |> List.length)
    Assert.DoesNotContain(trainers, fun trainer -> trainer.PartyType = TrainerPartyType.ItemMoves)

    for trainer in trainers do
        Assert.InRange(trainer.Party.Length, 1, 6)
        Assert.InRange(trainer.Dvs, 1, 0xFFFF)

        for mon in trainer.Party do
            match trainer.PartyType with
            | TrainerPartyType.Normal ->
                Assert.Equal(None, mon.HeldItem)
                Assert.Empty(mon.ExplicitMoves)
            | TrainerPartyType.Moves ->
                Assert.Equal(None, mon.HeldItem)
                Assert.Equal(4, mon.ExplicitMoves.Length)
            | TrainerPartyType.Item ->
                Assert.True(mon.HeldItem.IsSome)
                Assert.Empty(mon.ExplicitMoves)
            | TrainerPartyType.ItemMoves ->
                Assert.True(mon.HeldItem.IsSome)
                Assert.Equal(4, mon.ExplicitMoves.Length)

[<Fact>]
let ``boss trainer classes preserve source packed DVs`` () =
    [ "FALKNER", 0x9A77
      "WHITNEY", 0x8888
      "CHAMPION", 0xDCDD
      "RED", 0xFDDE ]
    |> List.iter (fun (group, expectedDvs) ->
        let trainer = Trainers.lookup group 1 |> Option.defaultWith (fun () -> failwith $"{group} not found")
        Assert.Equal(expectedDvs, trainer.Dvs))

[<Fact>]
let ``BAT-024 generated trainer AI profiles preserve boss source attributes`` () =
    let falkner = Trainers.lookup "FALKNER" 1 |> Option.defaultWith (fun () -> failwith "FALKNER not found")
    let lance = Trainers.lookup "CHAMPION" 1 |> Option.defaultWith (fun () -> failwith "LANCE not found")
    let red = Trainers.lookup "RED" 1 |> Option.defaultWith (fun () -> failwith "RED not found")

    Assert.Equal<string list>([ "AI_BASIC"; "AI_SETUP"; "AI_SMART"; "AI_AGGRESSIVE"; "AI_CAUTIOUS"; "AI_STATUS"; "AI_RISKY" ], falkner.AiMoveFlags)
    Assert.Equal<string list>([ "CONTEXT_USE"; "SWITCH_SOMETIMES" ], falkner.AiItemSwitchFlags)
    Assert.Empty(falkner.AiItems)
    Assert.Equal<string list>([ "FULL_HEAL"; "FULL_RESTORE" ], lance.AiItems)
    Assert.Equal<string list>([ "FULL_RESTORE"; "FULL_RESTORE" ], red.AiItems)

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

[<Fact>]
let ``all generated loadwildmon operands resolve exactly`` () =
    let unresolved =
        [ for KeyValue(mapId, map) in MapsData.all do
              for command in map.Script.Commands do
                  match command with
                  | Loadwildmon(species, _) when Species.all |> Map.containsKey species |> not ->
                      yield $"{mapId}: {species}"
                  | _ -> () ]

    Assert.True(List.isEmpty unresolved, "Unresolved loadwildmon operands: " + System.String.Join("; ", unresolved))
