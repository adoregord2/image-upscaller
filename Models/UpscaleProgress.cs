namespace ImageUpscaller.Models;

public sealed record UpscaleProgress(int Percent, string Message, int CompletedTiles = 0, int TotalTiles = 0);
