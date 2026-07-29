module PokeGold.Tests.CollisionTests

open Xunit
open PokeGold.Game.Data
open PokeGold.Game.Core

// Collision is parsed from the disassembly: collision_constants.asm (COLL ids and
// permission bases), collision_permissions.asm (id → permission), and the
// tileset's *_collision.asm (per-block quadrants). These tests pin known facts.

let private coll = Collision.loadNamed "johto_modern"

let private assertTile coll blockId qx qy collisionId permission walkable =
    Assert.Equal(collisionId, Collision.collisionIdAt coll blockId qx qy)
    Assert.Equal(permission, Collision.permissionAt coll blockId qx qy)
    Assert.Equal(walkable, Collision.isWalkable coll blockId qx qy)

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

[<Fact>]
let ``ice path ice block is walkable and recognized as ice`` () =
    let icePath = Collision.loadNamed "ice_path"

    // ice_path_collision.asm: block $0b TL is COLL_ICE.
    assertTile icePath 0x0b 0 0 0x23uy icePath.Land true
    Assert.True(Collision.isIceId(Collision.collisionIdAt icePath 0x0b 0 0))

[<Fact>]
let ``Johto and Kanto ledges resolve their permitted hop directions`` () =
    let johto = Collision.loadNamed "johto"
    let kanto = Collision.loadNamed "kanto"

    // johto_collision.asm: $4d TL, $4c TR, $4b TL, $51 TL, and $50 TR.
    Assert.Equal(Some [ Right ], Collision.tryLedge(Collision.collisionIdAt johto 0x4d 0 0))
    Assert.Equal(Some [ Left ], Collision.tryLedge(Collision.collisionIdAt johto 0x4c 1 0))
    Assert.Equal(Some [ Down ], Collision.tryLedge(Collision.collisionIdAt johto 0x4b 0 0))
    Assert.Equal(Some [ Right; Down ], Collision.tryLedge(Collision.collisionIdAt johto 0x51 0 0))
    Assert.Equal(Some [ Down; Left ], Collision.tryLedge(Collision.collisionIdAt johto 0x50 1 0))

    // kanto_collision.asm: block $4b TL is COLL_HOP_DOWN_RIGHT.
    Assert.Equal(Some [ Right; Down ], Collision.tryLedge(Collision.collisionIdAt kanto 0x4b 0 0))

[<Fact>]
let ``directional side walls retain their source land permission`` () =
    let cave = Collision.loadNamed "cave"
    let johto = Collision.loadNamed "johto"
    let kanto = Collision.loadNamed "kanto"

    // The collision permission table assigns COLL_UP_WALL ($b2) LAND_TILE.
    // cave_collision.asm $04 TR; johto_collision.asm $6a TR; kanto_collision.asm $3b TL.
    assertTile cave 0x04 1 0 0xb2uy cave.Land true
    assertTile johto 0x6a 1 0 0xb2uy johto.Land true
    assertTile kanto 0x3b 0 0 0xb2uy kanto.Land true

[<Fact>]
let ``indoor directional warp carpets are walkable`` () =
    let gate = Collision.loadNamed "gate"
    let underground = Collision.loadNamed "underground"

    // gate_collision.asm: $0a BL, $23 TL, and $24 TR.
    assertTile gate 0x0a 0 1 0x70uy gate.Land true
    assertTile gate 0x23 0 0 0x76uy gate.Land true
    assertTile gate 0x24 1 0 0x7euy gate.Land true
    // underground_collision.asm: block $25 TR is COLL_WARP_CARPET_UP.
    assertTile underground 0x25 1 0 0x78uy underground.Land true

[<Fact>]
let ``gate door tile is walkable`` () =
    let gate = Collision.loadNamed "gate"

    // gate_collision.asm: block $04 TL is COLL_DOOR.
    assertTile gate 0x04 0 0 0x71uy gate.Land true

[<Fact>]
let ``cave and indoor traversal tiles are walkable`` () =
    let cave = Collision.loadNamed "cave"
    let gate = Collision.loadNamed "gate"

    // gate_collision.asm $31 TR is COLL_LADDER and $11 TR is COLL_STAIRCASE.
    assertTile gate 0x31 1 0 0x72uy gate.Land true
    assertTile gate 0x11 1 0 0x7auy gate.Land true
    // cave_collision.asm $13 BR is COLL_CAVE.
    assertTile cave 0x13 1 1 0x7buy cave.Land true

[<Fact>]
let ``Pokecenter warp panel is walkable`` () =
    let pokecenter = Collision.loadNamed "pokecenter"

    // pokecenter_collision.asm: block $33 TR is COLL_WARP_PANEL.
    assertTile pokecenter 0x33 1 0 0x7cuy pokecenter.Land true

[<Fact>]
let ``port water and Johto whirlpool use water permission`` () =
    let port = Collision.loadNamed "port"
    let johto = Collision.loadNamed "johto"

    // port_collision.asm: block $02 TR is COLL_WATER.
    assertTile port 0x02 1 0 0x29uy port.Water false
    // johto_collision.asm: block $07 TL is COLL_WHIRLPOOL.
    assertTile johto 0x07 0 0 0x24uy johto.Water false

[<Fact>]
let ``Victory Road cave pit is land before its warp event`` () =
    let cave = Collision.loadNamed "cave"

    // VictoryRoad uses cave block $3f. Its BL quadrant is COLL_PIT, which the
    // permission table marks land; engine/overworld/tile_events.asm then warps it.
    assertTile cave 0x3f 0 1 0x60uy cave.Land true
