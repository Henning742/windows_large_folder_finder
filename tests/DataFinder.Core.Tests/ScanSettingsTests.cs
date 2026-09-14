using DataFinder.Core.Models;
using DataFinder.Core.Util;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class ScanSettingsTests
{
    [Theory]
    [InlineData("200", 200L * 1024 * 1024)]
    [InlineData("200 MB", 200L * 1024 * 1024)]
    [InlineData("1.5 GB", 1610612736L)]
    [InlineData("2048 KB", 2097152L)]
    [InlineData("500000 B", 500000L)]
    public void ParsesFriendlySizeText(string text, long expectedBytes)
    {
        Assert.True(ByteSize.TryParseUserInput(text, out long bytes, out string? error), error);
        Assert.Equal(expectedBytes, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public void RejectsUnusableSizeText(string text)
    {
        Assert.False(ByteSize.TryParseUserInput(text, out _, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void BuildsSettingsFromUserInput()
    {
        bool parsed = ScanSettings.TryParse("350", "1200", sizeIncludesSubfolders: false, out ScanSettings settings, out string? error);

        Assert.True(parsed, error);
        Assert.Equal(350L * 1024 * 1024, settings.MinSizeBytes);
        Assert.Equal(1200, settings.MinDirectFileCount);
        Assert.False(settings.SizeIncludesSubfolders);
    }

    [Fact]
    public void ReportsBadFileCounts()
    {
        Assert.False(ScanSettings.TryParse("200", "many", true, out _, out string? error));
        Assert.NotNull(error);
        Assert.Contains("whole number", error);
    }

    [Fact]
    public void FormatsByteCounts()
    {
        Assert.Equal("0 B", ByteSize.Format(0));
        Assert.Equal("1 KB", ByteSize.Format(1024));
        Assert.Equal("200 MB", ByteSize.Format(200L * 1024 * 1024));
    }
}

