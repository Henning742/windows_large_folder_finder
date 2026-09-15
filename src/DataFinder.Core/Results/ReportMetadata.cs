using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataFinder.Core.Results;

/// <summary>
/// Everything about a report that does not fit in a spreadsheet: which volumes were read, with
/// which rules, how the scan went and where the results file is.
/// </summary>
public sealed class ReportMetadata
{
    public string Format { get; set; } = "NTFS Folder Finder results";

    /// <summary>Raised whenever the fields below change meaning, so old files can be recognised.</summary>
    public int FormatVersion { get; set; } = 1;

    public string Producer { get; set; } = "NTFS Folder Finder";

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// The results file this describes, as a path relative to this file - just the file name, since
    /// the two are written side by side.
    /// </summary>
    public string ResultsFile { get; set; } = string.Empty;

    /// <summary>Set when the results came from an imported file rather than a fresh scan.</summary>
    public string? ImportedFrom { get; set; }

    public ReportRules? Rules { get; set; }

    public List<ReportVolume> Volumes { get; set; } = new();

    public int FolderCount { get; set; }

    public ReportScan? Scan { get; set; }
}

public sealed class ReportRules
{
    public long MinSizeBytes { get; set; }

    public int MinDirectFileCount { get; set; }

    public bool SizeIncludesSubfolders { get; set; }

    /// <summary>The rules in words, the same sentence the app shows in its Results pane.</summary>
    public string Description { get; set; } = string.Empty;
}

public sealed class ReportVolume
{
    public char DriveLetter { get; set; }

    public string RootPath { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string FileSystem { get; set; } = string.Empty;

    public long TotalSizeBytes { get; set; }

    public long FreeSpaceBytes { get; set; }
}

public sealed class ReportScan
{
    public double ElapsedSeconds { get; set; }

    public long RecordsRead { get; set; }

    public long ExpectedRecordCount { get; set; }

    public long RecordsInUse { get; set; }

    public bool MftReadCompleted { get; set; } = true;

    public List<string> Warnings { get; set; } = new();
}

/// <summary>Reads and writes the JSON file that sits next to a report.</summary>
public static class ReportMetadataFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(ReportMetadata metadata) => JsonSerializer.Serialize(metadata, Options);

    public static void Save(string path, ReportMetadata metadata) =>
        File.WriteAllText(path, Serialize(metadata) + Environment.NewLine, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public static ReportMetadata? Load(string path) =>
        JsonSerializer.Deserialize<ReportMetadata>(File.ReadAllText(path), Options);
}
