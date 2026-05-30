namespace PokeGold.Game.Save

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld

/// The overworld slice of a save: which map the player is on and where they
/// stand. Facing is stored as a stable string so the JSON stays readable and
/// resilient to reordering the `Direction` cases. `[<CLIMutable>]` lets
/// System.Text.Json populate it via its parameterless constructor.
[<CLIMutable>]
type OverworldSave =
    { MapId: string
      CellX: int
      CellY: int
      Facing: string }

/// A versioned save container. Today it carries only the overworld position;
/// future systems (party, bag, event flags) become sibling fields here, and the
/// `Version` lets `SaveFile` reject or migrate older shapes as they appear.
[<CLIMutable>]
type SaveData =
    { Version: int
      Overworld: OverworldSave }

module SaveData =

    /// The current on-disk schema version. Bump whenever the shape changes.
    [<Literal>]
    let CurrentVersion = 1

    let private facingToString (d: Direction) : string =
        match d with
        | Down -> "Down"
        | Up -> "Up"
        | Left -> "Left"
        | Right -> "Right"

    let private facingOfString (s: string) : Direction =
        match s with
        | "Up" -> Up
        | "Left" -> Left
        | "Right" -> Right
        | _ -> Down

    /// Snapshot the persistable state of a live overworld.
    let capture (s: OverworldState) : SaveData =
        { Version = CurrentVersion
          Overworld =
            { MapId = s.MapId
              CellX = s.Player.CellX
              CellY = s.Player.CellY
              Facing = facingToString s.Player.Facing } }

    /// Rebuild a live overworld from a save, restoring the player's map,
    /// cell, and facing. Requires the asset cache to reload the map.
    let apply (content: Content) (save: SaveData) : OverworldState =
        let ow = save.Overworld
        OverworldState.loadByIdAt content ow.MapId ow.CellX ow.CellY (facingOfString ow.Facing)
