namespace DataFinder.Core.Ntfs;

/// <summary>
/// The little of a volume the master file table reader needs: aligned bytes at a byte offset. It is
/// an interface so that the reading can be tested without a real drive.
/// </summary>
public interface IRawVolumeReader : IDisposable
{
    /// <summary>Sector size used to keep every read aligned.</summary>
    int BytesPerSector { get; }

    /// <summary>Size of the volume in bytes, or zero when it is not known.</summary>
    long VolumeSizeBytes { get; }

    /// <summary>Takes the geometry the boot sector turned out to describe.</summary>
    void Configure(int bytesPerSector, long volumeSizeBytes);

    /// <summary>
    /// Reads <paramref name="count"/> bytes at <paramref name="offset"/>, widening the request to
    /// sector boundaries. Returns the number of bytes actually copied into the buffer.
    /// </summary>
    int ReadAligned(long offset, byte[] buffer, int index, int count);
}

/// <summary>What every volume reader can be asked on top of <see cref="IRawVolumeReader"/>.</summary>
public static class RawVolumeReaderExtensions
{
    /// <summary>Reads enough bytes to cover a boot sector, always aligned to the device sector size.</summary>
    public static byte[] ReadBootSectorProbe(this IRawVolumeReader volume)
    {
        var buffer = new byte[NtfsBootSector.ProbeSize];
        int read = volume.ReadAligned(0, buffer, 0, buffer.Length);

        if (read < buffer.Length)
        {
            Array.Resize(ref buffer, Math.Max(read, 0));
        }

        return buffer;
    }
}
