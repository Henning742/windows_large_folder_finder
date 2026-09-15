using DataFinder.Core.Preview;
using DataFinder.Core.Preview.Raw;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class RawFileTypesTests
{
    [Fact]
    public void TakesTheDotForGranted()
    {
        Assert.Equal(new[] { ".raw", ".bin" }, RawFileTypes.Parse("raw bin"));
    }

    [Fact]
    public void AcceptsSpacesCommasAndSemicolons()
    {
        Assert.Equal(new[] { ".raw", ".bin", ".dat" }, RawFileTypes.Parse(".raw, bin; dat"));
    }

    [Fact]
    public void IgnoresRepeatsAndDifferentLetterCases()
    {
        Assert.Equal(new[] { ".raw", ".bin" }, RawFileTypes.Parse(".RAW .raw Bin"));
    }

    [Fact]
    public void HasTheRecordingsOfTheReferenceScriptsByDefault()
    {
        Assert.Equal(new[] { ".raw", ".bin", ".dat", ".data" }, RawFileTypes.Default);
    }

    [Fact]
    public void HasNoSuffixWhenTheBoxIsEmpty()
    {
        Assert.Empty(RawFileTypes.Parse("   "));
        Assert.Empty(RawFileTypes.Parse(null));
    }

    [Fact]
    public void TellsTheUserAboutSomethingThatCannotBeASuffix()
    {
        Assert.False(RawFileTypes.TryParse(".raw C:\\data\\file.dat", out _, out string? error));
        Assert.Contains("not a file suffix", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsATidyListBack()
    {
        Assert.True(RawFileTypes.TryParse("raw, bin", out IReadOnlyList<string> suffixes, out string? error));
        Assert.Null(error);
        Assert.Equal(new[] { ".raw", ".bin" }, suffixes);
    }

    [Fact]
    public void MatchesTheSuffixOfAFileNameRegardlessOfCase()
    {
        Assert.True(RawFileTypes.Matches(RawFileTypes.Default, @"D:\data\frame0001.DAT"));
        Assert.False(RawFileTypes.Matches(RawFileTypes.Default, @"D:\data\frame0001.jpg"));
        Assert.False(RawFileTypes.Matches(RawFileTypes.Default, @"D:\data\frame0001"));
        Assert.False(RawFileTypes.Matches(null, @"D:\data\frame0001.dat"));
    }

    [Fact]
    public void DescribesTheListInWords()
    {
        Assert.Equal(".raw, .bin and .dat files are read as data files.", RawFileTypes.Describe(new[] { ".raw", ".bin", ".dat" }));
        Assert.Equal(".raw files are read as data files.", RawFileTypes.Describe(new[] { ".raw" }));
        Assert.Contains("No file suffix", RawFileTypes.Describe(Array.Empty<string>()), StringComparison.Ordinal);
    }
}
