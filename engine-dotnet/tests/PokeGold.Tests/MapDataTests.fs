module PokeGold.Tests.MapDataTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Overworld.Script

// M10.0 — build-time map data generation. The `.asm` map sources (metadata,
// connections, events, scripts, text) are parsed at build time by PokeGold.DataGen
// and baked into `Maps.Generated.fs` as the typed `MapsData` table. These tests gate
// that the generated table is complete and structurally sound — no runtime `.asm`
// parsing is involved.

[<Fact>]
let ``all 368 maps are generated`` () =
    Assert.Equal(368, MapsData.all.Count)

[<Fact>]
let ``map connections are populated across the world`` () =
    let totalConnections =
        MapsData.all
        |> Seq.sumBy (fun kv -> kv.Value.Meta.Connections.Length)

    Assert.True(
        totalConnections > 100,
        sprintf "expected >100 map connections, got %d" totalConnections
    )

[<Fact>]
let ``NewBarkTown metadata and warps are baked correctly`` () =
    let m =
        match MapsData.byName "NewBarkTown" with
        | Some m -> m
        | None -> failwith "NewBarkTown missing from generated map data"

    Assert.Equal("NEW_BARK_TOWN", m.Meta.Const)
    Assert.Equal("MUSIC_NEW_BARK_TOWN", m.Meta.Music)
    Assert.Equal(10, m.Meta.WidthBlocks)
    Assert.Equal(9, m.Meta.HeightBlocks)

    let elmsLab =
        m.Events.Warps
        |> Array.tryFind (fun w -> w.DestMap = "ELMS_LAB")

    Assert.True(elmsLab.IsSome, "expected a warp to ELMS_LAB")

[<Fact>]
let ``every generated map has a non-empty const and matching name`` () =
    for kv in MapsData.all do
        Assert.Equal(kv.Key, kv.Value.Meta.Name)
        Assert.False(System.String.IsNullOrWhiteSpace kv.Value.Meta.Const)
