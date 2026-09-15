using DataFinder.Core.Preview.Raw;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class RawBordersTests
{
    [Fact]
    public void ReadsFourNumbers()
    {
        Assert.True(RawBorders.TryParse("1,1,0,0", out RawBorders borders, out string? error));
        Assert.Null(error);
        Assert.Equal(new RawBorders(1, 1, 0, 0), borders);
    }

    [Fact]
    public void ReadsFourNumbersSeparatedBySpaces()
    {
        Assert.Equal(new RawBorders(1, 2, 3, 4), RawBorders.Parse("1 2 3 4"));
    }

    [Fact]
    public void TreatsAnEmptyBoxAsNoCrop()
    {
        Assert.True(RawBorders.TryParse("  ", out RawBorders borders, out string? error));
        Assert.Null(error);
        Assert.True(borders.IsNone);
        Assert.Equal((640, 480), borders.ApplyTo(640, 480));
    }

    [Fact]
    public void ComplainsAboutTheWrongNumberOfNumbers()
    {
        Assert.False(RawBorders.TryParse("1,1", out _, out string? error));
        Assert.Contains("four numbers", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ComplainsAboutANegativeCrop()
    {
        Assert.False(RawBorders.TryParse("1,-1,0,0", out _, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void WritesBackWhatItRead()
    {
        Assert.Equal("1,1,0,0", new RawBorders(1, 1, 0, 0).ToString());
        Assert.Equal("no crop", RawBorders.None.Describe());
        Assert.Equal("crop 1,1,0,0", new RawBorders(1, 1, 0, 0).Describe());
    }
}
