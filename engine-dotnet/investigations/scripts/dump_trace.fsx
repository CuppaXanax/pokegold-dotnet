// Dump our sequencer's per-frame channel state for a song, as a CSV matching the
// PyBoy WRAM oracle (investigations/scripts/capture_regs2.py). Run from engine-dotnet:
//   dotnet fsi investigations/scripts/dump_trace.fsx <song.asm> <out.csv> <frames>
#r "../../src/PokeGold.Game/bin/Debug/net8.0/PokeGold.Game.dll"
open System.IO
open PokeGold.Game.Audio

let song = if fsi.CommandLineArgs.Length > 1 then fsi.CommandLineArgs.[1] else "audio/music/titlescreen.asm"
let outPath = if fsi.CommandLineArgs.Length > 2 then fsi.CommandLineArgs.[2] else "investigations/trace/title_ours.csv"
let frames = if fsi.CommandLineArgs.Length > 3 then int fsi.CommandLineArgs.[3] else 360

let parsed = SongParser.loadMusicFile song
let player = SongPlayer(parsed, true, 44100)

Directory.CreateDirectory(Path.GetDirectoryName outPath) |> ignore
let w = new StreamWriter(outPath)
let header =
    "frame," + String.concat "," [ for n in 1..4 -> sprintf "on%d,freq%d,duty%d,env%d,oct%d,dur%d" n n n n n n ]
w.WriteLine header

for f in 0 .. frames - 1 do
    let snap = player.DebugStepFrame()
    let cell (s: SeqSnapshot) =
        sprintf "%d,%d,%d,%d,%d,%d" (if s.On then 1 else 0) s.Period s.DutyByte s.EnvByte s.Octave s.FramesLeft
    // pad to 4 channels if the song has fewer
    let cells = [ for n in 0..3 -> if n < snap.Length then cell snap.[n] else "0,0,0,0,0,0" ]
    w.WriteLine(sprintf "%d,%s" f (String.concat "," cells))

printfn "wrote %s: %d frames, %d channels" outPath frames parsed.ChannelCount
w.Close()
