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

          yield! many ScriptCommandCase StubNoOp [ CriticalPathJohto; RequiredFor100Percent ] "Generated command is typed, but current runtime behavior is a no-op, fixed dummy result, or intentionally ad-hoc fallback on normal story/menu paths."
            [ "Loadmenu"; "Verticalmenu"; "Closewindow"; "TwoDMenu"; "Prompt"; "Catchtutorial"; "TextRam"; "MenuCoords" ]

          yield! many ScriptCommandCase StubNoOp [ SideSystem; RequiredFor100Percent ] "Generated command is typed, but current runtime behavior is a no-op, fixed dummy result, or intentionally ad-hoc fallback for side systems."
            [ "Pokepic"; "Closepokepic"; "Itemnotify"; "Elevator"; "Trade"; "Givepokemail"; "Checkpokemail"; "Writeobjectxy"; "Ugdoor"; "Warpcheck"
              "Addcellnum"; "Checkcellnum"; "Checkphonecall"; "Checkjustbattled"; "Askforphonenumber"
              "ConditionalEvent"; "Describedecoration"; "Stonetable"; "Cmdqueue"; "Writecmdqueue"; "Specialphonecall"; "Elevfloor" ]

          yield! many ScriptCommandCase StubNoOp [ Cosmetic; RequiredFor100Percent ] "Generated command is typed, but only cosmetic/timing behavior remains."
            [ "Cry"; "Waitsfx" ]

          yield! many ScriptCommandCase StubNoOp [ UnknownReachability ] "Generated command is typed, but reachability and required behavior have not been triaged."
            [ "Checkver"; "Unsupported" ]

          yield! many ScriptSpecial ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Explicitly handled by the script integration layer."
            [ "HealParty"; "PokemonCenterPC"; "RestartMapMusic"; "PlayMapMusic"; "FadeOutMusic" ]

          yield! many ScriptSpecial StubNoOp [ CriticalPathJohto; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback and is skipped on visible story/runtime paths."
            [ "BankOfMom"; "DisplayMoneyAndCoinBalance"; "FindPartyMonThatSpecies"; "FindPartyMonThatSpeciesYourTrainerID"
              "HealMachineAnim"; "InitialClearDSTFlag"; "InitialSetDSTFlag"; "MapRadio"; "NameRival"; "OverworldTownMap"; "PlayersHousePC"
              "SetDayOfWeek"; "TryQuickSave" ]

          yield! many ScriptSpecial StubNoOp [ SideSystem; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback and is skipped for side systems or completion content."
            [ "BillsGrandfather"; "BugContestJudging"; "CardFlip"; "CheckFirstMonIsEgg"; "CheckForLuckyNumberWinners"
              "CheckLuckyNumberShowFlag"; "CheckMagikarpLength"; "CheckMysteryGift"; "CheckPartyFullAfterContest"; "CheckPokerus"
              "ContestDropOffMons"; "ContestReturnMons"; "DaisysGrooming"; "DayCareLady"; "DayCareMan"; "DayCareManOutside"; "DayCareMon1"; "DayCareMon2"
              "DisplayCoinCaseBalance"; "GameCornerPrizeMonCheckDex"; "GetFirstPokemonHappiness"; "GetMysteryGiftItem"; "GiveParkBalls"; "GiveShuckle"
              "InitRoamMons"; "MagikarpHouseSign"; "MagnetTrain"; "MoveDeletion"; "MrChrono"; "NameRater"; "OlderHaircutBrother"; "PhotoStudio"; "PlaceMoneyTopRight"
              "PrintTodaysLuckyNumber"; "ProfOaksPCBoot"; "ResetLuckyNumberShowFlag"; "ReturnShuckie"; "SelectApricornForKurt"; "SelectRandomBugContestContestants"
              "SlotMachine"; "SnorlaxAwake"; "ToggleDecorationsVisibility"; "ToggleMaptileDecorations"; "TrainerHouse"; "UnlockMysteryGift"; "YoungerHaircutBrother" ]

          yield! many ScriptSpecial StubNoOp [ Cosmetic; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback and is skipped for cosmetic/host presentation behavior."
            [ "ClearBGPalettes"; "Diploma"; "FadeInFromBlack"; "FadeInFromWhite"; "FadeOutToBlack"; "FadeOutToWhite"; "LoadUsedSpritesGFX"
              "PlayCurMonCry"; "PlaySlowCry"; "PrintDiploma"; "ReloadSpritesNoPalettes"; "UnownPrinter"; "UnownPuzzle"; "UpdateSprites" ]

          yield! many ScriptSpecial StubNoOp [ LinkOnly; RequiredFor100Percent ] "Generated special currently reaches the generic Special fallback and is skipped for link-only systems."
            [ "CableClubCheckWhichChris"; "CheckBothSelectedSameRoom"; "CheckLinkTimeout_Receptionist"; "CheckTimeCapsuleCompatibility"; "CloseLink"
              "Colosseum"; "DisplayLinkRecord"; "EnterTimeCapsule"; "FailedLinkToPast"; "GameboyCheck"; "SetBitsForBattleRequest"
              "SetBitsForLinkTradeRequest"; "SetBitsForTimeCapsuleRequest"; "TimeCapsule"; "TradeCenter"; "WaitForLinkedFriend"; "WaitForOtherPlayerToExit" ]

          yield! many ObjectType ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "A-press dispatch exists, but type-specific GSC object dispatch still needs conformance work."
            [ "OBJECTTYPE_SCRIPT"; "OBJECTTYPE_TRAINER"; "OBJECTTYPE_ITEMBALL" ]

          yield! many BgEventKind ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Generated BG event kind is known; dispatch semantics need per-kind tests."
            [ "BGEVENT_READ"; "BGEVENT_IFSET"; "BGEVENT_IFNOTSET"; "BGEVENT_ITEM"; "BGEVENT_UP"; "BGEVENT_LEFT"; "BGEVENT_RIGHT" ]

          yield! many MapCallbackKind ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Map-entry callbacks run through the scheduler, but callback-kind ordering still needs conformance tests."
            [ "MAPCALLBACK_NEWMAP"; "MAPCALLBACK_TILES"; "MAPCALLBACK_OBJECTS"; "MAPCALLBACK_CMDQUEUE" ]

          yield! many SceneSurface ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Scene exists and can be driven, but full GUI e2e conformance is future UI-epic work."
            [ "BattleScene"; "MainMenuScene"; "MartScene"; "NamingScene"; "OptionsScene"; "OverworldScene"; "PackScene"; "PartyScene"
              "PokegearScene"; "SaveMenuScene"; "StartMenuScene"; "TextBoxScene"; "TitleScene"; "YesNoScene" ]

          yield! many SceneSurface ImplementedApproximate [ SideSystem; RequiredFor100Percent ] "Scene exists and can be driven, but full GUI e2e conformance is future UI-epic work for side systems."
            [ "PCBoxScene"; "PcMenuScene"; "PlayerPCScene"; "PokedexScene"; "SummaryScene" ]

          yield! many MoveEffect Unknown [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Generated move effect is present in move data; battle conformance status has not been audited in the ledger yet."
            [ "EFFECT_ACCURACY_DOWN"; "EFFECT_ACCURACY_DOWN_HIT"; "EFFECT_ALL_UP_HIT"; "EFFECT_ALWAYS_HIT"; "EFFECT_ATTACK_DOWN"
              "EFFECT_ATTACK_DOWN_2"; "EFFECT_ATTACK_DOWN_HIT"; "EFFECT_ATTACK_UP"; "EFFECT_ATTACK_UP_2"; "EFFECT_ATTACK_UP_HIT"; "EFFECT_ATTRACT"
              "EFFECT_BATON_PASS"; "EFFECT_BEAT_UP"; "EFFECT_BELLY_DRUM"; "EFFECT_BIDE"; "EFFECT_BURN_HIT"; "EFFECT_CONFUSE"; "EFFECT_CONFUSE_HIT"
              "EFFECT_CONVERSION"; "EFFECT_CONVERSION2"; "EFFECT_COUNTER"; "EFFECT_CURSE"; "EFFECT_DEFENSE_CURL"; "EFFECT_DEFENSE_DOWN"
              "EFFECT_DEFENSE_DOWN_2"; "EFFECT_DEFENSE_DOWN_HIT"; "EFFECT_DEFENSE_UP"; "EFFECT_DEFENSE_UP_2"; "EFFECT_DEFENSE_UP_HIT"
              "EFFECT_DESTINY_BOND"; "EFFECT_DISABLE"; "EFFECT_DOUBLE_HIT"; "EFFECT_DREAM_EATER"; "EFFECT_EARTHQUAKE"; "EFFECT_ENCORE"
              "EFFECT_ENDURE"; "EFFECT_EVASION_DOWN"; "EFFECT_EVASION_UP"; "EFFECT_FALSE_SWIPE"; "EFFECT_FLAME_WHEEL"; "EFFECT_FLINCH_HIT"
              "EFFECT_FLY"; "EFFECT_FOCUS_ENERGY"; "EFFECT_FORCE_SWITCH"; "EFFECT_FORESIGHT"; "EFFECT_FREEZE_HIT"; "EFFECT_FRUSTRATION"
              "EFFECT_FURY_CUTTER"; "EFFECT_FUTURE_SIGHT"; "EFFECT_GUST"; "EFFECT_HEAL"; "EFFECT_HEAL_BELL"; "EFFECT_HIDDEN_POWER"
              "EFFECT_HYPER_BEAM"; "EFFECT_JUMP_KICK"; "EFFECT_LEECH_HIT"; "EFFECT_LEECH_SEED"; "EFFECT_LEVEL_DAMAGE"; "EFFECT_LIGHT_SCREEN"
              "EFFECT_LOCK_ON"; "EFFECT_MAGNITUDE"; "EFFECT_MEAN_LOOK"; "EFFECT_METRONOME"; "EFFECT_MIMIC"; "EFFECT_MIRROR_COAT"
              "EFFECT_MIRROR_MOVE"; "EFFECT_MIST"; "EFFECT_MOONLIGHT"; "EFFECT_MORNING_SUN"; "EFFECT_MULTI_HIT"; "EFFECT_NIGHTMARE"
              "EFFECT_NORMAL_HIT"; "EFFECT_OHKO"; "EFFECT_PAIN_SPLIT"; "EFFECT_PARALYZE"; "EFFECT_PARALYZE_HIT"; "EFFECT_PAY_DAY"
              "EFFECT_PERISH_SONG"; "EFFECT_POISON"; "EFFECT_POISON_HIT"; "EFFECT_POISON_MULTI_HIT"; "EFFECT_PRESENT"; "EFFECT_PRIORITY_HIT"
              "EFFECT_PROTECT"; "EFFECT_PSYCH_UP"; "EFFECT_PSYWAVE"; "EFFECT_PURSUIT"; "EFFECT_RAGE"; "EFFECT_RAIN_DANCE"; "EFFECT_RAMPAGE"
              "EFFECT_RAPID_SPIN"; "EFFECT_RAZOR_WIND"; "EFFECT_RECOIL_HIT"; "EFFECT_REFLECT"; "EFFECT_RESET_STATS"; "EFFECT_RETURN"
              "EFFECT_REVERSAL"; "EFFECT_ROLLOUT"; "EFFECT_SACRED_FIRE"; "EFFECT_SAFEGUARD"; "EFFECT_SANDSTORM"; "EFFECT_SELFDESTRUCT"
              "EFFECT_SKETCH"; "EFFECT_SKULL_BASH"; "EFFECT_SKY_ATTACK"; "EFFECT_SLEEP"; "EFFECT_SLEEP_TALK"; "EFFECT_SNORE"; "EFFECT_SOLARBEAM"
              "EFFECT_SP_ATK_UP"; "EFFECT_SP_DEF_DOWN_HIT"; "EFFECT_SP_DEF_UP_2"; "EFFECT_SPEED_DOWN"; "EFFECT_SPEED_DOWN_2"; "EFFECT_SPEED_DOWN_HIT"
              "EFFECT_SPEED_UP_2"; "EFFECT_SPIKES"; "EFFECT_SPITE"; "EFFECT_SPLASH"; "EFFECT_STATIC_DAMAGE"; "EFFECT_STOMP"; "EFFECT_SUBSTITUTE"
              "EFFECT_SUNNY_DAY"; "EFFECT_SUPER_FANG"; "EFFECT_SWAGGER"; "EFFECT_SYNTHESIS"; "EFFECT_TELEPORT"; "EFFECT_THIEF"; "EFFECT_THUNDER"
              "EFFECT_TOXIC"; "EFFECT_TRANSFORM"; "EFFECT_TRAP_TARGET"; "EFFECT_TRI_ATTACK"; "EFFECT_TRIPLE_KICK"; "EFFECT_TWISTER" ]

          yield! many ItemFieldMenu Unknown [ CriticalPathJohto; RequiredFor100Percent ] "Item field-menu behavior exists in data; conformance status has not been audited in the ledger yet."
            [ "ITEMMENU_CLOSE"; "ITEMMENU_CURRENT"; "ITEMMENU_NOUSE"; "ITEMMENU_PARTY" ]

          yield! many ItemBattleMenu Unknown [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Item battle-menu behavior exists in data; conformance status has not been audited in the ledger yet."
            [ "ITEMMENU_CLOSE"; "ITEMMENU_NOUSE"; "ITEMMENU_PARTY" ]

          yield! many ItemHeldEffect Unknown [ RequiredFor100Percent; SideSystem ] "Held-item behavior exists in data; conformance status has not been audited in the ledger yet."
            [ "HELD_AMULET_COIN"; "HELD_BERRY"; "HELD_BRIGHTPOWDER"; "HELD_BUG_BOOST"; "HELD_CLEANSE_TAG"; "HELD_CRITICAL_UP"; "HELD_DARK_BOOST"
              "HELD_DRAGON_BOOST"; "HELD_ELECTRIC_BOOST"; "HELD_ESCAPE"; "HELD_FIGHTING_BOOST"; "HELD_FIRE_BOOST"; "HELD_FLINCH"
              "HELD_FLYING_BOOST"; "HELD_FOCUS_BAND"; "HELD_GHOST_BOOST"; "HELD_GRASS_BOOST"; "HELD_GROUND_BOOST"; "HELD_HEAL_BURN"
              "HELD_HEAL_CONFUSION"; "HELD_HEAL_FREEZE"; "HELD_HEAL_PARALYZE"; "HELD_HEAL_POISON"; "HELD_HEAL_SLEEP"; "HELD_HEAL_STATUS"
              "HELD_ICE_BOOST"; "HELD_LEFTOVERS"; "HELD_METAL_POWDER"; "HELD_NONE"; "HELD_NORMAL_BOOST"; "HELD_POISON_BOOST"; "HELD_PSYCHIC_BOOST"
              "HELD_QUICK_CLAW"; "HELD_RESTORE_PP"; "HELD_ROCK_BOOST"; "HELD_STEEL_BOOST"; "HELD_WATER_BOOST" ]

          yield! many FieldMove ImplementedApproximate [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "Field move has an initial runtime path but still needs GUI/input and map-effect conformance tests."
            [ "CUT"; "SURF"; "STRENGTH" ]

          yield! many FieldMove Unknown [ CriticalPathJohto; CriticalPathKanto; RequiredFor100Percent ] "HM field move is listed in data but not audited as a runtime field action."
            [ "FLY"; "FLASH"; "WHIRLPOOL"; "WATERFALL" ]

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
    Assert.Contains((ScriptSpecial, "BankOfMom"), debt)
    Assert.Contains((ScriptSpecial, "NameRival"), debt)

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
