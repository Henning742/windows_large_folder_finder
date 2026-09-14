namespace DataFinder.Core.Ntfs;

/// <summary>One name of a file or folder, as stored in a $FILE_NAME attribute.</summary>
public readonly record struct FileNameLink(
    uint RecordNumber,
    uint ParentRecordNumber,
    string Name,
    int NameNamespace,
    long FileNameSize,
    bool IsDirectory);

/// <summary>
/// The interesting contents of one master file table record. An instance is reused for every
/// record during a scan, so it has to be consumed before the next record is parsed.
/// </summary>
public sealed class MftRecordParseResult
{
    public uint RecordNumber { get; set; }

    public bool InUse { get; set; }

    public bool IsDirectory { get; set; }

    public bool IsCorrupt { get; set; }

    public bool HasData { get; set; }

    /// <summary>Size of the unnamed $DATA stream, which is the size Windows Explorer shows.</summary>
    public long DataSize { get; set; }

    /// <summary>True when the record continues in another record (a fragmented attribute).</summary>
    public bool HasAttributeList { get; set; }

    /// <summary>Raw data run list of the unnamed $DATA attribute. Only captured when asked for.</summary>
    public byte[]? DataRunlist { get; set; }

    public List<FileNameLink> Links { get; } = new();

    internal void Reset()
    {
        RecordNumber = 0;
        InUse = false;
        IsDirectory = false;
        IsCorrupt = false;
        HasData = false;
        DataSize = 0;
        HasAttributeList = false;
        DataRunlist = null;
        Links.Clear();
    }
}

