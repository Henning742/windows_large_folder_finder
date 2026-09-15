using DataFinder.Core.Models;
using DataFinder.Core.Preview;
using DataFinder.Core.Preview.Raw;
using DataFinder.Core.Util;

namespace DataFinder.Core.Results;

/// <summary>The most of anything a report will carry, so a run over a huge result list ends.</summary>
public sealed class HtmlReportLimits
{
    /// <summary>How many pictures each folder shows.</summary>
    public int PicturesPerFolder { get; init; } = 6;

    /// <summary>How many pictures are looked for in a folder before the ones to show are picked.</summary>
    public int CandidatesPerFolder { get; init; } = 40;

    /// <summary>How far into a folder the search goes before giving up on it.</summary>
    public int FilesExaminedPerFolder { get; init; } = 4000;

    /// <summary>A picture bigger than this is left out rather than carried in the report.</summary>
    public long MaxPictureBytes { get; init; } = 4L * 1024 * 1024;

    /// <summary>How much picture the whole report may carry.</summary>
    public long MaxTotalPictureBytes { get; init; } = 48L * 1024 * 1024;
}

/// <summary>How far the report has got, for a progress bar that has something to say.</summary>
public sealed record HtmlReportProgress(int FoldersDone, int FoldersTotal, string Message);

/// <summary>What came out of a run, so the window can say what it wrote.</summary>
public sealed record HtmlReportBuildResult(string Html, int Folders, int Pictures, int PicturesLeftOut);

/// <summary>
/// Puts the report together: walks the folders that matched, finds a few pictures in each of them,
/// decodes the recordings among them, and hands the lot to <see cref="HtmlReport"/>.
/// </summary>
public sealed class HtmlReportBuilder
{
    private readonly HtmlReportLimits _limits;
    private readonly Random _random;

    public HtmlReportBuilder(HtmlReportLimits? limits = null, Random? random = null)
    {
        _limits = limits ?? new HtmlReportLimits();
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// Builds the page. <paramref name="listFiles"/> says what sits inside a folder - the scan that
    /// is still in memory answers that without touching the disk, and the file system answers for
    /// imported folders.
    /// </summary>
    public HtmlReportBuildResult Build(
        IReadOnlyList<ResultTreeNode> roots,
        Func<string, IReadOnlyList<FileEntry>> listFiles,
        IReadOnlyList<string> decodeSuffixes,
        IReadOnlyList<RawSchema> schemas,
        bool stretch,
        DateTimeOffset generatedAt,
        HtmlReportOptions reportOptions,
        IProgress<HtmlReportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(listFiles);
        reportOptions ??= new HtmlReportOptions();

        IReadOnlyList<ResultTreeNode> rows = ResultTree.All(roots);
        List<ResultTreeNode> matches = rows.Where(node => node.IsMatch).ToList();

        var folders = new List<HtmlReportFolder>(rows.Count);
        var pictures = new PictureBudget(_limits.MaxTotalPictureBytes);
        int done = 0;

        // Every row is written out, because a folder that only leads to matches is still the way the
        // reader gets to them; only the folders that matched are walked for pictures.
        foreach (ResultTreeNode row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FolderResult? result = row.Result;
            IReadOnlyList<HtmlReportImage> images = Array.Empty<HtmlReportImage>();
            string? picturesNote = null;

            if (result is not null)
            {
                progress?.Report(new HtmlReportProgress(done, matches.Count, result.FullPath));
                (images, picturesNote) = Pictures(row.FullPath, listFiles, decodeSuffixes, schemas, stretch, pictures, cancellationToken);
                done++;
            }

            folders.Add(new HtmlReportFolder
            {
                FullPath = row.FullPath,
                Name = row.Name,
                Depth = row.Depth,
                IsMatch = row.IsMatch,
                SizeBytes = row.ShownSizeBytes,
                TotalSizeBytes = result?.TotalSizeBytes ?? 0,
                DirectFileCount = result?.DirectFileCount ?? 0,
                TotalFileCount = result?.TotalFileCount ?? 0,
                SubfolderCount = result?.SubfolderCount ?? 0,
                MatchesBelow = row.MatchesBelow,
                Comment = result?.Comment ?? string.Empty,
                Exists = result?.Exists ?? true,
                Images = images,
                PicturesNote = picturesNote,
            });
        }

        return new HtmlReportBuildResult(
            HtmlReport.Build(generatedAt, folders, reportOptions),
            matches.Count,
            pictures.Count,
            pictures.LeftOut);
    }

    /// <summary>
    /// A few pictures of what is inside one folder, picked at random from what is there: the
    /// pictures themselves, and the recordings the decoder can turn into pictures.
    /// </summary>
    private (IReadOnlyList<HtmlReportImage> Images, string? Note) Pictures(
        string folderPath,
        Func<string, IReadOnlyList<FileEntry>> listFiles,
        IReadOnlyList<string> decodeSuffixes,
        IReadOnlyList<RawSchema> schemas,
        bool stretch,
        PictureBudget budget,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FileEntry> files;
        try
        {
            files = listFiles(folderPath) ?? Array.Empty<FileEntry>();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return (Array.Empty<HtmlReportImage>(), $"The folder could not be read: {exception.Message}");
        }

        var candidates = new List<FileEntry>();
        int examined = 0;
        bool sawRecording = false;

        foreach (FileEntry file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.IsDirectory)
            {
                continue;
            }

            // Enough to choose from, or as far into the folder as this report is willing to go.
            if (examined >= _limits.FilesExaminedPerFolder || candidates.Count >= _limits.CandidatesPerFolder)
            {
                break;
            }

            examined++;

            if (PreviewClassifier.IsImageExtension(file.Name))
            {
                candidates.Add(file);
                continue;
            }

            if (RawFileTypes.Matches(decodeSuffixes, file.Name))
            {
                sawRecording = true;
                candidates.Add(file);
            }
        }

        if (examined == 0)
        {
            return (Array.Empty<HtmlReportImage>(), "There are no files directly inside this folder.");
        }

        if (candidates.Count == 0)
        {
            return (Array.Empty<HtmlReportImage>(), sawRecording
                ? "None of the files here could be shown as a picture."
                : $"Nothing among the {examined:N0} files looked at here could be shown as a picture.");
        }

        Shuffle(candidates);

        var images = new List<HtmlReportImage>();
        bool ranOutOfRoom = false;
        int refused = 0;

        foreach (FileEntry candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (images.Count >= _limits.PicturesPerFolder)
            {
                break;
            }

            if (!budget.HasRoomFor(_limits.MaxPictureBytes))
            {
                ranOutOfRoom = true;
                break;
            }

            HtmlReportImage? image = MakePicture(candidate, schemas, stretch);
            if (image is null)
            {
                refused++;
                continue;
            }

            budget.Add(image.Bytes.Length);
            images.Add(image);
        }

        if (images.Count == 0)
        {
            return (Array.Empty<HtmlReportImage>(), ranOutOfRoom
                ? "There was no room left in the report for the pictures of this folder."
                : "None of the pictures and recordings here could be read.");
        }

        var notes = new List<string>();

        if (candidates.Count > images.Count)
        {
            notes.Add($"Showing {images.Count} of the {candidates.Count} pictures and recordings here, picked at random.");
        }

        if (ranOutOfRoom)
        {
            notes.Add("The report was full, so the rest were left out.");
        }

        if (refused > 0)
        {
            notes.Add(refused == 1 ? "1 could not be read." : $"{refused} could not be read.");
        }

        return (images, notes.Count == 0 ? null : string.Join(' ', notes));
    }

