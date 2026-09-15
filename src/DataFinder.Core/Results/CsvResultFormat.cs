using System.Globalization;
using System.Text;
using DataFinder.Core.Models;

namespace DataFinder.Core.Results;

/// <summary>
/// The comma separated report the app writes. Every folder is one row, the note the user typed sits
/// next to the path in its own column, and the numbers are there in both a raw and a readable form.
/// The rules, the volumes and the notes about the scan travel in the JSON file next to it, so the
/// CSV itself stays something a spreadsheet can open without complaining.
/// </summary>
public static class CsvResultFormat
{
    public const string PathColumn = "Path";
    public const string CommentColumn = "Comment";

    /// <summary>The line ending RFC 4180 asks for, whatever the machine happens to use.</summary>
    private const string LineEnding = "\r\n";

    private static readonly string[] Columns =
    {
        PathColumn,
        CommentColumn,
        "SizeBytes",
        "Size",
        "DirectFiles",
        "Subfolders",
        "TotalFiles",
        "TotalSizeBytes",
        "Exists",
    };

    public static string Serialize(IEnumerable<FolderResult> results)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', Columns)).Append(LineEnding);

        foreach (FolderResult folder in results)
        {
            builder
                .Append(Escape(folder.FullPath)).Append(',')
                .Append(Escape(folder.Comment)).Append(',')
                .Append(folder.SizeBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(folder.SizeText)).Append(',')
                .Append(folder.DirectFileCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(folder.SubfolderCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(folder.TotalFileCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(folder.TotalSizeBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(folder.Exists ? "yes" : "no")
                .Append(LineEnding);
        }

        return builder.ToString();
    }

    /// <summary>Writes the report and the JSON file that describes it next to it.</summary>
    public static void Save(string csvPath, IEnumerable<FolderResult> results, ReportMetadata metadata)
    {
        var folders = results as IList<FolderResult> ?? results.ToList();

        File.WriteAllText(csvPath, Serialize(folders), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        metadata.ResultsFile = RelativeResultsPath(csvPath);
        metadata.FolderCount = folders.Count;
        ReportMetadataFile.Save(MetadataPathFor(csvPath), metadata);
    }

    /// <summary>The metadata file that belongs to a report: the report's name plus ".meta.json".</summary>
    public static string MetadataPathFor(string csvPath) =>
        Path.ChangeExtension(csvPath ?? string.Empty, null) + ".meta.json";

    /// <summary>
    /// How the metadata file points at its report. The two files sit in the same folder, so the
    /// name alone is the relative path and the pair can be moved or copied together.
    /// </summary>
    public static string RelativeResultsPath(string csvPath) => Path.GetFileName(csvPath);

    public static IReadOnlyList<ResultRow> Load(string csvPath) => Parse(File.ReadAllText(csvPath));

    /// <summary>
    /// Reads the report back. The columns are found by name, so a report that was edited or
    /// reordered still imports; a plain list of paths with no header works too. Blank rows,
    /// rows that start with '#' and repeated paths are dropped.
    /// </summary>
    public static IReadOnlyList<ResultRow> Parse(string? text)
    {
        var rows = new List<ResultRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(text))
        {
            return rows;
        }

        List<List<string>> records = ParseRecords(text);

        if (records.Count == 0)
        {
            return rows;
        }

        int pathIndex = 0;
        int commentIndex = -1;
        int firstRow = 0;

        if (TryFindColumns(records[0], out int headerPath, out int headerComment))
        {
            pathIndex = headerPath;
            commentIndex = headerComment;
            firstRow = 1;
        }

        for (int index = firstRow; index < records.Count; index++)
        {
            List<string> record = records[index];
            if (pathIndex >= record.Count)
            {
                continue;
            }

            string path = record[pathIndex].Trim();
            if (path.Length == 0 || path[0] == ResultFileFormat.CommentCharacter || !seen.Add(path))
            {
                continue;
            }

            string comment = commentIndex >= 0 && commentIndex < record.Count ? record[commentIndex].Trim() : string.Empty;
            rows.Add(new ResultRow(path, comment));
        }

        return rows;
    }

    /// <summary>Puts quotes around a field when a spreadsheet would otherwise read it wrong.</summary>
    public static string Escape(string? value)
    {
        string text = value ?? string.Empty;

        bool needsQuotes =
            text.Contains(',') ||
            text.Contains('"') ||
            text.Contains('\n') ||
            text.Contains('\r') ||
            (text.Length > 0 && (text[0] == ' ' || text[^1] == ' '));

        return needsQuotes ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }

    /// <summary>Splits the text into rows of fields, honouring quoted fields.</summary>
    private static List<List<string>> ParseRecords(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];

            if (inQuotes)
            {
                if (current == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(current);
                }

                continue;
            }

            switch (current)
            {
                case '"':
                    inQuotes = true;
                    break;

                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;

                case '\r':
                    // A "\r\n" is one break, a lone "\r" is a break of its own.
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }

                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = new List<string>();
                    break;

                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = new List<string>();
                    break;

                default:
                    field.Append(current);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    /// <summary>Finds the path and comment columns of a header row, if it is one.</summary>
    private static bool TryFindColumns(List<string> header, out int pathIndex, out int commentIndex)
    {
        pathIndex = -1;
        commentIndex = -1;

        for (int index = 0; index < header.Count; index++)
        {
            string name = header[index].Trim().TrimStart('\uFEFF');

            if (pathIndex < 0 && string.Equals(name, PathColumn, StringComparison.OrdinalIgnoreCase))
            {
                pathIndex = index;
            }
            else if (commentIndex < 0 && string.Equals(name, CommentColumn, StringComparison.OrdinalIgnoreCase))
            {
                commentIndex = index;
            }
        }

        return pathIndex >= 0;
    }

    /// <summary>Builds the metadata for a report from what a scan produced.</summary>
    public static ReportMetadata BuildMetadata(
        IEnumerable<VolumeInfo> volumes,
        ScanSettings? settings,
        IEnumerable<ScanReport> reports,
        bool imported,
        string? importedFrom = null,
        DateTimeOffset? generatedAt = null)
    {
        var volumeList = volumes.ToList();
        var reportList = reports.ToList();

        var metadata = new ReportMetadata
        {
            GeneratedAt = generatedAt ?? DateTimeOffset.Now,
            ImportedFrom = imported ? importedFrom : null,
            Volumes = volumeList.Select(volume => new ReportVolume
            {
                DriveLetter = volume.DriveLetter,
                RootPath = volume.RootPath,
                Label = volume.Label,
                FileSystem = volume.FileSystem,
                TotalSizeBytes = volume.TotalSizeBytes,
                FreeSpaceBytes = volume.FreeSpaceBytes,
            }).ToList(),
        };

        if (settings is not null)
        {
            metadata.Rules = new ReportRules
            {
                MinSizeBytes = settings.MinSizeBytes,
                MinDirectFileCount = settings.MinDirectFileCount,
                SizeIncludesSubfolders = settings.SizeIncludesSubfolders,
                Description = settings.Describe(),
            };
        }

        if (reportList.Count > 0)
        {
            metadata.Scan = new ReportScan
            {
                ElapsedSeconds = Math.Round(reportList.Sum(report => report.Elapsed.TotalSeconds), 3),
                RecordsRead = reportList.Sum(report => report.RecordsRead),
                ExpectedRecordCount = reportList.Sum(report => report.ExpectedRecordCount),
                RecordsInUse = reportList.Sum(report => report.RecordsInUse),
                MftReadCompleted = reportList.All(report => report.MftReadCompleted),
                Warnings = reportList.SelectMany(report => report.Warnings).ToList(),
            };
        }

        return metadata;
    }
}
