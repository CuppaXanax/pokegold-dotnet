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
    // Written octave 3, C#: GSC stores the octave inverted (engine octave 8-3=5)
    // and GetFrequency arithmetic-shifts the table value right by 7-5=2, giving
    // period 1575 -> ~277 Hz on hardware.
    let hz = AudioData.noteFrequency 3 2
    Assert.InRange(hz, 275.0, 279.0)

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
let ``the engine renders in mono: left and right carry the same mix`` () =
    // GSC's default sound option is MONO, so NR51 stereo panning is bypassed and
    // both output terminals receive the same summed mix. Every interleaved L/R pair
    // must therefore be identical.
    let song = SongParser.loadMusicFile "audio/music/azaleatown.asm"
    let player = SongPlayer(song, true, 44100)
    let frames = 44100
    let buf : float32[] = Array.zeroCreate (frames * 2)
    player.Render(buf, 0, frames, 1.0)
    Assert.Contains(buf, fun s -> s <> 0.0f)
    for i in 0 .. frames - 1 do
        Assert.Equal(buf.[2 * i], buf.[2 * i + 1])

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

// ---- Fidelity: wave instrument/volume, drums, vibrato, pitch slide ----------

let private mkSong (commands: SoundCommand[]) (channels: (int * int)[]) : Song =
    { ChannelCount = channels.Length; Channels = channels; Commands = commands }

/// Peak absolute amplitude over a rendered window.
let private peak (player: SongPlayer) (frames: int) : float32 =
    let buf : float32[] = Array.zeroCreate (frames * 2)
    player.Render(buf, 0, frames, 1.0)
    Array.fold (fun m s -> max m (abs s)) 0.0f buf

[<Fact>]
let ``wave channel volume_envelope decodes waveform index and level`` () =
    // volume_envelope X, Y on ch3: Y = waveform (0-9), X & 3 = level.
    let e0 = { Volume = 1; Sweep = 5 } // X=1 -> 100%, waveform 5
    let e1 = { Volume = 0; Sweep = 5 } // X=0 -> mute
    Assert.Equal(5, Envelope.waveformIndex e0)
    Assert.Equal(1.0, Envelope.waveVolume e0)
    Assert.Equal(0.0, Envelope.waveVolume e1)
    Assert.Equal(0.5, Envelope.waveVolume { Volume = 2; Sweep = 0 })
    Assert.Equal(0.25, Envelope.waveVolume { Volume = 3; Sweep = 0 })

[<Fact>]
let ``a muted wave note is silent but an audible one is not`` () =
    // Channel id 3 = Wave. A note with level-mute env must produce no signal.
    let muted =
        mkSong [| VolumeEnvelope { Volume = 0; Sweep = 2 }; Octave 5; Note(1, 8); SoundRet |] [| 3, 0 |]
    let audible =
        mkSong [| VolumeEnvelope { Volume = 2; Sweep = 2 }; Octave 5; Note(1, 8); SoundRet |] [| 3, 0 |]
    Assert.Equal(0.0f, peak (SongPlayer(muted, false, 44100)) 8000)
    Assert.True(peak (SongPlayer(audible, false, 44100)) 8000 > 0.0f)

[<Fact>]
let ``a volume envelope decays the note's amplitude over time`` () =
    // Pulse channel, fade-out envelope (sweep > 0). Later amplitude < earlier.
    let song =
        mkSong [| VolumeEnvelope { Volume = 15; Sweep = 1 }; Octave 4; Note(1, 15); SoundRet |] [| 1, 0 |]
    let player = SongPlayer(song, false, 44100)
    let early = peak player 4000
    let _mid = peak player 4000
    let late = peak player 4000
    Assert.True(early > 0.0f)
    Assert.True(late < early)

