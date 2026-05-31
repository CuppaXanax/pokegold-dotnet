namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Player
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// The options menu layered over the start menu. Shows three editable rows:
/// TEXT SPEED, TEXT FRAME (box border), and SOUND. Up/Down moves the cursor;
/// Left/Right cycles the selected option's value; B (or Start) commits the
/// updated `GameOptions` into the player state via `onChange` and pops.
type OptionsScene(content: Content, initialPlayer: PlayerState, onChange: PlayerState -> unit) =

    let mutable opts = initialPlayer.Options
    let mutable menu = MenuList.create 3 3 false
    let input = EdgeDetector()
    let palette = TextRenderer.palette

    // Box geometry in 8-px tiles (screen is 20 × 18 tiles).
    // Height: 2 border + 3 option rows = 5.
    // Anchored to the left side, vertically centred-ish.
    [<Literal>]
    let Left = 0

    [<Literal>]
    let Top = 6

    [<Literal>]
    let Width = 20

    [<Literal>]
    let Height = 5

    // Column at which value text starts (label is max 10 chars at col Left+2).
    [<Literal>]
    let ValueCol = 13

    let speedLabel v =
        match v with
        | 3 -> "FAST"
        | 1 -> "SLOW"
        | _ -> "MID"

    let soundLabel v =
        match v with
        | 1 -> "STEREO"
        | _ -> "MONO"

    let rowLabel row =
        match row with
        | 0 -> "TEXT SPEED"
        | 1 -> "TEXT FRAME"
        | 2 -> "SOUND"
        | _ -> ""

    let rowValue row =
        match row with
        | 0 -> speedLabel opts.TextSpeed
        | 1 -> string (opts.BoxBorder + 1)   // stored 0-7, shown as 1-8
        | 2 -> soundLabel opts.Sound
        | _ -> ""

    let cycleLeft row =
        match row with
        | 0 -> opts <- { opts with TextSpeed = if opts.TextSpeed <= 1 then 3 else opts.TextSpeed - 1 }
        | 1 -> opts <- { opts with BoxBorder = if opts.BoxBorder <= 0 then 7 else opts.BoxBorder - 1 }
        | 2 -> opts <- { opts with Sound     = if opts.Sound     <= 0 then 1 else opts.Sound     - 1 }
        | _ -> ()

    let cycleRight row =
        match row with
        | 0 -> opts <- { opts with TextSpeed = if opts.TextSpeed >= 3 then 1 else opts.TextSpeed + 1 }
        | 1 -> opts <- { opts with BoxBorder = if opts.BoxBorder >= 7 then 0 else opts.BoxBorder + 1 }
        | 2 -> opts <- { opts with Sound     = if opts.Sound     >= 1 then 0 else opts.Sound     + 1 }
        | _ -> ()

    /// Current cursor row (0-based). Exposed for unit tests.
    member _.Cursor = menu.Cursor

    /// In-progress options state. Exposed for unit tests.
    member _.CurrentOptions = opts

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            let edges = input.Update(buttons)

            if edges.Up then
                menu <- MenuList.moveUp menu
                Stay
            elif edges.Down then
                menu <- MenuList.moveDown menu
                Stay
            elif edges.Left then
                cycleLeft menu.Cursor
                Stay
            elif edges.Right then
                cycleRight menu.Cursor
                Stay
            elif edges.B || edges.Start then
                onChange { initialPlayer with Options = opts }
                Pop
            else
                Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawBox fb content.Font palette Left Top Width Height

            for i in 0 .. 2 do
                let tileRow = Top + 1 + i

                if i = menu.Cursor then
                    WindowRenderer.drawCursor fb content.Font palette (Left + 1) tileRow

                WindowRenderer.drawString fb content.Font palette (Left + 2) tileRow (rowLabel i)
                WindowRenderer.drawString fb content.Font palette (Left + ValueCol) tileRow (rowValue i)
