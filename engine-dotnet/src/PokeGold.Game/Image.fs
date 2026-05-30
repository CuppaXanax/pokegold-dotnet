namespace PokeGold.Game

open SixLabors.ImageSharp
open SixLabors.ImageSharp.PixelFormats

/// Loads pret graphics PNGs and decodes them into the engine's tile model.
///
/// pret tileset/graphics PNGs use four gray levels (white→black). The Game Boy
/// 2bpp convention treats the lightest color as index 0 and the darkest as
/// index 3, so the mapping is `index = (255 - gray) / 85`. Tiles are laid out in
/// the PNG left-to-right, top-to-bottom in 8×8 cells, exactly as `rgbgfx` reads
/// them, so a tile's linear id is `ty * tilesWide + tx`.
module Image =

    /// Map an 8-bit gray level (one of 0/85/170/255) to a 2-bit tile index.
    let grayToIndex (gray: byte) : byte = byte ((255 - int gray) / 85)

    /// Decode a PNG (given as bytes) into a row-major array of 8×8 index tiles.
    /// Width and height must be multiples of 8.
    let decodeTiles (pngBytes: byte[]) : Tile[] =
        use img = Image.Load<L8>(pngBytes)
        let tilesWide = img.Width / 8
        let tilesHigh = img.Height / 8
        let count = tilesWide * tilesHigh

        Array.init count (fun id ->
            let tx = (id % tilesWide) * 8
            let ty = (id / tilesWide) * 8
            let px = Array.zeroCreate<byte> Tile.PixelCount

            for row in 0..7 do
                for col in 0..7 do
                    let p = img.[tx + col, ty + row]
                    px.[row * 8 + col] <- grayToIndex p.PackedValue

            { Pixels = px })

    /// Decode a repo-relative PNG path into index tiles.
    let loadTiles (relative: string) : Tile[] = decodeTiles (Assets.readBytes relative)
