namespace PokeGold.Game.Overworld.Script

open PokeGold.Game.Core

/// Thin wrappers that feed a repo-relative `maps/<Name>.asm` through the pure
/// parsers in `PokeGold.MapData`. These are the last runtime `.asm` reads in the
/// overworld load path; M10.1 replaces them with lookups into the build-time
/// generated map tables. Kept here (not in the shared lib) because they touch
/// `Assets`, which is a Game concern.
module AsmLoad =

    /// Parse a map's script program from its `.asm`.
    let script (relativePath: string) : ScriptProgram =
        ScriptParser.parseText (Assets.readText relativePath)

    /// Parse a map's four event tables from its `.asm`.
    let events (relativePath: string) : MapEvents =
        MapEventParser.parseText (Assets.readText relativePath)

    /// Parse a map's resolved text labels from its `.asm`.
    let text (relativePath: string) : Map<string, string> =
        MapText.parseText (Assets.readText relativePath)
