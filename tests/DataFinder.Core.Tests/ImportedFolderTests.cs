using DataFinder.Core.Models;
using DataFinder.Core.Results;
using Xunit;

namespace DataFinder.Core.Tests;

/// <summary>
/// What an imported list shows: the numbers the report carried, and measurements taken off the
/// disk for a list that arrived without any.
/// </summary>
public sealed class ImportedFolderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "datafinder-tests", Guid.NewGuid().ToString("N"));

    public ImportedFolderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ShowsTheNumbersTheReportWasWrittenWithRatherThanWalkingTheFolderAgain()
    {
        string folder = Folder("set1");
        File.WriteAllBytes(Path.Combine(folder, "one.bin"), new byte[100]);

        // A report from a scan, over a folder that holds one file now: what is shown is what the
        // report says, which is the whole point of importing one rather than scanning again.
        var written = new FolderResult
        {
            FullPath = folder,
            SizeBytes = 200 * 1024 * 1024,
            TotalSizeBytes = 600 * 1024 * 1024,
            DirectFileCount = 412,
            SubfolderCount = 3,
            TotalFileCount = 1590,
            Comment = "worth another look",
        };

        ResultRow row = Assert.Single(CsvResultFormat.Parse(CsvResultFormat.Serialize(new[] { written })));
        FolderResult shown = ImportedFolder.Describe(row);

        Assert.Equal(200L * 1024 * 1024, shown.SizeBytes);
        Assert.Equal(600L * 1024 * 1024, shown.TotalSizeBytes);
        Assert.Equal(412, shown.DirectFileCount);
        Assert.Equal(3, shown.SubfolderCount);
        Assert.Equal(1590L, shown.TotalFileCount);
        Assert.Equal("worth another look", shown.Comment);

        // The folder itself is still looked at, so a folder that has gone is still marked as gone.
        Assert.True(shown.Exists);
    }

    [Fact]
    public void MeasuresAFolderWhenTheListCameWithoutNumbers()
    {
        string folder = Folder("set1");
        File.WriteAllBytes(Path.Combine(folder, "one.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(folder, "two.bin"), new byte[100]);
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllBytes(Path.Combine(folder, "sub", "three.bin"), new byte[100]);

        ResultRow row = Assert.Single(CsvResultFormat.Parse(folder));
        FolderResult shown = ImportedFolder.Describe(row);

        Assert.Null(row.Numbers);
        Assert.True(shown.Exists);
        Assert.Equal(2, shown.DirectFileCount);
        Assert.Equal(300L, shown.TotalSizeBytes);
        Assert.Equal(3L, shown.TotalFileCount);
        Assert.Equal(1, shown.SubfolderCount);
    }

    [Fact]
    public void MarksAFolderThatIsGoneAsNotFound()
    {
        string gone = Path.Combine(_folder, "gone");

        ResultRow row = Assert.Single(CsvResultFormat.Parse(gone));
        FolderResult shown = ImportedFolder.Describe(row);

        Assert.False(shown.Exists);
        Assert.Equal("not found", shown.SizeText);
    }

    [Fact]
    public void MarksAReportedFolderThatHasGoneAsNotFoundToo()
    {
        string gone = Path.Combine(_folder, "gone");
        var written = new FolderResult { FullPath = gone, SizeBytes = 1024, TotalSizeBytes = 2048, DirectFileCount = 4 };

        ResultRow row = Assert.Single(CsvResultFormat.Parse(CsvResultFormat.Serialize(new[] { written })));
        FolderResult shown = ImportedFolder.Describe(row);

        Assert.NotNull(row.Numbers);
        Assert.False(shown.Exists);
        Assert.Equal("not found", shown.SizeText);
    }

    private string Folder(string name)
    {
        string path = Path.Combine(_folder, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
