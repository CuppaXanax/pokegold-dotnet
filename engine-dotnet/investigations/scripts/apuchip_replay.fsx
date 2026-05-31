// APU port validation — STAGE 2 (replay).
// Reads the synthetic register-write script and renders it through our ApuChip
// (the faithful PyBoy port), dumping int16-interleaved raw samples to ours.bin for
// exact comparison against PyBoy's own sound.py (apuchip_cmp.py).
//
// usage: dotnet fsi apuchip_replay.fsx <writes.csv> <out.bin> <sampleRate> <numFrames>
#r "../../src/PokeGold.Game/bin/Debug/net8.0/PokeGold.Game.dll"
open System.IO
open PokeGold.Game.Audio

let csvPath = fsi.CommandLineArgs.[1]
let outPath = fsi.CommandLineArgs.[2]
let sr      = int fsi.CommandLineArgs.[3]
let nframes = int fsi.CommandLineArgs.[4]

// cgb = true (Pokémon Gold is a GBC title; matches the reference Sound(cgb=True)).
let samples = ApuReplay.renderLog csvPath sr nframes true

let fs = new FileStream(outPath, FileMode.Create)
let bw = new BinaryWriter(fs)
for s in samples do
    bw.Write(int16 (int s))   // raw 0..127 per side, like PyBoy's audiobuffer
bw.Flush()
bw.Close()
fs.Close()   // explicit: top-level `use` in fsx may not dispose before the process is measured
printfn "wrote %s (%d interleaved samples)" outPath samples.Length
