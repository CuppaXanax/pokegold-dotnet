namespace PokeGold.DataGen

open System.IO

/// Build-time data generator. Reads the disassembly tables and emits typed F#
/// literals into the game's `Data/Generated/` folder.
///
/// Usage: `PokeGold.DataGen [outputDir]`
///   outputDir defaults to `<repo>/engine-dotnet/src/PokeGold.Game/Data/Generated`.
module Program =

    [<EntryPoint>]
    let main argv =
        let outDir =
            match argv with
            | [| dir |] -> dir
            | [||] -> Repo.path "engine-dotnet/src/PokeGold.Game/Data/Generated"
            | _ ->
                eprintfn "usage: PokeGold.DataGen [outputDir]"
                exit 2

        Directory.CreateDirectory outDir |> ignore

        let results = Emit.all outDir

        let changed = results |> List.filter snd |> List.length

        printfn
            "PokeGold.DataGen: %d species, %d moves, %d items, %d dex entries, %d type ids, %d matchups -> %s (%d file(s) updated)"
            (List.length Parsers.species)
            (List.length Parsers.moves)
            (List.length Parsers.items)
            (List.length Parsers.dexEntries)
            (Map.count Parsers.typeIds)
            (List.length Parsers.typeMatchups)
            outDir
            changed

        0
