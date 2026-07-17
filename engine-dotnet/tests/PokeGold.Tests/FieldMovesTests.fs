module PokeGold.Tests.FieldMovesTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Scenes
open PokeGold.Game.Core
open PokeGold.Game.Audio

type private SilentSound() =
    interface ISoundBoard with
        member _.PlayMusic _ = ()
        member _.PlaySfx _ = ()
        member _.PlayJingle _ = ()
        member _.StopMusic() = ()

let private directionButton direction =
    match direction with
    | Down -> { Buttons.none with Down = true }
    | Up -> { Buttons.none with Up = true }
    | Left -> { Buttons.none with Left = true }
    | Right -> { Buttons.none with Right = true }

let private delta direction =
    match direction with
    | Down -> 0, 1
    | Up -> 0, -1
    | Left -> -1, 0
    | Right -> 1, 0

let private opposite direction =
    match direction with
    | Down -> Up
    | Up -> Down
    | Left -> Right
    | Right -> Left

let private cutTarget content mapId =
    let probe = OverworldState.loadById content mapId
    let tileset = (Maps.byName mapId).Value.Meta.Tileset
    let occupied = probe.Npcs |> Array.map (fun npc -> npc.CellX, npc.CellY) |> Set.ofArray
    let candidates = [ 0, 1, Up; 0, -1, Down; -1, 0, Right; 1, 0, Left ]

    seq {
        for targetY in 0 .. probe.Map.Height * 2 - 1 do
            for targetX in 0 .. probe.Map.Width * 2 - 1 do
                let collision = Movement.collisionIdAtCell probe.Map probe.Collision targetX targetY
                let blockX, blockY = targetX / 2, targetY / 2
                let block = probe.Map.BlockIds.[blockY * probe.Map.Width + blockX]

                match FieldMoves.tryCutReplacement tileset block with
                | Some replacement when collision = FieldMoves.CollCutTree || collision = FieldMoves.CollCutTree1A ->
                    for dx, dy, facing in candidates do
                        let playerX, playerY = targetX + dx, targetY + dy
                        if Movement.cellWalkable probe.Map probe.Collision playerX playerY
                           && not (Set.contains (targetX, targetY) occupied)
                           && not (Set.contains (playerX, playerY) occupied) then
                            yield playerX, playerY, facing, targetX, targetY, block, replacement
                | _ -> ()
    }
    |> Seq.tryHead
    |> Option.defaultWith (fun () -> failwithf "expected a source-backed Cut obstruction on %s" mapId)

let private dismissModal (stack: ResizeArray<Scene>) =
    let mutable frame = 0
    while frame < 2000 && stack.Count > 1 do
        frame <- frame + 1
        let buttons = if frame % 2 = 0 then { Buttons.none with A = true } else Buttons.none
        match stack.[stack.Count - 1].Update buttons with
        | Stay -> ()
        | Push child -> stack.Add child
        | Pop -> stack.RemoveAt(stack.Count - 1)
        | Replace child -> stack.[stack.Count - 1] <- child

    Assert.Equal(1, stack.Count)

let private surfRoute content mapId =
    let probe = OverworldState.loadById content mapId
    let directions = [ Down; Up; Left; Right ]
    let occupied = probe.Npcs |> Array.map (fun npc -> npc.CellX, npc.CellY) |> Set.ofArray

    seq {
        for landY in 0 .. probe.Map.Height * 2 - 1 do
            for landX in 0 .. probe.Map.Width * 2 - 1 do
                if Movement.cellWalkable probe.Map probe.Collision landX landY
                   && not (Set.contains (landX, landY) occupied) then
                    for enterDirection in directions do
                        let enterDx, enterDy = delta enterDirection
                        let waterX, waterY = landX + enterDx, landY + enterDy
                        let waterCollision = Movement.collisionIdAtCell probe.Map probe.Collision waterX waterY

                        if FieldMoves.isPassableSurfWater waterCollision
                           && not (Set.contains (waterX, waterY) occupied) then
                            let invalidDirection =
                                directions
                                |> List.tryFind (fun direction ->
                                    let dx, dy = delta direction
                                    let x, y = waterX + dx, waterY + dy
                                    x >= 0 && y >= 0 && x < probe.Map.Width * 2 && y < probe.Map.Height * 2
                                    && not (Movement.cellWalkable probe.Map probe.Collision x y)
                                    && not (Movement.collisionIdAtCell probe.Map probe.Collision x y |> FieldMoves.isPassableSurfWater))

                            match invalidDirection with
                            | Some invalidDirection ->
                                for traverseDirection in directions do
                                    let traverseDx, traverseDy = delta traverseDirection
                                    let nextX, nextY = waterX + traverseDx, waterY + traverseDy
                                    let nextCollision = Movement.collisionIdAtCell probe.Map probe.Collision nextX nextY

                                    if FieldMoves.isPassableSurfWater nextCollision
                                       && not (Set.contains (nextX, nextY) occupied) then
                                        yield landX, landY, enterDirection, waterX, waterY, invalidDirection, traverseDirection, nextX, nextY
                            | None -> ()
    }
    |> Seq.tryHead
    |> Option.defaultWith (fun () -> failwithf "expected a shore and traversable water on %s" mapId)

