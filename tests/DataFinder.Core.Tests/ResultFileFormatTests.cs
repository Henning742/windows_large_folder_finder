using DataFinder.Core.Models;
using DataFinder.Core.Results;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class ResultFileFormatTests
{
    [Fact]
    public void WritesAHeaderAndOneFolderPerLine()
    {
        var results = new List<FolderResult>
        {
            Folder("/tmp/a", 100),
            Folder("/tmp/b", 50),
        };

        string text = ResultFileFormat.Serialize(results, new ScanSettings(), generatedAt: DateTimeOffset.UnixEpoch);
        string[] lines = SplitLines(text);

        Assert.StartsWith("# NTFS Folder Finder results", lines[0]);
        Assert.Contains(lines, line => line.StartsWith("# rules:", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("# folders: 2", StringComparison.Ordinal));
        Assert.Contains("/tmp/a", lines);
        Assert.Contains("/tmp/b", lines);
    }

    [Fact]
    public void IgnoresCommentsBlankLinesAndDuplicates()
    {
        const string text = """
            # a comment
            C:\data\one

            C:\data\two   # trailing comment
              C:\data\three  
            c:\DATA\ONE
            """;

        IReadOnlyList<string> paths = ResultFileFormat.ParsePaths(text);

        Assert.Equal(new[] { @"C:\data\one", @"C:\data\two", @"C:\data\three" }, paths);
    }

    [Fact]
    public void CommentsOutAnEntireLineWhenItStartsWithHash()
    {
        Assert.Empty(ResultFileFormat.ParsePaths("# C:\\data\\one"));
    }

    [Fact]
    public void EscapesAHashInsideAPathSoItSurvivesARoundTrip()
    {
        var results = new List<FolderResult> { Folder(@"C:\data\#raw", 10) };

        string text = ResultFileFormat.Serialize(results);
        IReadOnlyList<string> paths = ResultFileFormat.ParsePaths(text);

        Assert.Contains(@"\#", text);
        Assert.Equal(new[] { @"C:\data\#raw" }, paths);
    }

    [Fact]
    public void SurvivesAByteOrderMark()
    {
        IReadOnlyList<string> paths = ResultFileFormat.ParsePaths("\uFEFFC:\\data\\one\r\n");

        Assert.Equal(new[] { @"C:\data\one" }, paths);
    }

    private static FolderResult Folder(string path, long size) => new()
    {
        FullPath = path,
        SizeBytes = size,
        TotalSizeBytes = size,
    };

    /// <summary>
    /// Splits the serialized text into lines. The writer ends its lines with the newline of the
    /// machine it runs on ("\r\n" on Windows, "\n" elsewhere), so these assertions must not care
    /// which one it happened to be.
    /// </summary>
    private static string[] SplitLines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
}
