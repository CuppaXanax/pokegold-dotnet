namespace PokeGold.Game.Save

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player

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

[<CLIMutable>]
type MovesSave = { MoveId: int; Pp: int }

[<CLIMutable>]
type PartyMonSave =
    { SpeciesId: int; Nickname: string; Level: int; Exp: int
      Hp: int; MaxHp: int; Status: string
      Moves: MovesSave[]; Dvs: int; StatExp: int
      HeldItem: string; OtName: string; OtId: int; Friendship: int }

[<CLIMutable>]
type MailSave = { Author: string; Body: string; Species: int }

[<CLIMutable>]
type BoxSave = { Name: string; Mons: PartyMonSave[] }

[<CLIMutable>]
type PcStorageSave =
    { Boxes: BoxSave[]
      CurrentBox: int
      PcItems: ItemSave[]
      Mailbox: MailSave[] }

[<CLIMutable>]
type PocketSave = { Items: ItemSave[]; Balls: ItemSave[]; KeyItems: ItemSave[]; TmHm: ItemSave[] }

[<CLIMutable>]
type GameOptionsSave = { TextSpeed: int; BoxBorder: int; Sound: int }

[<CLIMutable>]
type PlayerSave =
    { Name: string; Money: int
      Party: PartyMonSave[]
      PocketedBag: PocketSave
      DexSeen: int[]; DexOwn: int[]
      Badges: int; Options: GameOptionsSave
      Pc: PcStorageSave }

/// A versioned save container. Carries the overworld position, the script world
/// (event/engine flags, vars, scene ids), and the player state (party, bag, dex).
/// v3 saves have a full Player block; v2 saves only have a flat Bag array.
/// The `Version` lets `SaveFile` reject or migrate older shapes.
[<CLIMutable>]
type SaveData =
    { Version: int
      Overworld: OverworldSave
      World: WorldSave
      Bag: ItemSave[]           // kept for v1/v2 compat; v3 uses Player.PocketedBag
      Player: PlayerSave }

