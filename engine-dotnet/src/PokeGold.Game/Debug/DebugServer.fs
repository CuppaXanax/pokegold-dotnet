namespace PokeGold.Game.Debug

open System
open System.IO
open System.IO.Pipes
open System.Threading

/// A named-pipe server that exposes a running game's [`DebugChannel`] to external
/// clients (a CLI tool, an agent, a future FSI front-end).
///
/// Protocol (newline-delimited text): the client writes one command per line and
/// reads the reply, which is one or more lines terminated by a lone `<<END>>`
/// sentinel line. A reply of just `<<END>>` means "no output". This keeps
/// multi-line inspection dumps readable while staying trivially parseable.
///
/// The server runs on a background thread and accepts one client at a time
/// (reconnecting in a loop). Commands are handed to the channel, which marshals
/// them onto the game-update thread, so the pipe never touches game state itself.
type DebugServer(channel: DebugChannel, ?pipeName: string) =
    [<Literal>]
    let EndSentinel = "<<END>>"

    let name = defaultArg pipeName "pokegold-debug"
    let mutable running = false
    let mutable thread: Thread = null

    /// The pipe name clients connect to (default `pokegold-debug`).
    member _.PipeName = name

    /// Start accepting connections on a background thread. Idempotent.
    member this.Start() =
        if not running then
            running <- true
            thread <- Thread(ThreadStart(fun () -> this.Loop()))
            thread.IsBackground <- true
            thread.Name <- "pokegold-debug-pipe"
            thread.Start()

    /// Stop accepting new connections. The current blocking accept is abandoned
    /// when the process exits (the thread is a background thread).
    member _.Stop() = running <- false

    member private _.Loop() =
        while running do
            try
                use server =
                    new NamedPipeServerStream(
                        name,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None
                    )

                server.WaitForConnection()

                use reader = new StreamReader(server)
                use writer = new StreamWriter(server)
                writer.AutoFlush <- true

                let mutable line = reader.ReadLine()

                while running && not (isNull line) do
                    let reply = channel.Submit line

                    if not (String.IsNullOrEmpty reply) then
                        writer.WriteLine reply

                    writer.WriteLine EndSentinel
                    line <- reader.ReadLine()
            with _ ->
                // A client dropped or the pipe faulted — loop and wait for the next
                // connection. Never let a debug-channel hiccup take down the game.
                ()
