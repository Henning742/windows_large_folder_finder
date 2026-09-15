using System.Buffers.Binary;
using DataFinder.Core.Util;

namespace DataFinder.Core.Preview.Raw;

/// <summary>
/// Turns the bytes of one frame into pixels the window can draw. It is the same set of steps the
/// reference Python script takes - skip the header, read the values, crop, stretch, convert the
/// colours - written once and driven by a <see cref="RawSchema"/> instead of being spelled out per
/// file.
/// </summary>
public static class RawImageDecoder
{
    /// <summary>The share of pixels that decides the stretch: 2% go black, 98% go white.</summary>
    private const double LowPercentile = 2d;
    private const double HighPercentile = 98d;

    /// <summary>
    /// Reads one whole frame. <paramref name="frameBytes"/> has to hold the frame's pixels, so it is
    /// either what <see cref="RawFrameReader"/> read or the same bytes from somewhere else.
    /// <paramref name="stretch"/> pins the darkest 2% of the frame to black and the brightest 2% to
    /// white - see <see cref="RawDataTypes.StretchesByDefault"/> for what a layout is shown with
    /// when nobody has an opinion.
    /// </summary>
    public static RawDecodeResult Decode(byte[] frameBytes, RawSchema schema, bool stretch)
    {
        ArgumentNullException.ThrowIfNull(frameBytes);
        ArgumentNullException.ThrowIfNull(schema);

        if (schema.Validate() is { } problem)
        {
            return RawDecodeResult.Failure(problem);
        }

        int needed = schema.PayloadBytes;
        if (frameBytes.Length < needed)
        {
            return RawDecodeResult.Failure(
                $"'{schema.Name}' expects {ByteSize.Format(needed)} of pixels per frame, but only " +
                $"{ByteSize.Format(frameBytes.Length)} was there to read.");
        }

        return schema.DataType switch
        {
            RawDataType.U8 => DecodeGrayscale8(frameBytes, schema, stretch),
            RawDataType.U8Rgb => DecodeColour8(frameBytes, schema, stretch),
            RawDataType.U16 => DecodeWide(frameBytes, schema, 0xFFFF, stretch),
            RawDataType.U14InU16 => DecodeWide(frameBytes, schema, 0x3FFF, stretch),
            RawDataType.U16U8 => DecodePacked(frameBytes, schema, stretch),
            RawDataType.YuvUyvy => DecodePackedColour(frameBytes, schema, stretch),
            _ => RawDecodeResult.Failure($"'{schema.Name}' uses a layout this build does not know."),
        };
    }

    /// <summary>One byte per pixel: the value is already the grey level.</summary>
    private static RawDecodeResult DecodeGrayscale8(byte[] bytes, RawSchema schema, bool stretch)
    {
        var plane = new byte[schema.PayloadBytes];
        Buffer.BlockCopy(bytes, 0, plane, 0, plane.Length);

        plane = Crop(plane, schema.Width, schema.Height, 1, schema.Borders, out int width, out int height);
        if (stretch)
        {
            StretchInPlace(plane, plane.Length);
        }

        return RawDecodeResult.Success(new RawFrame
        {
            Width = width,
            Height = height,
            Format = RawPixelFormat.Gray8,
            Pixels = plane,
        });
    }

    /// <summary>Three bytes per pixel, in red, green, blue order.</summary>
    private static RawDecodeResult DecodeColour8(byte[] bytes, RawSchema schema, bool stretch)
    {
        var rgb = new byte[schema.PayloadBytes];
        Buffer.BlockCopy(bytes, 0, rgb, 0, rgb.Length);

        rgb = Crop(rgb, schema.Width, schema.Height, 3, schema.Borders, out int width, out int height);
        return RawDecodeResult.Success(
            new RawFrame
            {
                Width = width,
                Height = height,
                Format = RawPixelFormat.Bgra32,
                Pixels = ToBgra(rgb, width * height),
            },
            IgnoredStretch(schema, stretch));
    }

