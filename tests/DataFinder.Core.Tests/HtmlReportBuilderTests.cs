using System.Buffers.Binary;
using DataFinder.Core.Models;
using DataFinder.Core.Preview;
using DataFinder.Core.Preview.Raw;
using DataFinder.Core.Results;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class HtmlReportBuilderTests : IDisposable
{
    private static readonly DateTimeOffset Written = new(2026, 9, 15, 10, 30, 0, TimeSpan.FromHours(8));

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "datafinder-tests", Guid.NewGuid().ToString("N"));

    public HtmlReportBuilderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void WalksTheFoldersThatMatchedAndCarriesTheirPictures()
    {
        string set1 = Folder("set1");
        WritePicture(Path.Combine(set1, "shot.png"), 32, 16);
        WriteRecording(Path.Combine(set1, "frame0001.dat"), width: 8, height: 4, frames: 2);
        File.WriteAllText(Path.Combine(set1, "readme.txt"), "not a picture");

        HtmlReportBuildResult result = Build(new[] { set1 });

        Assert.Equal(1, result.Folders);
        Assert.Equal(2, result.Pictures);
        Assert.Contains("shot.png", result.Html, StringComparison.Ordinal);
        Assert.Contains("shown as it is", result.Html, StringComparison.Ordinal);
        Assert.Contains("frame0001.dat", result.Html, StringComparison.Ordinal);
        Assert.Contains("decoded with 16 bit grayscale 8 x 4, 8 x 4", result.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("readme.txt", result.Html, StringComparison.Ordinal);

        // Each picture becomes a file beside the page, and the page only names it.
        Assert.Equal(2, result.Files.Count);
        Assert.All(result.Files, file => Assert.StartsWith("report.files/", file.RelativePath));
        Assert.All(result.Files, file => Assert.Contains($"src=\"{file.RelativePath}\"", result.Html, StringComparison.Ordinal));
        Assert.DoesNotContain("base64", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void PicksOnlyAFewOfThePicturesThereAre()
    {
        string set1 = Folder("set1");
        for (int index = 0; index < 20; index++)
        {
            WritePicture(Path.Combine(set1, $"shot{index:00}.png"), 8, 8);
        }

        HtmlReportBuildResult result = Build(new[] { set1 }, limits: new HtmlReportLimits { PicturesPerFolder = 3 });

        Assert.Equal(3, result.Pictures);
        Assert.Equal(3, result.Files.Count);
        Assert.Equal(3, Count(result.Html, "<img "));
        Assert.Contains("Showing 3 of the 20 pictures and recordings here, picked at random.", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesThePictureFilesAfterTheFolderTheCallerAskedFor()
    {
        string set1 = Folder("set1");
        WritePicture(Path.Combine(set1, "shot.png"), 8, 8);
        WritePicture(Path.Combine(set1, "another.png"), 8, 8);

        HtmlReportBuildResult result = Build(new[] { set1 }, pictureFolder: "My report.files");

        Assert.Equal(2, result.Files.Count);
        Assert.All(result.Files, file => Assert.StartsWith("My report.files/", file.RelativePath));
        Assert.All(result.Files, file => Assert.EndsWith(".png", file.RelativePath));

        // The number in front is what keeps two folders' pictures of the same name apart.
        Assert.Equal(result.Files.Count, result.Files.Select(file => file.RelativePath).Distinct().Count());
        Assert.Equal(result.Files.Count, result.Files.Select(file => file.RelativePath.Split('/')[^1][..4]).Distinct().Count());
    }

    [Fact]
    public void NotesAFolderWithNothingToShow()
    {
        string set1 = Folder("set1");
        File.WriteAllText(Path.Combine(set1, "one.txt"), "text");
        File.WriteAllText(Path.Combine(set1, "two.log"), "text");

        HtmlReportBuildResult result = Build(new[] { set1 });

        Assert.Equal(0, result.Pictures);
        Assert.Contains("Nothing among the 2 files looked at here could be shown as a picture.", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void NotesRecordingsThatCouldNotBeDecoded()
    {
        string set1 = Folder("set1");
        File.WriteAllBytes(Path.Combine(set1, "tiny.dat"), new byte[16]);

        HtmlReportBuildResult result = Build(new[] { set1 });

        Assert.Equal(0, result.Pictures);
        Assert.Contains("None of the pictures and recordings here could be read.", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesPicturesOutWhenTheReportIsFull()
    {
        string set1 = Folder("set1");
        WritePicture(Path.Combine(set1, "shot.png"), 64, 64);

        HtmlReportBuildResult result = Build(new[] { set1 }, limits: new HtmlReportLimits { MaxTotalPictureBytes = 16 });

        Assert.Equal(0, result.Pictures);
        Assert.Equal(1, result.PicturesLeftOut);
        Assert.Contains("There was no room left in the report for the pictures of this folder.", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesOutAPictureTooBigToCarry()
    {
        string set1 = Folder("set1");
        WritePicture(Path.Combine(set1, "shot.png"), 32, 32);

        HtmlReportBuildResult result = Build(new[] { set1 }, limits: new HtmlReportLimits { MaxPictureBytes = 16 });

        Assert.Equal(0, result.Pictures);
        Assert.Contains("None of the pictures and recordings here could be read.", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void CarriesAPictureOfAnySizeWhenNoBoundIsAskedFor()
    {
        string set1 = Folder("set1");

        // Five of a megabyte, which the bounds the report used to carry would have turned away.
        File.WriteAllBytes(Path.Combine(set1, "huge.png"), new byte[5 * 1024 * 1024]);

        HtmlReportBuildResult result = Build(new[] { set1 });

        Assert.Equal(1, result.Pictures);
        Assert.Equal(0, result.PicturesLeftOut);
        Assert.Equal(5 * 1024 * 1024, Assert.Single(result.Files).Bytes.Length);
    }

    [Fact]
    public void WritesARowForTheFoldersOnTheWayWithWhatIsBelowThem()
    {
        string set1 = Folder("set1");

        HtmlReportBuildResult result = Build(new[] { set1 });

        // The temporary folder is under /tmp, so its parents are rows without a match of their own.
        Assert.Contains("<span class=\"count\">1 below</span>", result.Html, StringComparison.Ordinal);
        Assert.Contains("class=\"row parent\"", result.Html, StringComparison.Ordinal);
        Assert.Contains("<section class=\"folder\" id=\"f1\">", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysHowFarItHasGot()
    {
        string set1 = Folder("set1");
        string set2 = Folder("set2");
        var seen = new List<HtmlReportProgress>();

        Build(new[] { set1, set2 }, progress: new Progress<HtmlReportProgress>(seen.Add));

        Assert.Equal(2, seen.Count);
        Assert.Equal(new[] { 0, 1 }, seen.Select(step => step.FoldersDone).ToArray());
        Assert.All(seen, step => Assert.Equal(2, step.FoldersTotal));
        Assert.Contains("set2", seen[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StopsWhenItIsCancelled()
    {
        string set1 = Folder("set1");
        WritePicture(Path.Combine(set1, "shot.png"), 8, 8);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => Build(new[] { set1 }, cancellation: cancellation.Token));
    }

    private HtmlReportBuildResult Build(
        IReadOnlyList<string> folders,
        HtmlReportLimits? limits = null,
        IProgress<HtmlReportProgress>? progress = null,
        CancellationToken cancellation = default,
        string pictureFolder = HtmlReportBuilder.DefaultPictureFolder)
    {
        IReadOnlyList<ResultTreeNode> roots = ResultTree.Build(folders.Select(path => new FolderResult
        {
            FullPath = path,
            SizeBytes = 1024,
            TotalSizeBytes = 2048,
            DirectFileCount = 2,
            TotalFileCount = 5,
            SubfolderCount = 0,
        }));

        var builder = new HtmlReportBuilder(limits, new Random(1));

        return builder.Build(
            roots,
            path => FileSystemListing.EnumerateChildren(path),
            RawFileTypes.Default,
            new[] { new RawSchema { Name = "16 bit grayscale 8 x 4", Width = 8, Height = 4, DataType = RawDataType.U16 } },
            stretch: true,
            Written,
            new HtmlReportOptions { Title = "Folders found" },
            progress,
            cancellation,
            pictureFolder);
    }

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
            pixels[index] = (byte)(index * 7);
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

    private static void WriteRecording(string path, int width, int height, int frames)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);

        for (int frame = 0; frame < frames; frame++)
        {
            var bytes = new byte[width * height * 2];
            for (int index = 0; index < bytes.Length; index += 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index, 2), (ushort)(4000 + (index % 500)));
            }

            stream.Write(bytes);
        }
    }

    private static int Count(string text, string needle)
    {
        int count = 0;
        int index = text.IndexOf(needle, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + 1, StringComparison.Ordinal);
        }

        return count;
    }
}
