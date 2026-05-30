module PokeGold.Tests.AudioTests

open Xunit
open PokeGold.Game.Audio

// The audio subsystem is pure (no MonoGame): we can parse the GSC sound script
// straight from the disassembly and software-synth it to PCM here, with no device.

[<Fact>]
let ``Azalea Town parses into four hardware channels`` () =
    let song = SongParser.loadMusicFile "audio/music/azaleatown.asm"
    Assert.Equal(4, song.ChannelCount)
    Assert.Equal(4, song.Channels.Length)
    // Hardware channel ids 1..4 in order.
    Assert.Equal<int[]>([| 1; 2; 3; 4 |], song.Channels |> Array.map fst)

[<Fact>]
let ``Azalea Channel 1 opens with its tempo command`` () =
    let song = SongParser.loadMusicFile "audio/music/azaleatown.asm"
    let _, entry = song.Channels.[0]
    Assert.Equal(Tempo 160, song.Commands.[entry])

[<Fact>]
let ``Sfx_Menu parses as a single noise channel`` () =
    let song = SongParser.loadSfx "Sfx_Menu"
    Assert.Equal(1, song.ChannelCount)
    let id, _ = song.Channels.[0]
    Assert.Equal(8, id)
    Assert.Equal(Noise, VoiceKind.ofChannelId id)

[<Fact>]
let ``the note-frequency table matches the GB square formula`` () =
    // Octave 3, C# is ~1101 Hz on hardware (engine.asm GetFrequency).
    let hz = AudioData.noteFrequency 3 2
    Assert.InRange(hz, 1098.0, 1104.0)

[<Fact>]
let ``sequencing a song yields non-silent PCM`` () =
    let song = SongParser.loadMusicFile "audio/music/azaleatown.asm"
    let player = SongPlayer(song, true, 44100)
    let frames = 44100 // one second
    let buf : float32[] = Array.zeroCreate (frames * 2)
    player.Render(buf, 0, frames, 1.0)
    Assert.Contains(buf, fun s -> s <> 0.0f)

[<Fact>]
let ``a one-shot SFX runs to completion and ends`` () =
    let song = SongParser.loadSfx "Sfx_Menu"
    let player = SongPlayer(song, false, 44100)
    let frames = 44100
    let buf : float32[] = Array.zeroCreate (frames * 2)
    player.Render(buf, 0, frames, 1.0)
    // The blip is two noise notes then sound_ret; it must produce sound...
    Assert.Contains(buf, fun s -> s <> 0.0f)
    // ...and then stop (not loop, no infinite tail).
    Assert.True(player.Finished)

[<Fact>]
let ``the audio engine mixes a started track into its buffer`` () =
    let engine = AudioEngine(44100)
    engine.PlayMusic "audio/music/azaleatown.asm"
    let frames = 44100
    let buf : float32[] = Array.zeroCreate (frames * 2)
    engine.Mix(buf, frames)
    Assert.Contains(buf, fun s -> s <> 0.0f)
    // Soft-clamped to a sane range.
    Assert.All(buf, fun s -> Assert.InRange(s, -1.0f, 1.0f))
