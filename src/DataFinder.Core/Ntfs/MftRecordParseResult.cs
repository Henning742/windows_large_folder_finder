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

    /// <summary>
    /// The record this one belongs to when it is an extension record, or 0 when it is a base
    /// record. NTFS moves attributes that do not fit out of the base record into extension
    /// records, and those extension records must never be counted as files of their own.
    /// </summary>
    public uint BaseRecordNumber { get; set; }

    /// <summary>True when this record is an extension of another record rather than a file itself.</summary>
    public bool IsExtensionRecord => BaseRecordNumber != 0 && BaseRecordNumber != RecordNumber;

    public bool InUse { get; set; }

    public bool IsDirectory { get; set; }

    public bool IsCorrupt { get; set; }

    public bool HasData { get; set; }

    /// <summary>Size of the unnamed $DATA stream, which is the size Windows Explorer shows.</summary>
    public long DataSize { get; set; }

    /// <summary>True when the record continues in another record (a fragmented attribute).</summary>
    public bool HasAttributeList { get; set; }

    /// <summary>True when the $ATTRIBUTE_LIST is non-resident, so its own entries could not be read.</summary>
    public bool AttributeListIsNonResident { get; set; }

    /// <summary>The entries of the $ATTRIBUTE_LIST, each naming the record that holds an attribute.</summary>
    public List<AttributeListEntry> AttributeList { get; } = new();

    /// <summary>
    /// The extents of the unnamed $DATA attribute. Usually a single extent; more than one when the
    /// attribute is spread over several records. Only captured when asked for.
    /// </summary>
    public List<DataRunExtent> DataExtents { get; } = new();

    public List<FileNameLink> Links { get; } = new();

    internal void Reset()
    {
        RecordNumber = 0;
        BaseRecordNumber = 0;
        InUse = false;
        IsDirectory = false;
        IsCorrupt = false;
        HasData = false;
        DataSize = 0;
        HasAttributeList = false;
        AttributeListIsNonResident = false;
        AttributeList.Clear();
        DataExtents.Clear();
        Links.Clear();
    }
}
