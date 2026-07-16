module PokeGold.Tests.CoverageSweepTests

open System.IO
open Xunit
open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Overworld.Script

// M10.9 — coverage sweep. With all map data baked at build time, the sweep is a
// query over `MapsData.all` rather than a runtime `.asm` parse. These gates pin the
// shape of the generated world (command totals, the set of still-unsupported
// movement macros, and overworld-sprite art gaps) so regressions in the generator
// or accidental opcode-coverage changes fail loudly.

[<Fact>]
let ``the baked map scripts hold a stable command total`` () =
    // Matches the M9.6 runtime sweep: drift here means generation went stale.
    // (Increased from 20612 when trainer macro expansion started preserving the
    // GSC post-battle after-script jump path; from 20932 when hiddenitem
    // expansion gained its event-flag gate — 87 hidden items × 4 commands; from
    // 21280 when Goldenrod underground door macro data stopped emitting script;
    // from 21265 when 38 source text `prompt`/`text_ram` directives stopped leaking
    // into script IR.)
    let total =
        MapsData.all |> Seq.sumBy (fun kv -> kv.Value.Script.Commands.Length)

    Assert.Equal(21227, total)

[<Fact>]
let ``generated script IR contains no generic Unsupported commands`` () =
    let unsupported =
        [ for kv in MapsData.all do
              for c in kv.Value.Script.Commands do
                  match c with
                  | Unsupported(name, _) -> yield kv.Key, name
                  | _ -> ()

          for c in StdScriptsData.program.Commands do
              match c with
              | Unsupported(name, _) -> yield "StdScripts", name
              | _ -> () ]

    Assert.Empty unsupported

[<Fact>]
let ``movement scripts are almost fully supported; only deferred macros remain`` () =
    let moveUnsup =
        [ for kv in MapsData.all do
              for m in kv.Value.Movements do
                  for c in m.Value do
                      match c with
                      | MoveUnsupported name -> yield name
                      | _ -> () ]

    let totalMoveCmds =
        MapsData.all
        |> Seq.sumBy (fun kv -> kv.Value.Movements |> Seq.sumBy (fun m -> m.Value.Length))

    // The only unsupported movement macros are the explicitly-deferred ones
    // (facing locks, sliding, teleport, tree shake — M11+ / field-move milestones).
    let distinct = moveUnsup |> List.distinct |> Set.ofList

    Assert.Equal<Set<string>>(
        Set.ofList [ "fix_facing"; "remove_fixed_facing"; "set_sliding"; "remove_sliding"; "teleport_from"; "tree_shake" ],
        distinct
    )

    // Over 96% of all baked movement commands are fully supported.
    let supported = totalMoveCmds - moveUnsup.Length
    Assert.True(
        float supported / float totalMoveCmds > 0.96,
        sprintf "movement coverage too low: %d/%d supported" supported totalMoveCmds
    )

[<Fact>]
let ``overworld sprite-art gaps are enumerated and bounded`` () =
    let sprites =
        [ for kv in MapsData.all do
              for o in kv.Value.Events.Objects -> o.Sprite ]
        |> List.distinct

    let hasPng (s: string) =
        let file = s.Replace("SPRITE_", "").ToLowerInvariant()
        File.Exists(Assets.path $"gfx/sprites/{file}.png")

    let missing = sprites |> List.filter (hasPng >> not)

    // 112 distinct overworld sprites are referenced across all maps; 33 have no
    // PNG yet (Pokémon-overworld + deferred field objects rendered as blanks). This
    // is the documented art-gap snapshot — a regression gate, not a failure of M10.
    Assert.Equal(112, sprites.Length)
    Assert.Equal(33, missing.Length)
