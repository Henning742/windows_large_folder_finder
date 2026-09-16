using DataFinder.Core.Models;
using DataFinder.Core.Preview;
using DataFinder.Core.Preview.Raw;
using DataFinder.Core.Results;
using Xunit;

namespace DataFinder.Core.Tests;

/// <summary>
/// The piece the web page and the window both gather their thumbnails with, so that a folder reads
/// the same way whichever of the two is asked.
/// </summary>
public sealed class FolderPictureFinderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "datafinder-tests", Guid.NewGuid().ToString("N"));

    public FolderPictureFinderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void HandsOutThePicturesAndTheRecordingsOfAFolder()
    {
        string set1 = Folder("set1");
        WritePicture(Path.Combine(set1, "shot.png"), 16, 8);
        WriteRecording(Path.Combine(set1, "frame0001.dat"));
        File.WriteAllText(Path.Combine(set1, "readme.txt"), "not a picture");

        FolderPictures found = Finder().Find(
            set1, List, RawFileTypes.Default, Schemas(), stretch: true, CancellationToken.None);

        Assert.Equal(2, found.Pictures.Count);
        Assert.Contains(found.Pictures, picture => picture.Caption == "shot.png" && picture.MimeType == "image/png");
        Assert.Contains(found.Pictures, picture => picture.Caption == "frame0001.dat" && picture.Note.Contains("decoded with"));
        Assert.Null(found.Note);
    }

    [Fact]
    public void CountsWhatItHasTakenOnAndWhatTheRoomRanOutOn()
    {
        string set1 = Folder("set1");
        string set2 = Folder("set2");
        WritePicture(Path.Combine(set1, "one.png"), 8, 8);
        WritePicture(Path.Combine(set2, "two.png"), 8, 8);

        // Room for one picture only - the second folder is turned away, and both the folder itself
        // and the finder as a whole say so.
        var finder = new FolderPictureFinder(
            new HtmlReportLimits { MaxPictureBytes = 1024, MaxTotalPictureBytes = 1024 },
            new Random(1));

        FolderPictures first = finder.Find(
            set1, List, RawFileTypes.Default, Schemas(), stretch: true, CancellationToken.None);
        FolderPictures second = finder.Find(
            set2, List, RawFileTypes.Default, Schemas(), stretch: true, CancellationToken.None);

        Assert.Single(first.Pictures);
        Assert.Empty(second.Pictures);
        Assert.Equal("There was no room left in the report for the pictures of this folder.", second.Note);
        Assert.Equal(1, finder.Pictures);
        Assert.Equal(1, finder.LeftOut);
    }

    [Fact]
    public void TakesAPictureOfAnySizeWhenNoBoundIsSet()
    {
        string set1 = Folder("set1");
        File.WriteAllBytes(Path.Combine(set1, "huge.png"), new byte[5 * 1024 * 1024]);

        FolderPictures found = Finder().Find(
            set1, List, RawFileTypes.Default, Schemas(), stretch: true, CancellationToken.None);

        Assert.Single(found.Pictures);
        Assert.Equal(5 * 1024 * 1024, found.Pictures[0].Bytes.Length);
        Assert.Null(found.Note);
    }

    [Fact]
    public void SaysWhenThereIsNothingToShow()
    {
        string set1 = Folder("empty");

        FolderPictures found = Finder().Find(
            set1, List, RawFileTypes.Default, Schemas(), stretch: true, CancellationToken.None);

        Assert.Empty(found.Pictures);
        Assert.Equal("There are no files directly inside this folder.", found.Note);
    }

    [Fact]
    public void StopsWhenItIsCancelled()
    {
        string set1 = Folder("set1");
        WritePicture(Path.Combine(set1, "shot.png"), 8, 8);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => Finder().Find(
            set1, List, RawFileTypes.Default, Schemas(), stretch: true, cancellation.Token));
    }

    private static FolderPictureFinder Finder() => new(random: new Random(1));

    /// <summary>What sits inside a folder, straight from the file system.</summary>
    private static IReadOnlyList<FileEntry> List(string path) => FileSystemListing.EnumerateChildren(path);

    private static IReadOnlyList<RawSchema> Schemas() => new[]
    {
        new RawSchema { Name = "16 bit grayscale 16 x 8", Width = 16, Height = 8, DataType = RawDataType.U16 },
    };

    private string Folder(string name)
    {
        string path = Path.Combine(_folder, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WritePicture(string path, int width, int height)
    {
        var pixels = new byte[width * height];
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = (byte)(index * 5);
        }

        File.WriteAllBytes(
            path,
            RawPngWriter.Encode(new RawFrame
            {
                Width = width,
                Height = height,
                Format = RawPixelFormat.Gray8,
                Pixels = pixels,
            }));
    }

    private static void WriteRecording(string path)
    {
        var bytes = new byte[16 * 8 * 2];
        for (int index = 0; index < bytes.Length; index += 2)
        {
            bytes[index] = 0x40;
            bytes[index + 1] = 0x10;
        }

        File.WriteAllBytes(path, bytes);
    }
}
