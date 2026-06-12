namespace PokeGold.Game.Battle

open System
open System.Text.RegularExpressions
open PokeGold.Game.Core
open PokeGold.Game.Data

type AnimEffect =
    | HitFlash | FireBurst | WaterSplash | ElectricZap | GrassLeaf
    | IceCrystal | PsychicWave | PoisonCloud | GroundShake
    | NormalHit | StatusEffect | NoAnim

type AnimCommand =
    | AnimWait of frames: int
    | AnimObj of objectId: string * x: int option * y: int option * param: string option
    | AnimGfx of gfxIds: string list
    | AnimSound of soundId: string
    | AnimBgEffect of effectId: string
    | AnimCall of label: string
    | AnimLoop of count: int * label: string
    | AnimRet
    | AnimOther of macro: string * args: string list

type AnimScript =
    { MoveName: string
      Label: string
      Commands: AnimCommand list }

module BattleAnim =
    let private cleanLine (line: string) =
        let idx = line.IndexOf(';')
        if idx >= 0 then line.Substring(0, idx) else line

    let private parseIntToken (token: string) =
        let token = token.Trim()
        if token.StartsWith("$", StringComparison.Ordinal) then
            Some(Convert.ToInt32(token.Substring(1), 16))
        else
            match Int32.TryParse token with
            | true, value -> Some value
            | _ -> None

    let private splitArgs (args: string) =
        args.Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun arg -> arg.Trim())
        |> Array.filter ((<>) "")
        |> Array.toList

    let private parseCommand (line: string) =
        let trimmed = cleanLine line |> fun s -> s.Trim()
        if not (trimmed.StartsWith("anim_", StringComparison.Ordinal)) then
            None
        else
            let firstSpace = trimmed.IndexOfAny([| ' '; '\t' |])
            let macro, args =
                if firstSpace < 0 then trimmed, []
                else trimmed.Substring(0, firstSpace), splitArgs (trimmed.Substring(firstSpace + 1))

            match macro, args with
            | "anim_wait", value :: _ ->
                parseIntToken value |> Option.map AnimWait
            | "anim_obj", objectId :: x :: y :: param :: _ ->
                Some(AnimObj(objectId, parseIntToken x, parseIntToken y, Some param))
            | "anim_1gfx", _
            | "anim_2gfx", _
            | "anim_3gfx", _
            | "anim_4gfx", _
            | "anim_5gfx", _ ->
                Some(AnimGfx args)
            | "anim_sound", _ :: _ :: soundId :: _ ->
                Some(AnimSound soundId)
            | "anim_bgeffect", effectId :: _ ->
                Some(AnimBgEffect effectId)
            | "anim_call", label :: _ ->
                Some(AnimCall label)
            | "anim_loop", count :: label :: _ ->
                Some(AnimLoop(parseIntToken count |> Option.defaultValue 0, label))
            | "anim_ret", _ ->
                Some AnimRet
            | _ ->
                Some(AnimOther(macro, args))

    let private animationPointers =
        lazy
            Assets.readText "data/moves/animations.asm"
            |> fun text -> text.Replace("\r", "").Split('\n')
            |> Seq.takeWhile (fun line -> not (line.Contains("assert_table_length NUM_ATTACKS + 1", StringComparison.Ordinal)))
            |> Seq.choose (fun raw ->
                let line = cleanLine raw |> fun s -> s.Trim()
                if line.StartsWith("dw BattleAnim_", StringComparison.Ordinal) then
                    Some(line.Substring("dw ".Length).Trim())
                else
                    None)
            |> Seq.toList

    let private scriptsByLabel =
        lazy
            let scripts = System.Collections.Generic.Dictionary<string, ResizeArray<AnimCommand>>()
            let mutable current: string option = None
            let labelRx = Regex(@"^(BattleAnim_[A-Za-z0-9_]+):")

            for raw in (Assets.readText "data/moves/animations.asm" |> fun text -> text.Replace("\r", "").Split('\n')) do
                let line = cleanLine raw |> fun s -> s.Trim()
                let labelMatch = labelRx.Match line
                if labelMatch.Success then
                    let label = labelMatch.Groups.[1].Value
                    current <- Some label
                    scripts.[label] <- ResizeArray()
                else
                    match current, parseCommand line with
                    | Some label, Some command -> scripts.[label].Add command
                    | _ -> ()

            scripts
            |> Seq.map (fun kvp -> kvp.Key, List.ofSeq kvp.Value)
            |> Map.ofSeq

    let scriptsByMove =
        lazy
            let moveNames =
                MovesData.byIndex
                |> Array.toList
                |> List.map (fun move -> move.Name)
            let count = min moveNames.Length animationPointers.Value.Length

            List.zip (moveNames |> List.take count) (animationPointers.Value |> List.take count)
            |> List.choose (fun (moveName, label) ->
                scriptsByLabel.Value
                |> Map.tryFind label
                |> Option.bind (fun commands ->
                    if String.IsNullOrWhiteSpace moveName then None
                    else Some(moveName, { MoveName = moveName; Label = label; Commands = commands })))
            |> Map.ofList

    let scriptForMove (move: MoveData) : AnimScript option =
        scriptsByMove.Value |> Map.tryFind move.Name

    let private scriptTokens (script: AnimScript) =
        script.Commands
        |> List.collect (function
            | AnimGfx gfx -> gfx
            | AnimObj(objectId, _, _, _) -> [ objectId ]
            | AnimBgEffect effectId -> [ effectId ]
            | AnimSound soundId -> [ soundId ]
            | AnimOther(macro, args) -> macro :: args
            | _ -> [])

    let private effectFromScript (move: MoveData) (script: AnimScript) =
        let tokens = scriptTokens script
        let has (fragment: string) =
            tokens |> List.exists (fun token -> token.Contains(fragment, StringComparison.OrdinalIgnoreCase))

        if has "FIRE" || has "EMBER" || has "FLAME" then FireBurst
        elif has "WATER" || has "BUBBLE" || has "WAVE" || has "SURF" then WaterSplash
        elif has "LIGHTNING" || has "THUNDER" || has "SPARK" then ElectricZap
        elif has "ICE" || has "BLIZZARD" then IceCrystal
        elif has "PLANT" || has "LEAF" || has "FLOWER" || has "PETAL" then GrassLeaf
        elif has "POISON" || has "SLUDGE" || has "ACID" then PoisonCloud
        elif has "ROCK" || has "GROUND" || has "SAND" || has "QUAKE" then GroundShake
        elif has "PSYCHIC" || has "BEAM" || has "GLOBE" then PsychicWave
        elif move.Power = 0 then StatusEffect
        elif has "HIT" || has "PUNCH" || has "KICK" || has "CUT" || has "HORN" then NormalHit
        else HitFlash

    let effectForMove (move: MoveData) : AnimEffect =
        match scriptForMove move with
        | Some script -> effectFromScript move script
        | None ->
            if move.Power = 0 then StatusEffect
            else
                match TypeChart.nameOfType move.Type with
                | "FIRE" -> FireBurst
                | "WATER" -> WaterSplash
                | "ELECTRIC" -> ElectricZap
                | "GRASS" | "BUG" -> GrassLeaf
                | "ICE" -> IceCrystal
                | "PSYCHIC_TYPE" -> PsychicWave
                | "POISON" -> PoisonCloud
                | "GROUND" | "ROCK" -> GroundShake
                | "NORMAL" -> NormalHit
                | _ -> HitFlash

    let duration (effect: AnimEffect) : int =
        match effect with
        | NoAnim -> 0 | StatusEffect -> 15 | HitFlash | NormalHit -> 10 | _ -> 20

    let durationForMove (move: MoveData) : int =
        match scriptForMove move with
        | Some script ->
            script.Commands
            |> List.sumBy (function AnimWait frames -> frames | _ -> 0)
            |> fun frames -> if frames <= 0 then duration (effectForMove move) else min 120 frames
        | None -> duration (effectForMove move)

    let tintColor (effect: AnimEffect) : byte * byte * byte * byte =
        match effect with
        | FireBurst -> (255uy, 100uy, 0uy, 128uy)
        | WaterSplash -> (0uy, 100uy, 255uy, 128uy)
        | ElectricZap -> (255uy, 255uy, 0uy, 160uy)
        | GrassLeaf -> (0uy, 200uy, 50uy, 128uy)
        | IceCrystal -> (150uy, 220uy, 255uy, 128uy)
        | PsychicWave -> (200uy, 50uy, 255uy, 128uy)
        | PoisonCloud -> (160uy, 0uy, 200uy, 128uy)
        | GroundShake -> (180uy, 140uy, 80uy, 100uy)
        | NormalHit | HitFlash -> (255uy, 255uy, 255uy, 160uy)
        | StatusEffect -> (255uy, 255uy, 200uy, 60uy)
        | NoAnim -> (0uy, 0uy, 0uy, 0uy)
