// M9.6 coverage sweep: parse EVERY maps/*.asm through ScriptParser (and the text
// + event-table parsers) and report any opcode the script VM doesn't yet model.
// Falsifiable bar: zero unhandled exceptions across all maps; the Unsupported
// tally tells us exactly which (if any) real opcodes remain unmodelled.
//   dotnet fsi investigations/scripts/sweep_maps.fsx
#r "../../src/PokeGold.Game/bin/Debug/net8.0/PokeGold.Game.dll"
open System.IO
open PokeGold.Game.Overworld.Script

let mapsDir = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../../maps"))
let files = Directory.GetFiles(mapsDir, "*.asm") |> Array.sort

let unsupported = System.Collections.Generic.Dictionary<string, int>()
let mutable totalCommands = 0
let mutable totalLabels = 0
let mutable failures = 0

for path in files do
    try
        let prog = ScriptParser.parseText (File.ReadAllText path)
        totalCommands <- totalCommands + prog.Commands.Length
        totalLabels <- totalLabels + prog.Labels.Count
        for cmd in prog.Commands do
            match cmd with
            | ScriptCommand.Unsupported(mn, _) ->
                unsupported.[mn] <- (match unsupported.TryGetValue mn with
                                     | true, v -> v + 1
                                     | _ -> 1)
            | _ -> ()
    with ex ->
        failures <- failures + 1
        printfn "FAIL %s: %s" (Path.GetFileName path) ex.Message

printfn "maps parsed     : %d" files.Length
printfn "parse failures  : %d" failures
printfn "total commands  : %d" totalCommands
printfn "total labels    : %d" totalLabels
printfn "distinct unsup. : %d" unsupported.Count
printfn "--- unsupported opcodes (count desc) ---"
unsupported
|> Seq.sortByDescending (fun kv -> kv.Value)
|> Seq.iter (fun kv -> printfn "%6d  %s" kv.Value kv.Key)
