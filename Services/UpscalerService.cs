using ImageUpscaller.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageUpscaller.Services;

/// <summary>Runs the dynamic-shape Real-ESRGAN x4 model with overlapped, non-overlapping output tiles.</summary>
public sealed class UpscalerService : IDisposable, IAsyncDisposable
{
    private const int ModelScale = 4;

    public static string DefaultModelPath => Path.Combine(AppContext.BaseDirectory, "Models", "realesrgan-x4plus.onnx");

    private readonly string _modelPath;
    private readonly bool _tryDirectMl;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private InferenceSession? _session;
    private string? _inputName;
    private string? _outputName;
    private string _engine = "Not initialized";
    private int _disposeStarted;

    public UpscalerService(string? modelPath = null, bool tryDirectMl = true)
    {
        _modelPath = modelPath ?? DefaultModelPath;
        _tryDirectMl = tryDirectMl;
    }

    public string Engine => _engine;
    public string ModelPath => _modelPath;

    public async Task<BitmapSource> UpscaleAsync(
        BitmapSource input,
        UpscaleScale scale,
        IProgress<UpscaleProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (scale is not (UpscaleScale.X2 or UpscaleScale.X4))
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be 2× or 4×.");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linkedCancellation.Token;
        await _runGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => UpscaleCore(input, scale, progress, token), token).ConfigureAwait(false);
        }
        finally
        {
            _runGate.Release();
        }
    }

    private BitmapSource UpscaleCore(
        BitmapSource source,
        UpscaleScale requestedScale,
        IProgress<UpscaleProgress>? progress,
        CancellationToken cancellationToken)
    {
        EnsureSession(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int scale = (int)requestedScale;
        int outputWidth = checked(width * scale);
        int outputHeight = checked(height * scale);
        if (outputWidth > 32767 || outputHeight > 32767)
            throw new InvalidOperationException("The upscaled image exceeds the maximum supported dimensions (32,767 pixels per side). Choose 2× or a smaller source image.");

        var input = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        input.Freeze();
        var output = new WriteableBitmap(outputWidth, outputHeight, input.DpiX, input.DpiY, PixelFormats.Bgra32, null);
        var tiles = TilePlanner.Plan(width, height);
        int completedTiles = 0;
        int largestPatchPixels = checked((TilePlanner.DefaultTileSize + 2 * TilePlanner.DefaultOverlap) *
                                           (TilePlanner.DefaultTileSize + 2 * TilePlanner.DefaultOverlap));
        var sourcePixels = ArrayPool<byte>.Shared.Rent(checked(largestPatchPixels * 4));
        var inputTensorBuffer = ArrayPool<float>.Shared.Rent(checked(largestPatchPixels * 3));
        var outputTileBuffer = ArrayPool<byte>.Shared.Rent(checked(largestPatchPixels * ModelScale * ModelScale * 4));
        var reducedTileBuffer = ArrayPool<byte>.Shared.Rent(checked(TilePlanner.DefaultTileSize * TilePlanner.DefaultTileSize * scale * scale * 4));

        var timer = Stopwatch.StartNew();
        try
        {
            for (int tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tile = tiles[tileIndex];
                int sourceStride = checked(tile.SourceWidth * 4);
                int sourcePixelCount = checked(tile.SourceWidth * tile.SourceHeight);
                input.CopyPixels(new Int32Rect(tile.SourceX, tile.SourceY, tile.SourceWidth, tile.SourceHeight),
                    sourcePixels, sourceStride, 0);

                FillInputTensor(sourcePixels, inputTensorBuffer, tile.SourceWidth, tile.SourceHeight);
                var tensor = new DenseTensor<float>(
                    new Memory<float>(inputTensorBuffer, 0, checked(sourcePixelCount * 3)),
                    new[] { 1, 3, tile.SourceHeight, tile.SourceWidth });

                using var results = _session!.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName!, tensor) });
                var modelOutput = results.First(value => value.Name == _outputName).AsTensor<float>();
                OnnxModelContract.ValidateRuntimeOutput(modelOutput.Dimensions, tile.SourceHeight, tile.SourceWidth, ModelScale);

                if (requestedScale == UpscaleScale.X4)
                {
                    WriteX4Core(modelOutput, tile, outputTileBuffer, output);
                }
                else
                {
                    WriteX2Core(modelOutput, tile, input.DpiX, input.DpiY, outputTileBuffer, reducedTileBuffer, output);
                }

                int completed = tileIndex + 1;
                completedTiles = completed;
                progress?.Report(new UpscaleProgress(
                    5 + completed * 95 / tiles.Count,
                    $"Enhancing image · {_engine} · tile {completed}/{tiles.Count}", completed, tiles.Count));
            }

            cancellationToken.ThrowIfCancellationRequested();
            output.Freeze();
            AppLogger.Information("upscale.completed",
                ("model", _modelPath), ("provider", _engine), ("tileSize", TilePlanner.DefaultTileSize),
                ("tileOverlap", TilePlanner.DefaultOverlap), ("tileCount", tiles.Count),
                ("inputWidth", width), ("inputHeight", height), ("scale", scale),
                ("outputWidth", outputWidth), ("outputHeight", outputHeight), ("elapsedMs", timer.ElapsedMilliseconds));
            progress?.Report(new UpscaleProgress(100, "Upscale complete.", tiles.Count, tiles.Count));
            return output;
        }
        catch (OperationCanceledException)
        {
            AppLogger.Information("upscale.cancelled", ("provider", _engine), ("tilesCompleted", completedTiles), ("tileCount", tiles.Count));
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Error("upscale.failed", ex, ("provider", _engine), ("inputWidth", width), ("inputHeight", height));
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sourcePixels);
            ArrayPool<float>.Shared.Return(inputTensorBuffer);
            ArrayPool<byte>.Shared.Return(outputTileBuffer);
            ArrayPool<byte>.Shared.Return(reducedTileBuffer);
        }
    }

    private static void FillInputTensor(byte[] pixels, float[] tensor, int width, int height)
    {
        int planeSize = checked(width * height);
        for (int y = 0; y < height; y++)
        {
            int pixelOffset = y * width * 4;
            int tensorOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                int pixel = pixelOffset + x * 4;
                int index = tensorOffset + x;
                tensor[index] = pixels[pixel + 2] / 255f;
                tensor[planeSize + index] = pixels[pixel + 1] / 255f;
                tensor[2 * planeSize + index] = pixels[pixel] / 255f;
            }
        }
    }

    private static void WriteX4Core(Tensor<float> modelOutput, ProcessingTile tile, byte[] tilePixels, WriteableBitmap output)
    {
        int cropX = (tile.CoreX - tile.SourceX) * ModelScale;
        int cropY = (tile.CoreY - tile.SourceY) * ModelScale;
        int tileWidth = checked(tile.CoreWidth * ModelScale);
        int tileHeight = checked(tile.CoreHeight * ModelScale);
        int stride = checked(tileWidth * 4);

        for (int y = 0; y < tileHeight; y++)
        {
            int sourceY = cropY + y;
            for (int x = 0; x < tileWidth; x++)
            {
                int sourceX = cropX + x;
                int pixel = (y * tileWidth + x) * 4;
                tilePixels[pixel] = ToByte(modelOutput[0, 2, sourceY, sourceX]);
                tilePixels[pixel + 1] = ToByte(modelOutput[0, 1, sourceY, sourceX]);
                tilePixels[pixel + 2] = ToByte(modelOutput[0, 0, sourceY, sourceX]);
                tilePixels[pixel + 3] = 255;
            }
        }

        output.WritePixels(new Int32Rect(tile.CoreX * ModelScale, tile.CoreY * ModelScale, tileWidth, tileHeight),
            tilePixels, stride, 0);
    }

    private static void WriteX2Core(
        Tensor<float> modelOutput,
        ProcessingTile tile,
        double dpiX,
        double dpiY,
        byte[] patchPixels,
        byte[] reducedTilePixels,
        WriteableBitmap output)
    {
        int patchWidth = checked(tile.SourceWidth * ModelScale);
        int patchHeight = checked(tile.SourceHeight * ModelScale);
        int patchStride = checked(patchWidth * 4);
        for (int y = 0; y < patchHeight; y++)
        {
            for (int x = 0; x < patchWidth; x++)
            {
                int pixel = (y * patchWidth + x) * 4;
                patchPixels[pixel] = ToByte(modelOutput[0, 2, y, x]);
                patchPixels[pixel + 1] = ToByte(modelOutput[0, 1, y, x]);
                patchPixels[pixel + 2] = ToByte(modelOutput[0, 0, y, x]);
                patchPixels[pixel + 3] = 255;
            }
        }

        var patch = BitmapSource.Create(patchWidth, patchHeight, dpiX, dpiY, PixelFormats.Bgra32, null,
            patchPixels, patchStride);
        patch.Freeze();
        var halfSize = new TransformedBitmap(patch, new ScaleTransform(0.5, 0.5));
        halfSize.Freeze();

        int coreX = (tile.CoreX - tile.SourceX) * 2;
        int coreY = (tile.CoreY - tile.SourceY) * 2;
        int coreWidth = tile.CoreWidth * 2;
        int coreHeight = tile.CoreHeight * 2;
        var core = new CroppedBitmap(halfSize, new Int32Rect(coreX, coreY, coreWidth, coreHeight));
        core.Freeze();
        int coreStride = checked(coreWidth * 4);
        core.CopyPixels(reducedTilePixels, coreStride, 0);
        output.WritePixels(new Int32Rect(tile.CoreX * 2, tile.CoreY * 2, coreWidth, coreHeight),
            reducedTilePixels, coreStride, 0);
    }

    private void EnsureSession(CancellationToken cancellationToken)
    {
        if (_session is not null)
            return;
        if (!File.Exists(_modelPath))
            throw new FileNotFoundException("The local Real-ESRGAN x4 model was not found.", _modelPath);
        cancellationToken.ThrowIfCancellationRequested();

        // Validate the graph with CPU first so a corrupt or incompatible model is never reported as a DirectML issue.
        using (var validationOptions = CreateSessionOptions())
        using (var validationSession = new InferenceSession(_modelPath, validationOptions))
        {
            ReadAndValidateModelContract(validationSession);
        }

        if (_tryDirectMl)
        {
            using var directMlOptions = CreateSessionOptions();
            try
            {
                directMlOptions.AppendExecutionProvider_DML(0);
                var directMlSession = new InferenceSession(_modelPath, directMlOptions);
                ReadAndValidateModelContract(directMlSession);
                _session = directMlSession;
                _engine = "DirectML GPU";
                AppLogger.Information("inference.provider_selected", ("provider", _engine), ("model", _modelPath));
                return;
            }
            catch (Exception ex) when (IsDirectMlInitializationFailure(ex))
            {
                AppLogger.Warning("inference.directml_fallback", ("reason", ex.Message), ("exceptionType", ex.GetType().FullName));
            }
        }

        using var cpuOptions = CreateSessionOptions();
        _session = new InferenceSession(_modelPath, cpuOptions);
        ReadAndValidateModelContract(_session);
        _engine = "CPU";
        AppLogger.Information("inference.provider_selected", ("provider", _engine), ("model", _modelPath));
    }

    private static SessionOptions CreateSessionOptions() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        EnableMemoryPattern = false,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
    };

    private static bool IsDirectMlInitializationFailure(Exception exception) =>
        exception is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;

    private void ReadAndValidateModelContract(InferenceSession session)
    {
        if (session.InputMetadata.Count != 1 || session.OutputMetadata.Count != 1)
            throw new InvalidDataException("The Real-ESRGAN model must have one input and one output tensor.");

        var input = session.InputMetadata.Single();
        var output = session.OutputMetadata.Single();
        OnnxModelContract.ValidateTensorShape("input", input.Value.Dimensions);
        OnnxModelContract.ValidateTensorShape("output", output.Value.Dimensions);
        _inputName = input.Key;
        _outputName = output.Key;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)Math.Round(value * 255f), 0, 255);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        _lifetime.Cancel();
        await _runGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _session?.Dispose();
            _session = null;
        }
        finally
        {
            _runGate.Release();
            _runGate.Dispose();
            _lifetime.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
