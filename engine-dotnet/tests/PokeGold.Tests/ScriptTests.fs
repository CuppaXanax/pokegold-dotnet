module PokeGold.Tests.ScriptTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Text
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
let ``itemball expands to verbosegiveitem plus end`` () =
    let prog =
        parse
            "S:\n\
             \titemball POTION, 2\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Verbosegiveitem("POTION", 2); End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``hiddenitem expands to a single verbosegiveitem plus end`` () =
    let prog =
        parse
            "S:\n\
             \thiddenitem REVIVE\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Verbosegiveitem("REVIVE", 1); End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``fruittree expands to a BERRY verbosegiveitem plus end`` () =
    let prog =
        parse
            "S:\n\
             \tfruittree\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Verbosegiveitem("BERRY", 1); End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``parses changeblock with three integer arguments`` () =
    let prog =
        parse
            "CB:\n\
             \tchangeblock 4, 6, 11\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Changeblock(4, 6, 11); End ],
        ScriptProgram.blockAt "CB" prog
    )

[<Fact>]
let ``parses givepoke without a held item`` () =
    let prog =
        parse
            "S:\n\
             \tgivepoke CYNDAQUIL, 5\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Givepoke("CYNDAQUIL", 5, None); End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``parses givepoke with a held item`` () =
    let prog =
        parse
            "S:\n\
             \tgivepoke CYNDAQUIL, 5, BERRY\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Givepoke("CYNDAQUIL", 5, Some "BERRY"); End ],
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
        [ Special "Special_FadeOutPalettes"
          Unsupported("pause", [ "30" ])
          End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``applymovementlasttalked maps to LAST_TALKED`` () =
    let prog =
        parse
            "S:\n\
             \tapplymovementlasttalked Movement\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Applymovement("LAST_TALKED", "Movement"); End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``halloffame sets EVENT_BEAT_ELITE_FOUR`` () =
    let prog = parse "HoF:\n\thalloffame\n\tend\n"

    match Script.start "HoF" World.empty prog "" with
    | { World = w; Outcome = Suspended(_, HallOfFame) } ->
        Assert.True(World.hasEvent "EVENT_BEAT_ELITE_FOUR" w)
    | other -> Assert.Fail($"expected HallOfFame effect, got {other}")

[<Fact>]
let ``checktime sets script var to the current time-of-day`` () =
    let expected = TimeOfDay.toScriptVar (TimeOfDay.current())
    let prog =
        parse
            (sprintf
                "S:\n\
                 \tchecktime\n\
                 \tifequal %d, .Ok\n\
                 \tend\n\
                 .Ok:\n\
                 \tjumptext OkText\n"
                expected)

    match Script.start "S" World.empty prog "" with
    | { Outcome = Suspended(_, ShowText("OkText", _)) } -> ()
    | other -> Assert.Fail($"expected OkText after checktime, got {other}")

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
    let prog = AsmLoad.script "maps/AzaleaGym.asm"

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
    let prog = AsmLoad.script "maps/AzaleaTown.asm"
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

    let first = Script.start label world prog ""
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
let ``event-gated objects are visible only when their flag is clear`` () =
    let ev =
        { X = 0
          Y = 0
          Sprite = "SPRITE_BALL"
          Movement = "SPRITEMOVEDATA_STILL"
          RadiusX = 0
          RadiusY = 0
          Hour1 = 0
          Hour2 = 0
          Palette = "PAL_OW_RED"
          Type = "OBJECTTYPE_ITEMBALL"
          Sight = 0
          Script = ""
          EventFlag = Some "EVENT_X" }

    let visibleWhenClear = MapEvents.objectVisible World.empty ev
    let hiddenWhenSet = MapEvents.objectVisible (World.setEvent "EVENT_X" World.empty) ev

    Assert.True(visibleWhenClear)
    Assert.False(hiddenWhenSet)

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
let ``givepoke suspends with a GivePoke effect`` () =
    let prog = ScriptParser.parseText "S:\n\tgivepoke CYNDAQUIL, 5\n\tend\n"

    match Script.start "S" World.empty prog "" with
    | { Outcome = Suspended(_, GivePoke("CYNDAQUIL", 5, None)) } -> ()
    | other -> Assert.Fail(sprintf "Expected Suspended GivePoke, got %A" other)

[<Fact>]
let ``trainer macro second talk shows the badge dialog and sets the badge flag`` () =
    let prog =
        parse
            "GymLeader:\n\
             \ttrainer FALKNER, FALKNER1, EVENT_BEAT_FALKNER, FalknerSeenText, FalknerBeatenText, 0, .Script\n\
             \n\
             .Script:\n\
             \tendifjustbattled\n\
             \topentext\n\
             \tsetflag ENGINE_ZEPHYRBADGE\n\
             \twritetext GotBadgeText\n\
             \twaitbutton\n\
             \tclosetext\n\
             \tend\n"

    match Script.start "GymLeader" World.empty prog "" with
    | { Outcome = Suspended(vm, FacePlayer) } ->
        let world2 = World.setEvent "EVENT_BEAT_FALKNER" World.empty
        match Script.resume None world2 vm with
        | { World = worldAfter; Outcome = Suspended(_, ShowText("GotBadgeText", _)) } ->
            Assert.True(World.hasFlag "ENGINE_ZEPHYRBADGE" worldAfter)
        | other -> Assert.Fail($"expected badge text on second talk, got {other}")
    | other -> Assert.Fail($"expected initial FacePlayer, got {other}")

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
    let prog = AsmLoad.script "maps/AzaleaTown.asm"

    // Before clearing Slowpoke Well: faces the player, then the "before" text.
    let _, before = driveSilent World.empty "AzaleaTownGrampsScript" prog
    Assert.Equal<ScriptEffect list>([ FacePlayer; ShowText("AzaleaTownGrampsTextBefore", false) ], before)

    // After: the iftrue is taken and we get the "after" text.
    let cleared = World.setEvent "EVENT_CLEARED_SLOWPOKE_WELL" World.empty
    let _, after = driveSilent cleared "AzaleaTownGrampsScript" prog
    Assert.Equal<ScriptEffect list>([ FacePlayer; ShowText("AzaleaTownGrampsTextAfter", false) ], after)

// ---- M9.3 — map event tables (warps / coords / signs / objects) ------------

[<Fact>]
let ``parses Azalea Town event-table counts`` () =
    let ev = AsmLoad.events "maps/AzaleaTown.asm"
    // Counts verified against the def_*_events blocks in maps/AzaleaTown.asm.
    Assert.Equal(8, ev.Warps.Length)
    Assert.Equal(2, ev.Coords.Length)
    Assert.Equal(9, ev.Bgs.Length)
    Assert.Equal(11, ev.Objects.Length)

[<Fact>]
let ``parses the first warp / coord / bg records`` () =
    let ev = AsmLoad.events "maps/AzaleaTown.asm"

    Assert.Equal(
        { X = 15; Y = 9; DestMap = "AZALEA_POKECENTER_1F"; DestWarp = 1 },
        ev.Warps.[0]
    )

    Assert.Equal(
        { X = 5; Y = 10; Scene = "SCENE_AZALEATOWN_RIVAL_BATTLE"; Script = "AzaleaTownRivalBattleScene1" },
        ev.Coords.[0]
    )

    Assert.Equal({ X = 19; Y = 9; Kind = "BGEVENT_READ"; Script = "AzaleaTownSign" }, ev.Bgs.[0])

[<Fact>]
let ``parses an object record with all thirteen fields`` () =
    let ev = AsmLoad.events "maps/AzaleaTown.asm"

    // The Rocket: gated on a flag (EventFlag = Some _); Gramps: always present (None).
    Assert.Equal(
        { X = 31
          Y = 9
          Sprite = "SPRITE_AZALEA_ROCKET"
          Movement = "SPRITEMOVEDATA_STANDING_DOWN"
          RadiusX = 0
          RadiusY = 0
          Hour1 = -1
          Hour2 = -1
          Palette = "0"
          Type = "OBJECTTYPE_SCRIPT"
          Sight = 0
          Script = "AzaleaTownRocket1Script"
          EventFlag = Some "EVENT_AZALEA_TOWN_SLOWPOKETAIL_ROCKET" },
        ev.Objects.[0]
    )

    let gramps = ev.Objects |> Array.find (fun o -> o.Script = "AzaleaTownGrampsScript")
    Assert.Equal(None, gramps.EventFlag)
    Assert.Equal((21, 9), (gramps.X, gramps.Y))

[<Fact>]
let ``object visibility is gated on the world's event flags`` () =
    let ev = AsmLoad.events "maps/AzaleaTown.asm"

    // With no flags set, event-gated objects are visible (their flag is clear).
    let baseVisible = MapEvents.visibleObjects World.empty ev
    Assert.Contains(baseVisible, fun o -> o.Script = "AzaleaTownGrampsScript")
    Assert.Contains(baseVisible, fun o -> o.Script = "AzaleaTownRocket1Script")

    // Setting the Rocket's flag hides it again.
    let withRocket = World.setEvent "EVENT_AZALEA_TOWN_SLOWPOKETAIL_ROCKET" World.empty
    Assert.DoesNotContain(MapEvents.visibleObjects withRocket ev, fun o -> o.Script = "AzaleaTownRocket1Script")

[<Fact>]
let ``per-cell lookups find the event on a tile`` () =
    let ev = AsmLoad.events "maps/AzaleaTown.asm"

    Assert.Equal(Some "AZALEA_GYM", MapEvents.warpAt 10 15 ev |> Option.map (fun w -> w.DestMap))
    Assert.Equal(Some "AzaleaTownSign", MapEvents.bgAt 19 9 ev |> Option.map (fun b -> b.Script))
    // Gramps stands on (21, 9) and is always visible.
    Assert.Equal(
        Some "AzaleaTownGrampsScript",
        MapEvents.objectAt World.empty 21 9 ev |> Option.map (fun o -> o.Script)
    )
    Assert.Equal(None, MapEvents.warpAt 0 0 ev)

// ---- M9.4 — text resolution + interaction triggers -------------------------

[<Fact>]
let ``MapText resolves a real dialogue label to its token string`` () =
    let text = AsmLoad.text "maps/AzaleaTown.asm"

    Assert.Equal(
        "The SLOWPOKE have<LINE>disappeared from<CONT>town…<PARA>I heard their<LINE>TAILS are being<CONT>sold somewhere.<DONE>",
        text.["AzaleaTownGrampsTextBefore"]
    )

[<Fact>]
let ``resolved dialogue round-trips through the M5 text engine`` () =
    // The Gramps text contains a `…` glyph; encoding it through the text box must
    // not throw and must produce a non-empty, terminated box.
    let text = AsmLoad.text "maps/AzaleaTown.asm"
    let box = TextBox.ofString text.["AzaleaTownGrampsTextAfter"]
    Assert.False(box.Done)

[<Fact>]
let ``a script label with no text block produces no entry`` () =
    let text = AsmLoad.text "maps/AzaleaTown.asm"
    Assert.False(text.ContainsKey "AzaleaTownGrampsScript")

[<Fact>]
let ``actionScript returns the faced NPC's script`` () =
    let ev = AsmLoad.events "maps/AzaleaTown.asm"

    // Resolve objects over the static event table (the scene uses live NPC cells).
    let objAt fx fy =
        MapEvents.objectAt World.empty fx fy ev |> Option.map (fun o -> o.Script)

    // Gramps stands on (21, 9); a player on (21, 10) facing up faces that cell.
    Assert.Equal(
        Some "AzaleaTownGrampsScript",
        Triggers.actionScript objAt (fun _ _ -> false) ev 21 10 Up
    )

    // Facing an empty cell triggers nothing.
    Assert.Equal(None, Triggers.actionScript objAt (fun _ _ -> false) ev 21 10 Down)

[<Fact>]
let ``coordToFire gates on the active scene and fires once`` () =
    let ev = AsmLoad.events "maps/AzaleaTown.asm"
    let dflt = MapEvents.defaultScene ev
    Assert.Equal("SCENE_AZALEATOWN_NOOP", dflt)

    // The rival coords belong to a different scene, so they stay off by default.
    Assert.Equal(None, Triggers.coordToFire dflt Set.empty ev 5 10)

    // In the rival scene the coord fires — unless it has already fired.
    let scene = "SCENE_AZALEATOWN_RIVAL_BATTLE"
    Assert.Equal(
        Some "AzaleaTownRivalBattleScene1",
        Triggers.coordToFire scene Set.empty ev 5 10 |> Option.map (fun c -> c.Script)
    )
    Assert.Equal(None, Triggers.coordToFire scene (Set.singleton (5, 10)) ev 5 10)


// M9.6 — coverage sweep. The script engine must survive the WHOLE game: every
// real map parses without throwing, and any opcode outside the M9 slice degrades
// to `Unsupported` (a runtime no-op) rather than a crash. This guards against a
// parser change that starts throwing on some far-flung map.

[<Fact>]
let ``every game map parses without throwing and yields commands`` () =
    let maps = System.IO.Directory.GetFiles(Assets.path "maps", "*.asm")
    Assert.True(maps.Length > 300, $"expected the full map set, found {maps.Length}")

    let mutable totalCommands = 0
    for path in maps do
        // Must not throw on any map; record the command count.
        let prog = ScriptParser.parseText (System.IO.File.ReadAllText path)
        totalCommands <- totalCommands + prog.Commands.Length

    Assert.True(totalCommands > 10000, $"expected a substantial command corpus, got {totalCommands}")

[<Fact>]
let ``jumptext ends the script so consecutive sign scripts don't run together`` () =
    // Two adjacent sign scripts, exactly like maps/AzaleaTown.asm's sign block:
    // each is a bare `jumptext` with NO `end` before the next label. jumptext is
    // terminal, so reading the first sign must NOT fall into the second.
    let prog =
        parse
            "FirstSign:\n\
             \tjumptext FirstSignText\n\
             \n\
             SecondSign:\n\
             \tjumptext SecondSignText\n"

    let step = Script.start "FirstSign" World.empty prog ""

    match step.Outcome with
    | Suspended(vm, ShowText(label, _)) ->
        Assert.Equal("FirstSignText", label)
        // Dismissing the first sign ends the script — it does not show the second.
        match (Script.resume None World.empty vm).Outcome with
        | Completed -> ()
        | other -> Assert.Fail($"reading one sign should end the script, got {other}")
    | other -> Assert.Fail($"expected the first sign's text, got {other}")

[<Fact>]
let ``the VM skips unsupported opcodes and runs the rest of the script`` () =
    // A script mixing an out-of-slice opcode (trainertext) with text must still
    // reach the text — Unsupported is a no-op, not a stop.
    let prog =
        parse
            "Mixed:\n\
             \ttrainertext 0\n\
             \twritetext MixedText\n\
             \tend\n"

    let step = Script.start "Mixed" World.empty prog ""

    match step.Outcome with
    | Suspended(_, ShowText(label, _)) -> Assert.Equal("MixedText", label)
    | other -> Assert.Fail($"expected the script to run past trainertext to the text, got {other}")

// --- Trainer macro expansion tests ---

[<Fact>]
let ``trainer macro expands to TalkToTrainerScript: first encounter shows seen text`` () =
    let prog =
        parse
            "TrainerBugCatcherBenny:\n\
             \ttrainer BUG_CATCHER, BENNY1, EVENT_BEAT_BENNY, BennySeenText, BennyBeatenText, 0, .Script\n\
             \n\
             .Script:\n\
             \tendifjustbattled\n\
             \topentext\n\
             \twritetext BennyAfterText\n\
             \twaitbutton\n\
             \tclosetext\n\
             \tend\n"

    // First encounter (flag not set): should show seen text, then battle.
    let step = Script.start "TrainerBugCatcherBenny" World.empty prog ""
    match step.Outcome with
    | Suspended(vm, FacePlayer) ->
        // resume from faceplayer → checkevent (flag not set) → iftrue not taken → opentext → writetext SeenText
        let step2 = Script.resume None World.empty vm
        match step2.Outcome with
        | Suspended(_, ShowText(label, _)) ->
            Assert.Equal("BennySeenText", label)
        | other -> Assert.Fail($"expected seen text, got {other}")
    | other -> Assert.Fail($"expected FacePlayer first, got {other}")

[<Fact>]
let ``trainer macro expands to TalkToTrainerScript: already beaten shows after text`` () =
    let prog =
        parse
            "TrainerBugCatcherBenny:\n\
             \ttrainer BUG_CATCHER, BENNY1, EVENT_BEAT_BENNY, BennySeenText, BennyBeatenText, 0, .Script\n\
             \n\
             .Script:\n\
             \tendifjustbattled\n\
             \topentext\n\
             \twritetext BennyAfterText\n\
             \twaitbutton\n\
             \tclosetext\n\
             \tend\n"

    // Already beaten (flag set): should jump to .Script and show after text.
    let world = World.setEvent "EVENT_BEAT_BENNY" World.empty
    let step = Script.start "TrainerBugCatcherBenny" world prog ""
    match step.Outcome with
    | Suspended(vm, FacePlayer) ->
        let step2 = Script.resume None world vm
        match step2.Outcome with
        | Suspended(_, ShowText(label, _)) ->
            Assert.Equal("BennyAfterText", label)
        | other -> Assert.Fail($"expected after text, got {other}")
    | other -> Assert.Fail($"expected FacePlayer first, got {other}")
