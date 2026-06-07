namespace PokeGold.Host.Android

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open Microsoft.Xna.Framework.Input
open PokeGold.Game.Core

/// MonoGame Android shell. Same tick/render loop as the desktop host but reads
/// gamepad input (d-pad + face buttons) instead of the keyboard, and skips the
/// named-pipe debug server (not available on Android). Designed for handhelds
/// like the Retroid Pocket 5 that expose physical controls as a standard
/// Android gamepad.
type HostGame() as this =
    inherit Game()

    let graphics = new GraphicsDeviceManager(this)
    let game = PokeGold.Game.Game()

    let mutable spriteBatch : SpriteBatch = null
    let mutable screen : Texture2D = null
    let mutable hostAudio : HostAudio = Unchecked.defaultof<HostAudio>

    do
        this.Content.RootDirectory <- "Content"
        this.IsFixedTimeStep <- true
        this.TargetElapsedTime <- TimeSpan.FromTicks(int64 (10_000_000.0 / Display.FrameRate))
        graphics.IsFullScreen <- true
        graphics.SupportedOrientations <-
            DisplayOrientation.LandscapeLeft ||| DisplayOrientation.LandscapeRight
        graphics.SynchronizeWithVerticalRetrace <- true

    override _.Initialize() =
        base.Initialize()

    override _.LoadContent() =
        spriteBatch <- new SpriteBatch(this.GraphicsDevice)
        screen <- new Texture2D(this.GraphicsDevice, Display.Width, Display.Height, false, SurfaceFormat.Color)
        hostAudio <- new HostAudio(game)
        hostAudio.Start()

    override _.Update(_gameTime: GameTime) =
        let gp = GamePad.GetState(PlayerIndex.One)
        let kb = Keyboard.GetState()

        // Read from both gamepad (RP5 physical controls) and keyboard (USB/BT).
        let held keys gpTest =
            gpTest gp || (keys |> List.exists kb.IsKeyDown)

        let buttons: PokeGold.Game.Core.Buttons =
            { Up    = held [ Keys.Up; Keys.W ]    (fun g -> g.DPad.Up    = ButtonState.Pressed || g.ThumbSticks.Left.Y > 0.5f)
              Down  = held [ Keys.Down; Keys.S ]  (fun g -> g.DPad.Down  = ButtonState.Pressed || g.ThumbSticks.Left.Y < -0.5f)
              Left  = held [ Keys.Left; Keys.A ]  (fun g -> g.DPad.Left  = ButtonState.Pressed || g.ThumbSticks.Left.X < -0.5f)
              Right = held [ Keys.Right; Keys.D ] (fun g -> g.DPad.Right = ButtonState.Pressed || g.ThumbSticks.Left.X > 0.5f)
              A      = held [ Keys.Z; Keys.J ]         (fun g -> g.Buttons.A     = ButtonState.Pressed)
              B      = held [ Keys.X; Keys.K ]         (fun g -> g.Buttons.B     = ButtonState.Pressed)
              Start  = held [ Keys.Enter ]             (fun g -> g.Buttons.Start = ButtonState.Pressed)
              Select = held [ Keys.RightShift; Keys.Back ] (fun g -> g.Buttons.Back  = ButtonState.Pressed) }

        game.Tick(buttons)
        hostAudio.Update()
        base.Update(_gameTime)

    /// Compute the largest integer-scaled, centered destination rectangle that
    /// fits the current display (letterboxing the remainder).
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
