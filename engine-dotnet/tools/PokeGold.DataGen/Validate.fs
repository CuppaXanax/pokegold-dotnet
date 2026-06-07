namespace PokeGold.DataGen

open PokeGold.Game.Overworld.Script

module Validate =

    let private parserPollutionNames: Set<string> =
        set
            [ "callback"; "add_stdscript"; "DEF"; "INCLUDE"; "dbw"; "text_end";
              "turn_head"; "turn_step"; "slow_step"; "step"; "big_step";
              "slow_slide_step"; "slide_step"; "fast_slide_step"; "turn_away"; "turn_in"; "turn_waterfall";
              "slow_jump_step"; "jump_step"; "fast_jump_step";
              "remove_sliding"; "set_sliding"; "remove_fixed_facing"; "fix_facing"; "show_object"; "hide_object";
              "step_sleep"; "step_end"; "step_wait_end"; "remove_object"; "step_loop"; "step_stop";
              "teleport_from"; "teleport_to"; "skyfall"; "step_dig"; "step_bump"; "fish_got_bite"; "fish_cast_rod";
              "hide_emote"; "show_emote"; "step_shake"; "tree_shake"; "rock_smash"; "return_dig" ]

    let private validateProgram (source: string) (program: ScriptProgram) =
        for KeyValue(label, pc) in program.Labels do
            if pc < 0 || pc > program.Commands.Length then
                failwithf "Script label %s in %s points outside command stream (%d/%d)" label source pc program.Commands.Length

        for i = 0 to program.Commands.Length - 1 do
            match program.Commands.[i] with
            | Unsupported(name, _) when parserPollutionNames.Contains name ->
                failwithf "Parser-pollution command Unsupported(\"%s\") in %s at command %d" name source i
            | _ -> ()

    let generatedScripts (maps: GeneratedMap list) (stdScripts: ScriptProgram) =
        for map in maps do
            validateProgram map.Meta.Name map.Script

        validateProgram "StdScripts" stdScripts
