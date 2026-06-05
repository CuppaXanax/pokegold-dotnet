namespace PokeGold.Game.Overworld.Script

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

            // ---- Deferred opcodes ------------------------------------------
            // Outside the M9 slice: skip so the rest of the script still runs.
            | Unsupported _ -> run world next

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
