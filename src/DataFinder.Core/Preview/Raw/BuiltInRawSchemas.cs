namespace DataFinder.Core.Preview.Raw;

/// <summary>
/// The schematics the app comes with: one for each recording the reference scripts were written
/// for, plus a couple of plain shapes that raw data files often turn out to be. A fresh copy is
/// handed out every time, so changing one in the window never changes what the app starts with.
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
            Name = "8 bit grayscale 1280 x 720",
            Width = 1280,
            Height = 720,
            DataType = RawDataType.U8,
        },
        new RawSchema
        {
            Name = "8 bit colour 1920 x 1080",
            Width = 1920,
            Height = 1080,
            DataType = RawDataType.U8Rgb,
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
            Name = "16 bit grayscale 640 x 512, stretched",
            Width = 640,
            Height = 512,
            DataType = RawDataType.U16,
            Normalize = true,
        },
        new RawSchema
        {
            // The same frame without the stretch: it stays dark unless the data really does use the
            // whole 16 bit range.
            Name = "16 bit grayscale 640 x 512, as it is",
            Width = 640,
            Height = 512,
            DataType = RawDataType.U16,
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
            Name = "Colour 1920 x 540 (UYVY, speckle removed)",
            Width = 1920,
            Height = 540,
            DataType = RawDataType.YuvUyvy,
            RemoveWhite = true,
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
