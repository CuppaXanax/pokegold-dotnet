namespace PokeGold.Game.Debug

open System.Collections.Concurrent
open System.Threading.Tasks

/// One queued debug request: the raw command line and the promise the submitting
/// thread is blocked on until the game loop has produced a reply.
type private DebugRequest =
    { Command: string
      Reply: TaskCompletionSource<string> }

/// Thread-safe bridge between background debug clients (the named pipe, a future
/// FSI front-end) and the single-threaded MonoGame update loop.
///
/// Clients call [`Submit`] from any thread and block until the game loop has run
/// their command; the game loop calls [`Pump`] exactly once per frame to execute
/// every queued command **on its own thread**, so handlers see a consistent,
/// race-free view of mutable game state. The pipe thread only ever blocks its own
/// client — never the game.
type DebugChannel() =
    let queue = ConcurrentQueue<DebugRequest>()

    /// Submit a command from a background thread and block until the game loop has
    /// executed it the next time it pumps, returning the handler's textual result.
    member _.Submit(command: string) : string =
        let tcs = TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously)
        queue.Enqueue { Command = command; Reply = tcs }
        tcs.Task.GetAwaiter().GetResult()

    /// Execute every queued command with `handler` on the calling (game) thread,
    /// completing each waiting client. Handler exceptions are turned into an
    /// `error: …` reply rather than propagated, so one bad command can't crash the
    /// frame loop. Called once per `Game.Tick`.
    member _.Pump(handler: string -> string) =
        let mutable req = Unchecked.defaultof<DebugRequest>

        while queue.TryDequeue(&req) do
            let reply =
                try
                    handler req.Command
                with ex ->
                    "error: " + ex.Message

            req.Reply.TrySetResult reply |> ignore
