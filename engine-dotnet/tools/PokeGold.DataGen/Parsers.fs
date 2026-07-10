namespace PokeGold.DataGen

open System
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

    // --- Collision ---------------------------------------------------------

    type CollisionData =
        { Land: byte
          Water: byte
          Wall: byte
          Permissions: byte[]
          Tilesets: Map<string, byte[]> }

    let private collisionConstRx = Regex(@"^\s*DEF\s+(\w+)\s+EQU\s+(.+?)\s*$", RegexOptions.IgnoreCase)

    let private evalCollisionExpr (consts: Map<string, int>) (expr: string) : int =
        let operand (tok: string) : int =
            let t = tok.Trim()

            if t.Length = 0 then 0
            elif t.StartsWith "$" then Convert.ToInt32(t.Substring 1, 16)
            elif t.StartsWith "0x" || t.StartsWith "0X" then Convert.ToInt32(t.Substring 2, 16)
            else
                match consts.TryFind t with
                | Some v -> v
                | None ->
                    match System.Int32.TryParse t with
                    | true, v -> v
                    | _ -> 0

        expr.Split('|')
        |> Array.fold
            (fun acc term ->
                let v =
                    term.Split([| "<<" |], System.StringSplitOptions.None)
                    |> Array.map operand
                    |> Array.reduce (fun a b -> a <<< b)

                acc ||| v)
            0

    let private parseCollisionConstants (text: string) : Map<string, int> =
        text.Split([| '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.fold
            (fun consts raw ->
                let line =
                    let i = raw.IndexOf(';')
                    if i >= 0 then raw.Substring(0, i) else raw

                let m = collisionConstRx.Match line

                if m.Success then
                    let name = m.Groups.[1].Value
                    let value = evalCollisionExpr consts m.Groups.[2].Value
                    Map.add name value consts
                else
                    consts)
            Map.empty

    let private parseCollisionPermissions (consts: Map<string, int>) (text: string) : byte[] =
        text.Split([| '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.choose (fun raw ->
            let line =
                let i = raw.IndexOf(';')
                if i >= 0 then raw.Substring(0, i) else raw

            let t = line.Trim()

            if t.StartsWith "db " then Some(byte (evalCollisionExpr consts (t.Substring 3)))
            else None)

    let private parseCollisionTileset (consts: Map<string, int>) (text: string) : byte[] =
        text.Split([| '\n'; '\r' |], System.StringSplitOptions.RemoveEmptyEntries)
        |> Array.choose (fun raw ->
            let line =
                let i = raw.IndexOf(';')
                if i >= 0 then raw.Substring(0, i) else raw

            let t = line.Trim()

            if t.StartsWith "tilecoll" then
                let ids =
                    t.Substring(8).Split(',')
                    |> Array.map (fun s ->
                        let name = "COLL_" + s.Trim()

                        match consts.TryFind name with
                        | Some v -> byte v
                        | None -> 0uy)

                if ids.Length = 4 then Some ids else None
            else
                None)
        |> Array.concat

    let collision : CollisionData =
        let consts = parseCollisionConstants (Repo.readText "constants/collision_constants.asm")
        let permissions = parseCollisionPermissions consts (Repo.readText "data/collision/collision_permissions.asm")

        if permissions.Length <> 256 then
            failwithf "Expected 256 collision permission bytes, found %d" permissions.Length

        let tilesets =
            Directory.GetFiles(Repo.path "data/tilesets", "*_collision.asm")
            |> Array.map (fun path ->
                let name = Path.GetFileNameWithoutExtension path
                let tilesetName = name.Substring(0, name.Length - "_collision".Length)
                tilesetName, parseCollisionTileset consts (File.ReadAllText path))
            |> Map.ofArray

        let lookup k =
            match consts.TryFind k with
            | Some v -> byte v
            | None -> 0uy

        { Land = lookup "LAND_TILE"
          Water = lookup "WATER_TILE"
          Wall = lookup "WALL_TILE"
          Permissions = permissions
          Tilesets = tilesets }

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
          Type2: int
          CatchRate: int
          BaseExp: int
          GrowthRate: int }

    let private dex = AsmConstants.load "constants/pokemon_constants.asm"
    let private idRx = Regex(@"^\s*db\s+([A-Za-z_]\w*)\b")
    let private statsRx = Regex(@"db\s+(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)")
    let private pairRx = Regex(@"db\s+([A-Za-z_]\w*),\s*([A-Za-z_]\w*)")
    let private singleIntDbRx = Regex(@"^\s*db\s+(\d+)\s*$")
    let private growthRx = Regex(@"^\s*db\s+(GROWTH_\w+)")
    let private growthRates =
        Map.ofList [
            "GROWTH_MEDIUM_FAST", 0
            "GROWTH_SLIGHTLY_FAST", 1
            "GROWTH_SLIGHTLY_SLOW", 2
            "GROWTH_MEDIUM_SLOW", 3
            "GROWTH_FAST", 4
            "GROWTH_SLOW", 5
        ]

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
        let typeLineIndex, t1, t2 =
            lines
            |> List.mapi (fun i l -> i, l)
            |> List.tryPick (fun (i, l) ->
                let m = pairRx.Match l

                if m.Success then
                    match typeIds.TryFind m.Groups.[1].Value, typeIds.TryFind m.Groups.[2].Value with
                    | Some av, Some bv -> Some(i, av, bv)
                    | _ -> None
                else
                    None)
            |> Option.defaultWith (fun () -> failwithf "No type line in %s" file)

        let singleDbAfterType =
            lines
            |> List.mapi (fun i l -> i, l)
            |> List.filter (fun (i, l) -> i > typeLineIndex && singleIntDbRx.IsMatch l)
            |> List.map (fun (_, l) -> int (singleIntDbRx.Match(l).Groups.[1].Value))

        let catchRate = if singleDbAfterType.Length > 0 then singleDbAfterType.[0] else 0
        let baseExp = if singleDbAfterType.Length > 1 then singleDbAfterType.[1] else 0

        let growthRate =
            lines
            |> List.tryPick (fun l ->
                let m = growthRx.Match l
                if m.Success then growthRates.TryFind m.Groups.[1].Value else None)
            |> Option.defaultValue 0

        { Constant = constant
          Dex = dex.[constant]
          Hp = stats.[0]
          Attack = stats.[1]
          Defense = stats.[2]
          Speed = stats.[3]
          SpAttack = stats.[4]
          SpDefense = stats.[5]
          Type1 = t1
          Type2 = t2
          CatchRate = catchRate
          BaseExp = baseExp
          GrowthRate = growthRate }

    /// Every species' base stats, ordered by national dex number.
    let species : Species list =
        Directory.GetFiles(Repo.path "data/pokemon/base_stats", "*.asm")
        |> Array.map parseSpecies
        |> Array.sortBy (fun s -> s.Dex)
        |> Array.toList

    // --- Evolutions and learnsets ------------------------------------------

    type EvolutionEntry =
        { Method: string
          Param: string
          Param2: string
          Target: string }

    type LearnsetEntry =
        { Level: int
          Move: string }

    type EvosAttacks =
        { Species: string
          Evolutions: EvolutionEntry list
          Learnset: LearnsetEntry list }

    let private evosLabelRx = Regex(@"^\s*([A-Za-z0-9_]+)EvosAttacks:\s*$")
    let private speciesLabelMap =
        dex
        |> Map.keys
        |> Seq.map (fun constant -> constant.ToUpperInvariant().Replace("_", ""), constant)
        |> Map.ofSeq

    let private speciesConstant (label: string) : string =
        let key = label.ToUpperInvariant().Replace("_", "")

        match speciesLabelMap.TryFind key with
        | Some constant -> constant
        | None -> label.ToUpperInvariant()
    let private evoDbRx = Regex(@"^\s*db\s+(EVOLVE_[A-Z0-9_]+)\s*,\s*([^,]+?)\s*,\s*([^,]+?)\s*$")
    let private evoStatDbRx = Regex(@"^\s*db\s+(EVOLVE_STAT)\s*,\s*([^,]+?)\s*,\s*([^,]+?)\s*,\s*([^,]+?)\s*$")
    let private learnsetDbRx = Regex(@"^\s*db\s+(\d+)\s*,\s*([A-Za-z0-9_]+)\s*$")

    /// Every species' evolution and level-up learnset data, in source order.
    let evosAttacks : EvosAttacks list =
        let lines = Repo.readText("data/pokemon/evos_attacks.asm").Split('\n')

        let blocks =
            [ let mutable currentLabel = ""
              let mutable currentLines = ResizeArray<string>()

              for raw in lines do
                  let line =
                      let i = raw.IndexOf(';')
                      if i >= 0 then raw.Substring(0, i) else raw

                  let labelMatch = evosLabelRx.Match line

                  if labelMatch.Success then
                      if currentLabel <> "" then
                          yield currentLabel, List.ofSeq currentLines
                      currentLabel <- labelMatch.Groups.[1].Value
                      currentLines <- ResizeArray<string>()
                  else
                      currentLines.Add line

              if currentLabel <> "" then
                  yield currentLabel, List.ofSeq currentLines ]

        [ for species, block in blocks do
              let mutable inEvolutions = true
              let evolutions = ResizeArray<EvolutionEntry>()
              let learnset = ResizeArray<LearnsetEntry>()

              for raw in block do
                  if inEvolutions then
                      if raw.TrimStart().StartsWith("db 0") then
                          inEvolutions <- false
                      else
                          let evoMatch = evoDbRx.Match raw
                          let evoStatMatch = evoStatDbRx.Match raw

                          match evoMatch.Success, evoStatMatch.Success with
                          | true, false ->
                              let method = evoMatch.Groups.[1].Value
                              let param = evoMatch.Groups.[2].Value.Trim()
                              let target = evoMatch.Groups.[3].Value.Trim()

                              evolutions.Add {
                                  Method = method
                                  Param = param
                                  Param2 = ""
                                  Target = target }
                          | false, true ->
                              let method = evoStatMatch.Groups.[1].Value
                              let param = evoStatMatch.Groups.[2].Value.Trim()
                              let param2 = evoStatMatch.Groups.[3].Value.Trim()
                              let target = evoStatMatch.Groups.[4].Value.Trim()

                              evolutions.Add {
                                  Method = method
                                  Param = param
                                  Param2 = param2
                                  Target = target }
                          | _ -> ()
                  else
                      let learnMatch = learnsetDbRx.Match raw

                      if learnMatch.Success then
                          let level = int learnMatch.Groups.[1].Value
                          let move = learnMatch.Groups.[2].Value.Trim()

                          if move <> "0" then
                              learnset.Add { Level = level; Move = move }
                      elif raw.TrimStart().StartsWith("db 0") then
                          ()

              yield {
                  Species = speciesConstant species
                  Evolutions = List.ofSeq evolutions
                  Learnset = List.ofSeq learnset } ]

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

    // --- Trainers -----------------------------------------------------------

    type TrainerMon =
        { Species: string
          Level: int
          HeldItem: string option
          ExplicitMoves: string list }

    type TrainerEntry =
        { Group: string
          Id: int
          Name: string
          PartyType: string
          Party: TrainerMon list
          BaseReward: int }

    let private trainerGroupRx = Regex(@"^\s*([A-Za-z0-9_]+)Group:\s*$")
    let private trainerCommentRx = Regex(@"^\s*;\s*([A-Za-z0-9_]+(?: [A-Za-z0-9_]+)*)\s*(?:\(\d+\))?\s*$")
    let private trainerDbRx = Regex(@"^\s*db\s+""([^""]+)@""\s*,\s*([A-Za-z_][A-Za-z0-9_]*)\s*$")
    let private trainerPartyRx = Regex(@"^\s*db\s+(.+?)\s*$")
    let private trainerEndRx = Regex(@"^\s*db\s+-1\b")
    let private trainerClassConstRx = Regex(@"^\s*trainerclass\s+([A-Za-z0-9_]+)\b")
    let private trainerConstRx = Regex(@"^\s*const\s+([A-Za-z0-9_]+)\b")

    let private parseTrainerPartyLine (raw: string) (trainerType: string) : TrainerMon option =
        let m = trainerPartyRx.Match raw

        if not m.Success then
            None
        else
            let tokens =
                m.Groups.[1].Value.Split(',')
                |> Array.map (fun t -> t.Trim())
                |> Array.filter (fun t -> t <> "")

            let expectedFields, hasItem, hasMoves =
                match trainerType with
                | "TRAINERTYPE_NORMAL" -> 2, false, false
                | "TRAINERTYPE_MOVES" -> 6, false, true
                | "TRAINERTYPE_ITEM" -> 3, true, false
                | "TRAINERTYPE_ITEM_MOVES" -> 7, true, true
                | other -> failwithf "Unknown trainer party type '%s' in line: %s" other raw

            if tokens.Length <> expectedFields then
                failwithf
                    "Trainer party row for %s has %d fields; expected %d: %s"
                    trainerType
                    tokens.Length
                    expectedFields
                    raw

            let itemIndex = 2
            let movesIndex = if hasItem then 3 else 2

            Some
                { Species = tokens.[1]
                  Level = int tokens.[0]
                  HeldItem = if hasItem then Some tokens.[itemIndex] else None
                  ExplicitMoves =
                    if hasMoves then
                        tokens.[movesIndex .. movesIndex + 3] |> Array.toList
                    else
                        [] }

    let trainers : TrainerEntry list =
        let lines = Repo.readText("data/trainers/parties.asm").Split('\n')
        let result = ResizeArray<TrainerEntry>()
        let mutable currentGroupClass = ""
        let mutable currentId = 0
        let mutable currentTrainerName = ""
        let mutable currentTrainerType = ""
        let mutable party = ResizeArray<TrainerMon>()

        let flush () =
            if currentGroupClass <> "" && currentTrainerName <> "" && party.Count > 0 then
                result.Add
                    { Group = currentGroupClass
                      Id = currentId
                      Name = currentTrainerName
                      PartyType = currentTrainerType
                      Party = List.ofSeq party
                      BaseReward = 0 }

        for raw in lines do
            let cleanLine =
                let i = raw.IndexOf(';')
                if i >= 0 then raw.Substring(0, i) else raw

            if trainerGroupRx.IsMatch cleanLine then
                flush ()
                currentId <- 0
                currentTrainerName <- ""
                currentTrainerType <- ""
                party <- ResizeArray<TrainerMon>()
                currentGroupClass <- ""
            elif trainerCommentRx.IsMatch (raw.Trim()) then
                let commentMatch = trainerCommentRx.Match (raw.Trim())
                currentGroupClass <- commentMatch.Groups.[1].Value
            elif trainerDbRx.IsMatch cleanLine then
                let trainerMatch = trainerDbRx.Match cleanLine
                flush ()
                currentTrainerName <- trainerMatch.Groups.[1].Value.Replace("@", "")
                currentTrainerType <- trainerMatch.Groups.[2].Value
                currentId <- currentId + 1
                party <- ResizeArray<TrainerMon>()
            elif trainerEndRx.IsMatch cleanLine then
                flush ()
                currentTrainerName <- ""
                currentTrainerType <- ""
                party <- ResizeArray<TrainerMon>()
            elif currentTrainerName <> "" then
                match parseTrainerPartyLine cleanLine currentTrainerType with
                | Some mon -> party.Add mon
                | None -> ()

        flush ()
        List.ofSeq result

    let trainerConstants : Map<string, string * int> =
        let lines = Repo.readText("constants/trainer_constants.asm").Split('\n')
        let mutable currentGroup = ""
        let mutable currentId = 0
        let constants = ResizeArray<string * (string * int)>()

        for raw in lines do
            let cleanLine =
                let i = raw.IndexOf(';')
                if i >= 0 then raw.Substring(0, i) else raw

            let classMatch = trainerClassConstRx.Match cleanLine
            let constMatch = trainerConstRx.Match cleanLine

            if classMatch.Success then
                currentGroup <- classMatch.Groups.[1].Value
                currentId <- 0
            elif constMatch.Success && currentGroup <> "" then
                currentId <- currentId + 1
                constants.Add(constMatch.Groups.[1].Value, (currentGroup, currentId))

        constants |> Seq.map id |> Map.ofSeq

    let private normalizeTrainerClass (name: string) : string =
        name.ToUpperInvariant().Replace(" ", "_")

    let trainerRewards : Map<string, int> =
        let lines = Repo.readText("data/trainers/attributes.asm").Split('\n')
        let result = ResizeArray<string * int>()
        let mutable currentClass = ""
        let mutable reward = 0

        let flush () =
            if currentClass <> "" then
                result.Add(currentClass, reward)

        for raw in lines do
            let cleanLine =
                let i = raw.IndexOf(';')
                if i >= 0 then raw.Substring(0, i) else raw

            let classMatch = trainerCommentRx.Match (raw.Trim())
            if classMatch.Success then
                flush ()
                currentClass <- normalizeTrainerClass (classMatch.Groups.[1].Value)
                reward <- 0
            elif currentClass <> "" then
                let rewardMatch = Regex(@"^\s*db\s+(-?\d+)\b").Match cleanLine
                if rewardMatch.Success then
                    reward <- int rewardMatch.Groups.[1].Value

        flush ()
        Map.ofList (List.ofSeq result)

    let trainerDvs : Map<string, int> =
        let dvRx =
            Regex(@"^\s*dn\s+(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*;\s*([A-Za-z0-9_]+)\s*$")

        [ for raw in Repo.readText("data/trainers/dvs.asm").Split('\n') do
              let m = dvRx.Match raw

              if m.Success then
                  let attack = int m.Groups.[1].Value
                  let defense = int m.Groups.[2].Value
                  let speed = int m.Groups.[3].Value
                  let special = int m.Groups.[4].Value
                  let trainerClass = normalizeTrainerClass m.Groups.[5].Value
                  let packed = (attack <<< 12) ||| (defense <<< 8) ||| (speed <<< 4) ||| special
                  yield trainerClass, packed ]
        |> Map.ofList

    // --- Wild encounters ------------------------------------------------------

    type WildSlot =
        { Level: int
          Species: string }

    type WildEncounterTable =
        { Map: string
          GrassRate: int * int * int
          GrassMorn: WildSlot list
          GrassDay: WildSlot list
          GrassNite: WildSlot list
          WaterRate: int
          Water: WildSlot list }

    let private wildGrassRx = Regex(@"^\s*def_grass_wildmons\s+(\w+)\s*$")
    let private wildWaterRx = Regex(@"^\s*def_water_wildmons\s+(\w+)\s*$")
    let private wildRateRx = Regex(@"^\s*db\s+(\d+)\s+percent(?:,\s*(\d+)\s+percent,\s*(\d+)\s+percent)?")
    let private wildWaterRateRx = Regex(@"^\s*db\s+(\d+)\s+percent\s*$")
    let private wildSlotRx = Regex(@"^\s*db\s+(\d+),\s*([A-Za-z_][A-Za-z0-9_]*)\s*$")

    let private cleanAsmLine (raw: string) : string =
        let i = raw.IndexOf(';')
        if i >= 0 then raw.Substring(0, i) else raw
        |> fun s -> s.Trim()

    let private removeConditionalBranches (lines: string seq) : string list =
        let mutable inConditional = false
        let mutable includeConditional = false

        [ for raw in lines do
              let line = cleanAsmLine raw

              if line.StartsWith("IF DEF(") then
                  inConditional <- true
                  includeConditional <- line.Contains("(_GOLD)")
              elif line.StartsWith("ELIF DEF(") then
                  includeConditional <- line.Contains("(_GOLD)")
              elif line = "ENDC" then
                  inConditional <- false
                  includeConditional <- false
              elif inConditional && not includeConditional then
                  ()
              else
                  yield line ]

    let private parseWildSlots (lines: string list) : WildSlot list =
        [ for line in lines do
              let m = wildSlotRx.Match line

              if m.Success then
                  yield { Level = int m.Groups.[1].Value
                          Species = m.Groups.[2].Value } ]

    let private parseWildEncounterFile (relativePath: string) : WildEncounterTable list =
        let lines = removeConditionalBranches (Repo.readText(relativePath).Split('\n'))
        let mutable currentMap = ""
        let mutable currentKind = ""
        let mutable currentRate = (0, 0, 0)
        let mutable currentWaterRate = 0
        let mutable currentGrassMorn = ResizeArray<WildSlot>()
        let mutable currentGrassDay = ResizeArray<WildSlot>()
        let mutable currentGrassNite = ResizeArray<WildSlot>()
        let mutable currentWater = ResizeArray<WildSlot>()
        let mutable currentSection = ""
        let results = ResizeArray<WildEncounterTable>()

        let flush () =
            if currentMap <> "" then
                results.Add
                    { Map = currentMap
                      GrassRate = currentRate
                      GrassMorn = List.ofSeq currentGrassMorn
                      GrassDay = List.ofSeq currentGrassDay
                      GrassNite = List.ofSeq currentGrassNite
                      WaterRate = currentWaterRate
                      Water = List.ofSeq currentWater }

            currentMap <- ""
            currentKind <- ""
            currentRate <- (0, 0, 0)
            currentWaterRate <- 0
            currentSection <- ""
            currentGrassMorn <- ResizeArray<WildSlot>()
            currentGrassDay <- ResizeArray<WildSlot>()
            currentGrassNite <- ResizeArray<WildSlot>()
            currentWater <- ResizeArray<WildSlot>()

        for line in lines do
            match wildGrassRx.Match line, wildWaterRx.Match line with
            | m, _ when m.Success ->
                flush ()
                currentMap <- m.Groups.[1].Value
                currentKind <- "grass"
            | _, m when m.Success ->
                flush ()
                currentMap <- m.Groups.[1].Value
                currentKind <- "water"
            | _ when line.StartsWith("db ") && currentKind = "grass" && currentSection = "" ->
                let m = wildRateRx.Match line

                if m.Success then
                    let a = int m.Groups.[1].Value
                    let b = if m.Groups.[2].Success then int m.Groups.[2].Value else 0
                    let c = if m.Groups.[3].Success then int m.Groups.[3].Value else 0
                    currentRate <- (a, b, c)
                    currentSection <- "morn"
                else
                    currentSection <- ""
            | _ when line.StartsWith("db ") && currentKind = "water" && currentSection = "" ->
                let m = wildWaterRateRx.Match line

                if m.Success then
                    currentWaterRate <- int m.Groups.[1].Value
                    currentSection <- "water"
                else
                    currentSection <- ""
            | _ when currentKind = "grass" && line.StartsWith("db ") && currentSection <> "" ->
                let slots = parseWildSlots [ line ]

                if slots.Length > 0 then
                    let slot = slots.[0]

                    match currentSection with
                    | "morn" -> currentGrassMorn.Add slot
                    | "day" -> currentGrassDay.Add slot
                    | "nite" -> currentGrassNite.Add slot
                    | _ -> ()

                    if currentGrassMorn.Count = 7 && currentGrassDay.Count = 0 && currentGrassNite.Count = 0 then
                        currentSection <- "day"
                    elif currentGrassMorn.Count = 7 && currentGrassDay.Count = 7 && currentGrassNite.Count = 0 then
                        currentSection <- "nite"
            | _ when currentKind = "water" && line.StartsWith("db ") && currentSection = "water" ->
                let slots = parseWildSlots [ line ]

                if slots.Length > 0 then
                    currentWater.Add slots.[0]
            | _ when line.StartsWith("; morn") ->
                currentSection <- "morn"
            | _ when line.StartsWith("; day") ->
                currentSection <- "day"
            | _ when line.StartsWith("; nite") ->
                currentSection <- "nite"
            | _ -> ()

        flush ()
        List.ofSeq results

    let wildEncounters : WildEncounterTable list =
        [ "data/wild/johto_grass.asm"
          "data/wild/kanto_grass.asm"
          "data/wild/johto_water.asm"
          "data/wild/kanto_water.asm" ]
        |> List.collect parseWildEncounterFile

    // --- Items -----------------------------------------------------------------

    /// Intermediate record for one item parsed from the disassembly tables.
    type Item =
        { Constant: string
          DisplayName: string
          Price: int
          Pocket: string   // "ITEM"|"KEY_ITEM"|"BALL"|"TM_HM"
          CantSelect: bool
          CantToss: bool
          HeldEffect: string
          Param: int
          FieldMenu: string
          BattleMenu: string
          Description: string }

    let private parsePrice (s: string) : int =
        let trimmed = s.Trim()
        if trimmed.StartsWith "$" then
            System.Convert.ToInt32(trimmed.Substring(1), 16)
        else
            int trimmed

    /// Every item's parsed data, in attributes-table order (id 1 onwards).
    let items : Item list =
        // Step 1: Parse attributes from data/items/attributes.asm
        let attrText = Repo.readText "data/items/attributes.asm"
        let commentRx = Regex(@"^\s*;\s*([A-Z][A-Z0-9_]*)(\s|$)")
        let attrRx = Regex(@"item_attribute\s+([^,]+),\s*([^,]+),\s*([^,]+),\s*([^,]+),\s*([^,]+),\s*([^,]+),\s*(\S+)")
        
        let attributesRaw = ResizeArray<_>()
        let mutable lastConstant = ""
        
        for line in attrText.Split('\n') do
            let cm = commentRx.Match(line)
            if cm.Success then
                lastConstant <- cm.Groups.[1].Value
            else
                let am = attrRx.Match(line)
                if am.Success && lastConstant <> "" && lastConstant <> "$00" then
                    let price = parsePrice (am.Groups.[1].Value)
                    let heldEffect = am.Groups.[2].Value.Trim()
                    let param = int (am.Groups.[3].Value.Trim())
                    let perms = am.Groups.[4].Value.Trim()
                    let pocket = am.Groups.[5].Value.Trim()
                    let fieldMenu = am.Groups.[6].Value.Trim()
                    let battleMenu = am.Groups.[7].Value.Trim()
                    
                    let cantSelect = perms.Contains("CANT_SELECT")
                    let cantToss = perms.Contains("CANT_TOSS")
                    
                    attributesRaw.Add((lastConstant, price, heldEffect, param, cantSelect, cantToss, pocket, fieldMenu, battleMenu))
                    lastConstant <- ""
        
        // Step 2: Parse names from data/items/names.asm
        let namesText = Repo.readText "data/items/names.asm"
        let nameRx = Regex(@"li\s+""([^""]*)""")
        let names =
            [ for line in namesText.Split('\n') do
                  let m = nameRx.Match(line)
                  if m.Success then yield m.Groups.[1].Value ]
        
        // Step 3: Parse descriptions from data/items/descriptions.asm
        let descText = Repo.readText "data/items/descriptions.asm"
        let dwRx = Regex(@"dw\s+(\w+Desc)\b")
        let labelRx = Regex(@"^(\w+Desc):")
        let dbTextRx = Regex(@"(db|next|page)\s+""([^""]*)""")
        
        // 3a: Collect pointer table
        let pointerTable = ResizeArray<string>()
        for line in descText.Split('\n') do
            let m = dwRx.Match(line)
            if m.Success then
                pointerTable.Add(m.Groups.[1].Value)
        
        // 3b: Collect text blocks
        let textBlocks = System.Collections.Generic.Dictionary<string, string>()
        let mutable currentLabel = ""
        let mutable currentText = ResizeArray<string>()
        
        for line in descText.Split('\n') do
            let lm = labelRx.Match(line)
            if lm.Success then
                // Save previous block
                if currentLabel <> "" && currentText.Count > 0 then
                    let text = System.String.Join(" ", currentText).Replace("@", "").Trim()
                    textBlocks.[currentLabel] <- text
                currentLabel <- lm.Groups.[1].Value
                currentText <- ResizeArray<string>()
            else
                let tm = dbTextRx.Match(line)
                if tm.Success && currentLabel <> "" then
                    currentText.Add(tm.Groups.[2].Value)
        
        // Save last block
        if currentLabel <> "" && currentText.Count > 0 then
            let text = System.String.Join(" ", currentText).Replace("@", "").Trim()
            textBlocks.[currentLabel] <- text
        
        // Step 4: Build items list
        let result = ResizeArray<Item>()
        for idx, (constant, price, heldEffect, param, cantSelect, cantToss, pocket, fieldMenu, battleMenu) in Seq.indexed attributesRaw do
            if idx < names.Length && idx < pointerTable.Count then
                let name = names.[idx]
                let descLabel = pointerTable.[idx]
                let desc = if textBlocks.ContainsKey(descLabel) then textBlocks.[descLabel] else ""
                
                // Only keep items with valid pockets
                let pocketStr = 
                    match pocket with
                    | "ITEM" -> "ITEM"
                    | "BALL" -> "BALL"
                    | "KEY_ITEM" -> "KEY_ITEM"
                    | "TM_HM" -> "TM_HM"
                    | _ -> ""
                
                if pocketStr <> "" then
                    result.Add {
                        Constant = constant
                        DisplayName = name
                        Price = price
                        Pocket = pocketStr
                        CantSelect = cantSelect
                        CantToss = cantToss
                        HeldEffect = heldEffect
                        Param = param
                        FieldMenu = fieldMenu
                        BattleMenu = battleMenu
                        Description = desc
                    }
        
        List.ofSeq result

    // --- Dex entries -----------------------------------------------------------

    type DexEntryRaw =
        { Constant: string
          DisplayName: string
          Category: string
          HeightDm: int
          WeightHg: int
          Description: string }

    /// Every Pokémon's display name from data/pokemon/names.asm.
    let pokemonDisplayNames : string array =
        let namesText = Repo.readText "data/pokemon/names.asm"
        let nameRx = Regex(@"dname\s+""([^""]*)""")
        let names = ResizeArray<string>()
        names.Add("")  // Index 0 is unused
        
        for line in namesText.Split('\n') do
            let m = nameRx.Match(line)
            if m.Success then
                names.Add(m.Groups.[1].Value)
                if names.Count > 251 then
                    ()  // Skip remaining entries
        
        names.ToArray()

    /// Dex entries in dex-number order (1-251).
    let dexEntries : DexEntryRaw list =
        // Build reverse mapping: dex number -> constant
        let dexToConstant =
            dex
            |> Map.toList
            |> List.sortBy snd
            |> List.filter (fun (_, num) -> num >= 1 && num <= 251)
        
        let result = ResizeArray<DexEntryRaw>()
        
        for (constant, dexNum) in dexToConstant do
            let name = constant.ToLowerInvariant()
            let dexPath = Repo.path $"data/pokemon/dex_entries/gold/{name}.asm"
            
            if File.Exists(dexPath) then
                let lines = File.ReadAllLines(dexPath) |> Array.filter (fun l -> l.Trim() <> "")
                
                // First db line is category
                let categoryRx = Regex(@"db\s+""([^""@]*)@?""")
                let mutable category = ""
                let mutable foundCategory = false
                let mutable lineIdx = 0
                
                while not foundCategory && lineIdx < lines.Length do
                    let cm = categoryRx.Match(lines.[lineIdx])
                    if cm.Success then
                        category <- cm.Groups.[1].Value
                        foundCategory <- true
                    lineIdx <- lineIdx + 1
                
                // Next dw line is height, weight
                let dwRx = Regex(@"dw\s+(\d+),\s*(\d+)")
                let mutable height = 0
                let mutable weight = 0
                let mutable foundDw = false
                
                while not foundDw && lineIdx < lines.Length do
                    let dm = dwRx.Match(lines.[lineIdx])
                    if dm.Success then
                        height <- int dm.Groups.[1].Value
                        weight <- int dm.Groups.[2].Value
                        foundDw <- true
                    lineIdx <- lineIdx + 1
                
                // Remaining db/next/page lines form description
                let dbTextRx = Regex(@"(db|next|page)\s+""([^""]*)""")
                let descParts = ResizeArray<string>()
                
                while lineIdx < lines.Length do
                    let tm = dbTextRx.Match(lines.[lineIdx])
                    if tm.Success then
                        descParts.Add(tm.Groups.[2].Value)
                    lineIdx <- lineIdx + 1
                
                let desc = System.String.Join(" ", descParts).Replace("@", "").Trim()
                let displayName = if dexNum > 0 && dexNum < pokemonDisplayNames.Length then pokemonDisplayNames.[dexNum] else constant
                
                result.Add {
                    Constant = constant
                    DisplayName = displayName
                    Category = category
                    HeightDm = height
                    WeightHg = weight
                    Description = desc
                }
            else
                // Fallback for missing entries (eggs, etc.)
                let displayName = if dexNum > 0 && dexNum < pokemonDisplayNames.Length then pokemonDisplayNames.[dexNum] else constant
                result.Add {
                    Constant = constant
                    DisplayName = displayName
                    Category = ""
                    HeightDm = 0
                    WeightHg = 0
                    Description = ""
                }
        
        List.ofSeq result


    // --- Marts -----------------------------------------------------------------

    /// One mart: its label name (e.g. "MartCherrygrove") and ordered item constants.
    type Mart =
        { Label: string
          Items: string list }

    let private parseMarts () : Mart list * string list =
        let text = Repo.readText "data/items/marts.asm"
        let lines = text.Split('\n')

        let orderRx = Regex(@"^\s*dw\s+(Mart\w+)")
        let labelRx = Regex(@"^(Mart\w+):")
        // Only match uppercase item constants (db N and db -1 won't match).
        let dbItemRx = Regex(@"^\s*db\s+([A-Z][A-Z0-9_]*)")

        let order = ResizeArray<string>()
        let martItems = ResizeArray<string * ResizeArray<string>>()
        let mutable currentItems: ResizeArray<string> = null
        let mutable inPointerTable = false
        let mutable pastHeader = false

        for line in lines do
            if line.TrimStart().StartsWith("Marts:") then
                inPointerTable <- true
            elif inPointerTable && line.Contains("assert_table_length") then
                inPointerTable <- false
                pastHeader <- true
            elif inPointerTable then
                let m = orderRx.Match(line)
                if m.Success then order.Add(m.Groups.[1].Value)
            elif pastHeader then
                let lm = labelRx.Match(line)
                if lm.Success then
                    currentItems <- ResizeArray<string>()
                    martItems.Add(lm.Groups.[1].Value, currentItems)
                elif not (isNull currentItems) then
                    let dm = dbItemRx.Match(line)
                    if dm.Success then
                        currentItems.Add(dm.Groups.[1].Value)

        let itemMap =
            martItems
            |> Seq.map (fun (l, items) -> l, List.ofSeq items)
            |> Map.ofSeq

        let martsOrdered =
            [ for label in order do
                  match itemMap.TryFind label with
                  | Some items -> yield { Label = label; Items = items }
                  | None -> () ]

        martsOrdered, List.ofSeq order

    let private martsOnce = lazy parseMarts ()

    /// All mart inventories in pointer-table order.
    let marts : Mart list = fst martsOnce.Value

    /// Mart label names in the order they appear in the Marts: pointer table.
    let martOrder : string list = snd martsOnce.Value

    // --- Mart constant names -----------------------------------------------

    let private parseMartConstantNames () : string list =
        let text = Repo.readText "constants/mart_constants.asm"
        let rx = Regex(@"^\s*const\s+(MART_[A-Z0-9_]+)")
        [ for line in text.Split('\n') do
              let m = rx.Match line
              if m.Success then yield m.Groups.[1].Value ]

    let private martConstantNamesOnce = lazy parseMartConstantNames ()

    /// MART_* constant names in declaration order (parallel to martOrder by index).
    let martConstantNames : string list = martConstantNamesOnce.Value
