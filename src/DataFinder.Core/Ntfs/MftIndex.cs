using DataFinder.Core.Models;
using DataFinder.Core.Preview;
using DataFinder.Core.Util;

namespace DataFinder.Core.Ntfs;

/// <summary>
/// Builds an in-memory picture of a volume's folder tree from master file table records, then
/// answers two questions: which folders match the rules, and what sits inside a given folder.
/// </summary>
public sealed class MftIndex
{
    /// <summary>The MFT record number of a volume's root directory.</summary>
    public const uint RootRecordNumber = 5;

    /// <summary>Records below this number are NTFS metadata ($MFT, $LogFile, $Bitmap, ...).</summary>
    private const uint FirstUserRecordNumber = 16;

    private static readonly List<FileLink> NoFiles = new();
    private static readonly List<DirLink> NoDirectories = new();

    private readonly HashSet<uint> _directoryRecords = new();
    private readonly Dictionary<uint, List<FileLink>> _filesByDirectory = new();
    private readonly Dictionary<uint, List<DirLink>> _childDirectoriesByParent = new();

    public int DirectoryCount => _directoryRecords.Count;

    /// <summary>Number of file entries seen (a file with two names in two folders counts twice).</summary>
    public long FileLinkCount { get; private set; }

    /// <summary>
    /// Records in use that could not be put in a folder because no name could be read for them - the
    /// name lives in an extension record that could not be read. They are missing from the results.
    /// </summary>
    public long UnnamedRecordCount { get; private set; }

    /// <summary>Records that were too damaged to read at all.</summary>
    public long CorruptRecordCount { get; private set; }

    public void Add(MftRecordParseResult record)
    {
        if (record is null)
        {
            return;
        }

        if (record.IsCorrupt)
        {
            CorruptRecordCount++;
            return;
        }

        if (!record.InUse)
        {
            return;
        }

        // An extension record holds part of another file's attributes. Those attributes are merged
        // into the base record before it is added, so an extension record must never be counted as
        // a file of its own.
        if (record.IsExtensionRecord)
        {
            return;
        }

        uint recordNumber = record.RecordNumber;

        if (recordNumber == RootRecordNumber)
        {
            // The root's own $FILE_NAME is "." and points back at itself, so the root is
            // registered without any links and its path comes from the volume instead.
            _directoryRecords.Add(recordNumber);
            return;
        }

        if (recordNumber < FirstUserRecordNumber)
        {
            return;
        }

        // A file is only ever seen through a name, so a record whose names could not be read counts
        // nowhere else. Saying so is the difference between "nothing matched" and "some of it could
        // not be looked at".
        if (record.Links.Count == 0)
        {
            UnnamedRecordCount++;
            return;
        }

        foreach (FileNameLink link in record.Links)
        {
            // NTFS keeps its own bookkeeping in files that start with '$'; they are not user data.
            if (link.Name.Length == 0 || link.Name[0] == '$')
            {
                continue;
            }

            if (link.IsDirectory)
            {
                _directoryRecords.Add(link.RecordNumber);
                AddChildDirectory(link.ParentRecordNumber, link.RecordNumber, link.Name);
            }
            else
            {
                long size = record.HasData ? record.DataSize : link.FileNameSize;

                if (!_filesByDirectory.TryGetValue(link.ParentRecordNumber, out List<FileLink>? files))
                {
                    files = new List<FileLink>();
                    _filesByDirectory[link.ParentRecordNumber] = files;
                }

                files.Add(new FileLink(link.Name, size));
                FileLinkCount++;
            }
        }
    }

