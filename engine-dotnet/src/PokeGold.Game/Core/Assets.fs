namespace PokeGold.Game.Core

open System.IO

/// Locates the repository root so the engine can read the shared source assets
/// (`gfx/`, `maps/`, `data/`, …) in place, without copying them into the build.
module Assets =

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
    let root : string =
        let start = DirectoryInfo(System.AppContext.BaseDirectory)

        match findUp "roms.sha1" start with
        | Some d -> d.FullName
        | None ->
            // Fall back to the current directory (useful for tests run from the repo).
            match findUp "roms.sha1" (DirectoryInfo(Directory.GetCurrentDirectory())) with
            | Some d -> d.FullName
            | None -> failwith "Could not locate repository root (no roms.sha1 found in any parent)."

    /// Resolve a repo-relative path (e.g. "gfx/tilesets/johto_modern.png") to an
    /// absolute path under the repository root.
    let path (relative: string) : string =
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))

    /// Read all bytes of a repo-relative file.
    let readBytes (relative: string) : byte[] = File.ReadAllBytes(path relative)

    /// Read all text of a repo-relative file.
    let readText (relative: string) : string = File.ReadAllText(path relative)
