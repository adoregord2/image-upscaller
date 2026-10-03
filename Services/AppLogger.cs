using System.Text.Json;
using System.IO;

namespace ImageUpscaller.Services;

/// <summary>Small JSON-lines logger for diagnostics; it never interrupts image work if logging fails.</summary>
public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageUpscaller", "logs");

    public static void Write(string level, string eventName, IReadOnlyDictionary<string, object?>? fields = null)
    {
        try
        {
            var entry = new LogEntry(DateTimeOffset.UtcNow, level, eventName, fields ?? new Dictionary<string, object?>());
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                var filePath = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.jsonl");
                File.AppendAllText(filePath, json + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never cause an image operation to fail.
        }
    }

    public static void Information(string eventName, params (string Key, object? Value)[] fields) =>
        Write("information", eventName, ToDictionary(fields));

    public static void Warning(string eventName, params (string Key, object? Value)[] fields) =>
        Write("warning", eventName, ToDictionary(fields));

    public static void Error(string eventName, Exception exception, params (string Key, object? Value)[] fields)
    {
        var values = ToDictionary(fields);
        values["exceptionType"] = exception.GetType().FullName;
        values["exceptionMessage"] = exception.Message;
        Write("error", eventName, values);
    }

    private static Dictionary<string, object?> ToDictionary((string Key, object? Value)[] fields) =>
        fields.ToDictionary(field => field.Key, field => field.Value);

    private sealed record LogEntry(DateTimeOffset TimestampUtc, string Level, string Event, IReadOnlyDictionary<string, object?> Fields);
}
