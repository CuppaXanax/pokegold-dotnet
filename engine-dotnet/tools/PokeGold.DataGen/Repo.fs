namespace PokeGold.DataGen

open System.IO

/// Locates the repository root so the generator can read the disassembly source
/// tables (`constants/`, `data/`) in place. Mirrors `PokeGold.Game.Core.Assets`
/// but lives in the build-time tool, where reading the repo is expected.
module Repo =

    let rec private findUp (marker: string) (dir: DirectoryInfo) : DirectoryInfo option =
        if dir = null then None
        elif File.Exists(Path.Combine(dir.FullName, marker)) then Some dir
        else findUp marker dir.Parent

    /// The repository root, discovered from the running tool's location (falling
    /// back to the current directory), keyed off the `roms.sha1` marker.
    let root : string =
        let start = DirectoryInfo(System.AppContext.BaseDirectory)

        match findUp "roms.sha1" start with
        | Some d -> d.FullName
        | None ->
            match findUp "roms.sha1" (DirectoryInfo(Directory.GetCurrentDirectory())) with
            | Some d -> d.FullName
            | None -> failwith "Could not locate repository root (no roms.sha1 found in any parent)."

    /// Resolve a repo-relative path to an absolute path under the repository root.
    let path (relative: string) : string =
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))

    /// Read all text of a repo-relative file.
    let readText (relative: string) : string = File.ReadAllText(path relative)
