using DataFinder.Core.Util;

namespace DataFinder.Core.Preview.Raw;

/// <summary>What one read produced: the bytes of a frame, or the reason they were not there.</summary>
public sealed record RawReadResult(byte[]? Bytes, string? Error)
{
    public bool Succeeded => Bytes is not null;

    public static RawReadResult Success(byte[] bytes) => new(bytes, null);

    public static RawReadResult Failure(string error) => new(null, error);
}

/// <summary>
/// Reads the bytes of a single frame out of a file. Only the part of the file the frame sits in is
/// touched, so looking at a huge recording costs no more than looking at a small one.
/// </summary>
public static class RawFrameReader
{
    /// <summary>A frame bigger than this is taken as a mistake in the schematic.</summary>
    public const long MaxFrameBytes = 256L * 1024 * 1024;

    public static RawReadResult Read(string path, RawSchema schema, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        cancellationToken.ThrowIfCancellationRequested();

        if (schema.Validate() is { } problem)
        {
            return RawReadResult.Failure(problem);
        }

        long frameBytes = schema.FrameBytes;
        if (frameBytes > MaxFrameBytes)
        {
            return RawReadResult.Failure(
                $"'{schema.Name}' wants {ByteSize.Format(frameBytes)} per frame, which is more than a preview should read.");
        }

        long offset = ((long)schema.FrameIndex * frameBytes) + schema.HeaderLength;
        int payloadBytes = schema.PayloadBytes;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                64 * 1024,
                FileOptions.RandomAccess);

            long length = stream.Length;
            if (length < offset + payloadBytes)
            {
                return RawReadResult.Failure(schema.FrameIndex == 0
                    ? $"'{schema.Name}' needs {ByteSize.Format(offset + payloadBytes)} of this file, but it is only {ByteSize.Format(length)}."
                    : $"'{schema.Name}' looks for frame {schema.FrameIndex:N0} at byte {offset:N0}, but the file is only {ByteSize.Format(length)}.");
            }

            stream.Seek(offset, SeekOrigin.Begin);

            var buffer = new byte[payloadBytes];
            stream.ReadExactly(buffer, 0, payloadBytes);
            return RawReadResult.Success(buffer);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RawReadResult.Failure(exception.Message);
        }
    }

    /// <summary>
    /// Reads one frame and decodes it in a single step. <paramref name="stretch"/> is the choice the
    /// preview pane offers: see <see cref="RawImageDecoder.Decode"/>.
    /// </summary>
    public static RawDecodeResult Decode(string path, RawSchema schema, bool stretch, CancellationToken cancellationToken = default)
    {
        RawReadResult read = Read(path, schema, cancellationToken);
        return read.Bytes is null
            ? RawDecodeResult.Failure(read.Error ?? "The frame could not be read.")
            : RawImageDecoder.Decode(read.Bytes, schema, stretch);
    }
}
