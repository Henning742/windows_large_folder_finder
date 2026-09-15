namespace DataFinder.Core.Ntfs;

/// <summary>
/// Reads a range of an attribute through that attribute's own data runs, for the attributes whose
/// content lives on the volume rather than inside a record - a non-resident <c>$ATTRIBUTE_LIST</c>,
/// for instance.
/// </summary>
public static class RunReader
{
    /// <summary>
    /// Reads <paramref name="count"/> bytes at <paramref name="offset"/> into the destination.
    /// Returns false when no run covers part of the range, or the volume would not give up the bytes.
    /// </summary>
    public static bool TryRead(
        IRawVolumeReader volume,
        int bytesPerCluster,
        IReadOnlyList<DataRun> runs,
        long offset,
        byte[] destination,
        int count)
    {
        if (count <= 0)
        {
            return true;
        }

        if (offset < 0 || destination.Length < count || bytesPerCluster <= 0)
        {
            return false;
        }

        int copied = 0;

        while (copied < count)
        {
            long position = offset + copied;
            long vcn = position / bytesPerCluster;

            if (!TryFindRun(runs, vcn, out DataRun run))
            {
                return false;
            }

            long offsetInsideCluster = position - (vcn * bytesPerCluster);
            long remainingInRun = ((run.EndVcn - vcn) * bytesPerCluster) - offsetInsideCluster;
            int wanted = (int)Math.Min(count - copied, remainingInRun);

            if (wanted <= 0)
            {
                return false;
            }

            if (run.IsSparse)
            {
                Array.Clear(destination, copied, wanted);
            }
            else
            {
                long physicalOffset = ((run.StartLcn + (vcn - run.StartVcn)) * bytesPerCluster) + offsetInsideCluster;
                if (volume.ReadAligned(physicalOffset, destination, copied, wanted) < wanted)
                {
                    return false;
                }
            }

            copied += wanted;
        }

        return true;
    }

    /// <summary>Decodes an attribute's extents into runs and reads them in one step.</summary>
    public static bool TryReadExtents(
        IRawVolumeReader volume,
        int bytesPerCluster,
        IReadOnlyList<DataRunExtent> extents,
        long length,
        byte[] destination)
    {
        List<DataRun> runs = Runlist.DecodeExtents(extents);
        return runs.Count > 0 && TryRead(volume, bytesPerCluster, runs, 0, destination, (int)Math.Min(length, destination.Length));
    }

    private static bool TryFindRun(IReadOnlyList<DataRun> runs, long vcn, out DataRun found)
    {
        foreach (DataRun run in runs)
        {
            if (vcn >= run.StartVcn && vcn < run.EndVcn)
            {
                found = run;
                return true;
            }
        }

        found = default;
        return false;
    }
}
