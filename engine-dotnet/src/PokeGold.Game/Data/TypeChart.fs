namespace PokeGold.Game.Data

open System.Text.RegularExpressions
open PokeGold.Game.Core

/// The Gen-2 type system: the numeric type ids (`constants/type_constants.asm`),
/// the physical/special split (decided by type, not by move), and the type
/// effectiveness matchups (`data/types/type_matchups.asm`). Multipliers are the
/// raw disassembly values scaled by ten — 20 = ×2, 10 = ×1, 5 = ×½, 0 = ×0 —
/// so the damage code can multiply then divide by ten with the same integer
/// truncation the original performs.
module TypeChart =

    /// In Gen 2 the physical/special class is determined by the move's type:
    /// type ids below `SPECIAL` (= 20) are physical, the rest special.
    [<Literal>]
    let SpecialBoundary = 20

    [<Literal>]
    let SuperEffective = 20
    [<Literal>]
    let Neutral = 10
    [<Literal>]
    let NotVeryEffective = 5
    [<Literal>]
    let NoEffect = 0

    let private types = lazy (AsmConstants.load "constants/type_constants.asm")

    /// The numeric id of a named type (e.g. "FIRE" → 20).
    let value (name: string) : int = types.Value.[name]

    /// True when a move of the given type id is physical (uses Attack/Defense).
    let isPhysical (typeId: int) : bool = typeId < SpecialBoundary

    // attacker id → defender id → multiplier×10. Missing entries are neutral.
    let private matchups =
        lazy
            (let rx = Regex(@"^\s*db\s+([A-Za-z_]\w*),\s*([A-Za-z_]\w*),\s*([A-Za-z_]\w*)")

             let mult =
                 function
                 | "SUPER_EFFECTIVE" -> SuperEffective
                 | "NOT_VERY_EFFECTIVE" -> NotVeryEffective
                 | "NO_EFFECT" -> NoEffect
                 | other -> failwithf "Unknown type matchup multiplier '%s'" other

             let ty = types.Value

             [ for raw in Assets.readText("data/types/type_matchups.asm").Split('\n') do
                   let m = rx.Match raw
                   if m.Success then
                       match ty.TryFind m.Groups.[1].Value, ty.TryFind m.Groups.[2].Value with
                       | Some a, Some d -> yield (a, d), mult m.Groups.[3].Value
                       | _ -> () ]
             |> Map.ofList)

    /// The effectiveness multiplier (×10) of an attacking type against a single
    /// defending type. Returns `Neutral` (10) when the chart has no entry.
    let multiplier (attacking: int) (defending: int) : int =
        match matchups.Value.TryFind((attacking, defending)) with
        | Some m -> m
        | None -> Neutral
