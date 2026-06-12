namespace PokeGold.Game.Audio

open System
open System.Collections.Generic

/// The narrow audio interface scenes use, so they can ask for music/SFX without
/// depending on the synth or the host's sound device.
type ISoundBoard =
    /// Start (and loop) the music file at the given repo-relative path. A no-op if
    /// that track is already playing.
    abstract PlayMusic: string -> unit
    /// Play a named SFX from audio/sfx.asm once, layered over the music.
    abstract PlaySfx: string -> unit
    /// Play a *music* file once (non-looping), layered over the current track —
    /// used for short fanfares like the Pokémon Center heal jingle, which must
    /// play through and stop rather than loop as background music.
    abstract PlayJingle: string -> unit
    /// Stop the current music.
    abstract StopMusic: unit -> unit

/// Mixes one looping music track with any number of transient SFX into a stereo
/// PCM stream. Pure (no MonoGame): the host pulls samples via `Mix` and presents
/// them through its own sound device. Lightly locked so the game thread can change
/// what's playing while the host thread pulls samples.
type AudioEngine(sampleRate: int) =
    let sync = obj ()
    let sfx = List<SongPlayer>()
    let mutable music : SongPlayer option = None
    let mutable musicPath = ""

    // Four channels can sum to ±4 before gain; these keep the mix clear of the
    // hard clip most of the time, and the soft clamp catches the rest.
    let musicGain = 0.20
    let sfxGain = 0.30

    member _.SampleRate = sampleRate

    member _.PlayMusic(path: string) =
        lock sync (fun () ->
            if musicPath <> path then
                musicPath <- path
                music <- Some(SongPlayer(SongParser.loadMusicFile path, true, sampleRate)))

    member _.PlaySfx(name: string) =
        lock sync (fun () ->
            let song =
                match Cries.trySongForSfxName name with
                | Some cry -> cry
                | None -> SongParser.loadSfx name

            sfx.Add(SongPlayer(song, false, sampleRate)))

    /// Play a music file once (non-looping) layered over the current track, then
    /// let it retire itself — for fanfares like the heal jingle.
    member _.PlayJingle(path: string) =
        lock sync (fun () -> sfx.Add(SongPlayer(SongParser.loadMusicFile path, false, sampleRate)))

    member _.StopMusic() =
        lock sync (fun () ->
            music <- None
            musicPath <- "")

    /// Render `nFrames` interleaved stereo sample-frames into `buffer` (length ≥
    /// nFrames*2), replacing its contents. Finished SFX are retired.
    member _.Mix(buffer: float32[], nFrames: int) =
        lock sync (fun () ->
            Array.Clear(buffer, 0, nFrames * 2)
            music |> Option.iter (fun m -> m.Render(buffer, 0, nFrames, musicGain))
            for s in sfx do
                s.Render(buffer, 0, nFrames, sfxGain)
            sfx.RemoveAll(fun s -> s.Finished) |> ignore
            for i in 0 .. nFrames * 2 - 1 do
                buffer.[i] <- max -1.0f (min 1.0f buffer.[i]))

    interface ISoundBoard with
        member this.PlayMusic path = this.PlayMusic path
        member this.PlaySfx name = this.PlaySfx name
        member this.PlayJingle path = this.PlayJingle path
        member this.StopMusic() = this.StopMusic()
