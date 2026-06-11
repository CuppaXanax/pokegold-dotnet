namespace PokeGold.Game.Debug

open PokeGold.Game.Core
open PokeGold.Game.Overworld

type RuntimeActorSnapshot =
    { Index: int
      Sprite: string
      CellX: int
      CellY: int
      PixelX: int
      PixelY: int
      Facing: Direction
      Motion: string
      Visible: bool
      EventFlag: string option
      Script: string }

type RuntimePlayerSnapshot =
    { CellX: int
      CellY: int
      PixelX: int
      PixelY: int
      Facing: Direction
      Motion: string
      Moving: bool
      Name: string
      PartyCount: int
      PartySpecies: int list
      PhoneContacts: string list
      GameTimeWeekday: int
      GameTimeIsDst: bool
      Money: int }

type RuntimeOverworldSnapshot =
    { MapId: string
      Player: RuntimePlayerSnapshot
      Actors: RuntimeActorSnapshot list
      LastTextLabel: string option
      LastRenderedText: string option
      EventCount: int
      EngineFlagCount: int
      Events: string list
      EngineFlags: string list
      Vars: Map<string, int>
      Scenes: Map<string, int>
      SceneId: int
      CanCapture: bool }

type RuntimeSnapshot =
    { Frame: uint64
      /// Scene names ordered bottom-to-top, matching render order.
      SceneStack: string list
      TopScene: string
      Overworld: RuntimeOverworldSnapshot option }

type RuntimeControl =
    | Press of Buttons
    | Hold of Buttons * frames: int
    | LoadDebugAzalea
    | Teleport of x: int * y: int
    | Warp of mapId: string * x: int * y: int * facing: Direction option
    | SetEvent of flag: string * value: bool
    | SetFlag of flag: string * value: bool
    | SetVar of name: string * value: int
    | SetScene of mapId: string * scene: int
    | StartNewGame of playerName: string

type RuntimeControlResult =
    | Applied
    | Rejected of reason: string
