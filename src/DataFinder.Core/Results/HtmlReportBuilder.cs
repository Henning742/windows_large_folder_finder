using System.Text;
using DataFinder.Core.Models;
using DataFinder.Core.Preview.Raw;

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

/// <summary>
/// What came out of a run, so the window can say what it wrote: the page itself, the picture files
/// that go beside it, and how many folders and pictures it ended up with.
/// </summary>
public sealed record HtmlReportBuildResult(
    string Html,
    int Folders,
    int Pictures,
    int PicturesLeftOut,
    IReadOnlyList<HtmlReportFile> Files);

/// <summary>
/// Puts the report together: walks the folders that matched, finds a few pictures in each of them
/// with a <see cref="FolderPictureFinder"/>, and hands the lot to <see cref="HtmlReport"/>.
/// </summary>
public sealed class HtmlReportBuilder
{
    /// <summary>Where the pictures go when the caller does not say.</summary>
    public const string DefaultPictureFolder = "report.files";

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
        CancellationToken cancellationToken = default,
        string pictureFolder = DefaultPictureFolder)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(listFiles);
        reportOptions ??= new HtmlReportOptions();

        IReadOnlyList<ResultTreeNode> rows = ResultTree.All(roots);
        List<ResultTreeNode> matches = rows.Where(node => node.IsMatch).ToList();

        var folders = new List<HtmlReportFolder>(rows.Count);
        var finder = new FolderPictureFinder(_limits, _random);
        var files = new List<HtmlReportFile>();
        var names = new PictureNames(pictureFolder);
        int done = 0;

        // Every row is written out, because a folder that only leads to matches is still the way the
        // reader gets to them; only the folders that matched are walked for pictures.
        foreach (ResultTreeNode row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FolderResult? result = row.Result;
            var images = new List<HtmlReportPicture>();
            string? picturesNote = null;

            if (result is not null)
            {
                progress?.Report(new HtmlReportProgress(done, matches.Count, result.FullPath));

                FolderPictures found = finder.Find(
                    row.FullPath, listFiles, decodeSuffixes, schemas, stretch, cancellationToken);
                picturesNote = found.Note;

                // The picture becomes a file of its own beside the page; the page only names it.
                foreach (FolderPreviewPicture picture in found.Pictures)
                {
                    string source = names.Next(picture.Caption, picture.MimeType);
                    files.Add(new HtmlReportFile(source, picture.Bytes));
                    images.Add(new HtmlReportPicture(picture.Caption, source, picture.Note));
                }

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
            finder.Pictures,
            finder.LeftOut,
            files);
    }

    /// <summary>The suffix a picture file is written with, from the type the browser is told.</summary>
    private static string SuffixOf(string mimeType) => mimeType switch
    {
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/webp" => ".webp",
        "image/tiff" => ".tiff",
        "image/x-icon" => ".ico",
        "image/vnd.ms-photo" => ".wdp",
        _ => ".png",
    };

    /// <summary>
    /// Hands out the names the pictures are written under. The number in front keeps two pictures of
    /// the same name apart, so one folder cannot overwrite what another folder put there.
    /// </summary>
    private sealed class PictureNames
    {
        private readonly string _folder;
        private int _count;

        public PictureNames(string folder) => _folder = SafeFolder(folder);

        public string Next(string caption, string mimeType)
        {
            _count++;
            return $"{_folder}/{_count:D4}-{SafeName(caption)}{SuffixOf(mimeType)}";
        }
    }

    /// <summary>
    /// A folder name a page can link to without the browser taking part of it for a query or a
    /// fragment. Letters from any language are left alone - only what breaks a path or a URL is not.
    /// </summary>
    private static string SafeFolder(string folder)
    {
        string trimmed = (folder ?? string.Empty).Trim().Trim('/', '\\', '.');
        return trimmed.Length == 0 ? DefaultPictureFolder : MakeSafe(trimmed);
    }

    private static string SafeName(string caption)
    {
        // The suffix comes from the type the browser is told, so a name that already carries one is
        // not left with two of them.
        string name = MakeSafe(Path.GetFileNameWithoutExtension(caption ?? string.Empty)).Trim('_', ' ');
        if (name.Length == 0)
        {
            name = "picture";
        }

        return name.Length <= 40 ? name : name[..40];
    }

    /// <summary>Replaces the characters that would turn a file name into something else once linked to.</summary>
    private static string MakeSafe(string text)
    {
        var safe = new StringBuilder(text.Length);

        foreach (char character in text)
        {
            safe.Append(char.IsControl(character) || UnsafeNameCharacters.Contains(character) ? '_' : character);
        }

        return safe.ToString();
    }

    private static readonly HashSet<char> UnsafeNameCharacters = new()
    {
        '<', '>', ':', '"', '/', '\\', '|', '?', '*', '#', '%',
    };

}
