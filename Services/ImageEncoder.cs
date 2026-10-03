using ImageUpscaller.Models;
using System.IO;
using System.Windows.Media.Imaging;

namespace ImageUpscaller.Services;

public sealed class ImageEncoder
{
    public async Task<long> EncodeAsync(
        BitmapSource source,
        string path,
        OutputFormat format,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        switch (format)
        {
            case OutputFormat.Png:
                await Task.Run(() => PngEncoder.Save(source, path, cancellationToken), cancellationToken).ConfigureAwait(false);
                break;
            case OutputFormat.Webp:
                await Task.Run(() => WebpEncoder.Save(source, path, cancellationToken), cancellationToken).ConfigureAwait(false);
                break;
            case OutputFormat.AvifQuality:
            case OutputFormat.AvifTargetSize:
                await Task.Run(() => AvifEncoder.Save(source, path, format, cancellationToken), cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported output format.");
        }

        var bytes = new FileInfo(path).Length;
        AppLogger.Information("image.encoded", ("format", format), ("path", path), ("bytes", bytes), ("elapsedMs", timer.ElapsedMilliseconds));
        return bytes;
    }
}
