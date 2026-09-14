using System.Buffers.Binary;
using System.Text;

namespace DataFinder.Core.Ntfs;

/// <summary>
/// Geometry of an NTFS volume, read from the volume boot record. Everything the MFT reader
/// needs in order to translate a master file table record number into a byte offset.
/// </summary>
public sealed class NtfsBootSector
{
    /// <summary>Bytes to read when probing a volume (a multiple of both 512 and 4096, so the read is always aligned).</summary>
    public const int ProbeSize = 4096;

    private const string ExpectedOemId = "NTFS    ";

    public required string OemId { get; init; }

    public int BytesPerSector { get; init; }

    public int SectorsPerCluster { get; init; }

    public int BytesPerCluster => BytesPerSector * SectorsPerCluster;

    public int MftRecordSize { get; init; }

    public int IndexBufferSize { get; init; }

    public long TotalSectors { get; init; }

    public long MftStartCluster { get; init; }

    public long MftMirrorStartCluster { get; init; }

    public long VolumeSerialNumber { get; init; }

    public long VolumeSizeBytes => TotalSectors * (long)BytesPerSector;

    public long MftStartByteOffset => MftStartCluster * (long)BytesPerCluster;

    public static bool TryParse(ReadOnlySpan<byte> buffer, out NtfsBootSector? bootSector, out string? error)
    {
        bootSector = null;
        error = null;

        if (buffer.Length < 0x50)
        {
            error = "The volume boot sector is too small to be an NTFS boot sector.";
            return false;
        }

        string oemId = Encoding.ASCII.GetString(buffer.Slice(0x03, 8));
        if (!string.Equals(oemId, ExpectedOemId, StringComparison.Ordinal))
        {
            string shown = oemId.Trim();
            error = shown.Length == 0
                ? "This volume does not look like an NTFS volume (no file system name in the boot sector)."
                : $"This volume is not NTFS (its boot sector reports '{shown}').";
            return false;
        }

        int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0x0B, 2));
        int sectorsPerCluster = buffer[0x0D];

        if (!IsPowerOfTwo(bytesPerSector) || bytesPerSector < 256 || bytesPerSector > 8192)
        {
            error = $"The volume reports an odd sector size ({bytesPerSector} bytes).";
            return false;
        }

        if (!IsPowerOfTwo(sectorsPerCluster) || sectorsPerCluster == 0)
        {
            error = $"The volume reports an odd cluster size ({sectorsPerCluster} sectors per cluster).";
            return false;
        }

        int bytesPerCluster = bytesPerSector * sectorsPerCluster;
        int mftRecordSize = ClustersToBytes(buffer[0x40], bytesPerCluster);
        int indexBufferSize = ClustersToBytes(buffer[0x44], bytesPerCluster);

        if (mftRecordSize < 256 || mftRecordSize > 65536)
        {
            error = $"The volume reports an unsupported MFT record size ({mftRecordSize} bytes).";
            return false;
        }

        long totalSectors = BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(0x28, 8));
        long mftStartCluster = BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(0x30, 8));
        long mftMirrorStartCluster = BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(0x38, 8));

        if (mftStartCluster <= 0 || totalSectors <= 0)
        {
            error = "The volume boot sector does not contain a usable master file table location.";
            return false;
        }

        long serialNumber = BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(0x48, 8));

        bootSector = new NtfsBootSector
        {
            OemId = oemId,
            BytesPerSector = bytesPerSector,
            SectorsPerCluster = sectorsPerCluster,
            MftRecordSize = mftRecordSize,
            IndexBufferSize = indexBufferSize,
            TotalSectors = totalSectors,
            MftStartCluster = mftStartCluster,
            MftMirrorStartCluster = mftMirrorStartCluster,
            VolumeSerialNumber = serialNumber,
        };
        return true;
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    /// <summary>
    /// The boot sector stores the MFT record size either as a cluster count (positive) or as a
    /// negative power of two, meaning 2^-n bytes.
    /// </summary>
    private static int ClustersToBytes(byte rawValue, int bytesPerCluster)
    {
        sbyte signed = unchecked((sbyte)rawValue);
        if (signed > 0)
        {
            return signed * bytesPerCluster;
        }

        if (signed < 0 && -signed < 31)
        {
            return 1 << -signed;
        }

        return 0;
    }
}

