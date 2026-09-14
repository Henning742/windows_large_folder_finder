using DataFinder.Core.Preview;
using DataFinder.Core.Util;

namespace DataFinder.Core.Models;

/// <summary>One item shown in the preview pane.</summary>
public sealed class FileEntry
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public bool IsDirectory { get; init; }

    /// <summary>File size, or the total size of a folder.</summary>
    public long SizeBytes { get; init; }

    /// <summary>For folders: how many items sit directly inside.</summary>
    public int DirectChildCount { get; init; }

    public PreviewKind PreviewKind { get; init; } = PreviewKind.None;

    public string TypeText => IsDirectory ? "Folder" : "File";

    public string SizeText => ByteSize.Format(SizeBytes);

    public string ChildCountText => IsDirectory ? DirectChildCount.ToString("N0") : string.Empty;
}
