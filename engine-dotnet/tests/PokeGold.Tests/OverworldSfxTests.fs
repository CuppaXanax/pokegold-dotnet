module PokeGold.Tests.OverworldSfxTests

open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Audio
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Scenes

// M10.8 — the overworld scene turns locomotion into sound: a ledge hop and a wall
// bump each play their GSC SFX once as the action begins, and plain walking stays
// silent. These drive a real OverworldScene over the actual Azalea Town map through
// a recording soundboard that captures every PlaySfx call.

/// An ISoundBoard that records the SFX names it is asked to play (and ignores music).
type private RecordingSound() =
    let sfx = ResizeArray<string>()
    member _.Sfx = List.ofSeq sfx

    interface ISoundBoard with
        member _.PlayMusic _ = ()
        member _.PlaySfx name = sfx.Add name
        member _.PlayJingle _ = ()
        member _.StopMusic() = ()

let private delta (dir: Direction) : int * int =
    match dir with
    | Down -> 0, 1
    | Up -> 0, -1
    | Left -> -1, 0
    | Right -> 1, 0

let private press (dir: Direction) : Buttons =
    match dir with
    | Down -> { Buttons.none with Down = true }
    | Up -> { Buttons.none with Up = true }
    | Left -> { Buttons.none with Left = true }
    | Right -> { Buttons.none with Right = true }

/// A real ledge on Azalea Town whose forward cell is blocked and whose two-cell
/// landing is in-bounds, with the facing that hops it. (Same probe as MovementTests.)
let private findLedge (map: GameMap) (coll: Collision) =
    let cellsW = map.Width * 2
    let cellsH = map.Height * 2

    seq {
        for cy in 0 .. cellsH - 1 do
            for cx in 0 .. cellsW - 1 do
                match Collision.tryLedge (Movement.collisionIdAtCell map coll cx cy) with
                | Some dirs ->
                    for d in dirs do
                        let dx, dy = delta d
                        let lx, ly = cx + 2 * dx, cy + 2 * dy

                        if
                            not (Movement.cellWalkable map coll (cx + dx) (cy + dy))
                            && lx >= 0
                            && ly >= 0
                            && lx < cellsW
                            && ly < cellsH
                        then
                            yield (cx, cy), d
                | None -> ()
    }
    |> Seq.tryHead

/// A walkable neighbour of the default start cell, with the facing that steps onto it.
let private findOpenStep (map: GameMap) (coll: Collision) =
    let sx, sy = Movement.findStartCell map coll

    [ Down; Up; Left; Right ]
    |> List.tryPick (fun d ->
        let dx, dy = delta d

        if Movement.cellWalkable map coll (sx + dx) (sy + dy) then
            Some((sx, sy), d)
        else
            None)

let private scriptedScene content mapId x y facing label commands =
    let baseState = OverworldState.loadByIdAt content mapId x y facing

    { baseState with
        Events =
            { baseState.Events with
                Scenes = [| "SCENE_TEST" |]
                SceneLabels = [| label |]
                Coords = [||]
                Callbacks = [||] }
        Script =
            { Commands = commands
              Labels = Map.ofList [ label, 0 ] } }

[<Fact>]
let ``hopping a ledge plays the ledge SFX exactly once`` () =
    let content = Content()
    let probe = OverworldState.loadByIdAt content "AzaleaTown" 0 0 Down

    match findLedge probe.Map probe.Collision with
    | None -> failwith "no usable ledge found on Azalea Town"
    | Some((cx, cy), d) ->
        let sound = RecordingSound()
        let state = OverworldState.loadByIdAt content "AzaleaTown" cx cy d
        let scene = OverworldScene(content, sound, state) :> Scene

        // Drive the whole hop: one frame to start it, then through the arc.
        for _ in 0 .. Player.HopFrames do
            scene.Update(press d) |> ignore

        Assert.Equal(1, sound.Sfx |> List.filter ((=) "Sfx_JumpOverLedge") |> List.length)

[<Fact>]
let ``bumping a wall plays the bump SFX exactly once`` () =
    let content = Content()
    // Cell (0, y) facing into the map's left edge: the target cell is out of bounds,
    // so the step is blocked and (being no ledge) it bumps.
    let sound = RecordingSound()
    let state = OverworldState.loadByIdAt content "AzaleaTown" 0 8 Left
    let scene = OverworldScene(content, sound, state) :> Scene

    for _ in 0 .. Player.StepFrames do
        scene.Update(press Left) |> ignore

    Assert.Equal(1, sound.Sfx |> List.filter ((=) "Sfx_Bump") |> List.length)

[<Fact>]
let ``walking onto open ground is silent`` () =
    let content = Content()
    let probe = OverworldState.loadByIdAt content "AzaleaTown" 0 0 Down

    match findOpenStep probe.Map probe.Collision with
    | None -> failwith "no open step found on Azalea Town"
    | Some((cx, cy), d) ->
        let sound = RecordingSound()
        let state = OverworldState.loadByIdAt content "AzaleaTown" cx cy d
        let scene = OverworldScene(content, sound, state) :> Scene

        for _ in 0 .. Player.StepFrames do
            scene.Update(press d) |> ignore

        Assert.DoesNotContain("Sfx_JumpOverLedge", sound.Sfx)
        Assert.DoesNotContain("Sfx_Bump", sound.Sfx)

[<Fact>]
let ``script cry command plays pokemon cry sfx`` () =
    let content = Content()
    let sound = RecordingSound()
    let state =
        scriptedScene
            content
            "NewBarkTown"
            5
            5
            Down
            "CryScene"
            [| ScriptCommand.Cry "GYARADOS"; End |]

    let scene = OverworldScene(content, sound, state)
    scene.Restore(World.empty, PlayerStateOps.initial)

    Assert.Contains(Cries.sfxName "GYARADOS", sound.Sfx)
