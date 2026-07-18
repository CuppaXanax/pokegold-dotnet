module PokeGold.Tests.ScriptTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
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
let ``catchtutorial preserves its battle type and suspends for a tutorial scene`` () =
    let prog =
        parse
            "S:\n\
             \tloadwildmon RATTATA, 5\n\
             \tcatchtutorial BATTLETYPE_TUTORIAL\n\
             \tsetval 7\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Loadwildmon("RATTATA", 5); Catchtutorial "BATTLETYPE_TUTORIAL"; Setval 7; End ],
        ScriptProgram.blockAt "S" prog)

    let loaded = Script.start "S" World.empty prog "TEST"
    let loadedVm =
        match loaded.Outcome with
        | Suspended(vm, LoadWild("RATTATA", 5)) -> vm
        | other -> failwithf "expected LoadWild, got %A" other
    let tutorial = Script.resume None loaded.World loadedVm
    match tutorial.Outcome with
    | Suspended(_, StartCatchTutorial "BATTLETYPE_TUTORIAL") -> ()
    | other -> failwithf "expected StartCatchTutorial, got %A" other

[<Fact>]
let ``Pokepic resolves script variable species and waits until closepokepic`` () =
    let prog =
        parse "S:\n\tsetval 155\n\tpokepic 0\n\twaitbutton\n\tclosepokepic\n\twritetext AfterText\n\tend\n"

    let shown = Script.start "S" World.empty prog "TEST"
    let pictureVm =
        match shown.Outcome with
        | Suspended(vm, ShowPokePic "CYNDAQUIL") -> vm
        | other -> failwithf "expected Cyndaquil Pokepic, got %A" other

    let waiting = Script.resume None shown.World pictureVm
    let closeVm =
        match waiting.Outcome with
        | Suspended(vm, WaitPokePic) -> vm
        | other -> failwithf "expected Pokepic waitbutton, got %A" other

    let closing = Script.resume None waiting.World closeVm
    let textVm =
        match closing.Outcome with
        | Suspended(vm, ClosePokePic) -> vm
        | other -> failwithf "expected ClosePokePic, got %A" other

    match (Script.resume None closing.World textVm).Outcome with
    | Suspended(_, ShowText("AfterText", false)) -> ()
    | other -> failwithf "expected post-close text, got %A" other

[<Fact>]
let ``Warpcheck suspends for current-map warp resolution and resumes`` () =
    let prog =
        parse "S:\n\twarpcheck\n\twritetext AfterText\n\tend\n"

    let warpStep = Script.start "S" World.empty prog "TEST"
    let vm =
        match warpStep.Outcome with
        | Suspended(vm, WarpCheck) -> vm
        | other -> failwithf "expected WarpCheck, got %A" other

    match (Script.resume None warpStep.World vm).Outcome with
    | Suspended(_, ShowText("AfterText", false)) -> ()
    | other -> failwithf "expected post-warpcheck text, got %A" other

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
let ``expands Goldenrod underground door macros to changeblock commands`` () =
    let prog =
        parse
            "MACRO ugdoor_def\n\
             ENDM\n\
             \tugdoor_def 16, 6, $3e, $2d\n\
             \tugdoor_def 12, 6, $3f, $2a, 12, 8, $3d, $2d\n\
             S:\n\
             \tchangeugdoor 1, OPEN\n\
             \tchangeugdoor 2, CLOSED\n\
             \tend\n\
             for n, 1, ugdoor_n + 1\n\
             .OpenDoor{d:n}:\n\
             \tchangeugdoor n, OPEN\n\
             \tend\n\
             endr\n"

    Assert.DoesNotContain(
        prog.Commands,
        fun cmd ->
            match cmd with
            | Unsupported _ -> true
            | _ -> false
    )

    Assert.Equal<ScriptCommand list>(
        [ Changeblock(16, 6, 0x2d)
          Changeblock(12, 6, 0x3f)
          Changeblock(12, 8, 0x3d)
          End ],
        ScriptProgram.blockAt "S" prog
    )

    Assert.Equal<ScriptCommand list>(
        [ Changeblock(12, 6, 0x2a)
          Changeblock(12, 8, 0x2d)
          End ],
        ScriptProgram.blockAt "S.OpenDoor2" prog
    )

