using System.Buffers.Binary;
using DataFinder.Core.Models;
using DataFinder.Core.Ntfs;
using DataFinder.Core.Tests.Support;
using Xunit;

namespace DataFinder.Core.Tests;

/// <summary>
/// Whole scans over a volume built in memory. These are the shapes a real drive only shows once
/// something has gone wrong: a table split over several extents, a hole between its runs, and a
/// stretch of it the volume will not read.
/// </summary>
public sealed class NtfsVolumeScannerTests
{
    /// <summary>Every extent in these tests is eight clusters, which is thirty-two records.</summary>
    private const int RecordsPerExtent = 32;

    private const long TableSize = 3 * RecordsPerExtent * FakeNtfsVolume.RecordSize;

    private static readonly ScanSettings AnythingMatches = new() { MinSizeBytes = 0, MinDirectFileCount = 0 };

    [Fact]
    public void ReadsTheWholeTableWhenALaterExtensionRecordOnlyBecomesReachableAfterAnExtentIsMerged()
    {
        // Three extents: records 0-31, 32-63 and 64-95. The extension record for the third extent
        // sits at record 40, which the first extent does not cover, so it can only be read once the
        // second extent is known. Resolving the extents in a single pass stops short of it and loses
        // the last third of the table.
        var volume = new FakeNtfsVolume();
        PlaceMft(volume, TableSize);
        PlaceExtension(volume, TableSize, record: 7, lowestVcn: 8, startCluster: 200, at: ThreeExtents(7));
        PlaceExtension(volume, TableSize, record: 40, lowestVcn: 16, startCluster: 300, at: ThreeExtents(40));
        PlaceFolders(volume, ThreeExtents, "Media", folderRecord: 20, fileRecord: 21);
        PlaceFolders(volume, ThreeExtents, "Recordings", folderRecord: 80, fileRecord: 81);

        ScanReport report = Scan(volume);

        Assert.True(report.MftReadCompleted, string.Join(" / ", report.Warnings));
        Assert.Equal(3 * RecordsPerExtent, report.ExpectedRecordCount);
        Assert.Equal(report.ExpectedRecordCount, report.RecordsRead);
        Assert.Equal(new[] { @"D:\Media", @"D:\Recordings" }, Paths(report));
    }

    [Fact]
    public void CarriesOnPastAHoleBetweenTheRunsOfTheTable()
    {
        // The runs cover records 0-31 and, four clusters further on, records 48-79. The records in
        // between are in no run at all, so nothing there can be read - but whatever sits after the
        // hole still has to be found.
        var volume = new FakeNtfsVolume();
        PlaceMft(volume, 80 * FakeNtfsVolume.RecordSize);
        PlaceExtension(volume, 80 * FakeNtfsVolume.RecordSize, record: 7, lowestVcn: 12, startCluster: 120, at: ThreeExtents(7));
        PlaceFolders(volume, PastTheHole, "Media", folderRecord: 60, fileRecord: 61);

        ScanReport report = Scan(volume);

        Assert.False(report.MftReadCompleted);
        Assert.Equal(80, report.ExpectedRecordCount);
        Assert.Contains(report.Warnings, warning => warning.Contains("hole between the data runs", StringComparison.Ordinal));

        // The thirty-two records before the hole and the thirty-two after it were read; the sixteen
        // in the hole were not.
        Assert.Equal(64, report.RecordsRead);
        Assert.Equal(new[] { @"D:\Media" }, Paths(report));
    }

    [Fact]
    public void SkipsAStretchTheVolumeWillNotReadAndFindsWhatComesAfterIt()
    {
        // One extent of sixteen clusters, so records 0-63, with a bad patch where records 32-47 sit.
        const long size = 64 * FakeNtfsVolume.RecordSize;
        var volume = new FakeNtfsVolume();

        volume.Place(
            FakeNtfsVolume.Cluster(100),
            new MftRecordBuilder(0, isDirectory: false)
                .AddNonResidentDataAttribute(size, FakeNtfsVolume.Run(16, 100))
                .Build());

        volume.Volume.Fail(FakeNtfsVolume.Cluster(108), 4 * FakeNtfsVolume.BytesPerCluster);

        PlaceFolders(volume, recordNumber => InExtent(100, 0, recordNumber), "Media", folderRecord: 50, fileRecord: 51);

        ScanReport report = Scan(volume);

        Assert.False(report.MftReadCompleted);
        Assert.Contains(
            report.Warnings,
            warning => warning.Contains("could not be read from the volume and were skipped", StringComparison.Ordinal));

        // The read was tried more than once before it was given up on.
        Assert.True(volume.Volume.FailedReads >= 3, $"reads turned away: {volume.Volume.FailedReads}");

        // Every record outside the bad patch was read: the sixteen in it were skipped, and nothing
        // on the far side of it was stepped over.
        Assert.Equal(64 - 16, report.RecordsRead);

        // The folder past the bad patch is still found.
        Assert.Equal(new[] { @"D:\Media" }, Paths(report));
    }

