namespace PokeGold.Game.Overworld.Script

open PokeGold.Game.Core
open PokeGold.Game.Data

type PokegearTab =
    | MapTab
    | PhoneTab
    | RadioTab

type BalanceDisplay =
    | MoneyTopRight
    | CoinCase
    | MoneyAndCoins

type PaletteFadeColor =
    | FadeToBlack
    | FadeToWhite

type PaletteFadeDirection =
    | FadeIn
    | FadeOut

/// Something the script needs the outside world to do before it can continue. The
/// VM ([`Script`](#)) runs every *pure* command itself (control flow, flags, vars,
/// scene ids) but **suspends** on anything that touches the player, the screen, the
/// bag, audio, or a battle, handing back one of these. The integration layer
/// (M9.4) enacts it and calls `Script.resume`; result-bearing effects (their
/// doc-comment says "→ resume value") feed an int back into `wScriptVar`.
///
/// Text handling is collapsed to the player-visible behaviour: `writetext`
/// (and the `jumptext`/`jumptextfaceplayer` shorthands) all surface as `ShowText`,
/// which the text scene prints, waits a button on, and closes — so `opentext`,
/// `closetext`, and `promptbutton` are pure no-ops in the VM. `waitbutton`
/// suspends only while a preceding `pokepic` is active.
type ScriptEffect =
    /// `writetext` / `jumptext` / `jumptextfaceplayer` — print a text label
    /// (face the player first when `faceFirst`), wait for a button, close.
    | ShowText of text: string * faceFirst: bool
    /// `pokepic` — render a source Pokémon front picture over the overworld.
    | ShowPokePic of species: string
    /// `waitbutton` after `pokepic` — wait for the player to dismiss the picture.
    | WaitPokePic
    /// `closepokepic` — restore the ordinary overworld render path.
    | ClosePokePic
    /// `itemnotify` — show the current item and its source bag pocket.
    | ShowItemNotification
    /// `yesorno` — yes/no menu. → resume value: 1 (yes) / 0 (no).
    | AskYesNo
    /// `giveitem` / `verbosegiveitem` — add to the bag. → resume value: success.
    | GiveItem of item: string * qty: int * verbose: bool
    /// `takeitem` — remove from the bag. → resume value: success.
    | TakeItem of item: string * qty: int
    /// `checkitem` — is the item in the bag? → resume value: 1 / 0.
    | CheckItem of item: string
    /// Money/coin checks and mutations. Checks resume with HAVE_MORE/HAVE_AMOUNT/
    /// HAVE_LESS (0/1/2); take operations resume with 1/0; give operations resume
    /// with no value.
    | CheckMoney of amount: int
    | GiveMoney of amount: int
    | TakeMoney of amount: int
    | CheckCoins of amount: int
    | GiveCoins of amount: int
    | TakeCoins of amount: int
    /// `special SlotMachine` / `special CardFlip` — run the Game Corner game seam.
    | GameCornerGame of game: string * lucky: bool
    /// `special GameCornerPrizeMonCheckDex` — mark the staged prize species caught.
    | RegisterPrizeDex of dex: int
    /// Phone contact commands.
    | AddPhoneContact of phone: string
    | CheckPhoneContact of phone: string
    | AskPhoneNumber of phone: string
    /// `specialphonecall` / `checkphonecall` state lives in script world buffers.
    /// RTC/day setup commands and checks.
    | SetDayOfWeek
    | SetDstFlag of enabled: bool
    | CheckTime of time: string
    /// `givepoke` — add a Pokémon to the party. → resume value: 1 (success) / 0 (full).
    | GivePoke of species: string * level: int * item: string option
    /// `checkpoke` — check if species is in party. → resume value: 1 / 0.
    | CheckPoke of species: string
    /// `setlasttalked` — make this object the active one (for `faceplayer` etc.).
    | SetLastTalked of obj: string
    /// `applymovement` — run a movement script on an object; resume when it ends.
    | ApplyMovement of obj: string * movement: string
    /// `faceplayer` — turn the active object toward the player.
    | FacePlayer
    /// `faceobject` — turn object `a` toward object `b`.
    | FaceObject of a: string * b: string
    /// `appear` / `disappear` — show or hide an object.
    | SetVisible of obj: string * visible: bool
    /// `turnobject` — face an object a fixed direction.
    | TurnObject of obj: string * facing: string
    /// `moveobject` — move an object to a map cell.
    | MoveObject of obj: string * x: int * y: int
    /// `follow` — make one actor trail another until `stopfollow`.
    | Follow of follower: string * leader: string
    /// `stopfollow` — cancel the active follow relationship.
    | StopFollow
    /// `reanchormap` — recenter the camera on the player.
    | ReanchorMap
    /// `pause` / `showemote` timing — wait a number of frames before resuming.
    | Pause of frames: int
    /// `special Fade*` palette transitions. `engine/tilesets/timeofday_pals.asm`
    /// advances four palette levels with two frames per level.
    | PaletteFade of direction: PaletteFadeDirection * color: PaletteFadeColor
    /// `loadwildmon` — stage a wild encounter for the next `startbattle`.
    | LoadWild of species: string * level: int
    /// `loadtrainer` — stage a trainer battle for the next `startbattle`.
    | LoadTrainer of group: string * id: string
    /// `winlosstext` — set the staged battle's win/loss text labels.
    | WinLossText of win: string * loss: string
    /// `startbattle` — run the staged battle. → resume value: battle result.
    | StartBattle
    /// `catchtutorial` — run the source automated catching demonstration.
    | StartCatchTutorial of battleType: string
    /// `reloadmapafterbattle` / `reloadmap` / `refreshmap` — redraw the map.
    | ReloadMap
    /// `warpcheck` — enter the current generated warp event, if any.
    | WarpCheck
    /// `changeblock` — replace a map tile. Enacted by the integration layer.
    | ChangeBlock of x: int * y: int * blockId: int
    /// `playmusic` — change the background music.
    | PlayMusic of song: string
    /// `playsound` — play a sound effect.
    | PlaySound of sound: string
    /// `cry` / `special PlaySlowCry` — play a Pokémon cry.
    | Cry of species: string * slow: bool
    /// `special PlayCurMonCry` — play the current party mon selected by a prior UI.
    | CryCurrentPartyMon
    /// `waitsfx` — wait for the current sound effect to finish.
    | WaitSfx
    /// `warp` / `warpfacing` — move the player to another map cell.
    | Warp of map: string * x: int * y: int * facing: string option
    /// `special HealParty` — restore all party Pokémon to full HP, cleared
    /// status, and full PP. Enacted inline by the integration layer.
    | HealParty
    /// `pokemart` — open the Poké Mart with the given mart's inventory.
    /// Resume with None after the player closes the mart.
    | OpenMart of martType: string * items: string list
    /// `special PokemonCenterPC` — open the Pokémon Center PC dispatcher
    /// (Bill's PC / Player's PC / LOG OFF). Resume with None when the player
    /// logs off. M12.5 will wire this to the real Pokémon Center map scripts.
    | OpenPc
    /// `special BankOfMom` — open Mom's savings UI and persist money changes.
    | OpenMomBank
    /// PokeGear shell opened by Start menu or map/radio specials.
    | OpenPokegear of tab: PokegearTab * mapId: string * radioChannel: int option
    /// Money/coin display windows used by shop and prize scripts.
    | DisplayBalance of BalanceDisplay
    /// `closewindow` — clear script-opened overlay/menu windows.
    | CloseWindow
    /// `verticalmenu` / `2dmenu` — show the currently loaded script menu.
    | OpenScriptMenu of menu: string
    /// `special SelectApricornForKurt` — pick an apricorn from the bag.
    /// Resume with its numeric item id, or 0 for cancel/no apricorn.
    | SelectApricornForKurt
    /// Day-Care residents and Route 34 egg pickup specials.
    | DayCareResident of resident: string
    | DayCareManOutside
    | DayCareMon of slot: int
    /// `special CheckFirstMonIsEgg` — true when the lead party member is an egg.
    | CheckFirstMonIsEgg
    /// `special MoveDeletion` — pick a party mon and delete one move.
    | MoveDeletion
    /// `special InitRoamMons` — seed the roaming beasts' persistent map state.
    | InitRoamMons
    /// Haircut brothers' party picker/happiness special. Resume: 0 cancel, 1 egg,
    /// 2/3/4 for slightly happier / happier / much happier.
    | Haircut of brother: string
    /// `special NameRival` — open the rival naming scene and persist the result.
    | NameRival
    /// `special NameRater` — pick an owned party mon and rename it.
    | NameRater
    /// Bug-Catching Contest setup/result helpers.
    | GiveParkBalls
    | ContestDropOffMons
    | ContestReturnMons
    | BugContestJudging
    | CheckPartyFullAfterContest
    /// `special BillsGrandfather` — pick a party mon and resume with its species id.
    | BillsGrandfather
    /// `special CheckMagikarpLength` — pick a party mon, measure MAGIKARP, and resume with MAGIKARPLENGTH_*.
    | CheckMagikarpLength
    /// `special MagikarpHouseSign` — print the current longest-Magikarp record.
    | MagikarpHouseSign
    /// `special UnownPuzzle` — run the Ruins of Alph sliding-panel puzzle and resume with solved truth.
    | UnownPuzzle of puzzleId: int
    /// `special UnownPrinter` — show the Unown stamp printer UI.
    | UnownPrinter
    /// `special ProfOaksPCBoot` — count the runtime Pokédex, display Oak's
    /// source rating, play its fanfare, and resume after dismissal.
    | ShowOakPokedexRating
    /// The Hall of Fame sequence: set the champion flag and show the congratulation
    /// text sequence before the script ends.
    | HallOfFame
    /// The credits sequence, shared by Hall of Fame and Red's post-battle script.
    | RollCredits