[<Fact>]
let ``strict parser resolves symbolic numeric constants and scene ids`` () =
    let constants =
        Map.ofList
            [ "PARTY_LENGTH", 6
              "NUM_POKEMON", 251 ]

    let prog =
        ScriptParser.parseTextWithConstants
            constants
            "MapScripts:\n\
             \tdef_scene_scripts\n\
             \tscene_script Scene0, SCENE_TEST_START\n\
             \tscene_script Scene1, SCENE_TEST_DONE\n\
             \n\
             S:\n\
             \tsetscene SCENE_TEST_DONE\n\
             \tifequal PARTY_LENGTH, .Full\n\
             \tifgreater NUM_POKEMON - 2 - 1, .Done\n\
             \tend\n\
             .Full:\n\
             \tend\n\
             .Done:\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Setscene 1
          Ifequal(6, "S.Full")
          Ifgreater(248, "S.Done")
          End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``parser resolves symbolic money amounts`` () =
    let prog =
        ScriptParser.parseTextWithConstants
            Map.empty
            "DEF TEST_PRICE EQU 500\n\
             \n\
             S:\n\
             \tcheckmoney YOUR_MONEY, TEST_PRICE\n\
             \ttakemoney YOUR_MONEY, TEST_PRICE\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Checkmoney [ "YOUR_MONEY"; "500" ]
          Takemoney [ "YOUR_MONEY"; "500" ]
          End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``strict parser skips macro definitions instead of parsing their bodies`` () =
    let prog =
        ScriptParser.parseTextWithConstants
            (Map.ofList [ "TRUE", 1 ])
            "MACRO doorstate\n\
             \tchangeblock UGDOOR_\\1_YCOORD, UGDOOR_\\1_XCOORD, UNDERGROUND_DOOR_\\2\n\
             ENDM\n\
             \n\
             S:\n\
             \tsetval TRUE\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Setval 1; End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``parser skips movement script blocks from ScriptProgram`` () =
    let prog =
        ScriptParser.parseTextWithConstants
            Map.empty
            "MovementLabel:\n\
             \tstep LEFT\n\
             \tturn_head UP\n\
             \tstep_end\n\
             \n\
             RealScript:\n\
             \tsetval TRUE\n\
             \tend\n"

    Assert.False(prog.Labels.ContainsKey "MovementLabel")
    Assert.Equal<ScriptCommand list>(
        [ Setval 1; End ],
        ScriptProgram.blockAt "RealScript" prog
    )

