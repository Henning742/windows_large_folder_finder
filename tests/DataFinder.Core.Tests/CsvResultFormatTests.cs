using DataFinder.Core.Models;
using DataFinder.Core.Results;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class CsvResultFormatTests : IDisposable
{
    private static readonly string[] ExpectedHeader =
    {
        "Path", "Comment", "SizeBytes", "Size", "DirectFiles", "Subfolders", "TotalFiles", "TotalSizeBytes", "Exists",
    };

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "datafinder-tests", Guid.NewGuid().ToString("N"));

    public CsvResultFormatTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void WritesAHeaderAndOneRowPerFolder()
    {
        string text = CsvResultFormat.Serialize(new[]
        {
            Folder(@"D:\data\set1", 200 * 1024 * 1024, comment: "looks interesting"),
            Folder(@"D:\data\set2", 300 * 1024 * 1024),
        });

        string[] lines = SplitLines(text);

        Assert.Equal(ExpectedHeader, SplitFields(lines[0]));
        Assert.Equal(3, lines.Length);
        Assert.StartsWith(@"D:\data\set1,looks interesting,209715200,200 MB,3,1,9,", lines[1], StringComparison.Ordinal);
        Assert.StartsWith(@"D:\data\set2,,314572800,300 MB,", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsThePathAndTheCommentBack()
    {
        var written = new List<FolderResult> { Folder(@"D:\data\set1", 1024, "checked on monday") };

        IReadOnlyList<ResultRow> rows = CsvResultFormat.Parse(CsvResultFormat.Serialize(written));

        ResultRow row = Assert.Single(rows);
        Assert.Equal(@"D:\data\set1", row.Path);
        Assert.Equal("checked on monday", row.Comment);
    }

    [Fact]
    public void ReadsTheNumbersBackWithTheRow()
    {
        var written = new List<FolderResult> { Folder(@"D:\data\set1", 200 * 1024 * 1024, "checked on monday") };

        ResultRow row = Assert.Single(CsvResultFormat.Parse(CsvResultFormat.Serialize(written)));

        ReportedFolderNumbers numbers = Assert.IsType<ReportedFolderNumbers>(row.Numbers);
        Assert.Equal(200L * 1024 * 1024, numbers.SizeBytes);
        Assert.Equal(600L * 1024 * 1024, numbers.TotalSizeBytes);
        Assert.Equal(3, numbers.DirectFileCount);
        Assert.Equal(1, numbers.SubfolderCount);
        Assert.Equal(9L, numbers.TotalFileCount);
        Assert.True(numbers.Exists);
    }

    [Fact]
    public void LeavesARowWithoutNumbersWhenTheReportDoesNotHoldThemAll()
    {
        // A report that was edited down to the paths and notes it has: there is nothing to show a
        // folder from, and half a row would be worse than measuring the folder.
        const string text = """
            Path,Comment,Size
            C:\data\one,worth a look,200 MB
            """;

        ResultRow row = Assert.Single(CsvResultFormat.Parse(text));

        Assert.Equal(@"C:\data\one", row.Path);
        Assert.Equal("worth a look", row.Comment);
        Assert.Null(row.Numbers);
    }

    [Fact]
    public void LeavesARowWithoutNumbersWhenOneOfThemCannotBeRead()
    {
        const string text = """
            Path,SizeBytes,TotalSizeBytes,DirectFiles,Subfolders,TotalFiles,Exists
            C:\data\one,1024,2048,3,1,not a number,yes
            """;

        ResultRow row = Assert.Single(CsvResultFormat.Parse(text));

        Assert.Null(row.Numbers);
    }

    [Fact]
    public void ReadsTheExistenceFlagARowWasWrittenWith()
    {
        var written = new List<FolderResult> { Folder(@"D:\data\set1", 1024) };
        string text = CsvResultFormat.Serialize(written).Replace(",yes\r\n", ",no\r\n", StringComparison.Ordinal);

        ResultRow row = Assert.Single(CsvResultFormat.Parse(text));

        Assert.False(Assert.IsType<ReportedFolderNumbers>(row.Numbers).Exists);
    }

    [Fact]
    public void ReadsTheNumbersOfAReportThatLeavesTheExistenceFlagOut()
    {
        const string text = """
            Path,SizeBytes,TotalSizeBytes,DirectFiles,Subfolders,TotalFiles
            C:\data\one,1024,2048,3,1,9
            """;

        ReportedFolderNumbers numbers = Assert.IsType<ReportedFolderNumbers>(Assert.Single(CsvResultFormat.Parse(text)).Numbers);

        Assert.Equal(1024L, numbers.SizeBytes);
        Assert.Equal(2048L, numbers.TotalSizeBytes);
        Assert.Equal(3, numbers.DirectFileCount);
        Assert.Equal(1, numbers.SubfolderCount);
        Assert.Equal(9L, numbers.TotalFileCount);
    }

    [Fact]
    public void QuotesPathsAndCommentsThatACellWouldOtherwiseBreak()
    {
        var written = new List<FolderResult>
        {
            Folder(@"D:\data\one, two", 1024, "said \"keep\"\nsecond line"),
        };

        string text = CsvResultFormat.Serialize(written);
        ResultRow row = Assert.Single(CsvResultFormat.Parse(text));

        Assert.Contains("\"D:\\data\\one, two\"", text, StringComparison.Ordinal);
        Assert.Contains("\"said \"\"keep\"\"\nsecond line\"", text, StringComparison.Ordinal);
        Assert.Equal(@"D:\data\one, two", row.Path);
        Assert.Equal("said \"keep\"\nsecond line", row.Comment);
    }

    [Fact]
    public void KeepsACommentThatStartsWithAHash()
    {
        string text = CsvResultFormat.Serialize(new[] { Folder(@"D:\data\set1", 1024, "#1 candidate") });

        ResultRow row = Assert.Single(CsvResultFormat.Parse(text));

        Assert.Equal("#1 candidate", row.Comment);
    }

    [Fact]
    public void FindsTheColumnsByNameWhateverOrderTheyAreIn()
    {
        const string text = """
            Size,Comment,Path,Extra
            200 MB,worth a look,C:\data\set1,x
            """;

        ResultRow row = Assert.Single(CsvResultFormat.Parse(text));

        Assert.Equal(@"C:\data\set1", row.Path);
        Assert.Equal("worth a look", row.Comment);
    }

    [Fact]
    public void ReadsAPlainListOfPathsWithNoHeader()
    {
        const string text = """
            C:\data\one
            C:\data\two
            """;

        IReadOnlyList<ResultRow> rows = CsvResultFormat.Parse(text);

        Assert.Equal(new[] { @"C:\data\one", @"C:\data\two" }, rows.Select(row => row.Path).ToArray());
        Assert.All(rows, row => Assert.Empty(row.Comment));
    }

    [Fact]
    public void SkipsCommentLinesBlankRowsAndRepeats()
    {
        const string text = "Path,Comment\r\n# a note\r\n\r\nC:\\data\\one,\r\nc:\\DATA\\ONE,\r\n,\r\nC:\\data\\two,second\r\n";

        IReadOnlyList<ResultRow> rows = CsvResultFormat.Parse(text);

        Assert.Equal(new[] { @"C:\data\one", @"C:\data\two" }, rows.Select(row => row.Path).ToArray());
        Assert.Equal("second", rows[1].Comment);
    }

    [Fact]
    public void SurvivesAByteOrderMark()
    {
        ResultRow row = Assert.Single(CsvResultFormat.Parse("\uFEFFPath,Comment\r\nC:\\data\\one,hi\r\n"));

        Assert.Equal(@"C:\data\one", row.Path);
        Assert.Equal("hi", row.Comment);
    }

    [Fact]
    public void SavesTheReportAndTheFileThatDescribesIt()
    {
        string csvPath = Path.Combine(_folder, "folders-D-20260915.csv");
        var metadata = new ReportMetadata
        {
            GeneratedAt = DateTimeOffset.UnixEpoch,
            Volumes = { new ReportVolume { DriveLetter = 'D', RootPath = @"D:\", Label = "Work" } },
            Rules = new ReportRules { MinSizeBytes = 200, MinDirectFileCount = 3, Description = "size > 200 B" },
            Scan = new ReportScan { RecordsRead = 10, RecordsInUse = 8, ExpectedRecordCount = 10, ElapsedSeconds = 1.5 },
        };

        CsvResultFormat.Save(csvPath, new[] { Folder(@"D:\data\set1", 1024, "note") }, metadata);

        string metadataPath = CsvResultFormat.MetadataPathFor(csvPath);
        Assert.True(File.Exists(csvPath));
        Assert.True(File.Exists(metadataPath));
        Assert.Equal(Path.Combine(_folder, "folders-D-20260915.meta.json"), metadataPath);

        ReportMetadata? readBack = ReportMetadataFile.Load(metadataPath);
        Assert.NotNull(readBack);
        Assert.Equal("NTFS Folder Finder results", readBack.Format);
        Assert.Equal("folders-D-20260915.csv", readBack.ResultsFile);
        Assert.Equal(1, readBack.FolderCount);
        Assert.Equal("D", readBack.Volumes[0].DriveLetter.ToString());
        Assert.Equal("Work", readBack.Volumes[0].Label);
        Assert.Equal(3, readBack.Rules!.MinDirectFileCount);
        Assert.Equal(10, readBack.Scan!.RecordsRead);
        Assert.Equal(DateTimeOffset.UnixEpoch, readBack.GeneratedAt);
    }

    [Fact]
    public void PointsAtTheReportWithAPathRelativeToTheMetadataFile()
    {
        string csvPath = Path.Combine(_folder, "folders.csv");

        CsvResultFormat.Save(csvPath, Array.Empty<FolderResult>(), new ReportMetadata());

        string json = File.ReadAllText(CsvResultFormat.MetadataPathFor(csvPath));
        Assert.Contains("\"resultsFile\": \"folders.csv\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(_folder.Replace("\\", "\\\\", StringComparison.Ordinal), json, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAReportBackFromDisk()
    {
        string csvPath = Path.Combine(_folder, "folders.csv");
        CsvResultFormat.Save(csvPath, new[] { Folder(@"D:\data\set1", 1024, "keep me") }, new ReportMetadata());

        ResultRow row = Assert.Single(CsvResultFormat.Load(csvPath));

        Assert.Equal(@"D:\data\set1", row.Path);
        Assert.Equal("keep me", row.Comment);
    }

    [Fact]
    public void DescribesTheScanInTheMetadata()
    {
        var reports = new[]
        {
            Report(@"D:\", TimeSpan.FromSeconds(2), warnings: new[] { "one folder was skipped" }),
            Report(@"E:\", TimeSpan.FromSeconds(3)),
        };

        ReportMetadata metadata = CsvResultFormat.BuildMetadata(
            new[] { Volume('D'), Volume('E') },
            new ScanSettings { MinSizeBytes = 1024, MinDirectFileCount = 5, SizeIncludesSubfolders = false },
            reports,
            imported: false);

        Assert.Equal(2, metadata.Volumes.Count);
        Assert.Equal(@"D:\", metadata.Volumes[0].RootPath);
        Assert.Equal(5, metadata.Rules!.MinDirectFileCount);
        Assert.Contains("direct files only", metadata.Rules.Description, StringComparison.Ordinal);
        Assert.Equal(5d, metadata.Scan!.ElapsedSeconds);
        Assert.Equal(2, metadata.Scan.RecordsRead);
        Assert.Single(metadata.Scan.Warnings);
        Assert.True(metadata.Scan.MftReadCompleted);
        Assert.Null(metadata.ImportedFrom);
    }

    [Fact]
    public void MarksAnImportedReportAsImported()
    {
        ReportMetadata metadata = CsvResultFormat.BuildMetadata(
            Array.Empty<VolumeInfo>(),
            settings: null,
            Array.Empty<ScanReport>(),
            imported: true,
            importedFrom: @"C:\old\folders.csv");

        Assert.Equal(@"C:\old\folders.csv", metadata.ImportedFrom);
        Assert.Empty(metadata.Volumes);
        Assert.Null(metadata.Rules);
        Assert.Null(metadata.Scan);
    }

    private static ScanReport Report(string root, TimeSpan elapsed, IReadOnlyList<string>? warnings = null) => new()
    {
        Volume = Volume(root[0]),
        Settings = new ScanSettings(),
        Results = Array.Empty<FolderResult>(),
        Index = new Ntfs.MftIndex(),
        Aggregation = new Ntfs.AggregationResult(
            Array.Empty<FolderResult>(),
            new Dictionary<uint, Ntfs.DirStats>(),
            new Dictionary<string, uint>(),
            Array.Empty<string>(),
            rootFound: true,
            orphanDirectories: 0),
        Elapsed = elapsed,
        RecordsRead = 1,
        ExpectedRecordCount = 1,
        RecordsInUse = 1,
        Warnings = warnings ?? Array.Empty<string>(),
    };

    private static VolumeInfo Volume(char drive) => new()
    {
        DriveLetter = drive,
        RootPath = drive + @":\",
    };

    private static FolderResult Folder(string path, long size, string comment = "") => new()
    {
        FullPath = path,
        SizeBytes = size,
        TotalSizeBytes = size * 3,
        DirectFileCount = 3,
        SubfolderCount = 1,
        TotalFileCount = 9,
        Comment = comment,
    };

    /// <summary>Splits the report into rows. The closing line ending is not a row of its own.</summary>
    private static string[] SplitLines(string text) =>
        text.TrimEnd('\r', '\n').Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    /// <summary>Splits a line into fields, assuming the writer quoted anything that needed it.</summary>
    private static string[] SplitFields(string line) => line.Split(',');
}
