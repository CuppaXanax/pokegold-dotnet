namespace PokeGold.Game.Overworld

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Battle
open PokeGold.Game.Overworld.Script

type Roamer =
    { Slot: int
      Species: string
      Level: int
      MapId: string
      Hp: int }

module Roaming =

    let private key slot field = sprintf "__roamer_%d_%s" slot field

    let private initialRoamers =
        [ { Slot = 1; Species = "RAIKOU"; Level = 40; MapId = "Route42"; Hp = 0 }
          { Slot = 2; Species = "ENTEI"; Level = 40; MapId = "Route37"; Hp = 0 }
          { Slot = 3; Species = "SUICUNE"; Level = 40; MapId = "Route38"; Hp = 0 } ]

    let init (world: World) =
        initialRoamers
        |> List.fold (fun w roamer ->
            w
            |> World.setBuffer (key roamer.Slot "species") roamer.Species
            |> World.setBuffer (key roamer.Slot "map") roamer.MapId
            |> World.setVar (key roamer.Slot "level") roamer.Level
            |> World.setVar (key roamer.Slot "hp") roamer.Hp) world

    let active (world: World) =
        [ 1..3 ]
        |> List.choose (fun slot ->
            let species = World.getBuffer (key slot "species") world
            let mapId = World.getBuffer (key slot "map") world

            if species = "" || mapId = "" then
                None
            else
                Some
                    { Slot = slot
                      Species = species
                      Level = World.getVar (key slot "level") world
                      MapId = mapId
                      Hp = World.getVar (key slot "hp") world })

    let private canonicalMapId mapId =
        Maps.canonicalConst mapId
        |> Option.orElse (MapsData.byName mapId |> Option.map (fun map -> map.Meta.Const))
        |> Option.defaultValue mapId

    let tryEncounter (mapName: string) (isWater: bool) (rng: System.Random) (world: World) =
        if isWater then
            None
        else
            let roll = rng.Next(256)

            if roll >= 100 then
                None
            else
                match roll &&& 0x03 with
                | 0 -> None
                | selected ->
                    let currentMap = canonicalMapId mapName

                    active world
                    |> List.tryItem (selected - 1)
                    |> Option.filter (fun roamer -> canonicalMapId roamer.MapId = currentMap)
                    |> Option.map (fun roamer -> roamer.Species, roamer.Level)

[<RequireQualifiedAccess>]
type WildBattleType =
    | Normal
    | ForceItem
    | ForceShiny

module WildOpponent =

    let ofBattleTypeValue value =
        match value with
        | 7 -> WildBattleType.ForceShiny
        | 10 -> WildBattleType.ForceItem
        | _ -> WildBattleType.Normal

    /// Source thresholds: 0..191 none, then 0..19 rare item, otherwise common.
    let rollHeldItem (battleType: WildBattleType) (rng: System.Random) (species: BaseStats) : string option =
        match battleType with
        | WildBattleType.ForceItem -> species.Item1
        | _ ->
            if rng.Next(256) < 192 then
                None
            elif rng.Next(256) < 20 then
                species.Item2
            else
                species.Item1

    let rollDvs (battleType: WildBattleType) (rng: System.Random) : int =
        match battleType with
        | WildBattleType.ForceShiny -> 0xEAAA
        | _ -> (rng.Next(256) <<< 8) ||| rng.Next(256)

    let genderFromDvs (species: BaseStats) (dvs: int) : Gender =
        BattleMon.genderFromDvs species dvs

    let create (battleType: WildBattleType) (rng: System.Random) (species: BaseStats) (level: int) : BattleMon =
        let heldItem = rollHeldItem battleType rng species
        let dvs = rollDvs battleType rng
        let moves =
            MoveLearn.startingMoveNames species.Name level
            |> List.map Moves.byName

        { BattleMon.ofSpeciesWithStats species level moves dvs StatExperience.zero with
            HeldItem = heldItem
            Gender = genderFromDvs species dvs }

