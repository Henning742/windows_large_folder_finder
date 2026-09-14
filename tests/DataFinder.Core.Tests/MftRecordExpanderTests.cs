using DataFinder.Core.Ntfs;
using DataFinder.Core.Tests.Support;
using Xunit;

namespace DataFinder.Core.Tests;

/// <summary>
/// Covers the case that made folders go missing: a base record carries an <c>$ATTRIBUTE_LIST</c>
/// and the interesting attributes (<c>$DATA</c>, <c>$FILE_NAME</c>) live in extension records.
/// </summary>
public sealed class MftRecordExpanderTests
{
    private const int BytesPerSector = MftRecordBuilder.SectorSize;

    private sealed class FakeRecordSource : IMftRecordSource
    {
        private readonly Dictionary<uint, byte[]> _records = new();

        public FakeRecordSource(params (uint RecordNumber, byte[] Bytes)[] records)
        {
            foreach ((uint recordNumber, byte[] bytes) in records)
            {
                _records[recordNumber] = bytes;
            }
        }

        public bool TryReadRecord(uint recordNumber, byte[] destination)
        {
            if (!_records.TryGetValue(recordNumber, out byte[]? bytes))
            {
                return false;
            }

            bytes.CopyTo(destination, 0);
            return true;
        }
    }

    private static MftRecordParseResult Parse(byte[] record, uint recordNumber, bool captureDataRunlist = false) =>
        new MftRecordParser().Parse(record, BytesPerSector, recordNumber, captureDataRunlist)!;

    [Fact]
    public void PicksUpTheDataSizeFromAnExtensionRecord()
    {
        byte[] baseRecord = new MftRecordBuilder(100, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "big.bin", size: 0, isDirectory: false)
            .AddAttributeListAttribute((0x80, 0, 101))
            .Build();

        byte[] extension = new MftRecordBuilder(101, isDirectory: false)
            .AsExtensionOf(100)
            .AddNonResidentDataAttribute(size: 500_000_000)
            .Build();

        MftRecordParseResult result = Parse(baseRecord, 100);

        // Before resolving: the $DATA attribute is invisible, so the size looks like zero.
        Assert.False(result.HasData);
        Assert.Equal(0, result.DataSize);

        var expander = new MftRecordExpander(new FakeRecordSource((101, extension)), BytesPerSector, MftRecordBuilder.RecordSize);
        Assert.True(expander.Expand(result));

        Assert.True(result.HasData);
        Assert.Equal(500_000_000, result.DataSize);
        Assert.Equal("big.bin", Assert.Single(result.Links).Name);
    }

    [Fact]
    public void PicksUpTheFileNameFromAnExtensionRecord()
    {
        byte[] baseRecord = new MftRecordBuilder(200, isDirectory: false)
            .AddNonResidentDataAttribute(size: 1024)
            .AddAttributeListAttribute((0x30, 0, 201))
            .Build();

        byte[] extension = new MftRecordBuilder(201, isDirectory: false)
            .AsExtensionOf(200)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "spilled.txt", size: 1024, isDirectory: false)
            .Build();

        MftRecordParseResult result = Parse(baseRecord, 200);
        Assert.Empty(result.Links);

        var expander = new MftRecordExpander(new FakeRecordSource((201, extension)), BytesPerSector, MftRecordBuilder.RecordSize);
        Assert.True(expander.Expand(result));

