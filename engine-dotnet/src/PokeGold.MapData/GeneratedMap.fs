namespace PokeGold.Game.Overworld.Script

/// One map's complete static data, baked at build time by `PokeGold.DataGen` and
/// looked up at runtime by id. Bundles the metadata, the four event tables, the
/// label-addressed script program, and the resolved text labels — everything the
/// overworld previously parsed from `maps/<Name>.asm` at load time, now a plain
/// in-binary value with no `.asm` I/O.
type GeneratedMap =
    { Meta: MapMeta
      Events: MapEvents
      Script: ScriptProgram
      Text: Map<string, string>
      /// The map's `applymovement` actor scripts, by label.
      Movements: Map<string, MovementCmd[]>
      /// The map's object-constant names in `object_event` order, so a script's
      /// symbolic actor operand resolves to an object index by position.
      ObjectConsts: string[] }
