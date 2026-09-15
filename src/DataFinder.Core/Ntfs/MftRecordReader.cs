namespace DataFinder.Core.Ntfs;

/// <summary>
/// How a record read went. A scan needs these told apart: running off the end of the table is one
/// thing, a hole in the run list another, and a volume that will not hand over bytes a third.
/// </summary>
public enum MftRecordRead
{
    /// <summary>The record was copied into the destination.</summary>
    Success,

    /// <summary>The record number lies past the end of the table.</summary>
    BeyondTable,

    /// <summary>No data run of the table covers this record.</summary>
    NotCovered,

    /// <summary>A run covers the record, but the volume would not give up the bytes.</summary>
    ReadFailed,
}

/// <summary>
/// Streams master file table records in order. The records live in the $MFT $DATA stream, which
/// is usually fragmented, so every record is translated from a virtual cluster number to a
/// physical byte offset through the data runs of $MFT itself.
/// </summary>
public sealed class MftRecordReader : IDisposable, IMftRecordSource
{
    /// <summary>
    /// How often one read is tried. A volume that is busy, or a drive behind a tired USB cable,
    /// often gives the bytes on the next attempt - and treating that as the end of the table would
    /// drop the rest of the scan.
    /// </summary>
    private const int ReadAttempts = 3;

    /// <summary>
    /// How far the search for a readable record jumps before it starts closing in on the edge of the
    /// damage, in records. Jumps grow, so a long unreadable stretch costs a handful of reads.
    /// </summary>
    private const long MaxProbeStep = 4096;

    private readonly IRawVolumeReader _volume;
    private readonly int _bytesPerCluster;
    private readonly int _recordSize;
    private readonly IReadOnlyList<DataRun> _runs;
    private readonly long _mftDataSize;
    private readonly byte[] _buffer;
    private readonly byte[] _probe;

    private long _bufferStart;
    private int _validBytes;
    private int _lastRunIndex;

    public MftRecordReader(
        IRawVolumeReader volume,
        int bytesPerCluster,
        int recordSize,
        IReadOnlyList<DataRun> runs,
        long mftDataSize)
    {
        _volume = volume;
        _bytesPerCluster = bytesPerCluster;
        _recordSize = recordSize;
        _runs = runs;
        _mftDataSize = mftDataSize;
        _lastRunIndex = -1;

        int wanted = Math.Max(1 << 20, bytesPerCluster * 32);
        int alignment = Math.Max(bytesPerCluster, 4096);
        _buffer = new byte[(wanted + alignment - 1) / alignment * alignment];
        _probe = new byte[recordSize];
    }

    public long RecordCount => _recordSize > 0 ? _mftDataSize / _recordSize : 0;

    /// <summary>Copy of one record, used when following an <c>$ATTRIBUTE_LIST</c> into another record.</summary>
    public bool TryReadRecord(uint recordNumber, byte[] destination) => TryGetRecord(recordNumber, destination);

    /// <summary>Copies one record into <paramref name="destination"/>. Returns false at the end of the table.</summary>
    public bool TryGetRecord(long recordNumber, byte[] destination) =>
        ReadRecord(recordNumber, destination) == MftRecordRead.Success;

    /// <summary>
    /// Copies one record into <paramref name="destination"/>, saying what went wrong when it could
    /// not be read. The same destination is reused for every record, so it has to be consumed before
    /// the next one is asked for.
    /// </summary>
    public MftRecordRead ReadRecord(long recordNumber, byte[] destination)
    {
        if (destination.Length < _recordSize)
        {
            throw new ArgumentException(
                "The destination has to hold one whole master file table record.", nameof(destination));
        }

        if (recordNumber < 0)
        {
            return MftRecordRead.BeyondTable;
        }

        long offset = recordNumber * _recordSize;
        if (offset < 0 || offset + _recordSize > _mftDataSize)
        {
            return MftRecordRead.BeyondTable;
        }

        MftRecordRead filled = EnsureAvailable(offset, _recordSize);
        if (filled != MftRecordRead.Success)
        {
            return filled;
        }

        int delta = (int)(offset - _bufferStart);
        Buffer.BlockCopy(_buffer, delta, destination, 0, _recordSize);
        return MftRecordRead.Success;
    }

    /// <summary>
    /// The first record at or after <paramref name="recordNumber"/> that a data run covers, or -1
    /// when the runs hold nothing more. A hole in the run list is not the end of the table, so a
    /// scan can use this to carry on past damage instead of giving up on the rest of the volume.
    /// </summary>
    public long FindNextCoveredRecord(long recordNumber)
    {
        if (recordNumber < 0)
        {
            recordNumber = 0;
        }

        long offset = recordNumber * _recordSize;
        if (offset < 0 || offset + _recordSize > _mftDataSize)
        {
            return -1;
        }

        // The runs are in virtual cluster order, so the first one that ends after this offset is the
        // next place there is anything to read.
        foreach (DataRun run in _runs)
        {
            long runEnd = run.EndVcn * _bytesPerCluster;
            if (runEnd <= offset)
            {
                continue;
            }

            long start = Math.Max(offset, run.StartVcn * _bytesPerCluster);
            long next = (start + _recordSize - 1) / _recordSize;

            return next * _recordSize + _recordSize <= _mftDataSize ? next : -1;
        }

        return -1;
    }

