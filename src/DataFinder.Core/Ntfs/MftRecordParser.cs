using System.Buffers.Binary;
using System.Text;

namespace DataFinder.Core.Ntfs;

/// <summary>Parses a single NTFS master file table record out of a raw byte buffer.</summary>
public sealed class MftRecordParser
{
    private const uint Signature = 0x454C4946; // "FILE"
    private const uint AttributeEnd = 0xFFFFFFFF;
    private const uint AttributeAttributeList = 0x20;
    private const uint AttributeFileName = 0x30;
    private const uint AttributeData = 0x80;
    private const int DosNamespace = 2;
    private const uint FileNameFlagDirectory = 0x10000000;

    private readonly MftRecordParseResult _result = new();

    /// <summary>
    /// Parses one record. The same (reused) result object is returned every time, or null when
    /// the bytes are not a usable MFT record.
    /// </summary>
    /// <param name="captureDataRunlist">True when the $DATA run lists are wanted, which only $MFT asks for.</param>
    /// <param name="isKnownExtension">
    /// True when the caller already knows this is an extension record, read because a base record's
    /// <c>$ATTRIBUTE_LIST</c> named it. An extension record can have its in-use flag clear, and its
    /// attributes - a later extent of the attribute, for instance - are needed all the same.
    /// </param>
    public MftRecordParseResult? Parse(
        Span<byte> record,
        int bytesPerSector,
        uint recordNumber,
        bool captureDataRunlist = false,
        bool isKnownExtension = false)
    {
        _result.Reset();
        _result.RecordNumber = recordNumber;

        if (record.Length < 0x30 || BinaryPrimitives.ReadUInt32LittleEndian(record[..4]) != Signature)
        {
            _result.IsCorrupt = true;
            return null;
        }

        if (!ApplyUpdateSequenceArray(record, bytesPerSector))
        {
            _result.IsCorrupt = true;
            return null;
        }

        ushort flags = ReadUInt16(record, 0x16);
        _result.InUse = (flags & 0x0001) != 0;
        _result.IsDirectory = (flags & 0x0002) != 0;

        // A non-zero base reference marks this record as an extension of another record.
        _result.BaseRecordNumber = (uint)(ReadUInt64(record, 0x20) & 0x0000FFFFFFFFFFFFUL);

        // A record that is not in use is a deleted file, and what is left in it is not worth reading.
        // An extension record is not in use either, but its attributes are exactly what its base
        // record is missing, so a caller that knows it is one has them read.
        if (!_result.InUse && !isKnownExtension)
        {
            return _result;
        }

        int attributeOffset = ReadUInt16(record, 0x14);
        int usedSize = (int)ReadUInt32(record, 0x18);
        int limit = usedSize > 0 && usedSize <= record.Length ? usedSize : record.Length;

        if (attributeOffset < 0x18 || attributeOffset >= limit)
        {
            _result.IsCorrupt = true;
            return null;
        }

        long bestDataSize = -1;
        bool hasUnnamedData = false;
        int position = attributeOffset;

        while (position + 4 <= limit)
        {
            uint type = ReadUInt32(record, position);
            if (type == AttributeEnd)
            {
                break;
            }

            if (position + 0x10 > limit)
            {
                _result.IsCorrupt = true;
                break;
            }

            int length = (int)ReadUInt32(record, position + 0x04);
            if (length < 0x10 || position + length > limit)
            {
                _result.IsCorrupt = true;
                break;
            }

            byte nonResident = record[position + 0x08];
            bool hasName = record[position + 0x09] > 0;

            switch (type)
            {
                case AttributeAttributeList:
                    _result.HasAttributeList = true;
                    ReadAttributeListAttribute(record, position, length, _result);
                    break;

                // $FILE_NAME is an unnamed attribute: the file name lives in its content.
                case AttributeFileName when nonResident == 0:
                    ReadFileNameAttribute(record, position, length, _result);
                    break;

                case AttributeData when !hasName:
                {
                    long size = nonResident == 1
                        ? ReadInt64(record, position + 0x30)
                        : ReadUInt32(record, position + 0x10);

                    if (size > bestDataSize)
                    {
                        bestDataSize = size;
                    }

                    hasUnnamedData = true;

                    if (captureDataRunlist && nonResident == 1)
                    {
                        CaptureExtent(record, position, length, _result.DataExtents);
                    }

                    break;
                }
            }

            position += length;
        }

        _result.HasData = hasUnnamedData;
        _result.DataSize = hasUnnamedData ? Math.Max(bestDataSize, 0) : 0;

        ApplyDosNameFilter(_result);

        return _result;
    }