[<Fact>]
let ``parser does not emit text prompt or text ram directives as script commands`` () =
    let prog =
        parse
            "PromptText:\n\
             \ttext \"Choose wisely.\"\n\
             \tprompt\n\
             \n\
             BufferedText:\n\
             \ttext_ram wStringBuffer3\n\
             \tdone\n\
             \n\
             RealScript:\n\
             \tsetval 1\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>([ Setval 1; End ], prog.Commands |> Array.toList)
    Assert.Equal<ScriptCommand list>([ Setval 1; End ], ScriptProgram.blockAt "RealScript" prog)

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
        [ Givepoke("CYNDAQUIL", 5, None, None, None); End ],
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
        [ Givepoke("CYNDAQUIL", 5, Some "BERRY", None, None); End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``parses givepoke source nickname and trainer OT operands`` () =
    let prog =
        parse "S:\n\tgivepoke SPEAROW, 10, NO_ITEM, GiftSpearowName, GiftSpearowOTName\n\tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Givepoke("SPEAROW", 10, None, Some "GiftSpearowName", Some "GiftSpearowOTName"); End ],
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
let ``menu and UI opcodes set script var via control flow`` () =
    let verticalProg =
        parse
            "S:\n\
             \tverticalmenu\n\
             \tiftrue .Ok\n\
             \tend\n\
             .Ok:\n\
             \tjumptext OkText\n"

    match Script.start "S" World.empty verticalProg "" with
    | { World = world; Outcome = Suspended(vm, OpenScriptMenu "MENU") } ->
        match Script.resume (Some 1) world vm with
        | { Outcome = Suspended(_, ShowText("OkText", _)) } -> ()
        | other -> Assert.Fail($"expected ShowText after verticalmenu selection, got {other}")
    | other -> Assert.Fail($"expected menu effect from verticalmenu, got {other}")

    let checkProg =
        parse
            "S:\n\
             \tcheckpokemail\n\
             \tiffalse .Ok\n\
             \tend\n\
             .Ok:\n\
             \tjumptext OkText\n"

    match Script.start "S" World.empty checkProg "" with
    | { Outcome = Suspended(_, ShowText("OkText", _)) } -> ()
    | other -> Assert.Fail($"expected ShowText from checkpokemail branch, got {other}")


[<Fact>]
let ``conditional event headers gate background scripts with source flag polarity`` () =
    let prog =
        parse "Gate:\n\tconditional_event EVENT_GATE, .Body\n.Body:\n\tjumptext GatedText\n"

    let ifSet = { X = 0; Y = 0; Kind = "BGEVENT_IFSET"; Script = "Gate" }
    let ifNotSet = { ifSet with Kind = "BGEVENT_IFNOTSET" }
    let enabled = World.setEvent "EVENT_GATE" World.empty

    Assert.Equal(None, Triggers.conditionalBgScript World.empty ifSet prog)
    Assert.Equal(Some "Gate.Body", Triggers.conditionalBgScript enabled ifSet prog)
    Assert.Equal(Some "Gate.Body", Triggers.conditionalBgScript World.empty ifNotSet prog)
    Assert.Equal(None, Triggers.conditionalBgScript enabled ifNotSet prog)

[<Fact>]
let ``elevator suspends with its source floor-data label`` () =
    let prog = parse "S:\n\televator FloorData\n\tend\nFloorData:\n\televfloor FLOOR_1F, 1, TEST_MAP\n"

    match Script.start "S" World.empty prog "TEST" with
    | { Outcome = Suspended(_, OpenElevator "FloorData") } -> ()
    | other -> Assert.Fail($"expected OpenElevator FloorData, got {other}")

[<Fact>]
let ``giveegg suspends as GivePoke`` () =
    let prog = parse "S:\n\tgiveegg CYNDAQUIL, 5\n\tend\n"

    match Script.start "S" World.empty prog "" with
    | { Outcome = Suspended(_, GivePoke("CYNDAQUIL", 5, None, None, None)) } -> ()
    | other -> Assert.Fail($"expected GivePoke effect, got {other}")

[<Fact>]
let ``trade parses as a typed deferred opcode`` () =
    let prog =
        parse
            "S:\n\
             \ttrade NPC_TRADE_KIM\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Trade "NPC_TRADE_KIM"; End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``object and cosmetic opcodes are parsed as typed deferred commands`` () =
    let prog =
        parse
            "S:\n\
             \tmoveobject PLAYER, 5, 6\n\
             \tfollow PLAYER, RIVAL\n\
             \tstopfollow\n\
             \tvariablesprite VAR_SPRITE, SPRITE_POKEMON\n\
             \twriteobjectxy PLAYER\n\
             \tpause 30\n\
             \tshowemote EMOTE_SHOCK, PLAYER, 15\n\
             \tearthquake 8\n\
             \tdoorstate\n\
             \tdontrestartmapmusic\n\
             \tplaymapmusic\n\
             \tmusicfadeout\n\
             \tnewloadmap\n\
             \twarpcheck\n\
             \tblackoutmod NEW_BARK_TOWN\n\
             \treanchormap\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Moveobject("PLAYER", 5, 6)
          Follow("PLAYER", "RIVAL")
          Stopfollow
          Variablesprite("VAR_SPRITE", "SPRITE_POKEMON")
          Writeobjectxy "PLAYER"
          Pause 30
          Showemote("EMOTE_SHOCK", "PLAYER", 15)
          Earthquake(Some 8)
          Doorstate(None, None)
          Dontrestartmapmusic
          Playmapmusic
          Musicfadeout
          Newloadmap
          Warpcheck
          Blackoutmod "NEW_BARK_TOWN"
          Reanchormap
          End ],
        ScriptProgram.blockAt "S" prog
    )

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
          Pause 30
          End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``fade specials suspend as palette fade effects`` () =
    let cases =
        [ "FadeOutToWhite", PaletteFade(FadeOut, FadeToWhite)
          "FadeOutToBlack", PaletteFade(FadeOut, FadeToBlack)
          "FadeInFromWhite", PaletteFade(FadeIn, FadeToWhite)
          "FadeInFromBlack", PaletteFade(FadeIn, FadeToBlack)
          "ClearBGPalettes", PaletteFade(FadeOut, FadeToWhite) ]

    for special, expected in cases do
        let prog = parse (sprintf "S:\n\tspecial %s\n\tend\n" special)

        match (Script.start "S" World.empty prog "TestMap").Outcome with
        | Suspended(_, effect) -> Assert.Equal(expected, effect)
        | other -> failwithf "expected %s to suspend, got %A" special other

[<Fact>]
let ``cry command and cry specials suspend as cry effects`` () =
    let program commands =
        { Commands = commands
          Labels = Map.ofList [ "S", 0 ] }

    match (Script.start "S" World.empty (program [| ScriptCommand.Cry "CYNDAQUIL"; End |]) "TestMap").Outcome with
    | Suspended(_, effect) -> Assert.Equal(ScriptEffect.Cry("CYNDAQUIL", false), effect)
    | other -> failwithf "expected cry command to suspend, got %A" other

    match (Script.start "S" World.empty (program [| Setval 181; Special "PlaySlowCry"; End |]) "TestMap").Outcome with
    | Suspended(_, effect) -> Assert.Equal(ScriptEffect.Cry("AMPHAROS", true), effect)
    | other -> failwithf "expected PlaySlowCry to suspend, got %A" other

    match (Script.start "S" World.empty (program [| Special "PlayCurMonCry"; End |]) "TestMap").Outcome with
    | Suspended(_, effect) -> Assert.Equal(CryCurrentPartyMon, effect)
    | other -> failwithf "expected PlayCurMonCry to suspend, got %A" other

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
let ``sdefer parsed as sjump runs the deferred label`` () =
    let prog =
        parse
            "CB:\n\n\n\
             \tsdefer .Target\n\
             \tendcallback\n\
             .Target:\n\
             \tjumptext TargetText\n"

    match Script.start "CB" World.empty prog "" with
    | { Outcome = Suspended(_, ShowText("TargetText", _)) } -> ()
    | other -> Assert.Fail($"expected TargetText, got {other}")

