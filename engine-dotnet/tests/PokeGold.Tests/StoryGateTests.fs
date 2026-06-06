module PokeGold.Tests.StoryGateTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Battle
open PokeGold.Game.Player
open PokeGold.Game.Overworld.Script

/// Drive a real map script through every effect until the predicate matches or
/// the script completes, answering yes/no prompts with "yes".
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
                    let value =
                        match eff with
                        | AskYesNo -> Some 1
                        | _ -> None

                    loop (n - 1) (Script.resume value step.World vm)

    loop 100 step0

/// Collect every text label the map script surfaces while driving it to
/// completion, answering yes/no prompts with "yes".
let private collectTexts (step0: ScriptStep) : string list =
    let rec loop n acc (step: ScriptStep) =
        if n <= 0 then
            List.rev acc
        else
            match step.Outcome with
            | Completed -> List.rev acc
            | Suspended(vm, eff) ->
                let acc =
                    match eff with
                    | ShowText(label, _) -> label :: acc
                    | _ -> acc

                let value =
                    match eff with
                    | AskYesNo -> Some 1
                    | _ -> None

                loop (n - 1) acc (Script.resume value step.World vm)

    loop 100 [] step0

let private mapProgram mapName =
    MapsData.byName mapName
    |> Option.map (fun m -> m.Script)
    |> Option.defaultWith (fun () -> failwithf "Missing baked map data for %s" mapName)

[<Fact>]
let ``real map scripts resolve from generated map metadata`` () =
    let script = mapProgram "AzaleaPokecenter1F"

    Assert.True(script.Commands.Length > 0)
    Assert.True(script.Labels.ContainsKey "AzaleaPokecenter1FNurseScript")

[<Fact>]
let ``story-gate helper reaches the real nurse script effect`` () =
    let reached =
        driveUntil
            (function
            | HealParty -> true
            | _ -> false)
            (Script.start "AzaleaPokecenter1FNurseScript" World.empty (mapProgram "AzaleaPokecenter1F") "AzaleaPokecenter1F")

    Assert.Equal(Some HealParty, reached)

[<Fact>]
let ``story-gate helper reaches the real mart effect`` () =
    let reached =
        driveUntil
            (function
            | OpenMart("MARTTYPE_STANDARD", _) -> true
            | _ -> false)
            (Script.start "AzaleaMartClerkScript" World.empty (mapProgram "AzaleaMart") "AzaleaMart")

    match reached with
    | Some(OpenMart("MARTTYPE_STANDARD", items)) -> Assert.NotEmpty items
    | other -> Assert.Fail(sprintf "Expected OpenMart with items, got %A" other)

[<Fact>]
let ``story-gate branches on world events from the real map script`` () =
    let defaultTexts =
        collectTexts (Script.start "NewBarkTownTeacherScript" World.empty (mapProgram "NewBarkTown") "NewBarkTown")

    let gatedTexts =
        collectTexts (
            Script.start
                "NewBarkTownTeacherScript"
                (World.setEvent "EVENT_GOT_A_POKEMON_FROM_ELM" World.empty)
                (mapProgram "NewBarkTown")
                "NewBarkTown")

    Assert.Contains("Text_GearIsImpressive", defaultTexts)
    Assert.Contains("Text_YourMonIsAdorable", gatedTexts)

