module PokeGold.Tests.GoldenPathStoryGateTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Save

type private GateState =
    { MapId: string
      World: World
      Player: PlayerState
      Texts: string list }

let private content = Content()

let private mapData mapId =
    MapsData.byName mapId
    |> Option.defaultWith (fun () -> failwithf "missing generated map data for %s" mapId)

let private speciesId name =
    Species.all
    |> Map.find name
    |> fun stats -> stats.Dex

let private initialGate =
    { MapId = "PlayersHouse2F"
      World = World.empty
      Player = { PlayerStateOps.initial with Name = "GOLD" }
      Texts = [] }

let private roundTrip (state: GateState) =
    let ow = OverworldState.loadByIdAt content state.MapId 1 1 Down

    let save =
        SaveData.captureWith ow state.World state.Player
        |> SaveFile.serialize
        |> SaveFile.deserialize
        |> Option.defaultWith (fun () -> failwith "checkpoint save should deserialize")

    { state with
        MapId = save.Overworld.MapId
        World = SaveData.worldOf save
        Player = SaveData.playerOf save }

let private applyEffect (mapId: string) (player: PlayerState) (texts: string list) (effect: ScriptEffect) : string * PlayerState * string list * int option =
    match effect with
    | ShowText(label, _) -> mapId, player, label :: texts, None
    | AskYesNo -> mapId, player, texts, Some 1
    | GiveItem(item, qty, _) ->
        mapId, { player with Bag = Bag.add item qty player.Bag }, texts, Some 1
    | TakeItem(item, qty) ->
        let has = Bag.count item player.Bag >= qty
        let player = if has then { player with Bag = Bag.remove item qty player.Bag } else player
        mapId, player, texts, Some(if has then 1 else 0)
    | CheckItem item ->
        mapId, player, texts, Some(if Bag.count item player.Bag > 0 then 1 else 0)
    | GivePoke(species, level, item, _, _) ->
        match Species.all |> Map.tryFind species with
        | Some stats when player.Party.Length < 6 ->
            let mon =
                PartyMon.create stats.Dex level
                |> MoveLearn.seedStartingMoves
                |> fun mon -> { mon with HeldItem = item }

            mapId, { player with Party = player.Party @ [ mon ] }, texts, Some 1
        | _ -> mapId, player, texts, Some 0
    | CheckPoke species ->
        let dex = speciesId species
        mapId, player, texts, Some(if player.Party |> List.exists (fun mon -> mon.SpeciesId = dex) then 1 else 0)
    | AddPhoneContact phone ->
        mapId, { player with PhoneContacts = Set.add phone player.PhoneContacts }, texts, Some 0
    | CheckPhoneContact phone ->
        mapId, player, texts, Some(if Set.contains phone player.PhoneContacts then 1 else 0)
    | AskPhoneNumber phone ->
        mapId, { player with PhoneContacts = Set.add phone player.PhoneContacts }, texts, Some 0
    | HealParty ->
        mapId, { player with Party = Heal.healParty player.Party }, texts, None
    | StartBattle ->
        mapId, player, texts, Some 1
    | OpenScriptMenu _ ->
        mapId, player, texts, Some 1
    | ScriptEffect.Warp(dest, x, y, facing) ->
        let resolved =
            OverworldState.tryWarpExplicit content dest x y facing Down
            |> Option.map (fun s -> s.MapId)
            |> Option.defaultValue mapId
        resolved, player, texts, None
    | _ -> mapId, player, texts, None

let private runMapScript mapId label state =
    let data = mapData mapId
    let step0 = Script.start label state.World data.Script mapId

    let rec loop n mapId player texts (step: ScriptStep) =
        if n <= 0 then
            failwithf "script %s/%s did not complete" mapId label
        else
            match step.Outcome with
            | Completed ->
                { MapId = mapId
                  World = step.World
                  Player = player
                  Texts = state.Texts @ (List.rev texts) }
            | Suspended(vm, effect) ->
                let mapId, player, texts, value = applyEffect mapId player texts effect
                loop (n - 1) mapId player texts (Script.resume value step.World vm)

    loop 1000 mapId state.Player [] step0