    /// <summary>
    /// One picture: a picture file is carried as it is, and a recording is decoded with the first
    /// schematic that manages it - the ticked ones, in the order the dialog lists them.
    /// </summary>
    private HtmlReportImage? MakePicture(
        FileEntry file,
        IReadOnlyList<RawSchema> schemas,
        bool stretch)
    {
        if (file.SizeBytes > _limits.MaxPictureBytes)
        {
            return null;
        }

        if (PreviewClassifier.IsImageExtension(file.Name))
        {
            byte[]? bytes = ReadAllBytes(file.FullPath, _limits.MaxPictureBytes);
            return bytes is null
                ? null
                : new HtmlReportImage(file.Name, MimeTypeOf(file.Name), bytes, $"{ByteSize.Format(file.SizeBytes)} - shown as it is");
        }

        foreach (RawSchema schema in schemas)
        {
            RawDecodeResult decoded = RawFrameReader.Decode(file.FullPath, schema, stretch);
            if (decoded.Frame is not { } frame)
            {
                continue;
            }

            byte[] png;
            try
            {
                png = RawPngWriter.Encode(frame);
            }
            catch (Exception exception) when (exception is ArgumentException or OutOfMemoryException)
            {
                return null;
            }

            return new HtmlReportImage(
                file.Name,
                "image/png",
                png,
                $"{ByteSize.Format(file.SizeBytes)} - decoded with {schema.Name}, {frame.Width} x {frame.Height}");
        }

        return null;
    }

    private static byte[]? ReadAllBytes(string path, long maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > maxBytes)
            {
                return null;
            }

            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string MimeTypeOf(string pathOrName) => PreviewClassifier.ExtensionOf(pathOrName) switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" or ".jpe" or ".jfif" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" or ".dib" => "image/bmp",
        ".webp" => "image/webp",
        ".tif" or ".tiff" => "image/tiff",
        ".ico" => "image/x-icon",
        ".wdp" or ".jxr" => "image/vnd.ms-photo",
        _ => "image/png",
    };

    private void Shuffle(List<FileEntry> files)
    {
        for (int index = files.Count - 1; index > 0; index--)
        {
            int other = _random.Next(index + 1);
            (files[index], files[other]) = (files[other], files[index]);
        }
    }

    /// <summary>Keeps an eye on how much picture the report has taken on so far.</summary>
    private sealed class PictureBudget
    {
        private readonly long _total;
        private long _used;

        public PictureBudget(long total) => _total = total;

        public int Count { get; private set; }

        public int LeftOut { get; private set; }

        public bool HasRoomFor(long bytes)
        {
            if (_used + bytes <= _total)
            {
                return true;
            }

            LeftOut++;
            return false;
        }

        public void Add(long bytes)
        {
            _used += bytes;
            Count++;
        }
    }
}
