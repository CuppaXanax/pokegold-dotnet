module PokeGold.Tests.PokedexTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Scenes

// ── Test fixtures ──────────────────────────────────────────────────────────────
//
// Player state used by most tests:
//   DexSeen: species 1 (Bulbasaur), 4 (Charmander), 25 (Pikachu)
//   DexOwn:  species 1 (Bulbasaur), 25 (Pikachu)
//
// Species 5 (Charmeleon) is NOT in DexSeen — used for the "unseen" branch.

let private makePlayer () : PlayerState =
    { PlayerStateOps.initial with
        DexSeen = Set.ofList [ 1; 4; 25 ]
        DexOwn  = Set.ofList [ 1; 25 ] }

let private makeScene () = PokedexScene(Content(), makePlayer ())

let private update (scene: PokedexScene) (b: Buttons) : Transition =
    (scene :> Scene).Update(b)

/// Simulate one button press: one frame held (rising edge) + one frame released.
let private press (b: Buttons) (scene: PokedexScene) : Transition =
    let t = update scene b
    update scene Buttons.none |> ignore
    t

let private pressDown s = press { Buttons.none with Down = true } s
let private pressUp   s = press { Buttons.none with Up   = true } s
let private pressA    s = press { Buttons.none with A    = true } s
let private pressB    s = press { Buttons.none with B    = true } s
let private pressRight s = press { Buttons.none with Right = true } s
let private pressStart s = press { Buttons.none with Start = true } s

let private anyPixelIn (fb: Framebuffer) (x0: int) (y0: int) (x1: int) (y1: int) =
    seq {
        for y in y0 .. y1 do
            for x in x0 .. x1 do
                let b = (y * Display.Width + x) * 4
                yield fb.Pixels.[b] <> 0uy || fb.Pixels.[b + 1] <> 0uy || fb.Pixels.[b + 2] <> 0uy
    }
    |> Seq.exists id

let private assertHasSource species (sourceFragment: string) proof =
    let sources = PokedexObtainability.sourcesFor species proof
    Assert.Contains(sources, fun (source: string) -> source.Contains(sourceFragment))

// ── Pokedex pure helpers ────────────────────────────────────────────────────────

[<Fact>]
let ``Pokedex.seenCount matches DexSeen set size`` () =
    let player = makePlayer ()
    Assert.Equal(player.DexSeen.Count, Pokedex.seenCount player)

[<Fact>]
let ``Pokedex.ownCount matches DexOwn set size`` () =
    let player = makePlayer ()
    Assert.Equal(player.DexOwn.Count, Pokedex.ownCount player)

[<Fact>]
let ``Pokedex.seenCount is 3 for seeded player`` () =
    Assert.Equal(3, Pokedex.seenCount (makePlayer ()))

[<Fact>]
let ``Pokedex.ownCount is 2 for seeded player`` () =
    Assert.Equal(2, Pokedex.ownCount (makePlayer ()))

[<Fact>]
let ``Pokedex.rowLabel unseen entry shows masked name`` () =
    let player = makePlayer ()
    let label = Pokedex.rowLabel player 5   // Charmeleon — not in DexSeen
    Assert.Contains("#005", label)
    Assert.Contains("-", label)             // masked placeholder
    Assert.DoesNotContain("CHARMELEON", label)

[<Fact>]
let ``Pokedex.rowLabel owned entry shows species name with owned marker`` () =
    let player = makePlayer ()
    let label = Pokedex.rowLabel player 1   // Bulbasaur — in DexOwn
    Assert.Contains("#001", label)
    Assert.Contains("BULBASAUR", label)
    Assert.Contains("*", label)             // owned marker

[<Fact>]
let ``Pokedex.rowLabel seen-not-owned entry shows name without owned marker`` () =
    let player = makePlayer ()
    let label = Pokedex.rowLabel player 4   // Charmander — seen, not owned
    Assert.Contains("#004", label)
    Assert.Contains("CHARMANDE", label)     // 9-char truncation of "CHARMANDER"
    Assert.DoesNotContain("*", label)       // no owned marker