let private runCurrent label state = runMapScript state.MapId label state

let private newBarkPrologue () =
    initialGate
    |> runCurrent "PlayersHouse2FInitializeRoomCallback"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_INITIALIZED_EVENTS" s.World)
        s
    |> runMapScript "PlayersHouse1F" "PlayersHouse1FMeetMomScene"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasFlag "ENGINE_POKEGEAR" s.World)
        Assert.True(World.hasFlag "ENGINE_PHONE_CARD" s.World)
        Assert.Equal(1, World.getScene "PlayersHouse1F" s.World)
        Assert.True(World.hasEvent "EVENT_PLAYERS_HOUSE_MOM_1" s.World)
        Assert.False(World.hasEvent "EVENT_PLAYERS_HOUSE_MOM_2" s.World)
        s
    |> runMapScript "ElmsLab" "CyndaquilPokeBallScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_GOT_CYNDAQUIL_FROM_ELM" s.World)
        Assert.True(World.hasEvent "EVENT_GOT_A_POKEMON_FROM_ELM" s.World)
        Assert.Equal(5, World.getScene "ElmsLab" s.World)
        Assert.Equal(1, World.getScene "NEW_BARK_TOWN" s.World)
        Assert.Contains(s.Player.Party, fun mon -> mon.SpeciesId = speciesId "CYNDAQUIL")

        let newBarkScene = MapEvents.sceneAt (World.getScene "NEW_BARK_TOWN" s.World) (mapData "NewBarkTown").Events
        Assert.Equal("SCENE_NEWBARKTOWN_NOOP", newBarkScene)
        Assert.Equal(None, Triggers.coordToFire newBarkScene Set.empty (mapData "NewBarkTown").Events 1 8)
        s

let private mrPokemonLoop () =
    newBarkPrologue ()
    |> runMapScript "MrPokemonsHouse" "MrPokemonsHouseMeetMrPokemonScene"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_GOT_MYSTERY_EGG_FROM_MR_POKEMON" s.World)
        Assert.True(World.hasFlag "ENGINE_POKEDEX" s.World)
        Assert.Equal(1, World.getScene "MrPokemonsHouse" s.World)
        Assert.Equal(1, World.getScene "CHERRYGROVE_CITY" s.World)
        Assert.Equal(3, World.getScene "ELMS_LAB" s.World)
        Assert.Equal(1, Bag.count "MYSTERY_EGG" s.Player.Bag)
        s
    |> runMapScript "CherrygroveCity" "CherrygroveRivalSceneNorth"
    |> roundTrip
    |> fun s ->
        Assert.Equal(0, World.getScene "CherrygroveCity" s.World)
        Assert.Contains("CherrygroveRivalText_YouWon", s.Texts)
        s
    |> runMapScript "ElmsLab" "ElmAfterTheftScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_GAVE_MYSTERY_EGG_TO_ELM" s.World)
        Assert.Equal(0, Bag.count "MYSTERY_EGG" s.Player.Bag)
        Assert.Equal(1, World.getScene "ROUTE_29" s.World)
        Assert.Equal(6, World.getScene "ElmsLab" s.World)
        s
    |> runMapScript "ElmsLab" "AideScript_GiveYouBalls"
    |> roundTrip
    |> fun s ->
        Assert.Equal(5, Bag.count "POKE_BALL" s.Player.Bag)
        Assert.Equal(2, World.getScene "ElmsLab" s.World)
        s

let private violetGate () =
    mrPokemonLoop ()
    |> runMapScript "VioletGym" "VioletGymFalknerScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_FALKNER" s.World)
        Assert.True(World.hasFlag "ENGINE_ZEPHYRBADGE" s.World)
        Assert.True(World.hasEvent "EVENT_GOT_TM31_MUD_SLAP" s.World)
        Assert.Equal(1, Bag.count "TM_MUD_SLAP" s.Player.Bag)
        s

