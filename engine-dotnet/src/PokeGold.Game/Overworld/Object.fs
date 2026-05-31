namespace PokeGold.Game.Overworld

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld.Script

/// How an overworld object decides its own movement, derived from its
/// `SPRITEMOVEDATA_*` row's movement function. Only the autonomous behaviours are
/// modelled here; every other function (follow, scripted, strength boulders,
/// bouncing Pokémon, the player's own d-pad function, …) maps to `StandStill` for
/// this slice — those objects simply hold their pose until a script moves them.
type MovementKind =
    | StandStill
    | RandomWalkXY
    | RandomWalkX
    | RandomWalkY
    | SlowSpin
    | FastSpin

/// What a live object is doing this frame. NPCs only ever stand or walk a tile
/// (they never hop or bump on their own); a turn is folded into the stand pose.
type NpcMotion =
    | NpcStanding
    | NpcWalking

/// A live overworld object: its home cell + wander radius (from the map's object
/// event), the `SPRITEMOVEDATA`-derived behaviour, and the per-frame motion state
/// advanced by the pure `ObjectStep` system. `Event` is the source object event,
/// kept for visibility (event flag / time-of-day), sprite art and script lookups.
type NpcObject =
    { /// The source map object event this live object was built from.
      Event: ObjectEvent
      /// The autonomous behaviour this object follows when idle.
      Kind: MovementKind
      /// Origin cell the wander radius is measured from.
      HomeX: int
      HomeY: int
      /// Wander radius in cells (0 = unlimited on that axis), low/high nibble of
      /// the object event's packed radius.
      RadiusX: int
      RadiusY: int
      /// The cell the object occupies (or is stepping into), 16-px grid.
      CellX: int
      CellY: int
      /// Cell the current step started from (for smooth interpolation).
      SrcX: int
      SrcY: int
      Facing: Direction
      Motion: NpcMotion
      /// Frames elapsed in the current step (0 .. StepFrames).
      Progress: int
      /// Free-running leg-animation tick, like the player's (mirrors GSC's
      /// `OBJECT_STEP_FRAME`); only advances while stepping so the legs alternate.
      AnimFrame: int
      /// Frames remaining before the next movement decision (the sleep countdown
      /// GSC keeps in `OBJECT_STEP_DURATION` between wander steps).
      Sleep: int
      /// LCG state, seeded per object so wandering is deterministic and testable.
      Seed: uint32 }

    /// True while the object is translating between cells.
    member this.Moving = (this.Motion = NpcWalking)

module NpcObject =

    /// Frames one autonomous NPC tile-step takes — matched to the player's walk so
    /// NPCs move at the same overworld pace.
    [<Literal>]
    let StepFrames = 16

    /// Parse a `DOWN`/`UP`/`LEFT`/`RIGHT` facing token to a `Direction`.
    let directionOf (s: string) : Direction =
        match s with
        | "UP" -> Up
        | "LEFT" -> Left
        | "RIGHT" -> Right
        | _ -> Down

    /// Map a `SPRITEMOVEFN_*` movement function to the behaviour we model.
    let kindOfFn (fn: string) : MovementKind =
        match fn with
        | "SPRITEMOVEFN_RANDOM_WALK_XY" -> RandomWalkXY
        | "SPRITEMOVEFN_RANDOM_WALK_X" -> RandomWalkX
        | "SPRITEMOVEFN_RANDOM_WALK_Y" -> RandomWalkY
        | "SPRITEMOVEFN_SLOW_RANDOM_SPIN" -> SlowSpin
        | "SPRITEMOVEFN_FAST_RANDOM_SPIN" -> FastSpin
        | _ -> StandStill

    /// Build a live object from a map object event. `seedSalt` (e.g. the object's
    /// index in the map's object table) decorrelates objects that share a home cell
    /// so they don't wander in lockstep.
    let fromEvent (seedSalt: int) (o: ObjectEvent) : NpcObject =
        let fn, facing =
            match Map.tryFind o.Movement SpriteMovementData.all with
            | Some(f, fc) -> f, fc
            | None -> "SPRITEMOVEFN_STANDING", "DOWN"

        let seed =
            ((uint32 o.X * 73856093u) ^^^ (uint32 o.Y * 19349663u) ^^^ (uint32 seedSalt * 83492791u))
            ||| 1u

        { Event = o
          Kind = kindOfFn fn
          HomeX = o.X
          HomeY = o.Y
          RadiusX = o.RadiusX
          RadiusY = o.RadiusY
          CellX = o.X
          CellY = o.Y
          SrcX = o.X
          SrcY = o.Y
          Facing = directionOf facing
          Motion = NpcStanding
          Progress = 0
          AnimFrame = 0
          // Stagger first decisions so a row of wanderers doesn't move in unison.
          Sleep = (o.X * 7 + o.Y * 13 + seedSalt * 5) % 32 + 8
          Seed = seed }

    /// The sprite top-left in world pixels, interpolated during a step.
    let worldPixel (n: NpcObject) : int * int =
        if n.Moving then
            let t = float n.Progress / float StepFrames
            let px = float (n.SrcX * 16) + float ((n.CellX - n.SrcX) * 16) * t
            let py = float (n.SrcY * 16) + float ((n.CellY - n.SrcY) * 16) * t
            int (round px), int (round py)
        else
            n.CellX * 16, n.CellY * 16

    /// The sprite frame index + horizontal flip for the object this frame, using
    /// the same 6-frame overworld sheet and leg cadence as the player.
    let frameAndFlip (n: NpcObject) : int * bool =
        let phase =
            match n.Motion with
            | NpcWalking -> (n.AnimFrame >>> 3) &&& 3
            | NpcStanding -> 0

        let stepping = phase &&& 1 = 1
        let footFlip = phase = 3

        match n.Facing with
        | Down -> (if stepping then 3 else 0), footFlip
        | Up -> (if stepping then 4 else 1), footFlip
        | Left -> (if stepping then 5 else 2), false
        | Right -> (if stepping then 5 else 2), true
