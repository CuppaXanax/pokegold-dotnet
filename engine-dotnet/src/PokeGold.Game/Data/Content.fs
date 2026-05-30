namespace PokeGold.Game.Data

open System.Collections.Generic

/// A lazy, memoizing cache for decoded game assets. Tilesets, collision models,
/// sprites and maps are loaded once on first request and reused thereafter,
/// keeping repeated scene loads (and the eventual 286 maps) cheap.
type Content() =
    let tilesets = Dictionary<string, Tileset>()
    let collisions = Dictionary<string, Collision>()
    let sprites = Dictionary<string, Sprite>()
    let maps = Dictionary<string, GameMap>()

    let getOrAdd (cache: Dictionary<string, 'a>) (key: string) (load: unit -> 'a) : 'a =
        match cache.TryGetValue key with
        | true, v -> v
        | _ ->
            let v = load ()
            cache.[key] <- v
            v

    /// The named tileset (gfx + metatiles), cached.
    member _.Tileset(name: string) : Tileset =
        getOrAdd tilesets name (fun () -> Tileset.loadNamed name)

    /// The named tileset's collision model, cached.
    member _.Collision(name: string) : Collision =
        getOrAdd collisions name (fun () -> Collision.loadNamed name)

    /// The named overworld sprite, cached.
    member _.Sprite(name: string) : Sprite =
        getOrAdd sprites name (fun () -> Sprite.loadNamed name)

    /// The map at a repo-relative `.blk` path (cached by path).
    member _.Map(width: int, height: int, relative: string) : GameMap =
        getOrAdd maps relative (fun () -> Map.load width height relative)
