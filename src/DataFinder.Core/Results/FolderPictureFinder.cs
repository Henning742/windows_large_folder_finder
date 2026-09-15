using DataFinder.Core.Models;
using DataFinder.Core.Preview;
using DataFinder.Core.Preview.Raw;
using DataFinder.Core.Util;

namespace DataFinder.Core.Results;

/// <summary>
/// One picture taken from a folder: what the file is called, the type its bytes are in, the bytes
/// themselves, and a line about how they were come by. What happens to them is the caller's: the
/// web page writes them as files beside itself, and the window draws them.
/// </summary>
public sealed record FolderPreviewPicture(string Caption, string MimeType, byte[] Bytes, string Note);

/// <summary>What one folder had to show: a few pictures, and a line about what was left out.</summary>
public sealed record FolderPictures(IReadOnlyList<FolderPreviewPicture> Pictures, string? Note);

/// <summary>
/// Looks inside folders for something worth showing: a few pictures, picked at random from the
/// pictures and the recordings that sit directly inside each folder. The web page and the window
/// both work from this, so a folder reads the same way either way.
///
/// One finder looks after the room set aside for pictures as a whole, so a run over hundreds of
/// folders ends and the pictures stay in hand.
/// </summary>
public sealed class FolderPictureFinder
{
    private readonly HtmlReportLimits _limits;
    private readonly Random _random;
    private readonly PictureBudget _budget;

    public FolderPictureFinder(HtmlReportLimits? limits = null, Random? random = null)
    {
        _limits = limits ?? new HtmlReportLimits();
        _random = random ?? Random.Shared;
        _budget = new PictureBudget(_limits.MaxTotalPictureBytes);
    }

    /// <summary>How many pictures have been taken on since this finder was made.</summary>
    public int Pictures => _budget.Count;

    /// <summary>How many pictures were turned away because there was no room left for them.</summary>
    public int LeftOut => _budget.LeftOut;

    /// <summary>
    /// A few pictures of what is inside one folder, picked at random from what is there: the
    /// pictures themselves, and the recordings the decoder can turn into pictures.
    /// </summary>
    public FolderPictures Find(
        string folderPath,
        Func<string, IReadOnlyList<FileEntry>> listFiles,
        IReadOnlyList<string> decodeSuffixes,
        IReadOnlyList<RawSchema> schemas,
        bool stretch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listFiles);

        IReadOnlyList<FileEntry> files;
        try
        {
            files = listFiles(folderPath) ?? Array.Empty<FileEntry>();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return new FolderPictures(Array.Empty<FolderPreviewPicture>(), $"The folder could not be read: {exception.Message}");
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
            return new FolderPictures(
                Array.Empty<FolderPreviewPicture>(),
                "There are no files directly inside this folder.");
        }

        if (candidates.Count == 0)
        {
            return new FolderPictures(
                Array.Empty<FolderPreviewPicture>(),
                sawRecording
                    ? "None of the files here could be shown as a picture."
                    : $"Nothing among the {examined:N0} files looked at here could be shown as a picture.");
        }

        Shuffle(candidates);

        var images = new List<FolderPreviewPicture>();
        bool ranOutOfRoom = false;
        int refused = 0;

        foreach (FileEntry candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (images.Count >= _limits.PicturesPerFolder)
            {
                break;
            }

            if (!_budget.HasRoomFor(_limits.MaxPictureBytes))
            {
                ranOutOfRoom = true;
                break;
            }

            FolderPreviewPicture? image = MakePicture(candidate, schemas, stretch);
            if (image is null)
            {
                refused++;
                continue;
            }

            _budget.Add(image.Bytes.Length);
            images.Add(image);
        }

        if (images.Count == 0)
        {
            return new FolderPictures(
                Array.Empty<FolderPreviewPicture>(),
                ranOutOfRoom
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

        return new FolderPictures(images, notes.Count == 0 ? null : string.Join(' ', notes));
    }

    /// <summary>
    /// One picture: a picture file is carried as it is, and a recording is decoded with the first
    /// schematic that manages it - the ticked ones, in the order the dialog lists them.
    /// </summary>
    private FolderPreviewPicture? MakePicture(
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
                : new FolderPreviewPicture(file.Name, MimeTypeOf(file.Name), bytes, $"{ByteSize.Format(file.SizeBytes)} - shown as it is");
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

            return new FolderPreviewPicture(
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

    /// <summary>The type the bytes of a picture file of this name are in.</summary>
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

    /// <summary>Keeps an eye on how much picture has been taken on so far.</summary>
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
