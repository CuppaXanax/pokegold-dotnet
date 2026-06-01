module PokeGold.Tests.SaveMenuTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Save
open PokeGold.Game.Scenes

// ── Helpers ──────────────────────────────────────────────────────────────────

/// Press a button for one frame (held), then release for one frame (edge).
/// Returns the Transition from the held frame.
let private press (b: Buttons) (scene: SaveMenuScene) : Transition =
    let t = (scene :> Scene).Update(b)
    (scene :> Scene).Update(Buttons.none) |> ignore
    t

let private pressA    s = press { Buttons.none with A    = true } s
let private pressB    s = press { Buttons.none with B    = true } s
let private pressDown s = press { Buttons.none with Down = true } s

let private newScene (onSave: unit -> unit) =
    SaveMenuScene(Content(), "ASH", onSave)

// ── YES path ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``YES: onSave is called exactly once`` () =
    let count = ref 0
    let scene = newScene (fun () -> incr count)
    pressA scene |> ignore   // A with cursor on YES (default)
    Assert.Equal(1, !count)

[<Fact>]
let ``YES: transition from A is Push (text-box scene)`` () =
    let scene = newScene (fun () -> ())
    let t = pressA scene
    match t with
    | Push _ -> ()
    | other  -> Assert.Fail(sprintf "Expected Push, got %A" other)

[<Fact>]
let ``YES: second Update after Push returns Pop`` () =
    // Simulates the "saved" text box popping and SaveMenuScene getting Update again.
    let scene = newScene (fun () -> ())
    pressA scene |> ignore   // → Push, phase becomes WaitingToPop
    let t = (scene :> Scene).Update(Buttons.none)
    Assert.Equal(Pop, t)

// ── NO path (move cursor to NO, then A) ──────────────────────────────────────

[<Fact>]
let ``NO: onSave is not called`` () =
    let count = ref 0
    let scene = newScene (fun () -> incr count)
    pressDown scene |> ignore   // cursor → NO
    pressA    scene |> ignore
    Assert.Equal(0, !count)

[<Fact>]
let ``NO: transition is Pop`` () =
    let scene = newScene (fun () -> ())
    pressDown scene |> ignore
    let t = pressA scene
    Assert.Equal(Pop, t)

// ── B cancel path ─────────────────────────────────────────────────────────────

[<Fact>]
let ``B: onSave is not called`` () =
    let count = ref 0
    let scene = newScene (fun () -> incr count)
    pressB scene |> ignore
    Assert.Equal(0, !count)

[<Fact>]
let ``B: transition is Pop`` () =
    let scene = newScene (fun () -> ())
    Assert.Equal(Pop, pressB scene)

// ── Cursor navigation returns Stay ───────────────────────────────────────────

[<Fact>]
let ``Down (cursor to NO) returns Stay`` () =
    let scene = newScene (fun () -> ())
    Assert.Equal(Stay, pressDown scene)

[<Fact>]
let ``Down then Up (back to YES) returns Stay`` () =
    let scene = newScene (fun () -> ())
    pressDown scene |> ignore
    Assert.Equal(Stay, press { Buttons.none with Up = true } scene)

// ── onSave called exactly once even with extra frames after YES ───────────────

[<Fact>]
let ``onSave is not called again on subsequent frames after YES`` () =
    let count = ref 0
    let scene = newScene (fun () -> incr count)
    pressA scene |> ignore   // YES → Push, phase = WaitingToPop
    (scene :> Scene).Update(Buttons.none) |> ignore  // Pop
    // Extra frames must not trigger another save
    (scene :> Scene).Update(Buttons.none) |> ignore
    Assert.Equal(1, !count)

// ── SaveData v3 round-trip (no disk I/O) ─────────────────────────────────────

[<Fact>]
let ``SaveData v3 player block round-trips through serialize/deserialize`` () =
    let save =
        { Version = SaveData.CurrentVersion
          Overworld = { MapId = "AzaleaTown"; CellX = 5; CellY = 8; Facing = "Down" }
          World = { Events = [||]; EngineFlags = [||]; Vars = [||]; Scenes = [||] }
          Bag = [||]
          Player =
            { Name = "RED"
              Money = 12345
              Party = [||]
              PocketedBag = { Items = [||]; Balls = [||]; KeyItems = [||]; TmHm = [||] }
              DexSeen = [| 1; 4; 7 |]
              DexOwn  = [| 4 |]
              Badges  = 3
              Options = { TextSpeed = 3; BoxBorder = 1; Sound = 0 }
              Pc = Unchecked.defaultof<_> } }

    let json = SaveFile.serialize save

    match SaveFile.deserialize json with
    | Some back ->
        let p = SaveData.playerOf back
        Assert.Equal("RED",   p.Name)
        Assert.Equal(12345,   p.Money)
        Assert.Equal(3,       p.Badges)
        Assert.True(p.DexSeen.Contains 4)
        Assert.True(p.DexOwn.Contains  4)
        Assert.False(p.DexOwn.Contains 1)
        Assert.Equal(3, p.Options.TextSpeed)
        Assert.Equal(1, p.Options.BoxBorder)
    | None -> Assert.Fail("expected a readable v3 save")