    /// <summary>
    /// DOS style 8.3 names duplicate a real name; drop them when a long name exists. Shared with
    /// the attribute-list merging so a name pulled out of an extension record is filtered too.
    /// </summary>
    internal static void ApplyDosNameFilter(MftRecordParseResult result)
    {
        if (result.Links.Count > 1 && result.Links.Exists(link => link.NameNamespace != DosNamespace))
        {
            result.Links.RemoveAll(link => link.NameNamespace == DosNamespace);
        }
    }

    /// <summary>
    /// NTFS overwrites the last two bytes of every sector inside a record with an update
    /// sequence number, and keeps the original bytes in the update sequence array. Those bytes
    /// have to be put back before the record can be read.
    /// </summary>
    private static bool ApplyUpdateSequenceArray(Span<byte> record, int bytesPerSector)
    {
        int updateSequenceOffset = ReadUInt16(record, 0x04);
        int updateSequenceCount = ReadUInt16(record, 0x06);
        int sectorSize = bytesPerSector > 0 ? bytesPerSector : 512;

        if (updateSequenceCount < 2 ||
            updateSequenceOffset < 0x30 ||
            updateSequenceOffset + (updateSequenceCount * 2) > record.Length)
        {
            return false;
        }

        ushort updateSequenceNumber = ReadUInt16(record, updateSequenceOffset);

        for (int i = 1; i < updateSequenceCount; i++)
        {
            int sectorEnd = (i * sectorSize) - 2;
            if (sectorEnd < 0 || sectorEnd + 2 > record.Length)
            {
                return false;
            }

            if (ReadUInt16(record, sectorEnd) != updateSequenceNumber)
            {
                return false;
            }

            ushort original = ReadUInt16(record, updateSequenceOffset + (i * 2));
            BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(sectorEnd, 2), original);
        }

