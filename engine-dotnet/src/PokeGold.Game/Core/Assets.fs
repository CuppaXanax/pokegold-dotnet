namespace PokeGold.Game.Core

open System.IO
open System.Reflection

/// Locates the repository root so the engine can read the shared source assets
/// (`gfx/`, `maps/`, `data/`, …) in place, without copying them into the build.
module Assets =

    /// The assembly containing embedded game assets.
    let private asm =
        try
            let name = "PokeGold.Game"
            System.AppDomain.CurrentDomain.GetAssemblies()
            |> Array.tryFind (fun a -> a.GetName().Name = name)
            |> Option.defaultWith (fun () ->
                try Assembly.Load(name)
                with _ -> Assembly.GetExecutingAssembly())
        with ex ->
            System.Console.Error.WriteLine($"Assets: asm init failed: {ex}")
            reraise ()

    let private normalizeRelative (relative: string) : string =
        relative.Replace('\\', '/').TrimStart('/')

    /// Walk upward from a starting directory until a directory containing the
    /// given marker (default `roms.sha1`, which sits at the repo root) is found.
    let rec private findUp (marker: string) (dir: DirectoryInfo) : DirectoryInfo option =
        if dir = null then
            None
        elif File.Exists(Path.Combine(dir.FullName, marker)) then
            Some dir
        else
            findUp marker dir.Parent

    /// The repository root, discovered once from the running assembly's location.
    /// On platforms where the repo isn't present (e.g. Android APK), this will be
    /// None and all assets must come from embedded resources.
    let private repoRoot : string option =
        try
            let start = DirectoryInfo(System.AppContext.BaseDirectory)

            match findUp "roms.sha1" start with
            | Some d -> Some d.FullName
            | None ->
                match findUp "roms.sha1" (DirectoryInfo(Directory.GetCurrentDirectory())) with
                | Some d -> Some d.FullName
                | None -> None
        with ex ->
            System.Console.Error.WriteLine($"Assets: repoRoot init failed: {ex}")
            None

    /// The repository root. Empty on platforms without a repo (Android APK).
    let root : string =
        match repoRoot with
        | Some r -> r
        | None ->
            System.Console.Error.WriteLine("Assets: no repo root, relying on embedded resources only")
            ""

    let private findResource (relative: string) : string option =
        let assetPath = "assets/" + normalizeRelative relative
        let candidates =
            [ assetPath.Replace('/', '.')
              normalizeRelative (relative.Replace('/', '.')) ]

        asm.GetManifestResourceNames()
        |> Array.tryFind (fun name ->
            candidates
            |> List.exists (fun candidate -> name.EndsWith(candidate, System.StringComparison.OrdinalIgnoreCase)))

    let private tryRoot () : string option = repoRoot

    /// Resolve a repo-relative path (e.g. "gfx/tilesets/johto_modern.png") to an
    /// absolute path under the repository root.
    let path (relative: string) : string =
        match repoRoot with
        | Some r -> Path.Combine(r, relative.Replace('/', Path.DirectorySeparatorChar))
        | None -> failwith $"Cannot resolve path '{relative}': no repository root available (running from APK?)."

    /// Whether the asset exists either in the embedded resource set or on disk.
    let exists (relative: string) : bool =
        match findResource relative with
        | Some _ -> true
        | None ->
            match tryRoot () with
            | Some rootPath -> File.Exists(Path.Combine(rootPath, relative.Replace('/', Path.DirectorySeparatorChar)))
            | None -> false

    /// Read all bytes of a repo-relative file.
    let readBytes (relative: string) : byte[] =
        match findResource relative with
        | Some name ->
            use stream = asm.GetManifestResourceStream(name)
            if isNull stream then
                failwithf "Embedded resource '%s' could not be opened." name

            use ms = new MemoryStream()
            stream.CopyTo(ms)
            ms.ToArray()
        | None ->
            // Fall back to disk if repo root is available.
            match repoRoot with
            | Some r -> File.ReadAllBytes(Path.Combine(r, relative.Replace('/', Path.DirectorySeparatorChar)))
            | None -> failwithf "Asset '%s' not found as embedded resource and no repository root available." relative

    /// Read all text of a repo-relative file.
    let readText (relative: string) : string =
        System.Text.Encoding.UTF8.GetString(readBytes relative)
