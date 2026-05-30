namespace PokeGold.Host

open Microsoft.Xna.Framework.Audio

/// Bridges the game's pure float PCM mix to MonoGame's audio device. Each tick it
/// keeps a small queue of buffers submitted to a `DynamicSoundEffectInstance`,
/// pulling fresh samples from the game and converting them to signed 16-bit PCM.
/// This is the only place audio touches MonoGame.
type HostAudio(game: PokeGold.Game.Game) =
    let sampleRate = game.AudioSampleRate

    // ~33 ms of stereo audio per submitted buffer; keeping 2-3 queued hides the
    // host's frame jitter without adding much latency.
    let framesPerChunk = sampleRate / 30
    let targetQueued = 3

    let floatBuf : float32[] = Array.zeroCreate (framesPerChunk * 2)
    let pcm : byte[] = Array.zeroCreate (framesPerChunk * 2 * 2)

    let instance = new DynamicSoundEffectInstance(sampleRate, AudioChannels.Stereo)

    let submitChunk () =
        game.MixAudio(floatBuf, framesPerChunk)
        for i in 0 .. floatBuf.Length - 1 do
            let clamped = max -1.0f (min 1.0f floatBuf.[i])
            let s = int (clamped * 32767.0f)
            pcm.[i * 2] <- byte (s &&& 0xFF)
            pcm.[i * 2 + 1] <- byte ((s >>> 8) &&& 0xFF)
        instance.SubmitBuffer(pcm)

    /// Begin playback (call once after the audio device is available).
    member _.Start() =
        instance.Play()

    /// Top up the queued buffers; call once per frame.
    member _.Update() =
        while instance.PendingBufferCount < targetQueued do
            submitChunk ()

    interface System.IDisposable with
        member _.Dispose() = instance.Dispose()
