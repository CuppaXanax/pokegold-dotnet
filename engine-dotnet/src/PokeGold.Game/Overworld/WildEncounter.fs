namespace PokeGold.Game.Overworld

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Battle
open PokeGold.Game.Overworld.Script

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

    /// Fishing encounter tables (simplified — real tables from fish_group data are M16).
    let fishEncounter (rod: string) (rng: System.Random) : (string * int) =
        match rod with
        | "OLD_ROD" -> ("MAGIKARP", 10)
        | "GOOD_ROD" ->
            match rng.Next(3) with
            | 0 -> ("MAGIKARP", 15 + rng.Next(10))
            | 1 -> ("POLIWAG", 15 + rng.Next(10))
            | _ -> ("MAGIKARP", 20 + rng.Next(5))
        | "SUPER_ROD" ->
            match rng.Next(4) with
            | 0 -> ("POLIWAG", 20 + rng.Next(10))
            | 1 -> ("MAGIKARP", 25 + rng.Next(10))
            | 2 -> ("POLIWHIRL", 25 + rng.Next(10))
            | _ -> ("TENTACRUEL", 25 + rng.Next(10))
        | _ -> ("MAGIKARP", 10)

    /// Default encounter rate for grass (25%) and water (15%).
    let grassRate = 25
    let waterRate = 15

    /// Try to trigger a wild encounter. Returns Some (species, level) or None.
    let tryEncounter (mapName: string) (collId: byte) (rng: System.Random) (player: PokeGold.Game.Player.PlayerState) : (string * int) option =
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

                if not (shouldEncounter table.WaterRate encounterRoll) then
                    None
                else
                    let slotRoll = rng.Next(100)
                    let slot = selectSlot slotRoll
                    let entry = table.Water.[min slot (table.Water.Length - 1)]

                    if PokeGold.Game.Player.Repel.blocks player entry.Level then
                        None
                    else
                        Some(entry.Species, entry.Level)
            | Some table when collId <> CollWater && table.GrassRate <> (0, 0, 0) ->
                let encounterRoll = rng.Next(256)
                let rate = currentGrassRate table

                if not (shouldEncounter rate encounterRoll) then
                    None
                else
                    let slotRoll = rng.Next(100)
                    let slot = selectSlot slotRoll
                    let entry = currentGrassTable table |> fun slots -> slots.[min slot (slots.Length - 1)]

                    if PokeGold.Game.Player.Repel.blocks player entry.Level then
                        None
                    else
                        Some(entry.Species, entry.Level)
            | _ ->
                let encounterRoll = rng.Next(256)

                if not (shouldEncounter fallbackRate encounterRoll) then
                    None
                else
                    let slotRoll = rng.Next(100)
                    let slot = selectSlot slotRoll
                    let entry = fallbackTable.[min slot (fallbackTable.Length - 1)]

                    if PokeGold.Game.Player.Repel.blocks player entry.Level then
                        None
                    else
                        Some(entry.Species, entry.Level)
