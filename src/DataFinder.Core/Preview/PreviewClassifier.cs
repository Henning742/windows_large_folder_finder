namespace DataFinder.Core.Preview;

/// <summary>Decides what kind of preview makes sense for a file, based on its extension.</summary>
public static class PreviewClassifier
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".gif",
        ".tif", ".tiff", ".ico", ".wdp", ".jxr", ".dib",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".conf",
        ".md", ".markdown", ".rst", ".tex", ".bib",
        ".py", ".cs", ".csproj", ".fs", ".vb", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx",
        ".html", ".htm", ".css", ".scss", ".less", ".sql", ".sh", ".bash", ".bat", ".cmd", ".ps1",
        ".c", ".h", ".cc", ".cpp", ".hpp", ".hxx", ".java", ".kt", ".go", ".rs", ".rb", ".php",
        ".pl", ".lua", ".r", ".m", ".swift", ".toml", ".properties", ".sln", ".gitignore", ".editorconfig",
    };

    public static bool IsImageExtension(string pathOrName) => ImageExtensions.Contains(ExtensionOf(pathOrName));

    public static bool IsTextExtension(string pathOrName) => TextExtensions.Contains(ExtensionOf(pathOrName));

    /// <summary>
    /// What kind of preview a file gets. Files whose suffix the user listed for data decoding are
    /// only offered to the decoder when nothing else already knows the extension, so listing a
    /// suffix by mistake cannot take the text preview away from, say, a .csv file.
    /// </summary>
    public static PreviewKind Classify(string pathOrName, IEnumerable<string>? decodeSuffixes = null)
    {
        string extension = ExtensionOf(pathOrName);
        if (ImageExtensions.Contains(extension))
        {
            return PreviewKind.Image;
        }

        if (TextExtensions.Contains(extension))
        {
            return PreviewKind.Text;
        }

        return Raw.RawFileTypes.Matches(decodeSuffixes, pathOrName) ? PreviewKind.Binary : PreviewKind.None;
    }

    /// <summary>The extension of a name or path in lower case, the dot included; empty when there is none.</summary>
    public static string ExtensionOf(string pathOrName)
    {
        if (string.IsNullOrEmpty(pathOrName))
        {
            return string.Empty;
        }

        int slash = pathOrName.LastIndexOfAny(new[] { '\\', '/' });
        int dot = pathOrName.LastIndexOf('.');
        return dot > slash && dot >= 0 ? pathOrName[dot..].ToLowerInvariant() : string.Empty;
    }
}
