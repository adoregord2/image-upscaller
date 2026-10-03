using System.Windows.Media.Imaging;

namespace ImageUpscaller.Models;

public sealed record DecodedImage(BitmapSource FullImage, BitmapSource Preview, TimeSpan DecodeTime);