[<Fact>]
let ``Pokedex.heightLabel returns feet-and-inches string from packed value`` () =
    // Pikachu: HeightDm = 104 → feet = 1, inches = 04 → "1'04\""
    let label = Pokedex.heightLabel 104
    Assert.Contains("'", label)             // feet separator
    Assert.Contains("\"", label)            // inches mark
    Assert.Contains("1", label)             // 1 foot

[<Fact>]
let ``Pokedex.heightLabel Bulbasaur 204 gives 2 feet 4 inches`` () =
    let label = Pokedex.heightLabel 204
    Assert.StartsWith("2'04", label)

[<Fact>]
let ``Pokedex.weightLabel returns pound value from tenths-of-a-pound`` () =
    // 150 tenths = 15.0 lb  (Bulbasaur)
    let label = Pokedex.weightLabel 150
    Assert.Contains("15", label)
    Assert.Contains("lb", label)

[<Fact>]
let ``Pokedex.weightLabel 2000 gives 200.0 lb`` () =
    let label = Pokedex.weightLabel 2000
    Assert.Contains("200", label)
    Assert.Contains("lb", label)

[<Fact>]
let ``Pokedex.descLines splits on LINE and NEXT tokens`` () =
    let desc = "Line one.<LINE>Line two.<NEXT>Line three."
    let lines = Pokedex.descLines desc
    Assert.Equal(3, lines.Length)
    Assert.Equal("Line one.", lines.[0])
    Assert.Equal("Line two.", lines.[1])
    Assert.Equal("Line three.", lines.[2])

[<Fact>]
let ``Pokedex.descLines strips DONE token`` () =
    let desc = "Some text.<DONE>"
    let lines = Pokedex.descLines desc
    Assert.Equal(1, lines.Length)
    Assert.Equal("Some text.", lines.[0])

[<Fact>]
let ``Pokedex search type labels follow disassembly order`` () =
    Assert.Equal("----", Pokedex.searchTypeLabel 0)
    Assert.Equal("NORMAL", Pokedex.searchTypeLabel 1)
    Assert.Equal("FIRE", Pokedex.searchTypeLabel 2)
    Assert.Equal("GRASS", Pokedex.searchTypeLabel 4)
    Assert.Equal("STEEL", Pokedex.searchTypeLabel Pokedex.searchTypeCount)

[<Fact>]
let ``Pokedex search results include only owned matching species`` () =
    let player = makePlayer ()
    Assert.Equal<int list>([ 1 ], Pokedex.searchResults player 4 0)  // GRASS -> owned Bulbasaur
    Assert.Equal<int list>([ 1 ], Pokedex.searchResults player 4 8)  // GRASS + POISON -> Bulbasaur
    Assert.Equal<int list>([ 25 ], Pokedex.searchResults player 5 0) // ELECTRIC -> owned Pikachu

[<Fact>]
let ``Pokedex area locations are derived from wild encounter nests`` () =
    let locations = Pokedex.areaLocationsForDexNum 79 // Slowpoke
    Assert.Contains("SLOWPOKE_WELL_B1F", locations)
    Assert.Contains("SLOWPOKE_WELL_B2F", locations)

[<Fact>]
let ``All 251 species are obtainable in implementation`` () =
    let proof = PokedexObtainability.buildProof ()
    let missing = PokedexObtainability.missingSpecies proof

    Assert.Equal(251, Species.all.Count)

    if missing <> [] then
        let formatted =
            missing
            |> List.map (fun (dex, species) -> sprintf "#%03d %s" dex species)
            |> String.concat ", "

        failwithf "Unobtainable species in implementation: %s" formatted

    assertHasSource "CORSOLA" "fishing" proof
    assertHasSource "HERACROSS" "headbutt" proof
    assertHasSource "SCYTHER" "bug-contest" proof
    assertHasSource "PICHU" "breeding" proof
    assertHasSource "SCIZOR" "evolution:SCYTHER:EVOLVE_TRADE" proof
    assertHasSource "CELEBI" "built-in-event" proof

[<Fact>]
let ``Pokedex dexSpritePath uses front pic for seen species and question mark for unseen`` () =
    let player = makePlayer ()
    Assert.Equal("gfx/pokemon/bulbasaur/front_gold.png", Pokedex.dexSpritePath player 1)
    Assert.Equal("gfx/pokedex/question_mark.png", Pokedex.dexSpritePath player 5)

