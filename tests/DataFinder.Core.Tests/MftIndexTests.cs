using DataFinder.Core.Models;
using DataFinder.Core.Ntfs;
using DataFinder.Core.Tests.Support;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class MftIndexTests
{
    private const long Megabyte = 1024L * 1024L;

    [Fact]
    public void ReportsOnlyFoldersThatMatchBothRules()
    {
        var index = new MftIndex();
        AddDirectory(index, 5, 5, ".");
        AddDirectory(index, 100, 5, "dataset");
        AddDirectory(index, 101, 5, "single-file");
        AddFile(index, 200, 100, "a.bin", 60 * Megabyte);
        AddFile(index, 201, 100, "b.bin", 60 * Megabyte);
        AddFile(index, 202, 100, "c.bin", 60 * Megabyte);
        AddFile(index, 203, 101, "huge.bin", 500 * Megabyte);

        var settings = new ScanSettings { MinSizeBytes = 150 * Megabyte, MinDirectFileCount = 2 };
        AggregationResult aggregation = index.Build(settings, @"D:\");

        Assert.True(aggregation.RootFound);
        FolderResult result = Assert.Single(aggregation.Results);
        Assert.Equal(@"D:\dataset", result.FullPath);
        Assert.Equal(3, result.DirectFileCount);
        Assert.Equal(180 * Megabyte, result.TotalSizeBytes);
        Assert.Equal(180 * Megabyte, result.SizeBytes);
        Assert.Equal(3, result.TotalFileCount);
    }

    [Fact]
    public void TheSizeRuleCanIncludeOrIgnoreSubfolders()
    {
        var index = new MftIndex();
        AddDirectory(index, 5, 5, ".");
        AddDirectory(index, 110, 5, "parent");
        AddDirectory(index, 111, 110, "child");
        AddFile(index, 300, 110, "top.bin", 10 * Megabyte);
        AddFile(index, 301, 111, "deep.bin", 100 * Megabyte);

        AggregationResult recursive = index.Build(
            new ScanSettings { MinSizeBytes = 50 * Megabyte, MinDirectFileCount = 0, SizeIncludesSubfolders = true },
            @"D:\");

        Assert.Equal(
            new[] { @"D:\parent", @"D:\parent\child" },
            recursive.Results.Select(result => result.FullPath).ToArray());

        AggregationResult direct = index.Build(
            new ScanSettings { MinSizeBytes = 50 * Megabyte, MinDirectFileCount = 0, SizeIncludesSubfolders = false },
            @"D:\");

        Assert.Equal(@"D:\parent\child", Assert.Single(direct.Results).FullPath);
    }

    [Fact]
    public void ListsTheContentsOfAFolderWithFoldersFirst()
    {
        var index = new MftIndex();
        AddDirectory(index, 5, 5, ".");
        AddDirectory(index, 110, 5, "parent");
        AddDirectory(index, 111, 110, "child");
        AddFile(index, 300, 110, "top.bin", 10 * Megabyte);
        AddFile(index, 301, 111, "deep.bin", 100 * Megabyte);

        AggregationResult aggregation = index.Build(new ScanSettings(), @"D:\");
        IReadOnlyList<FileEntry> children = index.GetChildren(aggregation, 110, @"D:\parent");

        Assert.Equal(2, children.Count);
        Assert.True(children[0].IsDirectory);
        Assert.Equal("child", children[0].Name);
        Assert.Equal(@"D:\parent\child", children[0].FullPath);
        Assert.Equal(100 * Megabyte, children[0].SizeBytes);
        Assert.False(children[1].IsDirectory);
        Assert.Equal("top.bin", children[1].Name);
        Assert.Equal(10 * Megabyte, children[1].SizeBytes);
    }

    [Fact]
    public void SkipsNtfsBookkeepingRecords()
    {
        var index = new MftIndex();
        AddDirectory(index, 5, 5, ".");
        AddFile(index, 10, 5, "$LogFile", 1024);
        AddFile(index, 400, 5, "$BadClus", 1024);
        AddFile(index, 401, 5, "normal.bin", 1024);

        AggregationResult aggregation = index.Build(new ScanSettings(), @"D:\");

        Assert.Equal(1, aggregation.Stats[5].DirectFileCount);
        Assert.Equal(1024, aggregation.Stats[5].DirectSize);
        Assert.Equal(1, index.DirectoryCount);
    }

    [Fact]
    public void ReportsFoldersThatCannotBeReachedFromTheRoot()
    {
        var index = new MftIndex();
        AddDirectory(index, 5, 5, ".");
        AddDirectory(index, 120, 999, "orphan");

        AggregationResult aggregation = index.Build(new ScanSettings(), @"D:\");

        Assert.Equal(1, aggregation.OrphanDirectoryCount);
        Assert.Contains(aggregation.Warnings, warning => warning.Contains("not reachable", StringComparison.Ordinal));
        Assert.Empty(aggregation.Results);
    }

    [Fact]
    public void FindsTheRecordNumberOfAKnownFolder()
    {
        var index = new MftIndex();
        AddDirectory(index, 5, 5, ".");
        AddDirectory(index, 110, 5, "parent");
        AddFile(index, 300, 110, "top.bin", 1024);

        AggregationResult aggregation = index.Build(new ScanSettings(), @"D:\");

        Assert.True(aggregation.TryGetRecordNumber(@"D:\parent", out uint recordNumber));
        Assert.Equal(110u, recordNumber);
        Assert.False(aggregation.TryGetRecordNumber(@"D:\missing", out _));
    }

    private static void AddDirectory(MftIndex index, uint recordNumber, uint parent, string name)
    {
        byte[] record = new MftRecordBuilder(recordNumber, isDirectory: true)
            .AddFileNameAttribute(parent, name, 0, isDirectory: true)
            .Build();

        Add(index, record, recordNumber);
    }

    private static void AddFile(MftIndex index, uint recordNumber, uint parent, string name, long size)
    {
        byte[] record = new MftRecordBuilder(recordNumber, isDirectory: false)
            .AddFileNameAttribute(parent, name, size, isDirectory: false)
            .AddNonResidentDataAttribute(size)
            .Build();

        Add(index, record, recordNumber);
    }

    private static void Add(MftIndex index, byte[] record, uint recordNumber)
    {
        MftRecordParseResult? parsed = new MftRecordParser().Parse(record, MftRecordBuilder.SectorSize, recordNumber);
        Assert.NotNull(parsed);
        index.Add(parsed!);
    }
}
