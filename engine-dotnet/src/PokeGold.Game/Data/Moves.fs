namespace PokeGold.Game.Data

open System.Text.RegularExpressions
open PokeGold.Game.Core

/// A move's 7-byte data record from `data/moves/moves.asm`. The effect is kept
/// as its constant name (e.g. "EFFECT_NORMAL_HIT") and mapped to an effect
/// command sequence by the battle layer; the type is resolved to its numeric id.
type MoveData =
    { Name: string
      Effect: string
      Power: int
      Type: int
      Accuracy: int
      Pp: int
      EffectChance: int }

module Moves =

    let private rx =
        Regex(@"^\s*move\s+([A-Za-z0-9_]+),\s*([A-Za-z0-9_]+),\s*(\d+),\s*([A-Za-z_]\w*),\s*(\d+),\s*(\d+),\s*(\d+)")

    let private all =
        lazy
            ([ for raw in Assets.readText("data/moves/moves.asm").Split('\n') do
                   let m = rx.Match raw
                   if m.Success then
                       let name = m.Groups.[1].Value
                       yield
                           name,
                           { Name = name
                             Effect = m.Groups.[2].Value
                             Power = int m.Groups.[3].Value
                             Type = TypeChart.value m.Groups.[4].Value
                             Accuracy = int m.Groups.[5].Value
                             Pp = int m.Groups.[6].Value
                             EffectChance = int m.Groups.[7].Value } ]
             |> Map.ofList)

    /// Look up a move's data by its constant name (e.g. "TACKLE").
    let byName (name: string) : MoveData = all.Value.[name]