    public AggregationResult Build(ScanSettings settings, string volumeRootPath)
    {
        var stats = new Dictionary<uint, DirStats>();
        var pathToRecord = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var results = new List<FolderResult>();
        var warnings = new List<string>();
        string rootPath = WindowsPath.NormalizeRoot(volumeRootPath);

        if (!_directoryRecords.Contains(RootRecordNumber))
        {
            warnings.Add("The root directory of this volume was not found in the master file table, so no folders could be listed.");
            return new AggregationResult(results, stats, pathToRecord, warnings, rootFound: false, orphanDirectories: _directoryRecords.Count);
        }

        var visited = new HashSet<uint> { RootRecordNumber };
        var stack = new Stack<Frame>();

        stats[RootRecordNumber] = new DirStats { Path = rootPath };
        pathToRecord[rootPath] = RootRecordNumber;
        stack.Push(new Frame(RootRecordNumber, RootRecordNumber, rootPath, 0));

        while (stack.Count > 0)
        {
            Frame frame = stack.Pop();
            List<DirLink> children = GetChildDirectories(frame.Record);

            if (frame.ChildIndex < children.Count)
            {
                // Walk the children one at a time, then come back to finish this folder.
                stack.Push(frame with { ChildIndex = frame.ChildIndex + 1 });
                DirLink child = children[frame.ChildIndex];

                if (child.RecordNumber == frame.Record ||
                    !_directoryRecords.Contains(child.RecordNumber) ||
                    !visited.Add(child.RecordNumber))
                {
                    continue;
                }

                string childPath = WindowsPath.Combine(frame.Path, child.Name);
                stats[child.RecordNumber] = new DirStats { Path = childPath };
                pathToRecord[childPath] = child.RecordNumber;
                stack.Push(new Frame(child.RecordNumber, frame.Record, childPath, 0));
                continue;
            }

            FinishDirectory(frame, stats, settings, results);
        }

        long orphanDirectories = _directoryRecords.Count - visited.Count;
        if (orphanDirectories > 0)
        {
            warnings.Add($"{orphanDirectories:N0} folders were skipped because their parent chain is not reachable from the volume root.");
        }

        if (CorruptRecordCount > 0)
        {
            warnings.Add($"{CorruptRecordCount:N0} master file table records were damaged and could not be read.");
        }

        if (UnnamedRecordCount > 0)
        {
            warnings.Add(
                $"{UnnamedRecordCount:N0} files are missing from the results because their name could not be read, " +
                "so the folders they sit in are smaller than they really are.");
        }

        // Sorted by path rather than by size: the result list is read as a folder tree, so the
        // order has to match the tree the paths produce.
        results.Sort(static (left, right) => string.Compare(left.FullPath, right.FullPath, StringComparison.OrdinalIgnoreCase));
        return new AggregationResult(results, stats, pathToRecord, warnings, rootFound: true, orphanDirectories);
    }

