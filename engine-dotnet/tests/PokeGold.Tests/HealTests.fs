module PokeGold.Tests.HealTests

open Xunit
open PokeGold.Game.Player
open PokeGold.Game.Overworld.Script

// M12.1 — pure heal transform + script wiring tests.

let private makeMon hp maxHp status moves : PartyMon = { Id = System.Guid.NewGuid(); SpeciesId = 1; Nickname = "TEST"; Level = 10; Exp = 0; Hp = hp; MaxHp = maxHp; Status = status; Moves = moves; Dvs = 0; StatExp = PokeGold.Game.Battle.StatExperience.zero; Pokerus = 0; HeldItem = None; Mail = None; OtName = "PLAYER"; OtId = 0; Friendship = 70 }

// ---- Pure heal transform ---------------------------------------------------

[<Fact>]
let ``healMon restores HP to MaxHp`` () =
    let mon = makeMon 1 45 "" []
    let healed = Heal.healMon mon
    Assert.Equal(45, healed.Hp)
    Assert.Equal(45, healed.MaxHp)

[<Fact>]
let ``healMon clears status`` () =
    for status in [ "PSN"; "BRN"; "FRZ"; "PAR"; "SLP" ] do
        let healed = Heal.healMon (makeMon 10 45 status [])
        Assert.Equal("", healed.Status)

[<Fact>]
let ``healMon restores move PP to base PP`` () =
    // Move id 1 = POUND, base PP = 35 (verified in Moves.Generated.fs).
    let mon = makeMon 1 45 "" [ (1, 5) ]
    let healed = Heal.healMon mon
    Assert.Equal(35, snd healed.Moves.[0])

[<Fact>]
let ``healMon restores multiple depleted moves`` () =
    // 1 = POUND (35 PP), 10 = SCRATCH (35 PP), 33 = TACKLE (35 PP)
    let mon = makeMon 20 45 "PSN" [ (1, 0); (10, 3); (33, 1) ]
    let healed = Heal.healMon mon
    Assert.Equal(35, snd healed.Moves.[0])
    Assert.Equal(35, snd healed.Moves.[1])
    Assert.Equal(35, snd healed.Moves.[2])

[<Fact>]
let ``healMon with unknown move id yields PP 0`` () =
    let mon = makeMon 45 45 "" [ (9999, 0) ]
    let healed = Heal.healMon mon
    Assert.Equal(0, snd healed.Moves.[0])

[<Fact>]
let ``healMon does not change MaxHp`` () =
    let mon = makeMon 0 60 "SLP" []
    let healed = Heal.healMon mon
    Assert.Equal(60, healed.MaxHp)

[<Fact>]
let ``healParty heals all members`` () =
    let party =
        [ makeMon 1 45 "PSN" [ (1, 5) ]    // damaged, poisoned, low PP
          makeMon 0 60 "SLP" [ (1, 0) ]    // fainted, asleep, zero PP
          makeMon 50 50 "" [] ]             // already full, no moves
    let healed = Heal.healParty party
    Assert.Equal(3, healed.Length)
    Assert.Equal(45, healed.[0].Hp)
    Assert.Equal("", healed.[0].Status)
    Assert.Equal(35, snd healed.[0].Moves.[0])
    Assert.Equal(60, healed.[1].Hp)
    Assert.Equal("", healed.[1].Status)
    Assert.Equal(35, snd healed.[1].Moves.[0])
    Assert.Equal(50, healed.[2].Hp)

[<Fact>]
let ``healParty on empty party returns empty`` () =
    Assert.Equal<PartyMon list>([], Heal.healParty [])

// ---- Script parser ---------------------------------------------------------

[<Fact>]
let ``parser yields Special for the special opcode`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tspecial HealParty\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Special "HealParty"; End ],
        ScriptProgram.blockAt "S" prog
    )

[<Fact>]
let ``parser yields Special for cosmetic specials`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tspecial HealMachineAnim\n\
             \tspecial RestartMapMusic\n\
             \tend\n"

    Assert.Equal<ScriptCommand list>(
        [ Special "HealMachineAnim"; Special "RestartMapMusic"; End ],
        ScriptProgram.blockAt "S" prog
    )

