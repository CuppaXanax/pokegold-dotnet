namespace PokeGold.Game.Audio

open PokeGold.Game.Data

/// Runtime access to songs and sound effects. The `.asm` parsing now happens at
/// BUILD time in `PokeGold.DataGen` (which bakes every track via the shared
/// `SongAsm` parser into `Data/Generated/Songs.Generated.fs`), so this module is a
/// thin lookup over the baked literals - the runtime never reads `audio/*.asm`.
/// The `Song`/`SoundCommand` types live in the shared `PokeGold.MapData` project.
module SongParser =

    /// Load a single-song music file by its repo-relative path
    /// (e.g. "audio/music/azaleatown.asm").
    let loadMusicFile (relativePath: string) : Song =
        match SongsData.byPath.TryGetValue relativePath with
        | true, song -> song
        | _ -> failwithf "No baked song found for %s" relativePath

    /// Load a named SFX (e.g. "Sfx_Menu"), baked from audio/sfx.asm.
    let loadSfx (name: string) : Song =
        match SongsData.byName.TryGetValue name with
        | true, song -> song
        | _ -> failwithf "SFX '%s' not found in baked audio/sfx.asm" name
