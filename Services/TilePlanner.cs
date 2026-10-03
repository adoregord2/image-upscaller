using ImageUpscaller.Models;

namespace ImageUpscaller.Services;

/// <summary>Plans non-overlapping output cores with overlapping input regions.</summary>
public static class TilePlanner
{
    public const int DefaultTileSize = 128;
    public const int DefaultOverlap = 12;

    public static IReadOnlyList<ProcessingTile> Plan(
        int width,
        int height,
        int tileSize = DefaultTileSize,
        int overlap = DefaultOverlap)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image width and height must be positive.");
        if (tileSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileSize), "Tile size must be positive.");
        if (overlap < 0 || overlap >= tileSize)
            throw new ArgumentOutOfRangeException(nameof(overlap), "Overlap must be non-negative and smaller than the tile size.");

        var tiles = new List<ProcessingTile>();
        for (int coreY = 0; coreY < height; coreY += tileSize)
        {
            int coreHeight = Math.Min(tileSize, height - coreY);
            int sourceY = Math.Max(0, coreY - overlap);
            int sourceBottom = Math.Min(height, coreY + coreHeight + overlap);

            for (int coreX = 0; coreX < width; coreX += tileSize)
            {
                int coreWidth = Math.Min(tileSize, width - coreX);
                int sourceX = Math.Max(0, coreX - overlap);
                int sourceRight = Math.Min(width, coreX + coreWidth + overlap);
                tiles.Add(new ProcessingTile(
                    sourceX, sourceY, sourceRight - sourceX, sourceBottom - sourceY,
                    coreX, coreY, coreWidth, coreHeight));
            }
        }

        return tiles;
    }
}
