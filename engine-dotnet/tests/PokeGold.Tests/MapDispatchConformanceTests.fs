module PokeGold.Tests.MapDispatchConformanceTests

open Xunit
open PokeGold.Game.Audio
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Scenes

type private SilentSound() =
    interface ISoundBoard with
        member _.PlayMusic _ = ()
        member _.PlaySfx _ = ()
        member _.PlayJingle _ = ()
        member _.StopMusic() = ()

let private aPress = { Buttons.none with A = true }

let private interact mapId x y facing world player =
    let content = Content()
    let state = OverworldState.loadByIdAt content mapId x y facing
    let scene = OverworldScene(content, SilentSound(), state)
    scene.Restore(world, player)
    scene, (scene :> Scene).Update aPress

let private assertTextBox =
    function
    | Push (:? TextBoxScene) -> ()
    | transition -> Assert.Fail($"expected source script to open text, got {transition}")

[<Fact>]
let ``OBJECTTYPE_SCRIPT, TRAINER, and ITEMBALL dispatch their generated object scripts`` () =
    let scriptScene, scriptTransition =
        interact "AzaleaMart" 2 6 Up World.empty PlayerStateOps.initial
    assertTextBox scriptTransition
    Assert.Equal(Some "AzaleaMartCooltrainerMText", scriptScene.RuntimeSnapshot.LastTextLabel)

    let trainerScene, trainerTransition =
        interact "AzaleaGym" 8 9 Up World.empty PlayerStateOps.initial
    assertTextBox trainerTransition
    Assert.Equal(Some "BugCatcherAlSeenText", trainerScene.RuntimeSnapshot.LastTextLabel)

    let itemScene, itemTransition =
        interact "DarkCaveVioletEntrance" 6 9 Up World.empty PlayerStateOps.initial
    assertTextBox itemTransition
    Assert.Equal(1, Bag.count "POTION" itemScene.DebugPlayer.Bag)

[<Fact>]
let ``BGEVENT_READ dispatches the generated Azalea Town sign script`` () =
    let scene, transition =
        interact "AzaleaTown" 19 10 Up World.empty PlayerStateOps.initial
    assertTextBox transition
    Assert.Equal(Some "AzaleaTownSignText", scene.RuntimeSnapshot.LastTextLabel)

[<Fact>]
let ``BGEVENT_IFSET and IFNOTSET gate their generated background scripts by source event polarity`` () =
    let posterScene, posterTransition =
        interact
            "PlayersHouse2F"
            6
            1
            Up
            (World.setEvent "EVENT_PLAYERS_ROOM_POSTER" World.empty)
            PlayerStateOps.initial
    assertTextBox posterTransition
    Assert.Equal(Some "LookTownMapText", posterScene.RuntimeSnapshot.LastTextLabel)

    let content = Content()
    let blockedPosterScene =
        OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "PlayersHouse2F" 6 1 Up)
    blockedPosterScene.Restore(World.empty, PlayerStateOps.initial)
    blockedPosterScene.DebugSetEvent "EVENT_PLAYERS_ROOM_POSTER" false
    let blockedPosterTransition = (blockedPosterScene :> Scene).Update aPress
    Assert.Equal(Stay, blockedPosterTransition)
    Assert.Equal(None, blockedPosterScene.RuntimeSnapshot.LastTextLabel)

    let doorScene, doorTransition =
        interact "TeamRocketBaseB2F" 14 13 Up World.empty PlayerStateOps.initial
    assertTextBox doorTransition
    Assert.Equal(Some "RocketBaseDoorNoPasswordText", doorScene.RuntimeSnapshot.LastTextLabel)

    let openedDoorScene, openedDoorTransition =
        interact
            "TeamRocketBaseB2F"
            14
            13
            Up
            (World.setEvent "EVENT_OPENED_DOOR_TO_ROCKET_HIDEOUT_TRANSMITTER" World.empty)
            PlayerStateOps.initial
    Assert.Equal(Stay, openedDoorTransition)
    Assert.Equal(None, openedDoorScene.RuntimeSnapshot.LastTextLabel)

[<Fact>]
let ``BGEVENT_UP, LEFT, and RIGHT dispatch their generated facing-specific scripts`` () =
    let vendingScene, vendingTransition =
        interact "CeladonDeptStore6F" 8 2 Up World.empty PlayerStateOps.initial
    assertTextBox vendingTransition
    Assert.Equal(Some "CeladonVendingText", vendingScene.RuntimeSnapshot.LastTextLabel)

    let player =
        { PlayerStateOps.initial with
            Bag = Bag.add "COIN_CASE" 1 PlayerStateOps.initial.Bag
            Coins = 3 }

    let leftScene, leftTransition =
        interact "CeladonGameCorner" 2 11 Left World.empty player
    Assert.Equal(Stay, leftTransition)
    Assert.True([ 0; 6 ] |> List.contains leftScene.DebugPlayer.Coins)

    let rightScene, rightTransition =
        interact "CeladonGameCorner" 5 11 Right World.empty player
    Assert.Equal(Stay, rightTransition)
    Assert.True([ 0; 6 ] |> List.contains rightScene.DebugPlayer.Coins)

[<Fact>]
let ``MAPCALLBACK_NEWMAP, TILES, and OBJECTS apply their generated map-entry effects`` () =
    let content = Content()

    let newMap = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "AzaleaTown" 19 10 Up)
    newMap.Restore(World.empty, PlayerStateOps.initial)
    Assert.True(World.hasFlag "ENGINE_FLYPOINT_AZALEA" newMap.DebugWorld)

    let tilesState = OverworldState.loadByIdAt content "GoldenrodDeptStoreB1F" 2 2 Down
    let tiles = OverworldScene(content, SilentSound(), tilesState)
    tiles.Restore(World.empty, PlayerStateOps.initial)
    Assert.Equal(0x0duy, Map.blockAt tiles.DebugState.Map 5 4)

    let objects = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "DayCare" 2 5 Down)
    objects.Restore(World.empty, PlayerStateOps.initial)
    Assert.False(World.hasEvent "EVENT_DAY_CARE_MAN_IN_DAY_CARE" objects.DebugWorld)
    Assert.True(objects.RuntimeSnapshot.Actors |> List.exists (fun actor -> actor.Script = "DayCareManScript_Inside" && actor.Visible))

    let eggWorld = World.setFlag "ENGINE_DAY_CARE_MAN_HAS_EGG" World.empty
    let eggObjects = OverworldScene(content, SilentSound(), OverworldState.loadByIdAt content "DayCare" 2 5 Down)
    eggObjects.Restore(eggWorld, PlayerStateOps.initial)
    Assert.True(World.hasEvent "EVENT_DAY_CARE_MAN_IN_DAY_CARE" eggObjects.DebugWorld)
    Assert.True(eggObjects.RuntimeSnapshot.Actors |> List.exists (fun actor -> actor.Script = "DayCareManScript_Inside" && not actor.Visible))
