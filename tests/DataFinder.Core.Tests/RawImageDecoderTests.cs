using System.Buffers.Binary;
using DataFinder.Core.Preview.Raw;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class RawImageDecoderTests
{
    [Fact]
    public void ReadsOneBytePerPixelAsGreyLevels()
    {
        RawDecodeResult result = RawImageDecoder.Decode(new byte[] { 0, 128, 255, 64 }, Grey(2, 2));

        Assert.True(result.Succeeded);
        RawFrame frame = result.Frame!;
        Assert.Equal(RawPixelFormat.Gray8, frame.Format);
        Assert.Equal(new byte[] { 0, 128, 255, 64 }, frame.Pixels);
        Assert.Equal(2, frame.Stride);
    }

    [Fact]
    public void DropsTheRowsAndColumnsTheSchematicCrops()
    {
        var schema = Grey(3, 3);
        schema.Borders = new RawBorders(0, 1, 1, 0);

        RawDecodeResult result = RawImageDecoder.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, schema);

        Assert.Equal(new byte[] { 2, 3, 5, 6 }, result.Frame!.Pixels);
        Assert.Equal(2, result.Frame.Width);
        Assert.Equal(2, result.Frame.Height);
    }

    [Fact]
    public void LeavesSixteenBitFramesDarkWhenTheStretchIsOff()
    {
        var schema = Grey(2, 1);
        schema.DataType = RawDataType.U16;

        RawDecodeResult result = RawImageDecoder.Decode(SixteenBit(1000, 2000), schema);

        // 1000 of 65535 is nearly black, and 2000 is only twice that.
        Assert.Equal(new byte[] { 3, 7 }, result.Frame!.Pixels);
    }

    [Fact]
    public void StretchesSixteenBitFramesToTheFullRange()
    {
        var schema = Grey(10, 1);
        schema.DataType = RawDataType.U16;
        schema.Normalize = true;

        var values = new ushort[10];
        Array.Fill(values, (ushort)1000);
        values[9] = 2000;

        RawDecodeResult result = RawImageDecoder.Decode(SixteenBit(values), schema);

        Assert.Equal(0, result.Frame!.Pixels[0]);
        Assert.Equal(255, result.Frame.Pixels[9]);
    }

    [Fact]
    public void OnlyTheLowerFourteenBitsOfAFourteenBitFrameAreUsed()
    {
        var schema = Grey(1, 1);
        schema.DataType = RawDataType.U14InU16;

        // The top two bits of 0xFF03 are ignored, which leaves 0x3F03 of 0x3FFF.
        RawDecodeResult result = RawImageDecoder.Decode(SixteenBit(0xFF03), schema);

        Assert.Equal(251, result.Frame!.Pixels[0]);
    }

    [Fact]
    public void TakesThePackedEightBitPictureOutOfTheRightHandSide()
    {
        var schema = Grey(4, 1);
        schema.DataType = RawDataType.U16U8;

        RawDecodeResult result = RawImageDecoder.Decode(
            SixteenBit(0x1111, 0x2222, 0x1234, 0x5678),
            schema);

        // The left half is the 16 bit part and is not shown; the right half is two pixels per value,
        // low byte first.
        Assert.Equal(new byte[] { 0x34, 0x12, 0x78, 0x56 }, result.Frame!.Pixels);
        Assert.Equal(4, result.Frame.Width);
    }

    [Fact]
    public void CropsThePackedEightBitPictureInsideTheSixtyBitGrid()
    {
        var schema = Grey(4, 2);
        schema.DataType = RawDataType.U16U8;
        schema.Borders = new RawBorders(1, 0, 0, 0);

        RawDecodeResult result = RawImageDecoder.Decode(
            SixteenBit(0x0000, 0x0000, 0x1001, 0x2002, 0x0000, 0x0000, 0x3003, 0x4004),
            schema);

        Assert.Equal(new byte[] { 0x03, 0x30, 0x04, 0x40 }, result.Frame!.Pixels);
        Assert.Equal(1, result.Frame.Height);
    }

    [Fact]
    public void SaysSoWhenTheStretchIsSetOnALayoutThatIgnoresIt()
    {
        var schema = Grey(2, 1);
        schema.DataType = RawDataType.U16U8;
        schema.Normalize = true;

        RawDecodeResult result = RawImageDecoder.Decode(SixteenBit(0, 0, 0x0102, 0x0304), schema);

        Assert.NotNull(result.Warning);
        Assert.Contains("16 bit frames", result.Warning!, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsPackedColourBackToGrey()
    {
        var schema = Grey(2, 1);
        schema.DataType = RawDataType.YuvUyvy;

        // UYVY: colour first, then the two grey values. A colour of 128 is no colour at all.
        RawDecodeResult result = RawImageDecoder.Decode(new byte[] { 128, 200, 128, 50 }, schema);

        byte[] pixels = result.Frame!.Pixels;
        Assert.Equal(RawPixelFormat.Bgra32, result.Frame.Format);
        Assert.Equal(new byte[] { 200, 200, 200, 255 }, pixels[..4]);
        Assert.Equal(new byte[] { 50, 50, 50, 255 }, pixels[4..8]);
    }

    [Fact]
    public void ReadsPackedColourBackAsColour()
    {
        var schema = Grey(2, 1);
        schema.DataType = RawDataType.YuvUyvy;

        // A red cast: the colour pair pushes red up and blue down.
        RawDecodeResult result = RawImageDecoder.Decode(new byte[] { 128, 50, 200, 50 }, schema);

        byte[] pixel = result.Frame!.Pixels[..4];
        Assert.True(pixel[2] > pixel[0], "the red byte should be above the blue byte");
    }

    [Fact]
    public void DisregardsTheCropOfPackedColourAndSaysSo()
    {
        var schema = Grey(2, 2);
        schema.DataType = RawDataType.YuvUyvy;
        schema.Borders = new RawBorders(1, 0, 0, 0);

        RawDecodeResult result = RawImageDecoder.Decode(new byte[] { 128, 200, 128, 50, 128, 60, 128, 70 }, schema);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Frame!.Width);
        Assert.Equal(2, result.Frame.Height);
        Assert.Contains("crop", result.Warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PinsTheDarkestAndBrightestTwoPercentOfALongFrame()
    {
        var schema = Grey(100, 10);
        schema.DataType = RawDataType.U16;
        schema.Normalize = true;

        // A smooth ramp from 0 to 999, so roughly the first and last twenty samples are the ones
        // the stretch is meant to pin.
        var values = new ushort[1000];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (ushort)i;
        }

        RawDecodeResult result = RawImageDecoder.Decode(SixteenBit(values), schema);
        byte[] pixels = result.Frame!.Pixels;

        Assert.Equal(0, pixels[0]);
        Assert.Equal(255, pixels[^1]);

        int black = pixels.Count(pixel => pixel == 0);
        int white = pixels.Count(pixel => pixel == 255);

        // A couple more than the twenty samples the 2% says, because the last step of the stretch
        // drops the fraction of a grey level.
        Assert.InRange(black, 18, 26);
        Assert.InRange(white, 18, 26);

        for (int i = 1; i < pixels.Length; i++)
        {
            Assert.True(pixels[i] >= pixels[i - 1], "the ramp should stay in order");
        }
    }

    [Fact]
    public void DoesNotDivideByZeroWhenEveryPixelIsTheSame()
    {
        var schema = Grey(4, 1);
        schema.DataType = RawDataType.U16;
        schema.Normalize = true;

        RawDecodeResult result = RawImageDecoder.Decode(SixteenBit(700, 700, 700, 700), schema);

        Assert.Equal(new byte[] { 0, 0, 0, 0 }, result.Frame!.Pixels);
    }

    [Fact]
    public void ReplacesWhiteSpeckleWithTheNeighbouringSample()
    {
        var schema = Grey(3, 3);
        schema.RemoveWhite = true;

        var pixels = new byte[9];
        Array.Fill(pixels, (byte)10);
        pixels[4] = 255;

        RawDecodeResult result = RawImageDecoder.Decode(pixels, schema);

        Assert.Equal(10, result.Frame!.Pixels[4]);
    }

    [Fact]
    public void FailsWithAReasonWhenThereAreNotEnoughBytesForAFrame()
    {
        RawDecodeResult result = RawImageDecoder.Decode(new byte[3], Grey(2, 2));

        Assert.False(result.Succeeded);
        Assert.Contains("expects", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsWithAReasonWhenTheSchematicCannotWork()
    {
        var schema = Grey(2, 2);
        schema.Width = 0;

        RawDecodeResult result = RawImageDecoder.Decode(new byte[16], schema);

        Assert.False(result.Succeeded);
        Assert.Contains("width", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NeedsAnEvenWidthForPackedColour()
    {
        var schema = Grey(3, 1);
        schema.DataType = RawDataType.YuvUyvy;

        RawDecodeResult result = RawImageDecoder.Decode(new byte[6], schema);

        Assert.False(result.Succeeded);
        Assert.Contains("even", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    private static RawSchema Grey(int width, int height) => new()
    {
        Name = "test",
        Width = width,
        Height = height,
        DataType = RawDataType.U8,
    };

    private static byte[] SixteenBit(params ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), values[i]);
        }

        return bytes;
    }
}
