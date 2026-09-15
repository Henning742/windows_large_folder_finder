namespace DataFinder.Core.Preview.Raw;

/// <summary>
/// How the bytes of one frame are laid out. The names follow the "data_content" values of the
/// reference scripts this decoder was written from, so a layout that worked there can be typed
/// into a schematic here the same way.
/// </summary>
public enum RawDataType
{
    /// <summary>One byte per pixel and the value is the grey level: 640 x 512 is 327,680 bytes.</summary>
    U8 = 0,

    /// <summary>Three bytes per pixel, in red, green, blue order.</summary>
    U8Rgb = 1,

    /// <summary>One 16 bit little endian value per pixel, two bytes each.</summary>
    U16 = 2,

    /// <summary>
    /// Like <see cref="U16"/>, but only the lower 14 bits are used - a 16 bit camera that never
    /// fills its top two bits.
    /// </summary>
    U14InU16 = 3,

    /// <summary>
    /// A 16 bit frame that carries a second, smaller 8 bit picture on its right hand side. There
    /// every 16 bit value holds two pixels, the low byte first, so the picture comes out twice as
    /// wide as the columns it was packed into.
    /// </summary>
    U16U8 = 4,

    /// <summary>Packed colour (UYVY): two bytes per pixel, and each pair of pixels shares its colour.</summary>
    YuvUyvy = 5,
}

/// <summary>The layouts the app knows, and how to talk about them in the window.</summary>
public static class RawDataTypes
{
    /// <summary>Every layout, in the order the drop down shows them.</summary>
    public static IReadOnlyList<RawDataType> All { get; } = new[]
    {
        RawDataType.U8,
        RawDataType.U8Rgb,
        RawDataType.U16,
        RawDataType.U14InU16,
        RawDataType.U16U8,
        RawDataType.YuvUyvy,
    };

    /// <summary>How many bytes one pixel takes, colour included.</summary>
    public static int BytesPerPixel(RawDataType dataType) => dataType switch
    {
        RawDataType.U8 => 1,
        RawDataType.U8Rgb => 3,
        _ => 2,
    };

    /// <summary>True for the layouts that hold more than one value per pixel.</summary>
    public static bool IsColour(RawDataType dataType) => dataType is RawDataType.U8Rgb or RawDataType.YuvUyvy;

    /// <summary>
    /// True for the layouts that come out as grey levels, which are the ones that can be stretched
    /// to the full range. A colour picture is left alone: stretching it channel by channel would
    /// change what it looks like rather than how bright it is.
    /// </summary>
    public static bool CanStretch(RawDataType dataType) =>
        dataType is RawDataType.U8 or RawDataType.U16 or RawDataType.U14InU16 or RawDataType.U16U8;

    /// <summary>
    /// How a layout is shown unless the user says otherwise. 16 bit data is stretched, because the
    /// values fill only the first percent or two of the range and the picture would be black
    /// otherwise; 8 bit data is left as it is, because it already uses the whole range.
    /// </summary>
    public static bool StretchesByDefault(RawDataType dataType) =>
        dataType is RawDataType.U16 or RawDataType.U14InU16;

    public static string Describe(RawDataType dataType) => dataType switch
    {
        RawDataType.U8 => "8 bit grayscale",
        RawDataType.U8Rgb => "8 bit colour (RGB)",
        RawDataType.U16 => "16 bit grayscale",
        RawDataType.U14InU16 => "14 bit inside 16 bit",
        RawDataType.U16U8 => "16 bit + packed 8 bit",
        RawDataType.YuvUyvy => "packed colour (UYVY)",
        _ => dataType.ToString(),
    };
}
