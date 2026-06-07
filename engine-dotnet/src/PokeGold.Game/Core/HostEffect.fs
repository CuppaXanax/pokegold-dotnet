namespace PokeGold.Game.Core

/// Side effects requested by pure-ish game/runtime logic and interpreted by the
/// host-facing scene shell. This starts carving a data boundary between game
/// decisions and platform effects (audio, scene stack, rendering, etc.).
type HostEffect =
    | PlayMusic of path: string
    | StopMusic
    | PlaySfx of name: string
    | PlayJingle of path: string
