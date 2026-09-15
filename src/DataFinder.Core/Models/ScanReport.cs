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

    /// <summary>How many records were read. Records that could not be read and were skipped are not counted.</summary>
    public long RecordsRead { get; init; }

    /// <summary>How many records the master file table should have held, from its $DATA size.</summary>
    public long ExpectedRecordCount { get; init; }

    public long RecordsInUse { get; init; }

    /// <summary>
    /// False when the master file table could not be read in full, so the results are known to be
    /// incomplete. This is the difference between "found few folders" and "could not look".
    /// </summary>
    public bool MftReadCompleted { get; init; } = true;

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
