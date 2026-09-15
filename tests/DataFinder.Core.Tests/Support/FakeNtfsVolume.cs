using System.Buffers.Binary;
using System.Text;

namespace DataFinder.Core.Tests.Support;

/// <summary>
/// A small NTFS volume in memory: a boot sector that says where the table starts, and room to put
/// master file table records wherever the run list of a test says they sit.
/// </summary>
internal sealed class FakeNtfsVolume
{
    public const int BytesPerSector = 512;
    public const int SectorsPerCluster = 8;
    public const int BytesPerCluster = BytesPerSector * SectorsPerCluster;
    public const int RecordSize = 1024;
    /// <summary>Where the table starts, which is also where the first run of the tests begins.</summary>
    public const long MftStartCluster = 100;
    public const long TotalClusters = 2048;

    public FakeNtfsVolume()
    {
        Volume = new FakeRawVolume(TotalClusters * BytesPerCluster);

        Span<byte> boot = Volume.Bytes.AsSpan(0, 512);
        Encoding.ASCII.GetBytes("NTFS    ").CopyTo(boot[0x03..]);
        BinaryPrimitives.WriteUInt16LittleEndian(boot[0x0B..], BytesPerSector);
        boot[0x0D] = SectorsPerCluster;
        boot[0x40] = 0xF6; // -10, which is 2^10 bytes per record
        boot[0x44] = 0x01;
        BinaryPrimitives.WriteInt64LittleEndian(boot[0x28..], TotalClusters * SectorsPerCluster);
        BinaryPrimitives.WriteInt64LittleEndian(boot[0x30..], MftStartCluster);
    }

    public FakeRawVolume Volume { get; }

    public long MftStartByteOffset => MftStartCluster * BytesPerCluster;

    /// <summary>The byte offset of a cluster.</summary>
    public static long Cluster(long index) => index * BytesPerCluster;

    /// <summary>Puts one record where the run list of the test says it sits.</summary>
    public void Place(long byteOffset, byte[] record) => Volume.Write(byteOffset, record);

    /// <summary>The mapping pairs for one run: a cluster count and the cluster it starts at.</summary>
    public static byte[] Run(long clusterCount, long startCluster)
    {
        byte[] length = LittleEndian(clusterCount);
        byte[] offset = LittleEndian(startCluster);
        var bytes = new List<byte> { (byte)((offset.Length << 4) | length.Length) };

        bytes.AddRange(length);
        bytes.AddRange(offset);
        bytes.Add(0);
        return bytes.ToArray();
    }

    private static byte[] LittleEndian(long value)
    {
        var bytes = new List<byte>();

        do
        {
            bytes.Add((byte)(value & 0xFF));
            value >>= 8;
        }
        while (value > 0);

        // Mapping pairs are read as signed offsets, so a byte with its top bit set would come back
        // negative - cluster 200 has to be written as two bytes, not one.
        if ((bytes[^1] & 0x80) != 0)
        {
            bytes.Add(0);
        }

        return bytes.ToArray();
    }
}