/// The suspended state of a running script: where execution is paused and the
/// call/return stack and scratch var that survive across a `resume`. Opaque to
/// callers — produced by `Script.start`, threaded back through `Script.resume`.
type ScriptVm =
    { Program: ScriptProgram
      /// Index of the *next* command to run.
      Pc: int
      /// Return addresses pushed by `scall`/`callstd` — each carries the program
      /// to return into (the caller's own program, or a std-script program) and
      /// the command index to resume at, so a `callstd` into the shared
      /// std-script program returns to the right place.
      Stack: (ScriptProgram * int) list
      /// `wScriptVar` — the scratch register every check/compare routes through.
      ScriptVar: int
      /// The current map id, used for `checkscene` / `setscene` lookups.
      MapId: string }

/// The result of running (or resuming) a script until it next needs the outside
/// world or finishes.
type ScriptOutcome =
    /// Paused on an effect the caller must enact, then `Script.resume`.
    | Suspended of ScriptVm * ScriptEffect
    /// The script reached `end`/`endall` (or ran off the program). Nothing to do.
    | Completed

/// A run step bundles the (possibly updated) world with the outcome, since a script
/// mutates flags/vars/scenes as it runs.
type ScriptStep = { World: World; Outcome: ScriptOutcome }

/// The overworld **script virtual machine** — the high-level re-expression of the
/// GSC interpreter in `engine/overworld/scripting.asm`. It executes a
/// [`ScriptProgram`](ScriptCommand.fs) over a [`World`](EventFlags.fs), running
/// pure commands inline and suspending on a [`ScriptEffect`](#) whenever it needs
/// the player/screen/bag/audio/battle. It is itself pure: `start`/`resume` take a
/// world and VM and return new ones.
///
/// Branch semantics match the disassembly exactly — every check funnels through
/// `wScriptVar`: `checkevent`/`checkflag`/`checkitem` set it to 1/0, `setval`/
/// `readvar` load it, and `iftrue`/`iffalse`/`if{,not}equal`/`ifgreater`/`ifless`
/// compare it (`Script_iftrue` = "jump if `wScriptVar` ≠ 0", etc.).
module Script =

    let private intArg (args: string list) : int =
        args
        |> List.rev
        |> List.tryPick (fun raw ->
            let cleaned =
                raw.Replace("$", "0x").Replace(",", "").Trim()
            match System.Int32.TryParse cleaned with
            | true, value -> Some value
            | _ -> None)
        |> Option.defaultValue 0

    /// Resolve a jump/call target label to a command index. Targets that don't
    /// resolve within this program are cross-file references (`farscall` into
    /// another map) we don't model yet — the branch is skipped (fall through).
    /// `jumpstd`/`callstd` are handled separately against the baked std-script
    /// program (see [`StdScriptsData`]).
    let private targetOf (prog: ScriptProgram) (label: string) : int option = prog.Labels.TryFind label

    let private tryInt (s: string) : int =
        try int s with _ -> 0

    let private badgeFlags =
        [ // wJohtoBadges (constants/engine_flags.asm)
          "ENGINE_ZEPHYRBADGE"
          "ENGINE_HIVEBADGE"
          "ENGINE_PLAINBADGE"
          "ENGINE_FOGBADGE"
          "ENGINE_MINERALBADGE"
          "ENGINE_STORMBADGE"
          "ENGINE_GLACIERBADGE"
          "ENGINE_RISINGBADGE"
          // wKantoBadges
          "ENGINE_BOULDERBADGE"
          "ENGINE_CASCADEBADGE"
          "ENGINE_THUNDERBADGE"
          "ENGINE_RAINBOWBADGE"
          "ENGINE_SOULBADGE"
          "ENGINE_MARSHBADGE"
          "ENGINE_VOLCANOBADGE"
          "ENGINE_EARTHBADGE" ]

    let private bugContestantFlags =
        [ "EVENT_BUG_CATCHING_CONTESTANT_1A"
          "EVENT_BUG_CATCHING_CONTESTANT_2A"
          "EVENT_BUG_CATCHING_CONTESTANT_3A"
          "EVENT_BUG_CATCHING_CONTESTANT_4A"
          "EVENT_BUG_CATCHING_CONTESTANT_5A"
          "EVENT_BUG_CATCHING_CONTESTANT_6A"
          "EVENT_BUG_CATCHING_CONTESTANT_7A"
          "EVENT_BUG_CATCHING_CONTESTANT_8A"
          "EVENT_BUG_CATCHING_CONTESTANT_9A"
          "EVENT_BUG_CATCHING_CONTESTANT_10A" ]

    let private readVar (name: string) (world: World) =
        match name with
        | "VAR_BADGES" ->
            let explicitValue = World.getVar name world
            if explicitValue <> 0 then explicitValue
            else
                badgeFlags
                |> List.filter (fun flag -> World.hasFlag flag world)
                |> List.length
        | _ -> World.getVar name world

    /// Suspend the VM on an effect, with the pc already advanced past the command
    /// that produced it so `resume` continues after it.
    let private suspend (vm: ScriptVm) (world: World) (effect: ScriptEffect) : ScriptStep =
        { World = world; Outcome = Suspended(vm, effect) }

    let private speciesNameByDex dex =
        Species.all
        |> Map.tryPick (fun name stats -> if stats.Dex = dex then Some name else None)

    /// The VM state an `end` produces: return from the innermost `scall`, or — if
    /// unnested — a terminal pc so the next `run` completes. Used by the terminal
    /// text opcodes (`jumptext`/`jumptextfaceplayer`), which display text and then
    /// end the script, rather than falling through to the next command.
    let private endLike (vm: ScriptVm) : ScriptVm =
        match vm.Stack with
        | (prog, ret) :: rest -> { vm with Program = prog; Pc = ret; Stack = rest }
        | [] -> { vm with Pc = vm.Program.Commands.Length }

    /// Run pure commands from `vm.Pc` until the script suspends on an effect or
    /// terminates. The single source of truth for both `start` and `resume`.
    let rec private run (world: World) (vm: ScriptVm) : ScriptStep =
        if vm.Pc < 0 || vm.Pc >= vm.Program.Commands.Length then
            { World = world; Outcome = Completed }
        else
            let cmd = vm.Program.Commands.[vm.Pc]
            // The pc most commands advance to (the textual next command).
            let next = { vm with Pc = vm.Pc + 1 }
            // Jump to a label if it resolves in-program, else just fall through.
            let jump (label: string) =
                match targetOf vm.Program label with
                | Some i -> { vm with Pc = i }
                | None -> next

            let branch (taken: bool) (label: string) = run world (if taken then jump label else next)

            match cmd with
            // ---- Control flow ----------------------------------------------
            | Sjump target -> run world (jump target)
            // `jumpstd`/`callstd` resolve into the shared std-script program baked
            // at build time. `jumpstd` is a tail-jump (no return pushed — the std
            // script's `end` returns to the caller's frame, exactly like the GSC
            // driver); `callstd` pushes a return into THIS program. An unresolved
            // target (std program not baked) just falls through.
            | Jumpstd target ->
                match StdScriptsData.program.Labels.TryFind target with
                | Some i -> run world { vm with Program = StdScriptsData.program; Pc = i }
                | None -> run world next
            | Callstd target ->
                match StdScriptsData.program.Labels.TryFind target with
                | Some i ->
                    run world
                        { vm with
                            Program = StdScriptsData.program
                            Pc = i
                            Stack = (vm.Program, vm.Pc + 1) :: vm.Stack }
                | None -> run world next
            | Iftrue target -> branch (vm.ScriptVar <> 0) target
            | Iffalse target -> branch (vm.ScriptVar = 0) target
            | Ifequal(v, target) -> branch (vm.ScriptVar = v) target
            | Ifnotequal(v, target) -> branch (vm.ScriptVar <> v) target
            | Ifgreater(v, target) -> branch (vm.ScriptVar > v) target
            | Ifless(v, target) -> branch (vm.ScriptVar < v) target
            | Scall target ->
                // Push the return address (the command after this scall), then jump.
                let called =
                    { vm with
                        Pc = (match targetOf vm.Program target with Some i -> i | None -> vm.Pc + 1)
                        Stack = (vm.Program, vm.Pc + 1) :: vm.Stack }
                run world called
            | End ->
                // Return from the innermost scall, or stop the script if unnested.
                match vm.Stack with
                | (prog, ret) :: rest -> run world { vm with Program = prog; Pc = ret; Stack = rest }
                | [] -> { World = world; Outcome = Completed }
            | EndAll -> { World = world; Outcome = Completed }

            // ---- Variables -------------------------------------------------
            | Setval v -> run world { next with ScriptVar = v }
            | Addval v -> run world { next with ScriptVar = vm.ScriptVar + v }
            | Readvar var -> run world { next with ScriptVar = readVar var world }
            | Writevar var -> run (World.setVar var vm.ScriptVar world) next

            // ---- Event flags (EVENT_*) -------------------------------------
            | Checkevent flag -> run world { next with ScriptVar = (if World.hasEvent flag world then 1 else 0) }
            | Setevent flag -> run (World.setEvent flag world) next
            | Clearevent flag -> run (World.clearEvent flag world) next

            // ---- Engine flags (ENGINE_*) -----------------------------------
            | Checkflag flag -> run world { next with ScriptVar = (if World.hasFlag flag world then 1 else 0) }
            | Setflag flag -> run (World.setFlag flag world) next
            | Clearflag flag -> run (World.clearFlag flag world) next

            // ---- Map scene state -------------------------------------------
            | Checkscene -> run world { next with ScriptVar = World.getScene vm.MapId world }
            | Setscene scene -> run (World.setScene vm.MapId scene world) next
            | Checkmapscene map -> run world { next with ScriptVar = World.getScene map world }
            | Setmapscene(map, scene) -> run (World.setScene map scene world) next

            // ---- Pure no-ops (text-window mgmt is implicit in ShowText) ------
            | Opentext
            | Closetext
            | Promptbutton -> run world next
            | Waitbutton ->
                if World.getBuffer "__pokepic_species" world <> "" then
                    suspend next world WaitPokePic
                else
                    run world next

            // ---- Suspending effects ----------------------------------------
            | Writetext text -> suspend next world (ShowText(text, false))
            // `jumptext`/`jumptextfaceplayer` display text then END the script
            // (writetext + waitbutton + closetext + end), so resume must not fall
            // through to the next command — otherwise consecutive sign scripts run
            // into one another.
            | Jumptext text -> suspend (endLike vm) world (ShowText(text, false))
            | Jumptextfaceplayer text -> suspend (endLike vm) world (ShowText(text, true))
            | Yesorno -> suspend next world AskYesNo
            | Giveitem(item, qty) -> suspend next world (GiveItem(item, qty, false))
            | Verbosegiveitem(item, qty) -> suspend next world (GiveItem(item, qty, true))
            | Takeitem(item, qty) -> suspend next world (TakeItem(item, qty))
            | Checkitem item -> suspend next world (CheckItem item)
            | Givepoke(species, level, item) -> suspend next world (GivePoke(species, level, item))
            | Checkpoke species -> suspend next world (CheckPoke species)
            | Setlasttalked obj -> suspend next world (SetLastTalked obj)
            | Applymovement(obj, mv) -> suspend next world (ApplyMovement(obj, mv))
            | Faceplayer -> suspend next world FacePlayer
            | Faceobject(a, b) -> suspend next world (FaceObject(a, b))
            | Appear obj -> suspend next world (SetVisible(obj, true))
            | Disappear obj -> suspend next world (SetVisible(obj, false))
            | Turnobject(obj, facing) -> suspend next world (TurnObject(obj, facing))
            | Loadwildmon(species, level) -> suspend next world (LoadWild(species, level))
            | Loadtrainer(group, id) -> suspend next world (LoadTrainer(group, id))
            | Winlosstext(win, loss) -> suspend next world (WinLossText(win, loss))
            | Startbattle -> suspend next world StartBattle
            | Reloadmapafterbattle
            | Reloadmap
            | Refreshmap -> suspend next world ReloadMap
            | Changeblock(x, y, blockId) -> suspend next world (ChangeBlock(x, y, blockId))
            | Playmusic song -> suspend next world (PlayMusic song)
            | Playsound sound -> suspend next world (PlaySound sound)
            | ScriptCommand.Cry species -> suspend next world (ScriptEffect.Cry(species, false))
            | Waitsfx -> suspend next world WaitSfx
            | ScriptCommand.Warp(map, x, y) -> suspend next world (ScriptEffect.Warp(map, x, y, None))
            | Warpfacing(facing, map, x, y) -> suspend next world (ScriptEffect.Warp(map, x, y, Some facing))

            // ---- Special functions -----------------------------------------
            | Special "HealParty" -> suspend next world HealParty
            | Special "PokemonCenterPC" -> suspend next world OpenPc
            | Special "NameRival" -> suspend next world NameRival
            | Special "NameRater" -> suspend next world NameRater
            | Special "GiveParkBalls" -> suspend next world GiveParkBalls
            | Special "ContestDropOffMons" -> suspend next world ContestDropOffMons
            | Special "ContestReturnMons" -> suspend next world ContestReturnMons
            | Special "BugContestJudging" -> suspend next world BugContestJudging
            | Special "CheckPartyFullAfterContest" -> suspend next world CheckPartyFullAfterContest
            | Special "BillsGrandfather" -> suspend next world BillsGrandfather
            | Special "CheckMagikarpLength" -> suspend next world CheckMagikarpLength
            | Special "MagikarpHouseSign" -> suspend next world MagikarpHouseSign
            | Special "UnownPuzzle" -> suspend next world (UnownPuzzle vm.ScriptVar)
            | Special "UnownPrinter" -> suspend next world UnownPrinter
            | Special "SelectRandomBugContestContestants" ->
                let cleared =
                    bugContestantFlags
                    |> List.fold (fun w flag -> World.clearEvent flag w) world

                let selected =
                    bugContestantFlags
                    |> List.truncate 5
                    |> List.fold (fun w flag -> World.setEvent flag w) cleared

                run selected next
            | Special "SetDayOfWeek" -> suspend next world SetDayOfWeek
            | Special "InitialSetDSTFlag" -> suspend next world (SetDstFlag true)
            | Special "InitialClearDSTFlag" -> suspend next world (SetDstFlag false)
            | Special "BankOfMom" -> suspend next world OpenMomBank
            | Special "PlayersHousePC" -> suspend { next with ScriptVar = 0 } world OpenPc
            | Special "SelectApricornForKurt" -> suspend next world SelectApricornForKurt
            | Special "DayCareMan" -> suspend next world (DayCareResident "MAN")
            | Special "DayCareLady" -> suspend next world (DayCareResident "LADY")
            | Special "DayCareManOutside" -> suspend next world DayCareManOutside
            | Special "DayCareMon1" -> suspend next world (DayCareMon 1)
            | Special "DayCareMon2" -> suspend next world (DayCareMon 2)
            | Special "CheckFirstMonIsEgg" -> suspend next world CheckFirstMonIsEgg
            | Special "MoveDeletion" -> suspend next world MoveDeletion
            | Special "InitRoamMons" -> suspend next world InitRoamMons
            | Special "OlderHaircutBrother" -> suspend next world (Haircut "OLDER")
            | Special "YoungerHaircutBrother" -> suspend next world (Haircut "YOUNGER")
            | Special "DaisysGrooming" -> suspend next world (Haircut "DAISY")
            | Special "ProfOaksPCBoot" -> suspend next world ShowOakPokedexRating
            | Special "MagnetTrain" ->
                let destination =
                    if vm.ScriptVar = 0 then "SaffronMagnetTrainStation"
                    else "GoldenrodMagnetTrainStation"

                suspend next world (ScriptEffect.Warp(destination, 11, 6, Some "UP"))
            | Special "OverworldTownMap" -> suspend next world (OpenPokegear(MapTab, vm.MapId, None))
            | Special "MapRadio" -> suspend next world (OpenPokegear(RadioTab, vm.MapId, Some vm.ScriptVar))
            | Special "DisplayMoneyAndCoinBalance" -> suspend next world (DisplayBalance MoneyAndCoins)
            | Special "DisplayCoinCaseBalance" -> suspend next world (DisplayBalance CoinCase)
            | Special "PlaceMoneyTopRight" -> suspend next world (DisplayBalance MoneyTopRight)
            | Special "RestartMapMusic"
            | Special "PlayMapMusic" -> suspend next world (PlayMusic "__MAP_DEFAULT__")
            | Special "FadeOutMusic" -> suspend next world (PlayMusic "__STOP__")
            | Special "PlaySlowCry" ->
                match speciesNameByDex vm.ScriptVar with
                | Some species -> suspend next world (ScriptEffect.Cry(species, true))
                | None -> run world next
            | Special "PlayCurMonCry" -> suspend next world CryCurrentPartyMon
            | Special "FadeOutToWhite" -> suspend next world (PaletteFade(FadeOut, FadeToWhite))
            | Special "FadeOutToBlack" -> suspend next world (PaletteFade(FadeOut, FadeToBlack))
            | Special "FadeInFromWhite" -> suspend next world (PaletteFade(FadeIn, FadeToWhite))
            | Special "FadeInFromBlack" -> suspend next world (PaletteFade(FadeIn, FadeToBlack))
            | Special "ClearBGPalettes" -> suspend next world (PaletteFade(FadeOut, FadeToWhite))
            | Special "SlotMachine" -> suspend next world (GameCornerGame("SLOT_MACHINE", vm.ScriptVar <> 0))
            | Special "CardFlip" -> suspend next world (GameCornerGame("CARD_FLIP", false))
            | Special "GameCornerPrizeMonCheckDex" -> suspend next world (RegisterPrizeDex vm.ScriptVar)
            // engine/events/specials.asm: ScriptVar = 1 iff the party holds the
            // species staged by the preceding `setval`. The YourTrainerID variant's
            // OT check is unmodelled — every party mon belongs to the player here.
            | Special "FindPartyMonThatSpecies"
            | Special "FindPartyMonThatSpeciesYourTrainerID" ->
                let species =
                    Species.all
                    |> Map.tryPick (fun name stats -> if stats.Dex = vm.ScriptVar then Some name else None)

                match species with
                | Some name -> suspend next world (CheckPoke name)
                | None -> run world { next with ScriptVar = 0 }
            // engine/events/specials.asm SnorlaxAwake: ScriptVar = 1 iff the radio
            // is tuned to the Poké Flute channel next to Snorlax (proximity is the
            // caller's concern — the script only runs from the Snorlax object).
            | Special "SnorlaxAwake" ->
                let tuned = World.getBuffer "__radio_station" world = "POKE_FLUTE"
                run world { next with ScriptVar = if tuned then 1 else 0 }
            | Special _ -> run world next

            // ---- Mart -----------------------------------------------------
            // Resolve the MART_* constant to its item list and suspend on OpenMart.
            | Pokemart(martType, mart) ->
                let items = MartsData.byConstant |> Map.tryFind mart |> Option.defaultValue []
                suspend next world (OpenMart(martType, items))

            // ---- Typed deferred opcodes ------------------------------------
            | Verticalmenu
            | TwoDMenu ->
                let menu = World.getBuffer "__loaded_menu" world
                suspend next world (OpenScriptMenu(if menu = "" then "MENU" else menu))
            | Loadmenu menu -> run (World.setBuffer "__loaded_menu" menu world) next
            | MenuCoords coords -> run (World.setBuffer "__menu_coords" (String.concat "," coords) world) next
            | Pokepic operand ->
                let species =
                    match operand.Trim() with
                    | "0"
                    | "$0" -> speciesNameByDex vm.ScriptVar
                    | name -> Some name

                match species with
                | Some name ->
                    World.setBuffer "__pokepic_species" name world
                    |> fun pictureWorld -> suspend next pictureWorld (ShowPokePic name)
                | None -> run world next
            | Closepokepic ->
                World.clearBuffer "__pokepic_species" world
                |> fun clearedWorld -> suspend next clearedWorld ClosePokePic
            | Itemnotify -> suspend next world ShowItemNotification
            | Closewindow -> suspend next world CloseWindow
            | Elevator _ -> run world { next with ScriptVar = 1 }
            | Checkpokemail _
            | ConditionalEvent _ -> run world { next with ScriptVar = 0 }
            | Giveegg(species, level) -> suspend next world (GivePoke(species, level, None))
            | Catchtutorial battleType -> suspend next world (StartCatchTutorial battleType)
            | Trade _
            | Givepokemail _ -> run world next
            | Addcellnum phone -> suspend next world (AddPhoneContact phone)
            | Describedecoration _
            | Stonetable _
            | Cmdqueue _
            | Writecmdqueue _ -> run world next
            | Checktime time -> suspend next world (CheckTime time)
            | Checkcellnum phone -> suspend next world (CheckPhoneContact phone)
            | Checkphonecall ->
                let hasCall =
                    let call = World.getBuffer "__special_phone_call" world
                    call <> "" && call <> "SPECIALCALL_NONE"
                run world { next with ScriptVar = (if hasCall then 1 else 0) }
            | Checkjustbattled ->
                let justBattled = World.getVar "__just_battled" world <> 0
                run world { next with ScriptVar = if justBattled then 1 else 0 }
            | Askforphonenumber phone -> suspend next world (AskPhoneNumber phone)
            | Checkmoney args -> suspend next world (CheckMoney(intArg args))
            | Checkcoins amount -> suspend next world (CheckCoins(defaultArg amount 0))
            | Checkver -> run world { next with ScriptVar = 0 }
            | Random limit -> run world { next with ScriptVar = (vm.ScriptVar + 7) % (max 1 limit) }
            | Loadvar(varName, value) -> run (World.setVar varName value world) { next with ScriptVar = value }
            | Loadmem(addr, value) -> run (World.setVar addr value world) { next with ScriptVar = value }
            | Readmem addr -> run world { next with ScriptVar = World.getVar addr world }
            | Writemem addr -> run (World.setVar addr vm.ScriptVar world) next
            | Gettrainername(bufferName, group, id) ->
                let trainerName =
                    Trainers.lookupByName group id
                    |> Option.map (fun t -> t.Name)
                    |> Option.defaultValue id
                run (World.setBuffer bufferName trainerName world) next
            | Getitemname(bufferName, itemId) ->
                let itemName =
                    Items.byId |> Map.tryFind itemId |> Option.map (fun item -> item.Name) |> Option.defaultValue (itemId.Replace("_", " "))
                run (World.setBuffer bufferName itemName world) next
            | Getmonname(bufferName, speciesId) ->
                let monName =
                    Species.all |> Map.tryFind speciesId |> Option.map (fun stats -> stats.Name) |> Option.defaultValue (speciesId.Replace("_", " "))
                run (World.setBuffer bufferName monName world) next
            | Getstring(bufferName, value) -> run (World.setBuffer bufferName (value.Replace("_", " ")) world) next
            | Getnum(bufferName, varName) -> run (World.setBuffer bufferName (string (World.getVar varName world)) world) next
            | Getcurlandmarkname bufferName -> run (World.setBuffer bufferName (vm.MapId.Replace("_", " ")) world) next
            | Halloffame ->
                let w = World.setEvent "EVENT_BEAT_ELITE_FOUR" world
                suspend (endLike vm) w HallOfFame
            | Credits -> suspend (endLike vm) (World.setVar "__credits_rolled" 1 world) RollCredits
            | Givemoney args -> suspend next world (GiveMoney(intArg args))
            | Takemoney args -> suspend next world (TakeMoney(intArg args))
            | Blackoutmod map ->
                run (World.setBuffer "__blackout_map" map (World.setVar "wLastSpawnMap" 1 world)) next
            | Dontrestartmapmusic -> run (World.setVar "__dont_restart_map_music" 1 world) next
            | Doorstate(doorArg, stateArg) ->
                let door =
                    match doorArg with
                    | Some 1 -> Some(16, 6)
                    | Some 2 -> Some(10, 6)
                    | Some 3 -> Some(2, 6)
                    | Some 4 -> Some(2, 10)
                    | Some 5 -> Some(10, 10)
                    | Some 6 -> Some(16, 10)
                    | Some 7 -> Some(12, 6)
                    | Some 8 -> Some(12, 8)
                    | Some 9 -> Some(6, 6)
                    | Some 10 -> Some(6, 8)
                    | Some 11 -> Some(12, 10)
                    | Some 12 -> Some(12, 12)
                    | Some 13 -> Some(6, 10)
                    | Some 14 -> Some(6, 12)
                    | Some 15 -> Some(18, 10)
                    | Some 16 -> Some(18, 12)
                    | _ -> None
                let block =
                    match stateArg |> Option.map (fun s -> s.ToUpperInvariant()) with
                    | Some "CLOSED1" -> Some 0x2a
                    | Some "CLOSED2" -> Some 0x3e
                    | Some "CLOSED3" -> Some 0x3f
                    | Some "OPEN1" -> Some 0x2d
                    | Some "OPEN2" -> Some 0x3d
                    | _ -> None
                match door, block with
                | Some(x, y), Some b -> suspend next world (ChangeBlock(x, y, b))
                | _ -> run world next
            | Earthquake frames -> suspend next world (ScriptEffect.Pause(defaultArg frames 30))
            | Elevfloor _ -> run world next
            | Endifjustbattled ->
                if World.getVar "__just_battled" world <> 0 then
                    run (World.setVar "__just_battled" 0 world) (endLike vm)
                else run world next
            | ScriptCommand.Follow(follower, leader) -> suspend next world (ScriptEffect.Follow(follower, leader))
            | Stopfollow -> suspend next world StopFollow
            | Givecoins amount -> suspend next world (GiveCoins(defaultArg amount 0))
            | Takecoins amount -> suspend next world (TakeCoins(defaultArg amount 0))
            | Moveobject(obj, x, y) -> suspend next world (MoveObject(obj, x, y))
            | Musicfadeout -> suspend next world (PlayMusic "__STOP__")
            | Newloadmap -> suspend next world ReloadMap
            | ScriptCommand.Pause frames -> if frames <= 0 then run world next else suspend next world (ScriptEffect.Pause frames)
            | Playmapmusic -> suspend next world (PlayMusic "__MAP_DEFAULT__")
            | Reanchormap -> suspend next world ReanchorMap
            | Showemote(_, _, frames) -> if frames <= 0 then run world next else suspend next world (ScriptEffect.Pause frames)
            | Specialphonecall call ->
                let value = if call = "SPECIALCALL_NONE" then "" else call
                run (World.setBuffer "__special_phone_call" value world) next
            | TeleportFrom ->
                run (world |> World.setBuffer "__teleport_from_map" vm.MapId |> World.setVar "__teleport_from_x" 0 |> World.setVar "__teleport_from_y" 0) next
            | TreeShake -> suspend next world (ScriptEffect.Pause 30)
            | Ugdoor _ -> run world next
            | Variablesprite(sprite, replacement) -> run (World.setBuffer ("__sprite_" + sprite) replacement world) next
            | Warpcheck -> suspend next world WarpCheck
            | Writeobjectxy _ -> run world next

            // Generated data is validated to contain no generic Unsupported commands.
            // This fallback only keeps ad-hoc parser tests and exploratory snippets
            // from crashing when they intentionally include an unknown opcode.
            | Unsupported _ -> run world next

    /// Start a script at `label` over `world`, running until it suspends or ends.
    /// An unknown label completes immediately (nothing to run).
    let start (label: string) (world: World) (prog: ScriptProgram) (mapId: string) : ScriptStep =
        match prog.Labels.TryFind label with
        | None -> { World = world; Outcome = Completed }
        | Some pc ->
            run world
                { Program = prog
                  Pc = pc
                  Stack = []
                  ScriptVar = 0
                  MapId = mapId }

    /// Continue a suspended script after its effect was enacted. For result-bearing
    /// effects, pass `Some value` to feed `wScriptVar` (e.g. the yes/no choice or
    /// battle result); pass `None` for effects that produce nothing.
    let resume (value: int option) (world: World) (vm: ScriptVm) : ScriptStep =
        let vm =
            match value with
            | Some v -> { vm with ScriptVar = v }
            | None -> vm

        run world vm
