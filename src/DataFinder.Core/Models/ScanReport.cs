using DataFinder.Core.Ntfs;

namespace DataFinder.Core.Models;

/// <summary>Everything a scan produced, including the folder tree needed for the preview pane.</summary>
public sealed class ScanReport
{
    public required VolumeInfo Volume { get; init; }

    public required ScanSettings Settings { get; init; }

    public required IReadOnlyList<FolderResult> Results { get; init; }

    /// <summary>Kept alive so the preview pane can show folder contents without touching the disk again.</summary>
    public required MftIndex Index { get; init; }

    public required AggregationResult Aggregation { get; init; }

    public TimeSpan Elapsed { get; init; }

    public long RecordsRead { get; init; }

    public long RecordsInUse { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

