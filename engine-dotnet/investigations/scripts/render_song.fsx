// Render one song (full mix) from our synth to a 16-bit stereo WAV.
// usage: dotnet fsi render_song.fsx <song.asm> <out.wav> <seconds>
#r "../../src/PokeGold.Game/bin/Debug/net8.0/PokeGold.Game.dll"
open System.IO
open PokeGold.Game.Audio

let songPath = fsi.CommandLineArgs.[1]
let outPath  = fsi.CommandLineArgs.[2]
let seconds  = int fsi.CommandLineArgs.[3]
let sr = 44100
let song = SongParser.loadMusicFile songPath

let player = SongPlayer(song, true, sr)
let frames = sr * seconds
let buf : float32[] = Array.zeroCreate (frames * 2)
player.Render(buf, 0, frames, 1.0)

let fs = new FileStream(outPath, FileMode.Create)
let bw = new BinaryWriter(fs)
let n = frames * 2
bw.Write(System.Text.Encoding.ASCII.GetBytes "RIFF")
bw.Write(36 + n * 2)
bw.Write(System.Text.Encoding.ASCII.GetBytes "WAVE")
bw.Write(System.Text.Encoding.ASCII.GetBytes "fmt ")
bw.Write(16)
bw.Write(1s)            // PCM
bw.Write(2s)            // stereo
bw.Write(sr)
bw.Write(sr * 2 * 2)
bw.Write(4s)
bw.Write(16s)
bw.Write(System.Text.Encoding.ASCII.GetBytes "data")
bw.Write(n * 2)
for s in buf do
    let v = int (max -1.0f (min 1.0f s) * 32767.0f)
    bw.Write(int16 v)
bw.Flush()
bw.Close()
fs.Close()
printfn "wrote %s (%ds, %d channels)" outPath seconds song.ChannelCount