    /// <summary>The contents of one folder: subfolders first, then files, each sorted by name.</summary>
    public IReadOnlyList<FileEntry> GetChildren(
        AggregationResult aggregation,
        uint directoryRecordNumber,
        string directoryPath,
        IEnumerable<string>? decodeSuffixes = null)
    {
        var entries = new List<FileEntry>();

        foreach (DirLink link in GetChildDirectories(directoryRecordNumber))
        {
            if (link.RecordNumber == directoryRecordNumber ||
                !aggregation.Stats.TryGetValue(link.RecordNumber, out DirStats? directoryStats))
            {
                continue;
            }

            entries.Add(new FileEntry
            {
                Name = link.Name,
                FullPath = WindowsPath.Combine(directoryPath, link.Name),
                IsDirectory = true,
                SizeBytes = directoryStats.TotalSize,
                DirectChildCount = directoryStats.DirectFileCount + directoryStats.SubfolderCount,
            });
        }

        foreach (FileLink file in GetFiles(directoryRecordNumber))
        {
            entries.Add(new FileEntry
            {
                Name = file.Name,
                FullPath = WindowsPath.Combine(directoryPath, file.Name),
                IsDirectory = false,
                SizeBytes = file.Size,
                PreviewKind = PreviewClassifier.Classify(file.Name, decodeSuffixes),
            });
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

    private void FinishDirectory(Frame frame, Dictionary<uint, DirStats> stats, ScanSettings settings, List<FolderResult> results)
    {
        DirStats directory = stats[frame.Record];
        List<FileLink> files = GetFiles(frame.Record);

        long directSize = 0;
        foreach (FileLink file in files)
        {
            directSize += file.Size;
        }

        directory.Path = frame.Path;
        directory.DirectFileCount = files.Count;
        directory.DirectSize = directSize;
        directory.SubfolderCount = GetChildDirectories(frame.Record).Count;

        // Every child folder has already reported its totals, so this fold-up is the whole tree.
        directory.TotalSize += directSize;
        directory.TotalFileCount += files.Count;

        if (frame.Record != RootRecordNumber && stats.TryGetValue(frame.Parent, out DirStats? parent))
        {
            parent.TotalSize += directory.TotalSize;
            parent.TotalFileCount += directory.TotalFileCount;
        }

        long effectiveSize = settings.SizeIncludesSubfolders ? directory.TotalSize : directory.DirectSize;

        if (directory.DirectFileCount > settings.MinDirectFileCount && effectiveSize > settings.MinSizeBytes)
        {
            results.Add(new FolderResult
            {
                FullPath = frame.Path,
                RecordNumber = frame.Record,
                DirectFileCount = directory.DirectFileCount,
                DirectSizeBytes = directory.DirectSize,
                TotalSizeBytes = directory.TotalSize,
                SizeBytes = effectiveSize,
                SubfolderCount = directory.SubfolderCount,
                TotalFileCount = directory.TotalFileCount,
                SizeIncludesSubfolders = settings.SizeIncludesSubfolders,
            });
        }
    }

    private List<DirLink> GetChildDirectories(uint recordNumber) =>
        _childDirectoriesByParent.TryGetValue(recordNumber, out List<DirLink>? directories) ? directories : NoDirectories;

    private List<FileLink> GetFiles(uint recordNumber) =>
        _filesByDirectory.TryGetValue(recordNumber, out List<FileLink>? files) ? files : NoFiles;

    private void AddChildDirectory(uint parentRecordNumber, uint recordNumber, string name)
    {
        if (!_childDirectoriesByParent.TryGetValue(parentRecordNumber, out List<DirLink>? directories))
        {
            directories = new List<DirLink>();
            _childDirectoriesByParent[parentRecordNumber] = directories;
        }

        directories.Add(new DirLink(recordNumber, name));
    }

    private readonly record struct Frame(uint Record, uint Parent, string Path, int ChildIndex);

    private readonly record struct FileLink(string Name, long Size);

    private readonly record struct DirLink(uint RecordNumber, string Name);
}

/// <summary>Totals for one folder, kept for the preview pane.</summary>
public sealed class DirStats
{
    public string Path { get; set; } = string.Empty;

    public int DirectFileCount { get; set; }

    public long DirectSize { get; set; }

    public long TotalSize { get; set; }

    public long TotalFileCount { get; set; }

    public int SubfolderCount { get; set; }
}

/// <summary>The result of walking an <see cref="MftIndex"/>.</summary>
public sealed class AggregationResult
{
    public AggregationResult(
        IReadOnlyList<FolderResult> results,
        Dictionary<uint, DirStats> stats,
        Dictionary<string, uint> pathToRecord,
        IReadOnlyList<string> warnings,
        bool rootFound,
        long orphanDirectories)
    {
        Results = results;
        Stats = stats;
        PathToRecord = pathToRecord;
        Warnings = warnings;
        RootFound = rootFound;
        OrphanDirectoryCount = orphanDirectories;
    }

    public IReadOnlyList<FolderResult> Results { get; }

    public IReadOnlyDictionary<uint, DirStats> Stats { get; }

    public IReadOnlyDictionary<string, uint> PathToRecord { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool RootFound { get; }

    public long OrphanDirectoryCount { get; }

    /// <summary>Looks up the MFT record number for a folder path, tolerating a trailing separator.</summary>
    public bool TryGetRecordNumber(string path, out uint recordNumber)
    {
        recordNumber = 0;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string key = path.Trim();
        if (PathToRecord.TryGetValue(key, out recordNumber))
        {
            return true;
        }

        if (key.Length > 3 && PathToRecord.TryGetValue(key.TrimEnd('\\'), out recordNumber))
        {
            return true;
        }

        recordNumber = 0;
        return false;
    }
}
