namespace PokeGold.DataGen

open System.IO
open System.Text.RegularExpressions
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
            (Repo.readText "data/maps/blocks.asm")

    /// Source-defined whiteout destinations from data/maps/spawn_points.asm.
    /// Each entry retains the ROM map constant and resolves its runtime map id.
    let spawnPoints : (string * string * int * int) list =
        let pattern = Regex(@"^\s*spawn\s+([A-Z0-9_]+),\s*(-?\d+),\s*(-?\d+)", RegexOptions.Multiline)
        let source = Repo.readText "data/maps/spawn_points.asm"

        [ for m in pattern.Matches(source) do
              let mapConst = m.Groups.[1].Value
              if mapConst <> "N_A" then
                  let runtimeName =
                      metas
                      |> List.tryFind (fun meta -> meta.Const = mapConst)
                      |> Option.map (fun meta -> meta.Name)
                      |> Option.defaultWith (fun () -> failwithf "Unknown spawn-point map %s" mapConst)
                  yield mapConst, runtimeName, int m.Groups.[2].Value, int m.Groups.[3].Value ]

    let private addFirst (key: string) (value: int) (map: Map<string, int>) =
        if Map.containsKey key map then map else Map.add key value map

    let private mergeConstants (source: Map<string, int>) (target: Map<string, int>) =
        source |> Map.fold (fun acc key value -> addFirst key value acc) target

    let private constantsDirConstants () =
        Directory.GetFiles(Repo.path "constants", "*.asm")
        |> Array.map (fun path -> "constants/" + Path.GetFileName path)
        |> Array.fold (fun acc relative -> mergeConstants (AsmConstants.load relative) acc) Map.empty

    let private allSceneConstants () =
        [ for meta in metas do
              let path = Repo.path (sprintf "maps/%s.asm" meta.Name)

              if File.Exists path then
                  let events = MapEventParser.parseText (File.ReadAllText path)

                  for i, scene in events.Scenes |> Seq.indexed do
                      if scene <> "" then
                          yield scene, i ]
        |> List.fold (fun acc (scene, value) -> addFirst scene value acc) Map.empty

    let private scriptConstants : Map<string, int> =
        constantsDirConstants ()
        |> mergeConstants (AsmConstants.load "data/mon_menu.asm")
        |> mergeConstants (allSceneConstants ())

    /// Each map's full static record. A map whose `maps/<Name>.asm` is missing
    /// (should not happen for the real game) gets empty event/script/text tables
    /// rather than failing the whole generation.
    let maps : GeneratedMap list =
        [ for meta in metas do
              let path = Repo.path (sprintf "maps/%s.asm" meta.Name)

              let events, script, text =
                  if File.Exists path then
                      let asm = File.ReadAllText path
                      MapEventParser.parseText asm, ScriptParser.parseTextWithConstants scriptConstants asm, MapText.parseText asm
                  else
                      { Warps = [||]; Coords = [||]; Bgs = [||]; Objects = [||]; Scenes = [||]; SceneLabels = [||]; Callbacks = [||] },
                      { Commands = [||]; Labels = Map.empty },
                      Map.empty

              let movements, objectConsts =
                  if File.Exists path then
                      let asm = File.ReadAllText path
                      MovementParser.parseMovements asm, MovementParser.parseObjectConsts asm
                  else
                      Map.empty, [||]

              yield
                  { Meta = meta
                    Events = events
                    Script = script
                    Text = text
                    Movements = movements
                    ObjectConsts = objectConsts } ]

    /// Source Fly destinations joined to their spawn-point locations and engine
    /// flags. Most flags are declared by the map's new-map callback; Rock Tunnel
    /// uses the matching source engine-flag name because it has no such callback.
    let flyPoints : FlyPoint list =
        let callbackFlags =
            maps
            |> Seq.collect (fun map ->
                map.Script.Commands
                |> Seq.choose (function
                    | Setflag flag when flag.StartsWith("ENGINE_FLYPOINT_") -> Some(map.Meta.Landmark, flag)
                    | _ -> None))
            |> Seq.groupBy fst
            |> Seq.map (fun (landmark, entries) ->
                let flags = entries |> Seq.map snd |> Seq.distinct |> Seq.toList

                match flags with
                | [ flag ] -> landmark, flag
                | _ -> failwithf "Expected one flypoint flag for %s, found %A" landmark flags)
            |> Map.ofSeq

        let knownFlags =
            Regex.Matches(Repo.readText "constants/engine_flags.asm", @"^\s*const\s+(ENGINE_FLYPOINT_[A-Z0-9_]+)", RegexOptions.Multiline)
            |> Seq.cast<Match>
            |> Seq.map (fun m -> m.Groups.[1].Value)
            |> Set.ofSeq

        let spawnIds =
            Regex.Matches(Repo.readText "constants/map_data_constants.asm", @"^\s*const\s+(SPAWN_[A-Z0-9_]+)", RegexOptions.Multiline)
            |> Seq.cast<Match>
            |> Seq.map (fun m -> m.Groups.[1].Value)
            |> Seq.toList

        if spawnIds.Length <> spawnPoints.Length then
            failwithf "Spawn constant count %d does not match spawn table count %d" spawnIds.Length spawnPoints.Length

        let spawnById =
            List.zip spawnIds spawnPoints
            |> List.map (fun (spawnId, (_, mapId, x, y)) -> spawnId, (mapId, x, y))
            |> Map.ofList

        let pointPattern = Regex(@"^\s*db\s+(LANDMARK_[A-Z0-9_]+),\s+(SPAWN_[A-Z0-9_]+)", RegexOptions.Multiline)
        let source = Repo.readText "data/maps/flypoints.asm"

        [ for m in pointPattern.Matches(source) do
              let landmark = m.Groups.[1].Value
              let spawn = m.Groups.[2].Value
              let mapId, x, y =
                  Map.tryFind spawn spawnById
                  |> Option.defaultWith (fun () -> failwithf "Unknown Fly spawn %s" spawn)
              let fallbackFlag = "ENGINE_FLYPOINT_" + landmark.Substring("LANDMARK_".Length)
              let flag = Map.tryFind landmark callbackFlags |> Option.defaultValue fallbackFlag

              if not (Set.contains flag knownFlags) then
                  failwithf "Unknown Fly engine flag %s for %s" flag landmark

              yield
                  { Landmark = landmark
                    Spawn = spawn
                    Flag = flag
                    MapId = mapId
                    X = x
                    Y = y } ]

    /// The shared *standard* scripts (`engine/events/std_scripts.asm`) — the
    /// `jumpstd`/`callstd` targets (PokecenterNurseScript, bookshelves, signs, …) —
    /// parsed into one program addressed by label.
    let stdScripts: ScriptProgram =
        ScriptParser.parseTextWithConstants scriptConstants (Repo.readText "engine/events/std_scripts.asm")

    /// The standard scripts' text (`data/text/std_text.asm`) resolved to M5 token
    /// strings, so std-script `writetext` labels render in-game.
    let stdText: Map<string, string> =
        MapText.parseText (Repo.readText "data/text/std_text.asm")
