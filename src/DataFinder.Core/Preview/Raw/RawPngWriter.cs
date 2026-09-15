using System.IO.Compression;

namespace DataFinder.Core.Preview.Raw;

/// <summary>
/// Writes a decoded frame out as a PNG, shrunk to fit a thumbnail. It is here rather than in the
/// window because a report has to be able to carry its own pictures, and the window cannot do that
/// without a screen.
/// </summary>
public static class RawPngWriter
{
    /// <summary>The widest a thumbnail gets. A frame narrower than this is left at its own size.</summary>
    public const int DefaultMaxWidth = 320;

    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>The frame as PNG bytes: grey stays grey, colour loses its unused fourth byte.</summary>
    public static byte[] Encode(RawFrame frame, int maxWidth = DefaultMaxWidth)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Width < 1 || frame.Height < 1 || frame.Pixels.Length == 0)
        {
            throw new ArgumentException("A frame with no pixels cannot be written out.", nameof(frame));
        }

        int channels = frame.Format == RawPixelFormat.Gray8 ? 1 : 3;
        (byte[] pixels, int width, int height) = Shrink(frame, channels, Math.Max(1, maxWidth));

        return Write(pixels, width, height, channels);
    }

    /// <summary>
    /// Averages the frame down to the width asked for. Averaging rather than dropping pixels matters
    /// for these pictures: a raw camera frame is mostly noise, and picking single pixels out of it
    /// makes a thumbnail of speckle.
    /// </summary>
    private static (byte[] Pixels, int Width, int Height) Shrink(RawFrame frame, int channels, int maxWidth)
    {
        double scale = frame.Width > maxWidth ? (double)maxWidth / frame.Width : 1d;
        int width = Math.Max(1, (int)Math.Round(frame.Width * scale));
        int height = Math.Max(1, (int)Math.Round(frame.Height * scale));

        if (width == frame.Width && height == frame.Height)
        {
            return (ToPacked(frame, channels), width, height);
        }

        var thumbnail = new byte[width * height * channels];

        for (int y = 0; y < height; y++)
        {
            int firstRow = (int)(y * frame.Height / (double)height);
            int lastRow = Math.Max(firstRow + 1, (int)((y + 1) * frame.Height / (double)height));

            for (int x = 0; x < width; x++)
            {
                int firstColumn = (int)(x * frame.Width / (double)width);
                int lastColumn = Math.Max(firstColumn + 1, (int)((x + 1) * frame.Width / (double)width));
                int samples = (lastRow - firstRow) * (lastColumn - firstColumn);

                int blue = 0;
                int green = 0;
                int red = 0;

                for (int row = firstRow; row < lastRow; row++)
                {
                    for (int column = firstColumn; column < lastColumn; column++)
                    {
                        if (channels == 1)
                        {
                            green += frame.Pixels[(row * frame.Width) + column];
                            continue;
                        }

                        int source = ((row * frame.Width) + column) * 4;
                        blue += frame.Pixels[source];
                        green += frame.Pixels[source + 1];
                        red += frame.Pixels[source + 2];
                    }
                }

                int offset = ((y * width) + x) * channels;
                if (channels == 1)
                {
                    thumbnail[offset] = (byte)((green + (samples / 2)) / samples);
                    continue;
                }

                thumbnail[offset] = (byte)((red + (samples / 2)) / samples);
                thumbnail[offset + 1] = (byte)((green + (samples / 2)) / samples);
                thumbnail[offset + 2] = (byte)((blue + (samples / 2)) / samples);
            }
        }

        return (thumbnail, width, height);
    }

    /// <summary>Grey is already a byte per pixel; the fourth byte of a colour frame is dropped.</summary>
    private static byte[] ToPacked(RawFrame frame, int channels)
    {
        if (channels == 1)
        {
            return frame.Pixels;
        }

        var rgb = new byte[frame.Width * frame.Height * 3];
        for (int i = 0, source = 0, target = 0; i < frame.Width * frame.Height; i++, source += 4, target += 3)
        {
            rgb[target] = frame.Pixels[source + 2];
            rgb[target + 1] = frame.Pixels[source + 1];
            rgb[target + 2] = frame.Pixels[source];
        }

        return rgb;
    }

    private static byte[] Write(byte[] pixels, int width, int height, int channels)
    {
        int rowBytes = width * channels;
        var raw = new byte[(rowBytes + 1) * height];

        // Every row is written as it is; the filter byte in front of it says "no filter".
        for (int row = 0; row < height; row++)
        {
            Buffer.BlockCopy(pixels, row * rowBytes, raw, (row * (rowBytes + 1)) + 1, rowBytes);
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        var png = new MemoryStream();
        png.Write(Signature);

        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;
        header[9] = channels == 1 ? (byte)0 : (byte)2;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", Array.Empty<byte>());

        return png.ToArray();
    }

    private static void WriteChunk(Stream target, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        target.Write(length);

        byte[] name = System.Text.Encoding.ASCII.GetBytes(type);
        target.Write(name);
        target.Write(data);

        var crc = new byte[4];
        WriteBigEndian(crc, 0, (int)Crc32(name, data));
        target.Write(crc);
    }

    private static void WriteBigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte value in first)
        {
            crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
        }

        foreach (byte value in second)
        {
            crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
        }

        return ~crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
