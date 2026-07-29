module PokeGold.Tests.RouteCheckpoint

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open PokeGold.Game
open PokeGold.Game.Debug
open PokeGold.Game.Save
open PokeGold.Tests.GameDriver

type CheckpointParent =
    { Name: string
      SaveHash: string }

type RouteCheckpoint =
    { Name: string
      SavePath: string
      SaveHash: string
      Parent: CheckpointParent option }

type CheckpointMetadata =
    { Name: string
      SaveHash: string
      Parent: CheckpointParent option }

let private jsonOptions = JsonSerializerOptions(WriteIndented = true)

let private hashFile path =
    File.ReadAllBytes path
    |> SHA256.HashData
    |> Convert.ToHexString
    |> _.ToLowerInvariant()

let private validateName name =
    if
        String.IsNullOrWhiteSpace name
        || name = "."
        || name = ".."
        || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
        || name.Contains('/')
        || name.Contains('\\')
    then
        invalidArg (nameof name) "checkpoint name must be one safe path segment"

type CheckpointStore(rootDirectory: string) =
    let root = Path.GetFullPath rootDirectory

    let checkpointDirectory name =
        validateName name
        Path.Combine(root, name)

    let checkpointPath name =
        Path.Combine(checkpointDirectory name, "pokegold.sav")

    let metadataPath name =
        Path.Combine(checkpointDirectory name, "checkpoint.json")

    let readCheckpoint name =
        validateName name

        let savePath = checkpointPath name
        let manifestPath = metadataPath name

        if not (File.Exists savePath && File.Exists manifestPath) then
            invalidOp $"Route checkpoint '{name}' is incomplete or missing."

        let metadata =
            try
                JsonSerializer.Deserialize<CheckpointMetadata>(
                    File.ReadAllText manifestPath,
                    jsonOptions)
            with :? JsonException as ex ->
                invalidOp $"Route checkpoint '{name}' has unreadable ancestry metadata: {ex.Message}"

        if isNull (box metadata) || metadata.Name <> name then
            invalidOp $"Route checkpoint '{name}' has mismatched ancestry metadata."

        let actualHash = hashFile savePath

        if actualHash <> metadata.SaveHash then
            invalidOp $"Route checkpoint '{name}' save hash does not match its ancestry metadata."

        { Name = metadata.Name
          SavePath = savePath
          SaveHash = metadata.SaveHash
          Parent = metadata.Parent }

    let verifyAncestry name =
        let rec loop visited currentName =
            if Set.contains currentName visited then
                invalidOp $"Route checkpoint ancestry contains a cycle at '{currentName}'."

            let current = readCheckpoint currentName

            match current.Parent with
            | None -> [ current ]
            | Some parent ->
                let ancestors = loop (Set.add currentName visited) parent.Name
                let actualParent = List.last ancestors

                if actualParent.SaveHash <> parent.SaveHash then
                    invalidOp $"Route checkpoint '{current.Name}' does not match parent '{parent.Name}'."

                ancestors @ [ current ]

        loop Set.empty name

    let createActiveDirectory () =
        Directory.CreateDirectory(root) |> ignore
        let path = Path.Combine(root, $".active-{Guid.NewGuid():N}")
        Directory.CreateDirectory(path) |> ignore
        path

    member internal _.Capture(
        activeDirectory: string,
        game: Game,
        name: string,
        parent: RouteCheckpoint option
    ) : RouteCheckpoint =
        let destinationDirectory = checkpointDirectory name

        if Directory.Exists destinationDirectory then
            invalidOp $"Route checkpoint '{name}' already exists."

        game.Save()
        let activeSave = SaveFile.pathIn activeDirectory

        if not (File.Exists activeSave) then
            invalidOp "The live route is not at a capturable save boundary."

        Directory.CreateDirectory(destinationDirectory) |> ignore
        let savePath = checkpointPath name
        File.Copy(activeSave, savePath)

        let saveHash = hashFile savePath

        let ancestry =
            parent
            |> Option.map (fun checkpoint ->
                { Name = checkpoint.Name
                  SaveHash = checkpoint.SaveHash })

        let metadata: CheckpointMetadata =
            { Name = name
              SaveHash = saveHash
              Parent = ancestry }

        File.WriteAllText(
            metadataPath name,
            JsonSerializer.Serialize(metadata, jsonOptions))

        { Name = name
          SavePath = savePath
          SaveHash = saveHash
          Parent = ancestry }

    member this.StartNewGame(playerName: string) =
        let activeDirectory = createActiveDirectory ()
        let driver = GameDriver(Game(saveDirectory = activeDirectory))
        driver.Apply(StartNewGame playerName)
        new RouteRun(this, activeDirectory, driver, None)

    member this.Resume(name: string) =
        let checkpoint = verifyAncestry name |> List.last
        let activeDirectory = createActiveDirectory ()
        File.Copy(checkpoint.SavePath, SaveFile.pathIn activeDirectory)
        let driver = GameDriver(Game(saveDirectory = activeDirectory))
        driver.Game.Load()
        new RouteRun(this, activeDirectory, driver, Some checkpoint)

    member _.VerifyChain(names: string list) =
        match names with
        | [] -> invalidArg (nameof names) "checkpoint chain must not be empty"
        | _ ->
            let actual = verifyAncestry (List.last names)
            let actualNames = actual |> List.map _.Name

            if actualNames <> names then
                let actualDescription = String.concat "; " actualNames
                let expectedDescription = String.concat "; " names
                invalidOp
                    $"Route checkpoint ancestry was [{actualDescription}], expected [{expectedDescription}]."

            actual

and RouteRun internal
    (
        store: CheckpointStore,
        activeDirectory: string,
        driver: GameDriver,
        initialParent: RouteCheckpoint option
    ) =
    let mutable parent = initialParent
    let mutable disposed = false

    member _.Driver = driver

    member _.Capture(name: string) =
        if disposed then
            raise (ObjectDisposedException(nameof RouteRun))

        let checkpoint = store.Capture(activeDirectory, driver.Game, name, parent)
        parent <- Some checkpoint
        checkpoint

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                if Directory.Exists activeDirectory then
                    Directory.Delete(activeDirectory, true)
