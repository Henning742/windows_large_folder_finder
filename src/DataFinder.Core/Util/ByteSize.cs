using System.Globalization;

namespace DataFinder.Core.Util;

/// <summary>Helpers for turning byte counts into something a human can read.</summary>
public static class ByteSize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return "-" + Format(-bytes);
        }

        double value = bytes;
        int unit = 0;

        while (value >= 1024d && unit < Units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return unit == 0
            ? string.Format(CultureInfo.InvariantCulture, "{0} {1}", bytes, Units[0])
            : string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", value, Units[unit]);
    }

    /// <summary>Interprets user input such as "200", "200 MB" or "1.5 GB" as a byte count.</summary>
    public static bool TryParseUserInput(string? text, out long bytes, out string? error)
    {
        bytes = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a size, for example 200 for 200 MB.";
            return false;
        }

        string trimmed = text.Trim().Replace(',', '.');

        double multiplier = 1024d * 1024d;
        string[] unitSuffixes = { "PB", "TB", "GB", "MB", "KB", "B" };
        double[] unitMultipliers =
        {
            1024d * 1024d * 1024d * 1024d * 1024d,
            1024d * 1024d * 1024d * 1024d,
            1024d * 1024d * 1024d,
            1024d * 1024d,
            1024d,
            1d
        };

        for (int i = 0; i < unitSuffixes.Length; i++)
        {
            if (trimmed.EndsWith(unitSuffixes[i], StringComparison.OrdinalIgnoreCase))
            {
                multiplier = unitMultipliers[i];
                trimmed = trimmed[..^unitSuffixes[i].Length].Trim();
                break;
            }
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            error = $"'{text}' does not look like a number.";
            return false;
        }

        if (number <= 0 || double.IsNaN(number) || double.IsInfinity(number))
        {
            error = "The size must be greater than zero.";
            return false;
        }

        double total = number * multiplier;
        if (total > long.MaxValue)
        {
            error = "The size is too large.";
            return false;
        }

        bytes = (long)Math.Round(total);
        return true;
    }
}

