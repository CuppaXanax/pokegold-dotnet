module PokeGold.Host.Program

[<EntryPoint>]
let main _argv =
    use game = new HostGame()
    game.Run()
    0
