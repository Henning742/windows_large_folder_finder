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

    public static bool IsImageExtension(string pathOrName) => ImageExtensions.Contains(GetExtension(pathOrName));

    public static bool IsTextExtension(string pathOrName) => TextExtensions.Contains(GetExtension(pathOrName));

    public static PreviewKind Classify(string pathOrName)
    {
        string extension = GetExtension(pathOrName);
        if (ImageExtensions.Contains(extension))
        {
            return PreviewKind.Image;
        }

        return TextExtensions.Contains(extension) ? PreviewKind.Text : PreviewKind.None;
    }

    private static string GetExtension(string pathOrName)
    {
        if (string.IsNullOrEmpty(pathOrName))
        {
            return string.Empty;
        }

        int slash = pathOrName.LastIndexOfAny(new[] { '\\', '/' });
        int dot = pathOrName.LastIndexOf('.');
        return dot > slash && dot >= 0 ? pathOrName[dot..] : string.Empty;
    }
}

