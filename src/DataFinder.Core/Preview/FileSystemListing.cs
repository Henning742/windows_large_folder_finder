using DataFinder.Core.Models;

namespace DataFinder.Core.Preview;

/// <summary>Measurements taken straight from the file system instead of the master file table.</summary>
public sealed class FolderMeasurement
{
    public bool Exists { get; init; }

    public int DirectFileCount { get; init; }

    public long DirectSizeBytes { get; init; }

    public long TotalSizeBytes { get; init; }

    public long TotalFileCount { get; init; }

    public int SubfolderCount { get; init; }
}

/// <summary>
/// File system access used for two things: previewing an imported result that was not produced by
/// a scan, and measuring imported folders so their columns are not empty.
/// </summary>
public static class FileSystemListing
{
    private static readonly EnumerationOptions TopLevelOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static readonly EnumerationOptions RecursiveOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static IReadOnlyList<FileEntry> EnumerateChildren(string folderPath)
    {
        var entries = new List<FileEntry>();

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return entries;
        }

        try
        {
            foreach (string directory in Directory.EnumerateDirectories(folderPath, "*", TopLevelOptions))
            {
                var info = new DirectoryInfo(directory);
                long size = 0;
                long fileCount = 0;
                int subfolderCount = 0;

                try
                {
                    foreach (FileInfo file in info.EnumerateFiles("*", RecursiveOptions))
                    {
                        size += file.Length;
                        fileCount++;
                    }

                    subfolderCount = info.EnumerateDirectories("*", TopLevelOptions).Count();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }

                entries.Add(new FileEntry
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = true,
                    SizeBytes = size,
                    DirectChildCount = (int)Math.Min(subfolderCount + fileCount, int.MaxValue),
                });
            }

            foreach (string file in Directory.EnumerateFiles(folderPath, "*", TopLevelOptions))
            {
                var info = new FileInfo(file);
                long size = 0;
                try
                {
                    size = info.Length;
                }
                catch (IOException)
                {
                }

                entries.Add(new FileEntry
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = false,
                    SizeBytes = size,
                    PreviewKind = PreviewClassifier.Classify(info.Name),
                });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }

        entries.Sort(static (left, right) =>
        {
            if (left.IsDirectory != right.IsDirectory)
            {
                return left.IsDirectory ? -1 : 1;
            }

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        return entries;
    }

    public static FolderMeasurement Measure(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return new FolderMeasurement { Exists = false };
        }

        int directFileCount = 0;
        long directSize = 0;
        int subfolderCount = 0;

        try
        {
            foreach (string file in Directory.EnumerateFiles(folderPath, "*", TopLevelOptions))
            {
                directFileCount++;
                try
                {
                    directSize += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                }
            }

            subfolderCount = Directory.EnumerateDirectories(folderPath, "*", TopLevelOptions).Count();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }

        long totalSize = directSize;
        long totalFileCount = directFileCount;

        try
        {
            foreach (FileInfo file in new DirectoryInfo(folderPath).EnumerateFiles("*", RecursiveOptions))
            {
                totalSize += file.Length;
                totalFileCount++;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }

        return new FolderMeasurement
        {
            Exists = true,
            DirectFileCount = directFileCount,
            DirectSizeBytes = directSize,
            TotalSizeBytes = totalSize,
            TotalFileCount = totalFileCount,
            SubfolderCount = subfolderCount,
        };
    }
}

