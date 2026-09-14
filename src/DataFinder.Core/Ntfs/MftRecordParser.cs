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
    public MftRecordParseResult? Parse(
        Span<byte> record,
        int bytesPerSector,
        uint recordNumber,
        bool captureDataRunlist = false)
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

        if (!_result.InUse)
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
                        CaptureRunlist(record, position, length, _result);
                    }

                    break;
                }
            }

            position += length;
        }

        _result.HasData = hasUnnamedData;
        _result.DataSize = hasUnnamedData ? Math.Max(bestDataSize, 0) : 0;

        // DOS style 8.3 names duplicate a real name; drop them when a long name exists.
        if (_result.Links.Count > 1 && _result.Links.Exists(link => link.NameNamespace != DosNamespace))
        {
            _result.Links.RemoveAll(link => link.NameNamespace == DosNamespace);
        }

        return _result;
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

    private static void CaptureRunlist(ReadOnlySpan<byte> record, int attributeOffset, int attributeLength, MftRecordParseResult result)
    {
        if (result.DataRunlist is not null)
        {
            return;
        }

        int runlistOffset = ReadUInt16(record, attributeOffset + 0x20);
        int start = attributeOffset + runlistOffset;
        int end = attributeOffset + attributeLength;

        if (runlistOffset < 0x40 || start >= end || end > record.Length)
        {
            return;
        }

        // The run list ends with a zero byte; whatever follows it inside the record is padding.
        int length = end - start;
        for (int i = 0; i < length; i++)
        {
            if (record[start + i] == 0)
            {
                length = i + 1;
                break;
            }
        }

        result.DataRunlist = record.Slice(start, length).ToArray();
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
