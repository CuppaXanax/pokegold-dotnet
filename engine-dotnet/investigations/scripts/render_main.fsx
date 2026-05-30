// Render a song aligned to its .mainloop (skipping the one-shot intro) so it can
// be compared 1:1 against a hardware capture taken mid-loop.
// usage: dotnet fsi render_main.fsx <song.asm> <outprefix> <skipFrames> <seconds>
#r "src/PokeGold.Game/bin/Debug/net8.0/PokeGold.Game.dll"
open System.IO
open PokeGold.Game.Audio

let songPath  = fsi.CommandLineArgs.[1]
let prefix    = fsi.CommandLineArgs.[2]
let skipFrames = int fsi.CommandLineArgs.[3]   // 60 Hz engine frames of intro to drop
let seconds   = int fsi.CommandLineArgs.[4]
let sr = 44100
let song = SongParser.loadMusicFile songPath
let framesPerSecond = 4194304.0 / 70224.0
let skipSamples = int (float skipFrames * float sr / framesPerSecond)

let writeWav (path: string) (buf: float32[]) (sampleFrames: int) =
    use fs = new FileStream(path, FileMode.Create)
    use bw = new BinaryWriter(fs)
    let n = sampleFrames * 2
    bw.Write(System.Text.Encoding.ASCII.GetBytes "RIFF")
    bw.Write(36 + n * 2)
    bw.Write(System.Text.Encoding.ASCII.GetBytes "WAVE")
    bw.Write(System.Text.Encoding.ASCII.GetBytes "fmt ")
    bw.Write(16); bw.Write(1s); bw.Write(2s); bw.Write(sr)
    bw.Write(sr * 2 * 2); bw.Write(4s); bw.Write(16s)
    bw.Write(System.Text.Encoding.ASCII.GetBytes "data")
    bw.Write(n * 2)
    for i in 0 .. n - 1 do
        let s = buf.[skipSamples * 2 + i]
        bw.Write(int16 (int (max -1.0f (min 1.0f s) * 32767.0f)))

let outFrames = seconds * sr
let total = skipSamples + outFrames

let renderInto (channels: (int * int)[]) : float32[] =
    let s : Song = { ChannelCount = channels.Length; Channels = channels; Commands = song.Commands }
    let player = SongPlayer(s, true, sr)
    let buf : float32[] = Array.zeroCreate (total * 2)
    player.Render(buf, 0, total, 1.0)
    buf

writeWav (sprintf "%s.wav" prefix) (renderInto song.Channels) outFrames
printfn "wrote %s.wav (mainloop-aligned, skip %d frames)" prefix skipFrames
for i in 0 .. song.ChannelCount - 1 do
    let buf = renderInto [| song.Channels.[i] |]
    let path = sprintf "%s_ch%d.wav" prefix (i + 1)
    writeWav path buf outFrames
    printfn "wrote %s (hwId=%d)" path (fst song.Channels.[i])
