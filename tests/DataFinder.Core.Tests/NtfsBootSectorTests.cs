using System.Buffers.Binary;
using System.Text;
using DataFinder.Core.Ntfs;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class NtfsBootSectorTests
{
    [Fact]
    public void ReadsGeometryFromAnNtfsBootSector()
    {
        byte[] buffer = BuildBootSector(bytesPerSector: 512, sectorsPerCluster: 8, mftStartCluster: 4, mftRecordSizeRaw: 0xF6);

        bool parsed = NtfsBootSector.TryParse(buffer, out NtfsBootSector? bootSector, out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(bootSector);
        Assert.Equal(512, bootSector!.BytesPerSector);
        Assert.Equal(8, bootSector.SectorsPerCluster);
        Assert.Equal(4096, bootSector.BytesPerCluster);
        Assert.Equal(1024, bootSector.MftRecordSize);
        Assert.Equal(4 * 4096L, bootSector.MftStartByteOffset);
    }

    [Fact]
    public void ReadsPositiveClusterCountsForTheRecordSize()
    {
        byte[] buffer = BuildBootSector(bytesPerSector: 512, sectorsPerCluster: 8, mftStartCluster: 4, mftRecordSizeRaw: 1);

        Assert.True(NtfsBootSector.TryParse(buffer, out NtfsBootSector? bootSector, out _));
        Assert.Equal(4096, bootSector!.MftRecordSize);
    }

    [Fact]
    public void RejectsVolumesThatAreNotNtfs()
    {
        byte[] buffer = BuildBootSector(bytesPerSector: 512, sectorsPerCluster: 8, mftStartCluster: 4, mftRecordSizeRaw: 0xF6);
        Encoding.ASCII.GetBytes("FAT32   ").CopyTo(buffer, 0x03);

        bool parsed = NtfsBootSector.TryParse(buffer, out NtfsBootSector? bootSector, out string? error);

        Assert.False(parsed);
        Assert.Null(bootSector);
        Assert.Contains("not NTFS", error);
    }

    private static byte[] BuildBootSector(int bytesPerSector, int sectorsPerCluster, long mftStartCluster, byte mftRecordSizeRaw)
    {
        var buffer = new byte[512];
        Encoding.ASCII.GetBytes("NTFS    ").CopyTo(buffer, 0x03);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0x0B, 2), (ushort)bytesPerSector);
        buffer[0x0D] = (byte)sectorsPerCluster;
        buffer[0x40] = mftRecordSizeRaw;
        buffer[0x44] = 0x01;
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(0x28, 8), 2_000_000L);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(0x30, 8), mftStartCluster);
        return buffer;
    }
}

