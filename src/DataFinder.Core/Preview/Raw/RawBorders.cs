using System.Globalization;

namespace DataFinder.Core.Preview.Raw;

/// <summary>
/// Rows and columns to throw away around a frame, the way the reference scripts crop the odd edge
/// line some cameras add. <c>new RawBorders(1, 1, 0, 0)</c> drops the top and the bottom row.
/// </summary>
public readonly record struct RawBorders(int Top, int Bottom, int Left, int Right)
{
    /// <summary>Nothing is cropped.</summary>
    public static RawBorders None { get; } = new(0, 0, 0, 0);

    public bool IsNone => Top == 0 && Bottom == 0 && Left == 0 && Right == 0;

    /// <summary>The size that is left after cropping, which may be zero or less when too much is asked for.</summary>
    public (int Width, int Height) ApplyTo(int width, int height) => (width - Left - Right, height - Top - Bottom);

    /// <summary>Reads back what <see cref="ToString"/> wrote: four numbers, "top, bottom, left, right".</summary>
    public static bool TryParse(string? text, out RawBorders borders, out string? error)
    {
        borders = None;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        string[] parts = text.Split(
            new[] { ',', ';', ' ', '\t' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 4)
        {
            error = "The crop takes four numbers - top, bottom, left, right - for example 1,1,0,0. Leave it empty for no crop.";
            return false;
        }

        var numbers = new int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] < 0)
            {
                error = $"'{parts[i]}' is not a whole number of rows or columns. Use four numbers of zero or more, for example 1,1,0,0.";
                return false;
            }
        }

        borders = new RawBorders(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    /// <summary>The same as <see cref="TryParse"/>, but anything it cannot read becomes no crop.</summary>
    public static RawBorders Parse(string? text) =>
        TryParse(text, out RawBorders borders, out _) ? borders : None;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Top},{Bottom},{Left},{Right}");

    public string Describe() => IsNone ? "no crop" : $"crop {this}";
}