let private azaleaIlexGate () =
    violetGate ()
    |> runMapScript "SlowpokeWellB1F" "TrainerGruntM1"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_CLEARED_SLOWPOKE_WELL" s.World)
        Assert.True(World.hasEvent "EVENT_SLOWPOKE_WELL_KURT" s.World)
        Assert.Equal(1, World.getScene "AZALEA_TOWN" s.World)
        Assert.Equal("KurtsHouse", s.MapId)
        s
    |> runMapScript "AzaleaGym" "AzaleaGymBugsyScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_BUGSY" s.World)
        Assert.True(World.hasFlag "ENGINE_HIVEBADGE" s.World)
        Assert.True(World.hasEvent "EVENT_GOT_TM49_FURY_CUTTER" s.World)
        Assert.Equal(1, Bag.count "TM_FURY_CUTTER" s.Player.Bag)
        s
    |> runMapScript "IlexForest" "IlexForestCharcoalMasterScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_GOT_HM01_CUT" s.World)
        Assert.Equal(1, Bag.count "HM_CUT" s.Player.Bag)
        s

let private goldenrodSudowoodoGate () =
    azaleaIlexGate ()
    |> runMapScript "GoldenrodGym" "GoldenrodGymWhitneyScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_WHITNEY" s.World)
        Assert.True(World.hasEvent "EVENT_MADE_WHITNEY_CRY" s.World)
        Assert.Equal(1, World.getScene "GoldenrodGym" s.World)
        s
    |> runMapScript "GoldenrodGym" "WhitneyCriesScript"
    |> roundTrip
    |> fun s ->
        Assert.False(World.hasEvent "EVENT_MADE_WHITNEY_CRY" s.World)
        Assert.Equal(0, World.getScene "GoldenrodGym" s.World)
        s
    |> runMapScript "GoldenrodGym" "GoldenrodGymWhitneyScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasFlag "ENGINE_PLAINBADGE" s.World)
        Assert.True(World.hasEvent "EVENT_GOT_TM45_ATTRACT" s.World)
        Assert.Equal(1, Bag.count "TM_ATTRACT" s.Player.Bag)
        s
    |> runMapScript "GoldenrodFlowerShop" "FlowerShopTeacherScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_GOT_SQUIRTBOTTLE" s.World)
        Assert.Equal(1, Bag.count "SQUIRTBOTTLE" s.Player.Bag)
        s
    |> runMapScript "Route36" "SudowoodoScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_FOUGHT_SUDOWOODO" s.World)
        Assert.Contains("SudowoodoAttackedText", s.Texts)
        s

let private ecruteakOlivineGate () =
    goldenrodSudowoodoGate ()
    |> runMapScript "EcruteakGym" "EcruteakGymMortyScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_MORTY" s.World)
        Assert.True(World.hasFlag "ENGINE_FOGBADGE" s.World)
        Assert.True(World.hasEvent "EVENT_GOT_TM30_SHADOW_BALL" s.World)
        Assert.Equal(1, Bag.count "TM_SHADOW_BALL" s.Player.Bag)
        Assert.Equal(1, World.getScene "ECRUTEAK_TIN_TOWER_ENTRANCE" s.World)
        s
    |> runMapScript "OlivineLighthouse6F" "OlivineLighthouseJasmine"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_JASMINE_EXPLAINED_AMPHYS_SICKNESS" s.World)
        s
    |> runMapScript "CianwoodPharmacy" "CianwoodPharmacist"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_GOT_SECRETPOTION_FROM_PHARMACY" s.World)
        Assert.Equal(1, Bag.count "SECRETPOTION" s.Player.Bag)
        s
    |> runMapScript "OlivineLighthouse6F" "OlivineLighthouseJasmine"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_JASMINE_RETURNED_TO_GYM" s.World)
        Assert.False(World.hasEvent "EVENT_OLIVINE_GYM_JASMINE" s.World)
        Assert.Equal(0, Bag.count "SECRETPOTION" s.Player.Bag)
        s
    |> runMapScript "CianwoodGym" "CianwoodGymChuckScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_CHUCK" s.World)
        Assert.True(World.hasFlag "ENGINE_STORMBADGE" s.World)
        Assert.True(World.hasEvent "EVENT_GOT_TM01_DYNAMICPUNCH" s.World)
        Assert.Equal(1, Bag.count "TM_DYNAMICPUNCH" s.Player.Bag)
        s
    |> runMapScript "OlivineGym" "OlivineGymJasmineScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_JASMINE" s.World)
        Assert.True(World.hasFlag "ENGINE_MINERALBADGE" s.World)
        Assert.True(World.hasEvent "EVENT_GOT_TM23_IRON_TAIL" s.World)
        Assert.Equal(1, Bag.count "TM_IRON_TAIL" s.Player.Bag)
        s