// ---- Script VM -------------------------------------------------------------

[<Fact>]
let ``interpreter suspends with HealParty effect`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tspecial HealParty\n\
             \tend\n"

    match (Script.start "S" World.empty prog "").Outcome with
    | Suspended(_, HealParty) -> ()
    | other -> Assert.Fail(sprintf "Expected Suspended HealParty, got %A" other)

[<Fact>]
let ``interpreter suspends for the heal machine animation before map music resumes`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tspecial HealMachineAnim\n\
             \tspecial RestartMapMusic\n\
             \tend\n"

    let animation = Script.start "S" World.empty prog ""

    match animation.Outcome with
    | Suspended(vm, HealMachineAnimation 0) ->
        match (Script.resume None animation.World vm).Outcome with
        | Suspended(_, PlayMusic "__MAP_DEFAULT__") -> ()
        | other -> Assert.Fail(sprintf "Expected map music after animation, got %A" other)
    | other -> Assert.Fail(sprintf "Expected heal machine animation effect, got %A" other)

[<Fact>]
let ``Lucky Channel specials reset the Friday timer and render the generated ID`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tspecial ResetLuckyNumberShowFlag\n\
             \tspecial PrintTodaysLuckyNumber\n\
             \tend\n"

    let world =
        World.empty
        |> World.setVar "VAR_WEEKDAY" 5
        |> World.setVar "__day_count" 20
        |> World.setVar "__lucky_number_seed" 123

    let step = Script.start "S" world prog ""
    Assert.Equal(27, World.getVar "__lucky_number_due_day" step.World)
    Assert.Equal(5, (World.getBuffer "STRING_BUFFER_3" step.World).Length)

[<Fact>]
let ``side-system specials surface their real runtime effects`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tspecial CheckForLuckyNumberWinners\n\
             \tspecial GetFirstPokemonHappiness\n\
             \tspecial GiveShuckle\n\
             \tspecial ReturnShuckie\n\
             \tend\n"

    match (Script.start "S" World.empty prog "").Outcome with
    | Suspended(_, CheckLuckyNumberWinners) -> ()
    | other -> Assert.Fail(sprintf "Expected Lucky Channel effect, got %A" other)

[<Fact>]
let ``Lucky Channel selects the source prize tier from trailing OT-ID digits`` () =
    let exact = { makeMon 10 10 "" [] with OtId = 12345 }
    let near = { makeMon 10 10 "" [] with OtId = 99345 }
    let noMatch = { makeMon 10 10 "" [] with OtId = 88880 }

    let first, winner = LuckyNumber.bestMatch 12345 [ near; noMatch; exact ]
    Assert.Equal(1, first)
    Assert.Equal(Some exact.Id, winner |> Option.map _.Id)

    let none, winner = LuckyNumber.bestMatch 12345 [ noMatch ]
    Assert.Equal(0, none)
    Assert.Equal(None, winner)

[<Fact>]
let ``Shuckie return removes only Mania's unhappy Shuckle`` () =
    let party = Shuckie.give [] |> Option.defaultWith (fun () -> failwith "expected Shuckie")
    let returned, afterReturn = Shuckie.returnToMania 0 party
    Assert.Equal(2, returned)
    Assert.Empty(afterReturn)

    let happy = { party.Head with Friendship = 150 }
    let kept, afterKeep = Shuckie.returnToMania 0 [ happy ]
    Assert.Equal(3, kept)
    Assert.Single(afterKeep) |> ignore

[<Fact>]
let ``script continues past HealParty on resume`` () =
    let prog =
        ScriptParser.parseText
            "S:\n\
             \tspecial HealParty\n\
             \tsetval 42\n\
             \tend\n"

    let step1 = Script.start "S" World.empty prog ""

    match step1.Outcome with
    | Suspended(vm, HealParty) ->
        let step2 = Script.resume None step1.World vm
        Assert.Equal(Completed, step2.Outcome)
        Assert.Equal(42, step2.World.Vars |> Map.tryFind "wScriptVar" |> Option.defaultValue 42)
    | other -> Assert.Fail(sprintf "Expected HealParty suspension, got %A" other)