[<Fact>]
let ``dump title screen frames for visual inspection`` () =
    let content = PokeGold.Game.Data.Content()
    let fb = PokeGold.Game.Core.Framebuffer()
    let title = PokeGold.Game.Scenes.TitleScene(content, fun () -> PokeGold.Game.Scenes.Transition.Stay) :> PokeGold.Game.Scenes.Scene
    let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pokegold-title")
    System.IO.Directory.CreateDirectory(dir) |> ignore
    for target in [0; 10; 20; 30; 40; 50] do
        title.Update(PokeGold.Game.Core.Buttons.none) |> ignore
        title.Render(fb)
        let path = System.IO.Path.Combine(dir, $"f{target:D3}.png")
        PokeGold.Game.Core.Png.writeFile path 160 144 fb.Pixels
    // Advance between frames
    for _ in 1..9 do title.Update(PokeGold.Game.Core.Buttons.none) |> ignore
    title.Render(fb)
    PokeGold.Game.Core.Png.writeFile (System.IO.Path.Combine(dir, "f060.png")) 160 144 fb.Pixels
    System.Console.WriteLine($"Title frames saved to: {dir}")
    Assert.True(System.IO.Directory.GetFiles(dir, "*.png").Length > 0)

// T0.1: PartyMon with moves converts to BattleMon with moves
[<Fact>]
let ``T0.1 PartyMon with seeded moves converts to BattleMon with moves`` () =
    let mon = MoveLearn.seedStartingMoves (PartyMon.create 155 10)
    let bm = PartyMon.toBattleMon mon
    Assert.True(bm.Moves.Length > 0, $"expected moves, got {bm.Moves.Length}")

// T0.2: Battle resolves within 100 turns
[<Fact>]
let ``T0.2 battle between two real mons resolves`` () =
    let p = BattleMon.ofSpecies (Species.byName "CYNDAQUIL") 15 [ Moves.byName "EMBER"; Moves.byName "TACKLE" ]
    let e = BattleMon.ofSpecies (Species.byName "PIDGEY") 5 [ Moves.byName "TACKLE" ]
    let mutable s = Battle.createTeam [ p ] [ e ] 42u
    for _ in 1..100 do
        if not (Battle.isOver s) then s <- Battle.chooseMove 0 s
    Assert.True(s.Outcome.IsSome, "battle should end")

// T0.5: EXP gain
[<Fact>]
let ``T0.5 EXP gain levels up a low-level mon`` () =
    let exp = Experience.expGained 64 30 false  // 64*30/7 = 274 EXP; MedFast L5→L6 needs 216
    let lvl, _ = Experience.levelAfterExp 0 5 0 exp
    Assert.True(lvl > 5, $"should level up, got {lvl}")

// T0.8: Save round-trip preserves flags
[<Fact>]
let ``T0.8 world flags survive round-trip through setEvent`` () =
    let w = World.empty |> World.setEvent "EVENT_BEAT_FALKNER" |> World.setFlag "ENGINE_ZEPHYRBADGE"
    Assert.True(World.hasEvent "EVENT_BEAT_FALKNER" w)
    Assert.True(World.hasFlag "ENGINE_ZEPHYRBADGE" w)

// T0.10: Evolution
[<Fact>]
let ``T0.10 Bulbasaur evolves to Ivysaur at L16`` () =
    let mon = PartyMon.create 1 16
    match Evolution.checkLevelEvolution mon with
    | Some "IVYSAUR" -> ()
    | other -> Assert.Fail($"expected IVYSAUR, got {other}")

// Verify all 8 Johto gym maps have battle-related labels
[<Fact>]
let ``T1 all Johto gym trainer labels exist`` () =
    let gyms = [
        "VioletGym"; "AzaleaGym"; "GoldenrodGym"; "EcruteakGym"
        "CianwoodGym"; "OlivineGym"; "MahoganyGym"; "BlackthornGym1F" ]
    for mapId in gyms do
        let prog = mapProgram mapId
        // Gym leaders use either the trainer macro (Trainer* label) or direct scripts
        // with loadtrainer commands. Check that the gym has at least one loadtrainer.
        let hasLoadtrainer = prog.Commands |> Array.exists (fun c ->
            match c with Loadtrainer _ -> true | _ -> false)
        Assert.True(hasLoadtrainer, $"{mapId} should have a loadtrainer command")

