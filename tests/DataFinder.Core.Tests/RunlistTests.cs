using DataFinder.Core.Ntfs;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class RunlistTests
{
    [Fact]
    public void DecodesTwoRuns()
    {
        byte[] runList = { 0x11, 0x03, 0x10, 0x11, 0x02, 0x05, 0x00 };

        List<DataRun> runs = Runlist.Decode(runList);

        Assert.Equal(2, runs.Count);
        Assert.Equal(new DataRun(0, 3, 0x10), runs[0]);
        Assert.Equal(new DataRun(3, 2, 0x15), runs[1]);
    }

    [Fact]
    public void SignExtendsNegativeOffsets()
    {
        byte[] runList = { 0x11, 0x01, 0x20, 0x11, 0x01, 0xE0, 0x00 };

        List<DataRun> runs = Runlist.Decode(runList);

        Assert.Equal(2, runs.Count);
        Assert.Equal(0x20, runs[0].StartLcn);
        Assert.Equal(0x00, runs[1].StartLcn);
        Assert.Equal(1, runs[1].StartVcn);
    }

    [Fact]
    public void MarksSparseRunsAndStopsAtTheTerminator()
    {
        byte[] runList = { 0x01, 0x05, 0x00, 0x11, 0x01, 0x02 };

        List<DataRun> runs = Runlist.Decode(runList);

        DataRun run = Assert.Single(runs);
        Assert.True(run.IsSparse);
        Assert.Equal(5, run.ClusterCount);
    }

    [Fact]
    public void ReadsMultiByteOffsets()
    {
        byte[] runList = { 0x21, 0x02, 0x00, 0x01, 0x00 };

        DataRun run = Assert.Single(Runlist.Decode(runList));

        Assert.Equal(2, run.ClusterCount);
        Assert.Equal(0x100, run.StartLcn);
    }

    [Fact]
    public void CombinesTheExtentsOfASplitAttributeInVirtualClusterOrder()
    {
        var extents = new List<DataRunExtent>
        {
            // The second extent is listed first on purpose: the runs must be ordered by VCN anyway.
            new(4, new byte[] { 0x11, 0x02, 0x20, 0x00 }),
            new(0, new byte[] { 0x11, 0x04, 0x10, 0x00 }),
        };

        List<DataRun> runs = Runlist.DecodeExtents(extents);

        Assert.Equal(2, runs.Count);
        Assert.Equal(new DataRun(0, 4, 0x10), runs[0]);
        Assert.Equal(new DataRun(4, 2, 0x20), runs[1]);
    }
}
