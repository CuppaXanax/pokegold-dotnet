namespace PokeGold.Game.Core

open System.IO
open System.IO.Compression

/// A tiny, dependency-free PNG encoder for RGBA framebuffers. The game core is
/// platform-agnostic (no GraphicsDevice), so screenshots are produced straight
/// from the `Framebuffer`'s raw bytes — handy for the debug pipe and for an agent
/// inspecting the screen offline. Output is a standard 8-bit truecolor-with-alpha
/// PNG (color type 6), using .NET's built-in zlib for the IDAT stream.
module Png =

    let private crcTable =
        Array.init 256 (fun n ->
            let mutable c = uint32 n
            for _ in 0 .. 7 do
                c <- if c &&& 1u <> 0u then 0xEDB88320u ^^^ (c >>> 1) else c >>> 1
            c)

    let private crc32 (bytes: byte[]) : uint32 =
        let mutable c = 0xFFFFFFFFu
        for b in bytes do
            c <- crcTable.[int ((c ^^^ uint32 b) &&& 0xFFu)] ^^^ (c >>> 8)
        c ^^^ 0xFFFFFFFFu

    let private beBytes (v: uint32) : byte[] =
        [| byte (v >>> 24); byte (v >>> 16); byte (v >>> 8); byte v |]

    /// Append a PNG chunk (length, type, data, CRC over type+data) to `stream`.
    let private writeChunk (stream: Stream) (chunkType: string) (data: byte[]) =
        stream.Write(beBytes (uint32 data.Length), 0, 4)
        let typeBytes = System.Text.Encoding.ASCII.GetBytes chunkType
        stream.Write(typeBytes, 0, 4)
        stream.Write(data, 0, data.Length)
        let crc = crc32 (Array.append typeBytes data)
        stream.Write(beBytes crc, 0, 4)

    /// zlib-compress the raw, filtered scanline data for the IDAT chunk.
    let private deflate (raw: byte[]) : byte[] =
        use out = new MemoryStream()
        (use z = new ZLibStream(out, CompressionLevel.Optimal, true)
         z.Write(raw, 0, raw.Length))
        out.ToArray()

    /// Encode `rgba` (row-major, top-to-bottom, 4 bytes/pixel) as a PNG byte array.
    let encode (width: int) (height: int) (rgba: byte[]) : byte[] =
        // Filtered scanlines: each row is prefixed with filter-type 0 (None).
        let stride = width * 4
        let raw = Array.zeroCreate<byte> (height * (stride + 1))
        for y in 0 .. height - 1 do
            raw.[y * (stride + 1)] <- 0uy
            System.Array.Copy(rgba, y * stride, raw, y * (stride + 1) + 1, stride)

        let ihdr =
            Array.concat
                [ beBytes (uint32 width)
                  beBytes (uint32 height)
                  [| 8uy   // bit depth
                     6uy   // color type: RGBA
                     0uy   // compression: deflate
                     0uy   // filter: adaptive
                     0uy |] ] // interlace: none

        use stream = new MemoryStream()
        let signature = [| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |]
        stream.Write(signature, 0, signature.Length)
        writeChunk stream "IHDR" ihdr
        writeChunk stream "IDAT" (deflate raw)
        writeChunk stream "IEND" [||]
        stream.ToArray()

    /// Encode and write a PNG to `path` (creating the directory if needed).
    let writeFile (path: string) (width: int) (height: int) (rgba: byte[]) =
        let dir = Path.GetDirectoryName(path: string)
        if not (System.String.IsNullOrEmpty dir) then
            Directory.CreateDirectory dir |> ignore
        File.WriteAllBytes(path, encode width height rgba)
