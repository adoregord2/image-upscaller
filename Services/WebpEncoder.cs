using SkiaSharp;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageUpscaller.Services;

public static class WebpEncoder
{
    private const int Quality = 90;

    public static void Save(BitmapSource source, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        bgra.Freeze();

        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = checked(width * 4);
        int byteCount = checked(stride * height);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".{Path.GetFileNameWithoutExtension(path)}-{Guid.NewGuid():N}.webp");
        var pixels = GC.AllocateUninitializedArray<byte>(byteCount, pinned: true);
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var pinnedPixels = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bgra.CopyPixels(pixels, stride, 0);
            using var pixmap = new SKPixmap(imageInfo, pinnedPixels.AddrOfPinnedObject(), stride);
            using (var output = File.Create(temporaryPath))
            {
                var options = new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, Quality);
                if (!SKWebpEncoder.Encode(output, pixmap, options))
                    throw new InvalidOperationException("The WebP encoder could not write the output image.");
                output.Flush();
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            pinnedPixels.Free();
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
