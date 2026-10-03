using ImageUpscaller.Models;
using ImageUpscaller.Services;
using System.Windows.Media.Imaging;

namespace ImageUpscaller.Tests;

public sealed class ImagePipelineTests
{
    [Fact]
    public async Task PipelinesCoverPngWebpAvifAndBothScales()
    {
        using var directory = new TemporaryDirectory();
        string modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "realesrgan-x4plus.onnx");
        Assert.True(File.Exists(modelPath), $"Expected the test model at {modelPath}.");
        await using var pipeline = new ImagePipeline(new UpscalerService(modelPath, tryDirectMl: false));
        var encoder = new ImageEncoder();
        var source = TestImages.Create(16, 12);

        string pngInput = Path.Combine(directory.Path, "source.png");
        PngEncoder.Save(source, pngInput);
        var decodedPng = await pipeline.DecodeAsync(pngInput, CancellationToken.None);

        var pngResult = await pipeline.UpscaleAndSaveAsync(decodedPng.FullImage, pngInput,
            Path.Combine(directory.Path, "png-2x.png"), UpscaleScale.X2, OutputFormat.Png, null, CancellationToken.None);
        Assert.Equal((32, 24), (pngResult.Width, pngResult.Height));

        var webpResult = await pipeline.UpscaleAndSaveAsync(decodedPng.FullImage, pngInput,
            Path.Combine(directory.Path, "png-4x.webp"), UpscaleScale.X4, OutputFormat.Webp, null, CancellationToken.None);
        Assert.Equal((64, 48), (webpResult.Width, webpResult.Height));

        string webpInput = Path.Combine(directory.Path, "source.webp");
        await encoder.EncodeAsync(source, webpInput, OutputFormat.Webp, CancellationToken.None);
        var decodedWebp = await pipeline.DecodeAsync(webpInput, CancellationToken.None);
        var webpToPng = await pipeline.UpscaleAndSaveAsync(decodedWebp.FullImage, webpInput,
            Path.Combine(directory.Path, "webp-to-png.png"), UpscaleScale.X2, OutputFormat.Png, null, CancellationToken.None);
        Assert.Equal((32, 24), (webpToPng.Width, webpToPng.Height));

        string avifFromPng = Path.Combine(directory.Path, "source.avif");
        await encoder.EncodeAsync(source, avifFromPng, OutputFormat.AvifQuality, CancellationToken.None);
        var decodedAvif = await pipeline.DecodeAsync(avifFromPng, CancellationToken.None);
        var avifToWebp = await pipeline.UpscaleAndSaveAsync(decodedAvif.FullImage, avifFromPng,
            Path.Combine(directory.Path, "avif-to-webp.webp"), UpscaleScale.X2, OutputFormat.Webp, null, CancellationToken.None);
        Assert.Equal((32, 24), (avifToWebp.Width, avifToWebp.Height));

        var pngToAvif = await pipeline.UpscaleAndSaveAsync(decodedPng.FullImage, pngInput,
            Path.Combine(directory.Path, "png-to-avif.avif"), UpscaleScale.X2, OutputFormat.AvifQuality, null, CancellationToken.None);
        Assert.Equal((32, 24), (pngToAvif.Width, pngToAvif.Height));
        Assert.All(Directory.GetFiles(directory.Path), path => Assert.True(new FileInfo(path).Length > 0));
    }

    [Fact]
    public async Task UpscalerCancellationBeforeInferenceIsHonored()
    {
        string modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "realesrgan-x4plus.onnx");
        await using var upscaler = new UpscalerService(modelPath, tryDirectMl: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            upscaler.UpscaleAsync(TestImages.Create(8, 8), UpscaleScale.X2, null, cancellation.Token));
    }

    [Fact]
    public async Task InvalidModelFileIsReportedInsteadOfFallingBackSilently()
    {
        using var directory = new TemporaryDirectory();
        string modelPath = Path.Combine(directory.Path, "invalid.onnx");
        await File.WriteAllTextAsync(modelPath, "not an ONNX model");
        await using var upscaler = new UpscalerService(modelPath, tryDirectMl: true);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            upscaler.UpscaleAsync(TestImages.Create(8, 8), UpscaleScale.X2, null, CancellationToken.None));
    }

    [Fact]
    public async Task X2AndX4RetainExpectedDimensionsAcrossTileBoundaries()
    {
        string modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "realesrgan-x4plus.onnx");
        await using var upscaler = new UpscalerService(modelPath, tryDirectMl: false);
        foreach (var (width, height) in new[]
        {
            (128, 128), (129, 129), (256, 256), (257, 257),
            (400, 400), (401, 401), (513, 129)
        })
        {
            var source = TestImages.Create(width, height);
            var x2 = await upscaler.UpscaleAsync(source, UpscaleScale.X2, null, CancellationToken.None);
            Assert.Equal(width * 2, x2.PixelWidth);
            Assert.Equal(height * 2, x2.PixelHeight);

            var x4 = await upscaler.UpscaleAsync(source, UpscaleScale.X4, null, CancellationToken.None);
            Assert.Equal(width * 4, x4.PixelWidth);
            Assert.Equal(height * 4, x4.PixelHeight);
        }
    }
}
