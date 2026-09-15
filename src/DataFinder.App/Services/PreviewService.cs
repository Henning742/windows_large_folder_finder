using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DataFinder.Core.Models;
using DataFinder.Core.Preview;
using DataFinder.Core.Preview.Raw;
using DataFinder.Core.Util;

namespace DataFinder.App.Services;

public sealed record PreviewResult(ImageSource? Image, string? Text, string? Message);

/// <summary>
/// One picture the decoder produced: which schematic drew it, at what size, and what it has to say
/// about itself. A tile without a picture carries the reason instead, so "show them all at once"
/// never hides a schematic that did not work.
/// </summary>
public sealed record DecodeTile(string Caption, string Detail, ImageSource? Image, string? Message)
{
    public bool HasImage => Image is not null;

    public bool HasMessage => !string.IsNullOrEmpty(Message);
}

/// <summary>Loads an image thumbnail or a text snippet for the preview pane.</summary>
public sealed class PreviewService
{
    private const long MaxImageBytes = 256L * 1024 * 1024;
    private const int MaxTextBytes = 512 * 1024;
    private const int PreviewPixelWidth = 1600;

    /// <summary>
    /// Reads one data file through every schematic it is given, in the order they were listed. It
    /// runs off the UI thread: a decode sorts a frame of 16 bit values, which takes a moment.
    /// </summary>
    public Task<IReadOnlyList<DecodeTile>> DecodeAsync(
        string path,
        IReadOnlyList<RawSchema> schemas,
        CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<DecodeTile>>(
            () =>
            {
                var tiles = new List<DecodeTile>(schemas.Count);
                foreach (RawSchema schema in schemas)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    tiles.Add(DecodeOne(path, schema));
                }

                return tiles;
            },
            cancellationToken);

    private static DecodeTile DecodeOne(string path, RawSchema schema)
    {
        RawDecodeResult result = RawFrameReader.Decode(path, schema);
        if (result.Frame is not { } frame)
        {
            return new DecodeTile(schema.Name, schema.Description, null, result.Error ?? "The frame could not be read.");
        }

        return new DecodeTile(
            schema.Name,
            $"{frame.Width} x {frame.Height} - {schema.Description}",
            ToImage(frame),
            result.Warning);
    }

    /// <summary>Turns decoded pixels into something an Image control can draw.</summary>
    private static ImageSource ToImage(RawFrame frame)
    {
        PixelFormat format = frame.Format == RawPixelFormat.Gray8 ? PixelFormats.Gray8 : PixelFormats.Bgra32;
        var bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96d,
            96d,
            format,
            null,
            frame.Pixels,
            frame.Stride);

        bitmap.Freeze();
        return bitmap;
    }

    public async Task<PreviewResult> LoadAsync(FileEntry entry, CancellationToken cancellationToken)
    {
        if (entry.IsDirectory)
        {
            return new PreviewResult(null, null, $"Folder - {entry.SizeText}, {entry.DirectChildCount:N0} items directly inside.");
        }

        FileInfo info = new(entry.FullPath);
        if (!info.Exists)
        {
            return new PreviewResult(null, null, "This file no longer exists.");
        }

        switch (entry.PreviewKind)
        {
            case PreviewKind.Image:
            {
                if (info.Length > MaxImageBytes)
                {
                    return new PreviewResult(null, null, $"The image is too large to preview ({ByteSize.Format(info.Length)}).");
                }

                byte[] bytes = await File.ReadAllBytesAsync(entry.FullPath, cancellationToken);
                ImageSource? image = await Task.Run(() => TryLoadImage(bytes), cancellationToken);

                return image is not null
                    ? new PreviewResult(image, null, null)
                    : new PreviewResult(null, null, "This image could not be decoded. Windows Photo Viewer formats (png, jpg, gif, bmp, tif) work best.");
            }

            case PreviewKind.Text:
            {
                byte[] bytes = await ReadPrefixAsync(entry.FullPath, MaxTextBytes, cancellationToken);
                return new PreviewResult(null, DecodeText(bytes), null);
            }

            default:
            {
                string size = ByteSize.Format(info.Length);
                return new PreviewResult(null, null, $"No preview for this file type. Size: {size}.");
            }
        }
    }

    private static ImageSource? TryLoadImage(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;

            // Only ask for a smaller decode when the picture is actually bigger than the pane;
            // decoding a small picture "up" to the preview width would make it look soft.
            if (ReadPixelWidth(bytes) > PreviewPixelWidth)
            {
                bitmap.DecodePixelWidth = PreviewPixelWidth;
            }

            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Reads the pixel width from the image header without decoding the pixels.</summary>
    private static int ReadPixelWidth(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return frame.PixelWidth;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static async Task<byte[]> ReadPrefixAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        int length = (int)Math.Min(stream.Length, maxBytes);
        var buffer = new byte[length];
        int read = 0;

        while (read < length)
        {
            int got = await stream.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken);
            if (got <= 0)
            {
                break;
            }

            read += got;
        }

        return read == length ? buffer : buffer[..read];
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "(This file is empty.)";
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        int zeroBytes = 0;
        foreach (byte value in bytes)
        {
            if (value == 0)
            {
                zeroBytes++;
            }
        }

        if (zeroBytes * 20 > bytes.Length)
        {
            return "(This looks like a binary file, so there is no text preview.)";
        }

        int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
    }
}
