using DataFinder.Core.Ntfs;
using DataFinder.Core.Tests.Support;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class MftRecordParserTests
{
    private const int BytesPerSector = MftRecordBuilder.SectorSize;

    [Fact]
    public void ParsesFileNameAndSizeOfAFile()
    {
        byte[] record = new MftRecordBuilder(42, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "photo.jpg", size: 1234, isDirectory: false)
            .AddNonResidentDataAttribute(size: 1234)
            .Build();

        var parser = new MftRecordParser();
        MftRecordParseResult? result = parser.Parse(record, BytesPerSector, 42);

        Assert.NotNull(result);
        Assert.True(result!.InUse);
        Assert.False(result.IsDirectory);
        Assert.False(result.IsCorrupt);
        Assert.True(result.HasData);
        Assert.Equal(1234, result.DataSize);

        FileNameLink link = Assert.Single(result.Links);
        Assert.Equal("photo.jpg", link.Name);
        Assert.Equal(5u, link.ParentRecordNumber);
        Assert.Equal(42u, link.RecordNumber);
        Assert.False(link.IsDirectory);
    }

    [Fact]
    public void ParsesDirectoryRecords()
    {
        byte[] record = new MftRecordBuilder(77, isDirectory: true)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "dataset", size: 0, isDirectory: true)
            .Build();

        MftRecordParseResult? result = new MftRecordParser().Parse(record, BytesPerSector, 77);

        Assert.NotNull(result);
        Assert.True(result!.IsDirectory);
        Assert.True(Assert.Single(result.Links).IsDirectory);
    }

    [Fact]
    public void FallsBackToTheFileNameSizeWhenThereIsNoDataAttribute()
    {
        byte[] record = new MftRecordBuilder(90, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "tiny.txt", size: 512, isDirectory: false)
            .Build();

        MftRecordParseResult? result = new MftRecordParser().Parse(record, BytesPerSector, 90);

        Assert.NotNull(result);
        Assert.False(result!.HasData);
        Assert.Equal(512, Assert.Single(result.Links).FileNameSize);
    }

    [Fact]
    public void DropsDosStyleNamesWhenALongNameExists()
    {
        byte[] record = new MftRecordBuilder(55, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "LongFileName.txt", size: 10, isDirectory: false, nameNamespace: 1)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "LONGFI~1.TXT", size: 10, isDirectory: false, nameNamespace: 2)
            .Build();

        MftRecordParseResult? result = new MftRecordParser().Parse(record, BytesPerSector, 55);

        Assert.NotNull(result);
        Assert.Equal("LongFileName.txt", Assert.Single(result!.Links).Name);
    }

    [Fact]
    public void KeepsBothHardLinkNames()
    {
        byte[] record = new MftRecordBuilder(60, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "first.txt", size: 10, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 12, name: "second.txt", size: 10, isDirectory: false)
            .Build();

        MftRecordParseResult? result = new MftRecordParser().Parse(record, BytesPerSector, 60);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Links.Count);
        Assert.Contains(result.Links, link => link.ParentRecordNumber == 12 && link.Name == "second.txt");
    }

    [Fact]
    public void RecordsThatAreNotInUseAreReportedAsUnused()
    {
        byte[] record = new MftRecordBuilder(70, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "deleted.txt", size: 10, isDirectory: false)
            .Build();

        // Clear the "in use" flag of the record header.
        record[0x16] &= 0xFE;

        MftRecordParseResult? result = new MftRecordParser().Parse(record, BytesPerSector, 70);

        Assert.NotNull(result);
        Assert.False(result!.InUse);
    }

    [Fact]
    public void RejectsGarbage()
    {
        byte[] record = new byte[MftRecordBuilder.RecordSize];

        Assert.Null(new MftRecordParser().Parse(record, BytesPerSector, 1));
    }

    [Fact]
    public void RejectsRecordsWhoseUpdateSequenceNumberDoesNotMatch()
    {
        byte[] record = new MftRecordBuilder(80, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "file.txt", size: 10, isDirectory: false)
            .Build();

        record[MftRecordBuilder.SectorSize - 1] = 0xAB;

        Assert.Null(new MftRecordParser().Parse(record, BytesPerSector, 80));
    }

    [Fact]
    public void CapturesTheDataRunListOfTheMftRecord()
    {
        byte[] runList = { 0x11, 0x04, 0x10, 0x00 };
        byte[] record = new MftRecordBuilder(0, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "$MFT", size: 4096, isDirectory: false)
            .AddNonResidentDataAttribute(size: 4096, runList: runList)
            .Build();

        MftRecordParseResult? result = new MftRecordParser().Parse(record, BytesPerSector, 0, captureDataRunlist: true);

        Assert.NotNull(result);
        DataRunExtent extent = Assert.Single(result!.DataExtents);
        Assert.Equal(0, extent.LowestVcn);
        Assert.Equal(runList, extent.Runlist);

        List<DataRun> runs = Runlist.Decode(extent.Runlist);
        DataRun run = Assert.Single(runs);
        Assert.Equal(0, run.StartVcn);
        Assert.Equal(4, run.ClusterCount);
        Assert.Equal(0x10, run.StartLcn);
    }

    [Fact]
    public void CapturesARunListThatContainsZeroBytes()
    {
        // Cluster 0x4000 and the delta to cluster 0x8000 both contain a 0x00 byte. Scanning for the
        // first zero byte used to stop the capture early and hand the reader a truncated run list,
        // so the tail of the master file table could never be read.
        byte[] runList = { 0x21, 0x64, 0x00, 0x40, 0x21, 0x64, 0x00, 0x40, 0x00 };

        byte[] record = new MftRecordBuilder(0, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "$MFT", size: 4096, isDirectory: false)
            .AddNonResidentDataAttribute(size: 4096, runList: runList)
            .Build();

        MftRecordParseResult? result = new MftRecordParser().Parse(record, BytesPerSector, 0, captureDataRunlist: true);

        Assert.NotNull(result);
        DataRunExtent extent = Assert.Single(result!.DataExtents);
        Assert.Equal(runList, extent.Runlist);

        List<DataRun> runs = Runlist.Decode(extent.Runlist);
        Assert.Equal(2, runs.Count);
        Assert.Equal(new DataRun(0, 0x64, 0x4000), runs[0]);
        Assert.Equal(new DataRun(0x64, 0x64, 0x8000), runs[1]);
    }

    [Fact]
    public void CapturesAttributeListEntries()
    {
        byte[] record = new MftRecordBuilder(42, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "split.bin", size: 0, isDirectory: false)
            .AddAttributeListAttribute((0x80, 0, 777))
            .Build();

        MftRecordParseResult? result = new MftRecordParser().Parse(record, BytesPerSector, 42);

        Assert.NotNull(result);
        Assert.True(result!.HasAttributeList);
        AttributeListEntry entry = Assert.Single(result.AttributeList);
        Assert.Equal(0x80u, entry.AttributeType);
        Assert.Equal(0, entry.LowestVcn);
        Assert.Equal(777u, entry.RecordNumber);
    }

    [Fact]
    public void ReportsTheRecordAsAnExtensionWhenItPointsAtABaseRecord()
    {
        byte[] record = new MftRecordBuilder(900, isDirectory: false)
            .AsExtensionOf(100)
            .AddNonResidentDataAttribute(size: 4096)
            .Build();

        MftRecordParseResult? result = new MftRecordParser().Parse(record, BytesPerSector, 900);

        Assert.NotNull(result);
        Assert.Equal(100u, result!.BaseRecordNumber);
        Assert.True(result.IsExtensionRecord);
    }

    [Fact]
    public void BaseRecordsAreNotReportedAsExtensions()
    {
        byte[] record = new MftRecordBuilder(42, isDirectory: false)
            .AddFileNameAttribute(parentRecordNumber: 5, name: "photo.jpg", size: 1234, isDirectory: false)
            .Build();

        MftRecordParseResult? result = new MftRecordParser().Parse(record, BytesPerSector, 42);

        Assert.NotNull(result);
        Assert.Equal(0u, result!.BaseRecordNumber);
        Assert.False(result.IsExtensionRecord);
    }
}
