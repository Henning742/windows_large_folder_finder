using DataFinder.Core.Ntfs;
using DataFinder.Core.Tests.Support;
using Xunit;

namespace DataFinder.Core.Tests;

/// <summary>
/// The reader on its own: what it says when a record cannot be read, and where it says the scan can
/// pick up again afterwards.
/// </summary>
public sealed class MftRecordReaderTests
{
    private const int BytesPerCluster = FakeNtfsVolume.BytesPerCluster;
    private const int RecordSize = FakeNtfsVolume.RecordSize;

    /// <summary>
    /// Two runs of two clusters each with a hole between them: the table is records 0-7 and 16-23,
    /// and records 8-15 are in no run at all.
    /// </summary>
    private static readonly DataRun[] RunsWithAHole =
    {
        new(0, 2, 10),
        new(4, 2, 20),
    };

    [Fact]
    public void TellsTheEndOfTheTableFromAHoleInItsRuns()
    {
        var volume = new FakeRawVolume(64 * BytesPerCluster);
        using var reader = new MftRecordReader(volume, BytesPerCluster, RecordSize, RunsWithAHole, 24 * RecordSize);

        Assert.Equal(24, reader.RecordCount);
        Assert.Equal(MftRecordRead.Success, reader.ReadRecord(0, new byte[RecordSize]));
        Assert.Equal(MftRecordRead.Success, reader.ReadRecord(16, new byte[RecordSize]));
        Assert.Equal(MftRecordRead.NotCovered, reader.ReadRecord(8, new byte[RecordSize]));
        Assert.Equal(MftRecordRead.BeyondTable, reader.ReadRecord(24, new byte[RecordSize]));
        Assert.Equal(MftRecordRead.BeyondTable, reader.ReadRecord(-1, new byte[RecordSize]));
    }

    [Fact]
    public void FindsTheNextRecordARunCovers()
    {
        var volume = new FakeRawVolume(64 * BytesPerCluster);
        using var reader = new MftRecordReader(volume, BytesPerCluster, RecordSize, RunsWithAHole, 24 * RecordSize);

        Assert.Equal(16, reader.FindNextCoveredRecord(8));
        Assert.Equal(16, reader.FindNextCoveredRecord(16));
        Assert.Equal(20, reader.FindNextCoveredRecord(20));
        Assert.Equal(-1, reader.FindNextCoveredRecord(24));
    }

    [Fact]
    public void RetriesAReadAndThenLooksForTheFirstRecordThatComesBack()
    {
        var volume = new FakeRawVolume(64 * BytesPerCluster);
        var runs = new[] { new DataRun(0, 8, 10) };
        using var reader = new MftRecordReader(volume, BytesPerCluster, RecordSize, runs, 32 * RecordSize);

        // Record 16 is the fourth cluster of the run, and that cluster will not read.
        volume.Fail(14 * BytesPerCluster, BytesPerCluster);

        Assert.Equal(MftRecordRead.ReadFailed, reader.ReadRecord(16, new byte[RecordSize]));

        // The read was tried more than once before it was given up on.
        Assert.True(volume.FailedReads >= 3, $"reads turned away: {volume.FailedReads}");

        // The first record that can be read again is the one after the bad cluster, not a record
        // somewhere far past it.
        Assert.Equal(20, reader.FindNextReadableRecord(16, 32));
        Assert.Equal(MftRecordRead.Success, reader.ReadRecord(20, new byte[RecordSize]));
    }

    [Fact]
    public void SaysThereIsNothingLeftWhenTheRestOfTheTableWillNotRead()
    {
        var volume = new FakeRawVolume(64 * BytesPerCluster);
        var runs = new[] { new DataRun(0, 8, 10) };
        using var reader = new MftRecordReader(volume, BytesPerCluster, RecordSize, runs, 32 * RecordSize);

        // Everything from record 4 on sits in clusters that will not read.
        volume.Fail(11 * BytesPerCluster, 7 * BytesPerCluster);

        Assert.Equal(-1, reader.FindNextReadableRecord(4, 32));
    }
}
