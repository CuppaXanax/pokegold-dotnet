namespace PokeGold.Game.Overworld

open PokeGold.Game.Core
open PokeGold.Game.Data

/// The player-movement system: a pure function from (map, collision, input,
/// player) to the next player state. Collision is checked on the 16-px cell grid
/// and input is ignored mid-step (one grid step at a time), exactly matching the
/// GSC overworld feel.
module Movement =

    let private delta (dir: Direction) : int * int =
        match dir with
        | Down -> 0, 1
        | Up -> 0, -1
        | Left -> -1, 0
        | Right -> 1, 0

    /// Whether the 16-px cell (cx, cy) can be walked on foot.
    let cellWalkable (map: GameMap) (coll: Collision) (cx: int) (cy: int) : bool =
        let cellsW = map.Width * 2
        let cellsH = map.Height * 2

        if cx < 0 || cy < 0 || cx >= cellsW || cy >= cellsH then
            false
        else
            let blockId = int (Map.blockAt map (cx / 2) (cy / 2))
            Collision.isWalkable coll blockId (cx % 2) (cy % 2)

    /// The raw collision id of the 16-px cell (cx, cy); out-of-range reads as 0.
    let collisionIdAtCell (map: GameMap) (coll: Collision) (cx: int) (cy: int) : byte =
        let cellsW = map.Width * 2
        let cellsH = map.Height * 2

        if cx < 0 || cy < 0 || cx >= cellsW || cy >= cellsH then
            0uy
        else
            let blockId = int (Map.blockAt map (cx / 2) (cy / 2))
            Collision.collisionIdAt coll blockId (cx % 2) (cy % 2)

    /// The first walkable cell found spiralling out from the map center — used to
    /// place the player when no explicit spawn is given.
    let findStartCell (map: GameMap) (coll: Collision) : int * int =
        let cellsW = map.Width * 2
        let cellsH = map.Height * 2
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
        |> Seq.tryFind (fun (cx, cy) -> cellWalkable map coll cx cy)
        |> Option.defaultValue (cx0, cy0)

    /// Advance the player by one frame, consuming this frame's button state.
    let step (map: GameMap) (coll: Collision) (buttons: Buttons) (p: PlayerState) : PlayerState =
        match p.Motion with
        | Standing ->
            let dir =
                if buttons.Down then Some Down
                elif buttons.Up then Some Up
                elif buttons.Left then Some Left
                elif buttons.Right then Some Right
                else None

            match dir with
            | None -> { p with Bumped = false }
            | Some d when d <> p.Facing ->
                // Turn in place: face the new direction immediately but spend a
                // few frames pivoting before any step — a tap just turns, which
                // is what kills the old "glide-y" feel (GSC `.CheckTurning`).
                { p with
                    Facing = d
                    Motion = Turning
                    SrcX = p.CellX
                    SrcY = p.CellY
                    Progress = 0
                    Bumped = false }
            | Some d ->
                // Already facing the pressed direction: try to step.
                let dx, dy = delta d
                let tx, ty = p.CellX + dx, p.CellY + dy

                if cellWalkable map coll tx ty then
                    { p with
                        SrcX = p.CellX
                        SrcY = p.CellY
                        CellX = tx
                        CellY = ty
                        Motion = Walking
                        Progress = 0
                        Bumped = false }
                else
                    // Forward step is blocked: if the player stands on a ledge that
                    // permits a hop in this facing, vault two cells over it (the
                    // landing cell is not re-validated — faithful to GSC `.TryJump`).
                    let onLedge =
                        Collision.tryLedge (collisionIdAtCell map coll p.CellX p.CellY)
                        |> Option.map (List.contains d)
                        |> Option.defaultValue false

                    if onLedge then
                        { p with
                            SrcX = p.CellX
                            SrcY = p.CellY
                            CellX = p.CellX + 2 * dx
                            CellY = p.CellY + 2 * dy
                            Motion = Hopping
                            Progress = 0
                            Bumped = false }
                    else
                        // Bump: walk in place against the wall for a step cycle and
                        // pulse the SFX hook once (GSC `.bump` / OBJECT_ACTION_BUMP).
                        { p with
                            SrcX = p.CellX
                            SrcY = p.CellY
                            Motion = Bumping
                            Progress = 0
                            Bumped = true }
        | motion ->
            // A motion is in progress; input is locked until it finishes. Only a
            // walk or a hop translates the player and advances the walk cycle.
            let progress = p.Progress + 1

            if progress >= Player.stepDuration p then
                let translated =
                    match motion with
                    | Walking
                    | Hopping -> true
                    | _ -> false

                { p with
                    Motion = Standing
                    Progress = 0
                    StepCount = (if translated then p.StepCount + 1 else p.StepCount)
                    Bumped = false }
            else
                { p with
                    Progress = progress
                    Bumped = false }