let private mahoganyRadioTowerGate () =
    ecruteakOlivineGate ()
    |> runMapScript "LakeOfRage" "RedGyarados"
    |> roundTrip
    |> fun s ->
        Assert.Equal(1, Bag.count "RED_SCALE" s.Player.Bag)
        Assert.Contains("LakeOfRageGotRedScaleText", s.Texts)
        s
    |> runMapScript "LakeOfRage" "LakeOfRageLanceScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_DECIDED_TO_HELP_LANCE" s.World)
        Assert.Equal(1, World.getScene "MAHOGANY_MART_1F" s.World)
        s
    |> runMapScript "TeamRocketBaseB2F" "RocketBaseElectrodeScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_CLEARED_ROCKET_HIDEOUT" s.World)
        Assert.True(World.hasEvent "EVENT_GOT_HM06_WHIRLPOOL" s.World)
        Assert.Equal(1, Bag.count "HM_WHIRLPOOL" s.Player.Bag)
        Assert.False(World.hasFlag "ENGINE_ROCKET_SIGNAL_ON_CH20" s.World)
        s
    |> runMapScript "MahoganyGym" "MahoganyGymPryceScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_PRYCE" s.World)
        Assert.True(World.hasFlag "ENGINE_GLACIERBADGE" s.World)
        Assert.True(World.hasEvent "EVENT_GOT_TM16_ICY_WIND" s.World)
        Assert.Equal(1, Bag.count "TM_ICY_WIND" s.Player.Bag)
        Assert.True(World.hasFlag "ENGINE_ROCKETS_IN_RADIO_TOWER" s.World)
        Assert.False(World.hasEvent "EVENT_RADIO_TOWER_ROCKET_TAKEOVER" s.World)
        Assert.Equal(1, World.getScene "MAHOGANY_TOWN" s.World)
        s
    |> runMapScript "RadioTower5F" "RadioTower5FRocketBossScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_CLEARED_RADIO_TOWER" s.World)
        Assert.True(World.hasEvent "EVENT_TEAM_ROCKET_DISBANDED" s.World)
        Assert.True(World.hasEvent "EVENT_GOT_RAINBOW_WING" s.World)
        Assert.False(World.hasFlag "ENGINE_ROCKETS_IN_RADIO_TOWER" s.World)
        Assert.Equal(2, World.getScene "RadioTower5F" s.World)
        Assert.Equal(1, Bag.count "RAINBOW_WING" s.Player.Bag)
        s

