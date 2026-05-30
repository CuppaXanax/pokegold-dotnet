namespace PokeGold.Host

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open PokeGold.Game.Core

/// MonoGame DesktopGL shell. Owns the window, the fixed-step frame loop, and the
/// presentation of the game core's 160x144 framebuffer scaled to the window with
/// nearest-neighbor (integer scale, letterboxed). All MonoGame types live here.
type HostGame() as this =
    inherit Game()

    [<Literal>]
    let DefaultScale = 4

    let graphics = new GraphicsDeviceManager(this)
    // Fully qualified: the unqualified `Game` here is MonoGame's base class.
    let game = PokeGold.Game.Game()

    let mutable spriteBatch : SpriteBatch = null
    let mutable screen : Texture2D = null
    let mutable prevKb : KeyboardState = Unchecked.defaultof<KeyboardState>
    let mutable hostAudio : HostAudio = Unchecked.defaultof<HostAudio>

    do
        this.Content.RootDirectory <- "Content"
        this.IsMouseVisible <- true
        // D3: run logic at the Game Boy frame rate, one Tick per frame.
        this.IsFixedTimeStep <- true
        this.TargetElapsedTime <- TimeSpan.FromTicks(int64 (10_000_000.0 / Display.FrameRate))
        graphics.PreferredBackBufferWidth <- Display.Width * DefaultScale
        graphics.PreferredBackBufferHeight <- Display.Height * DefaultScale
        graphics.SynchronizeWithVerticalRetrace <- true

    override _.Initialize() =
        this.Window.Title <- "Pokémon Gold — F# engine"
        this.Window.AllowUserResizing <- true
        base.Initialize()

    override _.LoadContent() =
        spriteBatch <- new SpriteBatch(this.GraphicsDevice)
        screen <- new Texture2D(this.GraphicsDevice, Display.Width, Display.Height, false, SurfaceFormat.Color)
        hostAudio <- new HostAudio(game)
        hostAudio.Start()

    override _.Update(_gameTime: GameTime) =
        let kb = Keyboard.GetState()

        let held (keys: Keys list) = keys |> List.exists kb.IsKeyDown

        let buttons: Buttons =
            { Up = held [ Keys.Up; Keys.W ]
              Down = held [ Keys.Down; Keys.S ]
              Left = held [ Keys.Left; Keys.A ]
              Right = held [ Keys.Right; Keys.D ]
              A = held [ Keys.Z; Keys.J ]
              B = held [ Keys.X; Keys.K ]
              Start = held [ Keys.Enter ]
              Select = held [ Keys.RightShift; Keys.Back ] }

        game.Tick(buttons)

        // Debug save/load. F5/F9 aren't Game Boy buttons, so they bypass the
        // `Buttons` struct and call the game's host-facing save API directly, on
        // the key-down edge. A real Start-menu SAVE arrives with menus (M11).
        let pressed (k: Keys) = kb.IsKeyDown k && not (prevKb.IsKeyDown k)
        if pressed Keys.F5 then game.Save()
        if pressed Keys.F9 then game.Load()
        prevKb <- kb

        hostAudio.Update()

        base.Update(_gameTime)

    /// Compute the largest integer-scaled, centered destination rectangle that
    /// fits the current back buffer (letterboxing the remainder).
    member private _.DestRect() =
        let vw = this.GraphicsDevice.Viewport.Width
        let vh = this.GraphicsDevice.Viewport.Height
        let scale = max 1 (min (vw / Display.Width) (vh / Display.Height))
        let w = Display.Width * scale
        let h = Display.Height * scale
        Rectangle((vw - w) / 2, (vh - h) / 2, w, h)

    override _.Draw(_gameTime: GameTime) =
        screen.SetData(game.Framebuffer.Pixels)
        this.GraphicsDevice.Clear(Color.Black)
        spriteBatch.Begin(samplerState = SamplerState.PointClamp)
        spriteBatch.Draw(screen, this.DestRect(), Color.White)
        spriteBatch.End()
        base.Draw(_gameTime)