    /// <summary>
    /// 16 bit values, or 14 bits sitting in a 16 bit value. Only the pixels that are going to be
    /// shown are read, so the stretch is worked out from the frame as it comes out of the crop.
    /// </summary>
    private static RawDecodeResult DecodeWide(byte[] bytes, RawSchema schema, int mask, bool stretch)
    {
        int width = schema.Width;
        (int croppedWidth, int croppedHeight) = schema.Borders.ApplyTo(schema.Width, schema.Height);
        int count = croppedWidth * croppedHeight;

        var values = new ushort[count];

        // The values are whole numbers, so a histogram says everything the stretch needs to know:
        // 256 KB and one pass, rather than sorting half a million numbers on every decode.
        var histogram = stretch ? new int[mask + 1] : null;

        for (int row = 0; row < croppedHeight; row++)
        {
            int sourceRow = (row + schema.Borders.Top) * width;

            for (int column = 0; column < croppedWidth; column++)
            {
                int source = (sourceRow + column + schema.Borders.Left) * 2;
                ushort value = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(source, 2)) & mask);

                values[(row * croppedWidth) + column] = value;
                if (histogram is not null)
                {
                    histogram[value]++;
                }
            }
        }

        double low = 0d;
        double high = mask;
        if (histogram is not null)
        {
            low = Percentile(histogram, count, LowPercentile);
            high = Percentile(histogram, count, HighPercentile);
        }

        double span = high - low;
        var plane = new byte[count];
        for (int i = 0; i < count; i++)
        {
            double scaled = span <= 0d ? 0d : (Math.Clamp(values[i], low, high) - low) / span;
            plane[i] = (byte)(scaled * 255d);
        }

        return RawDecodeResult.Success(new RawFrame
        {
            Width = croppedWidth,
            Height = croppedHeight,
            Format = RawPixelFormat.Gray8,
            Pixels = plane,
        });
    }

    /// <summary>
    /// The 16 bit frame with a packed 8 bit picture on its right hand side: every 16 bit value
    /// there holds two pixels, the low byte first. The left hand side is not shown, which is what
    /// the reference script does as well.
    /// </summary>
    private static RawDecodeResult DecodePacked(byte[] bytes, RawSchema schema, bool stretch)
    {
        int width = schema.Width;
        int height = schema.Height;
        int split = schema.SplitColumn > 0 ? schema.SplitColumn : width / 2;

        // The crop is applied to the 16 bit grid first, exactly as the reference script crops
        // before it takes the right hand side apart.
        int firstRow = schema.Borders.Top;
        int rows = height - schema.Borders.Top - schema.Borders.Bottom;
        int firstColumn = Math.Max(split, schema.Borders.Left);
        int columns = width - schema.Borders.Right - firstColumn;

        if (rows < 1 || columns < 1)
        {
            return RawDecodeResult.Failure($"The crop of {schema.Borders} leaves no packed 8 bit picture in '{schema.Name}'.");
        }

        var plane = new byte[rows * columns * 2];
        for (int row = 0; row < rows; row++)
        {
            int sourceRow = firstRow + row;
            for (int column = 0; column < columns; column++)
            {
                int index = (sourceRow * width) + firstColumn + column;
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * 2, 2));
                plane[((row * columns) + column) * 2] = (byte)(value & 0xFF);
                plane[(((row * columns) + column) * 2) + 1] = (byte)(value >> 8);
            }
        }

        if (stretch)
        {
            StretchInPlace(plane, plane.Length);
        }

        return RawDecodeResult.Success(
            new RawFrame
            {
                Width = columns * 2,
                Height = rows,
                Format = RawPixelFormat.Gray8,
                Pixels = plane,
            },
            IgnoredStretch(schema, stretch));
    }

    /// <summary>
    /// Packed colour: two bytes per pixel, and each pair of pixels shares one colour pair. The crop
    /// is disregarded for this layout, the same way the reference script disregards it, and the
    /// window is told so rather than being shown a picture that quietly lost its edge lines.
    /// </summary>
    private static RawDecodeResult DecodePackedColour(byte[] bytes, RawSchema schema, bool stretch)
    {
        if (schema.Width % 2 != 0)
        {
            return RawDecodeResult.Failure("Packed colour needs an even frame width, because its pixels share their colour in pairs.");
        }

        int pixels = schema.Width * schema.Height;
        var rgb = new byte[pixels * 3];

        for (int pixel = 0; pixel < pixels; pixel += 2)
        {
            int start = pixel * 2;
            double u = bytes[start] - 128d;
            double y0 = bytes[start + 1];
            double v = bytes[start + 2] - 128d;
            double y1 = bytes[start + 3];

            WriteRgb(rgb, pixel, y0, u, v);
            WriteRgb(rgb, pixel + 1, y1, u, v);
        }

        string? warning = IgnoredStretch(schema, stretch);
        if (!schema.Borders.IsNone)
        {
            warning = string.Join(
                " ",
                new[]
                {
                    warning,
                    $"The crop of {schema.Borders} is disregarded for packed colour, just as the reference script does.",
                }.Where(part => !string.IsNullOrEmpty(part)));
        }

        return RawDecodeResult.Success(
            new RawFrame
            {
                Width = schema.Width,
                Height = schema.Height,
                Format = RawPixelFormat.Bgra32,
                Pixels = ToBgra(rgb, pixels),
            },
            warning);
    }

    /// <summary>
    /// One pixel of packed colour. The reference script used the same recipe but with a sign turned
    /// around in the green channel and the colours left in the order the data happened to be in;
    /// these are the ordinary coefficients and the usual order.
    /// </summary>
    private static void WriteRgb(byte[] target, int pixel, double y, double u, double v)
    {
        int offset = pixel * 3;
        target[offset] = Clamp8(y + (1.40200 * v));
        target[offset + 1] = Clamp8(y - (0.34414 * u) - (0.71414 * v));
        target[offset + 2] = Clamp8(y + (1.77200 * u));
    }

    private static byte Clamp8(double value) =>
        value <= 0d ? (byte)0 : value >= 255d ? (byte)255 : (byte)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>Red, green, blue triples to the four byte order Windows draws fastest.</summary>
    private static byte[] ToBgra(byte[] rgb, int pixels)
    {
        var bgra = new byte[pixels * 4];
        for (int i = 0, source = 0, target = 0; i < pixels; i++, source += 3, target += 4)
        {
            bgra[target] = rgb[source + 2];
            bgra[target + 1] = rgb[source + 1];
            bgra[target + 2] = rgb[source];
            bgra[target + 3] = 255;
        }

        return bgra;
    }

    /// <summary>Drops the rows and columns the schematic asks for.</summary>
    private static byte[] Crop(byte[] source, int width, int height, int bytesPerPixel, RawBorders borders, out int croppedWidth, out int croppedHeight)
    {
        (croppedWidth, croppedHeight) = borders.ApplyTo(width, height);

        if (borders.IsNone)
        {
            return source;
        }

        int rowBytes = croppedWidth * bytesPerPixel;
        var target = new byte[(long)croppedHeight * rowBytes];

        for (int row = 0; row < croppedHeight; row++)
        {
            int sourceStart = (((row + borders.Top) * width) + borders.Left) * bytesPerPixel;
            Buffer.BlockCopy(source, sourceStart, target, row * rowBytes, rowBytes);
        }

        return target;
    }

    /// <summary>
    /// The percentile with the interpolation the reference script's library uses, so a stretched
    /// frame comes out the same shade of grey here as it does there: the value that many percent of
    /// the frame sits at or below, with the two values either side of it mixed in proportion.
    /// </summary>
    private static double Percentile(ReadOnlySpan<int> histogram, int count, double percent)
    {
        if (count <= 0)
        {
            return 0d;
        }

        if (count == 1)
        {
            return LowestValue(histogram);
        }

        double rank = (count - 1) * percent / 100d;
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);

        double low = ValueAt(histogram, lower);
        if (lower == upper)
        {
            return low;
        }

        double high = ValueAt(histogram, upper);
        return low + ((rank - lower) * (high - low));
    }

    /// <summary>The value that many samples into the frame: the smallest value the count reaches.</summary>
    private static double ValueAt(ReadOnlySpan<int> histogram, int index)
    {
        int seen = 0;

        for (int value = 0; value < histogram.Length; value++)
        {
            seen += histogram[value];
            if (seen > index)
            {
                return value;
            }
        }

        return histogram.Length - 1;
    }

    private static double LowestValue(ReadOnlySpan<int> histogram)
    {
        for (int value = 0; value < histogram.Length; value++)
        {
            if (histogram[value] > 0)
            {
                return value;
            }
        }

        return 0d;
    }

    /// <summary>
    /// Stretches a plane of grey levels in place: the darkest 2% end up black and the brightest 2%
    /// white, and everything between is spread out over the greys in order.
    /// </summary>
    private static void StretchInPlace(byte[] plane, int count)
    {
        if (count == 0)
        {
            return;
        }

        Span<int> histogram = stackalloc int[256];
        foreach (byte value in plane.AsSpan(0, count))
        {
            histogram[value]++;
        }

        double low = Percentile(histogram, count, LowPercentile);
        double high = Percentile(histogram, count, HighPercentile);
        double span = high - low;

        for (int i = 0; i < count; i++)
        {
            plane[i] = span <= 0d
                ? (byte)0
                : (byte)((Math.Clamp(plane[i], low, high) - low) / span * 255d);
        }
    }

    /// <summary>
    /// Says so when the stretch is asked for on a layout that has nothing to stretch, instead of
    /// quietly doing something else.
    /// </summary>
    private static string? IgnoredStretch(RawSchema schema, bool stretch) =>
        stretch && !RawDataTypes.CanStretch(schema.DataType)
            ? "Stretching to the full range only applies to grey frames, and this one is colour, so it was left out here."
            : null;
}
