module PokeGold.Tests.PngTests

open System.IO
open System.IO.Compression
open Xunit
open PokeGold.Game.Core

let private beUInt32 (bytes: byte[]) (offset: int) : uint32 =
    (uint32 bytes.[offset] <<< 24)
    ||| (uint32 bytes.[offset + 1] <<< 16)
    ||| (uint32 bytes.[offset + 2] <<< 8)
    ||| uint32 bytes.[offset + 3]

[<Fact>]
let ``encode emits a valid PNG signature`` () =
    let png = Png.encode 1 1 [| 0uy; 0uy; 0uy; 255uy |]
    let sig' = [| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |]
    Assert.Equal<byte[]>(sig', png.[0..7])

[<Fact>]
let ``IHDR records the image dimensions`` () =
    // IHDR data starts after signature(8) + length(4) + type(4) = offset 16.
    let png = Png.encode 160 144 (Array.zeroCreate (160 * 144 * 4))
    Assert.Equal(160u, beUInt32 png 16)
    Assert.Equal(144u, beUInt32 png 20)
    Assert.Equal(8uy, png.[24])  // bit depth
    Assert.Equal(6uy, png.[25])  // color type RGBA

[<Fact>]
let ``IDAT round-trips back to the filtered scanlines`` () =
    // 2x2 image with distinct pixels; verify the zlib IDAT inflates to
    // filter-byte-0-prefixed scanlines matching the input.
    let rgba =
        [| 10uy; 20uy; 30uy; 255uy;  40uy; 50uy; 60uy; 255uy
           70uy; 80uy; 90uy; 255uy; 100uy; 110uy; 120uy; 255uy |]
    let png = Png.encode 2 2 rgba

    // Walk chunks to find IDAT payload.
    let mutable i = 8
    let mutable idat = [||]
    while i < png.Length do
        let len = int (beUInt32 png i)
        let typ = System.Text.Encoding.ASCII.GetString(png, i + 4, 4)
        if typ = "IDAT" then idat <- png.[i + 8 .. i + 8 + len - 1]
        i <- i + 12 + len

    use input = new MemoryStream(idat)
    use z = new ZLibStream(input, CompressionMode.Decompress)
    use output = new MemoryStream()
    z.CopyTo output
    let raw = output.ToArray()

    let expected =
        [| 0uy; 10uy; 20uy; 30uy; 255uy; 40uy; 50uy; 60uy; 255uy
           0uy; 70uy; 80uy; 90uy; 255uy; 100uy; 110uy; 120uy; 255uy |]
    Assert.Equal<byte[]>(expected, raw)

[<Fact>]
let ``writeFile produces a readable PNG on disk`` () =
    let path = Path.Combine(Path.GetTempPath(), sprintf "pokegold-png-%d.png" (System.Guid.NewGuid().GetHashCode()))
    try
        Png.writeFile path 4 4 (Array.create (4 * 4 * 4) 200uy)
        Assert.True(File.Exists path)
        let bytes = File.ReadAllBytes path
        Assert.Equal(137uy, bytes.[0])
        Assert.Equal(4u, beUInt32 bytes 16)
    finally
        if File.Exists path then File.Delete path