let private blackthornLeagueGate () =
    mahoganyRadioTowerGate ()
    |> runMapScript "BlackthornGym1F" "BlackthornGymClairScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_CLAIR" s.World)
        Assert.True(World.hasEvent "EVENT_BLACKTHORN_CITY_GRAMPS_BLOCKS_DRAGONS_DEN" s.World)
        Assert.False(World.hasFlag "ENGINE_RISINGBADGE" s.World)
        s
    |> runMapScript "DragonsDenB1F" "DragonsDenB1FDragonFangScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasFlag "ENGINE_RISINGBADGE" s.World)
        Assert.True(World.hasEvent "EVENT_GOT_TM24_DRAGONBREATH" s.World)
        Assert.Equal(1, Bag.count "DRAGON_FANG" s.Player.Bag)
        Assert.Equal(1, Bag.count "TM_DRAGONBREATH" s.Player.Bag)
        Assert.Equal("SPECIALCALL_MASTERBALL", World.getBuffer "__special_phone_call" s.World)
        s
    |> runMapScript "LancesRoom" "LancesRoomLanceScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_CHAMPION_LANCE" s.World)
        Assert.Equal("HallOfFame", s.MapId)
        s
    |> runMapScript "HallOfFame" "HallOfFameEnterScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_ELITE_FOUR" s.World)
        Assert.True(World.hasEvent "EVENT_TELEPORT_GUY" s.World)
        Assert.Equal(1, World.getScene "HallOfFame" s.World)
        Assert.Equal("SPECIALCALL_SSTICKET", World.getBuffer "__special_phone_call" s.World)
        s

// ---------------------------------------------------------------------------
//  Kanto (post-Hall-of-Fame). Source of truth per script named below.
// ---------------------------------------------------------------------------

let private kantoArrivalGate () =
    blackthornLeagueGate ()
    // ElmsLab: Elm hands over the S.S. TICKET after the Hall of Fame.
    |> runMapScript "ElmsLab" "ElmGiveTicketScript"
    |> roundTrip
    |> fun s ->
        Assert.Equal(1, Bag.count "S_S_TICKET" s.Player.Bag)
        s
    // Boarding scene: clears the arrival flag and stages the worried-grandpa cutscene.
    |> runMapScript "FastShip1F" "FastShip1FEnterShipScript"
    |> roundTrip
    |> fun s ->
        Assert.False(World.hasEvent "EVENT_FAST_SHIP_HAS_ARRIVED" s.World)
        Assert.Equal(2, World.getScene "FastShip1F" s.World)
        s
    |> runMapScript "FastShip1F" "WorriedGrandpaSceneLeft"
    |> roundTrip
    // Captain's cabin: finding the granddaughter gives METAL_COAT and docks the ship.
    |> runMapScript "FastShipCabins_SE_SSE_CaptainsCabin" "SSAquaGranddaughterBefore"
    |> roundTrip
    |> fun s ->
        Assert.Equal(1, Bag.count "METAL_COAT" s.Player.Bag)
        Assert.True(World.hasEvent "EVENT_FAST_SHIP_HAS_ARRIVED" s.World)
        Assert.True(World.hasEvent "EVENT_FAST_SHIP_FOUND_GIRL" s.World)
        Assert.True(World.hasEvent "EVENT_VERMILION_PORT_SAILOR_AT_GANGWAY" s.World)
        s
    // Sailor lets the player off in Vermilion: explicit warp + port leave-ship scene.
    |> runMapScript "FastShip1F" "FastShip1FSailor1Script"
    |> roundTrip
    |> fun s ->
        Assert.Equal("VermilionPort", s.MapId)
        Assert.Equal(1, World.getScene "VERMILION_PORT" s.World)
        s
    |> runMapScript "VermilionPort" "VermilionPortLeaveShipScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_FAST_SHIP_FIRST_TIME" s.World)
        Assert.Equal(0, World.getScene "VermilionPort" s.World)
        s

let private surgeSabrinaGate () =
    kantoArrivalGate ()
    |> runMapScript "VermilionGym" "VermilionGymSurgeScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_LTSURGE" s.World)
        Assert.True(World.hasFlag "ENGINE_THUNDERBADGE" s.World)
        s
    |> runMapScript "SaffronGym" "SaffronGymSabrinaScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_SABRINA" s.World)
        Assert.True(World.hasFlag "ENGINE_MARSHBADGE" s.World)
        s

