using System.Buffers.Binary;
using System.Text;

namespace DataFinder.Core.Tests.Support;

/// <summary>
/// Builds a synthetic but structurally correct NTFS master file table record, so the parser can
/// be tested without access to a real volume.
/// </summary>
internal sealed class MftRecordBuilder
{
    public const int RecordSize = 1024;
    public const int SectorSize = 512;

    private const int UpdateSequenceOffset = 0x30;
    private const int FirstAttributeOffset = 0x38;
    private const uint AttributeEnd = 0xFFFFFFFF;
    private const uint AttributeAttributeList = 0x20;
    private const uint AttributeFileName = 0x30;
    private const uint AttributeData = 0x80;

    private readonly byte[] _buffer = new byte[RecordSize];
    private int _attributeOffset = FirstAttributeOffset;

    public MftRecordBuilder(uint recordNumber, bool isDirectory)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(0x00, 4), 0x454C4946); // "FILE"
        WriteUInt16(0x04, UpdateSequenceOffset);
        WriteUInt16(0x06, 3);
        WriteUInt16(0x14, FirstAttributeOffset);
        WriteUInt16(0x16, (ushort)(isDirectory ? 0x0003 : 0x0001));
        WriteUInt32(0x1C, RecordSize);
        WriteUInt32(0x2C, recordNumber);

        // Update sequence array: the number plus the original bytes of both sector ends.
        WriteUInt16(UpdateSequenceOffset, 0x0001);
        WriteUInt16(UpdateSequenceOffset + 2, 0x0000);
        WriteUInt16(UpdateSequenceOffset + 4, 0x0000);
    }

    public MftRecordBuilder AddFileNameAttribute(uint parentRecordNumber, string name, long size, bool isDirectory, int nameNamespace = 1)
    {
        int nameBytes = name.Length * 2;
        int contentLength = 0x42 + nameBytes;
        int attributeLength = Align8(0x18 + contentLength);
        int start = _attributeOffset;

        WriteUInt32(start + 0x00, AttributeFileName);
        WriteUInt32(start + 0x04, (uint)attributeLength);
        _buffer[start + 0x08] = 0; // resident
        _buffer[start + 0x09] = 0; // unnamed attribute
        WriteUInt16(start + 0x0A, 0);
        WriteUInt16(start + 0x0C, 0);
        WriteUInt16(start + 0x0E, 0);
        WriteUInt32(start + 0x10, (uint)contentLength);
        WriteUInt16(start + 0x14, 0x18);

        int content = start + 0x18;
        WriteUInt64(content + 0x00, parentRecordNumber);
        WriteInt64(content + 0x30, size);
        WriteUInt32(content + 0x38, isDirectory ? 0x10000000u : 0x00000080u);
        _buffer[content + 0x40] = (byte)name.Length;
        _buffer[content + 0x41] = (byte)nameNamespace;
        Encoding.Unicode.GetBytes(name, 0, name.Length, _buffer, content + 0x42);

        _attributeOffset += attributeLength;
        return this;
    }

    public MftRecordBuilder AddNonResidentDataAttribute(long size, byte[]? runList = null, long lowestVcn = 0)
    {
        byte[] runs = runList ?? new byte[] { 0x11, 0x01, 0x00, 0x00 };
        const int runListOffset = 0x40;
        int attributeLength = Align8(runListOffset + runs.Length);
        int start = _attributeOffset;

        WriteUInt32(start + 0x00, AttributeData);
        WriteUInt32(start + 0x04, (uint)attributeLength);
        _buffer[start + 0x08] = 1; // non resident
        _buffer[start + 0x09] = 0; // unnamed
        WriteUInt16(start + 0x0A, 0);
        WriteUInt16(start + 0x0C, 0);
        WriteUInt16(start + 0x0E, 0);
        WriteInt64(start + 0x10, lowestVcn); // first VCN of this extent
        WriteInt64(start + 0x18, 0);        // last VCN
        WriteUInt16(start + 0x20, runListOffset);
        WriteInt64(start + 0x28, size);     // allocated size
        WriteInt64(start + 0x30, size);     // real size
        WriteInt64(start + 0x38, size);     // initialized size
        runs.CopyTo(_buffer, start + runListOffset);

        _attributeOffset += attributeLength;
        return this;
    }

    /// <summary>
    /// Marks this record as an extension of another record, as NTFS does when attributes spill over:
    /// the record points at its base and its in-use flag is left clear, so nothing counts it as a
    /// file of its own.
    /// </summary>
    public MftRecordBuilder AsExtensionOf(uint baseRecordNumber)
    {
        WriteUInt64(0x20, baseRecordNumber);
        WriteUInt16(0x16, 0x0000);
        return this;
    }

    /// <summary>
    /// Adds a resident <c>$ATTRIBUTE_LIST</c> naming where each attribute really lives. Every entry
    /// is an unnamed attribute, which is the common case for split <c>$DATA</c> and <c>$FILE_NAME</c>.
    /// </summary>
    public MftRecordBuilder AddAttributeListAttribute(params (uint AttributeType, long LowestVcn, uint RecordNumber)[] entries)
    {
        const int entryLength = 0x18;
        int contentLength = entryLength * entries.Length;
        int attributeLength = Align8(0x18 + contentLength);
        int start = _attributeOffset;

        WriteUInt32(start + 0x00, AttributeAttributeList);
        WriteUInt32(start + 0x04, (uint)attributeLength);
        _buffer[start + 0x08] = 0; // resident
        _buffer[start + 0x09] = 0; // unnamed
        WriteUInt16(start + 0x0A, 0);
        WriteUInt16(start + 0x0C, 0);
        WriteUInt16(start + 0x0E, 0);
        WriteUInt32(start + 0x10, (uint)contentLength);
        WriteUInt16(start + 0x14, 0x18);

        int position = start + 0x18;
        foreach ((uint attributeType, long lowestVcn, uint recordNumber) in entries)
        {
            WriteUInt32(position + 0x00, attributeType);
            WriteUInt16(position + 0x04, entryLength);
            _buffer[position + 0x06] = 0; // name length
            _buffer[position + 0x07] = 0; // name offset
            WriteInt64(position + 0x08, lowestVcn);
            WriteUInt64(position + 0x10, recordNumber);
            position += entryLength;
        }

        _attributeOffset += attributeLength;
        return this;
    }

    /// <summary>
    /// Adds an <c>$ATTRIBUTE_LIST</c> that does not fit in the record, so its entries live in the
    /// attribute's own data runs - the layout of a very fragmented table.
    /// </summary>
    public MftRecordBuilder AddNonResidentAttributeListAttribute(long size, byte[] runList)
    {
        const int runListOffset = 0x40;
        int attributeLength = Align8(runListOffset + runList.Length);
        int start = _attributeOffset;

        WriteUInt32(start + 0x00, AttributeAttributeList);
        WriteUInt32(start + 0x04, (uint)attributeLength);
        _buffer[start + 0x08] = 1; // non resident
        _buffer[start + 0x09] = 0; // unnamed
        WriteUInt16(start + 0x0A, 0);
        WriteUInt16(start + 0x0C, 0);
        WriteUInt16(start + 0x0E, 0);
        WriteInt64(start + 0x10, 0);        // first VCN
        WriteInt64(start + 0x18, 0);        // last VCN
        WriteUInt16(start + 0x20, runListOffset);
        WriteInt64(start + 0x28, size);     // allocated size
        WriteInt64(start + 0x30, size);     // real size
        WriteInt64(start + 0x38, size);     // initialized size
        runList.CopyTo(_buffer, start + runListOffset);

        _attributeOffset += attributeLength;
        return this;
    }

    public byte[] Build()
    {
        WriteUInt32(_attributeOffset, AttributeEnd);
        _attributeOffset += 4;
        WriteUInt32(0x18, (uint)Align8(_attributeOffset));

        // Stamp the sector ends with the update sequence number, as NTFS does on write.
        WriteUInt16(SectorSize - 2, 0x0001);
        WriteUInt16(RecordSize - 2, 0x0001);
        return _buffer;
    }

    private static int Align8(int value) => (value + 7) / 8 * 8;

    private void WriteUInt16(int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(offset, 2), value);

    private void WriteUInt32(int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(offset, 4), value);

    private void WriteUInt64(int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(offset, 8), value);

    private void WriteInt64(int offset, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(offset, 8), value);
}
