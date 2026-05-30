#r "src/PokeGold.Game/bin/Debug/net8.0/PokeGold.Game.dll"
open PokeGold.Game.Audio
printfn "freqTable len=%d first8=%A" AudioData.frequencyTable.Length (AudioData.frequencyTable |> Array.truncate 9)
for oct in [2;3;4] do
  for (nm,p) in ["C",1;"E",5;"G",8] do
    let per = AudioData.notePeriod oct p
    let hz = AudioData.periodToHz per
    printfn "oct=%d %s pitch=%d period=%d pulseHz=%.1f waveHz=%.1f" oct nm p per hz (hz*0.5)