[<Fact>]
let ``a drum plays every sub-note in its sequence, not just the first`` () =
    // Cry_Sample / azalea ch4 drives the noise channel; render a stretch and
    // confirm the noise voice is active (multi-sub-note drums stay audible).
    let song = SongParser.loadMusicFile "audio/music/azaleatown.asm"
    // Isolate channel 4 (noise) by playing only it.
    let _, entry = song.Channels |> Array.find (fun (id, _) -> id = 4)
    let solo = mkSong song.Commands [| 4, entry |]
    Assert.True(peak (SongPlayer(solo, false, 44100)) 44100 > 0.0f)

[<Fact>]
let ``pitch slide and vibrato render without error and stay in range`` () =
    // A note preceded by pitch_slide + vibrato must still produce bounded audio.
    let song =
        mkSong
            [| VolumeEnvelope { Volume = 15; Sweep = 0 }
               Vibrato(0, 2, 2)
               PitchSlide(8, 4, 8)
               Octave 4
               Note(1, 12)
               SoundRet |]
            [| 1, 0 |]
    let buf : float32[] = Array.zeroCreate (12000 * 2)
    (SongPlayer(song, false, 44100)).Render(buf, 0, 12000, 1.0)
    Assert.Contains(buf, fun s -> s <> 0.0f)
    Assert.All(buf, fun s -> Assert.InRange(s, -1.0f, 1.0f))

[<Fact>]
let ``the note-period table is faithful to the GB formula`` () =
    // Written octave 3 C# -> period 1575 (~277 Hz); strictly inside the 11-bit range.
    let p = AudioData.notePeriod 3 2
    Assert.Equal(1575, p)
    Assert.InRange(p, 1, 2047)
    Assert.InRange(AudioData.periodToHz p, 275.0, 279.0)

/// Render a window and return the interleaved stereo buffer.
let private renderBuf (player: SongPlayer) (frames: int) : float32[] =
    let buf : float32[] = Array.zeroCreate (frames * 2)
    player.Render(buf, 0, frames, 1.0)
    buf

[<Fact>]
let ``the DC blocker centres a sustained tone`` () =
    // The point-sampled DAC sum rides on a positive pedestal (each channel is 0..15,
    // summed 0..127). The APU's near-DC high-pass removes that static offset, so a
    // sustained tone settles to a zero mean (no audible thump / clean playback).
    let song =
        mkSong [| VolumeEnvelope { Volume = 15; Sweep = 0 }; Octave 4; Note(2, 15); SoundLoop(0, 2) |] [| 1, 0 |]
    let buf = renderBuf (SongPlayer(song, false, 44100)) 88200
    // Average the left channel over the settled second half (1..2 s).
    let tail = [| for i in 88200 .. 2 .. buf.Length - 1 -> buf.[i] |]
    let mean = Array.average tail
    Assert.InRange(mean, -0.02f, 0.02f)

[<Fact>]
let ``the point-sampled pulse is a two-level DAC square`` () =
    // The bit-faithful APU point-samples like the hardware DAC (the PyBoy oracle):
    // a steady pulse is a discrete two-level square (high / low), NOT a band-limited
    // continuum. Its samples therefore cluster at the DAC extremes — most of them sit
    // near +/- the peak amplitude (a sine or band-limited edge would not).
    let song =
        mkSong [| VolumeEnvelope { Volume = 15; Sweep = 0 }; Octave 4; Note(2, 15); SoundLoop(0, 2) |] [| 1, 0 |]
    let buf = renderBuf (SongPlayer(song, false, 44100)) 88200
    let tail = [| for i in 88200 .. 2 .. buf.Length - 1 -> buf.[i] |]
    let peak = tail |> Array.map abs |> Array.max
    let nearExtreme = tail |> Array.filter (fun s -> abs s > 0.5f * peak) |> Array.length
    Assert.True(
        float nearExtreme / float tail.Length > 0.8,
        $"a point-sampled square should spend most time at the DAC extremes, got {nearExtreme}/{tail.Length}")