let private machinePartMistyGate () =
    surgeSabrinaGate ()
    // Power Plant manager kicks off the machine-part quest.
    |> runMapScript "PowerPlant" "PowerPlantManager"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_MET_MANAGER_AT_POWER_PLANT" s.World)
        Assert.False(World.hasEvent "EVENT_FOUND_MACHINE_PART_IN_CERULEAN_GYM" s.World)
        Assert.Equal(1, World.getScene "CERULEAN_GYM" s.World)
        Assert.Equal(1, World.getScene "PowerPlant" s.World)
        s
    // The grunt flees the Cerulean Gym, staging the Route 24/25 chain.
    |> runMapScript "CeruleanGym" "CeruleanGymGruntRunsOutScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_MET_ROCKET_GRUNT_AT_CERULEAN_GYM" s.World)
        Assert.Equal(1, World.getScene "ROUTE_25" s.World)
        Assert.Equal(0, World.getScene "POWER_PLANT" s.World)
        s
    |> runMapScript "Route24" "Route24RocketScript"
    |> roundTrip
    // Misty's interrupted date sends her back to the gym.
    |> runMapScript "Route25" "Route25MistyDate1Script"
    |> roundTrip
    |> fun s ->
        Assert.False(World.hasEvent "EVENT_TRAINERS_IN_CERULEAN_GYM" s.World)
        Assert.Equal(0, World.getScene "Route25" s.World)
        s
    // The hidden machine part in the gym: collectable exactly once.
    |> runMapScript "CeruleanGym" "CeruleanGymHiddenMachinePart"
    |> roundTrip
    |> fun s ->
        Assert.Equal(1, Bag.count "MACHINE_PART" s.Player.Bag)
        Assert.True(World.hasEvent "EVENT_FOUND_MACHINE_PART_IN_CERULEAN_GYM" s.World)
        s
    |> runMapScript "CeruleanGym" "CeruleanGymHiddenMachinePart"
    |> roundTrip
    |> fun s ->
        Assert.Equal(1, Bag.count "MACHINE_PART" s.Player.Bag)
        s
    // Returning the part restores power to Kanto and earns TM07.
    |> runMapScript "PowerPlant" "PowerPlantManager"
    |> roundTrip
    |> fun s ->
        Assert.Equal(0, Bag.count "MACHINE_PART" s.Player.Bag)
        Assert.True(World.hasEvent "EVENT_RETURNED_MACHINE_PART" s.World)
        Assert.True(World.hasEvent "EVENT_RESTORED_POWER_TO_KANTO" s.World)
        Assert.Equal(1, Bag.count "TM_ZAP_CANNON" s.Player.Bag)
        s
    // The Lavender radio director hands over the EXPN CARD.
    |> runMapScript "LavRadioTower1F" "LavRadioTower1FGentlemanScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasFlag "ENGINE_EXPN_CARD" s.World)
        s
    |> runMapScript "CeruleanGym" "CeruleanGymMistyScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_MISTY" s.World)
        Assert.True(World.hasFlag "ENGINE_CASCADEBADGE" s.World)
        s

let private snorlaxPewterGate () =
    machinePartMistyGate ()
    // Snorlax won't budge until the radio is tuned to the Poké Flute channel.
    |> runMapScript "VermilionCity" "VermilionSnorlax"
    |> roundTrip
    |> fun s ->
        Assert.False(World.hasEvent "EVENT_FOUGHT_SNORLAX" s.World)
        Assert.Contains("VermilionCitySnorlaxSleepingText", s.Texts)
        // Tune the Pokégear radio (the overworld writes this buffer on tune).
        { s with World = World.setBuffer "__radio_station" "POKE_FLUTE" s.World }
    |> runMapScript "VermilionCity" "VermilionSnorlax"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_FOUGHT_SNORLAX" s.World)
        Assert.Contains("VermilionCityRadioNearSnorlaxText", s.Texts)
        s
    |> runMapScript "PewterGym" "PewterGymBrockScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_BROCK" s.World)
        Assert.True(World.hasFlag "ENGINE_BOULDERBADGE" s.World)
        s