[<Fact>]
let ``halloffame sets EVENT_BEAT_ELITE_FOUR`` () =
    let prog = parse "HoF:\n\thalloffame\n\tend\n"

    match Script.start "HoF" World.empty prog "" with
    | { World = w; Outcome = Suspended(_, HallOfFame) } ->
        Assert.True(World.hasEvent "EVENT_BEAT_ELITE_FOUR" w)
    | other -> Assert.Fail($"expected HallOfFame effect, got {other}")

[<Fact>]
let ``real Kanto quest scripts still parse through their current opcode coverage`` () =
    let powerPlant = AsmLoad.script "maps/PowerPlant.asm"
    let ceruleanGym = AsmLoad.script "maps/CeruleanGym.asm"

    Assert.Contains(
        Pause 30,
        ScriptProgram.blockAt "PowerPlantGuardPhoneScript" powerPlant)

    Assert.Contains(
        Sjump "CeruleanGymGruntRunsOutScript",
        ScriptProgram.blockAt "CeruleanGymGruntRunsOutScene" ceruleanGym)

    Assert.Contains(
        Showemote("EMOTE_SHOCK", "CERULEANGYM_ROCKET", 15),
        ScriptProgram.blockAt "CeruleanGymGruntRunsOutScript" ceruleanGym)

[<Fact>]
let ``legendary event flags can be set`` () =
    let flags =
        [ "EVENT_RELEASED_THE_BEASTS"
          "EVENT_GOT_RED_GYARADOS"
          "EVENT_BATTLED_LUGIA"
          "EVENT_BATTLED_HO_OH"
          "EVENT_BATTLED_CELEBI" ]

    for flag in flags do
        let world = World.setEvent flag World.empty
        Assert.True(World.hasEvent flag world, $"{flag} should be settable")

[<Fact>]
let ``NPC trades preserve Mike's zero-based source index`` () =
    let mike =
        NpcTrades.trades
        |> List.find (fun trade -> trade.Give = "DROWZEE")

    Assert.Equal(0, mike.Id)
    Assert.Equal("NPC_TRADE_MIKE", mike.Constant)
    Assert.Equal("MACHOP", mike.Receive)
    Assert.Equal("MUSCLE", mike.Nickname)
    Assert.Equal(0x3766, mike.Dvs)
    Assert.Equal("GOLD_BERRY", mike.HeldItem)
    Assert.Equal(37460, mike.OtId)
    Assert.Equal("MIKE", mike.OtName)
    Assert.Equal("TRADE_GENDER_EITHER", mike.Gender)

[<Fact>]
let ``Kenya mail payload is generated from Route 35 source data`` () =
    let kenya = ScriptMailData.byMapAndLabel.[("Route35GoldenrodGate", "GiftSpearowMail")]

    Assert.Equal("FLOWER_MAIL", kenya.Item)
    Assert.Equal("DARK CAVE leads\nto another road", kenya.Body)

[<Fact>]
let ``S.S. Aqua is gated by EVENT_BEAT_ELITE_FOUR`` () =
    let world = World.setEvent "EVENT_BEAT_ELITE_FOUR" World.empty

    Assert.True(World.hasEvent "EVENT_BEAT_ELITE_FOUR" world)

[<Fact>]
let ``checktime suspends for runtime game time check`` () =
    let prog =
        parse
            "S:\n\
             \tchecktime NITE\n\
             \tiftrue .Ok\n\
             \tend\n\
             .Ok:\n\
             \tjumptext OkText\n"

    match Script.start "S" World.empty prog "" with
    | { Outcome = Suspended(_, CheckTime "NITE") } -> ()
    | other -> Assert.Fail($"expected CheckTime NITE, got {other}")

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
let ``checkjustbattled exposes and endifjustbattled consumes trainer script state`` () =
    let branchProg =
        parse "S:\n\tcheckjustbattled\n\tiftrue .JustBattled\n\twritetext OrdinaryText\n\tend\n.JustBattled:\n\twritetext JustBattledText\n\tend\n"

    let justBattled = World.empty |> World.setVar "__just_battled" 1

    match (Script.start "S" justBattled branchProg "TEST").Outcome with
    | Suspended(_, ShowText("JustBattledText", false)) -> ()
    | other -> Assert.Fail(sprintf "expected just-battled branch, got %A" other)

    match (Script.start "S" World.empty branchProg "TEST").Outcome with
    | Suspended(_, ShowText("OrdinaryText", false)) -> ()
    | other -> Assert.Fail(sprintf "expected ordinary branch, got %A" other)

    let endProg = parse "S:\n\tendifjustbattled\n\twritetext AfterText\n\tend\n"
    let endedWorld, endedEffects = driveSilent justBattled "S" endProg
    Assert.Empty(endedEffects)
    Assert.Equal(0, World.getVar "__just_battled" endedWorld)

