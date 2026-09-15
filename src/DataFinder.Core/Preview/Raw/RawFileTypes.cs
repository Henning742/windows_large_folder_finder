namespace DataFinder.Core.Preview.Raw;

/// <summary>
/// The file suffixes the preview tries to read as data files. The list is typed by the user, so it
/// is handled leniently: ".raw .bin" and "raw, bin" mean the same thing.
/// </summary>
public static class RawFileTypes
{
    public const string DefaultText = ".raw .bin .dat .data";

    private static readonly char[] Separators = { ' ', ',', ';', '|', '\t', '\r', '\n' };

    /// <summary>What the app starts with: the suffixes the reference recordings use.</summary>
    public static IReadOnlyList<string> Default { get; } = Parse(DefaultText);

    /// <summary>Turns whatever was typed into a tidy list, quietly dropping what makes no sense.</summary>
    public static IReadOnlyList<string> Parse(string? text)
    {
        var result = new List<string>();

        foreach (string token in Split(text))
        {
            string suffix = Normalize(token);
            if (IsWellFormed(suffix) && !result.Contains(suffix, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(suffix);
            }
        }

        return result;
    }

    /// <summary>The same, but the user is told about a token that cannot be a file suffix.</summary>
    public static bool TryParse(string? text, out IReadOnlyList<string> suffixes, out string? error)
    {
        var result = new List<string>();
        error = null;

        foreach (string token in Split(text))
        {
            string suffix = Normalize(token);
            if (!IsWellFormed(suffix))
            {
                error = $"'{token}' is not a file suffix. Write them like .raw .bin, or raw, bin.";
                suffixes = Array.Empty<string>();
                return false;
            }

            if (!result.Contains(suffix, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(suffix);
            }
        }

        suffixes = result;
        return true;
    }

    /// <summary>True when the file name ends in one of the suffixes.</summary>
    public static bool Matches(IEnumerable<string>? suffixes, string pathOrName)
    {
        if (suffixes is null || string.IsNullOrEmpty(pathOrName))
        {
            return false;
        }

        string extension = PreviewClassifier.ExtensionOf(pathOrName);
        if (extension.Length == 0)
        {
            return false;
        }

        foreach (string suffix in suffixes)
        {
            if (string.Equals(suffix, extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How the list reads in one line, for the note under the box.</summary>
    public static string Describe(IEnumerable<string>? suffixes)
    {
        var list = suffixes?.Where(suffix => suffix.Length > 0).ToList() ?? new List<string>();
        return list.Count switch
        {
            0 => "No file suffix is set up for decoding.",
            1 => $"{list[0]} files are read as data files.",
            _ => $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]} files are read as data files.",
        };
    }

    private static IEnumerable<string> Split(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>"raw" becomes ".raw", so the dot does not have to be remembered.</summary>
    private static string Normalize(string token)
    {
        string trimmed = token.Trim().ToLowerInvariant();
        return trimmed.Length == 0 || trimmed[0] == '.' ? trimmed : "." + trimmed;
    }

    /// <summary>A suffix is a dot and at least one ordinary file name character.</summary>
    private static bool IsWellFormed(string suffix)
    {
        if (suffix.Length < 2 || suffix[0] != '.')
        {
            return false;
        }

        for (int i = 1; i < suffix.Length; i++)
        {
            char character = suffix[i];
            if (!char.IsLetterOrDigit(character) && character is not ('_' or '-' or '+' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
