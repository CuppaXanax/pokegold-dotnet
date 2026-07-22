namespace PokeGold.Game.Data

open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player

type PokedexObtainabilityProof =
    { Sources: Map<string, Set<string>> }

module PokedexObtainability =

    let private addSource species source sources =
        if Species.all.ContainsKey species then
            let existing = Map.tryFind species sources |> Option.defaultValue Set.empty
            Map.add species (Set.add source existing) sources
        else
            sources

    let private addMany source species sources =
        species |> List.fold (fun acc species -> addSource species source acc) sources

    let private fishingSpecies =
        let slots =
            FishEncountersData.byGroup
            |> Seq.collect (fun (KeyValue(_, group)) -> group.OldRod @ group.GoodRod @ group.SuperRod)
            |> Seq.toList

        let timedSpecies =
            slots
            |> List.choose (fun slot -> slot.TimeGroup)
            |> List.distinct
            |> List.collect (fun index ->
                match FishEncountersData.timeGroups |> Array.tryItem index with
                | Some group -> [ group.DaySpecies; group.NightSpecies ]
                | None -> [])

        slots
        |> List.choose (fun slot -> slot.Species)
        |> List.append timedSpecies
        |> Set.ofList
        |> Set.toList

    let private headbuttSpecies =
        // data\wild\treemons.asm.
        [ "ABRA"; "AIPOM"; "BUTTERFREE"; "CATERPIE"; "EXEGGCUTE"; "HERACROSS"
          "KRABBY"; "MAGNEMITE"; "METAPOD"; "PINECO"; "SHUCKLE"; "SPEAROW"
          "VENOMOTH"; "VENONAT" ]

    let private bugContestSpecies =
        // data\wild\bug_contest_mons.asm.
        [ "BEEDRILL"; "BUTTERFREE"; "CATERPIE"; "KAKUNA"; "METAPOD"; "PARAS"
          "PINSIR"; "SCYTHER"; "VENOMOTH"; "VENONAT"; "WEEDLE" ]

    let private swarmSpecies =
        // data\wild\swarm_grass.asm and data\wild\swarm_water.asm.
        [ "DUNSPARCE"; "MARILL"; "QWILFISH"; "REMORAID"; "SNUBBULL"; "YANMA" ]

    let private roamerSpecies =
        [ "RAIKOU"; "ENTEI"; "SUICUNE" ]

    let private breedingPairs =
        // Baby species whose parents are obtainable elsewhere in the proof.
        [ "PIKACHU", "PICHU"
          "CLEFAIRY", "CLEFFA"
          "JIGGLYPUFF", "IGGLYBUFF"
          "JYNX", "SMOOCHUM"
          "ELECTABUZZ", "ELEKID"
          "MAGMAR", "MAGBY" ]

    let private collectScriptSources sources =
        let mutable sources = sources

        let collect source commands =
            for command in commands do
                match command with
                | ScriptCommand.Givepoke(species, _, _, _, _) ->
                    sources <- addSource species $"gift:{source}" sources
                | ScriptCommand.Giveegg(species, _) ->
                    sources <- addSource species $"egg:{source}" sources
                | ScriptCommand.Loadwildmon(species, _) ->
                    sources <- addSource species $"static:{source}" sources
                | _ -> ()

        for KeyValue(mapId, map) in MapsData.all do
            collect mapId map.Script.Commands

        collect "std" StdScriptsData.program.Commands
        sources

    let private baseSources () =
        let mutable sources = Map.empty

        for KeyValue(mapId, table) in WildEncounters.all do
            let slots =
                List.concat [ table.GrassMorn; table.GrassDay; table.GrassNite; table.Water ]

            for slot in slots do
                sources <- addSource slot.Species $"wild:{mapId}" sources

        sources <- addMany "fishing:data/wild/fish.asm" fishingSpecies sources
        sources <- addMany "headbutt:data/wild/treemons.asm" headbuttSpecies sources
        sources <- addMany "bug-contest:data/wild/bug_contest_mons.asm" bugContestSpecies sources
        sources <- addMany "swarm:data/wild/swarm_*.asm" swarmSpecies sources
        sources <- addMany "roamer:InitRoamMons" roamerSpecies sources
        sources <- addMany "offline-trade-terminal:D7" (Trading.offlineImportCatalog |> Set.toList) sources
        sources <- addMany "built-in-event:D9" (DexCompletion.builtInEventUnlockSpecies |> Set.toList) sources
        collectScriptSources sources

    let buildProof () =
        let mutable sources = baseSources ()
        let mutable obtainable = sources |> Map.keys |> Set.ofSeq
        let mutable changed = true

        while changed do
            changed <- false

            for KeyValue(species, data) in EvosAttacksAccess.all do
                if Set.contains species obtainable then
                    for evolution in data.Evolutions do
                        if Species.all.ContainsKey evolution.Target && not (Set.contains evolution.Target obtainable) then
                            sources <- addSource evolution.Target $"evolution:{species}:{evolution.Method}" sources
                            obtainable <- Set.add evolution.Target obtainable
                            changed <- true

            for parent, baby in breedingPairs do
                if Set.contains parent obtainable && not (Set.contains baby obtainable) then
                    sources <- addSource baby $"breeding:{parent}" sources
                    obtainable <- Set.add baby obtainable
                    changed <- true

        { Sources = sources }

    let obtainableSpecies proof =
        proof.Sources |> Map.keys |> Set.ofSeq

    let sourcesFor species proof =
        Map.tryFind species proof.Sources |> Option.defaultValue Set.empty

    let missingSpecies proof =
        let obtainable = obtainableSpecies proof

        Species.all
        |> Map.toList
        |> List.sortBy (fun (_, stats) -> stats.Dex)
        |> List.choose (fun (name, stats) ->
            if Set.contains name obtainable then None
            else Some(stats.Dex, name))