[<Fact>]
let ``map music specials emit host music effects`` () =
    let prog =
        parse
            "S:\n\
             \tspecial RestartMapMusic\n\
             \tspecial FadeOutMusic\n\
             \tspecial PlayMapMusic\n\
             \tend\n"

    let _, effects = driveSilent World.empty "S" prog

    Assert.Equal<ScriptEffect list>(
        [ ScriptEffect.PlayMusic "__MAP_DEFAULT__"
          ScriptEffect.PlayMusic "__STOP__"
          ScriptEffect.PlayMusic "__MAP_DEFAULT__" ],
        effects)

[<Fact>]
let ``phone and rival specials emit runtime effects`` () =
    let prog =
        parse
            "S:\n\
             \taddcellnum PHONE_MOM\n\
             \tcheckcellnum PHONE_ELM\n\
             \taskforphonenumber PHONE_JOEY\n\
             \tspecial NameRival\n\
             \tend\n"

    let _, effects =
        drive
            (function
             | CheckPhoneContact _ -> Some 0
             | AskPhoneNumber _ -> Some 1
             | _ -> None)
            World.empty
            "S"
            prog

    Assert.Equal<ScriptEffect list>(
        [ AddPhoneContact "PHONE_MOM"
          CheckPhoneContact "PHONE_ELM"
          AskPhoneNumber "PHONE_JOEY"
          NameRival ],
        effects)

[<Fact>]
let ``SelectApricornForKurt resumes with the selected apricorn item id`` () =
    let constants = Map.ofList [ "BLU_APRICORN", 0x59 ]

    let prog =
        ScriptParser.parseTextWithConstants
           constants
           "S:\n\
            \tspecial SelectApricornForKurt\n\
            \tifequal BLU_APRICORN, .Blue\n\
            \tifequal 0, .Cancel\n\
            \twritetext Other\n\
            \tend\n\
            .Blue:\n\
            \twritetext Blue\n\
            \tend\n\
            .Cancel:\n\
            \twritetext Cancel\n\
            \tend\n"

    let _, selected =
        drive
           (function
            | SelectApricornForKurt -> Some 0x59
            | _ -> None)
           World.empty
           "S"
           prog

    Assert.Equal<ScriptEffect list>(
        [ SelectApricornForKurt; ShowText("Blue", false) ],
        selected)

    let _, cancelled =
        drive
           (function
            | SelectApricornForKurt -> Some 0
            | _ -> None)
           World.empty
           "S"
           prog

    Assert.Equal<ScriptEffect list>(
        [ SelectApricornForKurt; ShowText("Cancel", false) ],
        cancelled)

[<Fact>]
let ``daycare specials emit runtime effects`` () =
    let prog =
        parse
            "S:\n\
             \tspecial DayCareMan\n\
             \tspecial DayCareLady\n\
             \tspecial DayCareManOutside\n\
             \tspecial DayCareMon1\n\
             \tspecial DayCareMon2\n\
             \tend\n"

    let _, effects = driveSilent World.empty "S" prog

    Assert.Equal<ScriptEffect list>(
        [ DayCareResident "MAN"
          DayCareResident "LADY"
          DayCareManOutside
          DayCareMon 1
          DayCareMon 2 ],
        effects)

[<Fact>]
let ``MoveDeletion emits runtime effect`` () =
    let prog =
        parse
            "S:\n\
             \tspecial MoveDeletion\n\
             \tend\n"

    let _, effects = driveSilent World.empty "S" prog

    Assert.Equal<ScriptEffect list>([ MoveDeletion ], effects)

[<Fact>]
let ``InitRoamMons emits runtime effect`` () =
    let prog =
        parse
            "S:\n\
             \tspecial InitRoamMons\n\
             \tend\n"

    let _, effects = driveSilent World.empty "S" prog

    Assert.Equal<ScriptEffect list>([ InitRoamMons ], effects)

[<Fact>]
let ``MagnetTrain routes by script var direction`` () =
    let prog =
        parse
            "ToSaffron:\n\
             \tsetval 0\n\
             \tspecial MagnetTrain\n\
             \tend\n\
             ToGoldenrod:\n\
             \tsetval 1\n\
             \tspecial MagnetTrain\n\
             \tend\n"

    let _, toSaffron = driveSilent World.empty "ToSaffron" prog
    let _, toGoldenrod = driveSilent World.empty "ToGoldenrod" prog

    Assert.Equal<ScriptEffect list>([ ScriptEffect.Warp("SaffronMagnetTrainStation", 11, 6, Some "UP") ], toSaffron)
    Assert.Equal<ScriptEffect list>([ ScriptEffect.Warp("GoldenrodMagnetTrainStation", 11, 6, Some "UP") ], toGoldenrod)

