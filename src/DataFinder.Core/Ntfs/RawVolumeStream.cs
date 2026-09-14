namespace DataFinder.Core.Ntfs;

/// <summary>
/// Raw, sector aligned reads from a volume device (for example <c>\\.\D:</c>).
/// Opening a volume this way requires administrator rights.
/// </summary>
public sealed class RawVolumeStream : IDisposable
{
    private readonly FileStream _stream;
    private readonly object _gate = new();
    private byte[] _scratch = new byte[64 * 1024];
    private long _volumeSizeBytes;

    private RawVolumeStream(FileStream stream, char driveLetter)
    {
        _stream = stream;
        DriveLetter = driveLetter;
        BytesPerSector = 512;
    }

    public char DriveLetter { get; }

    /// <summary>Sector size used to keep every read aligned. Updated once the boot sector has been parsed.</summary>
    public int BytesPerSector { get; private set; }

    public long VolumeSizeBytes => _volumeSizeBytes;

    public static RawVolumeStream Open(char driveLetter)
    {
        char letter = char.ToUpperInvariant(driveLetter);
        string devicePath = $@"\\.\{letter}:";

        FileStream stream;
        try
        {
            stream = new FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.None);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new NtfsScanException(
                $"Windows denied access to {letter}:. Reading the NTFS master file table needs administrator rights - restart the app elevated and try again.",
                exception);
        }
        catch (IOException exception)
        {
            throw new NtfsScanException($"The volume {letter}: could not be opened for raw reading. {exception.Message}", exception);
        }

        var volume = new RawVolumeStream(stream, letter);

        try
        {
            volume._volumeSizeBytes = stream.Length;
        }
        catch (IOException)
        {
            volume._volumeSizeBytes = 0;
        }
        catch (NotSupportedException)
        {
            volume._volumeSizeBytes = 0;
        }

        return volume;
    }

    /// <summary>Applies the geometry found in the boot sector so that later reads stay aligned.</summary>
    public void Configure(int bytesPerSector, long volumeSizeBytes)
    {
        if (bytesPerSector > 0)
        {
            BytesPerSector = bytesPerSector;
        }

        if (volumeSizeBytes > 0)
        {
            _volumeSizeBytes = volumeSizeBytes;
        }
    }

    /// <summary>Reads enough bytes to cover the boot sector, always aligned to the device sector size.</summary>
    public byte[] ReadBootSectorProbe()
    {
        var buffer = new byte[NtfsBootSector.ProbeSize];
        int read = ReadAligned(0, buffer, 0, buffer.Length);
        if (read < buffer.Length)
        {
            Array.Resize(ref buffer, Math.Max(read, 0));
        }

        return buffer;
    }

    /// <summary>
    /// Reads <paramref name="count"/> bytes at <paramref name="offset"/>, widening the request to
    /// sector boundaries because Windows only lets a volume be read in whole sectors.
    /// Returns the number of bytes actually copied into <paramref name="buffer"/>.
    /// </summary>
    public int ReadAligned(long offset, byte[] buffer, int index, int count)
    {
        if (count <= 0 || offset < 0)
        {
            return 0;
        }

        int alignment = BytesPerSector > 0 ? BytesPerSector : 512;
        long alignedOffset = offset - (offset % alignment);
        int lead = (int)(offset - alignedOffset);
        int alignedCount = RoundUp(lead + count, alignment);

        lock (_gate)
        {
            if (_volumeSizeBytes > 0)
            {
                if (alignedOffset >= _volumeSizeBytes)
                {
                    return 0;
                }

                long available = _volumeSizeBytes - alignedOffset;
                alignedCount = (int)Math.Min(alignedCount, available);
            }

            EnsureScratch(alignedCount);
            int read = ReadFully(alignedOffset, _scratch, 0, alignedCount);
            if (read <= lead)
            {
                return 0;
            }

            int usable = Math.Min(count, read - lead);
            Buffer.BlockCopy(_scratch, lead, buffer, index, usable);
            return usable;
        }
    }

    public void Dispose() => _stream.Dispose();

    private static int RoundUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private void EnsureScratch(int size)
    {
        if (_scratch.Length < size)
        {
            _scratch = new byte[Math.Max(size, _scratch.Length * 2)];
        }
    }

    private int ReadFully(long offset, byte[] buffer, int index, int count)
    {
        _stream.Seek(offset, SeekOrigin.Begin);
        int total = 0;

        while (total < count)
        {
            int read;
            try
            {
                read = _stream.Read(buffer, index + total, count - total);
            }
            catch (IOException)
            {
                break;
            }

            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}