    [Fact]
    public void ReadsTheEntriesOfANonResidentAttributeListOfTheTableItself()
    {
        // A table fragmented enough that its own $ATTRIBUTE_LIST does not fit in a record: the
        // entries sit in a cluster of their own, which only a reader that has the volume can follow.
        var volume = new FakeNtfsVolume();

        volume.Place(
            FakeNtfsVolume.Cluster(100),
            new MftRecordBuilder(0, isDirectory: false)
                .AddNonResidentDataAttribute(TableSize, FakeNtfsVolume.Run(8, 100))
                .AddNonResidentAttributeListAttribute(3 * 0x18, FakeNtfsVolume.Run(1, 500))
                .Build());
        volume.Place(
            FakeNtfsVolume.Cluster(500),
            ListEntry(0x80, 0, 0).Concat(ListEntry(0x80, 8, 7)).Concat(ListEntry(0x80, 16, 40)).ToArray());

        PlaceExtension(volume, TableSize, record: 7, lowestVcn: 8, startCluster: 200, at: ThreeExtents(7));
        PlaceExtension(volume, TableSize, record: 40, lowestVcn: 16, startCluster: 300, at: ThreeExtents(40));
        PlaceFolders(volume, ThreeExtents, "Media", folderRecord: 20, fileRecord: 21);
        PlaceFolders(volume, ThreeExtents, "Recordings", folderRecord: 80, fileRecord: 81);

        ScanReport report = Scan(volume);

        Assert.True(report.MftReadCompleted, string.Join(" / ", report.Warnings));
        Assert.Equal(report.ExpectedRecordCount, report.RecordsRead);
        Assert.Equal(new[] { @"D:\Media", @"D:\Recordings" }, Paths(report));
    }

    private static ScanReport Scan(FakeNtfsVolume volume)
    {
        var scanner = new NtfsVolumeScanner(_ => volume.Volume);
        var info = new VolumeInfo
        {
            DriveLetter = 'D',
            RootPath = @"D:\",
            TotalSizeBytes = volume.Volume.VolumeSizeBytes,
        };

        return scanner.Scan(info, AnythingMatches, progress: null, CancellationToken.None);
    }

    private static IReadOnlyList<string> Paths(ScanReport report) =>
        report.Results.Select(result => result.FullPath).ToList();

    /// <summary>The $MFT record: the first extent of its $DATA, and a list naming the other two.</summary>
    private static void PlaceMft(FakeNtfsVolume volume, long tableSize) =>
        volume.Place(
            FakeNtfsVolume.Cluster(100),
            new MftRecordBuilder(0, isDirectory: false)
                .AddNonResidentDataAttribute(tableSize, FakeNtfsVolume.Run(8, 100))
                .AddAttributeListAttribute((0x80, 0, 0), (0x80, 8, 7), (0x80, 16, 40))
                .Build());

    /// <summary>An extension record of the table, carrying one more extent of its $DATA.</summary>
    private static void PlaceExtension(
        FakeNtfsVolume volume,
        long tableSize,
        uint record,
        long lowestVcn,
        long startCluster,
        long at) =>
        volume.Place(
            at,
            new MftRecordBuilder(record, isDirectory: false)
                .AsExtensionOf(0)
                .AddNonResidentDataAttribute(tableSize, FakeNtfsVolume.Run(8, startCluster), lowestVcn)
                .Build());

    /// <summary>The root, a folder under it, and a 300 MB file inside that folder.</summary>
    private static void PlaceFolders(
        FakeNtfsVolume volume,
        Func<uint, long> where,
        string folderName,
        uint folderRecord,
        uint fileRecord)
    {
        volume.Place(
            where(5),
            new MftRecordBuilder(5, isDirectory: true)
                .AddFileNameAttribute(5, ".", 0, isDirectory: true)
                .Build());
        volume.Place(
            where(folderRecord),
            new MftRecordBuilder(folderRecord, isDirectory: true)
                .AddFileNameAttribute(5, folderName, 0, isDirectory: true)
                .Build());
        volume.Place(
            where(fileRecord),
            new MftRecordBuilder(fileRecord, isDirectory: false)
                .AddFileNameAttribute(folderRecord, "big.bin", 300L * 1024 * 1024, isDirectory: false)
                .Build());
    }

    /// <summary>The byte offset of a record in an extent that starts at a cluster and a record number.</summary>
    private static long InExtent(long startCluster, uint firstRecord, uint recordNumber) =>
        FakeNtfsVolume.Cluster(startCluster) + ((long)recordNumber - firstRecord) * FakeNtfsVolume.RecordSize;

    /// <summary>Where a record sits when the table is the three extents at clusters 100, 200 and 300.</summary>
    private static long ThreeExtents(uint recordNumber) => recordNumber switch
    {
        < RecordsPerExtent => InExtent(100, 0, recordNumber),
        < 2 * RecordsPerExtent => InExtent(200, RecordsPerExtent, recordNumber),
        _ => InExtent(300, 2 * RecordsPerExtent, recordNumber),
    };

    /// <summary>Where a record sits when the runs cover records 0-31 and then 48-79.</summary>
    private static long PastTheHole(uint recordNumber) =>
        recordNumber < RecordsPerExtent ? InExtent(100, 0, recordNumber) : InExtent(120, 48, recordNumber);

    /// <summary>One entry of an <c>$ATTRIBUTE_LIST</c>, as NTFS writes it.</summary>
    private static byte[] ListEntry(uint attributeType, long lowestVcn, uint recordNumber)
    {
        var entry = new byte[0x18];
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(0x00), attributeType);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(0x04), 0x18);
        BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(0x08), lowestVcn);
        BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(0x10), recordNumber);
        return entry;
    }
}
