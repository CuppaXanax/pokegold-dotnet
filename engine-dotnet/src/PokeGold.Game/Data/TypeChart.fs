namespace PokeGold.Game.Data

/// The Gen-2 type system: numeric type ids, the physical/special split (decided
/// by type, not by move), and type effectiveness. The *data* (ids + matchups)
/// is baked at build time into `TypeChartData` (`Data/Generated`); this module
/// keeps only the runtime logic. Multipliers are scaled by ten — 20 = x2,
/// 10 = x1, 5 = x1/2, 0 = x0 — so the damage code can multiply then divide by
/// ten with the same integer truncation the original performs.
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

    /// The numeric id of a named type (e.g. "FIRE" -> 20).
    let value (name: string) : int = TypeChartData.typeIds.[name]

    /// The name of a numeric type id (e.g. 20 -> "FIRE").
    let nameOfType (typeId: int) : string =
        match TypeChartData.typeIds |> Map.toSeq |> Seq.tryFind (fun (_, id) -> id = typeId) with
        | Some (name, _) -> name
        | None -> "UNKNOWN"

    /// True when a move of the given type id is physical (uses Attack/Defense).
    let isPhysical (typeId: int) : bool = typeId < SpecialBoundary

    /// The effectiveness multiplier (x10) of an attacking type against a single
    /// defending type. Returns `Neutral` (10) when the chart has no entry.
    let multiplier (attacking: int) (defending: int) : int =
        match TypeChartData.matchups.TryFind((attacking, defending)) with
        | Some m -> m
        | None -> Neutral
