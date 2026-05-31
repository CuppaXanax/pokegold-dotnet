// Dump our driver's per-frame held APU register bytes (PyBoy pb.memory[0xFF10..] layout)
// so they can be diffed against a real PyBoy `apu_regs.csv` capture.
// usage: dotnet fsi dump_regs.fsx <song.asm> <out.csv> <nframes>
#r "../../src/PokeGold.Game/bin/Debug/net8.0/PokeGold.Game.dll"
open System.IO
open System.Text
open PokeGold.Game.Audio

let songPath = fsi.CommandLineArgs.[1]
let outPath  = fsi.CommandLineArgs.[2]
let nframes  = int fsi.CommandLineArgs.[3]
let sr = 44100
let song = SongParser.loadMusicFile songPath
let player = SongPlayer(song, true, sr)

// Column names match capture_apu.py: rFF10..rFF26 then wFF30..wFF3F.
let ctrl = [ for a in 0x10 .. 0x26 -> sprintf "rFF%02X" a ]
let wave = [ for a in 0x30 .. 0x3F -> sprintf "wFF%02X" a ]
let sb = StringBuilder()
sb.Append("frame,").Append(String.concat "," (ctrl @ wave)).Append('\n') |> ignore

for f in 0 .. nframes - 1 do
    let regs = player.DebugStepFrameRegs()        // index == offset == addr-0xFF10
    let ctrlVals = [ for o in 0 .. 22 -> string regs.[o] ]
    let waveVals = [ for o in 32 .. 47 -> string regs.[o] ]
    sb.Append(string f).Append(',')
      .Append(String.concat "," (ctrlVals @ waveVals)).Append('\n') |> ignore

File.WriteAllText(outPath, sb.ToString())
printfn "wrote %s (%d frames)" outPath nframes
