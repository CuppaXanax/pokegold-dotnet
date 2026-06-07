namespace PokeGold.DataGen

open PokeGold.Game.Overworld.Script

module Validate =

    let private validateProgram (source: string) (program: ScriptProgram) =
        for KeyValue(label, pc) in program.Labels do
            if pc < 0 || pc > program.Commands.Length then
                failwithf "Script label %s in %s points outside command stream (%d/%d)" label source pc program.Commands.Length

        for i = 0 to program.Commands.Length - 1 do
            match program.Commands.[i] with
            | Unsupported(name, _) ->
                failwithf "Generated script contains untyped command Unsupported(\"%s\") in %s at command %d" name source i
            | _ -> ()

    let generatedScripts (maps: GeneratedMap list) (stdScripts: ScriptProgram) =
        for map in maps do
            validateProgram map.Meta.Name map.Script

        validateProgram "StdScripts" stdScripts
