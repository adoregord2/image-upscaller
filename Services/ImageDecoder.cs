using SkiaSharp;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageUpscaller.Services;

public sealed class ImageDecoder
{
    public BitmapSource Decode(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        var extension = Path.GetExtension(path);
        BitmapSource image = extension.ToLowerInvariant() switch
        {
            ".webp" => DecodeWebp(path),
            ".avif" => DecodeAvif(path, cancellationToken),
            ".png" or ".jpg" or ".jpeg" => DecodeWithWpf(path),
            _ => throw new NotSupportedException("Choose a PNG, JPEG, WebP, or AVIF image.")
        };

        cancellationToken.ThrowIfCancellationRequested();
        AppLogger.Information("image.decoded", ("filename", Path.GetFileName(path)), ("width", image.PixelWidth),
            ("height", image.PixelHeight), ("format", extension.ToLowerInvariant()), ("elapsedMs", timer.ElapsedMilliseconds));
        return image;
    }

    private static BitmapSource DecodeWithWpf(string path)
    {
        var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("The image file does not contain a frame.");

        var frame = decoder.Frames[0];
        BitmapSource image = frame;
        if (frame.ColorContexts is { Count: > 0 } contexts)
        {
            var converted = new ColorConvertedBitmap(frame, contexts[0],
                new ColorContext(PixelFormats.Bgra32), PixelFormats.Bgra32);
            converted.Freeze();
            image = converted;
        }
        image.Freeze();
        return image;
    }

    private static BitmapSource DecodeWebp(string path)
    {
        var bounds = SKBitmap.DecodeBounds(path);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidDataException("The WebP image could not be decoded.");

        using var srgb = SKColorSpace.CreateSrgb();
        var imageInfo = new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, srgb);
        using var bitmap = SKBitmap.Decode(path, imageInfo)
            ?? throw new InvalidDataException("The WebP image could not be decoded.");
        int stride = checked(bounds.Width * 4);
        int bufferSize = checked(stride * bounds.Height);
        var source = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgra32, null,
            bitmap.GetPixels(), bufferSize, stride);
        source.Freeze();
        return source;
    }

    private static BitmapSource DecodeAvif(string path, CancellationToken cancellationToken)
    {
        var decoderPath = Path.Combine(AppContext.BaseDirectory, "Tools", "libavif-v1.4.2", "avifdec.exe");
        if (!File.Exists(decoderPath))
            throw new FileNotFoundException("The bundled AVIF decoder is missing.", decoderPath);

        var temporaryPngPath = Path.Combine(Path.GetTempPath(), $"image-upscaller-{Guid.NewGuid():N}.png");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = decoderPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--jobs");
            startInfo.ArgumentList.Add("2");
            startInfo.ArgumentList.Add(path);
            startInfo.ArgumentList.Add(temporaryPngPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The AVIF decoder could not be started.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));
            process.WaitForExit();
            Task.WaitAll(standardOutput, standardError);
            cancellationToken.ThrowIfCancellationRequested();

            if (process.ExitCode != 0 || !File.Exists(temporaryPngPath))
            {
                var details = standardError.Result.Trim();
                throw new InvalidDataException(string.IsNullOrEmpty(details)
                    ? $"The AVIF decoder exited with code {process.ExitCode}."
                    : $"The AVIF image could not be decoded: {details}");
            }

            return DetachFromDecoder(DecodeWithWpf(temporaryPngPath));
        }
        finally
        {
            if (File.Exists(temporaryPngPath))
                File.Delete(temporaryPngPath);
        }
    }

    private static BitmapSource DetachFromDecoder(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        bgra.Freeze();
        int stride = checked(bgra.PixelWidth * 4);
        var pixels = new byte[checked(stride * bgra.PixelHeight)];
        bgra.CopyPixels(pixels, stride, 0);
        var detached = BitmapSource.Create(bgra.PixelWidth, bgra.PixelHeight, bgra.DpiX, bgra.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        detached.Freeze();
        return detached;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
}
