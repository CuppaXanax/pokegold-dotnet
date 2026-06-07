namespace PokeGold.Host.Android

open Android.App
open Android.Content.PM
open Android.OS
open Android.Views
open Microsoft.Xna.Framework

/// Android activity that hosts the MonoGame game loop. Locks to landscape,
/// hides the system bars for a full-screen experience, and keeps the screen on.
[<Activity(
    Label = "PokéGold",
    MainLauncher = true,
    Icon = "@android:drawable/sym_def_app_icon",
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = (ConfigChanges.Orientation
                            ||| ConfigChanges.Keyboard
                            ||| ConfigChanges.KeyboardHidden
                            ||| ConfigChanges.ScreenSize))>]
type Activity() =
    inherit AndroidGameActivity()

    override this.OnCreate(bundle: Bundle) =
        base.OnCreate(bundle)

        // Immersive sticky mode — hide nav + status bars, swipe to reveal.
#nowarn "44" // suppress Obsolete warning on SystemUiVisibility
        this.Window.DecorView.SystemUiVisibility <-
            StatusBarVisibility.Hidden
            |> int
            |> (|||) (int SystemUiFlags.HideNavigation)
            |> (|||) (int SystemUiFlags.ImmersiveSticky)
            |> enum<StatusBarVisibility>

        this.Window.AddFlags(WindowManagerFlags.KeepScreenOn)

        let game = new HostGame()
        let view = game.Services.GetService(typeof<View>) :?> View
        this.SetContentView(view)
        game.Run()
