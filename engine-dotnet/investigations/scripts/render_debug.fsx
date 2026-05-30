// Temporary diagnostic: render azaleatown (full mix + per-channel solo) to WAVs.
#r "src/PokeGold.Game/bin/Debug/net8.0/PokeGold.Game.dll"

open System.IO
open PokeGold.Game.Audio

let sr = 44100
let seconds = 8
let song = SongParser.loadMusicFile "audio/music/azaleatown.asm"

let writeWav (path: string) (mono: float32[]) =
    use fs = new FileStream(path, FileMode.Create)
    use bw = new BinaryWriter(fs)
    let n = mono.Length
    let byteRate = sr * 2
    bw.Write(System.Text.Encoding.ASCII.GetBytes "RIFF")
    bw.Write(36 + n * 2)
    bw.Write(System.Text.Encoding.ASCII.GetBytes "WAVE")
    bw.Write(System.Text.Encoding.ASCII.GetBytes "fmt ")
    bw.Write(16)
    bw.Write(1s)            // PCM
    bw.Write(1s)            // mono
    bw.Write(sr)
    bw.Write(byteRate)
    bw.Write(2s)
    bw.Write(16s)
    bw.Write(System.Text.Encoding.ASCII.GetBytes "data")
    bw.Write(n * 2)
    for s in mono do
        let v = int (max -1.0f (min 1.0f s) * 32767.0f)
        bw.Write(int16 v)

let render (channels: (int * int)[]) =
    let s : Song = { ChannelCount = channels.Length; Channels = channels; Commands = song.Commands }
    let player = SongPlayer(s, true, sr)
    let frames = sr * seconds
    let buf : float32[] = Array.zeroCreate (frames * 2)
    player.Render(buf, 0, frames, 1.0)
    // mono = (L+R) so panned channels are not dropped
    Array.init frames (fun i -> (buf.[i * 2] + buf.[i * 2 + 1]) * 0.5f)

let stats (name: string) (mono: float32[]) =
    let peak = mono |> Array.fold (fun m s -> max m (abs s)) 0.0f
    let clipped = mono |> Array.filter (fun s -> abs s > 1.0f) |> Array.length
    let rms = sqrt (mono |> Array.sumBy (fun s -> float s * float s) |> fun t -> t / float mono.Length)
    printfn "%-14s rawPeak=%.3f rms=%.4f clipped=%.2f%%" name peak rms (100.0 * float clipped / float mono.Length)

let writeWavNorm (path: string) (mono: float32[]) =
    let peak = mono |> Array.fold (fun m s -> max m (abs s)) 1e-6f
    let k = 0.8f / peak
    writeWav path (mono |> Array.map (fun s -> s * k))

stats "full" (render song.Channels)
for (id, entry) in song.Channels do
    stats (sprintf "ch%d" id) (render [| id, entry |])

writeWavNorm "listen_full.wav" (render song.Channels)
for (id, entry) in song.Channels do
    writeWavNorm (sprintf "listen_ch%d.wav" id) (render [| id, entry |])

printfn "channels: %A" (song.Channels |> Array.map fst)
printfn "done"
