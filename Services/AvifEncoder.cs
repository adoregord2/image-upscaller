using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using ImageUpscaller.Models;

namespace ImageUpscaller.Services;

public static class AvifEncoder
{
    private const long TargetBytes = 10_000_000;
    private const int Quality = 90;

    public static void Save(BitmapSource source, string path, OutputFormat format, CancellationToken cancellationToken = default)
    {
        if (format is not (OutputFormat.AvifQuality or OutputFormat.AvifTargetSize))
            throw new ArgumentOutOfRangeException(nameof(format));
        var encoderPath = Path.Combine(AppContext.BaseDirectory, "Tools", "libavif-v1.4.2", "avifenc.exe");
        if (!File.Exists(encoderPath))
            throw new FileNotFoundException("The bundled AVIF encoder is missing.", encoderPath);

        var temporaryPngPath = Path.Combine(Path.GetTempPath(), $"image-upscaller-{Guid.NewGuid():N}.png");
        var temporaryAvifPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".{Path.GetFileNameWithoutExtension(path)}-{Guid.NewGuid():N}.avif");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pngEncoder = new PngBitmapEncoder();
            pngEncoder.Frames.Add(BitmapFrame.Create(source));
            using (var pngFile = File.Create(temporaryPngPath))
                pngEncoder.Save(pngFile);

            cancellationToken.ThrowIfCancellationRequested();
            var startInfo = new ProcessStartInfo
            {
                FileName = encoderPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (format == OutputFormat.AvifTargetSize)
            {
                startInfo.ArgumentList.Add("--target-size");
                startInfo.ArgumentList.Add(TargetBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                startInfo.ArgumentList.Add("--qcolor");
                startInfo.ArgumentList.Add(Quality.ToString(System.Globalization.CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add("--qalpha");
                startInfo.ArgumentList.Add("100");
            }
            startInfo.ArgumentList.Add("--jobs");
            startInfo.ArgumentList.Add("2");
            startInfo.ArgumentList.Add("--speed");
            startInfo.ArgumentList.Add("6");
            startInfo.ArgumentList.Add(temporaryPngPath);
            startInfo.ArgumentList.Add(temporaryAvifPath);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("The AVIF encoder could not be started.");

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });
            process.WaitForExit();
            Task.WaitAll(standardOutput, standardError);
            cancellationToken.ThrowIfCancellationRequested();

            if (process.ExitCode != 0)
            {
                var details = standardError.Result.Trim();
                throw new InvalidOperationException(string.IsNullOrEmpty(details)
                    ? $"The AVIF encoder exited with code {process.ExitCode}."
                    : $"The AVIF encoder failed: {details}");
            }

            if (!File.Exists(temporaryAvifPath))
                throw new InvalidOperationException("The AVIF encoder finished without creating an output file.");
            File.Move(temporaryAvifPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPngPath))
                File.Delete(temporaryPngPath);
            if (File.Exists(temporaryAvifPath))
                File.Delete(temporaryAvifPath);
        }
    }
}
