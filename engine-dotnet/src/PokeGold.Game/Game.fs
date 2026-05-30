namespace PokeGold.Game

open System.Collections.Generic
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Scenes

/// The platform-agnostic game: owns the framebuffer, the shared asset cache, and
/// a stack of scenes. Each tick it advances the active scene, applies any stack
/// transition it requests, then renders. The host depends only on `Tick` and
/// `Framebuffer`; it has no knowledge of scenes, the overworld, or assets.
///
/// The scene stack is what lets later modes (text boxes, menus, battles) layer
/// over the overworld and pop back without growing a monolithic update routine.
type Game() =
    let framebuffer = Framebuffer()
    let content = Content()
    let scenes = Stack<Scene>()
    let mutable frame = 0UL

    do scenes.Push(OverworldScene.Load content :> Scene)

    /// The framebuffer the host should present after each Tick.
    member _.Framebuffer = framebuffer

    /// Total frames advanced so far.
    member _.Frame = frame

    /// Advance the game by one frame, consuming this frame's button state.
    member _.Tick(buttons: Buttons) =
        frame <- frame + 1UL

        match scenes.Peek().Update(buttons) with
        | Stay -> ()
        | Push s -> scenes.Push s
        | Pop -> if scenes.Count > 1 then scenes.Pop() |> ignore
        | Replace s ->
            scenes.Pop() |> ignore
            scenes.Push s

        // Render the active scene. Transparent overlays (e.g. menus over the
        // overworld) can later render the stack bottom-to-top; for now the top
        // scene fills the screen.
        scenes.Peek().Render(framebuffer)
