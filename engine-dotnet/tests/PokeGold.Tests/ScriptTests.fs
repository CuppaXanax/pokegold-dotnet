module PokeGold.Tests.ScriptTests

open Xunit
open PokeGold.Game.Overworld.Script

// M9.1 — the overworld script command language (DU) + parser. These tests parse
// both small inline `.asm` snippets (deterministic) and the real Bugsy/Gramps
// scripts from the bundled map files, asserting exact command sequences. The
// parser reads the disassembly source directly, like the audio SongParser.

let private parse = ScriptParser.parseText

[<Fact>]
let ``parses a simple text-only NPC script`` () =
    let prog =
        parse
            "TestNpcScript:\n\
             \tjumptextfaceplayer TestNpcText\n"

    Assert.Equal<ScriptCommand list>(
        [ Jumptextfaceplayer "TestNpcText" ],
        ScriptProgram.blockAt "TestNpcScript" prog
    )

[<Fact>]
let ``parses opcodes with their typed arguments`` () =
    let prog =
        parse
            "S:\n\
             \tsetval 7\n\
             \tifequal 3, .Branch\n\
             \tgiveitem POTION, 2\n\
             \tcheckitem MASTER_BALL\n\
             \twarp NEW_BARK_TOWN, 4, 5\n\
             \tloadtrainer BUGSY, BUGSY1\n\
             \tend\n\
             .Branch:\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Setval 7
          Ifequal(3, "S.Branch")
          Giveitem("POTION", 2)
          Checkitem "MASTER_BALL"
          Warp("NEW_BARK_TOWN", 4, 5)
          Loadtrainer("BUGSY", "BUGSY1")
          End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``giveitem and verbosegiveitem default quantity to one`` () =
    let prog =
        parse
            "S:\n\
             \tgiveitem POTION\n\
             \tverbosegiveitem TM_FURY_CUTTER\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Giveitem("POTION", 1); Verbosegiveitem("TM_FURY_CUTTER", 1); End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``local labels are qualified with their enclosing global label`` () =
    let prog =
        parse
            "A:\n\
             \tiftrue .Done\n\
             \tend\n\
             .Done:\n\
             \tend\n\
             B:\n\
             \tiftrue .Done\n\
             \tend\n\
             .Done:\n\
             \tend\n"

    // Each `.Done` resolves to a distinct, fully-qualified label...
    Assert.Equal<ScriptCommand list>([ Iftrue "A.Done"; End ], ScriptProgram.blockAt "A" prog)
    Assert.Equal<ScriptCommand list>([ Iftrue "B.Done"; End ], ScriptProgram.blockAt "B" prog)
    // ...and both qualified labels exist in the map.
    Assert.True(prog.Labels.ContainsKey "A.Done")
    Assert.True(prog.Labels.ContainsKey "B.Done")

[<Fact>]
let ``jump targets resolve to the labelled command index`` () =
    let prog =
        parse
            "S:\n\
             \tsjump .Tail\n\
             \tsetval 1\n\
             .Tail:\n\
             \tend\n"

    // .Tail is the 3rd command (index 2: sjump, setval, end).
    Assert.Equal(2, prog.Labels.["S.Tail"])
    Assert.Equal(End, prog.Commands.[prog.Labels.["S.Tail"]])

[<Fact>]
let ``unmodelled opcodes become Unsupported but keep parsing`` () =
    let prog =
        parse
            "S:\n\
             \tspecial Special_FadeOutPalettes\n\
             \tpause 30\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Unsupported("special", [ "Special_FadeOutPalettes" ])
          Unsupported("pause", [ "30" ])
          End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``text and event-table directives never emit commands`` () =
    // A label in front of text/data points at the next real command (here none).
    let prog =
        parse
            "SomeText:\n\
             \ttext \"Hello\"\n\
             \tdone\n\
             \n\
             def_warp_events\n\
             \twarp_event 15, 9, AZALEA_POKECENTER_1F, 1\n"

    Assert.Empty(prog.Commands)
    // The label still exists (pointing one past the end), but yields no block.
    Assert.True(prog.Labels.ContainsKey "SomeText")
    Assert.Empty(ScriptProgram.blockAt "SomeText" prog)

// ---- Real map files (ground truth from the disassembly) --------------------

