using DataFinder.Core.Ntfs;

namespace DataFinder.Core.Tests.Support;

/// <summary>
/// A volume that lives in memory, so the raw reading can be tested without a drive - including the
/// awkward parts: a stretch that will not read at all, and a read that only works on the next try.
/// </summary>
internal sealed class FakeRawVolume : IRawVolumeReader
{
    private readonly List<(long Start, long End)> _failures = new();

    public FakeRawVolume(long sizeBytes)
    {
        Bytes = new byte[sizeBytes];
        BytesPerSector = 512;
        VolumeSizeBytes = sizeBytes;
    }

    public byte[] Bytes { get; }

    public int BytesPerSector { get; private set; }

    public long VolumeSizeBytes { get; private set; }

    /// <summary>How many reads were asked for, retries included.</summary>
    public int Reads { get; private set; }

    /// <summary>How many reads were turned away by a stretch marked with <see cref="Fail"/>.</summary>
    public int FailedReads { get; private set; }

    public void Configure(int bytesPerSector, long volumeSizeBytes)
    {
        if (bytesPerSector > 0)
        {
            BytesPerSector = bytesPerSector;
        }

        if (volumeSizeBytes > 0)
        {
            VolumeSizeBytes = volumeSizeBytes;
        }
    }

    /// <summary>Makes a stretch of the volume fail to read, the way a bad sector or a dropped transfer does.</summary>
    public void Fail(long offset, long length) => _failures.Add((offset, offset + length));

    public void Write(long offset, ReadOnlySpan<byte> bytes) => bytes.CopyTo(Bytes.AsSpan((int)offset));

    public int ReadAligned(long offset, byte[] buffer, int index, int count)
    {
        Reads++;

        if (count <= 0 || offset < 0)
        {
            return 0;
        }

        // The same widening to sector boundaries the real reader does, so a test cannot pass by
        // reading in a way Windows would refuse.
        int alignment = BytesPerSector > 0 ? BytesPerSector : 512;
        long alignedOffset = offset - (offset % alignment);
        long end = alignedOffset + (offset - alignedOffset) + count;
        long limit = Math.Min(VolumeSizeBytes > 0 ? VolumeSizeBytes : Bytes.Length, Bytes.Length);
        end = Math.Min(end, limit);

        if (alignedOffset >= limit || end <= offset)
        {
            return 0;
        }

        if (IsFailing(alignedOffset, end))
        {
            FailedReads++;
            return 0;
        }

        int usable = (int)Math.Min(count, end - offset);
        if (usable <= 0)
        {
            return 0;
        }

        Bytes.AsSpan((int)offset, usable).CopyTo(buffer.AsSpan(index));
        return usable;
    }

    public void Dispose()
    {
    }

    private bool IsFailing(long start, long end)
    {
        foreach ((long failureStart, long failureEnd) in _failures)
        {
            if (start < failureEnd && end > failureStart)
            {
                return true;
            }
        }

        return false;
    }
}
