namespace PokeGold.Game.Save

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script

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

/// A name→int pair (a `VAR_*` value or a map's scene id), serialized as an array
/// entry because System.Text.Json has no built-in F# `Map` converter.
[<CLIMutable>]
type NamedInt = { Name: string; Value: int }

/// A bag entry: an item constant and how many are held.
[<CLIMutable>]
type ItemSave = { Item: string; Qty: int }

/// The script world (`World`) flattened for JSON: the two flag sets as string
/// arrays, the vars and per-map scene ids as name/value arrays.
[<CLIMutable>]
type WorldSave =
    { Events: string[]
      EngineFlags: string[]
      Vars: NamedInt[]
      Scenes: NamedInt[] }

/// A versioned save container. Carries the overworld position, the script world
/// (event/engine flags, vars, scene ids), and the bag. The `Version` lets
/// `SaveFile` reject or migrate older shapes; a v1 save (position only) loads
/// with an empty world/bag.
[<CLIMutable>]
type SaveData =
    { Version: int
      Overworld: OverworldSave
      World: WorldSave
      Bag: ItemSave[] }

module SaveData =

    /// The current on-disk schema version. Bump whenever the shape changes.
    [<Literal>]
    let CurrentVersion = 2

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

    let private namedOfMap (m: Map<string, int>) : NamedInt[] =
        m |> Map.toArray |> Array.map (fun (n, v) -> { Name = n; Value = v })

    let private mapOfNamed (a: NamedInt[]) : Map<string, int> =
        if isNull a then Map.empty
        else a |> Array.map (fun e -> e.Name, e.Value) |> Map.ofArray

    let private setOfArray (a: string[]) : Set<string> =
        if isNull a then Set.empty else Set.ofArray a

    let private worldToSave (w: World) : WorldSave =
        { Events = Set.toArray w.Events
          EngineFlags = Set.toArray w.EngineFlags
          Vars = namedOfMap w.Vars
          Scenes = namedOfMap w.Scenes }

    /// The `World` a save restores (an absent/v1 block becomes the empty world).
    let worldOf (save: SaveData) : World =
        match box save.World with
        | null -> World.empty
        | _ ->
            let ws = save.World
            { Events = setOfArray ws.Events
              EngineFlags = setOfArray ws.EngineFlags
              Vars = mapOfNamed ws.Vars
              Scenes = mapOfNamed ws.Scenes }

    /// The bag a save restores (item constant → quantity).
    let bagOf (save: SaveData) : Map<string, int> =
        match box save.Bag with
        | null -> Map.empty
        | _ -> save.Bag |> Array.map (fun e -> e.Item, e.Qty) |> Map.ofArray

    /// Snapshot a live overworld plus its script world and bag into a save.
    let captureWith (s: OverworldState) (world: World) (bag: Map<string, int>) : SaveData =
        { Version = CurrentVersion
          Overworld =
            { MapId = s.MapId
              CellX = s.Player.CellX
              CellY = s.Player.CellY
              Facing = facingToString s.Player.Facing }
          World = worldToSave world
          Bag = bag |> Map.toArray |> Array.map (fun (i, q) -> { Item = i; Qty = q }) }

    /// Snapshot just the overworld position (empty world/bag) — the M7 entry point.
    let capture (s: OverworldState) : SaveData = captureWith s World.empty Map.empty

    /// Rebuild a live overworld from a save, restoring the player's map,
    /// cell, and facing. Requires the asset cache to reload the map.
    let apply (content: Content) (save: SaveData) : OverworldState =
        let ow = save.Overworld
        OverworldState.loadByIdAt content ow.MapId ow.CellX ow.CellY (facingOfString ow.Facing)
