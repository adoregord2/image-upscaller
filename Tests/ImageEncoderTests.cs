using ImageUpscaller.Models;
using ImageUpscaller.Services;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageUpscaller.Tests;

public sealed class ImageEncoderTests
{
    [Fact]
    public async Task PngWebpAndAvifEncodersRoundTripAndCleanTemporaryFiles()
    {
        using var directory = new TemporaryDirectory();
        var source = TestImages.Create(24, 18);
        var encoder = new ImageEncoder();
        var decoder = new ImageDecoder();

        foreach (var (format, extension) in new[]
        {
            (OutputFormat.Png, ".png"),
            (OutputFormat.Webp, ".webp"),
            (OutputFormat.AvifQuality, ".avif"),
            (OutputFormat.AvifTargetSize, ".avif")
        })
        {
            var path = Path.Combine(directory.Path, $"roundtrip-{format}{extension}");
            long bytes = await encoder.EncodeAsync(source, path, format, CancellationToken.None);
            var decoded = decoder.Decode(path);

            Assert.True(bytes > 0);
            Assert.Equal(source.PixelWidth, decoded.PixelWidth);
            Assert.Equal(source.PixelHeight, decoded.PixelHeight);
            Assert.Single(Directory.GetFiles(directory.Path, $"*{format}{extension}"));
        }
    }

    [Fact]
    public void PreCancelledAvifEncodeDoesNotLeaveTemporaryFiles()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var path = Path.Combine(directory.Path, "cancelled.avif");

        Assert.Throws<OperationCanceledException>(() => AvifEncoder.Save(
            TestImages.Create(8, 8), path, OutputFormat.AvifQuality, cancellation.Token));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void DecoderReportsMissingAndInvalidFiles()
    {
        using var directory = new TemporaryDirectory();
        var decoder = new ImageDecoder();
        Assert.Throws<FileNotFoundException>(() => decoder.Decode(Path.Combine(directory.Path, "missing.png")));
        var invalid = Path.Combine(directory.Path, "invalid.png");
        File.WriteAllText(invalid, "not an image");
        Assert.ThrowsAny<Exception>(() => decoder.Decode(invalid));
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"image-upscaller-tests-{Guid.NewGuid():N}");

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

internal static class TestImages
{
    public static BitmapSource Create(int width, int height)
    {
        int stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = y * stride + x * 4;
            pixels[offset] = (byte)(x * 17 % 256);
            pixels[offset + 1] = (byte)(y * 23 % 256);
            pixels[offset + 2] = (byte)((x + y) * 11 % 256);
            pixels[offset + 3] = 255;
        }

        var image = BitmapSource.Create(width, height, 144, 144, PixelFormats.Bgra32, null, pixels, stride);
        image.Freeze();
        return image;
    }
}
