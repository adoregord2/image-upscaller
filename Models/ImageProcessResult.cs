using System.Windows.Media.Imaging;

namespace ImageUpscaller.Models;

public sealed record ImageProcessResult(
    BitmapSource Preview,
    int Width,
    int Height,
    long OutputBytes,
    TimeSpan UpscaleTime,
    TimeSpan EncodeTime,
    TimeSpan TotalTime,
    long ApproximateWorkingSetDeltaBytes);
