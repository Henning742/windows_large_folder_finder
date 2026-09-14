using DataFinder.Core.Util;

namespace DataFinder.Core.Models;

/// <summary>A mounted volume that can be scanned.</summary>
public sealed class VolumeInfo
{
    public required char DriveLetter { get; init; }

    /// <summary>Root path including the trailing separator, for example <c>D:\</c>.</summary>
    public required string RootPath { get; init; }

    public string Label { get; init; } = string.Empty;

    public string FileSystem { get; init; } = "NTFS";

    public long TotalSizeBytes { get; init; }

    public long FreeSpaceBytes { get; init; }

    public string DisplayName
    {
        get
        {
            string label = string.IsNullOrWhiteSpace(Label) ? "Local disk" : Label;
            return $"{DriveLetter}: {label} - {ByteSize.Format(TotalSizeBytes)} ({FileSystem})";
        }
    }
}

