namespace PokeGold.Game

open System.Collections.Generic
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Save
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
    let mutable overworld = OverworldScene.Load content

    do scenes.Push(overworld :> Scene)

    /// Make the given overworld scene the sole, bottom scene (used on load).
    member private _.ResetTo(ow: OverworldScene) =
        overworld <- ow
        scenes.Clear()
        scenes.Push(ow :> Scene)

    /// The framebuffer the host should present after each Tick.
    member _.Framebuffer = framebuffer

    /// Total frames advanced so far.
    member _.Frame = frame

    /// Capture the current overworld and write it to the save slot.
    member _.Save() = SaveFile.write (overworld.Capture())

    /// Load the save slot, if present, replacing the scene stack with the
    /// restored overworld. No-op when there's no readable save.
    member this.Load() =
        match SaveFile.tryRead () with
        | Some save -> this.ResetTo(OverworldScene.OfSave(content, save))
        | None -> ()

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

        // Render the scene stack bottom-to-top so overlays (text boxes, menus)
        // layer over the scenes beneath them. The overworld fills the screen and
        // sits at the bottom; a text box on top draws only its six-row box.
        let stack = scenes.ToArray() // index 0 = top
        for i in stack.Length - 1 .. -1 .. 0 do
            stack.[i].Render(framebuffer)