[<Fact>]
let ``parses the real Azalea Gym Bugsy script`` () =
    let prog = ScriptParser.parseFile "maps/AzaleaGym.asm"

    // The pre-battle sequence, verbatim from maps/AzaleaGym.asm lines 15-34.
    let expected =
        [ Faceplayer
          Opentext
          Checkevent "EVENT_BEAT_BUGSY"
          Iftrue "AzaleaGymBugsyScript.FightDone"
          Writetext "BugsyText_INeverLose"
          Waitbutton
          Closetext
          Winlosstext("BugsyText_ResearchIncomplete", "0")
          Loadtrainer("BUGSY", "BUGSY1")
          Startbattle
          Reloadmapafterbattle
          Setevent "EVENT_BEAT_BUGSY"
          Opentext
          Writetext "Text_ReceivedHiveBadge"
          Playsound "SFX_GET_BADGE"
          Waitsfx
          Setflag "ENGINE_HIVEBADGE"
          Readvar "VAR_BADGES"
          Scall "AzaleaGymActivateRockets" ]

    let actual = ScriptProgram.blockAt "AzaleaGymBugsyScript" prog

    // Compare just the prefix we transcribed (the block continues past it).
    Assert.Equal<ScriptCommand list>(expected, actual |> List.truncate expected.Length)

[<Fact>]
let ``parses the real Azalea Town Gramps branch`` () =
    let prog = ScriptParser.parseFile "maps/AzaleaTown.asm"
    let block = ScriptProgram.blockAt "AzaleaTownGrampsScript" prog

    // Gramps faces the player, opens text, and branches on the Slowpoke Well flag.
    Assert.Equal<ScriptCommand list>(
        [ Faceplayer
          Opentext
          Checkevent "EVENT_CLEARED_SLOWPOKE_WELL"
          Iftrue "AzaleaTownGrampsScript.ClearedWell"
          Writetext "AzaleaTownGrampsTextBefore"
          Waitbutton
          Closetext
          End ],
        block
    )

    // The taken branch is its own qualified label with the "after" text.
    Assert.Equal<ScriptCommand list>(
        [ Writetext "AzaleaTownGrampsTextAfter"; Waitbutton; Closetext; End ],
        ScriptProgram.blockAt "AzaleaTownGrampsScript.ClearedWell" prog
    )

// ---- M9.2 — the resumable script VM + flag/var world store -----------------

// Drive a script from `label` to completion, auto-answering each result-bearing
// effect with `respond` and collecting the effects seen and the final world.
let private drive (respond: ScriptEffect -> int option) (world: World) (label: string) (prog: ScriptProgram) =
    let rec loop world step (effects: ScriptEffect list) =
        match step with
        | Completed -> world, List.rev effects
        | Suspended(vm, effect) ->
            let next = Script.resume (respond effect) world vm
            loop next.World next.Outcome (effect :: effects)

    let first = Script.start label world prog
    loop first.World first.Outcome []

// A driver that needs no answers (no result-bearing effects on the path).
let private driveSilent world label prog =
    drive (fun _ -> None) world label prog

[<Fact>]
let ``World flag set-check-clear round-trips`` () =
    let w = World.empty
    Assert.False(World.hasEvent "EVENT_X" w)
    let w = World.setEvent "EVENT_X" w
    Assert.True(World.hasEvent "EVENT_X" w)
    let w = World.clearEvent "EVENT_X" w
    Assert.False(World.hasEvent "EVENT_X" w)
    // Vars and scenes default to 0 and round-trip independently.
    Assert.Equal(0, World.getVar "VAR_X" w)
    Assert.Equal(42, World.getVar "VAR_X" (World.setVar "VAR_X" 42 w))

[<Fact>]
let ``checkevent then iftrue branches only when the flag is set`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tcheckevent EVENT_GATE\n\
             \tiftrue .Open\n\
             \twritetext ClosedText\n\
             \tend\n\
             .Open:\n\
             \twritetext OpenText\n\
             \tend\n"

    // Flag clear -> falls through to the "closed" text.
    let _, closed = driveSilent World.empty "S" prog
    Assert.Equal<ScriptEffect list>([ ShowText("ClosedText", false) ], closed)

    // Flag set -> the iftrue is taken and we see the "open" text.
    let _, opened = driveSilent (World.setEvent "EVENT_GATE" World.empty) "S" prog
    Assert.Equal<ScriptEffect list>([ ShowText("OpenText", false) ], opened)

[<Fact>]
let ``setevent persists into the returned world`` () =
    let prog = ScriptParser.parseText "S:\n\tsetevent EVENT_DONE\n\tend\n"
    let world, _ = driveSilent World.empty "S" prog
    Assert.True(World.hasEvent "EVENT_DONE" world)

[<Fact>]
let ``setval and ifequal compare the script var`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tsetval 3\n\
             \tifequal 3, .Match\n\
             \twritetext NoMatch\n\
             \tend\n\
             .Match:\n\
             \twritetext Match\n\
             \tend\n"

    let _, effects = driveSilent World.empty "S" prog
    Assert.Equal<ScriptEffect list>([ ShowText("Match", false) ], effects)

