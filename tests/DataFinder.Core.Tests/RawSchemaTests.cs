using DataFinder.Core.Preview.Raw;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class RawSchemaTests
{
    [Fact]
    public void CountsTheBytesOfAFrame()
    {
        var schema = new RawSchema { Width = 640, Height = 512, HeaderLength = 64, DataType = RawDataType.U16 };

        Assert.Equal(655_360, schema.PayloadBytes);
        Assert.Equal(655_424, schema.FrameBytes);
    }

    [Fact]
    public void CountsThreeBytesPerPixelForColour()
    {
        var schema = new RawSchema { Width = 10, Height = 10, DataType = RawDataType.U8Rgb };

        Assert.Equal(300, schema.PayloadBytes);
    }

    [Fact]
    public void AcceptsTheSchematicsTheAppShipsWith()
    {
        foreach (RawSchema schema in BuiltInRawSchemas.Create())
        {
            Assert.Null(schema.Validate());
        }
    }

    [Fact]
    public void HandsOutAFreshCopyOfTheBuiltInSchematics()
    {
        IReadOnlyList<RawSchema> first = BuiltInRawSchemas.Create();
        IReadOnlyList<RawSchema> second = BuiltInRawSchemas.Create();

        Assert.NotSame(first[0], second[0]);
        Assert.Equal(first.Count, second.Count);
    }

    [Fact]
    public void ShipsOneSchematicPerKindOfRecording()
    {
        IReadOnlyList<RawSchema> schemas = BuiltInRawSchemas.Create();

        Assert.Equal(6, schemas.Count);

        // Every recording the reference scripts were written for, and no second size of any of them.
        var shapes = schemas.Select(schema => $"{schema.Name} {schema.Width}x{schema.Height}").ToList();
        Assert.Equal(shapes.Count, shapes.Distinct().Count());

        Assert.Contains(schemas, schema => schema.DataType == RawDataType.U8);
        Assert.Contains(schemas, schema => schema.DataType == RawDataType.U16);
        Assert.Contains(schemas, schema => schema.DataType == RawDataType.U14InU16);
        Assert.Contains(schemas, schema => schema.DataType == RawDataType.U16U8);
        Assert.Contains(schemas, schema => schema.DataType == RawDataType.YuvUyvy);
    }

    [Theory]
    [InlineData(0, 512, "width")]
    [InlineData(640, 0, "height")]
    public void RejectsAFrameWithNoPixels(int width, int height, string expected)
    {
        string? problem = new RawSchema { Width = width, Height = height }.Validate();

        Assert.NotNull(problem);
        Assert.Contains(expected, problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAFrameThatWouldBeEnormous()
    {
        string? problem = new RawSchema { Width = 32768, Height = 32768 }.Validate();

        Assert.Contains("pixels", problem!, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsACropThatLeavesNothing()
    {
        var schema = new RawSchema { Width = 10, Height = 10, Borders = new RawBorders(5, 5, 0, 0) };

        Assert.Contains("leaves nothing", schema.Validate()!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void RejectsAPackedSplitOutsideTheFrame(int splitColumn)
    {
        var schema = new RawSchema { Width = 10, Height = 4, DataType = RawDataType.U16U8, SplitColumn = splitColumn };

        Assert.NotNull(schema.Validate());
    }

    [Fact]
    public void PutsThePackedSplitInTheMiddleUnlessToldOtherwise()
    {
        var schema = new RawSchema { Width = 960, Height = 4, DataType = RawDataType.U16U8 };

        Assert.Null(schema.Validate());
        Assert.Contains("column 480", schema.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsANegativeFrameNumber()
    {
        Assert.NotNull(new RawSchema { FrameIndex = -1 }.Validate());
    }

    [Fact]
    public void RejectsASchematicWithNoName()
    {
        Assert.NotNull(new RawSchema { Name = "  " }.Validate());
    }

    [Fact]
    public void CopiesEverySettingWhenCloned()
    {
        var schema = new RawSchema
        {
            Name = "wide",
            Width = 960,
            Height = 514,
            HeaderLength = 64,
            DataType = RawDataType.U16U8,
            Borders = new RawBorders(1, 2, 3, 4),
            SplitColumn = 700,
            FrameIndex = 3,
        };

        RawSchema copy = schema.Clone();
        copy.Name = "changed";
        copy.Borders = RawBorders.None;

        Assert.Equal("wide", schema.Name);
        Assert.Equal(new RawBorders(1, 2, 3, 4), schema.Borders);
        Assert.Equal(960, copy.Width);
        Assert.Equal(64, copy.HeaderLength);
        Assert.Equal(RawDataType.U16U8, copy.DataType);
        Assert.Equal(700, copy.SplitColumn);
        Assert.Equal(3, copy.FrameIndex);
    }

    [Fact]
    public void DescribesWhatItDoesInOneLine()
    {
        var schema = new RawSchema
        {
            Name = "wide",
            Width = 960,
            Height = 514,
            HeaderLength = 64,
            DataType = RawDataType.U16U8,
            Borders = new RawBorders(1, 1, 0, 0),
        };

        string text = schema.Describe();

        Assert.Contains("960 x 514", text, StringComparison.Ordinal);
        Assert.Contains("16 bit + packed 8 bit", text, StringComparison.Ordinal);
        Assert.Contains("64 byte header", text, StringComparison.Ordinal);
        Assert.Contains("crop 1,1,0,0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheGreyLayoutsCanBeStretched()
    {
        Assert.True(RawDataTypes.CanStretch(RawDataType.U8));
        Assert.True(RawDataTypes.CanStretch(RawDataType.U16));
        Assert.True(RawDataTypes.CanStretch(RawDataType.U14InU16));
        Assert.True(RawDataTypes.CanStretch(RawDataType.U16U8));

        Assert.False(RawDataTypes.CanStretch(RawDataType.U8Rgb));
        Assert.False(RawDataTypes.CanStretch(RawDataType.YuvUyvy));
    }

    [Fact]
    public void SixteenBitDataStartsStretchedAndEightBitDataDoesNot()
    {
        Assert.True(RawDataTypes.StretchesByDefault(RawDataType.U16));
        Assert.True(RawDataTypes.StretchesByDefault(RawDataType.U14InU16));

        Assert.False(RawDataTypes.StretchesByDefault(RawDataType.U8));
        Assert.False(RawDataTypes.StretchesByDefault(RawDataType.U16U8));
        Assert.False(RawDataTypes.StretchesByDefault(RawDataType.U8Rgb));
        Assert.False(RawDataTypes.StretchesByDefault(RawDataType.YuvUyvy));
    }

    [Fact]
    public void CountsTwoBytesPerPixelForEveryPackedLayout()
    {
        Assert.Equal(2, new RawSchema { DataType = RawDataType.U16U8 }.BytesPerPixel);
        Assert.Equal(2, new RawSchema { DataType = RawDataType.YuvUyvy }.BytesPerPixel);
        Assert.Equal(1, new RawSchema { DataType = RawDataType.U8 }.BytesPerPixel);
    }
}
