using DataFinder.Core.Util;

namespace DataFinder.Core.Preview.Raw;

/// <summary>
/// One way of reading a data file: how wide a frame is, how many bytes of header sit in front of
/// it, which values the pixels are and what to do with them. It is the settings of the reference
/// script's <c>config_dict</c>, written down once so the window can offer it as a choice.
/// </summary>
public sealed class RawSchema
{
    public const int MaxWidth = 32768;
    public const int MaxHeight = 32768;
    public const long MaxPixels = 64L * 1024 * 1024;
    public const int MaxHeaderLength = 1024 * 1024;

    public string Name { get; set; } = "New schematic";

    /// <summary>Pixels across one frame, before cropping.</summary>
    public int Width { get; set; } = 640;

    /// <summary>Rows per frame, before cropping.</summary>
    public int Height { get; set; } = 512;

    /// <summary>Bytes in front of every frame that are not pixels.</summary>
    public int HeaderLength { get; set; }

    public RawDataType DataType { get; set; } = RawDataType.U8;

    /// <summary>
    /// Stretch the values so the darkest 2% go black and the brightest 2% go white, which is what
    /// makes a 16 bit frame visible at all. Only the 16 bit layouts use it.
    /// </summary>
    public bool Normalize { get; set; }

    /// <summary>Rows and columns to throw away before showing the frame.</summary>
    public RawBorders Borders { get; set; } = RawBorders.None;

    /// <summary>
    /// For <see cref="RawDataType.U16U8"/>: the column where the packed 8 bit picture starts.
    /// Zero means the middle of the frame, which is what the reference scripts use.
    /// </summary>
    public int SplitColumn { get; set; }

    /// <summary>Which frame inside the file to show. 0 is the first one.</summary>
    public int FrameIndex { get; set; }

    public int BytesPerPixel => RawDataTypes.BytesPerPixel(DataType);

    public long Pixels => (long)Width * Height;

    /// <summary>Bytes of pixels in one frame, header not counted.</summary>
    public int PayloadBytes => (int)(Pixels * BytesPerPixel);

    /// <summary>Bytes from the start of one frame to the start of the next.</summary>
    public int FrameBytes => HeaderLength + PayloadBytes;

    /// <summary>True when the chosen layout does something with <see cref="Normalize"/>.</summary>
    public bool CanNormalize => RawDataTypes.CanNormalize(DataType);

    public string SizeText => $"{Width} x {Height}";

    /// <summary>Null when the schematic can be used, otherwise what is wrong with it.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Give the schematic a name, so it can be told apart in the list.";
        }

        if (Width is < 1 or > MaxWidth)
        {
            return $"The frame width must be between 1 and {MaxWidth:N0} pixels.";
        }

        if (Height is < 1 or > MaxHeight)
        {
            return $"The frame height must be between 1 and {MaxHeight:N0} pixels.";
        }

        if (Pixels > MaxPixels)
        {
            return $"A frame of {Width} x {Height} is {Pixels:N0} pixels. Keep it below {MaxPixels:N0}, or the preview would have to read an enormous amount of data.";
        }

        if (HeaderLength is < 0 or > MaxHeaderLength)
        {
            return $"The header length must be between 0 and {MaxHeaderLength:N0} bytes.";
        }

        if (FrameIndex < 0)
        {
            return "The frame number cannot be negative. 0 is the first frame.";
        }

        if (SplitColumn < 0)
        {
            return "The column the packed 8 bit part starts at cannot be negative. Leave it at 0 for the middle of the frame.";
        }

        if (Borders.Top < 0 || Borders.Bottom < 0 || Borders.Left < 0 || Borders.Right < 0)
        {
            return "The crop cannot be negative. Use 0 for the sides that should stay.";
        }

        (int croppedWidth, int croppedHeight) = Borders.ApplyTo(Width, Height);
        if (croppedWidth < 1 || croppedHeight < 1)
        {
            return $"The crop of {Borders} leaves nothing of a {Width} x {Height} frame.";
        }

        if (DataType == RawDataType.U16U8)
        {
            int split = SplitColumn > 0 ? SplitColumn : Width / 2;
            if (split < 1 || split > Width - 1)
            {
                return $"The packed 8 bit picture must start inside the frame, between column 1 and {Width - 1}.";
            }

            if (split < Borders.Left || split > Width - Borders.Right)
            {
                return $"The crop of {Borders} removes the columns the packed 8 bit picture lives in.";
            }
        }

        return null;
    }

    public bool IsValid => Validate() is null;

    public RawSchema Clone() => new()
    {
        Name = Name,
        Width = Width,
        Height = Height,
        HeaderLength = HeaderLength,
        DataType = DataType,
        Normalize = Normalize,
        Borders = Borders,
        SplitColumn = SplitColumn,
        FrameIndex = FrameIndex,
    };

    /// <summary>One line for a tool tip: the size, the layout, the header and what is done to it.</summary>
    public string Describe()
    {
        var parts = new List<string>
        {
            SizeText,
            RawDataTypes.Describe(DataType),
        };

        if (HeaderLength > 0)
        {
            parts.Add($"{HeaderLength:N0} byte header");
        }

        if (Normalize && CanNormalize)
        {
            parts.Add("stretched to 0-255");
        }

        if (DataType == RawDataType.U16U8)
        {
            parts.Add($"8 bit part from column {(SplitColumn > 0 ? SplitColumn : Width / 2)}");
        }

        if (!Borders.IsNone)
        {
            parts.Add(Borders.Describe());
        }

        if (FrameIndex > 0)
        {
            parts.Add($"frame {FrameIndex}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>The bytes one frame takes, in words, for the tool tip.</summary>
    public string FrameSizeText => ByteSize.Format(FrameBytes);

    /// <summary>The same line as <see cref="Describe"/>, for the window to bind to.</summary>
    public string Description => Describe();
}