    /// <summary>
    /// The first record at or after <paramref name="recordNumber"/> that the volume will give up,
    /// or -1 when nothing from there on can be read.
    /// <para>
    /// Reading a record costs a read that fails slowly, so this feels its way forward in jumps that
    /// double, and then closes in on where the readable stretch begins. Resuming at that first
    /// readable record is what keeps the records on the far side of a bad patch - a plain jump would
    /// step over them and lose folders that were never damaged.
    /// </para>
    /// </summary>
    public long FindNextReadableRecord(long recordNumber, long recordCount)
    {
        long bad = recordNumber - 1;
        long good = -1;

        for (long step = 1; good < 0; step = Math.Min(step * 2, MaxProbeStep))
        {
            long probe = bad + step;
            if (probe >= recordCount)
            {
                return -1;
            }

            if (CanRead(probe))
            {
                good = probe;
            }
            else
            {
                bad = probe;
            }
        }

        // Everything between the last record that failed and the one that worked is unknown; the
        // first readable record in there is where the scan picks up again.
        long low = bad + 1;
        long high = good;

        while (low < high)
        {
            long middle = low + ((high - low) / 2);
            if (CanRead(middle))
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }

    private bool CanRead(long recordNumber) => ReadRecord(recordNumber, _probe) == MftRecordRead.Success;

    public void Dispose()
    {
        // The volume stream is owned by the caller.
    }

    private MftRecordRead EnsureAvailable(long offset, int count)
    {
        if (offset < _bufferStart || offset > _bufferStart + _validBytes)
        {
            _bufferStart = offset;
            _validBytes = 0;
        }

        int delta = (int)(offset - _bufferStart);
        if (delta > 0)
        {
            Buffer.BlockCopy(_buffer, delta, _buffer, 0, _validBytes - delta);
            _bufferStart = offset;
            _validBytes -= delta;
        }

        while (_validBytes < count)
        {
            MftRecordRead filled = FillFromVolume();
            if (filled != MftRecordRead.Success)
            {
                return filled;
            }
        }

        return MftRecordRead.Success;
    }

    private MftRecordRead FillFromVolume()
    {
        long byteOffset = _bufferStart + _validBytes;
        if (byteOffset >= _mftDataSize || byteOffset < 0)
        {
            return MftRecordRead.BeyondTable;
        }

        long vcn = byteOffset / _bytesPerCluster;
        int runIndex = FindRun(vcn);
        if (runIndex < 0)
        {
            return MftRecordRead.NotCovered;
        }

        DataRun run = _runs[runIndex];
        long offsetInsideCluster = byteOffset - (vcn * _bytesPerCluster);
        long remainingInRun = ((run.EndVcn - vcn) * _bytesPerCluster) - offsetInsideCluster;
        if (remainingInRun <= 0)
        {
            return MftRecordRead.NotCovered;
        }

        int wanted = (int)Math.Min(_buffer.Length - _validBytes, remainingInRun);
        if (wanted <= 0)
        {
            return MftRecordRead.ReadFailed;
        }

        int read;
        if (run.IsSparse)
        {
            Array.Clear(_buffer, _validBytes, wanted);
            read = wanted;
        }
        else
        {
            long physicalOffset = ((run.StartLcn + (vcn - run.StartVcn)) * _bytesPerCluster) + offsetInsideCluster;
            read = ReadAsMuchAsTheVolumeGives(physicalOffset, wanted);
        }

        if (read <= 0)
        {
            return MftRecordRead.ReadFailed;
        }

        _validBytes += read;
        return MftRecordRead.Success;
    }

    /// <summary>
    /// Reads as much as the volume will give at this offset, asking for less when the whole request
    /// fails. A read that crosses unreadable clusters comes back empty in one piece, and taking that
    /// for the size of the damage would throw away every record before it as well.
    /// </summary>
    private int ReadAsMuchAsTheVolumeGives(long physicalOffset, int wanted)
    {
        // Windows only reads a whole number of sectors, so the request can only shrink that far.
        int smallest = Math.Max(_volume.BytesPerSector, 1);

        while (true)
        {
            int read = ReadWithRetries(physicalOffset, wanted);
            if (read > 0 || wanted <= smallest)
            {
                return read;
            }

            int shorter = wanted / 2 / smallest * smallest;
            wanted = shorter >= smallest ? shorter : smallest;
        }
    }

    private int ReadWithRetries(long physicalOffset, int count)
    {
        int read = 0;

        for (int attempt = 0; attempt < ReadAttempts && read <= 0; attempt++)
        {
            read = _volume.ReadAligned(physicalOffset, _buffer, _validBytes, count);
        }

        return read;
    }

    private int FindRun(long vcn)
    {
        if (_lastRunIndex >= 0 && _lastRunIndex < _runs.Count)
        {
            DataRun current = _runs[_lastRunIndex];
            if (vcn >= current.StartVcn && vcn < current.EndVcn)
            {
                return _lastRunIndex;
            }
        }

        for (int i = 0; i < _runs.Count; i++)
        {
            DataRun run = _runs[i];
            if (vcn >= run.StartVcn && vcn < run.EndVcn)
            {
                _lastRunIndex = i;
                return i;
            }
        }

        return -1;
    }
}
