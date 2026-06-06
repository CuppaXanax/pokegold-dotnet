namespace PokeGold.Game.Overworld

open PokeGold.Game.Data

/// HM field-move definitions: which badge gates each move, and which moves are
/// implemented. Full tile interaction is deferred; this module provides the
/// lookup tables the menu/overworld will query.
module FieldMoves =

    /// Result of attempting to use a field move.
    type FieldMoveResult =
        | Used of message: string
        | NotUsable of reason: string

    /// Collision IDs for HM-interactable tiles.
    [<Literal>]
    let CollCutTree = 0x12uy

    [<Literal>]
    let CollSurf = 0x29uy

    [<Literal>]
    let CollStrengthBoulder = 0x15uy

    [<Literal>]
    let CollWhirlpool = 0x24uy

    [<Literal>]
    let CollWaterfall = 0x33uy

    /// (MoveName, RequiredBadgeBit) — the ENGINE_* flag that must be set.
    /// Badge constants from constants/engine_flags.asm:
    /// ZEPHYRBADGE=0, HIVEBADGE=1, PLAINBADGE=2, FOGBADGE=3,
    /// MINERALBADGE=4, STORMBADGE=5, GLACIERBADGE=6, RISINGBADGE=7
    let hmMoves =
        [ "CUT",       "ENGINE_HIVEBADGE"
          "FLY",       "ENGINE_STORMBADGE"
          "SURF",      "ENGINE_FOGBADGE"
          "STRENGTH",  "ENGINE_PLAINBADGE"
          "FLASH",     "ENGINE_ZEPHYRBADGE"
          "WHIRLPOOL", "ENGINE_GLACIERBADGE"
          "WATERFALL", "ENGINE_RISINGBADGE" ]

    /// Can the player use this HM in the field right now?
    /// Checks: badge obtained + a party mon knows the move.
    let canUse (moveName: string) (world: PokeGold.Game.Overworld.Script.World) (party: PokeGold.Game.Player.Party) : bool =
        let hasBadge =
            hmMoves
            |> List.tryFind (fun (m, _) -> m = moveName)
            |> Option.map (fun (_, badge) -> PokeGold.Game.Overworld.Script.World.hasFlag badge world)
            |> Option.defaultValue false

        let hasMove =
            party
            |> List.exists (fun (mon: PokeGold.Game.Player.PartyMon) ->
                mon.Moves
                |> List.exists (fun (moveId, _pp) ->
                    Moves.tryByIndex moveId
                    |> Option.exists (fun md -> md.Name = moveName)))

        hasBadge && hasMove

    let tryCut (collId: byte) (world: PokeGold.Game.Overworld.Script.World) (party: PokeGold.Game.Player.Party) : FieldMoveResult =
        if collId <> CollCutTree then
            NotUsable "Not a cuttable tree"
        elif not (canUse "CUT" world party) then
            if not (PokeGold.Game.Overworld.Script.World.hasFlag "ENGINE_HIVEBADGE" world) then
                NotUsable "Need the HIVEBADGE"
            else
                NotUsable "No Pokémon knows CUT"
        else
            Used "Used CUT!"

    let trySurf (collId: byte) (world: PokeGold.Game.Overworld.Script.World) (party: PokeGold.Game.Player.Party) : FieldMoveResult =
        if collId <> CollSurf then
            NotUsable "Not water"
        elif not (canUse "SURF" world party) then
            NotUsable "Cannot use SURF"
        else
            Used "Used SURF!"

    let tryStrength (collId: byte) (world: PokeGold.Game.Overworld.Script.World) (party: PokeGold.Game.Player.Party) : FieldMoveResult =
        if collId <> CollStrengthBoulder then
            NotUsable "Not a boulder"
        elif not (canUse "STRENGTH" world party) then
            NotUsable "Cannot use STRENGTH"
        else
            Used "STRENGTH can be used!"
