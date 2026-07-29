module PokeGold.Tests.RouteCheckpointTests

open System
open System.IO
open System.Security.Cryptography
open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Save
open PokeGold.Tests.RouteCheckpoint

let private withCheckpointStore test =
    let root =
        Path.Combine(
            Path.GetTempPath(),
            $"pokegold-route-checkpoints-{Guid.NewGuid():N}")

    try
        test (CheckpointStore(root))
    finally
        if Directory.Exists root then
            Directory.Delete(root, true)

let private fileHash path =
    File.ReadAllBytes path
    |> SHA256.HashData
    |> Convert.ToHexString
    |> _.ToLowerInvariant()

[<Fact>]
let ``route checkpoint saves are ordinary saves captured from a live run`` () =
    withCheckpointStore (fun store ->
        use run = store.StartNewGame("A")
        let initial = run.Capture("new-game")

        run.Driver.Step Right
        let stepped = run.Capture("first-step")

        let initialSave =
            File.ReadAllText initial.SavePath
            |> SaveFile.deserialize
            |> Option.defaultWith (fun () -> failwith "initial checkpoint should be an ordinary readable save")

        let steppedSave =
            File.ReadAllText stepped.SavePath
            |> SaveFile.deserialize
            |> Option.defaultWith (fun () -> failwith "stepped checkpoint should be an ordinary readable save")

        Assert.Equal("PlayersHouse2F", initialSave.Overworld.MapId)
        Assert.Equal(initialSave.Overworld.CellX + 1, steppedSave.Overworld.CellX)
        Assert.Equal(initialSave.Overworld.CellY, steppedSave.Overworld.CellY)

        use resumed = store.Resume("first-step")
        let overworld = resumed.Driver.Snapshot.Overworld |> Option.get
        Assert.Equal("PlayersHouse2F", overworld.MapId)
        Assert.Equal(
            (steppedSave.Overworld.CellX, steppedSave.Overworld.CellY),
            (overworld.Player.CellX, overworld.Player.CellY)))

[<Fact>]
let ``route checkpoint chain verifies ancestry and hashes`` () =
    withCheckpointStore (fun store ->
        use run = store.StartNewGame("A")
        let root = run.Capture("new-game")
        run.Driver.Step Right
        let child = run.Capture("first-step")

        let chain = store.VerifyChain([ "new-game"; "first-step" ])

        Assert.Equal<string list>([ "new-game"; "first-step" ], chain |> List.map _.Name)
        Assert.Equal(None, root.Parent)
        Assert.Equal(
            Some
                { Name = root.Name
                  SaveHash = root.SaveHash },
            child.Parent)
        Assert.Equal(fileHash root.SavePath, root.SaveHash)
        Assert.Equal(fileHash child.SavePath, child.SaveHash))
