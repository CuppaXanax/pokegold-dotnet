module PokeGold.Tests.GameDriver

open System
open PokeGold.Game
open PokeGold.Game.Core
open PokeGold.Game.Debug

type TickTrace =
    { Frame: uint64
      HostInput: Buttons
      Snapshot: RuntimeSnapshot }

type GameDriver(?game: Game) =
    let game = defaultArg game (Game())
    let trace = ResizeArray<TickTrace>()

    member _.Game = game

    member _.Snapshot = game.Snapshot

    member _.Trace = trace |> Seq.toList

    member _.Apply(control: RuntimeControl) =
        match game.ApplyControl control with
        | Applied -> ()
        | Rejected reason -> invalidOp reason

    member _.Tick(buttons: Buttons) =
        game.Tick buttons
        let snapshot = game.Snapshot
        trace.Add(
            { Frame = snapshot.Frame
              HostInput = buttons
              Snapshot = snapshot })
        snapshot

    member this.Tick() = this.Tick Buttons.none

    member this.Hold(buttons: Buttons, frames: int) =
        if frames < 1 then invalidArg (nameof frames) "frames must be >= 1"
        for _ in 1 .. frames do
            this.Tick(buttons) |> ignore
        this.Tick() |> ignore

    member this.Press(buttons: Buttons) =
        this.Hold(buttons, 1)

    member this.Step(direction: Direction) =
        let buttons =
            match direction with
            | Up -> { Buttons.none with Up = true }
            | Down -> { Buttons.none with Down = true }
            | Left -> { Buttons.none with Left = true }
            | Right -> { Buttons.none with Right = true }

        this.Hold(buttons, 16)

    member this.Talk() =
        this.Press({ Buttons.none with A = true })

    member this.RunUntil(predicate: RuntimeSnapshot -> bool, maxFrames: int) =
        if maxFrames < 1 then invalidArg (nameof maxFrames) "maxFrames must be >= 1"

        let mutable remaining = maxFrames
        while remaining > 0 && not (predicate game.Snapshot) do
            this.Tick() |> ignore
            remaining <- remaining - 1

        if not (predicate game.Snapshot) then
            let stack = String.concat " > " game.Snapshot.SceneStack
            invalidOp $"Condition was not reached within {maxFrames} frame(s). Last scene stack: {stack}"

        game.Snapshot