// ── PokedexScene cursor & list ─────────────────────────────────────────────────

[<Fact>]
let ``PokedexScene initial mode is DexList`` () =
    Assert.Equal(DexList, makeScene().Mode)

[<Fact>]
let ``PokedexScene initial cursor is 0`` () =
    Assert.Equal(0, makeScene().Cursor)

[<Fact>]
let ``PokedexScene Down moves cursor to 1`` () =
    let scene = makeScene ()
    pressDown scene |> ignore
    Assert.Equal(1, scene.Cursor)

[<Fact>]
let ``PokedexScene Down six times reaches cursor 6`` () =
    let scene = makeScene ()
    for _ in 1 .. 6 do pressDown scene |> ignore
    Assert.Equal(6, scene.Cursor)

[<Fact>]
let ``PokedexScene Down scrolls MenuList window`` () =
    let scene = makeScene ()
    // Press Down 8 times — the window (visible=7) should scroll once.
    for _ in 1 .. 8 do pressDown scene |> ignore
    Assert.Equal(8, scene.Cursor)
    Assert.True(scene.MenuState.Top >= 2, "Window should have scrolled")

[<Fact>]
let ``PokedexScene Up from cursor 0 stays at 0 (no wrap)`` () =
    let scene = makeScene ()
    pressUp scene |> ignore
    Assert.Equal(0, scene.Cursor)

[<Fact>]
let ``PokedexScene Down returns Stay`` () =
    Assert.Equal(Stay, pressDown (makeScene ()))

[<Fact>]
let ``PokedexScene B returns Pop from list mode`` () =
    Assert.Equal(Pop, pressB (makeScene ()))

// ── PokedexScene mode transitions ─────────────────────────────────────────────

