namespace PokeGold.Game.Data

/// Canonical map identity seam. ROM constants (`NEW_BARK_TOWN`) are the canonical
/// external identity; generated/runtime map names (`NewBarkTown`) remain the
/// load/display name for assets and tests.
module Maps =

    let all : Map<string, PokeGold.Game.Overworld.Script.GeneratedMap> = MapsData.all

    let private byAlias =
        lazy
            (MapsData.all
             |> Seq.collect (fun (KeyValue(runtimeName, map)) ->
                 [ runtimeName, map
                   map.Meta.Name, map
                   map.Meta.Const, map ])
             |> Map.ofSeq)

    let tryResolve (mapRef: string) =
        Map.tryFind mapRef byAlias.Value

    let canonicalConst (mapRef: string) =
        tryResolve mapRef |> Option.map (fun map -> map.Meta.Const)

    let runtimeName (mapRef: string) =
        tryResolve mapRef |> Option.map (fun map -> map.Meta.Name)

    let byName (mapRef: string) = tryResolve mapRef