module SaveData =

    /// The current on-disk schema version. Bump whenever the shape changes.
    [<Literal>]
    let CurrentVersion = 4

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

    /// The bag a save restores (item constant → quantity). For v2/v1 saves only.
    let bagOf (save: SaveData) : Map<string, int> =
        match box save.Bag with
        | null -> Map.empty
        | _ -> save.Bag |> Array.map (fun e -> e.Item, e.Qty) |> Map.ofArray

    // Null-safe array helpers
    let private nullToEmpty (a: 'a[]) = if isNull a then [||] else a
    let private nullToEmptyStr (s: string) = if isNull s then "" else s
    let private nullToNone (s: string) = if isNull s || s = "" then None else Some s

    // ItemSave[] <-> (string*int) list
    let private itemSavesToList (a: ItemSave[]) : (string*int) list =
        if isNull a then []
        else a |> Array.map (fun x -> x.Item, x.Qty) |> Array.toList

    let private listToItemSaves (lst: (string*int) list) : ItemSave[] =
        lst |> List.map (fun (i, q) -> { Item = i; Qty = q }) |> List.toArray

    // PartyMon conversions
    let private partyMonToSave (pm: PartyMon) : PartyMonSave =
        { SpeciesId = pm.SpeciesId; Nickname = pm.Nickname; Level = pm.Level; Exp = pm.Exp
          Hp = pm.Hp; MaxHp = pm.MaxHp; Status = pm.Status
          Moves = pm.Moves |> List.map (fun (mid, pp) -> { MoveId = mid; Pp = pp }) |> List.toArray
          Dvs = pm.Dvs; StatExp = pm.StatExp
          HeldItem = pm.HeldItem |> Option.defaultValue ""
          OtName = pm.OtName; OtId = pm.OtId; Friendship = pm.Friendship }

    let private partyMonOfSave (s: PartyMonSave) : PartyMon =
        { SpeciesId = s.SpeciesId; Nickname = nullToEmptyStr s.Nickname
          Level = s.Level; Exp = s.Exp; Hp = s.Hp; MaxHp = s.MaxHp
          Status = nullToEmptyStr s.Status
          Moves = nullToEmpty s.Moves |> Array.map (fun m -> m.MoveId, m.Pp) |> Array.toList
          Dvs = s.Dvs; StatExp = s.StatExp
          HeldItem = nullToNone s.HeldItem
          OtName = nullToEmptyStr s.OtName; OtId = s.OtId; Friendship = s.Friendship }

    // Bag conversions
    let private bagToSave (bag: Bag) : PocketSave =
        { Items = listToItemSaves bag.Items
          Balls = listToItemSaves bag.Balls
          KeyItems = listToItemSaves bag.KeyItems
          TmHm = listToItemSaves bag.TmHm }

    let private bagOfSave (ps: PocketSave) : Bag =
        if box ps = null then Bag.empty
        else
            { Items = itemSavesToList ps.Items
              Balls = itemSavesToList ps.Balls
              KeyItems = itemSavesToList ps.KeyItems
              TmHm = itemSavesToList ps.TmHm }

    let private optionsToSave (o: GameOptions) : GameOptionsSave =
        { TextSpeed = o.TextSpeed; BoxBorder = o.BoxBorder; Sound = o.Sound }

    let private optionsOfSave (s: GameOptionsSave) : GameOptions =
        if box s = null then PlayerState.defaultOptions
        else { TextSpeed = s.TextSpeed; BoxBorder = s.BoxBorder; Sound = s.Sound }

    // PcStorage conversions
    let private mailToSave (m: Mail) : MailSave =
        { Author = m.Author; Body = m.Body; Species = m.Species }

    let private mailOfSave (s: MailSave) : Mail =
        { Author = nullToEmptyStr s.Author; Body = nullToEmptyStr s.Body; Species = s.Species }

    let private boxToSave (b: Box) : BoxSave =
        { Name = b.Name; Mons = b.Mons |> List.map partyMonToSave |> List.toArray }

    let private boxOfSave (s: BoxSave) : Box =
        { Name = nullToEmptyStr s.Name
          Mons = nullToEmpty s.Mons |> Array.map partyMonOfSave |> Array.toList }

    let private pcToSave (pc: PcStorage) : PcStorageSave =
        { Boxes = pc.Boxes |> Array.map boxToSave
          CurrentBox = pc.CurrentBox
          PcItems = listToItemSaves pc.PcItems
          Mailbox = pc.Mailbox |> List.map mailToSave |> List.toArray }

    let private pcOfSave (s: PcStorageSave) : PcStorage =
        if box s = null then Storage.empty
        else
            let savedBoxes = nullToEmpty s.Boxes
            { Boxes =
                if savedBoxes.Length = Storage.numBoxes then
                    savedBoxes |> Array.map boxOfSave
                else
                    // Migrate: fill missing boxes with named empty boxes.
                    Array.init Storage.numBoxes (fun i ->
                        if i < savedBoxes.Length then boxOfSave savedBoxes.[i]
                        else { Name = sprintf "BOX %d" (i + 1); Mons = [] })
              CurrentBox = s.CurrentBox
              PcItems = itemSavesToList s.PcItems
              Mailbox = nullToEmpty s.Mailbox |> Array.map mailOfSave |> Array.toList }

    let private playerToSave (p: PlayerState) : PlayerSave =
        { Name = p.Name; Money = p.Money
          Party = p.Party |> List.map partyMonToSave |> List.toArray
          PocketedBag = bagToSave p.Bag
          DexSeen = p.DexSeen |> Set.toArray; DexOwn = p.DexOwn |> Set.toArray
          Badges = p.Badges; Options = optionsToSave p.Options
          Pc = pcToSave p.Pc }

    /// The PlayerState a save restores. For v1/v2 saves (no Player block),
    /// migrates the flat Bag to a pocketed Bag; party/dex/money start empty/zero.
    /// For v3 saves (no Pc block), the PC is initialised to Storage.empty.
    let playerOf (save: SaveData) : PlayerState =
        match box save.Player with
        | null ->
            // v1/v2 migration: re-pocket the flat bag from the Bag field
            let flatBag = bagOf save
            { PlayerState.initial with Bag = Bag.ofFlat flatBag }
        | _ ->
            let ps = save.Player
            { Name = nullToEmptyStr ps.Name
              Money = ps.Money
              Party = nullToEmpty ps.Party |> Array.map partyMonOfSave |> Array.toList
              Bag = bagOfSave ps.PocketedBag
              DexSeen = nullToEmpty ps.DexSeen |> Set.ofArray
              DexOwn = nullToEmpty ps.DexOwn |> Set.ofArray
              Badges = ps.Badges
              Options = optionsOfSave ps.Options
              Pc = pcOfSave ps.Pc }

    /// Snapshot a live overworld plus its script world, bag, and player state into a save.
    let captureWith (s: OverworldState) (world: World) (player: PlayerState) : SaveData =
        { Version = CurrentVersion
          Overworld = { MapId = s.MapId; CellX = s.Player.CellX; CellY = s.Player.CellY; Facing = facingToString s.Player.Facing }
          World = worldToSave world
          Bag = player.Bag |> Bag.toFlat |> Map.toArray |> Array.map (fun (i, q) -> { Item = i; Qty = q })
          Player = playerToSave player }

    /// Snapshot just the overworld position (empty world/player) — the M7 entry point.
    let capture (s: OverworldState) : SaveData = captureWith s World.empty PlayerState.initial

    /// Rebuild a live overworld from a save, restoring the player's map,
    /// cell, and facing. Requires the asset cache to reload the map.
    let apply (content: Content) (save: SaveData) : OverworldState =
        let ow = save.Overworld
        OverworldState.loadByIdAt content ow.MapId ow.CellX ow.CellY (facingOfString ow.Facing)
