namespace DataFinder.Core.Ntfs;

/// <summary>
/// One entry of an <c>$ATTRIBUTE_LIST</c>. NTFS splits a file's attributes across several MFT
/// records when they do not all fit in the base record, and this list is how the pieces are
/// stitched back together: every entry names an attribute and the MFT record that stores it.
/// </summary>
public readonly record struct AttributeListEntry(
    uint AttributeType,
    long LowestVcn,
    uint RecordNumber,
    string Name);
