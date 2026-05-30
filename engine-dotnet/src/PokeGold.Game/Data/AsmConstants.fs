namespace PokeGold.Game.Data

open System.Text.RegularExpressions
open PokeGold.Game.Core

/// Parses the disassembly's `const_def` / `const` / `const_next` enumerations
/// (e.g. `constants/move_constants.asm`) into a name → numeric-id map. This is
/// the same mechanism rgbds uses at assembly time: a running counter that each
/// `const` consumes and increments, optionally reset by `const_def N` or
/// repositioned by `const_next N`.
module AsmConstants =

    let private defRx = Regex(@"^\s*const_def(?:\s+(-?\d+))?")
    let private nextRx = Regex(@"^\s*const_next\s+(-?\d+)")
    let private constRx = Regex(@"^\s*const\s+([A-Za-z_][A-Za-z0-9_]*)")

    /// Parse a repo-relative `.asm` constants file into name → value.
    let load (relative: string) : Map<string, int> =
        let mutable value = 0
        let mutable map = Map.empty

        for raw in Assets.readText(relative).Split('\n') do
            // Drop comments so a trailing `; 01` never trips the matchers.
            let line = let i = raw.IndexOf(';') in if i >= 0 then raw.Substring(0, i) else raw

            let md = defRx.Match line
            let mn = nextRx.Match line
            let mc = constRx.Match line

            if md.Success then
                value <- if md.Groups.[1].Success then int md.Groups.[1].Value else 0
            elif mn.Success then
                value <- int mn.Groups.[1].Value
            elif mc.Success then
                map <- Map.add mc.Groups.[1].Value value map
                value <- value + 1

        map
