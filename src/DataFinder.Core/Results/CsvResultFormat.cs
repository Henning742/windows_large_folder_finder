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
    ///
    /// The numbers of a row are read with it, so a report the app wrote does not have to be
    /// measured again when it is imported. A row that holds only some of them is left without any,
    /// because half a row is worse than walking the folder.
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

        ReportColumns columns = ReportColumns.OfPaths();
        int firstRow = 0;

        if (TryFindColumns(records[0], out ReportColumns header))
        {
            columns = header;
            firstRow = 1;
        }

        for (int index = firstRow; index < records.Count; index++)
        {
            List<string> record = records[index];
            if (columns.Path >= record.Count)
            {
                continue;
            }

            string path = record[columns.Path].Trim();
            if (path.Length == 0 || path[0] == ResultFileFormat.CommentCharacter || !seen.Add(path))
            {
                continue;
            }

            string comment = columns.Comment >= 0 && columns.Comment < record.Count
                ? record[columns.Comment].Trim()
                : string.Empty;

            rows.Add(new ResultRow(path, comment) { Numbers = ReadNumbers(record, columns) });
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

    /// <summary>
    /// Where the columns a row is read from sit in it, by name. Every column but the path is -1
    /// when the report does not have it.
    /// </summary>
    private sealed record ReportColumns(
        int Path,
        int Comment,
        int SizeBytes,
        int TotalSizeBytes,
        int DirectFiles,
        int TotalFiles,
        int Subfolders,
        int Exists)
    {
        /// <summary>A row read as nothing but a path, which is what a list with no header is.</summary>
        public static ReportColumns OfPaths() => new(0, -1, -1, -1, -1, -1, -1, -1);

        /// <summary>
        /// True when the row holds every number a folder is shown from. The existence flag is not
        /// among them: a report that leaves it out still says everything the columns need.
        /// </summary>
        public bool HasNumbers =>
            SizeBytes >= 0 && TotalSizeBytes >= 0 && DirectFiles >= 0 &&
            TotalFiles >= 0 && Subfolders >= 0;
    }

    /// <summary>Finds the columns of a header row, if it is one.</summary>
    private static bool TryFindColumns(List<string> header, out ReportColumns columns)
    {
        columns = ReportColumns.OfPaths();

        int path = -1;
        int comment = -1;
        int sizeBytes = -1;
        int totalSizeBytes = -1;
        int directFiles = -1;
        int totalFiles = -1;
        int subfolders = -1;
        int exists = -1;

        for (int index = 0; index < header.Count; index++)
        {
            string name = header[index].Trim().TrimStart('\uFEFF');

            if (path < 0 && string.Equals(name, PathColumn, StringComparison.OrdinalIgnoreCase))
            {
                path = index;
            }
            else if (comment < 0 && string.Equals(name, CommentColumn, StringComparison.OrdinalIgnoreCase))
            {
                comment = index;
            }
            else if (sizeBytes < 0 && string.Equals(name, "SizeBytes", StringComparison.OrdinalIgnoreCase))
            {
                sizeBytes = index;
            }
            else if (totalSizeBytes < 0 && string.Equals(name, "TotalSizeBytes", StringComparison.OrdinalIgnoreCase))
            {
                totalSizeBytes = index;
            }
            else if (directFiles < 0 && string.Equals(name, "DirectFiles", StringComparison.OrdinalIgnoreCase))
            {
                directFiles = index;
            }
            else if (totalFiles < 0 && string.Equals(name, "TotalFiles", StringComparison.OrdinalIgnoreCase))
            {
                totalFiles = index;
            }
            else if (subfolders < 0 && string.Equals(name, "Subfolders", StringComparison.OrdinalIgnoreCase))
            {
                subfolders = index;
            }
            else if (exists < 0 && string.Equals(name, "Exists", StringComparison.OrdinalIgnoreCase))
            {
                exists = index;
            }
        }

        if (path < 0)
        {
            return false;
        }

        columns = new ReportColumns(path, comment, sizeBytes, totalSizeBytes, directFiles, totalFiles, subfolders, exists);
        return true;
    }

    /// <summary>
    /// The numbers one row carries, or null when it does not carry all of them. The existence flag
    /// is read but not trusted on its own: whether the folder is still there is a question for the
    /// file system, and it is one look at the folder rather than a walk of everything below it.
    /// </summary>
    private static ReportedFolderNumbers? ReadNumbers(List<string> record, ReportColumns columns)
    {
        if (!columns.HasNumbers ||
            !TryReadLong(record, columns.SizeBytes, out long sizeBytes) ||
            !TryReadLong(record, columns.TotalSizeBytes, out long totalSizeBytes) ||
            !TryReadInt(record, columns.DirectFiles, out int directFiles) ||
            !TryReadLong(record, columns.TotalFiles, out long totalFiles) ||
            !TryReadInt(record, columns.Subfolders, out int subfolders))
        {
            return null;
        }

        return new ReportedFolderNumbers
        {
            SizeBytes = sizeBytes,
            TotalSizeBytes = totalSizeBytes,
            DirectFileCount = directFiles,
            TotalFileCount = totalFiles,
            SubfolderCount = subfolders,
            Exists = ReadFlag(record, columns.Exists),
        };
    }

    private static bool TryReadLong(List<string> record, int index, out long value)
    {
        value = 0;

        return index >= 0 && index < record.Count &&
            long.TryParse(record[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadInt(List<string> record, int index, out int value)
    {
        value = 0;

        return index >= 0 && index < record.Count &&
            int.TryParse(record[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads the yes/no column a report writes its existence flag in.</summary>
    private static bool ReadFlag(List<string> record, int index)
    {
        string text = index >= 0 && index < record.Count ? record[index].Trim() : string.Empty;

        return !string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(text, "0", StringComparison.Ordinal);
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
