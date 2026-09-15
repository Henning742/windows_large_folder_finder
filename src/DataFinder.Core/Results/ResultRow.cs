namespace DataFinder.Core.Results;

/// <summary>One folder read back from a result file: the path and the note the user left on it.</summary>
public sealed record ResultRow(string Path, string Comment)
{
    public ResultRow(string path)
        : this(path, string.Empty)
    {
    }
}