/// Source `TreeMonEncounter` selection for Headbutt trees.
module TreeEncounter =

    type TreeScore =
        | Bad
        | Good
        | Rare

    let private mapConst mapName =
        Maps.canonicalConst mapName
        |> Option.orElse (MapsData.byName mapName |> Option.map (fun map -> map.Meta.Const))
        |> Option.defaultValue mapName

    /// `GetTreeScore.CoordScore`: $(((x + 1)(y + 1) - 1) / 5) \bmod 10$.
    let coordinateScore x y =
        (((x + 1) * (y + 1) - 1) / 5) % 10

    /// `GetTreeScore`: compare the faced-tree score to the player's ID remainder.
    let score x y trainerId =
        let difference = (coordinateScore x y - (trainerId % 10) + 10) % 10

        if difference = 0 then Rare
        elif difference < 5 then Good
        else Bad

    let private selectWeighted roll slots =
        let rec select remaining =
            function
            | [] -> None
            | slot :: rest when remaining < slot.Weight -> Some(slot.Species, slot.Level)
            | slot :: rest -> select (remaining - slot.Weight) rest

        select roll slots

    /// Return the source tree encounter, if the map set, score gate, and weighted
    /// table all produce one. `trainerId` is a persisted port-side stand-in for
    /// GSC's wPlayerID; absent state reads as zero at the caller.
    let tryHeadbutt mapName x y trainerId (rng: System.Random) =
        match Map.tryFind (mapConst mapName) TreeMonsData.mapSets with
        | Some setName ->
            let tableName = setName.Replace("TREEMON_SET_", "")

            match Map.tryFind tableName TreeMonsData.tables with
            | Some table ->
                let treeScore = score x y trainerId
                let encounterChance, slots =
                    match treeScore with
                    | Bad -> 1, table.Common
                    | Good -> 5, table.Common
                    | Rare -> 8, table.Rare

                if rng.Next(10) >= encounterChance then
                    None
                else
                    selectWeighted (rng.Next(100)) slots
            | None -> None
        | None -> None

