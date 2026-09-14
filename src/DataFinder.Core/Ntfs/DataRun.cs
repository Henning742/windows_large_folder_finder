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

/// <summary>
/// One extent of a non-resident attribute: the raw mapping pairs of a single MFT record plus the
/// virtual cluster number the extent starts at. A large attribute that does not fit in one record
/// is stored as several extents, each described by its own record and its own <c>LowestVcn</c>.
/// </summary>
public sealed record DataRunExtent(long LowestVcn, byte[] Runlist);
