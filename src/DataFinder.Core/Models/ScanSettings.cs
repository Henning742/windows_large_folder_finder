using DataFinder.Core.Util;

namespace DataFinder.Core.Models;

/// <summary>The two rules that decide whether a folder shows up in the result list.</summary>
public sealed class ScanSettings
{
    public const long DefaultMinSizeBytes = 200L * 1024 * 1024;
    public const int DefaultMinDirectFileCount = 200;

    /// <summary>A folder must be larger than this to be reported.</summary>
    public long MinSizeBytes { get; set; } = DefaultMinSizeBytes;

    /// <summary>A folder must contain more than this many files directly inside it.</summary>
    public int MinDirectFileCount { get; set; } = DefaultMinDirectFileCount;

    /// <summary>
    /// When true (default) the size rule uses the size of the folder and everything below it.
    /// When false only the files sitting directly in the folder are counted.
    /// </summary>
    public bool SizeIncludesSubfolders { get; set; } = true;

    public ScanSettings Clone() => new()
    {
        MinSizeBytes = MinSizeBytes,
        MinDirectFileCount = MinDirectFileCount,
        SizeIncludesSubfolders = SizeIncludesSubfolders,
    };

    public string Describe()
    {
        string sizeScope = SizeIncludesSubfolders ? "whole folder" : "direct files only";
        return $"size > {ByteSize.Format(MinSizeBytes)} ({sizeScope}) and more than {MinDirectFileCount:N0} files directly inside";
    }

    public static bool TryParse(
        string? minSizeText,
        string? minFileCountText,
        bool sizeIncludesSubfolders,
        out ScanSettings settings,
        out string? error)
    {
        settings = new ScanSettings();
        error = null;

        if (!ByteSize.TryParseUserInput(minSizeText, out long minBytes, out error))
        {
            return false;
        }

        if (!int.TryParse((minFileCountText ?? string.Empty).Trim(), out int minFiles) || minFiles < 0)
        {
            error = "The file count must be a whole number of zero or more.";
            return false;
        }

        settings = new ScanSettings
        {
            MinSizeBytes = minBytes,
            MinDirectFileCount = minFiles,
            SizeIncludesSubfolders = sizeIncludesSubfolders,
        };
        return true;
    }
}