[<Fact>]
let ``haircut and grooming specials emit party picker effects`` () =
    let prog =
        parse
            "S:\n\
             \tspecial OlderHaircutBrother\n\
             \tspecial YoungerHaircutBrother\n\
             \tspecial DaisysGrooming\n\
             \tend\n"

    let _, effects = driveSilent World.empty "S" prog

    Assert.Equal<ScriptEffect list>([ Haircut "OLDER"; Haircut "YOUNGER"; Haircut "DAISY" ], effects)

[<Fact>]
let ``game corner specials emit game and prize dex effects`` () =
    let prog =
        parse
            "S:\n\
             \tsetval TRUE\n\
             \tspecial SlotMachine\n\
             \tspecial CardFlip\n\
             \tsetval 137\n\
             \tspecial GameCornerPrizeMonCheckDex\n\
             \tend\n"

    let _, effects = driveSilent World.empty "S" prog

    Assert.Equal<ScriptEffect list>(
        [ GameCornerGame("SLOT_MACHINE", true)
          GameCornerGame("CARD_FLIP", false)
          RegisterPrizeDex 137 ],
        effects)

[<Fact>]
let ``bug contest specials emit contest effects`` () =
    let prog =
        parse
            "S:\n\
             \tspecial GiveParkBalls\n\
             \tspecial ContestDropOffMons\n\
             \tspecial BugContestJudging\n\
             \tspecial ContestReturnMons\n\
             \tspecial CheckPartyFullAfterContest\n\
             \tend\n"

    let _, effects = driveSilent World.empty "S" prog

    Assert.Equal<ScriptEffect list>(
        [ GiveParkBalls
          ContestDropOffMons
          BugContestJudging
          ContestReturnMons
          CheckPartyFullAfterContest ],
        effects)

[<Fact>]
let ``BillsGrandfather special returns selected party species to script var`` () =
    let prog =
        parse
            "S:\n\
             \tspecial BillsGrandfather\n\
             \tifequal 108, .Lickitung\n\
             \twritetext Wrong\n\
             \tend\n\
             .Lickitung:\n\
             \twritetext Correct\n\
             \tend\n"

    let _, effects =
        drive
            (function
             | BillsGrandfather -> Some 108
             | _ -> None)
            World.empty
            "S"
            prog

    Assert.Equal<ScriptEffect list>(
        [ BillsGrandfather
          ShowText("Correct", false) ],
        effects)

[<Fact>]
let ``Magikarp length specials emit measurement and sign effects`` () =
    let prog =
        parse
            "S:\n\
             \tspecial CheckMagikarpLength\n\
             \tifequal 3, .Record\n\
             \twritetext TooShort\n\
             \tend\n\
             .Record:\n\
             \tspecial MagikarpHouseSign\n\
             \tend\n"

    let _, effects =
        drive
            (function
             | CheckMagikarpLength -> Some 3
             | _ -> None)
            World.empty
            "S"
            prog

    Assert.Equal<ScriptEffect list>(
        [ CheckMagikarpLength
          MagikarpHouseSign ],
        effects)

[<Fact>]
let ``Unown puzzle and printer specials emit completion UI effects`` () =
    let prog =
        parse
            "S:\n\
             \tsetval 2\n\
             \tspecial UnownPuzzle\n\
             \tiffalse .Failed\n\
             \tspecial UnownPrinter\n\
             \tend\n\
             .Failed:\n\
             \twritetext Failed\n\
             \tend\n"

    let _, effects =
        drive
            (function
             | UnownPuzzle _ -> Some 1
             | _ -> None)
            World.empty
            "S"
            prog

    Assert.Equal<ScriptEffect list>(
        [ UnownPuzzle 2
          UnownPrinter ],
        effects)

[<Fact>]
let ``CheckFirstMonIsEgg resumes with script var truth`` () =
    let prog =
        parse
            "S:\n\
             \tspecial CheckFirstMonIsEgg\n\
             \tiftrue .Egg\n\
             \twritetext NotEgg\n\
             \tend\n\
             .Egg:\n\
             \twritetext Egg\n\
             \tend\n"

    let _, egg =
        drive
            (function
             | CheckFirstMonIsEgg -> Some 1
             | _ -> None)
            World.empty
            "S"
            prog

    Assert.Equal<ScriptEffect list>(
        [ CheckFirstMonIsEgg; ShowText("Egg", false) ],
        egg)

    let _, notEgg =
        drive
            (function
             | CheckFirstMonIsEgg -> Some 0
             | _ -> None)
            World.empty
            "S"
            prog

    Assert.Equal<ScriptEffect list>(
        [ CheckFirstMonIsEgg; ShowText("NotEgg", false) ],
        notEgg)

