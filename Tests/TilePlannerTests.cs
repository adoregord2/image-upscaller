using ImageUpscaller.Services;

namespace ImageUpscaller.Tests;

public sealed class TilePlannerTests
{
    [Theory]
    [InlineData(128, 128)]
    [InlineData(129, 129)]
    [InlineData(256, 256)]
    [InlineData(257, 257)]
    [InlineData(400, 400)]
    [InlineData(401, 401)]
    [InlineData(1601, 377)]
    public void TileCoresCoverEveryPixelExactlyOnceAndRemainInsideOverlappedSource(int width, int height)
    {
        var tiles = TilePlanner.Plan(width, height);
        var coverage = new byte[checked(width * height)];

        foreach (var tile in tiles)
        {
            Assert.InRange(tile.SourceX, 0, width - 1);
            Assert.InRange(tile.SourceY, 0, height - 1);
            Assert.True(tile.SourceX + tile.SourceWidth <= width);
            Assert.True(tile.SourceY + tile.SourceHeight <= height);
            Assert.True(tile.SourceX <= tile.CoreX && tile.SourceY <= tile.CoreY);
            Assert.True(tile.SourceX + tile.SourceWidth >= tile.CoreX + tile.CoreWidth);
            Assert.True(tile.SourceY + tile.SourceHeight >= tile.CoreY + tile.CoreHeight);

            for (int y = tile.CoreY; y < tile.CoreY + tile.CoreHeight; y++)
            for (int x = tile.CoreX; x < tile.CoreX + tile.CoreWidth; x++)
                coverage[y * width + x]++;
        }

        Assert.All(coverage, count => Assert.Equal(1, count));
    }

    [Fact]
    public void InvalidDimensionsAndOverlapAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TilePlanner.Plan(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => TilePlanner.Plan(10, 10, tileSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TilePlanner.Plan(10, 10, tileSize: 8, overlap: 8));
    }
}
