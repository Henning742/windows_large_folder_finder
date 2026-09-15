namespace DataFinder.Core.Preview.Raw;

/// <summary>How the decoded pixels are stored, ready to be drawn.</summary>
public enum RawPixelFormat
{
    /// <summary>One byte per pixel, black to white.</summary>
    Gray8 = 0,

    /// <summary>Four bytes per pixel in blue, green, red, unused order - what Windows draws fastest.</summary>
    Bgra32 = 1,
}

/// <summary>One decoded frame: the size it came out as, and its pixels.</summary>
public sealed class RawFrame
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required RawPixelFormat Format { get; init; }

    /// <summary>The pixels, row by row, with no padding between the rows.</summary>
    public required byte[] Pixels { get; init; }

    public int Stride => Width * (Format == RawPixelFormat.Gray8 ? 1 : 4);
}

/// <summary>
/// What came out of one decode: a frame, or the reason there is none. Some layouts also have
/// something to mention - a setting they ignore, for instance - which the window shows next to the
/// picture instead of hiding.
/// </summary>
public sealed record RawDecodeResult(RawFrame? Frame, string? Error, string? Warning)
{
    public bool Succeeded => Frame is not null;

    public static RawDecodeResult Success(RawFrame frame, string? warning = null) => new(frame, null, warning);

    public static RawDecodeResult Failure(string error) => new(null, error, null);
}
