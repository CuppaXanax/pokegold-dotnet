namespace PokeGold.Game.Data

open System.Text.RegularExpressions
open PokeGold.Game.Core

/// A species' base stats, the 32-byte record from `data/pokemon/base_stats/`.
/// Only the fields the battle slice needs are decoded: the six base stats and
/// the (up to two) types. Stat order matches the disassembly's byte order
/// (HP, Attack, Defense, Speed, Sp.Atk, Sp.Def).
type BaseStats =
    { Dex: int
      Name: string
      Hp: int
      Attack: int
      Defense: int
      Speed: int
      SpAttack: int
      SpDefense: int
      Type1: int
      Type2: int }

module Species =

    let private statsRx =
        Regex(@"db\s+(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)")

    let private pairRx = Regex(@"db\s+([A-Za-z_]\w*),\s*([A-Za-z_]\w*)")

    let private dex = lazy (AsmConstants.load "constants/pokemon_constants.asm")

    /// Load a species' base stats by its base_stats file name (e.g. "cyndaquil").
    let load (name: string) : BaseStats =
        let text = Assets.readText $"data/pokemon/base_stats/{name}.asm"

        // Strip comments line-by-line so trailing annotations never interfere.
        let lines =
            [ for raw in text.Split('\n') ->
                  let i = raw.IndexOf(';')
                  if i >= 0 then raw.Substring(0, i) else raw ]

        let stats =
            lines
            |> List.tryPick (fun l ->
                let m = statsRx.Match l
                if m.Success then Some [ for g in 1..6 -> int m.Groups.[g].Value ] else None)
            |> Option.defaultWith (fun () -> failwithf "No base-stat line in %s" name)

        // The type line is the first `db IDENT, IDENT` whose idents are both types.
        let t1, t2 =
            lines
            |> List.tryPick (fun l ->
                let m = pairRx.Match l
                if m.Success then
                    let a = m.Groups.[1].Value
                    let b = m.Groups.[2].Value
                    match (try Some(TypeChart.value a) with _ -> None),
                          (try Some(TypeChart.value b) with _ -> None) with
                    | Some av, Some bv -> Some(av, bv)
                    | _ -> None
                else
                    None)
            |> Option.defaultWith (fun () -> failwithf "No type line in %s" name)

        { Dex = dex.Value.[name.ToUpperInvariant()]
          Name = name.ToUpperInvariant()
          Hp = stats.[0]
          Attack = stats.[1]
          Defense = stats.[2]
          Speed = stats.[3]
          SpAttack = stats.[4]
          SpDefense = stats.[5]
          Type1 = t1
          Type2 = t2 }