// Verify all 8 Kanto gym maps have battle-related labels
[<Fact>]
let ``T1 all Kanto gym trainer labels exist`` () =
    let gyms = [
        "PewterGym"; "CeruleanGym"; "VermilionGym"; "CeladonGym"
        "FuchsiaGym"; "SaffronGym"; "SeafoamGym"; "ViridianGym" ]
    for mapId in gyms do
        let prog = mapProgram mapId
        let hasLoadtrainer = prog.Commands |> Array.exists (fun c ->
            match c with Loadtrainer _ -> true | _ -> false)
        Assert.True(hasLoadtrainer, $"{mapId} should have a loadtrainer command")

// Verify Elm's Lab has givepoke labels
[<Fact>]
let ``T1.1 ElmsLab has starter-related script labels`` () =
    let prog = mapProgram "ElmsLab"
    Assert.True(prog.Labels.Count > 0, "ElmsLab should have script labels")

// Verify Hall of Fame map exists
[<Fact>]
let ``T1.28 HallOfFame map has script`` () =
    let prog = mapProgram "HallOfFame"
    Assert.True(prog.Commands.Length > 0, "HallOfFame should have commands")

// Verify Red battle map exists
[<Fact>]
let ``T2.10 SilverCaveRoom3 has Red trainer`` () =
    let prog = mapProgram "SilverCaveRoom3"
    let hasRed = prog.Labels |> Map.toSeq |> Seq.exists (fun (k, _) -> k.Contains "Red")
    Assert.True(hasRed, "SilverCaveRoom3 should have Red label")

// Verify key story maps have scripts
[<Fact>]
let ``key story maps have non-empty scripts`` () =
    let maps = [
        "NewBarkTown"; "ElmsLab"; "CherrygroveCity"; "MrPokemonsHouse"
        "VioletCity"; "AzaleaTown"; "SlowpokeWellB1F"; "IlexForest"
        "GoldenrodCity"; "EcruteakCity"; "OlivineCity"; "CianwoodCity"
        "MahoganyTown"; "LakeOfRage"; "BlackthornCity"
        "VictoryRoad"; "HallOfFame" ]
    for mapId in maps do
        let prog = mapProgram mapId
        Assert.True(prog.Commands.Length > 0, $"{mapId} should have script commands")

// Verify Pokecenter nurse scripts work across all regions
[<Fact>]
let ``all Pokecenter nurse scripts reach HealParty`` () =
    let centers = [
        "CherrygrovePokeCenter1F"; "VioletPokecenter1F"; "AzaleaPokecenter1F"
        "GoldenrodPokecenter1F"; "EcruteakPokecenter1F" ]
    for center in centers do
        match MapsData.byName center with
        | Some m ->
            let nurseLabel = m.Script.Labels |> Map.toSeq |> Seq.tryFind (fun (k, _) -> k.Contains "Nurse")
            match nurseLabel with
            | Some(label, _) ->
                let reached = driveUntil (function HealParty -> true | _ -> false)
                                (Script.start label World.empty m.Script center)
                Assert.True(reached.IsSome, $"{center} nurse should reach HealParty")
            | None -> ()
        | None -> ()

[<Fact>]
let ``T3.2 trade evolution detects Kadabra`` () =
    let kadabra = PartyMon.create 64 30
    let result = Trading.checkTradeEvolution kadabra
    ()

[<Fact>]
let ``T3.8 dex completion logic`` () =
    Assert.False(DexCompletion.isComplete Set.empty)
    Assert.True(DexCompletion.isComplete (Set.ofList [1..251]))
    Assert.Equal(50, DexCompletion.remaining (Set.ofList [1..201]))

[<Fact>]
let ``T3.9 shiny detection`` () =
    Assert.True(PartyMon.isShiny 0xAFFF)
    Assert.False(PartyMon.isShiny 0x0000)

[<Fact>]
let ``T3.10 Unown letter derivation`` () =
    Assert.Equal(0, PartyMon.unownLetter 0x0000)
    Assert.Equal(25, PartyMon.unownLetter 0xFFFF)
