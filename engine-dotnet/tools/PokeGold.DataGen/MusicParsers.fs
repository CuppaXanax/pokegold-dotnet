namespace PokeGold.DataGen

open System.IO
open System.Text.RegularExpressions

/// Build-time binding of each `MUSIC_*` song id to its on-disk music `.asm` file.
/// The disassembly keeps two parallel tables: `constants/music_constants.asm`
/// (the ordered `MUSIC_*` ids) and `audio/music_pointers.asm` (the matching
/// `dba Music_<Label>` song-pointer list). Zipping them by index links each id to
/// its `Music_<Label>`, and the song lives in `audio/music/<label>.asm` (the label
/// suffix, lowercased). The naming differs from the constant for ~24 songs (e.g.
/// `MUSIC_TITLE` → `Music_TitleScreen` → `titlescreen.asm`), so the pointer table is
/// authoritative — a plain constant→filename convention would be wrong.
module MusicParsers =

    let private dbaRx = Regex(@"^\s*dba\s+Music_([A-Za-z0-9_]+)")

    /// The ordered `Music_<Label>` names from the song-pointer table.
    let private pointerLabels () : string list =
        [ for raw in Repo.readText("audio/music_pointers.asm").Split('\n') do
              let line = let i = raw.IndexOf ';' in if i >= 0 then raw.Substring(0, i) else raw
              let m = dbaRx.Match line
              if m.Success then yield m.Groups.[1].Value ]

    /// `MUSIC_*` id → repo-relative song file, for every song whose file exists.
    /// Returned sorted by id for deterministic generation.
    let bindings () : (string * string) list =
        let idOf = AsmConstants.load "constants/music_constants.asm"
        let labels = pointerLabels () |> List.toArray

        [ for KeyValue (name, idx) in idOf do
              if idx >= 0 && idx < labels.Length then
                  let label = labels.[idx]
                  let file = sprintf "audio/music/%s.asm" (label.ToLowerInvariant())
                  if File.Exists(Repo.path file) then yield name, file ]
        |> List.sortBy fst
