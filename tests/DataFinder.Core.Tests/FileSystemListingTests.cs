using DataFinder.Core.Preview;
using Xunit;

namespace DataFinder.Core.Tests;

/// <summary>
/// Measuring a folder off the disk, which is what a list of bare paths is imported with.
/// </summary>
public sealed class FileSystemListingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "datafinder-tests", Guid.NewGuid().ToString("N"));

    public FileSystemListingTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void CountsTheFilesOfAFolderOnceAndEverythingBelowItAsWell()
    {
        string folder = Folder("set1");
        WriteFiles(folder, 3, 100);
        WriteFiles(Path.Combine(folder, "sub"), 2, 100);

        FolderMeasurement measured = FileSystemListing.Measure(folder);

        // The three files directly inside are the folder's own files, not two sets of three: the
        // total is the folder and its subfolder, five files and five hundred bytes.
        Assert.True(measured.Exists);
        Assert.Equal(3, measured.DirectFileCount);
        Assert.Equal(300, measured.DirectSizeBytes);
        Assert.Equal(1, measured.SubfolderCount);
        Assert.Equal(5, measured.TotalFileCount);
        Assert.Equal(500, measured.TotalSizeBytes);
    }

    [Fact]
    public void CountsFilesAtEveryDepthBelowTheFolder()
    {
        string folder = Folder("deep");
        WriteFiles(folder, 1, 10);
        WriteFiles(Path.Combine(folder, "one"), 2, 10);
        WriteFiles(Path.Combine(folder, "one", "two", "three"), 4, 10);

        FolderMeasurement measured = FileSystemListing.Measure(folder);

        Assert.Equal(1, measured.DirectFileCount);
        Assert.Equal(1, measured.SubfolderCount);
        Assert.Equal(10, measured.DirectSizeBytes);

        // The folders below the one that was asked about count towards the files, not towards the
        // subfolders, which are the ones sitting directly inside it.
        Assert.Equal(7, measured.TotalFileCount);
        Assert.Equal(70, measured.TotalSizeBytes);
    }

    [Fact]
    public void SaysAFolderIsGoneRatherThanCountingNothingInIt()
    {
        FolderMeasurement measured = FileSystemListing.Measure(Path.Combine(_folder, "not-there"));

        Assert.False(measured.Exists);
        Assert.Equal(0, measured.TotalFileCount);
        Assert.Equal(0, measured.TotalSizeBytes);
    }

    [Fact]
    public void MeasuresAFolderThatHoldsNothing()
    {
        FolderMeasurement measured = FileSystemListing.Measure(Folder("empty"));

        Assert.True(measured.Exists);
        Assert.Equal(0, measured.DirectFileCount);
        Assert.Equal(0, measured.SubfolderCount);
        Assert.Equal(0, measured.TotalFileCount);
        Assert.Equal(0, measured.TotalSizeBytes);
    }

    private string Folder(string name)
    {
        string path = Path.Combine(_folder, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFiles(string folder, int count, int bytes)
    {
        Directory.CreateDirectory(folder);

        for (int index = 0; index < count; index++)
        {
            File.WriteAllBytes(Path.Combine(folder, $"file{index}.bin"), new byte[bytes]);
        }
    }
}
