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

    let private constDefRx = Regex(@"^\s*const_def(?:\s+(.+?))?\s*$")
    let private constNextRx = Regex(@"^\s*const_next\s+(.+?)\s*$")
    let private constSkipRx = Regex(@"^\s*const_skip(?:\s+(.+?))?\s*$")
    let private constRx = Regex(@"^\s*const\s+([A-Za-z_][A-Za-z0-9_]*)")
    let private defEquRx = Regex(@"^\s*DEF\s+([A-Za-z_][A-Za-z0-9_]*)\s+EQU\s+(.+?)\s*$")

    let private parseInt (s: string) : int =
        let t = s.Trim()

        if t.StartsWith "$" then System.Convert.ToInt32(t.Substring 1, 16)
        elif t.StartsWith "%" then System.Convert.ToInt32(t.Substring 1, 2)
        elif t.StartsWith "&" then System.Convert.ToInt32(t.Substring 1, 8)
        else int t

    let private tryParseInt (s: string) =
        try Some(parseInt s) with _ -> None

    let private evalExpr (symbols: Map<string, int>) (constValue: int) (expr: string) : int option =
        let rec evalSimple (s: string) =
            let tokens =
                s.Trim().Replace("+", " + ").Replace("-", " - ").Split([| ' '; '\t' |], System.StringSplitOptions.RemoveEmptyEntries)

            let mutable sign = 1
            let mutable value = 0
            let mutable sawTerm = false
            let mutable failed = false

            for token in tokens do
                match token with
                | "+" -> sign <- 1
                | "-" -> sign <- -1
                | "const_value" ->
                    value <- value + sign * constValue
                    sawTerm <- true
                    sign <- 1
                | _ ->
                    match tryParseInt token |> Option.orElseWith (fun () -> Map.tryFind token symbols) with
                    | Some term ->
                        value <- value + sign * term
                        sawTerm <- true
                        sign <- 1
                    | None -> failed <- true

            if failed || not sawTerm then None else Some value

        match expr.Split([| "<<" |], System.StringSplitOptions.None) with
        | [| left; right |] ->
            match evalSimple left, evalSimple right with
            | Some a, Some b -> Some(a <<< b)
            | _ -> None
        | _ -> evalSimple expr

    let private addFirst name value map =
        if Map.containsKey name map then map else Map.add name value map

    /// Parse a repo-relative `.asm` constants file into name -> value.
    let load (relative: string) : Map<string, int> =
        let mutable value = 0
        let mutable map = Map.empty

        for raw in Repo.readText(relative).Split('\n') do
            let line = (let i = raw.IndexOf(';') in if i >= 0 then raw.Substring(0, i) else raw).Trim()

            let md = constDefRx.Match line
            let mn = constNextRx.Match line
            let ms = constSkipRx.Match line
            let mc = constRx.Match line
            let me = defEquRx.Match line

            if md.Success then
                value <-
                    if md.Groups.[1].Success then
                        let firstArg = md.Groups.[1].Value.Split(',').[0]
                        evalExpr map value firstArg |> Option.defaultValue 0
                    else
                        0
            elif mn.Success then
                value <- evalExpr map value mn.Groups.[1].Value |> Option.defaultValue value
            elif ms.Success then
                let count =
                    if ms.Groups.[1].Success then
                        evalExpr map value ms.Groups.[1].Value |> Option.defaultValue 1
                    else
                        1

                value <- value + count
            elif mc.Success then
                // First definition wins: a later enum block that re-uses a name
                // (e.g. a second `const_def 1`) must not clobber the pokedex id.
                map <- addFirst mc.Groups.[1].Value value map

                value <- value + 1
            elif me.Success then
                match evalExpr map value me.Groups.[2].Value with
                | Some resolved -> map <- addFirst me.Groups.[1].Value resolved map
                | None -> ()

        map
