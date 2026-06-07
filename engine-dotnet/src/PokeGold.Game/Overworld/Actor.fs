namespace PokeGold.Game.Overworld

open System
open PokeGold.Game.Core

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

    type Pose =
        { CellX: int
          CellY: int
          Facing: Direction }

    let tryPose (player: PlayerState) (npcs: NpcObject[]) actor =
        match actor with
        | Player ->
            Some
                { CellX = player.CellX
                  CellY = player.CellY
                  Facing = player.Facing }
        | Object idx when idx >= 0 && idx < npcs.Length ->
            let npc = npcs.[idx]
            Some
                { CellX = npc.CellX
                  CellY = npc.CellY
                  Facing = npc.Facing }
        | Object _ -> None

    let tryCell player npcs actor =
        tryPose player npcs actor |> Option.map (fun p -> p.CellX, p.CellY)

    let isMoving (player: PlayerState) (npcs: NpcObject[]) actor =
        match actor with
        | Player -> player.Moving
        | Object idx when idx >= 0 && idx < npcs.Length -> npcs.[idx].Moving
        | Object _ -> false

    let private updateObject idx f (npcs: NpcObject[]) =
        if idx >= 0 && idx < npcs.Length then
            let copy = Array.copy npcs
            copy.[idx] <- f copy.[idx]
            copy
        else
            npcs

    let setFacing actor facing (player: PlayerState) (npcs: NpcObject[]) =
        match actor with
        | Player -> { player with Facing = facing }, npcs
        | Object idx -> player, updateObject idx (fun npc -> { npc with Facing = facing }) npcs

    let place actor x y (player: PlayerState) (npcs: NpcObject[]) =
        match actor with
        | Player ->
            { player with
                CellX = x
                CellY = y
                SrcX = x
                SrcY = y
                Motion = Standing
                Progress = 0
                Bumped = false },
            npcs
        | Object idx ->
            player,
            updateObject
                idx
                (fun npc ->
                    { npc with
                        HomeX = x
                        HomeY = y
                        CellX = x
                        CellY = y
                        SrcX = x
                        SrcY = y
                        Motion = NpcStanding
                        Progress = 0 })
                npcs

    let beginStep actor facing targetX targetY (player: PlayerState) (npcs: NpcObject[]) =
        match actor with
        | Player ->
            { player with
                Facing = facing
                SrcX = player.CellX
                SrcY = player.CellY
                CellX = targetX
                CellY = targetY
                Motion = Walking
                Progress = 0
                Bumped = false },
            npcs
        | Object idx ->
            player,
            updateObject
                idx
                (fun npc ->
                    { npc with
                        Facing = facing
                        SrcX = npc.CellX
                        SrcY = npc.CellY
                        CellX = targetX
                        CellY = targetY
                        Motion = NpcWalking
                        Progress = 0 })
                npcs
