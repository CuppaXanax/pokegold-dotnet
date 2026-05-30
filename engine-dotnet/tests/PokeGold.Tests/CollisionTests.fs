module PokeGold.Tests.CollisionTests

open Xunit
open PokeGold.Game.Data

// Collision is parsed from the disassembly: collision_constants.asm (COLL ids and
// permission bases), collision_permissions.asm (id → permission), and the
// tileset's *_collision.asm (per-block quadrants). These tests pin known facts.

let private coll = Collision.loadNamed "johto_modern"

[<Fact>]
let ``permission table has 256 entries`` () =
    Assert.Equal(256, coll.Permissions.Length)

[<Fact>]
let ``base permission ids match the disassembly`` () =
    Assert.Equal(0x00uy, coll.Land)
    Assert.Equal(0x01uy, coll.Water)
    Assert.Equal(0x0Fuy, coll.Wall)

[<Fact>]
let ``COLL_FLOOR is land and COLL_WALL is wall`` () =
    // collision_permissions.asm: index $00 = LAND, $07 (COLL_WALL) = WALL.
    Assert.Equal(coll.Land, coll.Permissions.[0x00] &&& 0x0Fuy)
    Assert.Equal(coll.Wall, coll.Permissions.[0x07] &&& 0x0Fuy)

[<Fact>]
let ``floor block 0x00 is walkable, wall block 0x05 is not`` () =
    // johto_modern_collision.asm: block $00 = FLOOR×4, block $05 = WALL×4.
    for qx in 0..1 do
        for qy in 0..1 do
            Assert.True(Collision.isWalkable coll 0x00 qx qy)
            Assert.False(Collision.isWalkable coll 0x05 qx qy)

[<Fact>]
let ``tall grass block 0x03 is walkable`` () =
    // block $03 = TALL_GRASS×4, which is land (walkable).
    Assert.True(Collision.isWalkable coll 0x03 0 0)

[<Fact>]
let ``out-of-range block is treated as solid`` () =
    Assert.False(Collision.isWalkable coll 99999 0 0)
