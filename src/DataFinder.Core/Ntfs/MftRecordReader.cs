namespace DataFinder.Core.Ntfs;

/// <summary>
/// Streams master file table records in order. The records live in the $MFT $DATA stream, which
/// is usually fragmented, so every record is translated from a virtual cluster number to a
/// physical byte offset through the data runs of $MFT itself.
/// </summary>
public sealed class MftRecordReader : IDisposable, IMftRecordSource
{
    private readonly RawVolumeStream _volume;
    private readonly int _bytesPerCluster;
    private readonly int _recordSize;
    private readonly IReadOnlyList<DataRun> _runs;
    private readonly long _mftDataSize;
    private readonly byte[] _buffer;

    private long _bufferStart;
    private int _validBytes;
    private int _lastRunIndex;

    public MftRecordReader(
        RawVolumeStream volume,
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
    }

    public long RecordCount => _recordSize > 0 ? _mftDataSize / _recordSize : 0;

    /// <summary>Copy of one record, used when following an <c>$ATTRIBUTE_LIST</c> into another record.</summary>
    public bool TryReadRecord(uint recordNumber, byte[] destination) => TryGetRecord(recordNumber, destination);

    /// <summary>Copies one record into <paramref name="destination"/>. Returns false at the end of the table.</summary>
    public bool TryGetRecord(long recordNumber, byte[] destination)
    {
        if (recordNumber < 0 || destination.Length < _recordSize)
        {
            return false;
        }

        long offset = recordNumber * _recordSize;
        if (offset + _recordSize > _mftDataSize)
        {
            return false;
        }

        if (!EnsureAvailable(offset, _recordSize))
        {
            return false;
        }

        int delta = (int)(offset - _bufferStart);
        Buffer.BlockCopy(_buffer, delta, destination, 0, _recordSize);
        return true;
    }

    public void Dispose()
    {
        // The volume stream is owned by the caller.
    }

    private bool EnsureAvailable(long offset, int count)
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
            if (!FillFromVolume())
            {
                return false;
            }
        }

        return true;
    }

    private bool FillFromVolume()
    {
        long byteOffset = _bufferStart + _validBytes;
        if (byteOffset >= _mftDataSize || byteOffset < 0)
        {
            return false;
        }

        long vcn = byteOffset / _bytesPerCluster;
        int runIndex = FindRun(vcn);
        if (runIndex < 0)
        {
            return false;
        }

        DataRun run = _runs[runIndex];
        long offsetInsideCluster = byteOffset - (vcn * _bytesPerCluster);
        long remainingInRun = ((run.EndVcn - vcn) * _bytesPerCluster) - offsetInsideCluster;
        if (remainingInRun <= 0)
        {
            return false;
        }

        int wanted = (int)Math.Min(_buffer.Length - _validBytes, remainingInRun);
        if (wanted <= 0)
        {
            return false;
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
            read = _volume.ReadAligned(physicalOffset, _buffer, _validBytes, wanted);
        }

        if (read <= 0)
        {
            return false;
        }

        _validBytes += read;
        return true;
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
