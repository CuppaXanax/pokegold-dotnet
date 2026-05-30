module PokeGold.Capture.Program

open System
open System.IO
open System.Diagnostics
open SixLabors.ImageSharp
open SixLabors.ImageSharp.PixelFormats
open SixLabors.ImageSharp.Processing
open SixLabors.ImageSharp.Formats.Gif
open PokeGold.Game

// ---------------------------------------------------------------------------
// Headless capture of the Azalea Town "pan" demo.
//
// PokeGold.Game renders into a plain RGBA framebuffer with no MonoGame
// dependency, so we can render every frame here, drive a clean looping camera
// path ourselves, and encode the result to a GIF (via ImageSharp) and an MP4
// (via ffmpeg) — no game window required.
// ---------------------------------------------------------------------------

let arg (argv: string[]) i fallback =
    if argv.Length > i then argv.[i] else fallback

[<EntryPoint>]
let main argv =
    let outDir = arg argv 0 "captures"
    let frames = int (arg argv 1 "600")
    let scale = int (arg argv 2 "4")

    Directory.CreateDirectory(outDir) |> ignore
    let frameDir = Path.Combine(outDir, "frames")
    if Directory.Exists frameDir then Directory.Delete(frameDir, true)
    Directory.CreateDirectory(frameDir) |> ignore

    // Load the same real assets the game renders.
    let tileset = Tileset.loadNamed "johto_modern"
    let map = Map.load 20 9 "maps/AzaleaTown.blk"

    let palette =
        Palette.ofColors
            [ Palette.rgb555 30 31 26
              Palette.rgb555 17 24 14
              Palette.rgb555 6 13 10
              Palette.rgb555 1 4 3 ]

    let mapPixelW = map.Width * MapRenderer.BlockPixels
    let mapPixelH = map.Height * MapRenderer.BlockPixels
    let maxX = max 0 (mapPixelW - Display.Width)
    let maxY = max 0 (mapPixelH - Display.Height)

    // MP4 is rendered crisp at `scale`; the GIF at a smaller scale to stay a
    // shareable file size (it can get large with this many frames).
    let gifScale = min scale (int (arg argv 3 "3"))
    let sw = Display.Width * scale
    let sh = Display.Height * scale
    let gw = Display.Width * gifScale
    let gh = Display.Height * gifScale

    // Play back at the Game Boy frame rate so the pan moves at exactly the speed
    // it does in the running game. GIF frame delay is in centiseconds, so it
    // rounds to the nearest hundredth of a second (~2 cs ≈ 50 fps).
    let gifDelay = max 1 (int (Math.Round(100.0 / Display.FrameRate)))

    let fb = Framebuffer()
    use gif = new Image<Rgba32>(gw, gh)

    printfn "Rendering %d frames (mp4 %dx%d, gif %dx%d)..." frames sw sh gw gh

    for i in 0 .. frames - 1 do
        // Seamless loop: cosine paths start and end at the same point with zero
        // velocity, so the clip loops without a visible seam. Spread over `frames`
        // (default ≈ the game's natural pan period) the per-frame camera motion
        // matches the in-game demo, instead of racing across the map.
        let t = float i / float frames
        let camX = int ((0.5 - 0.5 * cos (2.0 * Math.PI * t)) * float maxX)
        let camY = int ((0.5 - 0.5 * cos (2.0 * Math.PI * t)) * float maxY)

        fb.Clear(0uy, 0uy, 0uy, 255uy)
        MapRenderer.draw fb palette tileset map camX camY

        use src = Image.LoadPixelData<Rgba32>(ReadOnlySpan<byte>(fb.Pixels), Display.Width, Display.Height)

        // Crisp nearest-neighbor upscale for the MP4 frames.
        use mp4Img = src.Clone(fun ctx ->
            ctx.Resize(ResizeOptions(Size = Size(sw, sh), Sampler = KnownResamplers.NearestNeighbor))
            |> ignore)

        mp4Img.SaveAsPng(Path.Combine(frameDir, sprintf "frame_%04d.png" i))

        // Smaller upscale for the GIF frame.
        use gifImg = src.Clone(fun ctx ->
            ctx.Resize(ResizeOptions(Size = Size(gw, gh), Sampler = KnownResamplers.NearestNeighbor))
            |> ignore)

        let added = gif.Frames.AddFrame(gifImg.Frames.RootFrame)
        let meta = added.Metadata.GetGifMetadata()
        meta.FrameDelay <- gifDelay
        meta.DisposalMethod <- GifDisposalMethod.RestoreToBackground

    // Drop the blank frame the container was constructed with.
    gif.Frames.RemoveFrame(0)
    gif.Metadata.GetGifMetadata().RepeatCount <- 0us

    let gifPath = Path.Combine(outDir, "azalea-pan.gif")
    gif.SaveAsGif(gifPath)
    printfn "Wrote %s" (Path.GetFullPath gifPath)

    // Encode an MP4 from the PNG frames with ffmpeg (yuv420p for broad support).
    let mp4Path = Path.Combine(outDir, "azalea-pan.mp4")

    let psi =
        ProcessStartInfo(
            FileName = "ffmpeg",
            RedirectStandardError = false,
            RedirectStandardOutput = false,
            UseShellExecute = false
        )

    [ "-y"
      "-framerate"; string Display.FrameRate
      "-i"; Path.Combine(frameDir, "frame_%04d.png")
      "-c:v"; "libx264"
      "-pix_fmt"; "yuv420p"
      "-movflags"; "+faststart"
      mp4Path ]
    |> List.iter psi.ArgumentList.Add

    try
        use p = Process.Start(psi)
        p.WaitForExit()
        if p.ExitCode = 0 then printfn "Wrote %s" (Path.GetFullPath mp4Path)
        else printfn "ffmpeg exited %d (GIF still produced)." p.ExitCode
    with ex ->
        printfn "ffmpeg not run (%s). GIF still produced." ex.Message

    0
