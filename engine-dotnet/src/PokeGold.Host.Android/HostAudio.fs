namespace PokeGold.Host.Android

open Microsoft.Xna.Framework.Audio

/// Bridges the game's pure float PCM mix to MonoGame's audio device. Identical
/// to the desktop HostAudio — the MonoGame audio API is the same on Android.
type HostAudio(game: PokeGold.Game.Game) =
    let sampleRate = game.AudioSampleRate
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

    member _.Start() = instance.Play()

    member _.Update() =
        while instance.PendingBufferCount < targetQueued do
            submitChunk ()

    interface System.IDisposable with
        member _.Dispose() = instance.Dispose()