let private southKantoGate () =
    snorlaxPewterGate ()
    |> runMapScript "CeladonGym" "CeladonGymErikaScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_ERIKA" s.World)
        Assert.True(World.hasFlag "ENGINE_RAINBOWBADGE" s.World)
        Assert.Equal(1, Bag.count "TM_GIGA_DRAIN" s.Player.Bag)
        s
    |> runMapScript "FuchsiaGym" "FuchsiaGymJanineScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_JANINE" s.World)
        Assert.True(World.hasFlag "ENGINE_SOULBADGE" s.World)
        Assert.Equal(1, Bag.count "TM_TOXIC" s.Player.Bag)
        s
    |> fun s ->
        // Blue starts hidden in his gym (set by InitializeEventsScript)…
        Assert.True(World.hasEvent "EVENT_VIRIDIAN_GYM_BLUE" s.World)
        s
    |> runMapScript "CinnabarIsland" "CinnabarIslandBlue"
    |> roundTrip
    |> fun s ->
        // …and meeting him on Cinnabar sends him back to it.
        Assert.False(World.hasEvent "EVENT_VIRIDIAN_GYM_BLUE" s.World)
        s
    |> runMapScript "SeafoamGym" "SeafoamGymBlaineScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_BLAINE" s.World)
        Assert.True(World.hasFlag "ENGINE_VOLCANOBADGE" s.World)
        s
    |> runMapScript "ViridianGym" "ViridianGymBlueScript"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_BEAT_BLUE" s.World)
        Assert.True(World.hasFlag "ENGINE_EARTHBADGE" s.World)
        s

let private mtSilverRedGate () =
    southKantoGate ()
    // Oak counts all 16 badges (readvar VAR_BADGES) and opens Mt. Silver.
    |> runMapScript "OaksLab" "Oak"
    |> roundTrip
    |> fun s ->
        Assert.True(World.hasEvent "EVENT_OPENED_MT_SILVER" s.World)
        s
    |> runMapScript "SilverCaveRoom3" "Red"
    |> roundTrip
    |> fun s ->
        Assert.Contains("RedSeenText", s.Texts)
        Assert.Contains("RedLeavesText", s.Texts)
        s

[<Fact>]
let ``G1 known good through New Bark starter unlock`` () =
    newBarkPrologue () |> ignore

[<Fact>]
let ``G2 known good through Mr Pokemon and Elm return`` () =
    mrPokemonLoop () |> ignore

[<Fact>]
let ``G3 known good through Violet Gym`` () =
    violetGate () |> ignore

[<Fact>]
let ``G4 known good through Azalea Gym and Ilex pre-Cut`` () =
    azaleaIlexGate () |> ignore

[<Fact>]
let ``G5 known good through Goldenrod and Sudowoodo`` () =
    goldenrodSudowoodoGate () |> ignore

[<Fact>]
let ``G6 known good through Ecruteak Olivine and Cianwood`` () =
    ecruteakOlivineGate () |> ignore

[<Fact>]
let ``G7 known good through Mahogany and Radio Tower`` () =
    mahoganyRadioTowerGate () |> ignore

[<Fact>]
let ``G8 known good through Blackthorn and Hall of Fame`` () =
    blackthornLeagueGate () |> ignore

[<Fact>]
let ``G9 known good through SS Aqua arrival in Vermilion`` () =
    kantoArrivalGate () |> ignore

[<Fact>]
let ``G10 known good through Surge and Sabrina`` () =
    surgeSabrinaGate () |> ignore

[<Fact>]
let ``G11 known good through machine part quest and Misty`` () =
    machinePartMistyGate () |> ignore

[<Fact>]
let ``G12 known good through Snorlax wake and Brock`` () =
    snorlaxPewterGate () |> ignore

[<Fact>]
let ``G13 known good through Erika Janine Blaine and Blue`` () =
    southKantoGate () |> ignore

[<Fact>]
let ``G14 known good through Mt Silver unlock and Red`` () =
    mtSilverRedGate () |> ignore