        return true;
    }

    private static void ReadFileNameAttribute(ReadOnlySpan<byte> record, int attributeOffset, int attributeLength, MftRecordParseResult result)
    {
        int contentLength = (int)ReadUInt32(record, attributeOffset + 0x10);
        int contentOffset = ReadUInt16(record, attributeOffset + 0x14);
        int start = attributeOffset + contentOffset;
        int end = attributeOffset + attributeLength;

        if (contentOffset <= 0 || contentLength < 0x42 || start + 0x42 > end || end > record.Length)
        {
            return;
        }

        ulong parentReference = ReadUInt64(record, start);
        uint parentRecordNumber = (uint)(parentReference & 0x0000FFFFFFFFFFFFUL);
        long size = ReadInt64(record, start + 0x30);
        uint fileAttributes = ReadUInt32(record, start + 0x38);
        int nameLength = record[start + 0x40];
        int nameNamespace = record[start + 0x41];
        int nameBytes = nameLength * 2;

        if (nameLength == 0 || start + 0x42 + nameBytes > end)
        {
            return;
        }

        string name = Encoding.Unicode.GetString(record.Slice(start + 0x42, nameBytes));
        bool isDirectory = result.IsDirectory || (fileAttributes & FileNameFlagDirectory) != 0;

        result.Links.Add(new FileNameLink(
            result.RecordNumber,
            parentRecordNumber,
            name,
            nameNamespace,
            size,
            isDirectory));
    }

    private static void ReadAttributeListAttribute(ReadOnlySpan<byte> record, int attributeOffset, int attributeLength, MftRecordParseResult result)
    {
        byte nonResident = record[attributeOffset + 0x08];
        if (nonResident != 0)
        {
            // A non-resident $ATTRIBUTE_LIST keeps its entries in the attribute's own data runs, so
            // the record can only say where they are: reading them takes the volume. Whoever has one
            // - the scanner - does that and calls ReadAttributeListEntries with what came back.
            result.AttributeListIsNonResident = true;
            result.AttributeListDataSize = Math.Max(ReadInt64(record, attributeOffset + 0x30), 0);
            CaptureExtent(record, attributeOffset, attributeLength, result.AttributeListExtents);
            return;
        }

        int contentLength = (int)ReadUInt32(record, attributeOffset + 0x10);
        int contentOffset = ReadUInt16(record, attributeOffset + 0x14);
        int start = attributeOffset + contentOffset;
        int end = attributeOffset + attributeLength;

        if (contentOffset <= 0 || start >= end || end > record.Length)
        {
            return;
        }

        int available = Math.Min(end, start + contentLength) - start;
        if (available > 0)
        {
            ReadAttributeListEntries(record.Slice(start, available), result);
        }
    }

    /// <summary>
    /// Reads the entries of an <c>$ATTRIBUTE_LIST</c> out of its content, wherever that content came
    /// from: the record itself, or the attribute's own data runs when the list is not resident.
    /// </summary>
    public static void ReadAttributeListEntries(ReadOnlySpan<byte> list, MftRecordParseResult result)
    {
        int listEnd = list.Length;
        int position = 0;

        while (position + 0x18 <= listEnd)
        {
            uint attributeType = ReadUInt32(list, position);
            int entryLength = ReadUInt16(list, position + 0x04);
            int nameLength = list[position + 0x06];
            int nameOffset = list[position + 0x07];
            long lowestVcn = ReadInt64(list, position + 0x08);
            uint recordNumber = (uint)(ReadUInt64(list, position + 0x10) & 0x0000FFFFFFFFFFFFUL);

            if (entryLength < 0x18 || position + entryLength > listEnd)
            {
                break;
            }

            string name = string.Empty;
            if (nameLength > 0 &&
                nameOffset >= 0x18 &&
                nameOffset + (nameLength * 2) <= entryLength)
            {
                name = Encoding.Unicode.GetString(list.Slice(position + nameOffset, nameLength * 2));
            }

            result.AttributeList.Add(new AttributeListEntry(attributeType, lowestVcn, recordNumber, name));
            position += entryLength;
        }
    }

    private static void CaptureExtent(ReadOnlySpan<byte> record, int attributeOffset, int attributeLength, List<DataRunExtent> extents)
    {
        int runlistOffset = ReadUInt16(record, attributeOffset + 0x20);
        int start = attributeOffset + runlistOffset;
        int end = attributeOffset + attributeLength;

        if (runlistOffset < 0x40 || start >= end || end > record.Length)
        {
            return;
        }

        // Each extent's mapping pairs are relative to its own starting virtual cluster number.
        long lowestVcn = ReadInt64(record, attributeOffset + 0x10);
        if (extents.Exists(extent => extent.LowestVcn == lowestVcn))
        {
            return;
        }

        extents.Add(new DataRunExtent(lowestVcn, record.Slice(start, MeasureRunlist(record, start, end)).ToArray()));
    }

    /// <summary>
    /// Length of the run list starting at <paramref name="start"/>, including its terminator. The
    /// list is measured by walking its own entries, not by looking for the first zero byte: a
    /// mapping pairs field may legitimately contain 0x00 (an LCN's low byte often does, for
    /// example cluster 0x4000), and stopping there truncated the run list the reader depends on.
    /// </summary>
    private static int MeasureRunlist(ReadOnlySpan<byte> record, int start, int limit)
    {
        int position = start;

        while (position < limit)
        {
            byte header = record[position];
            if (header == 0)
            {
                return (position - start) + 1;
            }

            int lengthBytes = header & 0x0F;
            int offsetBytes = header >> 4;
            if (lengthBytes == 0 || lengthBytes > 8 || offsetBytes > 8)
            {
                break;
            }

            position += 1 + lengthBytes + offsetBytes;
        }

        // Malformed list: keep everything and let the decoder decide where to stop.
        return limit - start;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> buffer, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset, 2));

    private static uint ReadUInt32(ReadOnlySpan<byte> buffer, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(offset, 4));

    private static ulong ReadUInt64(ReadOnlySpan<byte> buffer, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(offset, 8));

    private static long ReadInt64(ReadOnlySpan<byte> buffer, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(offset, 8));
}
