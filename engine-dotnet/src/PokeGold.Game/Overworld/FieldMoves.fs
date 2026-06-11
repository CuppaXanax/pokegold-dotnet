namespace PokeGold.Game.Overworld

open PokeGold.Game.Data

/// HM field-move definitions: badge gates, party move checks, and the collision
/// tiles that decide whether the move can be used at the player's current target.
module FieldMoves =

    /// Result of attempting to use a field move.
    type FieldMoveResult =
        | Used of moveName: string * message: string
        | NotUsable of reason: string

    /// Collision IDs for HM-interactable tiles.
    [<Literal>]
    let CollCutTree = 0x12uy

    [<Literal>]
    let CollCutTree1A = 0x1auy

    [<Literal>]
    let CollSurf = 0x29uy

    [<Literal>]
    let CollWater21 = 0x21uy

    [<Literal>]
    let CollWhirlpool = 0x24uy

    [<Literal>]
    let CollWhirlpool2C = 0x2cuy

    [<Literal>]
    let CollWaterfallRight = 0x30uy

    [<Literal>]
    let CollWaterfallLeft = 0x31uy

    [<Literal>]
    let CollWaterfallUp = 0x32uy

    [<Literal>]
    let CollWaterfall = 0x33uy

    /// (MoveName, RequiredBadgeBit) — the ENGINE_* flag that must be set.
    /// Badge constants from constants/engine_flags.asm:
    /// ZEPHYRBADGE=0, HIVEBADGE=1, PLAINBADGE=2, FOGBADGE=3,
    /// MINERALBADGE=4, STORMBADGE=5, GLACIERBADGE=6, RISINGBADGE=7
    let hmMoves: (string * string) list =
        [ "CUT", "ENGINE_HIVEBADGE"
          "FLY", "ENGINE_STORMBADGE"
          "SURF", "ENGINE_FOGBADGE"
          "STRENGTH", "ENGINE_PLAINBADGE"
          "FLASH", "ENGINE_ZEPHYRBADGE"
          "WHIRLPOOL", "ENGINE_GLACIERBADGE"
          "WATERFALL", "ENGINE_RISINGBADGE" ]

    let private normalize (moveName: string) =
        moveName.Trim().ToUpperInvariant()

    let requiredBadge moveName =
        hmMoves
        |> List.tryFind (fun (m, _) -> m = normalize moveName)
        |> Option.map snd

    let private badgeDisplay (badge: string) =
        badge.Replace("ENGINE_", "").Replace("BADGE", "BADGE")

    /// Can the player use this HM in the field right now?
    /// Checks: badge obtained + a party mon knows the move.
    let canUse (moveName: string) (world: PokeGold.Game.Overworld.Script.World) (party: PokeGold.Game.Player.Party) : bool =
        let moveName = normalize moveName
        let hasBadge =
            requiredBadge moveName
            |> Option.map (fun badge -> PokeGold.Game.Overworld.Script.World.hasFlag badge world)
            |> Option.defaultValue false

        let hasMove =
            party
            |> List.exists (fun (mon: PokeGold.Game.Player.PartyMon) ->
                mon.Moves
                |> List.exists (fun (moveId, _pp) ->
                    Moves.tryByIndex moveId
                    |> Option.exists (fun md -> md.Name = moveName)))

        hasBadge && hasMove

    let private hasMove moveName (party: PokeGold.Game.Player.Party) =
        let moveName = normalize moveName
        party
        |> List.exists (fun mon ->
            mon.Moves
            |> List.exists (fun (moveId, _) ->
                Moves.tryByIndex moveId
                |> Option.exists (fun md -> md.Name = moveName)))

    let private requirementFailure moveName world party =
        let moveName = normalize moveName
        match requiredBadge moveName with
        | None -> Some $"Unknown field move {moveName}"
        | Some badge when not (PokeGold.Game.Overworld.Script.World.hasFlag badge world) ->
            Some $"Need {badgeDisplay badge}"
        | Some _ when not (hasMove moveName party) ->
            Some $"No Pokémon knows {moveName}"
        | _ -> None

    let private isCutTree collId =
        collId = CollCutTree || collId = CollCutTree1A

    let private isSurfWater collId =
        [ CollSurf; CollWater21; CollWhirlpool; CollWhirlpool2C; CollWaterfallRight; CollWaterfallLeft; CollWaterfallUp; CollWaterfall ]
        |> List.contains collId

    let private isWhirlpool collId =
        collId = CollWhirlpool || collId = CollWhirlpool2C

    let private isWaterfall collId =
        [ CollWaterfallRight; CollWaterfallLeft; CollWaterfallUp; CollWaterfall ]
        |> List.contains collId

    let tryUse (moveName: string) (targetCollId: byte) (mapId: string) (world: PokeGold.Game.Overworld.Script.World) (party: PokeGold.Game.Player.Party) : FieldMoveResult =
        let moveName = normalize moveName
        match requirementFailure moveName world party with
        | Some reason -> NotUsable reason
        | None ->
            match moveName with
            | "CUT" when not (isCutTree targetCollId) -> NotUsable "Can't use CUT here"
            | "CUT" -> Used("CUT", "Used CUT!")
            | "SURF" when not (isSurfWater targetCollId) -> NotUsable "Can't use SURF here"
            | "SURF" -> Used("SURF", "Used SURF!")
            | "STRENGTH" -> Used("STRENGTH", "Boulders may now be moved!")
            | "WHIRLPOOL" when not (isWhirlpool targetCollId) -> NotUsable "Can't use WHIRLPOOL here"
            | "WHIRLPOOL" -> Used("WHIRLPOOL", "Used WHIRLPOOL!")
            | "WATERFALL" when not (isWaterfall targetCollId) -> NotUsable "Can't use WATERFALL here"
            | "WATERFALL" -> Used("WATERFALL", "Used WATERFALL!")
            | "FLY" when not (world.EngineFlags |> Set.exists (fun flag -> flag.StartsWith("ENGINE_FLYPOINT_"))) ->
                NotUsable "No known FLY destination"
            | "FLY" -> Used("FLY", $"Used FLY from {mapId}!")
            | "FLASH" -> Used("FLASH", "Used FLASH!")
            | _ -> NotUsable $"Can't use {moveName} here"

    let tryCut (collId: byte) (world: PokeGold.Game.Overworld.Script.World) (party: PokeGold.Game.Player.Party) : FieldMoveResult =
        tryUse "CUT" collId "" world party

    let trySurf (collId: byte) (world: PokeGold.Game.Overworld.Script.World) (party: PokeGold.Game.Player.Party) : FieldMoveResult =
        tryUse "SURF" collId "" world party

    let tryStrength (collId: byte) (world: PokeGold.Game.Overworld.Script.World) (party: PokeGold.Game.Player.Party) : FieldMoveResult =
        tryUse "STRENGTH" collId "" world party
