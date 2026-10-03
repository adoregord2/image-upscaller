namespace ImageUpscaller.Models;

public readonly record struct ProcessingTile(
    int SourceX,
    int SourceY,
    int SourceWidth,
    int SourceHeight,
    int CoreX,
    int CoreY,
    int CoreWidth,
    int CoreHeight);
