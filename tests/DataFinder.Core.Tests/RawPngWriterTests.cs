using System.Buffers.Binary;
using System.IO.Compression;
using DataFinder.Core.Preview.Raw;
using Xunit;

namespace DataFinder.Core.Tests;

public sealed class RawPngWriterTests
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public void WritesAGreyFrameOutAsPng()
    {
        var frame = new RawFrame
        {
            Width = 2,
            Height = 2,
            Format = RawPixelFormat.Gray8,
            Pixels = new byte[] { 0, 64, 128, 255 },
        };

        byte[] png = RawPngWriter.Encode(frame);

        Assert.Equal(Signature, png[..8]);
        Assert.Equal((2, 2, 8, 0), ReadHeader(png));
        Assert.Equal(new byte[] { 0, 64, 128, 255 }, ReadPixels(png, 2, 2, 1));
    }

    [Fact]
    public void WritesAColourFrameAsThreeBytesPerPixel()
    {
        var frame = new RawFrame
        {
            Width = 2,
            Height = 1,
            Format = RawPixelFormat.Bgra32,
            Pixels = new byte[] { 10, 20, 30, 255, 40, 50, 60, 255 },
        };

        byte[] png = RawPngWriter.Encode(frame);

        // Colour type 2 is truecolour, and the bytes come back red, green, blue.
        Assert.Equal((2, 1, 8, 2), ReadHeader(png));
        Assert.Equal(new byte[] { 30, 20, 10, 60, 50, 40 }, ReadPixels(png, 2, 1, 3));
    }

    [Fact]
    public void ShrinksAFrameToTheWidthAskedFor()
    {
        var frame = new RawFrame
        {
            Width = 8,
            Height = 8,
            Format = RawPixelFormat.Gray8,
            Pixels = Enumerable.Range(0, 64).Select(value => (byte)(value * 4)).ToArray(),
        };

        byte[] png = RawPngWriter.Encode(frame, maxWidth: 4);

        Assert.Equal((4, 4, 8, 0), ReadHeader(png));

        // Every pixel of the thumbnail is the average of the 2 by 2 block it stands for.
        byte[] pixels = ReadPixels(png, 4, 4, 1);
        Assert.Equal(EnumerateAverages(), pixels);
    }

    [Fact]
    public void LeavesAFrameNarrowerThanTheThumbnailAtItsOwnSize()
    {
        var frame = new RawFrame
        {
            Width = 4,
            Height = 2,
            Format = RawPixelFormat.Gray8,
            Pixels = new byte[8],
        };

        Assert.Equal((4, 2, 8, 0), ReadHeader(RawPngWriter.Encode(frame, maxWidth: 320)));
    }

    [Fact]
    public void RefusesAFrameWithNoPixels()
    {
        var frame = new RawFrame
        {
            Width = 0,
            Height = 0,
            Format = RawPixelFormat.Gray8,
            Pixels = Array.Empty<byte>(),
        };

        Assert.Throws<ArgumentException>(() => RawPngWriter.Encode(frame));
    }

    private static IEnumerable<byte> EnumerateAverages()
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                int total = 0;
                for (int y = row * 2; y < (row * 2) + 2; y++)
                {
                    for (int x = column * 2; x < (column * 2) + 2; x++)
                    {
                        total += ((y * 8) + x) * 4;
                    }
                }

                yield return (byte)((total + 2) / 4);
            }
        }
    }

    private static (int Width, int Height, int Depth, int ColourType) ReadHeader(byte[] png)
    {
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));

        return (
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)),
            png[24],
            png[25]);
    }

    /// <summary>Unpacks the pixels the way a browser would: unwrap the zlib stream, then drop the filter byte of each row.</summary>
    private static byte[] ReadPixels(byte[] png, int width, int height, int channels)
    {
        int offset = 8;
        var raw = new MemoryStream();

        while (offset < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);

            if (type == "IDAT")
            {
                using var compressed = new MemoryStream(png, offset + 8, length);
                using var deflate = new ZLibStream(compressed, CompressionMode.Decompress);
                deflate.CopyTo(raw);
            }

            offset += 12 + length;
        }

        byte[] bytes = raw.ToArray();
        var pixels = new byte[width * height * channels];
        int rowBytes = width * channels;

        for (int row = 0; row < height; row++)
        {
            Assert.Equal(0, bytes[row * (rowBytes + 1)]);
            Array.Copy(bytes, (row * (rowBytes + 1)) + 1, pixels, row * rowBytes, rowBytes);
        }

        return pixels;
    }
}
