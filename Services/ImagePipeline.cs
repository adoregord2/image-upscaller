using ImageUpscaller.Models;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace ImageUpscaller.Services;

/// <summary>Coordinates decode, tiled inference, encoding, previews, cancellation, and operation diagnostics.</summary>
public sealed class ImagePipeline : IAsyncDisposable
{
    private readonly UpscalerService _upscaler;
    private readonly ImageDecoder _decoder;
    private readonly ImageEncoder _encoder;

    public ImagePipeline(UpscalerService? upscaler = null, ImageDecoder? decoder = null, ImageEncoder? encoder = null)
    {
        _upscaler = upscaler ?? new UpscalerService();
        _decoder = decoder ?? new ImageDecoder();
        _encoder = encoder ?? new ImageEncoder();
    }

    public string ModelPath => _upscaler.ModelPath;
    public string Engine => _upscaler.Engine;

    public async Task<DecodedImage> DecodeAsync(string path, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var image = await Task.Run(() => _decoder.Decode(path, cancellationToken), cancellationToken).ConfigureAwait(false);
        var preview = await Task.Run(() => PreviewFactory.Create(image), cancellationToken).ConfigureAwait(false);
        return new DecodedImage(image, preview, timer.Elapsed);
    }

    public async Task<ImageProcessResult> UpscaleAndSaveAsync(
        BitmapSource source,
        string inputPath,
        string outputPath,
        UpscaleScale scale,
        OutputFormat format,
        IProgress<UpscaleProgress>? progress,
        CancellationToken cancellationToken,
        Action<OutputFormat>? encodingStarted = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var totalTimer = Stopwatch.StartNew();
        long workingSetBefore = Environment.WorkingSet;

        var upscaleTimer = Stopwatch.StartNew();
        var fullResolutionResult = await _upscaler.UpscaleAsync(source, scale, progress, cancellationToken).ConfigureAwait(false);
        upscaleTimer.Stop();
        cancellationToken.ThrowIfCancellationRequested();

        encodingStarted?.Invoke(format);
        var encodeTimer = Stopwatch.StartNew();
        long outputBytes = await _encoder.EncodeAsync(fullResolutionResult, outputPath, format, cancellationToken).ConfigureAwait(false);
        encodeTimer.Stop();

        var preview = await Task.Run(() => PreviewFactory.Create(fullResolutionResult), CancellationToken.None).ConfigureAwait(false);
        totalTimer.Stop();
        long workingSetAfter = Environment.WorkingSet;
        long memoryDelta = workingSetAfter - workingSetBefore;

        AppLogger.Information("pipeline.completed",
            ("inputFilename", Path.GetFileName(inputPath)), ("inputWidth", source.PixelWidth), ("inputHeight", source.PixelHeight),
            ("scale", (int)scale), ("model", _upscaler.ModelPath), ("provider", _upscaler.Engine),
            ("tileSize", TilePlanner.DefaultTileSize),
            ("tileCount", ((source.PixelWidth + TilePlanner.DefaultTileSize - 1) / TilePlanner.DefaultTileSize) *
                          ((source.PixelHeight + TilePlanner.DefaultTileSize - 1) / TilePlanner.DefaultTileSize)),
            ("outputFormat", format), ("outputWidth", fullResolutionResult.PixelWidth), ("outputHeight", fullResolutionResult.PixelHeight),
            ("outputBytes", outputBytes), ("upscaleMs", upscaleTimer.ElapsedMilliseconds),
            ("encodeMs", encodeTimer.ElapsedMilliseconds), ("totalMs", totalTimer.ElapsedMilliseconds),
            ("workingSetDeltaBytes", memoryDelta));

        return new ImageProcessResult(preview, fullResolutionResult.PixelWidth, fullResolutionResult.PixelHeight,
            outputBytes, upscaleTimer.Elapsed, encodeTimer.Elapsed, totalTimer.Elapsed, memoryDelta);
    }

    public ValueTask DisposeAsync() => _upscaler.DisposeAsync();
}
