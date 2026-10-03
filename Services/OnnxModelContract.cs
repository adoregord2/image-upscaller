using System.IO;

namespace ImageUpscaller.Services;

public static class OnnxModelContract
{
    public static void ValidateTensorShape(string tensorName, IReadOnlyList<int> dimensions)
    {
        if (dimensions.Count != 4 || dimensions[0] is not (-1 or 1) || dimensions[1] != 3)
            throw new InvalidDataException($"The Real-ESRGAN {tensorName} must be rank-4 NCHW with batch 1 and 3 RGB channels.");
        if (dimensions[2] != -1 || dimensions[3] != -1)
            throw new InvalidDataException($"The Real-ESRGAN {tensorName} must support dynamic height and width for tiled processing.");
    }

    public static void ValidateRuntimeOutput(ReadOnlySpan<int> dimensions, int inputHeight, int inputWidth, int modelScale = 4)
    {
        if (dimensions.Length != 4 || dimensions[0] != 1 || dimensions[1] != 3 ||
            dimensions[2] != inputHeight * modelScale || dimensions[3] != inputWidth * modelScale)
        {
            throw new InvalidDataException(
                $"The model output must be [1, 3, {inputHeight * modelScale}, {inputWidth * modelScale}], but it returned [{string.Join(", ", dimensions.ToArray())}].");
        }
    }
}