let private driveTo (scene: OverworldScene) direction target =
    let mutable frames = 0
    while frames < 64
          && ((scene.RuntimeSnapshot.Player.CellX, scene.RuntimeSnapshot.Player.CellY) <> target
              || scene.RuntimeSnapshot.Player.Moving) do
        frames <- frames + 1
        (scene :> Scene).Update(directionButton direction) |> ignore

    Assert.Equal(target, (scene.RuntimeSnapshot.Player.CellX, scene.RuntimeSnapshot.Player.CellY))

[<Fact>]
let ``FieldMoves.canUse returns false without the required badge`` () =
    let mon = { PartyMon.create 155 5 with Moves = [ 15, 30 ] }
    let world = World.empty

    Assert.False(FieldMoves.canUse "CUT" world [ mon ])

[<Fact>]
let ``FieldMoves.canUse returns true when badge and move are present`` () =
    let mon = { PartyMon.create 155 5 with Moves = [ 15, 30 ] }
    let world = World.setFlag "ENGINE_HIVEBADGE" World.empty

    Assert.True(FieldMoves.canUse "CUT" world [ mon ])

[<Fact>]
let ``CUT collision ID is 0x12`` () =
    Assert.Equal(0x12uy, FieldMoves.CollCutTree)

[<Fact>]
let ``canUse CUT requires HIVEBADGE`` () =
    let world = World.empty
    Assert.False(FieldMoves.canUse "CUT" world [])

    let worldWithBadge = World.setFlag "ENGINE_HIVEBADGE" world
    Assert.False(FieldMoves.canUse "CUT" worldWithBadge [])

[<Fact>]
let ``Cut succeeds on cut tree with badge and move`` () =
    let world = World.setFlag "ENGINE_HIVEBADGE" World.empty
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove "CUT" [] }

    match FieldMoves.tryCut 0x12uy world [ mon ] with
    | FieldMoves.Used("CUT", _) -> ()
    | FieldMoves.NotUsable reason -> Assert.Fail(reason)
    | other -> Assert.Fail(sprintf "unexpected result %A" other)

[<Fact>]
let ``Cut fails without badge`` () =
    match FieldMoves.tryCut 0x12uy World.empty [] with
    | FieldMoves.NotUsable _ -> ()
    | FieldMoves.Used _ -> Assert.Fail("should fail without badge")

[<Fact>]
let ``OVR-004 Cut replacement table preserves every source tileset entry`` () =
    let expected =
        [ "TILESET_JOHTO", [ 0x03uy, 0x02uy; 0x5buy, 0x3cuy; 0x5fuy, 0x3duy; 0x63uy, 0x3fuy; 0x67uy, 0x3euy ]
          "TILESET_JOHTO_MODERN", [ 0x03uy, 0x02uy ]
          "TILESET_KANTO", [ 0x0buy, 0x0auy; 0x32uy, 0x6duy; 0x33uy, 0x6cuy; 0x34uy, 0x6fuy; 0x35uy, 0x4cuy; 0x60uy, 0x6euy ]
          "TILESET_PARK", [ 0x13uy, 0x03uy; 0x03uy, 0x04uy ]
          "TILESET_FOREST", [ 0x0fuy, 0x17uy ] ]

    for tileset, replacements in expected do
        for source, replacement in replacements do
            Assert.Equal(Some replacement, FieldMoves.tryCutReplacement tileset source)

