module PokeGold.Tests.RuntimeInvariants

open Xunit
open PokeGold.Game.Debug

type InvariantFailure =
    { Name: string
      Frame: uint64
      Message: string }

type RuntimeInvariant = RuntimeSnapshot -> InvariantFailure list

let private fail name (snapshot: RuntimeSnapshot) message =
    { Name = name
      Frame = snapshot.Frame
      Message = message }

let sceneStackIsConsistent: RuntimeInvariant =
    fun snapshot ->
        match List.rev snapshot.SceneStack with
        | top :: _ when top = snapshot.TopScene -> []
        | top :: _ ->
            [ fail "scene-stack-consistent" snapshot $"top scene '{snapshot.TopScene}' does not match stack top '{top}'" ]
        | [] ->
            [ fail "scene-stack-consistent" snapshot "scene stack is empty" ]

let noVisibleActorOverlapsPlayer: RuntimeInvariant =
    fun snapshot ->
        match snapshot.Overworld with
        | None -> []
        | Some ow ->
            ow.Actors
            |> List.filter (fun actor ->
                actor.Visible
                && actor.CellX = ow.Player.CellX
                && actor.CellY = ow.Player.CellY)
            |> List.map (fun actor ->
                fail
                    "no-actor-player-overlap"
                    snapshot
                    $"visible actor {actor.Index} ({actor.Sprite}) overlaps player at {ow.Player.CellX},{ow.Player.CellY}")

let noVisibleActorOverlapsVisibleActor: RuntimeInvariant =
    fun snapshot ->
        match snapshot.Overworld with
        | None -> []
        | Some ow ->
            ow.Actors
            |> List.filter (fun actor -> actor.Visible)
            |> List.groupBy (fun actor -> actor.CellX, actor.CellY)
            |> List.collect (fun ((x, y), actors) ->
                match actors with
                | []
                | [ _ ] -> []
                | _ ->
                    let ids =
                        actors
                        |> List.map (fun actor -> string actor.Index)
                        |> String.concat ", "

                    [ fail "no-actor-actor-overlap" snapshot $"visible actors [{ids}] overlap at {x},{y}" ])

let noUnresolvedRenderedTextPlaceholders: RuntimeInvariant =
    fun snapshot ->
        match snapshot.Overworld |> Option.bind (fun ow -> ow.LastRenderedText) with
        | None -> []
        | Some text ->
            [ "STRING_BUFFER"; "<RAM_"; "ItemText"; "PokegearName"; "<PLAYER>"; "<RIVAL>"; "<MOM>" ]
            |> List.filter text.Contains
            |> List.map (fun token ->
                fail "no-unresolved-rendered-text" snapshot $"rendered text still contains '{token}': {text}")

let core =
    [ sceneStackIsConsistent
      noVisibleActorOverlapsPlayer
      noVisibleActorOverlapsVisibleActor
      noUnresolvedRenderedTextPlaceholders ]

let failures invariants snapshot =
    invariants |> List.collect (fun invariant -> invariant snapshot)

let assertHold invariants snapshot =
    let failures = failures invariants snapshot
    let message =
        failures
        |> List.map (fun f -> $"{f.Name}@{f.Frame}: {f.Message}")
        |> String.concat "\n"

    Assert.True(List.isEmpty failures, message)