/// Wild encounter trigger logic.
/// Source: engine/overworld/wildmons.asm::TryWildEncounter
module WildEncounter =

    /// Collision IDs that trigger encounters.
    [<Literal>]
    let CollTallGrass = 0x18uy

    [<Literal>]
    let CollLongGrass = 0x14uy

    [<Literal>]
    let CollWater = 0x29uy

    /// Is this collision ID an encounter tile?
    let isEncounterTile (collId: byte) : bool =
        collId = CollTallGrass || collId = CollLongGrass || collId = CollWater

    /// Gen-2 encounter rate check. The encounter rate byte (0-100) is compared
    /// against a random roll (0-255). Rate is scaled: rate * 16 / 100 = threshold.
    /// Source: engine/overworld/wildmons.asm::TryWildEncounter
    let shouldEncounter (rate: int) (roll: int) : bool =
        let threshold = rate * 16 / 100
        roll < threshold

    let effectiveRate (player: PlayerState) (rate: int) : int =
        match player.Party with
        | lead :: _ when lead.HeldItem = Some "CLEANSE_TAG" ->
            max 0 (rate * 2 / 3)
        | _ -> rate

    /// Encounter probability table (7 slots) from data/wild/probabilities.asm.
    /// Each entry is the cumulative threshold out of 100.
    let private probTable = [| 30; 60; 80; 90; 95; 99; 100 |]

    /// Select a slot (0-6) from the probability table given a roll 0-99.
    let selectSlot (roll: int) : int =
        probTable |> Array.tryFindIndex (fun t -> roll < t) |> Option.defaultValue 6

    /// A single encounter table entry.
    type WildEntry = { Level: int; Species: string }

    let private currentGrassTable (table: WildEncounterTable) : WildSlot list =
        match TimeOfDay.current() with
        | Morn -> table.GrassMorn
        | Day -> table.GrassDay
        | Nite -> table.GrassNite

    let private currentGrassRate (table: WildEncounterTable) : int =
        let morn, day, nite = table.GrassRate

        match TimeOfDay.current() with
        | Morn -> morn
        | Day -> day
        | Nite -> nite

    let private currentWaterTable (table: WildEncounterTable) : WildSlot list =
        table.Water

    /// Hardcoded fallback encounter table (Route 29-ish).
    /// Real tables will come from M16 data generation.
    let fallbackGrassTable : WildEntry[] =
        [| { Level = 2; Species = "PIDGEY" }
           { Level = 3; Species = "SENTRET" }
           { Level = 3; Species = "PIDGEY" }
           { Level = 4; Species = "SENTRET" }
           { Level = 5; Species = "RATTATA" }
           { Level = 4; Species = "PIDGEY" }
           { Level = 5; Species = "SENTRET" } |]

    let fallbackWaterTable : WildEntry[] =
        [| { Level = 10; Species = "TENTACOOL" }
           { Level = 10; Species = "TENTACOOL" }
           { Level = 15; Species = "TENTACOOL" }
           { Level = 15; Species = "TENTACRUEL" }
           { Level = 20; Species = "TENTACOOL" }
           { Level = 20; Species = "TENTACRUEL" }
           { Level = 25; Species = "TENTACRUEL" } |]

    let private rodSlots (rod: string) (group: FishGroupTable) =
        match rod with
        | "OLD_ROD" -> group.OldRod
        | "GOOD_ROD" -> group.GoodRod
        | "SUPER_ROD" -> group.SuperRod
        | _ -> []

    let private resolveFishSlot (timeOfDay: TimeOfDay) (slot: FishSlot) =
        match slot.Species, slot.TimeGroup with
        | Some species, _ -> Some(species, slot.Level)
        | None, Some index ->
            FishEncountersData.timeGroups
            |> Array.tryItem index
            |> Option.map (fun timeGroup ->
                match timeOfDay with
                | Nite -> timeGroup.NightSpecies, timeGroup.NightLevel
                | Morn
                | Day -> timeGroup.DaySpecies, timeGroup.DayLevel)
        | None, None -> None

    let tryFish (groupName: string) (rod: string) (timeOfDay: TimeOfDay) (rng: System.Random) : (string * int) option =
        FishEncountersData.byGroup
        |> Map.tryFind groupName
        |> Option.bind (fun group ->
            let slots = rodSlots rod group

            if slots.IsEmpty || rng.Next(256) >= group.BiteThreshold then
                None
            else
                let roll = rng.Next(256)

                slots
                |> List.tryFind (fun slot -> roll <= slot.Threshold)
                |> Option.bind (resolveFishSlot timeOfDay))

    /// Default encounter rate for grass (25%) and water (15%).
    let grassRate = 25
    let waterRate = 15

    /// Try to trigger a wild encounter. Returns Some (species, level) or None.
    let tryEncounter (mapName: string) (collId: byte) (rng: System.Random) (player: PokeGold.Game.Player.PlayerState) (world: World) : (string * int) option =
        if not (isEncounterTile collId) then
            None
        else
            // Map IDs are like "Route29" but encounter tables use constants like "ROUTE_29"
            let mapConst =
                MapsData.byName mapName
                |> Option.map (fun m -> m.Meta.Const)
                |> Option.defaultValue mapName

            let fallbackTable = if collId = CollWater then fallbackWaterTable else fallbackGrassTable
            let fallbackRate = if collId = CollWater then waterRate else grassRate

            match WildEncounters.forMap mapConst with
            | Some table when collId = CollWater && table.WaterRate > 0 ->
                let encounterRoll = rng.Next(256)

                let rate = effectiveRate player table.WaterRate

                if not (shouldEncounter rate encounterRoll) then
                    None
                else
                    match Roaming.tryEncounter mapName (collId = CollWater) rng world with
                    | Some roamer -> Some roamer
                    | None ->
                        let slotRoll = rng.Next(100)
                        let slot = selectSlot slotRoll
                        let entry = table.Water.[min slot (table.Water.Length - 1)]

                        if PokeGold.Game.Player.Repel.blocks player entry.Level then
                            None
                        else
                            Some(entry.Species, entry.Level)
            | Some table when collId <> CollWater && table.GrassRate <> (0, 0, 0) ->
                let encounterRoll = rng.Next(256)
                let rate = currentGrassRate table |> effectiveRate player

                if not (shouldEncounter rate encounterRoll) then
                    None
                else
                    match Roaming.tryEncounter mapName (collId = CollWater) rng world with
                    | Some roamer -> Some roamer
                    | None ->
                        let slotRoll = rng.Next(100)
                        let slot = selectSlot slotRoll
                        let entry = currentGrassTable table |> fun slots -> slots.[min slot (slots.Length - 1)]

                        if PokeGold.Game.Player.Repel.blocks player entry.Level then
                            None
                        else
                            Some(entry.Species, entry.Level)
            | _ ->
                let encounterRoll = rng.Next(256)

                let fallbackRate = effectiveRate player fallbackRate

                if not (shouldEncounter fallbackRate encounterRoll) then
                    None
                else
                    match Roaming.tryEncounter mapName (collId = CollWater) rng world with
                    | Some roamer -> Some roamer
                    | None ->
                        let slotRoll = rng.Next(100)
                        let slot = selectSlot slotRoll
                        let entry = fallbackTable.[min slot (fallbackTable.Length - 1)]

                        if PokeGold.Game.Player.Repel.blocks player entry.Level then
                            None
                        else
                            Some(entry.Species, entry.Level)
