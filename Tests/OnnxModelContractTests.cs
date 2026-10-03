using ImageUpscaller.Services;

namespace ImageUpscaller.Tests;

public sealed class OnnxModelContractTests
{
    [Fact]
    public void AcceptsDynamicNchwRgbInputAndOutput()
    {
        OnnxModelContract.ValidateTensorShape("input", new[] { 1, 3, -1, -1 });
        OnnxModelContract.ValidateTensorShape("output", new[] { 1, 3, -1, -1 });
    }

    [Theory]
    [MemberData(nameof(InvalidShapes))]
    public void RejectsInvalidNchwOrFixedSpatialShapes(int[] dimensions)
    {
        Assert.Throws<InvalidDataException>(() => OnnxModelContract.ValidateTensorShape("test", dimensions));
    }

    public static IEnumerable<object[]> InvalidShapes => new[]
    {
        new object[] { new[] { 1, 3, 16 } },
        new object[] { new[] { 1, 4, -1, -1 } },
        new object[] { new[] { 2, 3, -1, -1 } },
        new object[] { new[] { 1, 3, 128, -1 } }
    };

    [Fact]
    public void ValidatesRuntimeOutputRankChannelsAndScale()
    {
        OnnxModelContract.ValidateRuntimeOutput(new[] { 1, 3, 512, 512 }, 128, 128);
        Assert.Throws<InvalidDataException>(() => OnnxModelContract.ValidateRuntimeOutput(new[] { 1, 1, 512, 512 }, 128, 128));
        Assert.Throws<InvalidDataException>(() => OnnxModelContract.ValidateRuntimeOutput(new[] { 1, 3, 256, 512 }, 128, 128));
    }
}
