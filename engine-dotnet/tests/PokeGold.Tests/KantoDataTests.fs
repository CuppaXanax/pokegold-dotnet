module PokeGold.Tests.KantoDataTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Overworld.Script

[<Fact>]
let ``Kanto Route 1 has wild encounter data`` () =
    Assert.True(WildEncounters.forMap "ROUTE_1" |> Option.isSome)

[<Fact>]
let ``Brock is in trainer data`` () =
    Assert.True(Trainers.lookup "BROCK" 1 |> Option.isSome)

[<Fact>]
let ``Red is in trainer data`` () =
    Assert.True(Trainers.lookup "RED" 1 |> Option.isSome)

[<Fact>]
let ``key Kanto map scripts are baked into generated map data`` () =
    let hasMap name = MapsData.byName name |> Option.isSome

    Assert.True(hasMap "PewterCity")
    Assert.True(hasMap "ViridianGym")
    Assert.True(hasMap "CeruleanCity")

[<Fact>]
let ``all 8 Kanto gym leaders have trainer data`` () =
    let leaders = [ "BROCK"; "MISTY"; "LT_SURGE"; "ERIKA"; "JANINE"; "SABRINA"; "BLAINE"; "BLUE" ]

    for leader in leaders do
        Assert.True(Trainers.lookup leader 1 |> Option.isSome, $"{leader} should have trainer data")

[<Fact>]
let ``Red on Mt Silver uses the standard trainer script pattern`` () =
    match MapsData.byName "SilverCaveRoom3" with
    | Some map ->
        let hasLoadTrainer =
            map.Script.Commands
            |> Array.exists (function
                | Loadtrainer("RED", "RED1") -> true
                | _ -> false)

        Assert.True(hasLoadTrainer, "expected the Red encounter script to contain RED/RED1")
    | None ->
        Assert.Fail("SilverCaveRoom3 should exist in generated map data")
