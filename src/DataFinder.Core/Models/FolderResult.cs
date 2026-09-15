using DataFinder.Core.Util;

namespace DataFinder.Core.Models;

/// <summary>One folder that matched the rules, or one folder imported from a text file.</summary>
public sealed class FolderResult
{
    public required string FullPath { get; init; }

    /// <summary>Master file table record number, or 0 when the entry came from an imported file.</summary>
    public uint RecordNumber { get; init; }

    /// <summary>Number of files sitting directly inside the folder.</summary>
    public int DirectFileCount { get; init; }

    /// <summary>Total size of the files sitting directly inside the folder.</summary>
    public long DirectSizeBytes { get; init; }

    /// <summary>Size of the folder including every subfolder.</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>The size the rules were tested against. See <see cref="ScanSettings.SizeIncludesSubfolders"/>.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Number of folders sitting directly inside this one.</summary>
    public int SubfolderCount { get; init; }

    /// <summary>Number of files below this folder, at any depth.</summary>
    public long TotalFileCount { get; init; }

    public bool SizeIncludesSubfolders { get; init; } = true;

    /// <summary>False when an imported path no longer exists on disk.</summary>
    public bool Exists { get; init; } = true;

    /// <summary>A note the user typed about this folder. It travels with the report.</summary>
    public string Comment { get; set; } = string.Empty;

    public string SizeText => Exists ? ByteSize.Format(SizeBytes) : "not found";

    public string FilesText => Exists ? DirectFileCount.ToString("N0") : "-";

    public string SubfolderText => Exists ? SubfolderCount.ToString("N0") : "-";

    public string DetailText => Exists
        ? $"{ByteSize.Format(TotalSizeBytes)} in total, {TotalFileCount:N0} files below, {DirectFileCount:N0} files directly inside, {SubfolderCount:N0} subfolders"
        : "The folder does not exist (or is not reachable) right now.";
}
