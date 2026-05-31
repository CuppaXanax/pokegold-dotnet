namespace PokeGold.Game.Overworld

open PokeGold.Game.Core
open PokeGold.Game.Overworld.Script

/// Drives a single overworld object through an `applymovement` movement script,
/// one frame at a time. A high-level re-expression of GSC's movement-object engine:
/// each directional command walks the actor one tile (collision-checked), turns are
/// instantaneous, sleeps hold position, and `step_end` finishes — at which point the
/// suspended map script resumes. Speed variants animate at the normal tile pace; the
/// behaviour (path + final facing) is what matters for the high-level port.
module MovementRunner =

    /// A movement script in flight over one object.
    type Run =
        { Cmds: MovementCmd[]
          /// Index of the command currently executing (or about to).
          Index: int
          /// Frames left to hold for a `step_sleep`.
          Wait: int
          /// The actor being animated.
          Npc: NpcObject }

        /// True once the script has run off its end (or hit `step_end`).
        member this.Done = this.Index >= this.Cmds.Length

    let private delta (d: Direction) : int * int =
        match d with
        | Down -> 0, 1
        | Up -> 0, -1
        | Left -> -1, 0
        | Right -> 1, 0

    let private dirOf (i: int) : Direction =
        match i &&& 3 with
        | 0 -> Down
        | 1 -> Up
        | 2 -> Left
        | _ -> Right

    /// The direction operand of a directional command, if it has one.
    let private cmdDir (c: MovementCmd) : int option =
        match c with
        | MoveStep d
        | MoveBigStep d
        | MoveSlowStep d
        | MoveTurnStep d
        | MoveSlideStep d
        | MoveJumpStep d
        | MoveTurnHead d -> Some d
        | _ -> None

    /// Set up the command at `Index`, advancing instantly through zero-frame commands
    /// (turns, no-ops) until the actor is mid-walk, mid-sleep, or finished. The result
    /// is never left "ready": callers can rely on `step` only advancing motion.
    let rec private enter (walkable: int -> int -> bool) (r: Run) : Run =
        if r.Index >= r.Cmds.Length then
            r
        else
            match r.Cmds.[r.Index] with
            | MoveStepEnd -> { r with Index = r.Cmds.Length }
            | MoveTurnHead d -> enter walkable { r with Npc = { r.Npc with Facing = dirOf d }; Index = r.Index + 1 }
            | MoveUnsupported _ -> enter walkable { r with Index = r.Index + 1 }
            | MoveStepSleep n -> { r with Wait = max 1 n }
            | c ->
                // A walking command: step the tile if it's clear, else just face it.
                let dir = cmdDir c |> Option.defaultValue 0 |> dirOf
                let dx, dy = delta dir
                let tx, ty = r.Npc.CellX + dx, r.Npc.CellY + dy

                if walkable tx ty then
                    { r with
                        Npc =
                            { r.Npc with
                                Facing = dir
                                Motion = NpcWalking
                                SrcX = r.Npc.CellX
                                SrcY = r.Npc.CellY
                                CellX = tx
                                CellY = ty
                                Progress = 0 } }
                else
                    enter walkable { r with Npc = { r.Npc with Facing = dir }; Index = r.Index + 1 }

    /// Begin running `cmds` on `npc`. The returned `Run` is immediately settled into
    /// its first motion/sleep (or already `Done` for an empty/`step_end`-only script).
    let start (walkable: int -> int -> bool) (cmds: MovementCmd[]) (npc: NpcObject) : Run =
        enter
            walkable
            { Cmds = cmds
              Index = 0
              Wait = 0
              Npc = { npc with Motion = NpcStanding; Progress = 0 } }

    /// Advance the run by one frame: progress an in-flight walk or sleep, and step to
    /// the next command at each boundary.
    let step (walkable: int -> int -> bool) (r: Run) : Run =
        if r.Done then
            r
        elif r.Npc.Motion = NpcWalking then
            let p = r.Npc.Progress + 1

            if p >= NpcObject.StepFrames then
                enter
                    walkable
                    { r with
                        Npc =
                            { r.Npc with
                                Motion = NpcStanding
                                Progress = 0
                                SrcX = r.Npc.CellX
                                SrcY = r.Npc.CellY
                                AnimFrame = r.Npc.AnimFrame + 1 }
                        Index = r.Index + 1 }
            else
                { r with Npc = { r.Npc with Progress = p; AnimFrame = r.Npc.AnimFrame + 1 } }
        elif r.Wait > 0 then
            let w = r.Wait - 1

            if w = 0 then
                enter walkable { r with Wait = 0; Index = r.Index + 1 }
            else
                { r with Wait = w }
        else
            // Settled but not moving/sleeping (e.g. all remaining commands were
            // instant): re-enter to make progress.
            enter walkable r