[<Theory>]
[<InlineData("IlexForest")>]
[<InlineData("Route2")>]
let ``OVR-004 Cut removes a real obstruction traverses it and resets on reload`` mapId =
    let content = Content()
    let playerX, playerY, facing, targetX, targetY, originalBlock, replacement = cutTarget content mapId
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove "CUT" [] }
    let player = { PlayerStateOps.initial with Party = [ mon ] }
    let world = World.empty |> World.setFlag "ENGINE_HIVEBADGE"
    let scene = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content mapId playerX playerY facing)
    scene.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    match (scene :> Scene).Update { Buttons.none with A = true } with
    | Push modal -> stack.Add modal
    | other -> Assert.Fail(sprintf "expected Cut text after A press, got %A" other)

    let blockX, blockY = targetX / 2, targetY / 2
    Assert.Equal(replacement, scene.DebugState.Map.BlockIds.[blockY * scene.DebugState.Map.Width + blockX])
    Assert.True(Movement.cellWalkable scene.DebugState.Map scene.DebugState.Collision targetX targetY)

    dismissModal stack
    let restored = OverworldScene.OfSave(content, SilentSound(), scene.Capture())
    Assert.Equal(originalBlock, restored.DebugState.Map.BlockIds.[blockY * restored.DebugState.Map.Width + blockX])

    let mutable frames = 0
    while frames < 32 && (scene.RuntimeSnapshot.Player.CellX, scene.RuntimeSnapshot.Player.CellY) <> (targetX, targetY) do
        frames <- frames + 1
        (scene :> Scene).Update(directionButton facing) |> ignore

    Assert.Equal((targetX, targetY), (scene.RuntimeSnapshot.Player.CellX, scene.RuntimeSnapshot.Player.CellY))

[<Fact>]
let ``OVR-004 Cut leaves Ilex Forest blocked without badge or party move`` () =
    let content = Content()
    let playerX, playerY, facing, targetX, targetY, originalBlock, _ = cutTarget content "IlexForest"
    let tryBlocked world player =
        let scene = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "IlexForest" playerX playerY facing)
        scene.Restore(world, player)
        (scene :> Scene).Update { Buttons.none with A = true } |> ignore
        let blockX, blockY = targetX / 2, targetY / 2
        Assert.Equal(originalBlock, scene.DebugState.Map.BlockIds.[blockY * scene.DebugState.Map.Width + blockX])
        Assert.False(Movement.cellWalkable scene.DebugState.Map scene.DebugState.Collision targetX targetY)

    let cutter = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove "CUT" [] }
    tryBlocked World.empty { PlayerStateOps.initial with Party = [ cutter ] }
    tryBlocked (World.empty |> World.setFlag "ENGINE_HIVEBADGE") PlayerStateOps.initial

[<Fact>]
let ``OVR-005 Surf enters traverses dismounts and restores legally from save`` () =
    let content = Content()
    let landX, landY, enterDirection, waterX, waterY, invalidDirection, traverseDirection, nextX, nextY = surfRoute content "NewBarkTown"
    let surfer = { PartyMon.create 158 10 with Moves = MoveLearn.tryLearnMove "SURF" [] }
    let player = { PlayerStateOps.initial with Party = [ surfer ] }
    let world = World.empty |> World.setFlag "ENGINE_FOGBADGE"
    let scene = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "NewBarkTown" landX landY enterDirection)
    scene.Restore(world, player)

    let stack = ResizeArray<Scene>()
    stack.Add(scene :> Scene)
    match (scene :> Scene).Update { Buttons.none with A = true } with
    | Push modal -> stack.Add modal
    | other -> Assert.Fail(sprintf "expected Surf text after A press, got %A" other)

    Assert.Equal((waterX, waterY), (scene.RuntimeSnapshot.Player.CellX, scene.RuntimeSnapshot.Player.CellY))
    Assert.Equal(1, World.getVar "__surfing" scene.DebugWorld)
    Assert.Equal(content.Sprite "surf", scene.DebugState.Sprite)

    dismissModal stack
    let restored = OverworldScene.OfSave(content, SilentSound(), scene.Capture())
    Assert.Equal((waterX, waterY), (restored.RuntimeSnapshot.Player.CellX, restored.RuntimeSnapshot.Player.CellY))
    Assert.Equal(1, World.getVar "__surfing" restored.DebugWorld)
    Assert.Equal(content.Sprite "surf", restored.DebugState.Sprite)

    for _ in 1 .. 40 do
        (scene :> Scene).Update(directionButton invalidDirection) |> ignore
    Assert.Equal((waterX, waterY), (scene.RuntimeSnapshot.Player.CellX, scene.RuntimeSnapshot.Player.CellY))

    driveTo scene traverseDirection (nextX, nextY)
    driveTo scene (opposite traverseDirection) (waterX, waterY)
    driveTo scene (opposite enterDirection) (landX, landY)

    Assert.Equal(0, World.getVar "__surfing" scene.DebugWorld)
    Assert.Equal(content.Sprite "chris", scene.DebugState.Sprite)

