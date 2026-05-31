namespace PokeGold.DataGen

open System.IO
open PokeGold.Game.Overworld.Script

/// Build-time loading of every map's static data using the shared `PokeGold.MapData`
/// parsers — the exact same `parseText` logic the runtime test-suite exercises, so
/// the baked tables and the live parser can never diverge. Produces a list of
/// `GeneratedMap` values ready for `EmitMaps` to render as F# literals.
module MapParsers =

    /// Every map's joined metadata (dimensions/group from map_constants, tileset/
    /// music/palette from maps.asm, border/connections from attributes.asm).
    let metas : MapMeta list =
        MapMetaParser.join
            (Repo.readText "constants/map_constants.asm")
            (Repo.readText "data/maps/maps.asm")
            (Repo.readText "data/maps/attributes.asm")

    /// Each map's full static record. A map whose `maps/<Name>.asm` is missing
    /// (should not happen for the real game) gets empty event/script/text tables
    /// rather than failing the whole generation.
    let maps : GeneratedMap list =
        [ for meta in metas do
              let path = Repo.path (sprintf "maps/%s.asm" meta.Name)

              let events, script, text =
                  if File.Exists path then
                      let asm = File.ReadAllText path
                      MapEventParser.parseText asm, ScriptParser.parseText asm, MapText.parseText asm
                  else
                      { Warps = [||]; Coords = [||]; Bgs = [||]; Objects = [||]; Scenes = [||] },
                      { Commands = [||]; Labels = Map.empty },
                      Map.empty

              yield
                  { Meta = meta
                    Events = events
                    Script = script
                    Text = text } ]
