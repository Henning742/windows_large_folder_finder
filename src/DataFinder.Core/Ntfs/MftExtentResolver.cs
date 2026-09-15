namespace DataFinder.Core.Ntfs;

/// <summary>
/// Works out the data runs of the <c>$MFT</c>'s own <c>$DATA</c> attribute. A big table is split over
/// several extents, each of them described by an extension record - and an extension record can only
/// be found once a run covers the byte offset its record number points at. Each needs the other, so
/// this goes round in passes: every pass can reach records the pass before could not, and it stops as
/// soon as a pass adds no extent.
/// </summary>
public static class MftExtentResolver
{
    /// <summary>More passes than any attribute list a real volume can have needs.</summary>
    private const int MaxPasses = 8;

    /// <summary>
    /// Follows the <c>$MFT</c>'s <c>$ATTRIBUTE_LIST</c> for as long as it keeps turning up extents,
    /// and answers with the runs the whole table can be read with.
    /// </summary>
    public static List<DataRun> Resolve(
        IRawVolumeReader volume,
        int bytesPerCluster,
        int bytesPerSector,
        int recordSize,
        MftRecordParseResult mftRecord)
    {
        List<DataRun> runs = Runlist.DecodeExtents(mftRecord.DataExtents);
        long dataSize = mftRecord.DataSize;

        for (int pass = 0; pass < MaxPasses; pass++)
        {
            int knownExtents = mftRecord.DataExtents.Count;

            using (var reader = new MftRecordReader(volume, bytesPerCluster, recordSize, runs, dataSize))
            {
                var expander = new MftRecordExpander(
                    reader,
                    bytesPerSector,
                    recordSize,
                    captureDataRunlists: true,
                    readAttributeContent: AttributeContent(volume, bytesPerCluster));

                expander.Expand(mftRecord);
            }

            if (mftRecord.DataExtents.Count == knownExtents)
            {
                break;
            }

            // A malformed run list in an extension record leaves the table as it was; the scan still
            // reads what it can rather than refusing the volume.
            List<DataRun>? decoded = TryDecode(mftRecord.DataExtents);
            if (decoded is not null)
            {
                runs = decoded;
                dataSize = Math.Max(dataSize, mftRecord.DataSize);
            }
        }

        return runs;
    }

    private static List<DataRun>? TryDecode(IReadOnlyList<DataRunExtent> extents)
    {
        try
        {
            return Runlist.DecodeExtents(extents);
        }
        catch (NtfsScanException)
        {
            return null;
        }
    }

    /// <summary>Reads the content of an attribute that lives on the volume rather than in a record.</summary>
    public static Func<IReadOnlyList<DataRunExtent>, long, byte[]?> AttributeContent(
        IRawVolumeReader volume,
        int bytesPerCluster) =>
        (extents, length) =>
        {
            if (length <= 0 || length > int.MaxValue)
            {
                return null;
            }

            var content = new byte[(int)length];
            return RunReader.TryReadExtents(volume, bytesPerCluster, extents, length, content) ? content : null;
        };
}
