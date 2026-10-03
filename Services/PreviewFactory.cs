using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageUpscaller.Services;

public static class PreviewFactory
{
    public const int MaximumPreviewDimension = 1200;

    public static BitmapSource Create(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int longestSide = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longestSide <= MaximumPreviewDimension)
            return source;

        double scale = (double)MaximumPreviewDimension / longestSide;
        var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        transformed.Freeze();
        var converted = new FormatConvertedBitmap(transformed, PixelFormats.Bgra32, null, 0);
        converted.Freeze();

        int stride = checked(converted.PixelWidth * 4);
        var pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        var preview = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight,
            converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        preview.Freeze();
        return preview;
    }
}
