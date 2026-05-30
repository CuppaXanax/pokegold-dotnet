// Render each channel of a song solo to <prefix>_chN.wav for spectral localization.
// usage: dotnet fsi render_solo.fsx <song.asm> <outprefix> <seconds>
#r "src/PokeGold.Game/bin/Debug/net8.0/PokeGold.Game.dll"
open System.IO
open PokeGold.Game.Audio

let songPath = fsi.CommandLineArgs.[1]
let prefix   = fsi.CommandLineArgs.[2]
let seconds  = int fsi.CommandLineArgs.[3]
let sr = 44100
let song = SongParser.loadMusicFile songPath

let writeWav (path: string) (buf: float32[]) (frames: int) =
    use fs = new FileStream(path, FileMode.Create)
    use bw = new BinaryWriter(fs)
    let n = frames * 2
    bw.Write(System.Text.Encoding.ASCII.GetBytes "RIFF")
    bw.Write(36 + n * 2)
    bw.Write(System.Text.Encoding.ASCII.GetBytes "WAVE")
    bw.Write(System.Text.Encoding.ASCII.GetBytes "fmt ")
    bw.Write(16); bw.Write(1s); bw.Write(2s); bw.Write(sr)
    bw.Write(sr * 2 * 2); bw.Write(4s); bw.Write(16s)
    bw.Write(System.Text.Encoding.ASCII.GetBytes "data")
    bw.Write(n * 2)
    for s in buf do bw.Write(int16 (int (max -1.0f (min 1.0f s) * 32767.0f)))

let frames = sr * seconds
for i in 0 .. song.ChannelCount - 1 do
    let solo = { song with ChannelCount = 1; Channels = [| song.Channels.[i] |] }
    let player = SongPlayer(solo, true, sr)
    let buf : float32[] = Array.zeroCreate (frames * 2)
    player.Render(buf, 0, frames, 1.0)
    let path = sprintf "%s_ch%d.wav" prefix (i + 1)
    writeWav path buf frames
    printfn "wrote %s (hwId=%d)" path (fst song.Channels.[i])
