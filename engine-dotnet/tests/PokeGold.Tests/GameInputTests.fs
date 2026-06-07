module PokeGold.Tests.GameInputTests

open Xunit
open PokeGold.Game
open PokeGold.Game.Core
open PokeGold.Game.Debug

// ── Buttons masking helpers (pure) ───────────────────────────────────────────

[<Fact>]
let ``intersect keeps only buttons held in both frames`` () =
    let a = { Buttons.none with A = true; Start = true; Down = true }
    let b = { Buttons.none with A = true; Down = false; Start = true }
    let r = Buttons.intersect a b
    Assert.True(r.A)
    Assert.True(r.Start)
    Assert.False(r.Down)
    Assert.False(r.B)

[<Fact>]
let ``except clears every button present in the mask`` () =
    let a = { Buttons.none with A = true; B = true; Up = true }
    let mask = { Buttons.none with A = true; Up = true }
    let r = Buttons.except a mask
    Assert.False(r.A)   // masked
    Assert.False(r.Up)  // masked
    Assert.True(r.B)    // not masked, still held

[<Fact>]
let ``except with an empty mask is identity`` () =
    let a = { Buttons.none with A = true; Left = true }
    Assert.Equal(a, Buttons.except a Buttons.none)

// ── Game-level debounce: the press that opens a scene must not leak into it ───

/// The active (top) scene type, read through the debug command bridge.
let private topScene (g: Game) = g.RunDebugCommand "scene"

let private assertApplied =
    function
    | Applied -> ()
    | Rejected reason -> failwith reason

let private startMainMenu () =
    let g = Game()
    g.Tick({ Buttons.none with Start = true })
    g.Tick(Buttons.none)
    g

[<Fact>]
let ``new Game starts at title without an implicit debug overworld`` () =
    let g = Game()
    Assert.Equal("TitleScene", topScene g)
    Assert.Contains("no overworld scene active", g.RunDebugCommand "player")

[<Fact>]
let ``debug Azalea boot is explicit`` () =
    let g = Game()
    Assert.Equal("ok: debug Azalea overworld loaded", g.RunDebugCommand "debug-azalea")
    Assert.Equal("OverworldScene", topScene g)
    Assert.Contains("map     AzaleaTown", g.RunDebugCommand "player")

[<Fact>]
let ``typed runtime snapshot starts at title without overworld`` () =
    let g = Game()
    let snap = g.Snapshot

    Assert.Equal("TitleScene", snap.TopScene)
    Assert.Equal<string list>([ "TitleScene" ], snap.SceneStack)
    Assert.True(Option.isNone snap.Overworld)

[<Fact>]
let ``typed runtime control can boot and inspect debug Azalea`` () =
    let g = Game()
    g.ApplyControl LoadDebugAzalea |> assertApplied

    let snap = g.Snapshot
    let ow = snap.Overworld |> Option.defaultWith (fun () -> failwith "expected overworld snapshot")

    Assert.Equal("OverworldScene", snap.TopScene)
    Assert.Equal("AzaleaTown", ow.MapId)
    Assert.True(ow.Actors |> List.exists (fun actor -> actor.Visible))

[<Fact>]
let ``typed runtime input drives the real tick path`` () =
    let g = Game()
    g.ApplyControl(Press { Buttons.none with Start = true }) |> assertApplied
    g.Tick Buttons.none

    Assert.Equal("MainMenuScene", g.Snapshot.TopScene)

[<Fact>]
let ``holding Start keeps the main menu open instead of flickering`` () =
    let g = startMainMenu ()
    let start = { Buttons.none with Start = true }

    Assert.Equal("MainMenuScene", topScene g)

    g.Tick(start)                                    // Start still held → masked
    Assert.Equal("MainMenuScene", topScene g)

    g.Tick(start)                                    // and again while held
    Assert.Equal("MainMenuScene", topScene g)

[<Fact>]
let ``releasing then re-pressing Start keeps the main menu open`` () =
    let g = startMainMenu ()
    let start = { Buttons.none with Start = true }

    Assert.Equal("MainMenuScene", topScene g)
    g.Tick(Buttons.none)                             // release input
    Assert.Equal("MainMenuScene", topScene g)
    g.Tick(start)                                    // fresh press does not close the menu
    Assert.Equal("MainMenuScene", topScene g)

[<Fact>]
let ``the A that opens the naming screen does not bleed into it`` () =
    let g = startMainMenu ()

    g.Tick({ Buttons.none with A = true })           // A opens the naming screen
    let child = topScene g
    Assert.True(child <> "MainMenuScene")           // a child scene is now on top

    g.Tick({ Buttons.none with A = true })           // A still held → masked
    Assert.Equal(child, topScene g)                  // child stays open, no bleed