[<Fact>]
let ``PokedexScene A on owned entry (cursor 0 = #1 Bulbasaur) enters DexDetail`` () =
    // makePlayer has DexOwn = [1; 25]; cursor 0 → entry #1.
    let scene = makeScene ()
    pressA scene |> ignore
    match scene.Mode with
    | DexDetail 1 -> ()
    | other -> Assert.Fail(sprintf "Expected DexDetail 1, got %A" other)

[<Fact>]
let ``PokedexScene A on unseen entry stays in DexList`` () =
    // Navigate to cursor 4 → entry #5 (Charmeleon), which is NOT in DexSeen.
    let scene = makeScene ()
    for _ in 1 .. 4 do pressDown scene |> ignore
    Assert.Equal(4, scene.Cursor)
    pressA scene |> ignore
    Assert.Equal(DexList, scene.Mode)

[<Fact>]
let ``PokedexScene A on seen-not-owned entry enters DexDetail`` () =
    // Navigate to cursor 3 → entry #4 (Charmander), seen but not owned.
    let scene = makeScene ()
    for _ in 1 .. 3 do pressDown scene |> ignore
    pressA scene |> ignore
    match scene.Mode with
    | DexDetail 4 -> ()
    | other -> Assert.Fail(sprintf "Expected DexDetail 4, got %A" other)

[<Fact>]
let ``PokedexScene B from DexDetail returns to DexList`` () =
    let scene = makeScene ()
    pressA scene |> ignore   // enter DexDetail 1 (owned)
    pressB scene |> ignore   // B → back to list
    Assert.Equal(DexList, scene.Mode)

[<Fact>]
let ``PokedexScene A in DexDetail returns Stay`` () =
    let scene = makeScene ()
    pressA scene |> ignore   // enter DexDetail
    Assert.Equal(Stay, pressA scene)

[<Fact>]
let ``PokedexScene B in DexDetail returns Stay`` () =
    let scene = makeScene ()
    pressA scene |> ignore   // enter DexDetail
    Assert.Equal(Stay, pressB scene)

[<Fact>]
let ``PokedexScene Start opens search screen with NORMAL and blank defaults`` () =
    let scene = makeScene ()
    pressStart scene |> ignore
    match scene.Mode with
    | DexSearch state ->
        Assert.Equal(1, state.Type1)
        Assert.Equal(0, state.Type2)
        Assert.Equal(0, state.Cursor)
    | other -> Assert.Fail(sprintf "Expected DexSearch, got %A" other)

[<Fact>]
let ``PokedexScene search begin shows owned type matches`` () =
    let scene = makeScene ()
    pressStart scene |> ignore
    for _ in 1 .. 3 do pressRight scene |> ignore // NORMAL -> GRASS
    pressDown scene |> ignore
    pressDown scene |> ignore
    pressA scene |> ignore
    match scene.Mode with
    | DexSearchResults state ->
        Assert.Equal(4, state.Type1)
        Assert.Equal<int list>([ 1 ], scene.SearchResults)
    | other -> Assert.Fail(sprintf "Expected DexSearchResults, got %A" other)

[<Fact>]
let ``PokedexScene search without matches stays on search with not-found message`` () =
    let scene = makeScene ()
    pressStart scene |> ignore
    pressDown scene |> ignore
    pressDown scene |> ignore
    pressA scene |> ignore
    match scene.Mode with
    | DexSearch state -> Assert.Equal(1, state.Type1)
    | other -> Assert.Fail(sprintf "Expected DexSearch, got %A" other)
    Assert.Equal(Some "The specified type was not found.", scene.SearchMessage)

[<Fact>]
let ``PokedexScene detail AREA opens nest screen and returns to detail`` () =
    let scene = makeScene ()
    pressA scene |> ignore
    pressRight scene |> ignore
    Assert.Equal(1, scene.DetailAction)
    pressA scene |> ignore
    match scene.Mode with
    | DexArea 1 -> ()
    | other -> Assert.Fail(sprintf "Expected DexArea 1, got %A" other)
    pressB scene |> ignore
    match scene.Mode with
    | DexDetail 1 -> ()
    | other -> Assert.Fail(sprintf "Expected DexDetail 1, got %A" other)

// ── Render smoke tests ─────────────────────────────────────────────────────────

[<Fact>]
let ``PokedexScene renders non-zero pixels in list mode`` () =
    let scene = makeScene ()
    let fb = Framebuffer()
    (scene :> Scene).Render(fb)
    let mutable any = false
    for i in 0 .. Display.Width * Display.Height - 1 do
        let b = i * 4
        if fb.Pixels.[b] <> 0uy || fb.Pixels.[b+1] <> 0uy || fb.Pixels.[b+2] <> 0uy then
            any <- true
    Assert.True(any, "List mode should produce non-zero pixels")

[<Fact>]
let ``PokedexScene renders non-zero pixels in detail mode for owned entry`` () =
    let scene = makeScene ()
    pressA scene |> ignore   // cursor 0 → entry #1 (owned) → DexDetail 1
    let fb = Framebuffer()
    (scene :> Scene).Render(fb)
    let mutable any = false
    for i in 0 .. Display.Width * Display.Height - 1 do
        let b = i * 4
        if fb.Pixels.[b] <> 0uy || fb.Pixels.[b+1] <> 0uy || fb.Pixels.[b+2] <> 0uy then
            any <- true
    Assert.True(any, "Detail mode should produce non-zero pixels")

[<Fact>]
let ``PokedexScene renders dex front sprite in detail mode`` () =
    let scene = makeScene ()
    pressA scene |> ignore
    let fb = Framebuffer()
    (scene :> Scene).Render(fb)
    Assert.True(anyPixelIn fb 112 24 143 63, "Detail sprite region should contain front-pic pixels")

[<Fact>]
let ``PokedexScene renders search results and area screens`` () =
    let scene = makeScene ()
    pressStart scene |> ignore
    for _ in 1 .. 3 do pressRight scene |> ignore
    pressDown scene |> ignore
    pressDown scene |> ignore
    pressA scene |> ignore

    let resultsFb = Framebuffer()
    (scene :> Scene).Render(resultsFb)
    Assert.True(anyPixelIn resultsFb 8 32 151 111, "Search results should render list pixels")

    pressA scene |> ignore
    pressRight scene |> ignore
    pressA scene |> ignore
    let areaFb = Framebuffer()
    (scene :> Scene).Render(areaFb)
    Assert.True(anyPixelIn areaFb 8 24 151 111, "Area screen should render nest text")