[<Fact>]
let ``OVR-005 Pikachu Surf uses the source alternate overworld sprite`` () =
    let content = Content()
    let landX, landY, enterDirection, _, _, _, _, _, _ = surfRoute content "NewBarkTown"
    let pikachu = { PartyMon.create 25 10 with Moves = MoveLearn.tryLearnMove "SURF" [] }
    let player = { PlayerStateOps.initial with Party = [ pikachu ] }
    let world = World.empty |> World.setFlag "ENGINE_FOGBADGE"
    let scene = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "NewBarkTown" landX landY enterDirection)
    scene.Restore(world, player)

    (scene :> Scene).Update { Buttons.none with A = true } |> ignore

    Assert.Equal(1, World.getVar "__surfing" scene.DebugWorld)
    Assert.Equal(1, World.getVar "__surfing_pikachu" scene.DebugWorld)
    Assert.Equal(content.Sprite "surfing_pikachu", scene.DebugState.Sprite)

[<Theory>]
[<InlineData("CUT", "ENGINE_HIVEBADGE", 0x12)>]
[<InlineData("SURF", "ENGINE_FOGBADGE", 0x29)>]
[<InlineData("WHIRLPOOL", "ENGINE_GLACIERBADGE", 0x24)>]
[<InlineData("WATERFALL", "ENGINE_RISINGBADGE", 0x33)>]
let ``tile field moves require matching terrain`` (move: string) (badge: string) (collId: int) =
    let world = World.empty |> World.setFlag badge
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove move [] }

    match FieldMoves.tryUse move (byte collId) "Route36" world [ mon ] with
    | FieldMoves.Used(used, _) -> Assert.Equal(move, used)
    | FieldMoves.NotUsable reason -> Assert.Fail(reason)

[<Theory>]
[<InlineData("CUT", "ENGINE_HIVEBADGE")>]
[<InlineData("SURF", "ENGINE_FOGBADGE")>]
[<InlineData("STRENGTH", "ENGINE_PLAINBADGE")>]
[<InlineData("FLASH", "ENGINE_ZEPHYRBADGE")>]
[<InlineData("WHIRLPOOL", "ENGINE_GLACIERBADGE")>]
[<InlineData("WATERFALL", "ENGINE_RISINGBADGE")>]
let ``HM blockers report missing badge before move use`` (move: string) (badge: string) =
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove move [] }

    match FieldMoves.tryUse move FieldMoves.CollSurf "Route36" World.empty [ mon ] with
    | FieldMoves.NotUsable reason -> Assert.Contains(badge.Replace("ENGINE_", "").Replace("BADGE", "BADGE"), reason)
    | FieldMoves.Used _ -> Assert.Fail("should require badge")

[<Fact>]
let ``Fly requires a known fly point`` () =
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove "FLY" [] }
    let badgeOnly = World.empty |> World.setFlag "ENGINE_STORMBADGE"

    match FieldMoves.tryUse "FLY" 0uy "GoldenrodCity" badgeOnly [ mon ] with
    | FieldMoves.NotUsable reason -> Assert.Contains("FLY destination", reason)
    | FieldMoves.Used _ -> Assert.Fail("should require a flypoint")

    let withFlypoint = badgeOnly |> World.setFlag "ENGINE_FLYPOINT_GOLDENROD"
    match FieldMoves.tryUse "FLY" 0uy "GoldenrodCity" withFlypoint [ mon ] with
    | FieldMoves.Used("FLY", _) -> ()
    | other -> Assert.Fail(sprintf "expected FLY to be usable, got %A" other)

[<Fact>]
let ``PartyScene dispatches detected HM field move`` () =
    let mutable fieldMove = None
    let mon = { PartyMon.create 155 10 with Moves = MoveLearn.tryLearnMove "CUT" [] }
    let player = { PlayerStateOps.initial with Party = [ mon ] }
    let scene =
        PartyScene(
            Content(),
            player,
            ignore,
            onFieldMove = (fun move -> fieldMove <- Some move; Stay))

    let update buttons = (scene :> Scene).Update buttons
    update { Buttons.none with A = true } |> ignore
    update Buttons.none |> ignore

    for _ in 1 .. 3 do
        update { Buttons.none with Down = true } |> ignore
        update Buttons.none |> ignore

    update { Buttons.none with A = true } |> ignore

    Assert.Equal(Some "CUT", fieldMove)

[<Fact>]
let ``Repel.blocks suppresses weak encounters when lead mon is strong enough`` () =
    let lead = PartyMon.create 155 10
    let player = { PlayerStateOps.initial with Party = [ lead ]; RepelSteps = 50 }

    Assert.True(PokeGold.Game.Player.Repel.blocks player 5)
    Assert.False(PokeGold.Game.Player.Repel.blocks player 15)
