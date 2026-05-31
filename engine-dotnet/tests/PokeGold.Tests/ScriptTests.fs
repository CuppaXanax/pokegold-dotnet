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
