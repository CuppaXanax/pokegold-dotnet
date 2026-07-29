module PokeGold.Tests.StdScriptsTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Overworld.Script

// M12.5 — `jumpstd`/`callstd` resolve into the baked shared standard-script
// program, so a real Pokémon Center nurse (and any std-script user) works
// end-to-end from its map script. The clerk Mart path is also pinned here.

/// Drive a script from `step`, resuming through every effect (yes/no answered
/// "yes") until the predicate matches an effect or the script completes / a step
/// cap is hit. Returns the matching effect, if reached.
let private driveUntil (matches: ScriptEffect -> bool) (step0: ScriptStep) : ScriptEffect option =
    let rec loop n (step: ScriptStep) =
        if n <= 0 then
            None
        else
            match step.Outcome with
            | Completed -> None
            | Suspended(vm, eff) ->
                if matches eff then
                    Some eff
                else
                    let v =
                        match eff with
                        | AskYesNo -> Some 1
                        | _ -> None

                    loop (n - 1) (Script.resume v step.World vm)

    loop 100 step0

let private nurseProgram = (MapsData.byName "AzaleaPokecenter1F").Value.Script
let private martProgram = (MapsData.byName "AzaleaMart").Value.Script

// ---- Opcode classification -------------------------------------------------

[<Fact>]
let ``parser yields Jumpstd and Callstd`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tjumpstd PokecenterNurseScript\n\
             \tcallstd MartSignScript\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Jumpstd "PokecenterNurseScript"; Callstd "MartSignScript"; End ],
        ScriptProgram.blockAt "S" prog
    )

// ---- Baked std-script program ---------------------------------------------

[<Fact>]
let ``std-script program bakes PokecenterNurseScript`` () =
    Assert.True(StdScriptsData.program.Labels.ContainsKey "PokecenterNurseScript")

[<Fact>]
let ``std-script text resolves nurse prompt`` () =
    Assert.True(StdScriptsData.text.ContainsKey "NurseAskHealText")

// ---- jumpstd resolution & fall-through -------------------------------------

[<Fact>]
let ``jumpstd into std program runs the std script`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tjumpstd PokecenterNurseScript\n\
             \tend\n"

    // The nurse asks to heal: the first text the std script surfaces.
    let reached =
        driveUntil
            (function
                | ShowText("NurseAskHealText", _) -> true
                | _ -> false)
            (Script.start "S" World.empty prog "")

    Assert.True(reached.IsSome)

[<Fact>]
let ``jumpstd to an unknown target falls through`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tjumpstd NotABakedStdScript\n\
             \tsetval 7\n\
             \tend\n"

    // Unresolved std target is a no-op: execution falls through to `setval`/`end`.
    match (Script.start "S" World.empty prog "").Outcome with
    | Completed -> ()
    | other -> Assert.Fail(sprintf "Expected Completed, got %A" other)

[<Fact>]
let ``callstd returns to the caller after the std script ends`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tcallstd PokecenterSignScript\n\
             \tsetval 99\n\
             \tend\n"

    // PokecenterSignScript shows a sign then ends; callstd must return so the
    // caller's `setval 99` runs and the script completes (not left dangling).
    let step1 = Script.start "S" World.empty prog ""

    let reachedSetval =
        let rec loop n (step: ScriptStep) =
            if n <= 0 then
                false
            else
                match step.Outcome with
                | Completed -> true
                | Suspended(vm, _) -> loop (n - 1) (Script.resume None step.World vm)

        loop 100 step1

    Assert.True(reachedSetval)

// ---- Real-map acceptance: Pokémon Center nurse heals -----------------------

/// Collect every text label a script surfaces (answering yes/no "yes"), so a test
/// can assert which branches actually ran.
let private collectTexts (step0: ScriptStep) : string list =
    let rec loop n acc (step: ScriptStep) =
        if n <= 0 then List.rev acc
        else
            match step.Outcome with
            | Completed -> List.rev acc
            | Suspended(vm, eff) ->
                let acc =
                    match eff with
                    | ShowText(label, _) -> label :: acc
                    | _ -> acc
                let v =
                    match eff with
                    | AskYesNo -> Some 1
                    | CheckTime _ -> Some 1
                    | _ -> None
                loop (n - 1) acc (Script.resume v step.World vm)
    loop 100 [] step0

[<Fact>]
let ``nurse script reaches the current time-of-day greeting and heal prompt`` () =
    let texts =
        collectTexts (Script.start "AzaleaPokecenter1FNurseScript" World.empty nurseProgram "")

    Assert.Contains("NurseAskHealText", texts)
    Assert.Contains(texts, fun text ->
        text = "NurseMornText" || text = "NurseDayText" || text = "NurseNiteText")

[<Fact>]
let ``Azalea nurse script reaches HealParty via jumpstd`` () =
    let reached =
        driveUntil
            (function
                | HealParty -> true
                | _ -> false)
            (Script.start "AzaleaPokecenter1FNurseScript" World.empty nurseProgram "")

    Assert.Equal(Some HealParty, reached)

[<Fact>]
let ``Azalea nurse heal restores a fainted party`` () =
    // The integration enacts HealParty by applying Heal.healParty; verify the two
    // halves connect: the real script reaches the effect, and the effect heals.
    let reached =
        driveUntil
            (function
                | HealParty -> true
                | _ -> false)
            (Script.start "AzaleaPokecenter1FNurseScript" World.empty nurseProgram "")

    Assert.Equal(Some HealParty, reached)

    let party =
        [ { Id = System.Guid.NewGuid()
            SpeciesId = 1
            Nickname = "A"
            Level = 10
            Exp = 0
            Hp = 0
            MaxHp = 45
            Status = "SLP"
            Moves = [ (1, 0) ]
            Dvs = 0
            StatExp = PokeGold.Game.Battle.StatExperience.zero
            Pokerus = 0
            HeldItem = None
            Mail = None
            OtName = "P"
            OtId = 0
            Friendship = 70
            HatchSteps = None } ]

    let healed = Heal.healParty party
    Assert.Equal(45, healed.[0].Hp)
    Assert.Equal("", healed.[0].Status)

// ---- Real-map acceptance: Mart clerk opens the shop ------------------------

[<Fact>]
let ``Ruins of Alph puzzle flags can be set`` () =
    let world = World.setEvent "EVENT_SOLVED_KABUTO_PUZZLE" World.empty
    Assert.True(World.hasEvent "EVENT_SOLVED_KABUTO_PUZZLE" world)

[<Fact>]
let ``Azalea mart clerk script reaches OpenMart`` () =
    let reached =
        driveUntil
            (function
                | OpenMart("MARTTYPE_STANDARD", _) -> true
                | _ -> false)
            (Script.start "AzaleaMartClerkScript" World.empty martProgram "")

    match reached with
    | Some(OpenMart("MARTTYPE_STANDARD", items)) -> Assert.NotEmpty items
    | other -> Assert.Fail(sprintf "Expected OpenMart with items, got %A" other)
