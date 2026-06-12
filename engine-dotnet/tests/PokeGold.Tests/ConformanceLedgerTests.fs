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
              "Loadwildmon"; "Givepoke"; "Checkpoke"; "Loadtrainer"; "Startbattle"; "Reloadmapafterbattle"; "Winlosstext"; "Setlasttalked"; "Giveegg"
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

          yield! many ScriptCommandCase StubNoOp [ CriticalPathJohto; RequiredFor100Percent ] "Generated command is typed, but current runtime behavior is a no-op, fixed dummy result, or intentionally ad-hoc fallback on normal story/menu paths."
            [ "Prompt"; "Catchtutorial"; "TextRam" ]

          yield! many ScriptCommandCase StubNoOp [ SideSystem; RequiredFor100Percent ] "Generated command is typed, but current runtime behavior is a no-op, fixed dummy result, or intentionally ad-hoc fallback for side systems."
            [ "Pokepic"; "Closepokepic"; "Itemnotify"; "Elevator"; "Trade"; "Givepokemail"; "Checkpokemail"; "Writeobjectxy"; "Ugdoor"; "Warpcheck"
              "Checkjustbattled"
              "ConditionalEvent"; "Describedecoration"; "Stonetable"; "Cmdqueue"; "Writecmdqueue"; "Elevfloor" ]

          yield! many ScriptCommandCase StubNoOp [ Cosmetic; RequiredFor100Percent ] "Generated command is typed, but only cosmetic/timing behavior remains."
            [ "Cry"; "Waitsfx" ]

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

          yield! many ScriptSpecial StubNoOp [ CriticalPathJohto; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback and is skipped on visible story/runtime paths."
            [ "HealMachineAnim"; "TryQuickSave" ]

          yield! many ScriptSpecial ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "Routes the setval-staged species through the CheckPoke party effect; the YourTrainerID OT check is unmodelled (all party mons are the player's)."
            [ "FindPartyMonThatSpecies"; "FindPartyMonThatSpeciesYourTrainerID" ]

          yield! many ScriptSpecial StubNoOp [ SideSystem; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback and is skipped for side systems or completion content."
            [ "CheckForLuckyNumberWinners"
              "CheckLuckyNumberShowFlag"; "CheckMysteryGift"; "CheckPokerus"
              "DaisysGrooming"
              "GetFirstPokemonHappiness"; "GetMysteryGiftItem"; "GiveShuckle"
              "MrChrono"; "PhotoStudio"
              "PrintTodaysLuckyNumber"; "ProfOaksPCBoot"; "ResetLuckyNumberShowFlag"; "ReturnShuckie"
              "ToggleDecorationsVisibility"; "ToggleMaptileDecorations"; "TrainerHouse"; "UnlockMysteryGift" ]

          yield entry ScriptSpecial "SnorlaxAwake" FaithfulTested [ CriticalPathKanto; RequiredFor100Percent ] "A18 runtime test tunes Poké Flute through the Pokégear radio UI, then verifies Vermilion Snorlax wakes, battles, and disappears."

          yield! many ScriptSpecial StubNoOp [ Cosmetic; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback and is skipped for cosmetic/host presentation behavior."
            [ "ClearBGPalettes"; "Diploma"; "FadeInFromBlack"; "FadeInFromWhite"; "FadeOutToBlack"; "FadeOutToWhite"; "LoadUsedSpritesGFX"
              "PlayCurMonCry"; "PlaySlowCry"; "PrintDiploma"; "ReloadSpritesNoPalettes"; "UpdateSprites" ]

          yield! many ScriptSpecial StubNoOp [ LinkOnly; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback and is skipped for link-only systems."
            [ "CableClubCheckWhichChris"; "CheckBothSelectedSameRoom"; "CheckLinkTimeout_Receptionist"; "CheckTimeCapsuleCompatibility"; "CloseLink"
              "Colosseum"; "DisplayLinkRecord"; "EnterTimeCapsule"; "FailedLinkToPast"; "GameboyCheck"; "SetBitsForBattleRequest"
              "SetBitsForLinkTradeRequest"; "SetBitsForTimeCapsuleRequest"; "TimeCapsule"; "TradeCenter"; "WaitForLinkedFriend"; "WaitForOtherPlayerToExit" ]

          yield! many ObjectType ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "A-press dispatch exists, but type-specific GSC object dispatch still needs conformance work."
            [ "OBJECTTYPE_SCRIPT"; "OBJECTTYPE_TRAINER"; "OBJECTTYPE_ITEMBALL" ]

          yield! many BgEventKind ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Generated BG event kind is known; dispatch semantics need per-kind tests."
            [ "BGEVENT_READ"; "BGEVENT_IFSET"; "BGEVENT_IFNOTSET"; "BGEVENT_UP"; "BGEVENT_LEFT"; "BGEVENT_RIGHT" ]

          yield entry BgEventKind "BGEVENT_ITEM" FaithfulTested [ CriticalPathKanto; RequiredFor100Percent ] "A17 Cerulean Gym hidden Machine Part runtime test covers A-press BG item dispatch, event gating, item grant, and return-to-manager consumption."

          yield! many MapCallbackKind ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Map-entry callbacks run through the scheduler, but callback-kind ordering still needs conformance tests."
            [ "MAPCALLBACK_NEWMAP"; "MAPCALLBACK_TILES"; "MAPCALLBACK_OBJECTS"; "MAPCALLBACK_CMDQUEUE" ]

          yield! many SceneSurface ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Scene exists and can be driven, but full GUI e2e conformance is future UI-epic work."
            [ "BattleScene"; "MainMenuScene"; "MartScene"; "NamingScene"; "OptionsScene"; "OverworldScene"; "PackScene"; "PartyScene"
              "PokegearScene"; "SaveMenuScene"; "StartMenuScene"; "TextBoxScene"; "TitleScene"; "YesNoScene" ]

          yield entry SceneSurface "WeekdayScene" FaithfulTested [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "WeekdaySceneTests cover selection, confirmation, cancellation, and wrapping."
          yield entry SceneSurface "MomBankScene" ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "CriticalSpecialSceneTests and OverworldSchedulerTests cover core Mom savings flows; exact digit/menu fidelity remains future UI work."
          yield entry SceneSurface "ScriptMenuScene" ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "Generic ROM script-menu UI is covered by CriticalSpecialSceneTests and ScriptTests; exact per-menu labels remain future menu-header decoding work."
          yield entry SceneSurface "ApricornSelectionScene" FaithfulTested [ SideSystem; RequiredFor100Percent ] "B1 scene test covers disassembly-order picker entries, selection, and cancel."
          yield entry SceneSurface "DayCareScene" ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "B2 scene tests cover deposit, withdraw, and egg pickup flows; exact text/level-growth fidelity remains future daycare polish."
          yield entry SceneSurface "MoveDeletionScene" FaithfulTested [ SideSystem; RequiredFor100Percent ] "B3 scene test covers party-pick, move-pick, confirmation, and compaction after deletion."

          yield! many SceneSurface ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "Scene exists and can be driven, but full GUI e2e conformance is future UI-epic work for side systems."
            [ "PCBoxScene"; "PcMenuScene"; "PlayerPCScene"; "PokedexScene"; "SummaryScene" ]

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

          yield! many MoveEffect ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Move effect is routed through explicit battle effect commands or an intentional no-op/fallback path with unit coverage for the effect family."
            [ "EFFECT_BEAT_UP"; "EFFECT_COUNTER"
              "EFFECT_DISABLE"
              "EFFECT_ENCORE"; "EFFECT_ENDURE"
              "EFFECT_FUTURE_SIGHT"
              "EFFECT_MIRROR_COAT"
              "EFFECT_OHKO"
              "EFFECT_PROTECT"
              "EFFECT_RAMPAGE"
              "EFFECT_SKETCH"
              "EFFECT_SLEEP_TALK"; "EFFECT_SPITE"
              "EFFECT_TELEPORT"; "EFFECT_THIEF"
              "EFFECT_TRANSFORM"
              "EFFECT_BATON_PASS"; "EFFECT_BIDE"; "EFFECT_CONVERSION"; "EFFECT_CONVERSION2"; "EFFECT_FORCE_SWITCH"; "EFFECT_METRONOME"
              "EFFECT_MIMIC"; "EFFECT_MIRROR_MOVE"; "EFFECT_PURSUIT" ]

          yield! many MoveEffect Unknown [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Generated move effect is not yet implemented as a faithful battle command and should stay visible battle debt."
            []

          yield! many ItemFieldMenu Unknown [ CriticalPathJohto; RequiredFor100Percent ] "Item field-menu behavior exists in data; conformance status has not been audited in the ledger yet."
            [ "ITEMMENU_CLOSE"; "ITEMMENU_CURRENT"; "ITEMMENU_NOUSE"; "ITEMMENU_PARTY" ]

          yield entry ItemBattleMenu "ITEMMENU_PARTY" ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "BattleScene item menu supports active-mon HP/status item use and runtime tests cover battle item use."

          yield! many ItemBattleMenu ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "BattleScene ball/item filtering covers close-after-use balls and excludes unusable battle items from the selectable battle item menu."
            [ "ITEMMENU_CLOSE"; "ITEMMENU_NOUSE" ]

          yield! many ItemHeldEffect ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "BattleTests cover party-held item propagation and end-of-turn held healing behavior."
            [ "HELD_LEFTOVERS"; "HELD_BERRY" ]

          yield entry ItemHeldEffect "HELD_AMULET_COIN" ImplementedApproximate [ CriticalPathJohto; RequiredFor100Percent ] "Battle reward helper and trainer reward path double prize money when a party mon holds AMULET_COIN."

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

          yield! many FieldMove ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "HM field move has badge/move/terrain blockers, party-menu dispatch, and runtime coverage; exact map mutations/warps remain future fidelity work."
            [ "CUT"; "SURF"; "STRENGTH"; "FLY"; "FLASH"; "WHIRLPOOL"; "WATERFALL" ]

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

    Assert.Contains((ScriptCommandCase, "TextRam"), debt)
    Assert.Contains((ScriptSpecial, "HealMachineAnim"), debt)
    Assert.Contains((ScriptSpecial, "TryQuickSave"), debt)

[<Fact>]
let ``link-only debt is separated from normal story debt`` () =
    let debt =
        ConformanceLedger.debtWithTag LinkOnly
        |> List.map (fun entry -> entry.Category, entry.Name)
        |> Set.ofList

    Assert.Contains((ScriptSpecial, "TimeCapsule"), debt)
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

    Assert.Contains(Unknown, statuses)
    Assert.Contains(StubNoOp, statuses)
