namespace PokeGold.Game.Debug

open System
open PokeGold.Game.Core
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Scenes

/// Parses and answers debug-pipe commands against a live [`OverworldScene`].
///
/// Every command runs on the game-update thread (the [`DebugChannel`] marshals it
/// there), so the inspectors below read a coherent snapshot and the mutators are
/// applied between frames. Replies are plain text — one or more lines — built for
/// a human or an agent reading the pipe.
module DebugCommands =

    let private dirName =
        function
        | Up -> "up"
        | Down -> "down"
        | Left -> "left"
        | Right -> "right"

    let private parseDir (s: string) : Direction option =
        match s.ToLowerInvariant() with
        | "up" -> Some Up
        | "down" -> Some Down
        | "left" -> Some Left
        | "right" -> Some Right
        | _ -> None

    let help =
        String.Join(
            "\n",
            [ "commands:"
              "  help                      this list"
              "  ping                      -> pong"
              "  frame                     current frame counter"
              "  scene                     active (top) scene type"
              "  player                    map, cell, facing, motion, pixel"
              "  map                       map id, size, camera, neighbours"
              "  npcs                      live overworld objects"
              "  flags                     set EVENT_*/ENGINE_* flags and VAR_*"
              "  bag                       items held"
              "  tp <x> <y>                teleport player on the current map"
              "  warp <map> <x> <y> [dir]  load another map at a cell"
              "  setflag <EVENT_*>         set an event flag"
              "  clearflag <EVENT_*>       clear an event flag"
              "  setvar <VAR_*> <n>        set a script variable" ]
        )

    let private playerInfo (scene: OverworldScene) : string =
        let s = scene.DebugState
        let p = s.Player
        let px, py = Player.worldPixel p

        String.Join(
            "\n",
            [ $"map     {s.MapId}"
              $"cell    {p.CellX},{p.CellY}"
              $"facing  {dirName p.Facing}"
              $"motion  {p.Motion}"
              $"pixel   {px},{py}" ]
        )

    let private mapInfo (scene: OverworldScene) : string =
        let s = scene.DebugState
        let neighbors = s.Neighbors |> List.map (fun n -> n.Placement.Conn.Map) |> List.toArray
        let neighborList = String.Join(", ", neighbors)

        String.Join(
            "\n",
            [ $"id        {s.MapId}"
              $"blocks    {s.Map.Width}x{s.Map.Height}"
              $"cells     {s.Map.Width * 2}x{s.Map.Height * 2}"
              $"camera    {s.CamX},{s.CamY}"
              $"neighbors {neighbors.Length}: {neighborList}" ]
        )

    let private npcsInfo (scene: OverworldScene) : string =
        let npcs = scene.DebugState.Npcs

        if npcs.Length = 0 then
            "(no objects)"
        else
            npcs
            |> Array.mapi (fun i n ->
                let vis = if scene.DebugVisible n.Event then "vis" else "hid"
                let flag = n.Event.EventFlag |> Option.defaultValue "-"
                $"[{i}] {n.Event.Sprite} @ {n.CellX},{n.CellY} {vis} flag={flag} script={n.Event.Script}")
            |> String.concat "\n"

    let private flagsInfo (scene: OverworldScene) : string =
        let w = scene.DebugWorld
        let events = w.Events |> Set.toList
        let engine = w.EngineFlags |> Set.toList
        let vars = w.Vars |> Map.toList |> List.map (fun (k, v) -> $"{k}={v}")
        let eventList = String.Join(", ", events)
        let engineList = String.Join(", ", engine)
        let varList = String.Join(", ", vars)

        String.Join(
            "\n",
            [ $"events ({events.Length}): {eventList}"
              $"engine ({engine.Length}): {engineList}"
              $"vars   ({vars.Length}): {varList}" ]
        )

    let private bagInfo (scene: OverworldScene) : string =
        let items = scene.DebugBag |> Map.toList

        if items.IsEmpty then
            "(empty)"
        else
            items |> List.map (fun (k, v) -> $"{k} x{v}") |> String.concat "\n"

    /// Answer one command line against the live scene. `frame` and `topScene`
    /// are supplied by the game (which owns the frame counter and scene stack).
    let dispatch (scene: OverworldScene) (frame: uint64) (topScene: string) (line: string) : string =
        let parts =
            line.Trim().Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)

        if parts.Length = 0 then
            ""
        else
            let arg i = if i < parts.Length then Some parts.[i] else None
            let tryInt (s: string) = match Int32.TryParse s with | true, v -> Some v | _ -> None

            match parts.[0].ToLowerInvariant() with
            | "help" -> help
            | "ping" -> "pong"
            | "frame" -> string frame
            | "scene" -> topScene
            | "player" -> playerInfo scene
            | "map" -> mapInfo scene
            | "npcs" -> npcsInfo scene
            | "flags" -> flagsInfo scene
            | "bag" -> bagInfo scene
            | "tp" ->
                match arg 1 |> Option.bind tryInt, arg 2 |> Option.bind tryInt with
                | Some x, Some y ->
                    scene.DebugTeleport x y
                    $"ok: player -> {x},{y}"
                | _ -> "usage: tp <x> <y>"
            | "warp" ->
                match arg 1, arg 2 |> Option.bind tryInt, arg 3 |> Option.bind tryInt with
                | Some map, Some x, Some y ->
                    let facing =
                        arg 4 |> Option.bind parseDir |> Option.defaultValue scene.DebugState.Player.Facing

                    scene.DebugWarp map x y facing
                    $"ok: warped to {map} @ {x},{y} facing {dirName facing}"
                | _ -> "usage: warp <map> <x> <y> [up|down|left|right]"
            | "setflag" ->
                match arg 1 with
                | Some flag ->
                    scene.DebugSetEvent flag true
                    $"ok: set {flag}"
                | None -> "usage: setflag <EVENT_*>"
            | "clearflag" ->
                match arg 1 with
                | Some flag ->
                    scene.DebugSetEvent flag false
                    $"ok: cleared {flag}"
                | None -> "usage: clearflag <EVENT_*>"
            | "setvar" ->
                match arg 1, arg 2 |> Option.bind tryInt with
                | Some var, Some n ->
                    scene.DebugSetVar var n
                    $"ok: {var} = {n}"
                | _ -> "usage: setvar <VAR_*> <n>"
            | other -> $"unknown command '{other}' (try 'help')"
