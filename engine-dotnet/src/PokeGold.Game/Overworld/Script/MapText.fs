namespace PokeGold.Game.Overworld.Script

open System.Text

/// Resolves a map's text **labels** to the M5 token strings the text box renders.
///
/// Scripts reference text by label (`writetext AzaleaTownGrampsTextBefore`); the
/// map `.asm` defines each label as a block of `text`/`line`/`cont`/`para`
/// directives ending in `done`/`prompt`. This re-expresses such a block as the
/// `<LINE>`/`<CONT>`/`<PARA>`/`<DONE>` token string [`TextBox.ofString`] consumes,
/// so the existing M5 typewriter renders real in-game dialogue unchanged.
///
/// Only the common overworld directives are handled; a block that uses something
/// else (e.g. `text_far`, `text_ram`) ends at that point. Labels with no text
/// block (script/data labels) produce no entry.
module MapText =

    /// The text directives and the token each contributes before its string.
    let private leading =
        [ "text", ""; "line", "<LINE>"; "next", "<LINE>"; "cont", "<CONT>"; "para", "<PARA>" ]
        |> Map.ofList

    /// The content of the first `"…"` on a line, or `""` if unquoted.
    let private quoted (s: string) : string =
        let i = s.IndexOf '"'
        let j = s.LastIndexOf '"'
        if i >= 0 && j > i then s.Substring(i + 1, j - i - 1) else ""

    /// Parse all text labels in a map `.asm` into a `label -> token string` map.
    let parseText (text: string) : Map<string, string> =
        let result = System.Collections.Generic.Dictionary<string, string>()
        // The label currently being accumulated, and its builder.
        let mutable current: string option = None
        let sb = StringBuilder()

        let flush (terminator: string) =
            match current with
            | Some label ->
                sb.Append terminator |> ignore
                result.[label] <- sb.ToString()
            | None -> ()

            current <- None
            sb.Clear() |> ignore

        for raw in text.Replace("\r\n", "\n").Split('\n') do
            let trimmed = raw.Trim()
            // Drop a trailing comment (outside quotes is fine for these lines).
            let body =
                let i = trimmed.IndexOf ';'
                (if i >= 0 then trimmed.Substring(0, i) else trimmed).Trim()

            if body <> "" then
                if body.EndsWith ":" && not (body.Contains " ") && not (body.Contains "\t") then
                    // A new label: abandon any half-accumulated block, start fresh.
                    current <- None
                    sb.Clear() |> ignore
                    current <- Some(body.Substring(0, body.Length - 1))
                else
                    let mnEnd = body.IndexOfAny([| ' '; '\t' |])
                    let mn = (if mnEnd < 0 then body else body.Substring(0, mnEnd)).Trim()

                    match mn with
                    | "done" -> flush "<DONE>"
                    | "prompt" -> flush "<PROMPT>"
                    | _ ->
                        match Map.tryFind mn leading with
                        | Some token when current.IsSome -> sb.Append(token).Append(quoted body) |> ignore
                        // Any non-text line inside a block (a script command, data)
                        // means this label wasn't a text label — abandon it.
                        | _ ->
                            current <- None
                            sb.Clear() |> ignore

        result |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

    /// Parse a map `.asm`'s text labels from a repo-relative path.
    let parseFile (relativePath: string) : Map<string, string> =
        parseText (PokeGold.Game.Core.Assets.readText relativePath)
