using DataFinder.Core.Models;
using DataFinder.Core.Preview;

namespace DataFinder.Core.Results;

/// <summary>
/// Turns a row of an imported list into the folder the window shows.
///
/// A report the app wrote carries every number, so the folder is not walked again: the row shows
/// what the scan found when the report was written. The only thing asked of the disk then is
/// whether the folder is still there, which is one look at the folder rather than a walk of
/// everything below it.
///
/// A plain list of paths, and anything else that arrives without the numbers, is measured from the
/// file system instead - which is where the columns came from in the first place.
/// </summary>
public static class ImportedFolder
{
    /// <summary>The folder shown for one row of an imported list.</summary>
    public static FolderResult Describe(ResultRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Numbers is { } numbers ? FromTheReport(row, numbers) : FromTheFileSystem(row);
    }

    /// <summary>
    /// What the report says, with the folder looked at to see whether it is still there: the sizes
    /// describe the folder as the scan found it, which is the point of reading a report back.
    /// </summary>
    private static FolderResult FromTheReport(ResultRow row, ReportedFolderNumbers numbers) => new()
    {
        FullPath = row.Path,
        Comment = row.Comment,
        RecordNumber = 0,
        DirectFileCount = numbers.DirectFileCount,
        TotalSizeBytes = numbers.TotalSizeBytes,
        SizeBytes = numbers.SizeBytes,
        SubfolderCount = numbers.SubfolderCount,
        TotalFileCount = numbers.TotalFileCount,
        SizeIncludesSubfolders = true,
        Exists = Directory.Exists(row.Path),
    };

    /// <summary>Every number measured off the disk, for a list that came without any.</summary>
    private static FolderResult FromTheFileSystem(ResultRow row)
    {
        FolderMeasurement measurement = FileSystemListing.Measure(row.Path);

        return new FolderResult
        {
            FullPath = row.Path,
            Comment = row.Comment,
            RecordNumber = 0,
            DirectFileCount = measurement.DirectFileCount,
            DirectSizeBytes = measurement.DirectSizeBytes,
            TotalSizeBytes = measurement.TotalSizeBytes,
            SizeBytes = measurement.TotalSizeBytes,
            SubfolderCount = measurement.SubfolderCount,
            TotalFileCount = measurement.TotalFileCount,
            SizeIncludesSubfolders = true,
            Exists = measurement.Exists,
        };
    }
}
