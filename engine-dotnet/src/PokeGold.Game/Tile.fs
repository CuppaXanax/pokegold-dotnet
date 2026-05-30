namespace PokeGold.Game

/// An 8×8 tile of 2-bit color indices (0..3), stored row-major (64 entries).
/// The indices are looked up in a Palette at draw time.
type Tile =
    { Pixels: byte[] }

/// Game Boy 2bpp ("2 bits per pixel") tile decoding.
///
/// Each tile is 16 bytes: 8 rows × 2 bytes. For a row, the first byte is the
/// low bitplane and the second is the high bitplane. Within a byte, bit 7 is the
/// leftmost pixel. A pixel's index is (highBit << 1) | lowBit.
module Tile =

    [<Literal>]
    let Size = 8

    [<Literal>]
    let PixelCount = 64

    [<Literal>]
    let BytesPerTile = 16

    /// Decode one 8×8 tile from 2bpp `data` starting at `offset`.
    let decode (data: byte[]) (offset: int) : Tile =
        let px = Array.zeroCreate<byte> PixelCount

        for row in 0..7 do
            let lo = int data.[offset + row * 2]
            let hi = int data.[offset + row * 2 + 1]

            for col in 0..7 do
                let bit = 7 - col
                let lobit = (lo >>> bit) &&& 1
                let hibit = (hi >>> bit) &&& 1
                px.[row * 8 + col] <- byte ((hibit <<< 1) ||| lobit)

        { Pixels = px }

    /// Decode a contiguous sheet of tiles. `data.Length` must be a multiple of 16.
    let decodeSheet (data: byte[]) : Tile[] =
        let count = data.Length / BytesPerTile
        Array.init count (fun i -> decode data (i * BytesPerTile))
