namespace PokeGold.DataGen

open System.Text.RegularExpressions

/// Parses the disassembly's `const_def` / `const` / `const_next` enumerations
/// into a name -> numeric-id map. This is the same mechanism rgbds uses at
/// assembly time: a running counter that each `const` consumes and increments,
/// optionally reset by `const_def N` or repositioned by `const_next N`.
///
/// This is the build-time twin of the former runtime `AsmConstants`; the regex
/// now lives in tooling, exactly where flimsy text parsing belongs.
module AsmConstants =

    let private defRx = Regex(@"^\s*const_def(?:\s+(-?\d+))?")
    let private nextRx = Regex(@"^\s*const_next\s+(-?\d+)")
    let private constRx = Regex(@"^\s*const\s+([A-Za-z_][A-Za-z0-9_]*)")

    /// Parse a repo-relative `.asm` constants file into name -> value.
    let load (relative: string) : Map<string, int> =
        let mutable value = 0
        let mutable map = Map.empty

        for raw in Repo.readText(relative).Split('\n') do
            let line = let i = raw.IndexOf(';') in if i >= 0 then raw.Substring(0, i) else raw

            let md = defRx.Match line
            let mn = nextRx.Match line
            let mc = constRx.Match line

            if md.Success then
                value <- if md.Groups.[1].Success then int md.Groups.[1].Value else 0
            elif mn.Success then
                value <- int mn.Groups.[1].Value
            elif mc.Success then
                // First definition wins: a later enum block that re-uses a name
                // (e.g. a second `const_def 1`) must not clobber the pokedex id.
                if not (Map.containsKey mc.Groups.[1].Value map) then
                    map <- Map.add mc.Groups.[1].Value value map

                value <- value + 1

        map
