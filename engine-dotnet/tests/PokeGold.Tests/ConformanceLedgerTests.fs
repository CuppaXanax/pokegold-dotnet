module PokeGold.Tests.ConformanceLedgerTests

open System
open Microsoft.FSharp.Reflection
open Xunit
open PokeGold.Game.Battle
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Scenes

type ConformanceCategory =
    | ScriptCommandCase
    | ScriptSpecial
    | ObjectType
    | BgEventKind
    | MapCallbackKind
    | SceneSurface
    | MoveEffect
    | ItemHeldEffect
    | ItemFieldMenu
    | ItemBattleMenu
    | FieldMove
    | HostEffectCase

type ConformanceStatus =
    | FaithfulTested
    | ImplementedApproximate
    | StubNoOp
    | Unknown

type ConformanceTag =
    | CriticalPathJohto
    | CriticalPathKanto
    | RequiredFor100Percent
    | SideSystem
    | Cosmetic
    | LinkOnly
    | UnknownReachability

type LedgerEntry =
    { Category: ConformanceCategory
      Name: string
      Status: ConformanceStatus
      Tags: Set<ConformanceTag>
      Notes: string }

module ConformanceLedger =
    let private entry category name status tags notes =
        { Category = category
          Name = name
          Status = status
          Tags = Set.ofList tags
          Notes = notes }

    let private many category status tags notes names =
        names |> List.map (fun name -> entry category name status tags notes)

    let all: LedgerEntry list =
        [
          yield! many ScriptCommandCase ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Runtime has a typed path, but behavior still needs command-level conformance proof."
            [ "Scall"; "Sjump"; "Jumpstd"; "Callstd"; "Iffalse"; "Iftrue"; "Ifequal"; "Ifnotequal"; "Ifgreater"; "Ifless"
              "Setval"; "Addval"; "Readvar"; "Writevar"; "Loadvar"; "Loadmem"; "Readmem"; "Writemem"; "Random"
              "Checkevent"; "Clearevent"; "Setevent"; "Checkflag"; "Clearflag"; "Setflag"
              "Checkmapscene"; "Setmapscene"; "Checkscene"; "Setscene"
              "Giveitem"; "Takeitem"; "Checkitem"; "Verbosegiveitem"
              "Checkmoney"; "Takemoney"; "Givemoney"; "Checkcoins"; "Takecoins"; "Givecoins"
              "Opentext"; "Closetext"; "Writetext"; "Jumptext"; "Jumptextfaceplayer"; "Waitbutton"; "Promptbutton"; "Yesorno"
              "Loadwildmon"; "Givepoke"; "Checkpoke"; "Loadtrainer"; "Reloadmapafterbattle"; "Winlosstext"; "Setlasttalked"; "Giveegg"
              "Applymovement"; "Faceplayer"; "Faceobject"; "Disappear"; "Appear"; "Turnobject"; "Moveobject"; "Follow"; "Stopfollow"; "Variablesprite"; "Pause"; "Showemote"; "Earthquake"
              "Playmusic"; "Playsound"
              "Warp"; "Warpfacing"; "Reloadmap"; "Refreshmap"; "Changeblock"; "Doorstate"; "Dontrestartmapmusic"; "Playmapmusic"; "Musicfadeout"; "Newloadmap"; "Blackoutmod"; "Reanchormap"
              "End"; "EndAll"; "Halloffame"; "Credits"; "Special"; "Pokemart"
              "Checktime"; "Endifjustbattled"; "Gettrainername"; "Getitemname"; "Getmonname"; "Getstring"; "Getnum"; "Getcurlandmarkname"; "TeleportFrom"; "TreeShake" ]

          yield! many ScriptCommandCase ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "Phone contact commands mutate player state or route through a yes/no effect; broader phone system behavior still needs conformance proof."
            [ "Addcellnum"; "Checkcellnum"; "Askforphonenumber" ]

          yield! many ScriptCommandCase ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "Phone-call and script window commands now update runtime world/UI state with targeted VM and scheduler coverage."
            [ "Checkphonecall"; "Specialphonecall"; "Closewindow" ]

          yield! many ScriptCommandCase ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "Menu commands preserve loaded menu state and route vertical/2D menu choices through a generic runtime UI."
            [ "Loadmenu"; "Verticalmenu"; "TwoDMenu"; "MenuCoords" ]

          yield entry ScriptCommandCase "Catchtutorial" FaithfulTested [ CriticalPathJohto; RequiredFor100Percent ] "Route 29 preserves the source battle-type operand, stages the scripted wild Rattata, and runs an automated ordinary BattleScene with Dude's temporary level-5 Rattata and one Poke Ball without mutating the real player."

          yield entry ScriptCommandCase "Itemnotify" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Runtime test proves giveitem stages the current item, itemnotify renders the source item and pocket names, waits in a textbox, and resumes the script."

          yield entry ScriptCommandCase "Checkjustbattled" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "SCR-001 ScriptTests prove the source-compatible transient trainer-script truth value, false branch, and one-shot endifjustbattled consumption through the port's __just_battled state seam."

          yield entry ScriptCommandCase "Startbattle" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "SCR-001 runtime tests prove script-visible WIN=0 for capture, DRAW=2 for wild RUN, and existing BAT-016 through BAT-019 loss blackout/abort behavior. reloadmapafterbattle's Mom/Bill post-battle dispatch remains open."

          yield! many ScriptCommandCase ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "SCR-003 VM and Elm's Lab runtime tests prove source operand-0 species resolution, visible front-pic rendering, A/B waitbutton dismissal, and close-to-text script resumption. Exact GBC tilemap/SGB restoration remains presentation work."
            [ "Pokepic"; "Closepokepic" ]

          yield entry ScriptCommandCase "Warpcheck" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "SCR-003 ScriptTests prove the command suspends and resumes; the real Kabuto Ruins puzzle runs source WarpCheck semantics through the generated current-cell warp event into the inner chamber. GBC collision-byte classification and map-entry modes use the port's ordinary warp resolver."

          yield! many ScriptCommandCase ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "OVR-006 consumes every generated CMDQUEUE_STONETABLE pairing when a settled Strength boulder reaches its source warp, then executes the mapped fallout script. Ice Path and Blackthorn Gym runtime tests prove both outcomes; generic Game Boy command-queue scheduling remains unmodelled outside these only generated uses."
            [ "Stonetable"; "Cmdqueue"; "Writecmdqueue" ]

          yield entry ScriptCommandCase "Trade" ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "SCR-003 generates all six source NPC trade records and runs a player-facing party selection flow. Mike's real Goldenrod script proves source replacement, trade marker, and script resumption; scene tests prove recipient level/order/DVs/item/OT/Dex registration and Emy's female-only gate. Exact dialogue/animation and mail restrictions remain UI work."

          yield entry ScriptCommandCase "ConditionalEvent" ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "SCR-003 handles the source map-data macro at BGEVENT_IFSET/BGEVENT_IFNOTSET dispatch rather than as a VM opcode. Polarity tests and the real Rocket Base locked door prove its flag-gated body and silent no-action path."

          yield! many ScriptCommandCase ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "SCR-003 consumes generated elevfloor table records through a source-style floor selector. Goldenrod proves cancel/current-floor false result and a 2F transition after source timing; Celadon proves its independent table. The port directly resolves the selected map/warp after script completion instead of emulating GBC backup-warp memory."
            [ "Elevator"; "Elevfloor" ]

          yield! many ScriptCommandCase StubNoOp [ SideSystem; RequiredFor100Percent ] "Generated command is typed, but current runtime behavior is a no-op, fixed dummy result, or intentionally ad-hoc fallback for side systems."
            [ "Givepokemail"; "Checkpokemail"; "Writeobjectxy"; "Ugdoor"; "Describedecoration" ]

          yield entry ScriptCommandCase "Cry" ImplementedApproximate [ Cosmetic; RequiredFor100Percent ] "D Pokémon cries route script `cry` through parsed data/pokemon/cries.asm metadata and audio/cries.asm base scripts; `Waitsfx` still does not block on active audio."

          yield! many ScriptCommandCase StubNoOp [ Cosmetic; RequiredFor100Percent ] "Generated command is typed, but only cosmetic/timing behavior remains."
            [ "Waitsfx" ]

          yield! many ScriptCommandCase StubNoOp [ UnknownReachability ] "Generated command is typed, but reachability and required behavior have not been triaged."
            [ "Checkver"; "Unsupported" ]

          yield! many ScriptSpecial ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Explicitly handled by the script integration layer."
            [ "HealParty"; "PokemonCenterPC"; "RestartMapMusic"; "PlayMapMusic"; "FadeOutMusic"; "NameRival"
              "InitialSetDSTFlag"; "InitialClearDSTFlag" ]

          yield! many ScriptSpecial ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "Runtime UI/state path exists and is covered; deeper exact menu fidelity remains future UI work."
            [ "BankOfMom"; "DisplayMoneyAndCoinBalance"; "MapRadio"; "OverworldTownMap"; "PlayersHousePC" ]

          yield! many ScriptSpecial ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "Runtime balance overlay path exists and is covered for scripts that show coin/money windows."
            [ "DisplayCoinCaseBalance"; "PlaceMoneyTopRight" ]

          yield entry ScriptSpecial "SetDayOfWeek" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Weekday setup UI is covered by WeekdaySceneTests and OverworldSchedulerTests."

          yield entry ScriptSpecial "SelectApricornForKurt" FaithfulTested [ SideSystem; RequiredFor100Percent ] "B1 tests cover wScriptVar item-id resume, disassembly-order bag filtering/cancel, and KurtsHouse consuming the selected apricorn."

          yield entry ScriptSpecial "CheckFirstMonIsEgg" FaithfulTested [ SideSystem; RequiredFor100Percent ] "B2 VM tests cover true/false script-var branching; runtime handler reads the generated-egg marker used by BreedingTests."

          yield! many ScriptSpecial ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "B2 DayCareScene tests cover resident deposit/withdraw, Route 34 egg pickup, and script effect routing; exact text/level-growth fidelity remains future daycare polish."
            [ "DayCareLady"; "DayCareMan"; "DayCareManOutside"; "DayCareMon1"; "DayCareMon2" ]

          yield entry ScriptSpecial "MoveDeletion" FaithfulTested [ SideSystem; RequiredFor100Percent ] "B3 tests cover script effect routing and the move deleter scene's party-pick, move-pick, confirmation, and move/PP compaction behavior."

          yield entry ScriptSpecial "InitRoamMons" FaithfulTested [ SideSystem; RequiredFor100Percent ] "B4 tests cover script effect routing, disassembly starting species/routes/levels, save persistence through World, and roamer encounter override on matching grass routes."

          yield entry ScriptSpecial "MagnetTrain" FaithfulTested [ SideSystem; RequiredFor100Percent ] "B5 tests cover script-var direction routing and PASS-gated Saffron-to-Goldenrod runtime travel through the station officer script."

          yield! many ScriptSpecial FaithfulTested [ SideSystem; RequiredFor100Percent ] "B6 tests cover day-gated Goldenrod Underground runtime scripts, party selection, symbolic price parsing, money deduction, and friendship gains for both haircut brothers."
            [ "OlderHaircutBrother"; "YoungerHaircutBrother" ]

          yield entry ScriptSpecial "GameCornerPrizeMonCheckDex" FaithfulTested [ SideSystem; RequiredFor100Percent ] "B7 runtime test covers setval-staged prize dex registration before the Celadon Porygon givepoke path."

          yield! many ScriptSpecial ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "B7 tests cover SlotMachine/CardFlip script dispatch and a minimal fair coin-game runtime seam; exact reel/card UI remains future Game Corner polish."
            [ "SlotMachine"; "CardFlip" ]

          yield entry ScriptSpecial "NameRater" ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "B8 tests cover Goldenrod Name Rater runtime selection and renaming through NamingScene; exact internal text prompts remain UI polish."

          yield! many ScriptSpecial ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "B9 tests cover Park Ball setup, party drop-off/return seams, deterministic contestant selection, Scyther/Pinsir first-place judging, and caught-mon result cleanup; exact timed contest UI and NPC score randomization remain future polish."
            [ "BugContestJudging"; "CheckPartyFullAfterContest"; "ContestDropOffMons"; "ContestReturnMons"; "GiveParkBalls"; "SelectRandomBugContestContestants" ]

          yield entry ScriptSpecial "BillsGrandfather" FaithfulTested [ SideSystem; RequiredFor100Percent ] "B10 tests cover party selection returning the selected species id and BillsHouse reward/reject branches with the species-name text buffer."

          yield! many ScriptSpecial FaithfulTested [ SideSystem; RequiredFor100Percent ] "B11 tests cover the disassembly DV/OT-id Magikarp length formula, new-record reward, too-short branch, and current-record sign text."
            [ "CheckMagikarpLength"; "MagikarpHouseSign" ]

          yield! many ScriptSpecial ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "B12 tests cover UnownPuzzle returning solved truth so chamber completion scripts set the right events/flags, plus Research Center UnownPrinter UI dispatch; exact sliding-panel and Game Boy Printer UI fidelity remains future polish."
            [ "UnownPrinter"; "UnownPuzzle" ]

          yield entry ScriptSpecial "HealMachineAnim" StubNoOp [ CriticalPathJohto; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback; StdScriptsTests cover that nurse scripts reach HealParty and HealTests cover continuing past the cosmetic animation seam."

          yield! many ScriptSpecial ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "Routes the setval-staged species through the CheckPoke party effect; the YourTrainerID OT check is unmodelled (all party mons are the player's)."
            [ "FindPartyMonThatSpecies"; "FindPartyMonThatSpeciesYourTrainerID" ]

          yield entry ScriptSpecial "DaisysGrooming" FaithfulTested [ SideSystem; RequiredFor100Percent ] "Blues House runtime test covers 3 PM gating, party selection, source 255/256 grooming chance, low-tier +3 friendship, and daily ENGINE_DAISYS_GROOMING flag."

          yield entry ScriptSpecial "ProfOaksPCBoot" FaithfulTested [ SideSystem; RequiredFor100Percent ] "SCR-009 Oak rating boundary coverage preserves all 19 source ceilings and the Oaks Lab staged runtime test proves live seen/owned counts, exact rating text, matching fanfare, and script resumption."

          yield! many ScriptSpecial StubNoOp [ SideSystem; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback and is skipped for side systems or completion content."
            [ "CheckForLuckyNumberWinners"
              "CheckLuckyNumberShowFlag"; "CheckMysteryGift"; "CheckPokerus"
              "GetFirstPokemonHappiness"; "GetMysteryGiftItem"; "GiveShuckle"
              "MrChrono"; "PhotoStudio"
              "PrintTodaysLuckyNumber"; "ResetLuckyNumberShowFlag"; "ReturnShuckie"
              "ToggleDecorationsVisibility"; "ToggleMaptileDecorations"; "TrainerHouse"; "UnlockMysteryGift" ]

          yield entry ScriptSpecial "SnorlaxAwake" FaithfulTested [ CriticalPathKanto; RequiredFor100Percent ] "A18 runtime test tunes Poké Flute through the Pokégear radio UI, then verifies Vermilion Snorlax wakes, battles, and disappears."

          yield! many ScriptSpecial ImplementedApproximate [ Cosmetic; RequiredFor100Percent ] "D Screen fades route `Fade*`/`ClearBGPalettes` specials through an eight-frame scene overlay matching the four palette levels with two DelayFrames each in engine/tilesets/timeofday_pals.asm; exact CGB palette RAM mutation remains host-renderer polish."
            [ "ClearBGPalettes"; "FadeInFromBlack"; "FadeInFromWhite"; "FadeOutToBlack"; "FadeOutToWhite" ]

          yield! many ScriptSpecial ImplementedApproximate [ Cosmetic; RequiredFor100Percent ] "D Pokémon cries route PlaySlowCry and PlayCurMonCry through parsed cry metadata/base scripts; PlaySlowCry applies the -$140 pitch and +$60 length offsets from engine/events/play_slow_cry.asm."
            [ "PlayCurMonCry"; "PlaySlowCry" ]

          yield! many ScriptSpecial StubNoOp [ Cosmetic; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback and is skipped for cosmetic/host presentation behavior."
            [ "Diploma"; "LoadUsedSpritesGFX"; "PrintDiploma"; "ReloadSpritesNoPalettes"; "UpdateSprites" ]

          yield! many ScriptSpecial StubNoOp [ LinkOnly; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback and is skipped for link-only systems."
            [ "CableClubCheckWhichChris"; "CheckBothSelectedSameRoom"; "CheckLinkTimeout_Receptionist"; "CheckTimeCapsuleCompatibility"; "CloseLink"
              "Colosseum"; "DisplayLinkRecord"; "EnterTimeCapsule"; "FailedLinkToPast"; "GameboyCheck"; "SetBitsForBattleRequest"
              "SetBitsForLinkTradeRequest"; "SetBitsForTimeCapsuleRequest"; "TimeCapsule"; "TradeCenter"; "TryQuickSave"; "WaitForLinkedFriend"; "WaitForOtherPlayerToExit" ]

          yield! many ObjectType ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "A-press dispatch exists, but type-specific GSC object dispatch still needs conformance work."
            [ "OBJECTTYPE_SCRIPT"; "OBJECTTYPE_TRAINER"; "OBJECTTYPE_ITEMBALL" ]

          yield! many BgEventKind ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Generated BG event kind is known; dispatch semantics need per-kind tests."
            [ "BGEVENT_READ"; "BGEVENT_IFSET"; "BGEVENT_IFNOTSET"; "BGEVENT_UP"; "BGEVENT_LEFT"; "BGEVENT_RIGHT" ]

          yield entry BgEventKind "BGEVENT_ITEM" FaithfulTested [ CriticalPathKanto; RequiredFor100Percent ] "A17 Cerulean Gym hidden Machine Part runtime test covers A-press BG item dispatch, event gating, item grant, and return-to-manager consumption."

          yield! many MapCallbackKind ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Map-entry callbacks run through the scheduler, but callback-kind ordering still needs conformance tests."
            [ "MAPCALLBACK_NEWMAP"; "MAPCALLBACK_TILES"; "MAPCALLBACK_OBJECTS"; "MAPCALLBACK_CMDQUEUE" ]

          yield! many SceneSurface ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Scene exists and can be driven, but full GUI e2e conformance is future UI-epic work."
            [ "MartScene"; "NamingScene"; "OptionsScene"; "OverworldScene"; "PackScene"; "PartyScene"
              "PokegearScene"; "SaveMenuScene"; "StartMenuScene"; "TextBoxScene"; "YesNoScene" ]

          yield entry SceneSurface "CatchTutorialScene" FaithfulTested [ CriticalPathJohto; RequiredFor100Percent ] "SCR-002 parser, scene, and scheduler tests cover Dude's automated Route 29 Poke Ball demonstration and verify that it resumes without mutating the real player."
          yield entry SceneSurface "BattleScene" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "BUX tests cover trainer/wild battle context, FIGHT/PKMN/PACK/RUN command menu, submenu back-out, party switching, party-targeted items, trainer ball/RUN rejection, and Cherrygrove rival entry through the real runtime script path; forced-switch prompt timing and full trainer battle fidelity remain future battle-shell work."
          yield entry SceneSurface "TitleScene" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "D Title-to-continue runtime test drives Start through the real title/menu input path; exact title animation timing remains future UI polish."
          yield entry SceneSurface "MainMenuScene" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "D Title-to-continue runtime test covers save-present CONTINUE ordering from engine/menus/main_menu.asm and verifies Continue then Save preserves the exact persistent JSON state."
          yield entry SceneSurface "WeekdayScene" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "WeekdaySceneTests cover selection, confirmation, cancellation, and wrapping."
          yield entry SceneSurface "CreditsScene" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "D Credits scene parses data/credits_script.asm and data/credits_strings.asm, renders the sequenced pages, and is reached from Hall of Fame and credits scripts."
          yield entry SceneSurface "MomBankScene" ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "CriticalSpecialSceneTests and OverworldSchedulerTests cover core Mom savings flows; exact digit/menu fidelity remains future UI work."
          yield entry SceneSurface "ScriptMenuScene" ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "Generic ROM script-menu UI is covered by CriticalSpecialSceneTests and ScriptTests; exact per-menu labels remain future menu-header decoding work."
          yield entry SceneSurface "ApricornSelectionScene" FaithfulTested [ SideSystem; RequiredFor100Percent ] "B1 scene test covers disassembly-order picker entries, selection, and cancel."
          yield entry SceneSurface "FlyDestinationScene" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "OVR-009 scheduler coverage drives the real Start/Party/FLY flow through discovered source destinations, cancellation, a source spawn warp, and save restoration; the original town-map art and Fly animation remain presentation work."
          yield entry SceneSurface "DayCareScene" ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "B2 scene tests cover deposit, withdraw, and egg pickup flows; exact text/level-growth fidelity remains future daycare polish."
          yield entry SceneSurface "MoveDeletionScene" FaithfulTested [ SideSystem; RequiredFor100Percent ] "B3 scene test covers party-pick, move-pick, confirmation, and compaction after deletion."
          yield entry SceneSurface "LearnMoveScene" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "BAT-012 tests cover explicit slot replacement, two-stage decline confirmation, HM rejection, cancellation, and one-shot decisions."
          yield entry SceneSurface "EvolutionScene" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "BAT-014 tests cover explicit evolution acceptance/cancellation and one-shot decisions; persistent mutation remains deferred until acceptance."
          yield entry SceneSurface "PokedexScene" ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "D Pokédex tests cover Start-driven type search, caught-only type filtering from data/types/search_types.asm, AREA wild-encounter nests via FindNest data, and front/question-mark dex pic rendering; exact Pokégear town-map nest icons remain future UI polish."
          yield entry SceneSurface "PokePicWaitScene" ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "SCR-003 runtime coverage opens Elm's Cyndaquil front picture through the real script, asserts visible source-menu-region pixels, dismisses with A, and resumes TakeCyndaquilText; exact original tilemap/SGB restoration remains presentation work."
          yield entry SceneSurface "NpcTradeScene" ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "SCR-003 supports source-record NPC trade confirmation, party selection, requested species/gender validation, one-time completion, recipient metadata, and Pokédex registration. Exact GBC text and cable animation remain presentation work."
          yield entry SceneSurface "ElevatorScene" ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "SCR-003 selector presents generated source floors, returns cancel/current-floor false, and routes Goldenrod/Celadon selections to source destination warps after the surrounding script finishes. Exact scrolling-menu art and backup-warp memory remain presentation/runtime approximations."

          yield! many SceneSurface ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "Scene exists and can be driven, but full GUI e2e conformance is future UI-epic work for side systems."
            [ "PCBoxScene"; "PcMenuScene"; "PlayerPCScene"; "SummaryScene" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C1 BattleTests audit the disassembly command families and worked battle outcomes for the first high-traffic effect batch, including Thunder weather accuracy and priority/recharge gates."
            [ "EFFECT_NORMAL_HIT"; "EFFECT_ACCURACY_DOWN_HIT"; "EFFECT_CONFUSE_HIT"; "EFFECT_THUNDER"; "EFFECT_SLEEP"
              "EFFECT_PARALYZE"; "EFFECT_BURN_HIT"; "EFFECT_ATTACK_DOWN_HIT"; "EFFECT_HYPER_BEAM"; "EFFECT_PRIORITY_HIT" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C2 BattleTests audit the next high-traffic secondary-hit, trap, drain, and recoil effects against data/moves/effects.asm and effect_commands.asm HP/status side effects."
            [ "EFFECT_PARALYZE_HIT"; "EFFECT_FREEZE_HIT"; "EFFECT_FLINCH_HIT"; "EFFECT_POISON_HIT"; "EFFECT_DEFENSE_DOWN_HIT"
              "EFFECT_SPEED_DOWN_HIT"; "EFFECT_SP_ATK_DOWN_HIT"; "EFFECT_SP_DEF_DOWN_HIT"; "EFFECT_TRAP_TARGET"; "EFFECT_LEECH_HIT"; "EFFECT_RECOIL_HIT" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C3 BattleTests audit the non-damaging stat-stage command families against data/moves/effects.asm ordering, including single- and double-stage user raises and target drops."
            [ "EFFECT_ATTACK_UP"; "EFFECT_DEFENSE_UP"; "EFFECT_SP_ATK_UP"; "EFFECT_EVASION_UP"; "EFFECT_ATTACK_UP_2"
              "EFFECT_DEFENSE_UP_2"; "EFFECT_SPEED_UP_2"; "EFFECT_SP_DEF_UP_2"; "EFFECT_ATTACK_DOWN"; "EFFECT_DEFENSE_DOWN"
              "EFFECT_SPEED_DOWN"; "EFFECT_ACCURACY_DOWN"; "EFFECT_EVASION_DOWN"; "EFFECT_ATTACK_DOWN_2"; "EFFECT_DEFENSE_DOWN_2"; "EFFECT_SPEED_DOWN_2" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C4 BattleTests audit non-damaging poison/confusion/toxic, healing, screen/protection, and weather command families against data/moves/effects.asm and the small move_effects command files."
            [ "EFFECT_POISON"; "EFFECT_CONFUSE"; "EFFECT_TOXIC"; "EFFECT_HEAL"; "EFFECT_REFLECT"; "EFFECT_LIGHT_SCREEN"
              "EFFECT_MIST"; "EFFECT_SAFEGUARD"; "EFFECT_RAIN_DANCE"; "EFFECT_SUNNY_DAY"; "EFFECT_SANDSTORM" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C5 BattleTests audit common utility and fixed-damage effects, including always-hit accuracy bypasses, fixed HP damage, False Swipe capping, Return/Frustration zero-power bug behavior, Focus Energy, Substitute, and Leech Seed."
            [ "EFFECT_ALWAYS_HIT"; "EFFECT_STATIC_DAMAGE"; "EFFECT_LEVEL_DAMAGE"; "EFFECT_SUPER_FANG"; "EFFECT_FALSE_SWIPE"
              "EFFECT_RETURN"; "EFFECT_FRUSTRATION"; "EFFECT_FOCUS_ENERGY"; "EFFECT_SUBSTITUTE"; "EFFECT_LEECH_SEED" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C6 BattleTests audit random fixed-damage and multi-hit effects against data/moves/effects.asm plus present, magnitude, and triple-kick helper command files."
            [ "EFFECT_PSYWAVE"; "EFFECT_REVERSAL"; "EFFECT_PRESENT"; "EFFECT_MAGNITUDE"; "EFFECT_TRIPLE_KICK"
              "EFFECT_MULTI_HIT"; "EFFECT_DOUBLE_HIT"; "EFFECT_POISON_MULTI_HIT" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C7 BattleTests audit Jump Kick crash damage, Pay Day coin messaging, and Rapid Spin user-side hazard clearing against data/moves/effects.asm and the corresponding helper commands."
            [ "EFFECT_JUMP_KICK"; "EFFECT_PAY_DAY"; "EFFECT_RAPID_SPIN" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C8 BattleTests audit volatile control effects represented by the current battle state, including Attract, Mean Look, Curse's user-type split, and single-layer Spikes behavior."
            [ "EFFECT_ATTRACT"; "EFFECT_MEAN_LOOK"; "EFFECT_CURSE"; "EFFECT_SPIKES" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C9 BattleTests audit utility effects represented by the current battle state, including Belly Drum's low-HP boost bug, Psych Up failure conditions, Reset Stats preserving volatile flags, Dream Eater, Pain Split, and Splash."
            [ "EFFECT_BELLY_DRUM"; "EFFECT_PSYCH_UP"; "EFFECT_RESET_STATS"; "EFFECT_DREAM_EATER"; "EFFECT_PAIN_SPLIT"; "EFFECT_SPLASH" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C10 BattleTests audit Selfdestruct's defense-halving damage and side-effect cleanup plus Tri Attack's disassembly-order random paralysis/freeze/burn secondary."
            [ "EFFECT_SELFDESTRUCT"; "EFFECT_TRI_ATTACK" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C11 BattleTests audit stat-up-hit effects, Defense Curl's curled substatus, and Rollout's post-STAB damage doubling against data/moves/effects.asm and rollout.asm."
            [ "EFFECT_ALL_UP_HIT"; "EFFECT_ATTACK_UP_HIT"; "EFFECT_DEFENSE_UP_HIT"; "EFFECT_DEFENSE_CURL"; "EFFECT_ROLLOUT" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C12 BattleTests audit Destiny Bond's user substatus and Swagger's target Attack raise before confusion against data/moves/effects.asm."
            [ "EFFECT_DESTINY_BOND"; "EFFECT_SWAGGER" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C13 BattleTests audit Gust, Earthquake, Twister, and Stomp conditional double-damage substatus checks plus Twister/Stomp flinch secondaries."
            [ "EFFECT_EARTHQUAKE"; "EFFECT_GUST"; "EFFECT_STOMP"; "EFFECT_TWISTER" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C14 BattleTests audit Fury Cutter's post-STAB damage ramp and Snore's sleep gate plus flinch secondary against the move-effect scripts."
            [ "EFFECT_FURY_CUTTER"; "EFFECT_SNORE" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C15 BattleTests audit Hidden Power's modeled DV-zero damage, Lock-On, Foresight, Nightmare, and Perish Song against their helper scripts."
            [ "EFFECT_HIDDEN_POWER"; "EFFECT_LOCK_ON"; "EFFECT_FORESIGHT"; "EFFECT_NIGHTMARE"; "EFFECT_PERISH_SONG" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C16 BattleTests audit Flame Wheel/Sacred Fire self-thaw command order and Heal Bell's active status/Nightmare cleanup."
            [ "EFFECT_FLAME_WHEEL"; "EFFECT_SACRED_FIRE"; "EFFECT_HEAL_BELL" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C17 BattleTests audit two-turn move first-turn runtime charging and second-turn effect commands for Fly, SolarBeam, Razor Wind, Skull Bash, and Sky Attack."
            [ "EFFECT_FLY"; "EFFECT_SOLARBEAM"; "EFFECT_RAZOR_WIND"; "EFFECT_SKULL_BASH"; "EFFECT_SKY_ATTACK" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C18 BattleTests audit Morning Sun, Synthesis, and Moonlight through the shared time/weather heal helper, covering full-HP failure and modeled weather multipliers."
            [ "EFFECT_MORNING_SUN"; "EFFECT_SYNTHESIS"; "EFFECT_MOONLIGHT" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C19 BattleTests audit Rage's dedicated damage counter and opponent-hit counter build behavior against BattleCommand_RageDamage and BuildOpponentRage."
            [ "EFFECT_RAGE" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C20 BattleTests audit Protect and Endure through the shared ProtectChance helper, covering substitute failure, consecutive-use chance reset, opponent-went-first failure, damage blocking, and Endure's 1 HP clamp."
            [ "EFFECT_PROTECT"; "EFFECT_ENDURE" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C21 BattleTests audit Beat Up's base-stat damage formula and healthy, status-free party-member loop against BattleCommand_BeatUp."
            [ "EFFECT_BEAT_UP" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C22 BattleTests audit Counter's opponent-went-first, physical last-move, nonzero-damage, and doubled-damage requirements against BattleCommand_Counter."
            [ "EFFECT_COUNTER" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C23 BattleTests audit Disable's last-counter-move targeting, PP/non-active failure gates, random 2-8 turn count, and disabled-move pre-turn block against BattleCommand_Disable."
            [ "EFFECT_DISABLE" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C24 BattleTests audit Encore's previous-last-move targeting, excluded move failures, 3-6 turn count, and same-turn forced move behavior against BattleCommand_Encore."
            [ "EFFECT_ENCORE" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C25 BattleTests audit Future Sight's stored pre-variation damage, four-count setup, payoff-turn accuracy/variation, and counter cleanup against future_sight.asm and HandleFutureSight."
            [ "EFFECT_FUTURE_SIGHT" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C26 BattleTests audit Mirror Coat's opponent-went-first, special last-move, nonzero-damage, and doubled-damage requirements against BattleCommand_MirrorCoat."
            [ "EFFECT_MIRROR_COAT" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C27 BattleTests audit OHKO's lower-level failure, type-immunity failure, modified accuracy, and runtime command-owned hit logic against BattleCommand_OHKO."
            [ "EFFECT_OHKO" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C28 BattleTests audit Rampage's random lock duration, forced locked move, no-reset locked turns, and two-or-three-turn confusion expiry against BattleCommand_CheckRampage and BattleCommand_Rampage."
            [ "EFFECT_RAMPAGE" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C29 BattleTests audit Sketch's last-counter-move source, Substitute/duplicate failure gates, permanent slot replacement, base PP copy, and ClearLastMove behavior against BattleCommand_Sketch."
            [ "EFFECT_SKETCH" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C30 BattleTests audit Sleep Talk's asleep-only gate, slot resampling, disabled/two-turn exclusions, sleep decrement messaging, and called-move last-move tracking against BattleCommand_SleepTalk."
            [ "EFFECT_SLEEP_TALK" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C31 BattleTests audit Spite's last-counter-move target, Struggle/no-last/zero-PP failure gates, and random clamped 2-5 PP drain against BattleCommand_Spite."
            [ "EFFECT_SPITE" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C32 BattleTests audit Teleport's wild-battle flee outcome, player level/random escape gate, CantEscape failure, and wild-enemy always-succeeds behavior against BattleCommand_Teleport."
            [ "EFFECT_TELEPORT" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C33 BattleTests audit Thief's post-damage held-item transfer, user-already-holding gate, target-no-item gate, and mail exclusion against BattleCommand_Thief and ItemIsMail."
            [ "EFFECT_THIEF" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C34 BattleTests audit Transform's target-transformed/hidden failure gates, ClearLastMove behavior, Disable reset, stat/stage/move copy, and Sketch-vs-other copied PP initialization against BattleCommand_Transform."
            [ "EFFECT_TRANSFORM" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C35 BattleTests audit Baton Pass's healthy-bench failure gate, stat-stage preservation, passable volatile carryover, and ResetBatonPassStatus cleanup of Nightmare, Disable, Attract, Transform, Encore, last-move, and wrap state."
            [ "EFFECT_BATON_PASS" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C36 BattleTests audit Bide's 2-3 turn storage, damage accumulation, zero-damage failure text, doubled release damage, and 65535 clamp against BattleCommand_StoreEnergy and BattleCommand_UnleashEnergy."
            [ "EFFECT_BIDE" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C37 BattleTests audit Conversion's own-move type buffer, current-type and Curse exclusions, failure when no valid type exists, four-slot random sampling, and command-owned no-checkhit RNG path against BattleCommand_Conversion."
            [ "EFFECT_CONVERSION" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C38 BattleTests audit Conversion2's last-counter-move source, no-last and Curse-type failure gates, valid-type rejection sampling, resistance requirement, and checkhit-without-extra-crit-RNG path against BattleCommand_Conversion2."
            [ "EFFECT_CONVERSION2" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C39 BattleTests audit Force Switch's checkhit gate, trainer target-moved timing gate, random healthy bench selection path, and wild-battle flee outcome/level gate against BattleCommand_ForceSwitch."
            [ "EFFECT_FORCE_SWITCH" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C40 BattleTests audit Metronome's ClearLastMove behavior, numeric move-id rejection sampling, exception list, user-known move exclusion, and called-move last-move tracking against BattleCommand_Metronome."
            [ "EFFECT_METRONOME" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C41 BattleTests audit Mimic's ClearLastMove behavior, checkhit-owned RNG path, hidden-target/duplicate failure gates, last-counter-move source, and 5-PP copied slot against BattleCommand_Mimic."
            [ "EFFECT_MIMIC" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C42 BattleTests audit Mirror Move's ClearLastMove behavior, opponent last-counter-move source, user-known/no-last failure gates, called-move execution, PP consumption, and called-move last-move tracking against BattleCommand_MirrorMove."
            [ "EFFECT_MIRROR_MOVE" ]

          yield! many MoveEffect FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "C43 BattleTests audit Pursuit's switching-target damage doubling against BattleCommand_Pursuit."
            [ "EFFECT_PURSUIT" ]

          yield! many MoveEffect ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Move effect is routed through explicit battle effect commands or an intentional no-op/fallback path with unit coverage for the effect family."
            []

          yield! many MoveEffect Unknown [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Generated move effect is not yet implemented as a faithful battle command and should stay visible battle debt."
            []

          yield! many ItemFieldMenu ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "PackScene builds action menus from item field-menu metadata: PackTests cover ITEMMENU_CLOSE cant-toss key items and PackUseGiveTests cover ITEMMENU_PARTY use/give plus ITEMMENU_NOUSE deferred-use gating; exact per-key-item field effects remain future item polish."
            [ "ITEMMENU_CLOSE"; "ITEMMENU_CURRENT"; "ITEMMENU_NOUSE"; "ITEMMENU_PARTY" ]

          yield entry ItemBattleMenu "ITEMMENU_PARTY" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "BattleScene PACK command supports party-targeted HP/status item use; BattleTests cover using Potion on a benched party mon."

          yield! many ItemBattleMenu ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "BattleScene ball/item filtering covers close-after-use balls and excludes unusable battle items from the selectable battle item menu."
            [ "ITEMMENU_CLOSE"; "ITEMMENU_NOUSE" ]

          yield! many ItemHeldEffect ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "BattleTests cover party-held item propagation and end-of-turn held healing behavior."
            [ "HELD_LEFTOVERS"; "HELD_BERRY" ]

          yield entry ItemHeldEffect "HELD_AMULET_COIN" FaithfulTested [ CriticalPathJohto; RequiredFor100Percent ] "BAT-011 runtime settlement and BattleTests prove the sticky source condition: doubling activates only after a holder is sent out and applies to trainer and Pay Day winnings exactly once."

          yield! many ItemHeldEffect ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "BattleTests cover special held-item battle mechanics for priority and lethal-hit survival."
            [ "HELD_QUICK_CLAW"; "HELD_FOCUS_BAND" ]

          yield! many ItemHeldEffect ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "BattleTests cover type-boosting held items increasing matching move damage."
            [ "HELD_BUG_BOOST"; "HELD_DARK_BOOST"; "HELD_DRAGON_BOOST"; "HELD_ELECTRIC_BOOST"; "HELD_FIGHTING_BOOST"; "HELD_FIRE_BOOST"
              "HELD_FLYING_BOOST"; "HELD_GHOST_BOOST"; "HELD_GRASS_BOOST"; "HELD_GROUND_BOOST"; "HELD_ICE_BOOST"; "HELD_NORMAL_BOOST"
              "HELD_POISON_BOOST"; "HELD_PSYCHIC_BOOST"; "HELD_ROCK_BOOST"; "HELD_STEEL_BOOST"; "HELD_WATER_BOOST" ]

          yield! many ItemHeldEffect ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "BattleTests cover status/confusion cure berries in the residual held-item slot."
            [ "HELD_HEAL_BURN"; "HELD_HEAL_CONFUSION"; "HELD_HEAL_FREEZE"; "HELD_HEAL_PARALYZE"; "HELD_HEAL_POISON"; "HELD_HEAL_SLEEP"; "HELD_HEAL_STATUS" ]

          yield! many ItemHeldEffect ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "BattleTests cover remaining special held-item mechanics: accuracy reduction, crit boost, flinch, PP restore, Smoke Ball escape, and Ditto Metal Powder defenses."
            [ "HELD_BRIGHTPOWDER"; "HELD_CRITICAL_UP"; "HELD_ESCAPE"; "HELD_FLINCH"; "HELD_METAL_POWDER"; "HELD_RESTORE_PP" ]

          yield entry ItemHeldEffect "HELD_CLEANSE_TAG" ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "WildEncounterTests cover Cleanse Tag reducing encounter rate when held by the lead party mon."

          yield entry ItemHeldEffect "HELD_NONE" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "HELD_NONE intentionally has no battle effect."

          yield entry FieldMove "WHIRLPOOL" FaithfulTested [ CriticalPathJohto; RequiredFor100Percent ] "OVR-007 tests prove source block $07 becomes passable water block $36 in Dragons Den B1F and Route 41, surf state persists, traversal succeeds on both maps, and missing Glacier Badge or party move leaves the obstruction intact and non-traversable."

          yield entry FieldMove "CUT" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "OVR-004 tests preserve every source CutTreeBlockPointers entry and prove ordinary A-press Cut in Ilex Forest and Route 2 replaces the correct forest/Kanto block, opens traversal, resets on save/reload, and cannot mutate without the Hive Badge or a party user."

          yield entry FieldMove "SURF" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "OVR-005 runtime tests prove badge/move/facing-water entry, normal and Pikachu surfing sprites, invalid-terrain rejection, water traversal, legal shore dismount, and save restoration to a legal surfing state."

          yield entry FieldMove "STRENGTH" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "OVR-006 tests prove badge/move activation requirements, one-cell boulder pushing, every generated source stonetable pairing, Ice Path lower-floor event transfer, and Blackthorn Gym non-Ice pit behavior through the source fallout scripts."

          yield entry FieldMove "WATERFALL" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "OVR-008 runtime test proves source upward-facing activation, forced animated ascent and source current descent through the real Tohjo Falls waterfall column, stable water landings, and retained surfing state."

          yield entry FieldMove "FLY" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "OVR-009 scheduler test proves the generated 24-entry flypoint table, source outdoor gating, discovered-destination filtering through the real Party menu, cancellation, source-spawn arrival, and save restoration. The original town-map art and Fly animation remain presentation work."

          yield entry FieldMove "FLASH" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "OVR-010 tests prove source PALETTE_DARK eligibility, visible Dark Cave illumination from the real Party menu, persistence through cave and save transitions, and source town/route reset. The original white fade, SFX timing, and exact palette curve remain presentation approximations."

          yield entry FieldMove "HEADBUTT" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "OVR-011 tests prove real Route 39 tree A-press and Party-menu dispatch, generated Gold treemon map/table data, source score/chance/weighted selection, a normal catchable wild battle, and the no-encounter text branch. The original confirmation prompt, tree-shake animation/SFX, and randomly assigned player ID remain approximated; the port reads a persisted __trainer_id seam that defaults to zero."

          yield! many HostEffectCase ImplementedApproximate [ Cosmetic; RequiredFor100Percent ] "Host effect is explicitly interpreted by the scene shell."
            [ "PlayMusic"; "StopMusic"; "PlaySfx"; "PlayJingle" ] ]

    let withTag tag =
        all |> List.filter (fun entry -> entry.Tags.Contains tag)

    let debtWithTag tag =
        withTag tag
        |> List.filter (fun entry -> entry.Status = StubNoOp || entry.Status = Unknown)

module private Inventory =
    let private generatedCommands =
        seq {
            for KeyValue(_, map) in MapsData.all do
                yield! map.Script.Commands
            yield! StdScriptsData.program.Commands
        }
        |> Seq.toArray

    let private unionCaseName (value: obj) =
        let case, _ = FSharpValue.GetUnionFields(value, value.GetType())
        case.Name

    let private unionCases<'T> =
        FSharpType.GetUnionCases(typeof<'T>)
        |> Array.map (fun c -> c.Name)
        |> Set.ofArray

    let scriptCommandCases = unionCases<ScriptCommand>

    let generatedScriptCommandCases =
        generatedCommands
        |> Seq.map (fun c -> unionCaseName (box c))
        |> Set.ofSeq

    let generatedSpecials =
        generatedCommands
        |> Seq.choose (function | Special name -> Some name | _ -> None)
        |> Set.ofSeq

    let objectTypes =
        MapsData.all
        |> Seq.collect (fun (KeyValue(_, map)) -> map.Events.Objects |> Seq.map (fun o -> o.Type))
        |> Set.ofSeq

    let bgEventKinds =
        MapsData.all
        |> Seq.collect (fun (KeyValue(_, map)) -> map.Events.Bgs |> Seq.map (fun bg -> bg.Kind))
        |> Set.ofSeq

    let callbackKinds =
        MapsData.all
        |> Seq.collect (fun (KeyValue(_, map)) -> map.Events.Callbacks |> Seq.map (fun cb -> cb.Kind))
        |> Set.ofSeq

    let sceneSurfaces =
        typeof<Scene>.Assembly.GetTypes()
        |> Array.filter (fun t -> typeof<Scene>.IsAssignableFrom(t) && not t.IsInterface && not t.IsAbstract)
        |> Array.map (fun t -> t.Name)
        |> Set.ofArray

    let moveEffects =
        Moves.all
        |> Seq.map (fun (KeyValue(_, move)) -> move.Effect)
        |> Set.ofSeq

    let itemHeldEffects =
        Items.all
        |> Seq.map (fun item -> item.HeldEffect)
        |> Seq.filter (String.IsNullOrWhiteSpace >> not)
        |> Set.ofSeq

    let itemFieldMenus =
        Items.all
        |> Seq.map (fun item -> item.FieldMenu)
        |> Seq.filter (String.IsNullOrWhiteSpace >> not)
        |> Set.ofSeq

    let itemBattleMenus =
        Items.all
        |> Seq.map (fun item -> item.BattleMenu)
        |> Seq.filter (String.IsNullOrWhiteSpace >> not)
        |> Set.ofSeq

    let fieldMoves =
        FieldMoves.hmMoves
        |> List.map fst
        |> Set.ofList

    let hostEffects = unionCases<HostEffect>

module private AssertLedger =
    let entriesFor category =
        ConformanceLedger.all
        |> List.filter (fun e -> e.Category = category)
        |> List.map (fun e -> e.Name)
        |> Set.ofList

    let covers category inventory =
        let missing = Set.difference inventory (entriesFor category)
        let missingText = String.concat ", " missing
        Assert.True(Set.isEmpty missing, $"Missing {category} ledger entries: {missingText}")

    let hasNoUnknownReferences category inventory =
        let unknown =
            entriesFor category
            |> Set.difference inventory
        let unknownText = String.concat ", " unknown
        Assert.True(Set.isEmpty unknown, $"Unknown {category} ledger entries: {unknownText}")

[<Fact>]
let ``ledger entries are unique`` () =
    let duplicates =
        ConformanceLedger.all
        |> List.countBy (fun e -> e.Category, e.Name)
        |> List.filter (fun (_, count) -> count > 1)
        |> List.map (fun ((category, name), count) -> $"{category}:{name} x{count}")

    let duplicateText = String.concat ", " duplicates
    Assert.True(List.isEmpty duplicates, $"Duplicate ledger entries: {duplicateText}")

[<Fact>]
let ``ledger entries carry burn-down metadata`` () =
    let missing =
        ConformanceLedger.all
        |> List.filter (fun entry -> Set.isEmpty entry.Tags)
        |> List.map (fun entry -> $"{entry.Category}:{entry.Name}")

    let missingText = String.concat ", " missing
    Assert.True(List.isEmpty missing, $"Ledger entries missing metadata tags: {missingText}")

[<Fact>]
let ``critical Johto debt is queryable`` () =
    let debt =
        ConformanceLedger.debtWithTag CriticalPathJohto
        |> List.map (fun entry -> entry.Category, entry.Name)
        |> Set.ofList

    Assert.DoesNotContain((ScriptCommandCase, "Catchtutorial"), debt)
    Assert.Contains((ScriptSpecial, "HealMachineAnim"), debt)
    Assert.DoesNotContain((ScriptSpecial, "TryQuickSave"), debt)

[<Fact>]
let ``link-only debt is separated from normal story debt`` () =
    let debt =
        ConformanceLedger.debtWithTag LinkOnly
        |> List.map (fun entry -> entry.Category, entry.Name)
        |> Set.ofList

    Assert.Contains((ScriptSpecial, "TimeCapsule"), debt)
    Assert.Contains((ScriptSpecial, "TryQuickSave"), debt)
    Assert.DoesNotContain((ScriptSpecial, "BankOfMom"), debt)

[<Fact>]
let ``script command cases are classified`` () =
    AssertLedger.covers ScriptCommandCase Inventory.scriptCommandCases
    AssertLedger.hasNoUnknownReferences ScriptCommandCase Inventory.scriptCommandCases

[<Fact>]
let ``generated script command cases are classified`` () =
    AssertLedger.covers ScriptCommandCase Inventory.generatedScriptCommandCases

[<Fact>]
let ``generated script specials are classified`` () =
    AssertLedger.covers ScriptSpecial Inventory.generatedSpecials
    AssertLedger.hasNoUnknownReferences ScriptSpecial Inventory.generatedSpecials

[<Fact>]
let ``generated map event kinds are classified`` () =
    AssertLedger.covers ObjectType Inventory.objectTypes
    AssertLedger.covers BgEventKind Inventory.bgEventKinds
    AssertLedger.covers MapCallbackKind Inventory.callbackKinds
    AssertLedger.hasNoUnknownReferences ObjectType Inventory.objectTypes
    AssertLedger.hasNoUnknownReferences BgEventKind Inventory.bgEventKinds
    AssertLedger.hasNoUnknownReferences MapCallbackKind Inventory.callbackKinds

[<Fact>]
let ``scene surfaces are classified`` () =
    AssertLedger.covers SceneSurface Inventory.sceneSurfaces
    AssertLedger.hasNoUnknownReferences SceneSurface Inventory.sceneSurfaces

[<Fact>]
let ``data-driven battle and item surfaces are classified`` () =
    AssertLedger.covers MoveEffect Inventory.moveEffects
    AssertLedger.covers ItemHeldEffect Inventory.itemHeldEffects
    AssertLedger.covers ItemFieldMenu Inventory.itemFieldMenus
    AssertLedger.covers ItemBattleMenu Inventory.itemBattleMenus
    AssertLedger.hasNoUnknownReferences MoveEffect Inventory.moveEffects
    AssertLedger.hasNoUnknownReferences ItemHeldEffect Inventory.itemHeldEffects
    AssertLedger.hasNoUnknownReferences ItemFieldMenu Inventory.itemFieldMenus
    AssertLedger.hasNoUnknownReferences ItemBattleMenu Inventory.itemBattleMenus

[<Fact>]
let ``field and host effects are classified`` () =
    AssertLedger.covers FieldMove Inventory.fieldMoves
    AssertLedger.covers HostEffectCase Inventory.hostEffects
    AssertLedger.hasNoUnknownReferences FieldMove Inventory.fieldMoves
    AssertLedger.hasNoUnknownReferences HostEffectCase Inventory.hostEffects

[<Fact>]
let ``ledger keeps unresolved debt visible`` () =
    let statuses =
        ConformanceLedger.all
        |> List.map (fun e -> e.Status)
        |> Set.ofList

    Assert.DoesNotContain(Unknown, statuses)
    Assert.Contains(StubNoOp, statuses)
