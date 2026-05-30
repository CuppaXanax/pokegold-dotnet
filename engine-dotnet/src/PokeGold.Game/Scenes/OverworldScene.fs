namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld
open PokeGold.Game.Render

/// The walk-around-the-map scene. Owns a mutable OverworldState that the pure
/// Overworld systems advance each frame; everything inside the state is
/// immutable, so this single `mutable` is the only piece of mutation.
type OverworldScene(initial: OverworldState) =
    let mutable state = initial

    /// Load the Azalea Town overworld scene through the shared asset cache.
    static member Load(content: Content) : OverworldScene =
        OverworldScene(OverworldState.loadAzalea content)

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            state <- OverworldState.tick buttons state
            Stay

        member _.Render(fb: Framebuffer) = OverworldRenderer.draw fb state