        FileNameLink link = Assert.Single(result.Links);
        Assert.Equal("spilled.txt", link.Name);
        // The name is attached to the base record, not to the extension record.
        Assert.Equal(200u, link.RecordNumber);
    }

    [Fact]
    public void KeepsADirectoryMarkedAsADirectoryWhenTheNameIsInAnExtension()
    {
        byte[] baseRecord = new MftRecordBuilder(300, isDirectory: true)
            .AddAttributeListAttribute((0x30, 0, 301))
            .Build();

        byte[] extension = new MftRecordBuilder(301, isDirectory: true)
            .AsExtensionOf(300)
            // The $FILE_NAME attribute itself does not carry the directory flag in this case.
            .AddFileNameAttribute(parentRecordNumber: 5, name: "spilled-folder", size: 0, isDirectory: false)
            .Build();

        MftRecordParseResult result = Parse(baseRecord, 300);

        var expander = new MftRecordExpander(new FakeRecordSource((301, extension)), BytesPerSector, MftRecordBuilder.RecordSize);
        Assert.True(expander.Expand(result));

        FileNameLink link = Assert.Single(result.Links);
        Assert.True(link.IsDirectory);
        Assert.Equal(300u, link.RecordNumber);
    }

    [Fact]
    public void FollowsTheListThroughAnIntermediateExtensionRecord()
    {
        byte[] baseRecord = new MftRecordBuilder(400, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "chain.bin", size: 0, isDirectory: false)
            .AddAttributeListAttribute((0x80, 0, 401))
            .Build();

        // The middle record only points on to the record that really holds the data.
        byte[] middle = new MftRecordBuilder(401, isDirectory: false)
            .AsExtensionOf(400)
            .AddAttributeListAttribute((0x80, 0, 402))
            .Build();

        byte[] last = new MftRecordBuilder(402, isDirectory: false)
            .AsExtensionOf(400)
            .AddNonResidentDataAttribute(size: 123_456)
            .Build();

        MftRecordParseResult result = Parse(baseRecord, 400);

        var expander = new MftRecordExpander(
            new FakeRecordSource((401, middle), (402, last)),
            BytesPerSector,
            MftRecordBuilder.RecordSize);

        Assert.True(expander.Expand(result));
        Assert.True(result.HasData);
        Assert.Equal(123_456, result.DataSize);
    }

    [Fact]
    public void TerminatesOnACyclicAttributeList()
    {
        byte[] baseRecord = new MftRecordBuilder(500, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "cyclic.bin", size: 0, isDirectory: false)
            .AddAttributeListAttribute((0x80, 0, 501))
            .Build();

        // 501 points back at the base record, which would loop forever without cycle protection.
        byte[] extension = new MftRecordBuilder(501, isDirectory: false)
            .AsExtensionOf(500)
            .AddNonResidentDataAttribute(size: 2048)
            .AddAttributeListAttribute((0x80, 0, 500))
            .Build();

        MftRecordParseResult result = Parse(baseRecord, 500);

        var expander = new MftRecordExpander(new FakeRecordSource((501, extension)), BytesPerSector, MftRecordBuilder.RecordSize);
        Assert.True(expander.Expand(result));
        Assert.Equal(2048, result.DataSize);
    }

    [Fact]
    public void ReportsAnIncompleteListWhenAnExtensionRecordIsMissing()
    {
        byte[] baseRecord = new MftRecordBuilder(600, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "broken.bin", size: 0, isDirectory: false)
            .AddAttributeListAttribute((0x80, 0, 999))
            .Build();

        MftRecordParseResult result = Parse(baseRecord, 600);

        var expander = new MftRecordExpander(new FakeRecordSource(), BytesPerSector, MftRecordBuilder.RecordSize);

        Assert.False(expander.Expand(result));
        Assert.False(result.HasData);
    }

    [Fact]
    public void CollectsTheDataRunExtentsOfAFragmentedAttribute()
    {
        byte[] baseRecord = new MftRecordBuilder(0, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "$MFT", size: 4096, isDirectory: false)
            .AddNonResidentDataAttribute(size: 4096, runList: new byte[] { 0x11, 0x04, 0x10, 0x00 })
            .AddAttributeListAttribute((0x80, 0, 701))
            .Build();

        byte[] extension = new MftRecordBuilder(701, isDirectory: false)
            .AsExtensionOf(0)
            .AddNonResidentDataAttribute(size: 4096, runList: new byte[] { 0x11, 0x02, 0x20, 0x00 }, lowestVcn: 4)
            .Build();

        MftRecordParseResult result = Parse(baseRecord, 0, captureDataRunlist: true);

        var expander = new MftRecordExpander(
            new FakeRecordSource((701, extension)),
            BytesPerSector,
            MftRecordBuilder.RecordSize,
            captureDataRunlists: true);

        Assert.True(expander.Expand(result));

        List<DataRun> runs = Runlist.DecodeExtents(result.DataExtents);
        Assert.Equal(2, runs.Count);
        Assert.Equal(new DataRun(0, 4, 0x10), runs[0]);
        Assert.Equal(new DataRun(4, 2, 0x20), runs[1]);
    }
}
