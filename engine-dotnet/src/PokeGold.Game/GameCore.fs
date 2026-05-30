namespace PokeGold.Game

/// A cardinal facing/movement direction on the overworld grid.
type Direction =
    | Down
    | Up
    | Left
    | Right

/// The platform-agnostic game core: owns game state and renders into a
/// Framebuffer each tick, with no dependency on MonoGame or any host concern.
///
/// At M4 it loads a real map (Azalea Town), its tileset and collision, and the
/// player sprite, then lets the player walk the map on the 16-px collision grid
/// with smooth grid-stepped movement, correct facing/animation, solid-tile
/// collision, and a camera that follows the player within the map bounds.
type GameCore() =
    let framebuffer = Framebuffer()
    let mutable frame = 0UL

    // Real game data, loaded once from the shared repository assets.
    let tileset = Tileset.loadNamed "johto_modern"
    let map = Map.load 20 9 "maps/AzaleaTown.blk"
    let collision = Collision.loadNamed "johto_modern"
    let sprite = Sprite.loadNamed "chris"

    // DMG-style 4-shade green palette for the map's tile indices 0..3.
    let palette =
        Palette.ofColors
            [ Palette.rgb555 30 31 26
              Palette.rgb555 17 24 14
              Palette.rgb555 6 13 10
              Palette.rgb555 1 4 3 ]

    // Sprite palette: index 0 is transparent (skipped at draw time); 1..3 are a
    // light→dark grayscale so the player reads clearly against the green map.
    let spritePalette =
        Palette.ofColors
            [ Palette.rgb555 0 0 0
              Palette.rgb555 31 31 31
              Palette.rgb555 13 13 15
              Palette.rgb555 2 2 3 ]

    // The world is a 16-px cell grid; each 32-px block is a 2×2 cell quadrant.
    let cellsW = map.Width * 2
    let cellsH = map.Height * 2
    let mapPixelW = map.Width * MapRenderer.BlockPixels
    let mapPixelH = map.Height * MapRenderer.BlockPixels
    let maxCamX = max 0 (mapPixelW - Display.Width)
    let maxCamY = max 0 (mapPixelH - Display.Height)

    let stepFrames = 16

    let delta dir =
        match dir with
        | Down -> 0, 1
        | Up -> 0, -1
        | Left -> -1, 0
        | Right -> 1, 0

    let cellWalkable cx cy =
        if cx < 0 || cy < 0 || cx >= cellsW || cy >= cellsH then
            false
        else
            let blockId = int (Map.blockAt map (cx / 2) (cy / 2))
            Collision.isWalkable collision blockId (cx % 2) (cy % 2)

    // Start on the first walkable cell found spiralling out from map center.
    let startCell =
        let cx0, cy0 = cellsW / 2, cellsH / 2

        let candidates =
            seq {
                for r in 0 .. (max cellsW cellsH) do
                    for dy in -r .. r do
                        for dx in -r .. r do
                            if abs dx = r || abs dy = r then
                                yield cx0 + dx, cy0 + dy
            }

        candidates
        |> Seq.tryFind (fun (cx, cy) -> cellWalkable cx cy)
        |> Option.defaultValue (cx0, cy0)

    let mutable cellX = fst startCell
    let mutable cellY = snd startCell
    let mutable facing = Down
    let mutable moving = false
    let mutable srcX = cellX
    let mutable srcY = cellY
    let mutable progress = 0
    let mutable stepCount = 0

    let clamp lo hi v = max lo (min hi v)

    /// Sprite top-left in world pixels, interpolated during a step.
    let worldPixel () =
        if moving then
            let t = float progress / float stepFrames
            let px = float (srcX * 16) + float ((cellX - srcX) * 16) * t
            let py = float (srcY * 16) + float ((cellY - srcY) * 16) * t
            int (round px), int (round py)
        else
            cellX * 16, cellY * 16

    /// Choose the sprite frame and horizontal flip for the current facing/anim.
    let frameAndFlip () =
        let walking = moving
        let foot = walking && stepCount % 2 = 1

        match facing with
        | Down -> (if walking then 3 else 0), foot
        | Up -> (if walking then 4 else 1), foot
        | Left -> (if walking then 5 else 2), false
        | Right -> (if walking then 5 else 2), true

    /// The framebuffer the host should present after each Tick.
    member _.Framebuffer = framebuffer

    /// Total frames advanced so far.
    member _.Frame = frame

    /// Current player cell (16-px grid) — exposed for tests/debugging.
    member _.PlayerCellX = cellX

    member _.PlayerCellY = cellY

    member _.Facing = facing

    /// Advance the game by one frame, consuming this frame's button state.
    member _.Tick(buttons: Buttons) =
        frame <- frame + 1UL

        if moving then
            progress <- progress + 1

            if progress >= stepFrames then
                moving <- false
                progress <- 0
                stepCount <- stepCount + 1
        else
            let dir =
                if buttons.Down then Some Down
                elif buttons.Up then Some Up
                elif buttons.Left then Some Left
                elif buttons.Right then Some Right
                else None

            match dir with
            | Some d ->
                facing <- d
                let dx, dy = delta d
                let tx, ty = cellX + dx, cellY + dy

                if cellWalkable tx ty then
                    srcX <- cellX
                    srcY <- cellY
                    cellX <- tx
                    cellY <- ty
                    moving <- true
                    progress <- 0
            | None -> ()

        // Render: map first, then the player centered by the camera.
        let px, py = worldPixel ()
        let camX = clamp 0 maxCamX (px + 8 - Display.Width / 2)
        let camY = clamp 0 maxCamY (py + 8 - Display.Height / 2)

        framebuffer.Clear(0uy, 0uy, 0uy, 255uy)
        MapRenderer.draw framebuffer palette tileset map camX camY

        let frameIndex, hflip = frameAndFlip ()
        Sprite.draw framebuffer spritePalette sprite frameIndex (px - camX) (py - camY) hflip
