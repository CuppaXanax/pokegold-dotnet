namespace PokeGold.DataGen

open System.IO
open System.Text.RegularExpressions

/// Build-time parsers that turn the disassembly's `.asm` data tables into plain
/// in-memory values, ready to be emitted as F# literals. All name -> id
/// resolution happens here so the emitted code carries only final integers.
module Parsers =

    // --- Types -------------------------------------------------------------

    /// Numeric type ids (`constants/type_constants.asm`).
    let typeIds : Map<string, int> = AsmConstants.load "constants/type_constants.asm"

    let private multValue =
        function
        | "SUPER_EFFECTIVE" -> 20
        | "NOT_VERY_EFFECTIVE" -> 5
        | "NO_EFFECT" -> 0
        | other -> failwithf "Unknown type matchup multiplier '%s'" other

    /// (attackerId, defenderId) -> multiplier x10, from `type_matchups.asm`.
    let typeMatchups : ((int * int) * int) list =
        let rx = Regex(@"^\s*db\s+([A-Za-z_]\w*),\s*([A-Za-z_]\w*),\s*([A-Za-z_]\w*)")

        [ for raw in Repo.readText("data/types/type_matchups.asm").Split('\n') do
              let m = rx.Match raw

              if m.Success then
                  match typeIds.TryFind m.Groups.[1].Value, typeIds.TryFind m.Groups.[2].Value with
                  | Some a, Some d -> yield (a, d), multValue m.Groups.[3].Value
                  | _ -> () ]

    // --- Species -----------------------------------------------------------

    type Species =
        { Constant: string
          Dex: int
          Hp: int
          Attack: int
          Defense: int
          Speed: int
          SpAttack: int
          SpDefense: int
          Type1: int
          Type2: int }

    let private dex = AsmConstants.load "constants/pokemon_constants.asm"
    let private idRx = Regex(@"^\s*db\s+([A-Za-z_]\w*)\b")
    let private statsRx = Regex(@"db\s+(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)")
    let private pairRx = Regex(@"db\s+([A-Za-z_]\w*),\s*([A-Za-z_]\w*)")

    let private parseSpecies (file: string) : Species =
        let lines =
            [ for raw in File.ReadAllText(file).Split('\n') ->
                  let i = raw.IndexOf(';')
                  if i >= 0 then raw.Substring(0, i) else raw ]

        // Canonical species constant: the leading `db IDENT` on the first line.
        let constant =
            lines
            |> List.tryPick (fun l ->
                let m = idRx.Match l
                if m.Success then Some m.Groups.[1].Value else None)
            |> Option.defaultWith (fun () -> failwithf "No species id in %s" file)

        let stats =
            lines
            |> List.tryPick (fun l ->
                let m = statsRx.Match l
                if m.Success then Some [ for g in 1..6 -> int m.Groups.[g].Value ] else None)
            |> Option.defaultWith (fun () -> failwithf "No base-stat line in %s" file)

        // The type line is the first `db IDENT, IDENT` whose idents are both types.
        let t1, t2 =
            lines
            |> List.tryPick (fun l ->
                let m = pairRx.Match l

                if m.Success then
                    match typeIds.TryFind m.Groups.[1].Value, typeIds.TryFind m.Groups.[2].Value with
                    | Some av, Some bv -> Some(av, bv)
                    | _ -> None
                else
                    None)
            |> Option.defaultWith (fun () -> failwithf "No type line in %s" file)

        { Constant = constant
          Dex = dex.[constant]
          Hp = stats.[0]
          Attack = stats.[1]
          Defense = stats.[2]
          Speed = stats.[3]
          SpAttack = stats.[4]
          SpDefense = stats.[5]
          Type1 = t1
          Type2 = t2 }

    /// Every species' base stats, ordered by national dex number.
    let species : Species list =
        Directory.GetFiles(Repo.path "data/pokemon/base_stats", "*.asm")
        |> Array.map parseSpecies
        |> Array.sortBy (fun s -> s.Dex)
        |> Array.toList

    // --- Moves -------------------------------------------------------------

    type Move =
        { Constant: string
          Effect: string
          Power: int
          Type: int
          Accuracy: int
          Pp: int
          EffectChance: int }

    let private moveRx =
        Regex(
            @"^\s*move\s+([A-Za-z0-9_]+),\s*([A-Za-z0-9_]+),\s*(\d+),\s*([A-Za-z_]\w*),\s*(\d+),\s*(\d+),\s*(\d+)"
        )

    /// Every move's data, in source order.
    let moves : Move list =
        [ for raw in Repo.readText("data/moves/moves.asm").Split('\n') do
              let m = moveRx.Match raw

              if m.Success then
                  yield
                      { Constant = m.Groups.[1].Value
                        Effect = m.Groups.[2].Value
                        Power = int m.Groups.[3].Value
                        Type = typeIds.[m.Groups.[4].Value]
                        Accuracy = int m.Groups.[5].Value
                        Pp = int m.Groups.[6].Value
                        EffectChance = int m.Groups.[7].Value } ]

    // --- Sprite movement data ----------------------------------------------

    /// One `SPRITEMOVEDATA_*` row: its movement function (`SPRITEMOVEFN_*`) and the
    /// object's initial facing (`DOWN`/`UP`/`LEFT`/`RIGHT`). These are the only two
    /// fields the high-level NPC engine needs from `data/sprites/map_objects.asm`.
    type SpriteMovement =
        { Constant: string
          Fn: string
          Facing: string }

    /// Every `SPRITEMOVEDATA_*` row in table order, from `data/sprites/map_objects.asm`.
    /// Stops at `assert_table_length` so the trailing unused entry is excluded.
    let spriteMovement : SpriteMovement list =
        let nameRx = Regex(@"^\s*;\s*(SPRITEMOVEDATA_\w+)")
        let dbRx = Regex(@"^\s*db\s+([A-Za-z0-9_]+)")
        let result = ResizeArray<SpriteMovement>()
        let mutable name = ""
        let mutable dbIndex = 0
        let mutable fn = ""
        let mutable stop = false

        for raw in Repo.readText("data/sprites/map_objects.asm").Split('\n') do
            if not stop then
                if raw.Contains "assert_table_length" then
                    stop <- true
                else
                    let nm = nameRx.Match raw

                    if nm.Success then
                        name <- nm.Groups.[1].Value
                        dbIndex <- 0
                    else
                        let db = dbRx.Match raw

                        if db.Success && name <> "" then
                            match dbIndex with
                            | 0 -> fn <- db.Groups.[1].Value
                            | 1 -> result.Add { Constant = name; Fn = fn; Facing = db.Groups.[1].Value }
                            | _ -> ()

                            dbIndex <- dbIndex + 1

        List.ofSeq result
