namespace PokeGold.Game.Save

open System
open System.IO
open System.Text.Json

/// On-disk persistence for `SaveData`: JSON at a per-user path outside the repo.
/// JSON (over a hand-rolled binary format) keeps saves human-readable and easy to
/// migrate as the schema grows over the coming milestones (decision D6, format
/// pick). The single slot lives under the OS's local app-data so it survives
/// rebuilds and never clutters the source tree.
module SaveFile =

    let private options =
        JsonSerializerOptions(WriteIndented = true)

    /// Directory holding the save: `%LocalAppData%/PokeGold` (or the platform
    /// equivalent). Created on demand.
    let directory () : string =
        let root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        Path.Combine(root, "PokeGold")

    /// Full path to the single save slot.
    let path () : string = Path.Combine(directory (), "pokegold.sav")

    /// Serialize a save to a JSON string. Pure; useful for round-trip tests.
    let serialize (save: SaveData) : string =
        JsonSerializer.Serialize(save, options)

    /// Parse a save from JSON, accepting only versions this build understands.
    /// Unknown/zero versions return None — the seam where migration lands later.
    let deserialize (json: string) : SaveData option =
        try
            let save = JsonSerializer.Deserialize<SaveData>(json)
            if save.Version >= 1 && save.Version <= SaveData.CurrentVersion then Some save
            else None
        with :? JsonException ->
            None

    /// Write the save to disk, creating the directory if needed.
    let write (save: SaveData) : unit =
        Directory.CreateDirectory(directory ()) |> ignore
        File.WriteAllText(path (), serialize save)

    /// Read the save from disk, or None if it's missing or unreadable.
    let tryRead () : SaveData option =
        let p = path ()
        if File.Exists p then deserialize (File.ReadAllText p) else None
