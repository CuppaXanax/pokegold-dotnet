namespace PokeGold.Game.Scenes

open PokeGold.Game.Core

/// A scene is a self-contained mode of the game (overworld, menu, battle, …).
/// Each frame it updates from this frame's input and returns how the scene stack
/// should change, then renders itself into the framebuffer. Modes are kept on a
/// stack so a menu or battle can be pushed over the overworld and popped to
/// return — without `Tick` becoming an ever-growing match.
type Scene =
    /// Advance the scene by one frame and report the resulting stack transition.
    abstract member Update: Buttons -> Transition
    /// Draw the scene into the framebuffer.
    abstract member Render: Framebuffer -> unit

/// How the scene stack changes after a scene updates.
and Transition =
    | Stay
    | Push of Scene
    | Pop
    | Replace of Scene
