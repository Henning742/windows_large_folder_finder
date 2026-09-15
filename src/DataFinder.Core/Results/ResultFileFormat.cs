using System.Globalization;
using System.Text;
using DataFinder.Core.Models;

namespace DataFinder.Core.Results;

/// <summary>
/// The plain text format the app used to export before reports became CSV: one folder per line, and
/// everything after a '#' is treated as a comment. Writing it is kept so those files can still be
/// produced and round-tripped, but the app itself only reads them now.
/// </summary>
public static class ResultFileFormat
{
    public const char CommentCharacter = '#';
    public const char EscapeCharacter = '\\';

    public static string Serialize(
        IEnumerable<FolderResult> results,
        ScanSettings? settings = null,
        VolumeInfo? volume = null,
        DateTimeOffset? generatedAt = null)
    {
        var folders = results as IList<FolderResult> ?? results.ToList();
        var builder = new StringBuilder();

        builder.Append("# NTFS Folder Finder results");
        builder.AppendLine();
        builder.Append("# generated: ")
            .Append((generatedAt ?? DateTimeOffset.Now).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))
            .AppendLine();

        if (volume is not null)
        {
            string label = string.IsNullOrWhiteSpace(volume.Label) ? "Local disk" : volume.Label;
            builder.Append("# volume: ").Append(volume.DriveLetter).Append(": ").Append(label).AppendLine();
        }

        if (settings is not null)
        {
            builder.Append("# rules: ").Append(settings.Describe()).AppendLine();
        }

        builder.Append("# folders: ").Append(folders.Count.ToString(CultureInfo.InvariantCulture)).AppendLine();
        builder.Append("# format: one folder per line. Text after '")
            .Append(CommentCharacter)
            .Append("' is a comment. A '")
            .Append(CommentCharacter)
            .Append("' inside a path is written as '")
            .Append(EscapeCharacter)
            .Append(CommentCharacter)
            .AppendLine("'.");

        foreach (FolderResult folder in folders)
        {
            builder.Append(EscapePath(folder.FullPath)).AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>Reads folder paths out of the text, ignoring comments, blank lines and duplicates.</summary>
    public static IReadOnlyList<string> ParsePaths(string? text)
    {
        return ParseRows(text).Select(row => row.Path).ToList();
    }

    /// <summary>
    /// Reads the folder paths out of the text, ignoring comments, blank lines and duplicates. The
    /// old format has no column for a note, so every comment comes back empty.
    /// </summary>
    public static IReadOnlyList<ResultRow> ParseRows(string? text)
    {
        var rows = new List<ResultRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(text))
        {
            return rows;
        }

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length > 0 && line[0] == '\uFEFF')
            {
                line = line[1..];
            }

            string path = ExtractPath(line);
            if (path.Length > 0 && seen.Add(path))
            {
                rows.Add(new ResultRow(path, string.Empty));
            }
        }

        return rows;
    }

    /// <summary>Returns the folder path on a single line, with any trailing comment removed.</summary>
    public static string ExtractPath(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        int commentStart = FindCommentStart(line);
        string candidate = commentStart >= 0 ? line[..commentStart] : line;
        return Unescape(candidate).Trim();
    }

    public static string EscapePath(string path) =>
        string.IsNullOrEmpty(path) ? string.Empty : path.Replace(CommentCharacter.ToString(), EscapeCharacter + CommentCharacter.ToString());

    public static void Save(string filePath, IEnumerable<FolderResult> results, ScanSettings? settings = null, VolumeInfo? volume = null)
    {
        string text = Serialize(results, settings, volume);
        File.WriteAllText(filePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static IReadOnlyList<string> Load(string filePath) => ParsePaths(File.ReadAllText(filePath));

    public static IReadOnlyList<ResultRow> LoadRows(string filePath) => ParseRows(File.ReadAllText(filePath));

    /// <summary>Finds the first '#' that is not escaped with a backslash.</summary>
    private static int FindCommentStart(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            char current = line[i];

            if (current == EscapeCharacter && i + 1 < line.Length && line[i + 1] == CommentCharacter)
            {
                i++;
                continue;
            }

            if (current == CommentCharacter)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Unescape(string value)
    {
        if (value.IndexOf(EscapeCharacter) < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);

        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];

            if (current == EscapeCharacter && i + 1 < value.Length && value[i + 1] == CommentCharacter)
            {
                builder.Append(CommentCharacter);
                i++;
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