[<Fact>]
let ``readvar loads a game variable into the script var`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \treadvar VAR_BADGES\n\
             \tifequal 8, .AllBadges\n\
             \tend\n\
             .AllBadges:\n\
             \twritetext Congrats\n\
             \tend\n"

    let world = World.setVar "VAR_BADGES" 8 World.empty
    let _, effects = driveSilent world "S" prog
    Assert.Equal<ScriptEffect list>([ ShowText("Congrats", false) ], effects)

[<Fact>]
let ``writevar stores the script var into a game variable`` () =
    let prog = ScriptParser.parseText "S:\n\tsetval 5\n\twritevar VAR_RESULT\n\tend\n"
    let world, _ = driveSilent World.empty "S" prog
    Assert.Equal(5, World.getVar "VAR_RESULT" world)

[<Fact>]
let ``scall runs a sub-script and end returns to the caller`` () =
    let prog =
        ScriptParser.parseText
            "Main:\n\
             \twritetext Before\n\
             \tscall Sub\n\
             \twritetext After\n\
             \tend\n\
             Sub:\n\
             \twritetext Inside\n\
             \tend\n"

    // end inside Sub returns after the scall, so we see Before, Inside, After.
    let _, effects = driveSilent World.empty "Main" prog
    Assert.Equal<ScriptEffect list>(
        [ ShowText("Before", false); ShowText("Inside", false); ShowText("After", false) ],
        effects
    )

[<Fact>]
let ``nested scall/end unwinds the call stack in order`` () =
    let prog =
        ScriptParser.parseText
            "Main:\n\
             \tscall A\n\
             \twritetext M\n\
             \tend\n\
             A:\n\
             \tscall B\n\
             \twritetext AfromA\n\
             \tend\n\
             B:\n\
             \twritetext B\n\
             \tend\n"

    let _, effects = driveSilent World.empty "Main" prog
    Assert.Equal<ScriptEffect list>(
        [ ShowText("B", false); ShowText("AfromA", false); ShowText("M", false) ],
        effects
    )

[<Fact>]
let ``endall stops even inside a sub-script`` () =
    let prog =
        ScriptParser.parseText
            "Main:\n\
             \tscall Sub\n\
             \twritetext NeverShown\n\
             \tend\n\
             Sub:\n\
             \twritetext Inside\n\
             \tendall\n"

    let _, effects = driveSilent World.empty "Main" prog
    Assert.Equal<ScriptEffect list>([ ShowText("Inside", false) ], effects)

[<Fact>]
let ``yesorno feeds the choice back into the script var`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tyesorno\n\
             \tiftrue .Yes\n\
             \twritetext SaidNo\n\
             \tend\n\
             .Yes:\n\
             \twritetext SaidYes\n\
             \tend\n"

    // Answer the AskYesNo effect with 1 (yes).
    let respondYes = function AskYesNo -> Some 1 | _ -> None
    let _, yes = drive respondYes World.empty "S" prog
    Assert.Equal<ScriptEffect list>([ AskYesNo; ShowText("SaidYes", false) ], yes)

    // ...and with 0 (no).
    let respondNo = function AskYesNo -> Some 0 | _ -> None
    let _, no = drive respondNo World.empty "S" prog
    Assert.Equal<ScriptEffect list>([ AskYesNo; ShowText("SaidNo", false) ], no)

[<Fact>]
let ``jumptextfaceplayer shows text facing the player then ends`` () =
    let prog = ScriptParser.parseText "S:\n\tjumptextfaceplayer Greeting\n"
    let _, effects = driveSilent World.empty "S" prog
    Assert.Equal<ScriptEffect list>([ ShowText("Greeting", true) ], effects)

[<Fact>]
let ``unsupported opcodes are skipped so the script keeps running`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tspecial Special_Foo\n\
             \twritetext Shown\n\
             \tpause 30\n\
             \tend\n"

    let _, effects = driveSilent World.empty "S" prog
    Assert.Equal<ScriptEffect list>([ ShowText("Shown", false) ], effects)

[<Fact>]
let ``the real Gramps script runs the right branch for each flag state`` () =
    let prog = ScriptParser.parseFile "maps/AzaleaTown.asm"

    // Before clearing Slowpoke Well: faces the player, then the "before" text.
    let _, before = driveSilent World.empty "AzaleaTownGrampsScript" prog
    Assert.Equal<ScriptEffect list>([ FacePlayer; ShowText("AzaleaTownGrampsTextBefore", false) ], before)

    // After: the iftrue is taken and we get the "after" text.
    let cleared = World.setEvent "EVENT_CLEARED_SLOWPOKE_WELL" World.empty
    let _, after = driveSilent cleared "AzaleaTownGrampsScript" prog
    Assert.Equal<ScriptEffect list>([ FacePlayer; ShowText("AzaleaTownGrampsTextAfter", false) ], after)

