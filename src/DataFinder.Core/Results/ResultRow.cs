namespace DataFinder.Core.Results;

/// <summary>
/// The numbers one folder of a report carried: what the scan found when the report was written. A
/// report the app wrote holds all of them, and they are what the rows are shown from, so importing
/// one does not have to walk every folder again.
/// </summary>
public sealed record ReportedFolderNumbers
{
    /// <summary>The size the rules were tested against when the report was written.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>The size of the folder including everything below it.</summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>How many files sat directly inside the folder.</summary>
    public required int DirectFileCount { get; init; }

    /// <summary>How many files sat below the folder, at any depth.</summary>
    public required long TotalFileCount { get; init; }

    public required int SubfolderCount { get; init; }

    /// <summary>
    /// Whether the folder was there when the report was written, or true when the report does not
    /// say. Importing looks at the folder again rather than taking this for the answer, so the flag
    /// is what the report said rather than what is true now.
    /// </summary>
    public bool Exists { get; init; } = true;
}

/// <summary>
/// One folder read back from a result file: the path, the note the user left on it, and the numbers
/// the file carried for it - which a plain list of paths has none of.
/// </summary>
public sealed record ResultRow(string Path, string Comment)
{
    public ResultRow(string path)
        : this(path, string.Empty)
    {
    }

    /// <summary>
    /// The size and file counts the report holds for this folder, or null when it holds none - a
    /// list of paths with no numbers, or a row whose numbers are not all there. Null means the
    /// folder has to be measured from the file system to fill the columns in.
    /// </summary>
    public ReportedFolderNumbers? Numbers { get; init; }
}
