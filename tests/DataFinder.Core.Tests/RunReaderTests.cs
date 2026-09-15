using DataFinder.Core.Ntfs;
using DataFinder.Core.Tests.Support;
using Xunit;

namespace DataFinder.Core.Tests;

/// <summary>Reading an attribute straight through its own runs, which is how a list that is not in a record is read.</summary>
public sealed class RunReaderTests
{
    private const int BytesPerCluster = FakeNtfsVolume.BytesPerCluster;

    [Fact]
    public void ReadsAcrossTheRunsOfAnAttribute()
    {
        var volume = new FakeRawVolume(64 * BytesPerCluster);
        volume.Write(10 * BytesPerCluster, Filler(1));
        volume.Write(20 * BytesPerCluster, Filler(2));

        var runs = new[] { new DataRun(0, 1, 10), new DataRun(1, 1, 20) };
        var destination = new byte[2 * BytesPerCluster];

        Assert.True(RunReader.TryRead(volume, BytesPerCluster, runs, 0, destination, destination.Length));
        Assert.Equal(1, destination[0]);
        Assert.Equal(1, destination[BytesPerCluster - 1]);
        Assert.Equal(2, destination[BytesPerCluster]);
        Assert.Equal(2, destination[^1]);
    }

    [Fact]
    public void ReadsASparseRunAsZeroes()
    {
        var volume = new FakeRawVolume(64 * BytesPerCluster);
        var runs = new[] { new DataRun(0, 2, -1) };
        var destination = new byte[512];

        Assert.True(RunReader.TryRead(volume, BytesPerCluster, runs, BytesPerCluster, destination, destination.Length));
        Assert.All(destination, value => Assert.Equal(0, value));
    }

    [Fact]
    public void RefusesARangeNoRunCovers()
    {
        var volume = new FakeRawVolume(64 * BytesPerCluster);
        var runs = new[] { new DataRun(0, 1, 10) };
        var destination = new byte[BytesPerCluster];

        Assert.False(RunReader.TryRead(volume, BytesPerCluster, runs, 0, destination, BytesPerCluster + 1));
        Assert.False(RunReader.TryRead(volume, BytesPerCluster, Array.Empty<DataRun>(), 0, destination, 1));
    }

    [Fact]
    public void RefusesAStretchTheVolumeWillNotRead()
    {
        var volume = new FakeRawVolume(64 * BytesPerCluster);
        volume.Fail(10 * BytesPerCluster, BytesPerCluster);

        var runs = new[] { new DataRun(0, 1, 10) };
        Assert.False(RunReader.TryRead(volume, BytesPerCluster, runs, 0, new byte[512], 512));
    }

    private static byte[] Filler(byte value)
    {
        var bytes = new byte[BytesPerCluster];
        Array.Fill(bytes, value);
        return bytes;
    }
}
