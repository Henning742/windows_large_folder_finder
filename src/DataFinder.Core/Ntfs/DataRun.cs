namespace DataFinder.Core.Ntfs;

/// <summary>One contiguous piece of a non-resident attribute.</summary>
public readonly record struct DataRun(long StartVcn, long ClusterCount, long StartLcn)
{
    public long EndVcn => StartVcn + ClusterCount;

    /// <summary>True for a sparse run, which reads back as zeroes.</summary>
    public bool IsSparse => StartLcn < 0;

    public long ByteLength(int bytesPerCluster) => ClusterCount * bytesPerCluster;

    public long StartByteOffset(int bytesPerCluster) => StartLcn * bytesPerCluster;
}

