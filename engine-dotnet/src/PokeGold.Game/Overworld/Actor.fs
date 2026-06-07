namespace PokeGold.Game.Overworld

open System

/// A stable identifier for a live overworld actor. Script operands address either
/// the player or one of the current map's object slots; object slots are the
/// zero-based `object_event` / `object_const_def` order.
type ActorId =
    | Player
    | Object of index: int

module Actor =

    let isPlayer (actor: ActorId) =
        match actor with
        | Player -> true
        | Object _ -> false

    let objectIndex (actor: ActorId) =
        match actor with
        | Object i -> Some i
        | Player -> None

    /// Resolve a script actor operand (`PLAYER`, `LAST_TALKED`, numeric ids, or an
    /// object constant) to a live actor id. Numeric object ids follow GSC script
    /// convention: object 1 is the first map object, so the runtime index is 0.
    let resolve (objectIndexOf: string -> int option) (lastTalked: ActorId option) (operand: string) : ActorId option =
        match operand.Trim().ToUpperInvariant() with
        | "PLAYER"
        | "0"
        | "-1" -> Some Player
        | "LAST_TALKED"
        | "-2" -> lastTalked
        | text ->
            match Int32.TryParse text with
            | true, n when n > 0 -> Some(Object(n - 1))
            | _ -> objectIndexOf operand |> Option.map Object

    let resolveObjectIndex objectIndexOf lastTalked operand =
        resolve objectIndexOf lastTalked operand |> Option.bind objectIndex
