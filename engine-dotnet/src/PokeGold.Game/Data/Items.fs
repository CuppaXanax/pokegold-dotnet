namespace PokeGold.Game.Data

/// Runtime access to baked item metadata.
/// Types (`Pocket`, `ItemData`) are in `Data/Schema.fs`.
/// The table itself is in `Data/Generated/Items.Generated.fs` as `ItemsData`.
/// This module re-exports the two most-used accessors for discoverability.
module Items =
    let all : ItemData[] = ItemsData.all
    let byId : Map<string, ItemData> = ItemsData.byId
