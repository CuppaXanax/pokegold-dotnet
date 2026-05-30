namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Audio
open PokeGold.Game.Overworld
open PokeGold.Game.Render
open PokeGold.Game.Save

/// The walk-around-the-map scene. Owns a mutable OverworldState that the pure
/// Overworld systems advance each frame; everything inside the state is
/// immutable, so this single `mutable` is the only piece of mutation.
type OverworldScene(content: Content, sound: ISoundBoard, initial: OverworldState) =
    let mutable state = initial
    let mutable prevA = false
    let mutable prevStart = false

    /// Start this map's background music as soon as the scene exists.
    do sound.PlayMusic(OverworldScene.musicFor initial.MapId)

    /// A real Azalea Town sign/NPC text, demonstrating the M5 text engine end to
    /// end: literal glyphs, `<LINE>`, `<CONT>` (scroll), `<PARA>` (clear), `<DONE>`.
    static member val DemoText =
        "Did you come to<LINE>get KURT to make<CONT>some BALLS?<PARA>A lot of people do<LINE>just that.<DONE>"

    /// The repo-relative music file for a map id (the overworld BGM).
    static member private musicFor(mapId: string) : string =
        match mapId with
        | _ -> "audio/music/azaleatown.asm"

    /// Load the Azalea Town overworld scene through the shared asset cache.
    static member Load(content: Content, sound: ISoundBoard) : OverworldScene =
        OverworldScene(content, sound, OverworldState.loadAzalea content)

    /// Restore an overworld scene from a save.
    static member OfSave(content: Content, sound: ISoundBoard, save: SaveData) : OverworldScene =
        OverworldScene(content, sound, SaveData.apply content save)

    /// Snapshot this scene's persistable state for a save.
    member _.Capture() : SaveData = SaveData.capture state

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let aPressed = buttons.A && not prevA
            let startPressed = buttons.Start && not prevStart
            prevA <- buttons.A
            prevStart <- buttons.Start

            // Pressing Start (while standing still) starts a scripted wild battle.
            if startPressed && not state.Player.Moving then
                sound.PlaySfx "Sfx_Menu"
                Push(BattleScene.StartDemo(content) :> Scene)
            // Pressing A while standing still opens a sample speech box.
            elif aPressed && not state.Player.Moving then
                sound.PlaySfx "Sfx_Menu"
                Push(TextBoxScene.Of(content, OverworldScene.DemoText) :> Scene)
            else
                state <- OverworldState.tick buttons state
                Stay

        member _.Render(fb: Framebuffer) = OverworldRenderer.draw fb state
