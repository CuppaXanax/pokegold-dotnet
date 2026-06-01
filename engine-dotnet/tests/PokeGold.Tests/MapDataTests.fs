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

[<Fact>]
let ``the overworld sources its events from the baked table, not live asm`` () =
    // M10.1 — OverworldState consumes MapsData. Loading AzaleaTown must yield the
    // exact event/script/text tables baked into MapsData (proving the load path no
    // longer parses maps/<Name>.asm at runtime). Content decodes only binary/gfx
    // assets; the map data is the generated value.
    let content = PokeGold.Game.Data.Content()
    let state = PokeGold.Game.Overworld.OverworldState.loadById content "AzaleaTown"

    let baked =
        match MapsData.byName "AzaleaTown" with
        | Some m -> m
        | None -> failwith "AzaleaTown missing from generated map data"

    Assert.Equal<WarpEvent[]>(baked.Events.Warps, state.Events.Warps)
    Assert.Equal<ObjectEvent[]>(baked.Events.Objects, state.Events.Objects)
    Assert.Equal(baked.Script.Commands.Length, state.Script.Commands.Length)
    Assert.Equal<Map<string, string>>(baked.Text, state.Text)

[<Fact>]
let ``map music ids resolve to shipped song files`` () =
    // M10.2 — per-map music binding. Every map's baked Meta.Music that maps to a
    // shipped song must resolve through the generated MUSIC_* -> file table; the few
    // that don't (MUSIC_NONE, songs not yet in the tree) are simply absent.
    Assert.True(MusicData.byId.Count >= 90, sprintf "expected >=90 music bindings, got %d" MusicData.byId.Count)
    Assert.Equal("audio/music/azaleatown.asm", MusicData.byId.["MUSIC_AZALEA_TOWN"])
    Assert.Equal("audio/music/newbarktown.asm", MusicData.byId.["MUSIC_NEW_BARK_TOWN"])
    // The naming exception: MUSIC_TITLE -> Music_TitleScreen -> titlescreen.asm.
    Assert.Equal("audio/music/titlescreen.asm", MusicData.byId.["MUSIC_TITLE"])

    // AzaleaTown's baked music id binds to a real song file.
    let azalea =
        match MapsData.byName "AzaleaTown" with
        | Some m -> m
        | None -> failwith "AzaleaTown missing"

    Assert.True(MusicData.byId.ContainsKey azalea.Meta.Music)

// ---- M12.5 — shared blockdata so interiors are enterable --------------------

[<Fact>]
let ``parseBlocks resolves stacked shared labels to one file`` () =
    // Many interiors stack their `_Blocks` labels before a single INCBIN, so they
    // all share one .blk (the Mart/Pokecenter templates). A map with its own
    // dedicated file resolves to itself.
    let asm =
        "BlackthornMart_Blocks:\n\
         AzaleaMart_Blocks:\n\
         CherrygroveMart_Blocks:\n\
         \tINCBIN \"maps/Mart.blk\"\n\
         \n\
         AzaleaTown_Blocks:\n\
         \tINCBIN \"maps/AzaleaTown.blk\"\n"

    let blocks = MapMetaParser.parseBlocks asm
    Assert.Equal("Mart", blocks.["AzaleaMart"])
    Assert.Equal("Mart", blocks.["BlackthornMart"])
    Assert.Equal("Mart", blocks.["CherrygroveMart"])
    Assert.Equal("AzaleaTown", blocks.["AzaleaTown"])

[<Fact>]
let ``Azalea interiors bake their shared blockdata file`` () =
    let blocksOf name =
        match MapsData.byName name with
        | Some m -> m.Meta.Blocks
        | None -> failwithf "%s missing from generated map data" name

    // Shared templates: every Mart -> Mart.blk, every Pokémon Center -> Pokecenter1F.blk.
    Assert.Equal("Mart", blocksOf "AzaleaMart")
    Assert.Equal("Pokecenter1F", blocksOf "AzaleaPokecenter1F")
    // A map with a dedicated layout still points at its own file.
    Assert.Equal("AzaleaTown", blocksOf "AzaleaTown")

[<Fact>]
let ``warping into the Azalea Pokemon Center and Mart succeeds`` () =
    // The town's warps target the interiors by MAP_* const. Before M12.5 these
    // interiors had no maps/<Name>.blk (their layout is the shared Mart/Pokecenter1F
    // file), so canLoad failed and the warp silently no-opped. Now they load.
    let content = PokeGold.Game.Data.Content()

    // AzaleaTown warp #1 -> AZALEA_POKECENTER_1F warp 1; warp #3 -> AZALEA_MART warp 2.
    match PokeGold.Game.Overworld.OverworldState.tryWarp content "AZALEA_POKECENTER_1F" 1 with
    | Some s -> Assert.Equal("AzaleaPokecenter1F", s.MapId)
    | None -> Assert.Fail "expected to warp into AzaleaPokecenter1F"

    match PokeGold.Game.Overworld.OverworldState.tryWarp content "AZALEA_MART" 2 with
    | Some s -> Assert.Equal("AzaleaMart", s.MapId)
    | None -> Assert.Fail "expected to warp into AzaleaMart"

