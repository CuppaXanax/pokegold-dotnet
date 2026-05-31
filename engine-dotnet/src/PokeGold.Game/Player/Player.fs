namespace PokeGold.Game.Player

/// Player-facing game options (persisted in save).
type GameOptions =
    { TextSpeed: int   // 1=slow, 2=mid, 3=fast (GSC default = mid)
      BoxBorder: int   // 0-7, frame style
      Sound: int }     // 0=mono, 1=stereo

/// The full persistent player state.
type PlayerState =
    { Name: string
      Money: int
      Party: Party
      Bag: Bag
      DexSeen: Set<int>
      DexOwn: Set<int>
      Badges: int
      Options: GameOptions }

module PlayerState =

    let defaultOptions = { TextSpeed = 2; BoxBorder = 0; Sound = 0 }

    /// A brand-new game player state (empty party, no items, no dex).
    let initial =
        { Name = "PLAYER"
          Money = 3000
          Party = []
          Bag = Bag.empty
          DexSeen = Set.empty
          DexOwn = Set.empty
          Badges = 0
          Options = defaultOptions }
