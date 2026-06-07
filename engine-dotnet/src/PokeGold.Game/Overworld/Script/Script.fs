namespace PokeGold.Game.Overworld.Script

open PokeGold.Game.Core
open PokeGold.Game.Data

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
/// `closetext`, `waitbutton`, and `promptbutton` are pure no-ops in the VM.
type ScriptEffect =
    /// `writetext` / `jumptext` / `jumptextfaceplayer` — print a text label
    /// (face the player first when `faceFirst`), wait for a button, close.
    | ShowText of text: string * faceFirst: bool
    /// `yesorno` — yes/no menu. → resume value: 1 (yes) / 0 (no).
    | AskYesNo
    /// `giveitem` / `verbosegiveitem` — add to the bag. → resume value: success.
    | GiveItem of item: string * qty: int * verbose: bool
    /// `takeitem` — remove from the bag. → resume value: success.
    | TakeItem of item: string * qty: int
    /// `checkitem` — is the item in the bag? → resume value: 1 / 0.
    | CheckItem of item: string
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
    /// `loadwildmon` — stage a wild encounter for the next `startbattle`.
    | LoadWild of species: string * level: int
    /// `loadtrainer` — stage a trainer battle for the next `startbattle`.
    | LoadTrainer of group: string * id: string
    /// `winlosstext` — set the staged battle's win/loss text labels.
    | WinLossText of win: string * loss: string
    /// `startbattle` — run the staged battle. → resume value: battle result.
    | StartBattle
    /// `reloadmapafterbattle` / `reloadmap` / `refreshmap` — redraw the map.
    | ReloadMap
    /// `changeblock` — replace a map tile. Enacted by the integration layer.
    | ChangeBlock of x: int * y: int * blockId: int
    /// `playmusic` — change the background music.
    | PlayMusic of song: string
    /// `playsound` — play a sound effect.
    | PlaySound of sound: string
    /// `cry` — play a Pokémon cry.
    | Cry of species: string
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
    /// The Hall of Fame sequence: set the champion flag and show the congratulation
    /// text sequence before the script ends.
    | HallOfFame

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

    /// Resolve a jump/call target label to a command index. Targets that don't
    /// resolve within this program are cross-file references (`farscall` into
    /// another map) we don't model yet — the branch is skipped (fall through).
    /// `jumpstd`/`callstd` are handled separately against the baked std-script
    /// program (see [`StdScriptsData`]).
    let private targetOf (prog: ScriptProgram) (label: string) : int option = prog.Labels.TryFind label

    let private tryInt (s: string) : int =
        try int s with _ -> 0

    /// Suspend the VM on an effect, with the pc already advanced past the command
    /// that produced it so `resume` continues after it.
    let private suspend (vm: ScriptVm) (world: World) (effect: ScriptEffect) : ScriptStep =
        { World = world; Outcome = Suspended(vm, effect) }

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
            | Readvar var -> run world { next with ScriptVar = World.getVar var world }
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
            | Waitbutton
            | Promptbutton -> run world next

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
            | ScriptCommand.Cry species -> suspend next world (ScriptEffect.Cry species)
            | Waitsfx -> suspend next world WaitSfx
            | ScriptCommand.Warp(map, x, y) -> suspend next world (ScriptEffect.Warp(map, x, y, None))
            | Warpfacing(facing, map, x, y) -> suspend next world (ScriptEffect.Warp(map, x, y, Some facing))

            // ---- Special functions -----------------------------------------
            // HealParty is enacted by the integration layer.
            // PokemonCenterPC opens the PC dispatcher (M12.3).
            // All other specials (HealMachineAnim, RestartMapMusic, etc.) are cosmetic and skipped.
            | Special "HealParty" -> suspend next world HealParty
            | Special "PokemonCenterPC" -> suspend next world OpenPc
            | Special _ -> run world next

            // ---- Mart -----------------------------------------------------
            // Resolve the MART_* constant to its item list and suspend on OpenMart.
            | Pokemart(martType, mart) ->
                let items = MartsData.byConstant |> Map.tryFind mart |> Option.defaultValue []
                suspend next world (OpenMart(martType, items))

            // ---- Typed deferred opcodes ------------------------------------
            | Verticalmenu
            | TwoDMenu -> run world { next with ScriptVar = 1 }
            | Loadmenu _
            | Closewindow
            | Pokepic _
            | Closepokepic
            | Itemnotify
            | Prompt -> run world next
            | Elevator _ -> run world { next with ScriptVar = 1 }
            | Checkpokemail _
            | ConditionalEvent _ -> run world { next with ScriptVar = 0 }
            | Giveegg(species, level) -> suspend next world (GivePoke(species, level, None))
            | Catchtutorial
            | Trade _
            | Givepokemail _
            | Addcellnum _
            | Describedecoration _
            | Stonetable _
            | Cmdqueue _
            | Writecmdqueue _ -> run world next
            | Checktime _ -> run world { next with ScriptVar = TimeOfDay.toScriptVar (TimeOfDay.current()) }
            | Checkcellnum _ -> run world { next with ScriptVar = 0 }
            | Checkphonecall -> run world { next with ScriptVar = 0 }
            | Checkjustbattled -> run world { next with ScriptVar = 0 }
            | Askforphonenumber _ -> run world { next with ScriptVar = 2 }
            | Checkmoney _ -> run world { next with ScriptVar = 1 }
            | Checkcoins _ -> run world { next with ScriptVar = 1 }
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
            | TextRam _ -> run world next
            | Halloffame ->
                let w = World.setEvent "EVENT_BEAT_ELITE_FOUR" world
                suspend (endLike vm) w HallOfFame
            | Credits -> run world (endLike vm)
            | Givemoney _
            | Takemoney _ -> run world next
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
            | Givecoins _
            | Takecoins _ -> run world next
            | MenuCoords _ -> run world next
            | Moveobject(obj, x, y) -> suspend next world (MoveObject(obj, x, y))
            | Musicfadeout -> suspend next world (PlayMusic "__STOP__")
            | Newloadmap -> suspend next world ReloadMap
            | ScriptCommand.Pause frames -> if frames <= 0 then run world next else suspend next world (ScriptEffect.Pause frames)
            | Playmapmusic -> suspend next world (PlayMusic "__MAP_DEFAULT__")
            | Reanchormap -> suspend next world ReanchorMap
            | Showemote(_, _, frames) -> if frames <= 0 then run world next else suspend next world (ScriptEffect.Pause frames)
            | Specialphonecall _ -> run world next
            | TeleportFrom ->
                run (world |> World.setBuffer "__teleport_from_map" vm.MapId |> World.setVar "__teleport_from_x" 0 |> World.setVar "__teleport_from_y" 0) next
            | TreeShake -> suspend next world (ScriptEffect.Pause 30)
            | Ugdoor _ -> run world next
            | Variablesprite(sprite, replacement) -> run (World.setBuffer ("__sprite_" + sprite) replacement world) next
            | Warpcheck -> run world next
            | Writeobjectxy _ -> run world next

            // ---- Deferred opcodes ------------------------------------------
            // Outside the M9 slice: a few of these are still required to set the
            // script var or write back to world state for the control-flow scripts
            // that depend on them.
            | Unsupported(name, args) ->
                match name with
                | "verticalmenu" | "_2dmenu" -> run world { next with ScriptVar = 1 }
                | "loadmenu" | "closewindow" | "pokepic" | "closepokepic" | "itemnotify" | "prompt" -> run world next
                | "elevator" -> run world { next with ScriptVar = 1 }
                | "checkpokemail" | "conditional_event" -> run world { next with ScriptVar = 0 }
                | "giveegg" when args.Length >= 2 ->
                    suspend next world (GivePoke(args.[0], (try int args.[1] with _ -> 5), None))
                | "catchtutorial" | "trade" | "givepokemail" | "addcellnum" | "describedecoration" | "stonetable" | "cmdqueue" | "writecmdqueue" -> run world next
                | "checktime" ->
                    run world { next with ScriptVar = TimeOfDay.toScriptVar (TimeOfDay.current()) }
                | "checkcellnum" -> run world { next with ScriptVar = 0 }
                | "checkphonecall" -> run world { next with ScriptVar = 0 }
                | "checkjustbattled" -> run world { next with ScriptVar = 0 }
                | "askforphonenumber" -> run world { next with ScriptVar = 2 }
                | "checkmoney" -> run world { next with ScriptVar = 1 }
                | "checkcoins" -> run world { next with ScriptVar = 1 }
                | "checkver" -> run world { next with ScriptVar = 0 }
                | "random" when args.Length >= 1 ->
                    let limit = max 1 (tryInt args.[0])
                    run world { next with ScriptVar = (vm.ScriptVar + 7) % limit }
                | "loadvar" when args.Length >= 2 ->
                    let varName = args.[0]
                    let value = tryInt args.[1]
                    run (World.setVar varName value world) { next with ScriptVar = value }
                | "loadmem" when args.Length >= 2 ->
                    let addr = args.[0]
                    let value = tryInt args.[1]
                    run (World.setVar addr value world) { next with ScriptVar = value }
                | "readmem" when args.Length >= 1 ->
                    run world { next with ScriptVar = World.getVar args.[0] world }
                | "writemem" when args.Length >= 1 ->
                    run (World.setVar args.[0] vm.ScriptVar world) next
                | "gettrainername" when args.Length >= 3 ->
                    let bufferName = args.[0]
                    let trainerName =
                        Trainers.lookupByName args.[1] args.[2]
                        |> Option.map (fun t -> t.Name)
                        |> Option.defaultValue args.[2]
                    run (World.setBuffer bufferName trainerName world) next
                | "getitemname" when args.Length >= 2 ->
                    let itemName =
                        Items.byId |> Map.tryFind args.[1] |> Option.map (fun item -> item.Name) |> Option.defaultValue (args.[1].Replace("_", " "))
                    run (World.setBuffer args.[0] itemName world) next
                | "getmonname" when args.Length >= 2 ->
                    let monName =
                        Species.all |> Map.tryFind args.[1] |> Option.map (fun stats -> stats.Name) |> Option.defaultValue (args.[1].Replace("_", " "))
                    run (World.setBuffer args.[0] monName world) next
                | "getstring" when args.Length >= 2 ->
                    run (World.setBuffer args.[0] (args.[1].Replace("_", " ")) world) next
                | "getnum" when args.Length >= 2 ->
                    run (World.setBuffer args.[0] (string (World.getVar args.[1] world)) world) next
                | "text_ram" -> run world next
                | "halloffame" ->
                    let w = World.setEvent "EVENT_BEAT_ELITE_FOUR" world
                    suspend (endLike vm) w HallOfFame
                | "credits" ->
                    run world (endLike vm)
                | "givemoney"
                | "takemoney" -> run world next
                // --- Remaining 24 opcodes: every one handled explicitly ---
                | "blackoutmod" when args.Length >= 1 ->
                    // Store the blackout destination map for whiteout/respawn
                    run (World.setBuffer "__blackout_map" args.[0] (World.setVar "wLastSpawnMap" 1 world)) next
                | "dontrestartmapmusic" ->
                    run (World.setVar "__dont_restart_map_music" 1 world) next
                | "doorstate" when args.Length >= 2 ->
                    let door =
                        match tryInt args.[0] with
                        | 1 -> Some(16, 6)
                        | 2 -> Some(10, 6)
                        | 3 -> Some(2, 6)
                        | 4 -> Some(2, 10)
                        | 5 -> Some(10, 10)
                        | 6 -> Some(16, 10)
                        | 7 -> Some(12, 6)
                        | 8 -> Some(12, 8)
                        | 9 -> Some(6, 6)
                        | 10 -> Some(6, 8)
                        | 11 -> Some(12, 10)
                        | 12 -> Some(12, 12)
                        | 13 -> Some(6, 10)
                        | 14 -> Some(6, 12)
                        | 15 -> Some(18, 10)
                        | 16 -> Some(18, 12)
                        | _ -> None
                    let block =
                        match args.[1].ToUpperInvariant() with
                        | "CLOSED1" -> Some 0x2a
                        | "CLOSED2" -> Some 0x3e
                        | "CLOSED3" -> Some 0x3f
                        | "OPEN1" -> Some 0x2d
                        | "OPEN2" -> Some 0x3d
                        | _ -> None
                    match door, block with
                    | Some(x, y), Some b -> suspend next world (ChangeBlock(x, y, b))
                    | _ -> run world next
                | "doorstate" -> run world next
                | "earthquake" when args.Length >= 1 -> suspend next world (Pause(tryInt args.[0]))
                | "earthquake" -> suspend next world (Pause 30)
                | "elevfloor" -> run world next  // elevator floor display
                | "endifjustbattled" ->
                    // End script if we just came from a trainer battle.
                    // We don't track wRunningTrainerBattleScript, so check if
                    // the last battle was a trainer via a world var.
                    if World.getVar "__just_battled" world <> 0 then
                        run (World.setVar "__just_battled" 0 world) (endLike vm)
                    else run world next
                | "follow" when args.Length >= 2 -> suspend next world (Follow(args.[0], args.[1]))
                | "follow" -> run world next
                | "stopfollow" -> suspend next world StopFollow
                | "givecoins" -> run world next  // coins tracked on PlayerState
                | "takecoins" -> run world next
                | "menu_coords" -> run world next  // set menu position (UI layout)
                | "moveobject" when args.Length >= 3 ->
                    suspend next world (MoveObject(args.[0], tryInt args.[1], tryInt args.[2]))
                | "moveobject" -> run world next
                | "musicfadeout" -> suspend next world (PlayMusic "__STOP__")
                | "newloadmap" -> suspend next world ReloadMap
                | "pause" when args.Length >= 1 ->
                    suspend next world (Pause(tryInt args.[0]))
                | "pause" -> run world next
                | "playmapmusic" -> suspend next world (PlayMusic "__MAP_DEFAULT__")
                | "reanchormap" -> suspend next world ReanchorMap
                | "showemote" when args.Length >= 3 ->
                    suspend next world (Pause(tryInt args.[2]))
                | "showemote" -> run world next
                | "specialphonecall" -> run world next  // trigger phone call
                | "teleport_from" ->
                    run (world |> World.setBuffer "__teleport_from_map" vm.MapId |> World.setVar "__teleport_from_x" 0 |> World.setVar "__teleport_from_y" 0) next
                | "tree_shake" -> suspend next world (Pause 30)
                | "ugdoor" -> run world next  // underground door declarations are macro data
                | "variablesprite" when args.Length >= 2 ->
                    run (World.setBuffer ("__sprite_" + args.[0]) args.[1] world) next
                | "variablesprite" -> run world next
                | "warpcheck" -> run world next  // check if warp should apply
                | _ -> run world next

    /// Start a script at `label` over `world`, running until it suspends or ends.
    /// An unknown label completes immediately (nothing to run).
    let start (label: string) (world: World) (prog: ScriptProgram) (mapId: string) : ScriptStep =
        match prog.Labels.TryFind label with
        | None -> { World = world; Outcome = Completed }
        | Some pc -> run world { Program = prog; Pc = pc; Stack = []; ScriptVar = 0; MapId = mapId }

    /// Continue a suspended script after its effect was enacted. For result-bearing
    /// effects, pass `Some value` to feed `wScriptVar` (e.g. the yes/no choice or
    /// battle result); pass `None` for effects that produce nothing.
    let resume (value: int option) (world: World) (vm: ScriptVm) : ScriptStep =
        let vm =
            match value with
            | Some v -> { vm with ScriptVar = v }
            | None -> vm

        run world vm
