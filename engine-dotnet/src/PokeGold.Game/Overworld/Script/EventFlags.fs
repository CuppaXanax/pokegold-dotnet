namespace PokeGold.Game.Overworld.Script

open PokeGold.Game.Data

/// The persistent world state the overworld scripts read and mutate: the two flag
/// bitsets (`EVENT_*` story/progress events and `ENGINE_*` badges/options), the
/// script variables (`VAR_*`), and per-map "scene" ids (`setscene`/`setmapscene`).
///
/// In the disassembly these are packed WRAM bitsets addressed by numeric bit
/// index (`wEventFlags`, `wEngineFlags`); this is the high-level re-expression —
/// each flag is identified by its **constant name** (a `Set` of set names), which
/// is the same information without the bit-packing. Numeric save layout is M9.5's
/// problem; behaviour here is identical. All immutable — every mutator returns a
/// new `World`.
type World =
    { /// `EVENT_*` flags currently set (a story/progress bit being 1).
      Events: Set<string>
      /// `ENGINE_*` flags currently set (badges, Pokégear cards, options…).
      EngineFlags: Set<string>
      /// `VAR_*` game variables (`readvar`/`writevar`); absent ⇒ 0.
      Vars: Map<string, int>
      /// Per-map scene id (`setscene`/`setmapscene`); absent ⇒ 0. The empty key
      /// `""` is *this* map's scene, used by the map-less `checkscene`/`setscene`. */
      Scenes: Map<string, int>
      /// Named text buffers used by `gettrainername`, `getitemname`, `getmonname`,
      /// `getstring`, and `getnum`.
      StringBuffers: Map<string, string> }

module World =

    /// A fresh world: every flag clear, every var/scene 0.
    let empty: World =
        { Events = Set.empty
          EngineFlags = Set.empty
          Vars = Map.empty
          Scenes = Map.empty
          StringBuffers = Map.empty }

    // ---- Event flags (EVENT_*) ----------------------------------------------

    /// Is this `EVENT_*` flag set?
    let hasEvent (flag: string) (w: World) : bool = Set.contains flag w.Events

    /// Set an `EVENT_*` flag.
    let setEvent (flag: string) (w: World) : World =
        { w with Events = Set.add flag w.Events }

    /// Clear an `EVENT_*` flag.
    let clearEvent (flag: string) (w: World) : World =
        { w with Events = Set.remove flag w.Events }

    // ---- Engine flags (ENGINE_*) --------------------------------------------

    /// Is this `ENGINE_*` flag set?
    let hasFlag (flag: string) (w: World) : bool = Set.contains flag w.EngineFlags

    /// Set an `ENGINE_*` flag.
    let setFlag (flag: string) (w: World) : World =
        { w with EngineFlags = Set.add flag w.EngineFlags }

    /// Clear an `ENGINE_*` flag.
    let clearFlag (flag: string) (w: World) : World =
        { w with EngineFlags = Set.remove flag w.EngineFlags }

    // ---- Variables (VAR_*) --------------------------------------------------

    /// Read a `VAR_*` game variable (absent ⇒ 0).
    let getVar (var: string) (w: World) : int =
        Map.tryFind var w.Vars |> Option.defaultValue 0

    /// Write a `VAR_*` game variable.
    let setVar (var: string) (value: int) (w: World) : World =
        { w with Vars = Map.add var value w.Vars }

    // ---- Map scene ids ------------------------------------------------------

    let private sceneKey (map: string) : string =
        if map = "" then ""
        else Maps.canonicalConst map |> Option.defaultValue map

    let private sceneLookupKeys (map: string) : string list =
        let key = sceneKey map

        if key = "" then
            [ "" ]
        else
            match Maps.byName key with
            | Some data ->
                [ data.Meta.Const; data.Meta.Name; map ]
                |> List.distinct
            | None -> [ key; map ] |> List.distinct

    /// Read a map's scene id (absent ⇒ 0). Use `""` for the current map.
    let getScene (map: string) (w: World) : int =
        sceneLookupKeys map
        |> List.tryPick (fun key -> Map.tryFind key w.Scenes)
        |> Option.defaultValue 0

    /// Write a map's scene id. Use `""` for the current map.
    let setScene (map: string) (value: int) (w: World) : World =
        { w with Scenes = Map.add (sceneKey map) value w.Scenes }

    /// Read a text buffer (absent ⇒ "").
    let getBuffer (name: string) (w: World) : string =
        Map.tryFind name w.StringBuffers |> Option.defaultValue ""

    /// Write a text buffer.
    let setBuffer (name: string) (value: string) (w: World) : World =
        { w with StringBuffers = Map.add name value w.StringBuffers }
