namespace PokeGold.Game

open System.Collections.Generic
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Audio
open PokeGold.Game.Overworld
open PokeGold.Game.Overworld.Script
open PokeGold.Game.Player
open PokeGold.Game.Save
open PokeGold.Game.Scenes
open PokeGold.Game.Debug

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
    let audio = AudioEngine(44100)
    let scenes = Stack<Scene>()
    let mutable frame = 0UL
    let mutable overworld = OverworldScene.Load(content, audio)
    let hasSave = SaveFile.tryRead() |> Option.isSome

    /// Run the std-script InitializeEventsScript to set the ~60 event flags GSC
    /// expects at new-game time (hides cops, Rockets, rivals in wrong positions).
    let initializeNewGameWorld () : World =
        let stdProg = StdScriptsData.program
        let rec drive (step: ScriptStep) =
            match step.Outcome with
            | Completed -> step.World
            | Suspended(vm, _) -> drive (Script.resume None step.World vm)
        drive (Script.start "InitializeEventsScript" World.empty stdProg "")

    let newGameScene (name: string) : OverworldScene =
        let world = initializeNewGameWorld ()
        let state = OverworldState.loadById content "PlayersHouse2F"
        let ow = OverworldScene(content, audio, state)
        ow.Restore(world, { PlayerStateOps.initial with Name = name })
        ow

    let titleScene =
        TitleScene(content, fun () ->
            Replace(
                MainMenuScene(
                    content,
                    audio,
                    hasSave,
                    (fun () -> Replace(NamingScene(content.Font, "ENTER NAME", fun name -> Replace(newGameScene name)))),
                    (fun () ->
                        match SaveFile.tryRead() with
                        | Some save -> Replace(OverworldScene.OfSave(content, audio, save))
                        | None -> Stay),
                    (fun () -> Push(OptionsScene(content, PlayerStateOps.initial, fun _ -> ()))))))
    /// The debug command bridge. Background clients (the named pipe) submit
    /// commands here; they're drained on the game thread each Tick via `Pump`.
    let debug = DebugChannel()

    /// Buttons carried over from the press that opened the current top scene.
    /// While a masked button stays physically held it is hidden from the active
    /// scene, so the same press can't both open a menu and immediately act inside
    /// it (the input "debounce" every scene transition needs). Cleared per-button
    /// as the player releases each one.
    let mutable inputMask = Buttons.none

    /// Debug-injected input: a queue of button frames to OR into the host's physical
    /// input, one per Tick, until drained. Lets the debug pipe (an agent or a test)
    /// drive the *real* input path — walking, talking, opening menus — exactly as a
    /// player would. Mutated only on the game thread (from `RunDebugCommand`).
    let injected = Queue<Buttons>()

    do scenes.Push(titleScene :> Scene)

    /// Make the given overworld scene the sole, bottom scene (used on load).
    member private _.ResetTo(ow: OverworldScene) =
        overworld <- ow
        scenes.Clear()
        scenes.Push(ow :> Scene)

    /// Start a fresh adventure with a clean world and player state.
    member this.NewGame(playerName: string) =
        this.ResetTo(newGameScene playerName)

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
        | Some save -> this.ResetTo(OverworldScene.OfSave(content, audio, save))
        | None -> ()

    /// The sample rate of the audio mix the host should request.
    member _.AudioSampleRate = audio.SampleRate

    /// The debug command bridge a host can expose over a transport (e.g. a named
    /// pipe). Commands submitted here run on the game thread during `Tick`.
    member _.DebugChannel = debug

    /// Execute one debug command line against the live game and return its textual
    /// reply. Runs on the game thread (called from the channel's `Pump`), so it
    /// sees a coherent view of scene state and its mutations are frame-safe.
    member _.RunDebugCommand(line: string) : string =
        let top = scenes.Peek().GetType().Name
        let parts =
            line.Trim().Split([| ' '; '\t' |], System.StringSplitOptions.RemoveEmptyEntries)

        match (if parts.Length > 0 then parts.[0].ToLowerInvariant() else "") with
        // Capture the most recently rendered framebuffer to a PNG. Handled here
        // (not in DebugCommands) because the framebuffer is owned by the Game.
        // `screenshot [path]` — defaults to %TEMP%/pokegold/screenshot.png.
        | "screenshot" | "ss" | "capture" ->
            let path =
                if parts.Length > 1 then parts.[1]
                else
                    System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), "pokegold", "screenshot.png")
            try
                Png.writeFile path Display.Width Display.Height framebuffer.Pixels
                sprintf "wrote %d×%d screenshot: %s" Display.Width Display.Height path
            with ex ->
                sprintf "screenshot failed: %s" ex.Message
        // Inject input frames into the real input path. `hold <keys> [frames]` holds
        // the given buttons (e.g. `up`, `a`, `up+a`) for N frames (default 1); `press`
        // is shorthand for a one-frame tap. A grid step is 16 frames, so `hold up 16`
        // walks one cell. Lets an agent/test walk, talk and open menus over the pipe.
        | "hold" | "press" ->
            if parts.Length < 2 then "usage: hold <keys> [frames]   e.g. hold up 16"
            else
                let frame1 = Buttons.parse parts.[1]
                let n =
                    if parts.[0] = "press" then 1
                    elif parts.Length > 2 then
                        match System.Int32.TryParse parts.[2] with
                        | true, v -> max 1 v
                        | _ -> 1
                    else 1
                for _ in 1 .. n do injected.Enqueue frame1
                sprintf "ok: injected %s for %d frame(s)" parts.[1] n
        | _ -> DebugCommands.dispatch overworld frame top line

    /// Fill `buffer` with the next `nFrames` interleaved stereo sample-frames
    /// (range [-1, 1]). The host converts these to its device's PCM format.
    member _.MixAudio(buffer: float32[], nFrames: int) = audio.Mix(buffer, nFrames)

    /// Advance the game by one frame, consuming this frame's button state.
    member this.Tick(buttons: Buttons) =
        frame <- frame + 1UL

        // Apply any pending debug commands on the game thread before the scene
        // updates, so inspectors and mutations see this frame's starting state.
        debug.Pump(this.RunDebugCommand)

        // Merge one frame of debug-injected input (if any) over the physical input,
        // so a pipe client can drive the real input path frame-by-frame.
        let buttons =
            if injected.Count > 0 then Buttons.union buttons (injected.Dequeue())
            else buttons

        // Shrink the carry-over mask to buttons that are *still* held (a release
        // clears that button), then hide the remaining masked buttons from the
        // active scene. This debounces every scene transition: the button that
        // opened a menu won't register again until it's released and pressed anew.
        inputMask <- Buttons.intersect inputMask buttons
        let effective = Buttons.except buttons inputMask

        // A transition that changes which scene is on top re-arms the mask with
        // whatever is held this frame, so the newly-active scene starts clean.
        let armMask () = inputMask <- buttons

        match scenes.Peek().Update(effective) with
        | Stay -> ()
        | Push s -> scenes.Push s; armMask ()
        | Pop ->
            if scenes.Count > 1 then
                scenes.Pop() |> ignore
                armMask ()
        | Replace s ->
            scenes.Pop() |> ignore
            scenes.Push s
            armMask ()

        // Render the scene stack bottom-to-top so overlays (text boxes, menus)
        // layer over the scenes beneath them. The overworld fills the screen and
        // sits at the bottom; a text box on top draws only its six-row box.
        let stack = scenes.ToArray() // index 0 = top
        for i in stack.Length - 1 .. -1 .. 0 do
            stack.[i].Render(framebuffer)