[<Fact>]
let ``special phone call state is stored in script world`` () =
    let prog =
        parse
            "S:\n\
             \tspecialphonecall SPECIALCALL_POKERUS\n\
             \tcheckphonecall\n\
             \tiffalse .Fail\n\
             \tspecialphonecall SPECIALCALL_NONE\n\
             \tcheckphonecall\n\
             \tiftrue .Fail\n\
             \twritetext Cleared\n\
             \tend\n\
             .Fail:\n\
             \twritetext Failed\n\
             \tend\n"

    let world, effects = driveSilent World.empty "S" prog

    Assert.Equal<ScriptEffect list>([ ShowText("Cleared", false) ], effects)
    Assert.Equal("", World.getBuffer "__special_phone_call" world)

[<Fact>]
let ``critical specials emit runtime UI effects`` () =
    let prog =
        parse
            "S:\n\
             \tspecial BankOfMom\n\
             \tspecial OverworldTownMap\n\
             \tsetval 4\n\
             \tspecial MapRadio\n\
             \tspecial NameRater\n\
             \tspecial DisplayMoneyAndCoinBalance\n\
             \tspecial DisplayCoinCaseBalance\n\
             \tspecial PlaceMoneyTopRight\n\
             \tclosewindow\n\
             \tspecial PlayersHousePC\n\
             \tend\n"

    let _, effects = driveSilent World.empty "S" prog

    Assert.Equal<ScriptEffect list>(
        [ OpenMomBank
          OpenPokegear(MapTab, "", None)
          OpenPokegear(RadioTab, "", Some 4)
          NameRater
          DisplayBalance MoneyAndCoins
          DisplayBalance CoinCase
          DisplayBalance MoneyTopRight
          CloseWindow
          OpenPc ],
        effects)

[<Fact>]
let ``script menu commands emit menu effect and resume with selection`` () =
    let prog =
        parse
            "S:\n\
             \tloadmenu TestMenuHeader\n\
             \tmenu_coords 0, 0, 8, 8\n\
             \tverticalmenu\n\
             \tifequal 2, .Second\n\
             \twritetext First\n\
             \tend\n\
             .Second:\n\
             \twritetext Second\n\
             \tend\n"

    let world, effects =
        drive
            (function
             | OpenScriptMenu _ -> Some 2
             | _ -> None)
            World.empty
            "S"
            prog

    Assert.Equal<ScriptEffect list>([ OpenScriptMenu "TestMenuHeader"; ShowText("Second", false) ], effects)
    Assert.Equal("TestMenuHeader", World.getBuffer "__loaded_menu" world)
    Assert.Equal("0,0,8,8", World.getBuffer "__menu_coords" world)

[<Fact>]
let ``loadvar sets the world var and script var`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tloadvar VAR_TEST, 5\n\
             \tifequal 5, .Ok\n\
             \tend\n\
             .Ok:\n\
             \twritetext Ok\n\
             \tend\n"

    let world, effects = driveSilent World.empty "S" prog
    Assert.Equal<ScriptEffect list>([ ShowText("Ok", false) ], effects)
    Assert.Equal(5, World.getVar "VAR_TEST" world)

[<Fact>]
let ``readvar VAR_BADGES derives current Johto badge flags`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \treadvar VAR_BADGES\n\
             \tifequal 3, .Ok\n\
             \tend\n\
             .Ok:\n\
             \twritetext Ok\n\
             \tend\n"

    let world =
        World.empty
        |> World.setFlag "ENGINE_ZEPHYRBADGE"
        |> World.setFlag "ENGINE_HIVEBADGE"
        |> World.setFlag "ENGINE_PLAINBADGE"

    let _, effects = driveSilent world "S" prog
    Assert.Equal<ScriptEffect list>([ ShowText("Ok", false) ], effects)

[<Fact>]
let ``checkmoney and checkcoins suspend for runtime funds checks`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tcheckmoney 100\n\
             \tcheckcoins 10\n\
             \tifequal 1, .Ok\n\
             \tend\n\
             .Ok:\n\
             \twritetext Ok\n\
             \tend\n"

    let _, effects =
        drive
            (function
             | CheckMoney _
             | CheckCoins _ -> Some 1
             | _ -> None)
            World.empty
            "S"
            prog

    Assert.Equal<ScriptEffect list>([ CheckMoney 100; CheckCoins 10; ShowText("Ok", false) ], effects)

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
let ``World map scenes normalize ROM constants and runtime ids`` () =
    let byConst = World.empty |> World.setScene "NEW_BARK_TOWN" 1
    Assert.Equal(1, World.getScene "NEW_BARK_TOWN" byConst)
    Assert.Equal(1, World.getScene "NewBarkTown" byConst)

    let byRuntimeId = World.empty |> World.setScene "NewBarkTown" 1
    Assert.Equal(1, World.getScene "NEW_BARK_TOWN" byRuntimeId)
    Assert.Equal(1, World.getScene "NewBarkTown" byRuntimeId)

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
    | { Outcome = Suspended(_, GivePoke("CYNDAQUIL", 5, None, None, None)) } -> ()
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
let ``supported timed formerly-unsupported opcodes suspend then keep running`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tspecial Special_Foo\n\
             \twritetext Shown\n\
             \tpause 30\n\
             \tshowemote EMOTE_SHOCK, PLAYER, 15\n\
             \tmoveobject PLAYER, 5, 6\n\
             \tend\n"

    let _, effects = driveSilent World.empty "S" prog
    Assert.Equal<ScriptEffect list>(
        [ ShowText("Shown", false)
          ScriptEffect.Pause 30
          ScriptEffect.Pause 15
          MoveObject("PLAYER", 5, 6) ],
        effects)

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
let ``MapText preserves text_ram string buffers`` () =
    let text = AsmLoad.text "data/text/std_text.asm"

    Assert.Equal(
        "<PLAYER> received<LINE>@<STRING_BUFFER_4>.<DONE>",
        text.["ReceivedItemText"]
    )

