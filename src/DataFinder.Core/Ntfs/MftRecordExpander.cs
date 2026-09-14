namespace DataFinder.Core.Ntfs;

/// <summary>Reads one master file table record by number. Lets the expander work without a volume.</summary>
public interface IMftRecordSource
{
    /// <summary>Copies the record into <paramref name="destination"/>. False when it cannot be read.</summary>
    bool TryReadRecord(uint recordNumber, byte[] destination);
}

/// <summary>
/// Resolves an <c>$ATTRIBUTE_LIST</c>: reads the referenced extension records and merges their
/// attributes into the base record, so a file whose <c>$DATA</c> or <c>$FILE_NAME</c> lives in an
/// extension record is seen as one complete file.
/// </summary>
public sealed class MftRecordExpander
{
    private readonly IMftRecordSource _source;
    private readonly MftRecordParser _parser = new();
    private readonly int _bytesPerSector;
    private readonly bool _captureDataRunlists;
    private readonly byte[] _buffer;

    /// <param name="source">Where extension records are read from.</param>
    /// <param name="bytesPerSector">Sector size used to undo the update sequence fix-ups.</param>
    /// <param name="recordSize">Size of one MFT record.</param>
    /// <param name="captureDataRunlists">True when the $DATA run lists are needed (only for $MFT).</param>
    public MftRecordExpander(
        IMftRecordSource source,
        int bytesPerSector,
        int recordSize,
        bool captureDataRunlists = false)
    {
        _source = source;
        _bytesPerSector = bytesPerSector;
        _captureDataRunlists = captureDataRunlists;
        _buffer = new byte[recordSize];
    }

    /// <summary>
    /// Merges every attribute the list points at into <paramref name="baseRecord"/>. The list is
    /// followed breadth first, so an extension record that carries a list of its own is followed
    /// too. Cycles and records that cannot be read are skipped rather than trusted.
    /// Returns false when the list was incomplete, so the caller can warn about it.
    /// </summary>
    public bool Expand(MftRecordParseResult baseRecord)
    {
        if (baseRecord.AttributeListIsNonResident)
        {
            return false;
        }

        if (baseRecord.AttributeList.Count == 0)
        {
            return true;
        }

        bool complete = true;
        var visited = new HashSet<uint> { baseRecord.RecordNumber, 0 };
        var pending = new Queue<AttributeListEntry>();

        foreach (AttributeListEntry entry in baseRecord.AttributeList)
        {
            pending.Enqueue(entry);
        }

        while (pending.Count > 0)
        {
            AttributeListEntry entry = pending.Dequeue();
            if (!visited.Add(entry.RecordNumber))
            {
                continue;
            }

            if (!_source.TryReadRecord(entry.RecordNumber, _buffer))
            {
                complete = false;
                continue;
            }

            MftRecordParseResult? extension = _parser.Parse(_buffer, _bytesPerSector, entry.RecordNumber, _captureDataRunlists);
            if (extension is null || extension.IsCorrupt)
            {
                complete = false;
                continue;
            }

            Merge(baseRecord, extension);

            foreach (AttributeListEntry nested in extension.AttributeList)
            {
                pending.Enqueue(nested);
            }
        }

        return complete;
    }

    /// <summary>
    /// Folds the attributes of an extension record into its base record. The extension's own
    /// record number is replaced by the base record's, so names and sizes end up attached to the
    /// file the user can actually see.
    /// </summary>
    public static void Merge(MftRecordParseResult baseRecord, MftRecordParseResult extension)
    {
        if (extension.IsDirectory)
        {
            baseRecord.IsDirectory = true;
        }

        foreach (FileNameLink link in extension.Links)
        {
            var adjusted = link with
            {
                RecordNumber = baseRecord.RecordNumber,
                IsDirectory = baseRecord.IsDirectory || link.IsDirectory,
            };

            if (!ContainsLink(baseRecord.Links, adjusted))
            {
                baseRecord.Links.Add(adjusted);
            }
        }

        if (extension.HasData)
        {
            baseRecord.HasData = true;
            baseRecord.DataSize = Math.Max(baseRecord.DataSize, extension.DataSize);
        }

        foreach (DataRunExtent extent in extension.DataExtents)
        {
            if (!baseRecord.DataExtents.Exists(existing => existing.LowestVcn == extent.LowestVcn))
            {
                baseRecord.DataExtents.Add(extent);
            }
        }

        MftRecordParser.ApplyDosNameFilter(baseRecord);
    }

    private static bool ContainsLink(List<FileNameLink> links, FileNameLink candidate)
    {
        foreach (FileNameLink link in links)
        {
            if (link.ParentRecordNumber == candidate.ParentRecordNumber &&
                link.NameNamespace == candidate.NameNamespace &&
                string.Equals(link.Name, candidate.Name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
