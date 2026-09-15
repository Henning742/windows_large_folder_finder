namespace DataFinder.Core.Preview.Raw;

/// <summary>
/// The schematics the app comes with: the recordings the reference scripts were written for, plus
/// the two plain shapes a raw data file usually turns out to be. One per layout, so nothing is
/// offered twice - anything else is a couple of boxes away in the Decode settings dialog. A fresh
/// copy is handed out every time, so changing one in the window never changes what the app starts
/// with.
/// </summary>
public static class BuiltInRawSchemas
{
    public static IReadOnlyList<RawSchema> Create() => new[]
    {
        new RawSchema
        {
            // The simplest kind of recording: one byte per pixel and no header.
            Name = "8 bit grayscale 640 x 512",
            Width = 640,
            Height = 512,
            DataType = RawDataType.U8,
        },
        new RawSchema
        {
            // The headerless 16 bit dump an ordinary frame grabber writes.
            Name = "16 bit grayscale 640 x 512",
            Width = 640,
            Height = 512,
            DataType = RawDataType.U16,
            Normalize = true,
        },
        new RawSchema
        {
            // The infrared recording of the reference scripts, with the odd edge line cropped off.
            Name = "16 bit infrared 644 x 514, cropped",
            Width = 644,
            Height = 514,
            DataType = RawDataType.U16,
            Normalize = true,
            Borders = new RawBorders(1, 1, 4, 0),
        },
        new RawSchema
        {
            // A 16 bit camera that never fills its top two bits, with 64 bytes of header per frame.
            Name = "14 bit inside 16 bit 640 x 514 (64 byte header)",
            Width = 640,
            Height = 514,
            HeaderLength = 64,
            DataType = RawDataType.U14InU16,
            Normalize = true,
        },
        new RawSchema
        {
            // The wide recording: 16 bit rows that hold a packed 8 bit picture on their right side.
            Name = "16 bit + packed 8 bit 960 x 514 (64 byte header)",
            Width = 960,
            Height = 514,
            HeaderLength = 64,
            DataType = RawDataType.U16U8,
            Borders = new RawBorders(1, 1, 0, 0),
        },
        new RawSchema
        {
            Name = "Colour 1920 x 540 (UYVY)",
            Width = 1920,
            Height = 540,
            DataType = RawDataType.YuvUyvy,
        },
    };
}
