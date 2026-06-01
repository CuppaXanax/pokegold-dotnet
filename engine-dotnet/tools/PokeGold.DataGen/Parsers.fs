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