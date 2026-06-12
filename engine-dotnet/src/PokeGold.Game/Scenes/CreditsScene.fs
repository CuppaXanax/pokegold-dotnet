namespace PokeGold.Game.Scenes

open System
open System.Text.RegularExpressions
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render

type CreditPage =
    { Scene: string option
      Lines: string list
      Duration: int
      TheEnd: bool }

module CreditsScript =
    let private sceneNames = [| "BELLOSSOM"; "TOGEPI"; "ELEKID"; "SENTRET" |]

    let private cleanLine (line: string) =
        let idx = line.IndexOf(';')
        if idx >= 0 then line.Substring(0, idx) else line

    let private dbTokens (line: string) =
        let line = cleanLine line
        let idx = line.IndexOf("db", StringComparison.Ordinal)
        if idx < 0 then
            []
        else
            line.Substring(idx + 2).Replace(",", " ").Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.toList

    let private creditsConstantNames =
        lazy
            Assets.readText "constants/credits_constants.asm"
            |> fun text -> text.Replace("\r", "").Split('\n')
            |> Seq.takeWhile (fun line -> not (line.TrimStart().StartsWith("DEF NUM_CREDITS_STRINGS", StringComparison.Ordinal)))
            |> Seq.choose (fun line ->
                let trimmed = line.Trim()
                if trimmed.StartsWith("const ", StringComparison.Ordinal) then
                    Some(trimmed.Substring("const ".Length).Trim())
                else
                    None)
            |> Seq.toList

    let private pointerLabels =
        lazy
            Assets.readText "data/credits_strings_pointers.asm"
            |> fun text -> text.Replace("\r", "").Split('\n')
            |> Seq.choose (fun line ->
                let trimmed = cleanLine line |> fun s -> s.Trim()
                if trimmed.StartsWith("dw Credits_", StringComparison.Ordinal) then
                    Some(trimmed.Substring("dw ".Length).Trim())
                else
                    None)
            |> Seq.toList

    let private quotedText (line: string) =
        let m = Regex.Match(line, @"\b(?:db|next)\s+""([^""]*)""")
        if m.Success then
            let text = m.Groups.[1].Value
            Some(text.Replace("@", ""))
        else
            None

    let private stringByLabel =
        lazy
            let labels = System.Collections.Generic.Dictionary<string, string list>()
            let mutable current: string option = None

            for raw in (Assets.readText "data/credits_strings.asm" |> fun text -> text.Replace("\r", "").Split('\n')) do
                let labelMatch = Regex.Match(raw, @"^\s*(Credits_[A-Za-z0-9_]+)::")
                if labelMatch.Success then
                    current <- Some labelMatch.Groups.[1].Value
                    labels.[labelMatch.Groups.[1].Value] <- []

                match current, quotedText raw with
                | Some label, Some text when text <> "" ->
                    labels.[label] <- labels.[label] @ [ text ]
                | _ -> ()

            labels.["Credits_Staff"] <- [ "      #MON"; "    GOLD VERSION"; "       STAFF" ]
            labels.["Credits_Copyright"] <-
                [ "(C) 1995-2000 NINTENDO"
                  "(C) 1995-2000 CREATURES INC."
                  "(C) 1995-2000 GAME FREAK INC." ]

            labels
            |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
            |> Map.ofSeq

    let private stringByConstant =
        lazy
            List.zip creditsConstantNames.Value pointerLabels.Value
            |> List.choose (fun (constantName, label) ->
                stringByLabel.Value
                |> Map.tryFind label
                |> Option.map (fun text -> constantName, text))
            |> Map.ofList

    let private addText lineNo textLines (slots: Map<int, string>) =
        textLines
        |> List.indexed
        |> List.fold (fun acc (offset, text) -> Map.add (lineNo + offset) text acc) slots

    let pages =
        lazy
            let tokens =
                Assets.readText "data/credits_script.asm"
                |> fun text -> text.Replace("\r", "").Split('\n')
                |> Array.toList
                |> List.collect dbTokens

            let rec loop index scene slots theEnd pages =
                if index >= tokens.Length then
                    List.rev pages
                else
                    match tokens.[index] with
                    | "CREDITS_END" -> List.rev pages
                    | "CREDITS_CLEAR" -> loop (index + 1) None Map.empty false pages
                    | "CREDITS_MUSIC" -> loop (index + 1) scene slots theEnd pages
                    | "CREDITS_SCENE" ->
                        let sceneIndex = Int32.Parse(tokens.[index + 1])
                        let sceneName =
                            if sceneIndex >= 0 && sceneIndex < sceneNames.Length then Some sceneNames.[sceneIndex]
                            else None
                        loop (index + 2) sceneName slots theEnd pages
                    | "CREDITS_THEEND" -> loop (index + 1) scene slots true pages
                    | "CREDITS_WAIT"
                    | "CREDITS_WAIT2" ->
                        let duration = max 1 (Int32.Parse(tokens.[index + 1]) * 8)
                        let lines = slots |> Map.toList |> List.sortBy fst |> List.map snd
                        let page = { Scene = scene; Lines = lines; Duration = duration; TheEnd = theEnd }
                        loop (index + 2) scene Map.empty false (page :: pages)
                    | token ->
                        let lineNo = Int32.Parse(tokens.[index + 1])
                        let textLines =
                            stringByConstant.Value
                            |> Map.tryFind token
                            |> Option.defaultValue [ token.Replace("_", " ") ]
                        loop (index + 2) scene (addText lineNo textLines slots) theEnd pages

            loop 0 None Map.empty false []

type CreditsScene(content: Content, allowSkip: bool) =
    let pages = CreditsScript.pages.Value
    let mutable pageIndex = 0
    let mutable timer = if pages.IsEmpty then 0 else pages.[0].Duration
    let mutable complete = pages.IsEmpty
    let mutable prevA = false
    let mutable prevB = false
    let palette = TextRenderer.palette

    let edge now was = now && not was

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let aPressed = edge buttons.A prevA
            let bPressed = edge buttons.B prevB
            prevA <- buttons.A
            prevB <- buttons.B

            if complete && aPressed then
                Pop
            else
                if allowSkip && bPressed && not complete then
                    timer <- min timer 1

                if not complete then
                    timer <- timer - 1
                    if timer <= 0 then
                        if pageIndex + 1 >= pages.Length then
                            complete <- true
                        else
                            pageIndex <- pageIndex + 1
                            timer <- pages.[pageIndex].Duration

                Stay

        member _.Render(fb: Framebuffer) =
            let bg = Palette.rgb555 0 0 0
            for y in 0 .. Display.Height - 1 do
                for x in 0 .. Display.Width - 1 do
                    fb.SetPixel(x, y, bg.R, bg.G, bg.B, bg.A)

            if not pages.IsEmpty then
                let page = pages.[min pageIndex (pages.Length - 1)]
                WindowRenderer.drawBox fb content.Font palette 0 0 20 18

                match page.Scene with
                | Some scene -> WindowRenderer.drawString fb content.Font palette 5 2 scene
                | None -> ()

                page.Lines
                |> List.truncate 8
                |> List.iteri (fun i line ->
                    WindowRenderer.drawString fb content.Font palette 0 (5 + i * 2) line)

                if page.TheEnd || complete then
                    WindowRenderer.drawString fb content.Font palette 7 14 "THE END"