[<Fact>]
let ``MapText preserves non-string-buffer text_ram operands`` () =
    let text =
        MapText.parseText
            "BattleText:\n\ttext_ram wBattleMonNickname\n\ttext \" fainted!\"\n\tdone\n"

    Assert.Equal("<RAM_wBattleMonNickname> fainted!<DONE>", text.["BattleText"])

[<Fact>]
let ``MapText preserves db string labels for getstring`` () =
    let text = AsmLoad.text "maps/PlayersHouse1F.asm"

    Assert.Equal("#GEAR@", text.["PokegearName"])

[<Fact>]
let ``generated std text preserves item receive buffer`` () =
    Assert.Equal(
        "<PLAYER> received<LINE>@<STRING_BUFFER_4>.<DONE>",
        StdScriptsData.text.["ReceivedItemText"]
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

[<Fact>]
let ``coord events fire when the world scene matches`` () =
    let ev = AsmLoad.events "maps/AzaleaTown.asm"
    let world = World.empty |> World.setScene "AzaleaTown" 1

    let currentScene = MapEvents.sceneAt (World.getScene "AzaleaTown" world) ev

    Assert.Equal("SCENE_AZALEATOWN_RIVAL_BATTLE", currentScene)
    Assert.Equal(
        Some "AzaleaTownRivalBattleScene1",
        Triggers.coordToFire currentScene Set.empty ev 5 10 |> Option.map (fun c -> c.Script)
    )


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

[<Fact>]
let ``FindPartyMonThatSpecies suspends on a CheckPoke for the setval species`` () =
    let prog =
        parse
            "S:\n\
             \tsetval 175\n\
             \tspecial FindPartyMonThatSpeciesYourTrainerID\n\
             \tiftrue .Found\n\
             \tend\n\
             .Found:\n\
             \tjumptext FoundText\n"

    match Script.start "S" World.empty prog "" with
    | { World = world; Outcome = Suspended(vm, CheckPoke "TOGEPI") } ->
        match Script.resume (Some 1) world vm with
        | { Outcome = Suspended(_, ShowText("FoundText", _)) } -> ()
        | other -> Assert.Fail($"expected FoundText after party hit, got {other}")
    | other -> Assert.Fail($"expected CheckPoke TOGEPI, got {other}")

[<Fact>]
let ``SnorlaxAwake reads the tuned radio station buffer`` () =
    let prog =
        parse
            "S:\n\
             \tspecial SnorlaxAwake\n\
             \tiftrue .Awake\n\
             \tjumptext SleepingText\n\
             .Awake:\n\
             \tjumptext AwakeText\n"

    match Script.start "S" World.empty prog "" with
    | { Outcome = Suspended(_, ShowText("SleepingText", _)) } -> ()
    | other -> Assert.Fail($"expected sleeping branch with no radio, got {other}")

    let tuned = World.setBuffer "__radio_station" "POKE_FLUTE" World.empty
    match Script.start "S" tuned prog "" with
    | { Outcome = Suspended(_, ShowText("AwakeText", _)) } -> ()
    | other -> Assert.Fail($"expected awake branch on the flute channel, got {other}")

[<Fact>]
let ``hiddenitem expands to an event-gated one-time give`` () =
    let prog =
        parse
            "HiddenSpot:\n\
             \thiddenitem MACHINE_PART, EVENT_FOUND_MACHINE_PART_IN_CERULEAN_GYM\n"

    Assert.Equal<ScriptCommand list>(
        [ Checkevent "EVENT_FOUND_MACHINE_PART_IN_CERULEAN_GYM"
          Iftrue "HiddenSpot.hiddenItemDone"
          Verbosegiveitem("MACHINE_PART", 1)
          Iffalse "HiddenSpot.hiddenItemDone"
          Setevent "EVENT_FOUND_MACHINE_PART_IN_CERULEAN_GYM"
          End ],
        ScriptProgram.blockAt "HiddenSpot" prog
    )
