namespace PokeGold.Game.Player

/// Player-facing game options (persisted in save).
type GameOptions =
    { TextSpeed: int   // 1=slow, 2=mid, 3=fast (GSC default = mid)
      BoxBorder: int   // 0-7, frame style
      Sound: int }     // 0=mono, 1=stereo

/// The full persistent player state.
type PersistentPlayerState =
    { Name: string
      Money: int
      Party: Party
      Bag: Bag
      DexSeen: Set<int>
      DexOwn: Set<int>
      Badges: int
      Options: GameOptions
      Pc: PcStorage
      RepelSteps: int
      PhoneContacts: Set<string> }

/// Public alias for the persistent player state record.
type PlayerState = PersistentPlayerState

module Options =

    /// Convert a TextSpeed value (1=slow, 2=mid, 3=fast) to frames per glyph.
    /// GSC: FAST → 1 frame, MID → 3 frames, SLOW → 5 frames.
    let textSpeedDelay (textSpeed: int) : int =
        match textSpeed with
        | 3 -> 1   // FAST
        | 1 -> 5   // SLOW
        | _ -> 3   // MID (default)

module PlayerStateOps =

    let defaultOptions = { TextSpeed = 2; BoxBorder = 0; Sound = 0 }

    /// A brand-new game player state (empty party, no items, no dex, empty PC).
    let initial =
        { Name = "PLAYER"
          Money = 3000
          Party = []
          Bag = Bag.empty
          DexSeen = Set.empty
          DexOwn = Set.empty
          Badges = 0
          Options = defaultOptions
          Pc = Storage.empty
          RepelSteps = 0
          PhoneContacts = Set.empty }

module Repel =

    /// Check whether an active repel suppresses this wild encounter.
    let blocks (player: PlayerState) (wildLevel: int) : bool =
        player.RepelSteps > 0 &&
        match player.Party with
        | lead :: _ -> lead.Level >= wildLevel
        | [] -> false
